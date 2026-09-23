using System.Diagnostics;

namespace VgnTrayBattery;

internal sealed class VgnDeviceMonitor : IDisposable
{
    // IDs observed on the current machine and on other F1-family receiver
    // revisions. A receiver exposes several HID collections under one ID.
    private static readonly (int VendorId, int ProductId)[] KnownIds =
    [
        (0x3554, 0xF503),
        (0x391D, 0x1005),
        (0x391D, 0x1A05)
    ];

    private readonly object _gate = new();
    private readonly string _logDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VgnTrayBattery", "logs");
    private VgnDeviceState _currentState = VgnDeviceState.Disconnected;
    private int _refreshing;
    private bool _disposed;

    public event EventHandler<VgnDeviceState>? StateChanged;
    public VgnDeviceState CurrentState { get { lock (_gate) return _currentState; } }

    public async Task RefreshAsync()
    {
        if (_disposed || Interlocked.Exchange(ref _refreshing, 1) == 1) return;
        try
        {
            var state = await Task.Run(FindAndReadState);
            lock (_gate) _currentState = state;
            StateChanged?.Invoke(this, state);
        }
        finally
        {
            Volatile.Write(ref _refreshing, 0);
        }
    }

    public async Task<bool> SetDpiAsync(int dpi)
    {
        if (_disposed) return false;
        return await Task.Run(() =>
        {
            var device = FindConfigurationDevice();
            return device is not null && VgnHidProtocol.TrySetDpi(device, dpi, Log);
        });
    }

    public async Task<bool> SetMotionSyncAsync(bool enabled)
    {
        if (_disposed) return false;
        return await Task.Run(() =>
        {
            var device = FindConfigurationDevice();
            return device is not null && VgnHidProtocol.TrySetMotionSync(device, enabled, Log);
        });
    }

    public void OpenLogDirectory()
    {
        Directory.CreateDirectory(_logDirectory);
        Process.Start(new ProcessStartInfo("explorer.exe", _logDirectory) { UseShellExecute = true });
    }

    private VgnDeviceState FindAndReadState()
    {
        try
        {
            Log("Starting HID refresh.");
            var device = FindConfigurationDevice();
            if (device is null) return VgnDeviceState.Disconnected;

            var parsed = VgnHidProtocol.TryReadKnownTelemetry(device, Log);
            var label = $"VGN F1 2.4G ({device.VendorId:X4}:{device.ProductId:X4})";
            var lastKnownBattery = CurrentState.BatteryPercent;
            return new VgnDeviceState(
                IsConnected: true,
                DeviceName: label,
                BatteryPercent: parsed.BatteryPercent ?? lastKnownBattery,
                Dpi: parsed.Dpi,
                MotionSyncEnabled: parsed.MotionSyncEnabled,
                ProtocolReady: false);
        }
        catch (Exception ex)
        {
            Log(ex.ToString());
            return VgnDeviceState.Disconnected;
        }
    }

    private NativeHidDevice? FindConfigurationDevice()
    {
        var allDevices = NativeHid.Enumerate();
        var devices = allDevices
            .Where(d => KnownIds.Contains((d.VendorId, d.ProductId)))
            .ToList();

        if (devices.Count == 0)
        {
            var summary = string.Join(", ", allDevices
                .GroupBy(d => (d.VendorId, d.ProductId))
                .Select(g => $"{g.Key.VendorId:X4}:{g.Key.ProductId:X4} x{g.Count()}"));
            Log($"No known VGN HID collection found. Enumerated {allDevices.Count}: {summary}");
        }
        foreach (var d in devices)
            Log($"HID candidate {d.VendorId:X4}:{d.ProductId:X4} usage {d.UsagePage:X4}:{d.Usage:X4} lengths {d.InputReportLength}/{d.OutputReportLength}/{d.FeatureReportLength}");

        // MI_01 on the receiver is vendor-defined (usage page FFxx). Prefer it
        // over the ordinary keyboard/mouse collections.
        // Prefer the Nordic52 battery collection documented for F1, then
        // fall back to the receiver's vendor collection for diagnostics.
        return devices
            .Where(d => d.UsagePage >= 0xFF00)
            .OrderByDescending(d => d.UsagePage == 0xFF02 && d.Usage == 0x0002)
            .ThenByDescending(d => d.OutputReportLength >= 17 || d.InputReportLength >= 17)
            .ThenByDescending(d => d.FeatureReportLength)
            .FirstOrDefault()
            ?? devices.FirstOrDefault();
    }

    private void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(_logDirectory);
            File.AppendAllText(Path.Combine(_logDirectory, "vgn-tray.log"),
                $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never terminate a tray process.
        }
    }

    public void Dispose() => _disposed = true;
}

internal sealed record VgnDeviceState(
    bool IsConnected,
    string DeviceName,
    int? BatteryPercent,
    int? Dpi,
    bool? MotionSyncEnabled,
    bool ProtocolReady)
{
    public static VgnDeviceState Disconnected { get; } =
        new(false, "未连接", null, null, null, false);
}
