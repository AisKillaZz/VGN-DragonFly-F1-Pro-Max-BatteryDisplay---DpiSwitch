namespace VgnTrayBattery;

internal static class VgnHidProtocol
{
    internal sealed record Telemetry(int? BatteryPercent, int? Dpi, bool? MotionSyncEnabled);

    private static readonly object ProbeGate = new();
    private static readonly HashSet<byte> WorkingReportIds = [];
    private static bool ProbeComplete;
    private static string? LastConfigurationFeature;

    public static Telemetry TryReadKnownTelemetry(NativeHidDevice device, Action<string> log)
    {
        // The ordinary mouse reports are standard HID, but VGN's battery, DPI,
        // and Motion Sync values live in vendor-defined feature reports. F1
        // revisions use different report IDs and layouts. Until a report is
        // captured from this exact 2.4G receiver, do not guess a command: an
        // incorrect feature report can overwrite onboard settings.
        log($"Detected HID {device.VendorId:X4}:{device.ProductId:X4} " +
            $"usage {device.UsagePage:X4}:{device.Usage:X4}, " +
            $"input/output/feature lengths {device.InputReportLength}/{device.OutputReportLength}/{device.FeatureReportLength}.");
        ObserveConfigurationFeature(log);

        if (device.UsagePage == 0xFF02 && device.Usage == 0x0002)
        {
            var request = new byte[17];
            request[0] = 0x08;
            request[1] = 0x04;
            request[16] = 0x49;
            if (NativeHid.TryExchange(device, request, 17, 100, out var response) && response.Length >= 7 && response[0] == 0x08 && response[1] == 0x04)
            {
                var percent = response[6] <= 100 ? response[6] : (byte?)null;
                log($"Battery response: {Convert.ToHexString(response)}; percent={percent?.ToString() ?? "unknown"}.");
                return new Telemetry(percent, null, null);
            }
            log("Battery exchange did not return a valid 0x08/0x04 response.");
        }
        // Do not brute-force feature IDs: a number of VGN collections accept
        // the request but never answer, and probing all 256 IDs is both slow
        // and unnecessary for the confirmed Nordic52 protocol.
        return new Telemetry(null, null, null);
    }

    private static void ObserveConfigurationFeature(Action<string> log)
    {
        var device = NativeHid.Enumerate()
            .FirstOrDefault(d => d.UsagePage == 0xFF04 && d.Usage == 0x0002);
        if (device is null || !NativeHid.TryGetFeature(device, 0x06, out var feature)) return;
        var hex = Convert.ToHexString(feature);
        if (!string.Equals(hex, LastConfigurationFeature, StringComparison.Ordinal))
        {
            LastConfigurationFeature = hex;
            log($"Configuration feature 06 changed: {hex}");
        }
    }

    private static void ProbeFeatureReports(NativeHidDevice device, Action<string> log)
    {
        lock (ProbeGate)
        {
            if (!ProbeComplete)
            {
                // The probe is read-only.  Report IDs are cheap to test and
                // vendor collections generally expose only a handful of them.
                for (var id = 0; id <= byte.MaxValue; id++)
                {
                    if (!NativeHid.TryGetFeature(device, (byte)id, out var report)) continue;
                    WorkingReportIds.Add((byte)id);
                    log($"Feature report id {id:X2}: {Convert.ToHexString(report)}");
                }
                ProbeComplete = true;
                log($"Feature probe complete; working IDs: " +
                    (WorkingReportIds.Count == 0
                        ? "none"
                        : string.Join(",", WorkingReportIds.OrderBy(id => id).Select(id => id.ToString("X2")))));
                return;
            }

            foreach (var id in WorkingReportIds.OrderBy(id => id))
            {
                if (NativeHid.TryGetFeature(device, id, out var report))
                    log($"Feature report id {id:X2}: {Convert.ToHexString(report)}");
            }
        }
    }

    public static bool TrySetDpi(NativeHidDevice device, int dpi, Action<string> log)
    {
        var dpiIndex = dpi switch
        {
            400 => (byte)0,
            800 => (byte)1,
            1600 => (byte)2,
            _ => (byte)0xFF
        };
        if (dpiIndex == 0xFF)
        {
            log($"DPI {dpi} is not one of the captured presets (400/800/1600).");
            return false;
        }

        var marker = (byte)(0x55 - dpiIndex);
        var report = BuildOutputReport(
            0x07, 0x00, 0x00, 0x04, 0x02, dpiIndex, marker,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xEB);
        var sent = NativeHid.TrySendOutputReport(device, report);
        log(sent
            ? $"Captured DPI command sent: {dpi} DPI ({Convert.ToHexString(report)})."
            : $"Failed to send captured {dpi} DPI command ({Convert.ToHexString(report)}).");
        return sent;
    }

    public static bool TrySetMotionSync(NativeHidDevice device, bool enabled, Action<string> log)
    {
        var state = enabled ? (byte)0x01 : (byte)0x00;
        var marker = enabled ? (byte)0x54 : (byte)0x55;
        var report = BuildOutputReport(
            0x07, 0x00, 0x00, 0xAB, 0x02, state, marker,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x44);
        var sent = NativeHid.TrySendOutputReport(device, report);
        log(sent
            ? $"Captured Motion Sync {(enabled ? "on" : "off")} command sent ({Convert.ToHexString(report)})."
            : $"Failed to send captured Motion Sync {(enabled ? "on" : "off")} command ({Convert.ToHexString(report)}).");
        return sent;
    }

    private static byte[] BuildOutputReport(params byte[] payload)
    {
        var report = new byte[payload.Length + 1];
        report[0] = 0x08;
        payload.CopyTo(report, 1);
        return report;
    }
}
