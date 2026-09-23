using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

namespace VgnTrayBattery;

/// <summary>
/// Small, dependency-free Windows HID enumerator. Keeping this in the app
/// avoids downloading a third-party package just to find the receiver.
/// </summary>
internal static class NativeHid
{
    private const uint DigcfPresent = 0x00000002;
    private const uint DigcfDeviceInterface = 0x00000010;
    private const uint ErrorNoMoreItems = 259;
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const int HidpStatusSuccess = 0x00110000;

    public static IReadOnlyList<NativeHidDevice> Enumerate(Action<string>? log = null)
    {
        HidD_GetHidGuid(out var hidGuid);
        var infoSet = SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero,
            DigcfPresent | DigcfDeviceInterface);
        if (infoSet == InvalidHandleValue)
        {
            log?.Invoke($"SetupDiGetClassDevs failed: {Marshal.GetLastWin32Error()}");
            return [];
        }

        var result = new List<NativeHidDevice>();
        try
        {
            for (uint index = 0; ; index++)
            {
                var interfaceData = new SpDeviceInterfaceData
                {
                    CbSize = Marshal.SizeOf<SpDeviceInterfaceData>()
                };
                if (!SetupDiEnumDeviceInterfaces(infoSet, IntPtr.Zero, ref hidGuid, index, ref interfaceData))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == ErrorNoMoreItems) break;
                    // Any other error means the device-info set cannot
                    // advance. Continuing would spin forever and prevent the
                    // tray from ever reaching its first refresh.
                    log?.Invoke($"SetupDiEnumDeviceInterfaces({index}) failed: {error}");
                    break;
                }

                if (!SetupDiGetDeviceInterfaceDetail(infoSet, ref interfaceData, IntPtr.Zero, 0,
                        out var requiredSize, IntPtr.Zero) && requiredSize <= 0)
                {
                    log?.Invoke($"SetupDiGetDeviceInterfaceDetail size failed: {Marshal.GetLastWin32Error()}, required={requiredSize}");
                    continue;
                }

                var detail = Marshal.AllocHGlobal(requiredSize);
                try
                {
                    // cbSize is 8 on 64-bit and 5 on 32-bit for the Unicode
                    // SP_DEVICE_INTERFACE_DETAIL_DATA structure.
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 5);
                    if (!SetupDiGetDeviceInterfaceDetail(infoSet, ref interfaceData, detail,
                            requiredSize, out _, IntPtr.Zero))
                    {
                        continue;
                    }

                    // For the Unicode SetupAPI structure the path starts
                    // immediately after the 4-byte cbSize field.  The
                    // structure itself is packed for this API even in a
                    // 64-bit process.
                    var path = Marshal.PtrToStringUni(IntPtr.Add(detail, 4));
                    if (string.IsNullOrWhiteSpace(path))
                    {
                        log?.Invoke("HID interface path was empty.");
                        continue;
                    }

                    using var handle = Open(path, writeAccess: false);
                    if (handle is null || handle.IsInvalid)
                    {
                        continue;
                    }

                    var attributes = new HidAttributes { Size = Marshal.SizeOf<HidAttributes>() };
                    if (!HidD_GetAttributes(handle, ref attributes))
                    {
                        continue;
                    }

                    if (!HidD_GetPreparsedData(handle, out var preparsedData))
                    {
                        continue;
                    }
                    try
                    {
                        var caps = new HidpCaps { Reserved = new ushort[17] };
                        var capsStatus = HidP_GetCaps(preparsedData, ref caps);
                        if (capsStatus != HidpStatusSuccess)
                        {
                            continue;
                        }
                        result.Add(new NativeHidDevice(
                            path,
                            attributes.VendorID,
                            attributes.ProductID,
                            attributes.VersionNumber,
                            caps.UsagePage,
                            caps.Usage,
                            caps.InputReportByteLength,
                            caps.OutputReportByteLength,
                            caps.FeatureReportByteLength));
                    }
                    finally
                    {
                        HidD_FreePreparsedData(preparsedData);
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(detail);
                }
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(infoSet);
        }

        return result;
    }

    internal static SafeFileHandle? Open(string path, bool writeAccess)
    {
        var access = writeAccess ? GenericRead | GenericWrite : 0u;
        var handle = CreateFile(path, access, FileShareRead | FileShareWrite, IntPtr.Zero,
            OpenExisting, 0, IntPtr.Zero);
        return handle.IsInvalid ? null : handle;
    }

    internal static bool GetFeature(NativeHidDevice device, byte[] report)
    {
        using var handle = Open(device.Path, writeAccess: true);
        return handle is not null && !handle.IsInvalid && HidD_GetFeature(handle, report, report.Length);
    }

    internal static bool SetFeature(NativeHidDevice device, byte[] report)
    {
        using var handle = Open(device.Path, writeAccess: true);
        return handle is not null && !handle.IsInvalid && HidD_SetFeature(handle, report, report.Length);
    }

    internal static bool TrySendOutputReport(NativeHidDevice device, byte[] report)
    {
        if (device.OutputReportLength != report.Length) return false;
        using var handle = Open(device.Path, writeAccess: true);
        if (handle is null || handle.IsInvalid) return false;
        return WriteFile(handle, report, report.Length, out var written, IntPtr.Zero)
            && written == report.Length;
    }

    internal static bool TryExchange(NativeHidDevice device, byte[] request, int responseLength, int timeoutMs, out byte[] response)
    {
        response = [];
        using var handle = Open(device.Path, writeAccess: true);
        if (handle is null || handle.IsInvalid) return false;
        if (!WriteFile(handle, request, request.Length, out _, IntPtr.Zero)) return false;
        Thread.Sleep(Math.Clamp(timeoutMs, 1, 500));
        var buffer = new byte[Math.Max(1, responseLength)];
        // A vendor collection may accept the request but not return a report
        // on this interface. ReadFile is synchronous and can otherwise block
        // the tray refresh forever, so perform it on a worker and enforce a
        // hard timeout; disposing the handle cancels the pending read.
        var readTask = Task.Run(() => ReadFile(handle, buffer, buffer.Length, out var read, IntPtr.Zero)
            ? read
            : 0);
        if (!readTask.Wait(Math.Clamp(timeoutMs, 10, 500)) || readTask.Result <= 0) return false;
        response = buffer.AsSpan(0, readTask.Result).ToArray();
        return true;
    }

    /// <summary>
    /// Reads a single feature report using the report ID in byte zero.  This
    /// is intentionally read-only and is used while discovering vendor HID
    /// layouts for a particular receiver revision.
    /// </summary>
    internal static bool TryGetFeature(NativeHidDevice device, byte reportId, out byte[] report)
    {
        var length = Math.Max(device.FeatureReportLength, 1);
        report = new byte[length];
        report[0] = reportId;
        using var handle = Open(device.Path, writeAccess: true);
        if (handle is null || handle.IsInvalid || !HidD_GetFeature(handle, report, report.Length))
        {
            report = [];
            return false;
        }
        return true;
    }

    private static readonly IntPtr InvalidHandleValue = new(-1);

    [DllImport("hid.dll")]
    private static extern void HidD_GetHidGuid(out Guid hidGuid);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetAttributes(SafeFileHandle hidDeviceObject, ref HidAttributes attributes);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetPreparsedData(SafeFileHandle hidDeviceObject, out IntPtr preparsedData);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetFeature(SafeFileHandle hidDeviceObject, [In, Out] byte[] report, int reportLength);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_SetFeature(SafeFileHandle hidDeviceObject, [In] byte[] report, int reportLength);

    [DllImport("hid.dll")]
    private static extern int HidP_GetCaps(IntPtr preparsedData, ref HidpCaps capabilities);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator,
        IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet, IntPtr deviceInfoData,
        ref Guid interfaceClassGuid, uint memberIndex, ref SpDeviceInterfaceData deviceInterfaceData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr deviceInfoSet,
        ref SpDeviceInterfaceData deviceInterfaceData, IntPtr deviceInterfaceDetailData,
        int deviceInterfaceDetailDataSize, out int requiredSize, IntPtr deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode,
        IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteFile(SafeFileHandle hFile, byte[] buffer, int numberOfBytesToWrite,
        out int numberOfBytesWritten, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadFile(SafeFileHandle hFile, byte[] buffer, int numberOfBytesToRead,
        out int numberOfBytesRead, IntPtr overlapped);

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDeviceInterfaceData
    {
        public int CbSize;
        public Guid InterfaceClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HidAttributes
    {
        public int Size;
        public ushort VendorID;
        public ushort ProductID;
        public ushort VersionNumber;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HidpCaps
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
        public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps;
        public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps;
        public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps;
        public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;
    }
}

internal sealed record NativeHidDevice(
    string Path,
    int VendorId,
    int ProductId,
    int Version,
    int UsagePage,
    int Usage,
    int InputReportLength,
    int OutputReportLength,
    int FeatureReportLength);
