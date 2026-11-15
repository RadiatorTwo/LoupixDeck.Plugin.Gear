using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;

namespace LoupixDeck.Plugin.Gear.Hid.Windows;

/// <summary>HID transport on top of hid.dll / cfgmgr32.dll.</summary>
[SupportedOSPlatform("windows")]
internal sealed partial class WindowsHidTransport : IHidTransport
{
    public IReadOnlyList<HidDeviceInfo> Enumerate()
    {
        List<HidDeviceInfo> devices = [];
        foreach (string path in HidNative.GetHidDevicePaths())
        {
            HidDeviceInfo? info = Probe(path);
            if (info is not null)
            {
                devices.Add(info);
            }
        }

        return devices;
    }

    public IHidConnection Open(HidDeviceInfo device)
    {
        SafeFileHandle handle = HidNative.CreateFileW(
            device.Path,
            HidNative.GenericRead | HidNative.GenericWrite,
            HidNative.ShareReadWrite,
            0,
            HidNative.OpenExisting,
            HidNative.FileFlagOverlapped,
            0);

        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            handle.Dispose();
            if (error == HidNative.ErrorAccessDenied)
            {
                throw new HidAccessException($"Access to HID device '{device.Path}' was denied.");
            }

            throw new IOException($"Could not open HID device '{device.Path}' (error {error}).");
        }

        return new WindowsHidConnection(handle, device.InputLength);
    }

    /// <summary>Reads attributes and capabilities through a zero-access handle; null if the device is unusable.</summary>
    private static HidDeviceInfo? Probe(string path)
    {
        using SafeFileHandle handle = HidNative.CreateFileW(path, 0, HidNative.ShareReadWrite, 0, HidNative.OpenExisting, 0, 0);
        if (handle.IsInvalid)
        {
            return null;
        }

        HidNative.HidAttributes attributes = new() { Size = (uint)Marshal.SizeOf<HidNative.HidAttributes>() };
        if (!HidNative.HidD_GetAttributes(handle, ref attributes))
        {
            return null;
        }

        if (!HidNative.HidD_GetPreparsedData(handle, out nint preparsed))
        {
            return null;
        }

        HidNative.HidpCaps caps = default;
        try
        {
            if (HidNative.HidP_GetCaps(preparsed, ref caps) < 0)
            {
                return null;
            }
        }
        finally
        {
            HidNative.HidD_FreePreparsedData(preparsed);
        }

        return new HidDeviceInfo(
            path,
            attributes.VendorId,
            attributes.ProductId,
            ParseInterfaceNumber(path),
            caps.UsagePage,
            caps.Usage,
            caps.InputReportByteLength,
            caps.OutputReportByteLength,
            // HidD_Get*String can block for seconds on some devices and the scan runs every few seconds.
            null,
            null,
            ParseContainerKey(path));
    }

    /// <summary>The <c>MI_xx</c> interface number of a device path, or -1 for single-interface devices.</summary>
    internal static int ParseInterfaceNumber(string path)
    {
        Match match = InterfaceRegex().Match(path);
        return match.Success ? Convert.ToInt32(match.Groups[1].Value, 16) : -1;
    }

    /// <summary>
    /// The device path without the interface class suffix, the <c>&amp;COLnn</c> part and the
    /// per-collection tail of the instance id, so all collections of one interface share a key.
    /// </summary>
    internal static string ParseContainerKey(string path)
    {
        string[] parts = path.TrimStart('\\', '?').Split('#');
        if (parts.Length < 3)
        {
            return path.ToLowerInvariant();
        }

        string hardwareId = CollectionRegex().Replace(parts[1], string.Empty);
        string instance = parts[2];
        int lastAmp = instance.LastIndexOf('&');
        if (lastAmp > 0)
        {
            instance = instance[..lastAmp];
        }

        return $"{parts[0]}#{hardwareId}#{instance}".ToLowerInvariant();
    }

    [GeneratedRegex(@"&mi_([0-9a-f]{2})", RegexOptions.IgnoreCase)]
    private static partial Regex InterfaceRegex();

    [GeneratedRegex(@"&col[0-9a-f]{2}", RegexOptions.IgnoreCase)]
    private static partial Regex CollectionRegex();
}
