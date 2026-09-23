using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;

namespace VgnTrayBattery;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private static readonly int[] DpiPresets = [400, 800, 1600];
    private const string StartupValueName = "VgnTrayBattery";
    private const string StartupRunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private readonly BatteryMenuControl _batteryControl;
    private readonly ToolStripMenuItem _deviceItem;
    private readonly ToolStripMenuItem _dpiMenu;
    private readonly ToolStripMenuItem _motionSyncItem;
    private readonly ToolStripMenuItem _startupItem;
    private readonly Dictionary<int, ToolStripMenuItem> _dpiItems = [];
    private readonly VgnDeviceMonitor _monitor;
    private readonly System.Windows.Forms.Timer _timer;
    private Icon? _currentIcon;
    private int? _renderedBatteryPercent;
    private bool _hasRenderedBattery;
    private string? _tooltipText;
    private bool _motionSyncChecked;

    public TrayApplicationContext()
    {
        _monitor = new VgnDeviceMonitor();
        _monitor.StateChanged += (_, state) => UpdateUi(state);

        _batteryControl = new BatteryMenuControl();
        var batteryHost = new ToolStripControlHost(_batteryControl)
        {
            AutoSize = false,
            Size = new Size(238, 53),
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };

        _menu = new ContextMenuStrip
        {
            ShowImageMargin = false,
            ShowCheckMargin = false,
            Renderer = new ToolStripProfessionalRenderer(new Windows10ColorTable()),
            Font = new Font("Segoe UI", 9f),
            Padding = new Padding(0, 4, 0, 4)
        };
        _menu.Items.Add(batteryHost);
        _menu.Items.Add(new ToolStripSeparator());

        _deviceItem = new ToolStripMenuItem("设备：未连接") { Enabled = false };
        _menu.Items.Add(_deviceItem);

        _dpiMenu = new ToolStripMenuItem("DPI");
        foreach (var dpi in DpiPresets)
        {
            var item = new ToolStripMenuItem(dpi.ToString())
            {
                Tag = dpi,
                Enabled = false
            };
            item.Click += DpiItem_Click;
            _dpiItems[dpi] = item;
            _dpiMenu.DropDownItems.Add(item);
        }
        _menu.Items.Add(_dpiMenu);

        _motionSyncItem = new ToolStripMenuItem
        {
            Text = "☐  移动同步",
            Enabled = false,
            ToolTipText = "开启或关闭 Motion Sync"
        };
        _motionSyncItem.Click += MotionSyncItem_Click;
        _menu.Items.Add(_motionSyncItem);

        _menu.Items.Add(new ToolStripSeparator());
        _startupItem = new ToolStripMenuItem
        {
            Text = GetStartupEnabled() ? "☑  开机启动" : "☐  开机启动"
        };
        _startupItem.Click += StartupItem_Click;
        _menu.Items.Add(_startupItem);
        _menu.Items.Add(new ToolStripMenuItem("刷新", null, async (_, _) => await _monitor.RefreshAsync()));
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(new ToolStripMenuItem("退出", null, (_, _) => ExitThread()));

        _currentIcon = BatteryIconRenderer.CreateIcon(null);
        _renderedBatteryPercent = null;
        _hasRenderedBattery = true;
        _tooltipText = BatteryIconRenderer.ToTooltipText(null);
        _notifyIcon = new NotifyIcon
        {
            Icon = _currentIcon,
            Text = _tooltipText,
            Visible = true,
            ContextMenuStrip = _menu
        };
        _notifyIcon.DoubleClick += (_, _) => ShowStatusMessage();
        _menu.Opening += (_, _) =>
        {
            var state = _monitor.CurrentState;
            _batteryControl.BatteryPercent = state.BatteryPercent;
            UpdateStartupItem();
        };

        _timer = new System.Windows.Forms.Timer { Interval = 3000 };
        _timer.Tick += async (_, _) => await _monitor.RefreshAsync();
        _timer.Start();
        _ = _monitor.RefreshAsync();
    }

    private void UpdateUi(VgnDeviceState state)
    {
        if (_menu.IsDisposed) return;
        if (_menu.InvokeRequired)
        {
            _menu.BeginInvoke(new Action(() => UpdateUi(state)));
            return;
        }

        _deviceItem.Text = state.IsConnected ? "设备：VGN F1 2.4G" : "设备：未连接";
        _batteryControl.BatteryPercent = state.BatteryPercent;

        foreach (var pair in _dpiItems)
        {
            pair.Value.Enabled = state.IsConnected;
            pair.Value.Checked = state.Dpi == pair.Key;
        }
        _dpiMenu.Enabled = state.IsConnected;

        if (state.MotionSyncEnabled is bool known)
        {
            _motionSyncChecked = known;
        }
        _motionSyncItem.Enabled = state.IsConnected;
        _motionSyncItem.Text = _motionSyncChecked ? "☑  移动同步" : "☐  移动同步";

        if (!_hasRenderedBattery || _renderedBatteryPercent != state.BatteryPercent)
        {
            ReplaceTrayIcon(BatteryIconRenderer.CreateIcon(state.BatteryPercent));
            _renderedBatteryPercent = state.BatteryPercent;
            _hasRenderedBattery = true;
        }

        var tooltip = BatteryIconRenderer.ToTooltipText(state.BatteryPercent);
        if (!string.Equals(_tooltipText, tooltip, StringComparison.Ordinal))
        {
            _notifyIcon.Text = tooltip;
            _tooltipText = tooltip;
        }
    }

    private async void DpiItem_Click(object? sender, EventArgs e)
    {
        if (sender is not ToolStripMenuItem item || item.Tag is not int dpi) return;
        var ok = await _monitor.SetDpiAsync(dpi);
        if (!ok)
        {
            ShowProtocolPending("DPI");
        }
    }

    private async void MotionSyncItem_Click(object? sender, EventArgs e)
    {
        var desired = !_motionSyncChecked;
        _motionSyncChecked = desired;
        _motionSyncItem.Text = desired ? "☑  移动同步" : "☐  移动同步";
        var ok = await _monitor.SetMotionSyncAsync(desired);
        if (!ok)
        {
            // Keep the menu honest until a confirmed vendor report is
            // available; do not make the checkbox look changed when the
            // receiver did not accept a command.
            _motionSyncChecked = !desired;
            _motionSyncItem.Text = _motionSyncChecked ? "☑  移动同步" : "☐  移动同步";
            ShowProtocolPending("移动同步");
        }
    }

    private async void StartupItem_Click(object? sender, EventArgs e)
    {
        var enabled = !GetStartupEnabled();
        try
        {
            using var runKey = Registry.CurrentUser.OpenSubKey(StartupRunKey, writable: true)
                ?? Registry.CurrentUser.CreateSubKey(StartupRunKey, writable: true);
            if (enabled)
            {
                var executable = Environment.ProcessPath
                    ?? Application.ExecutablePath;
                runKey.SetValue(StartupValueName, $"\"{executable}\"", RegistryValueKind.String);
            }
            else
            {
                runKey.DeleteValue(StartupValueName, throwOnMissingValue: false);
            }
            UpdateStartupItem();
        }
        catch (Exception ex)
        {
            _notifyIcon.ShowBalloonTip(3000, "VGN F1", $"无法更改开机启动：{ex.Message}", ToolTipIcon.Error);
        }
        await Task.CompletedTask;
    }

    private static bool GetStartupEnabled()
    {
        using var runKey = Registry.CurrentUser.OpenSubKey(StartupRunKey, writable: false);
        return runKey?.GetValue(StartupValueName) is string;
    }

    private void UpdateStartupItem()
    {
        if (_startupItem is not null)
            _startupItem.Text = GetStartupEnabled() ? "☑  开机启动" : "☐  开机启动";
    }

    private void ShowProtocolPending(string feature)
    {
        _notifyIcon.ShowBalloonTip(2600, "VGN F1", $"{feature}界面已准备好，但 2.4G 报文协议还需要确认。", ToolTipIcon.Info);
    }

    private void ShowStatusMessage()
    {
        var state = _monitor.CurrentState;
        var text = state.IsConnected
            ? BatteryIconRenderer.ToTooltipText(state.BatteryPercent)
            : "设备：未连接";
        _notifyIcon.ShowBalloonTip(2200, "VGN F1", text, ToolTipIcon.Info);
    }

    private void ReplaceTrayIcon(Icon next)
    {
        var old = _currentIcon;
        _currentIcon = next;
        _notifyIcon.Icon = next;
        old?.Dispose();
    }

    protected override void ExitThreadCore()
    {
        _timer.Stop();
        _monitor.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _currentIcon?.Dispose();
        _menu.Dispose();
        base.ExitThreadCore();
    }
}
