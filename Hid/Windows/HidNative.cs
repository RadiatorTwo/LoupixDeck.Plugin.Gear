using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace LoupixDeck.Plugin.Gear.Hid.Windows;

/// <summary>P/Invoke surface for HID enumeration and I/O (explicit W entry points).</summary>
internal static unsafe partial class HidNative
{
    public const uint GenericRead = 0x80000000;
    public const uint GenericWrite = 0x40000000;
    public const uint ShareReadWrite = 0x3;
    public const uint OpenExisting = 3;
    public const uint FileFlagOverlapped = 0x40000000;
    public const int ErrorAccessDenied = 5;
    private const int CrSuccess = 0;
    private const int CrBufferSmall = 0x1A;

    public static readonly Guid HidInterfaceGuid = new("4d1e55b2-f16f-11cf-88cb-001111000030");

    [StructLayout(LayoutKind.Sequential)]
    public struct HidAttributes
    {
        public uint Size;
        public ushort VendorId;
        public ushort ProductId;
        public ushort VersionNumber;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HidpCaps
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;
        public fixed ushort Reserved[17];
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

    [LibraryImport("cfgmgr32.dll", EntryPoint = "CM_Get_Device_Interface_List_SizeW")]
    private static partial int CM_Get_Device_Interface_List_SizeW(out uint length, in Guid interfaceClass, nint deviceId, uint flags);

    [LibraryImport("cfgmgr32.dll", EntryPoint = "CM_Get_Device_Interface_ListW")]
    private static partial int CM_Get_Device_Interface_ListW(in Guid interfaceClass, nint deviceId, char* buffer, uint length, uint flags);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    public static partial SafeFileHandle CreateFileW(string fileName, uint access, uint share, nint security, uint disposition, uint flags, nint template);

    [LibraryImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool HidD_GetAttributes(SafeFileHandle handle, ref HidAttributes attributes);

    [LibraryImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool HidD_GetPreparsedData(SafeFileHandle handle, out nint preparsedData);

    [LibraryImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool HidD_FreePreparsedData(nint preparsedData);

    [LibraryImport("hid.dll")]
    public static partial int HidP_GetCaps(nint preparsedData, ref HidpCaps caps);

    [LibraryImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool HidD_SetOutputReport(SafeFileHandle handle, byte* report, uint length);

    /// <summary>Device paths of all present HID interfaces.</summary>
    public static List<string> GetHidDevicePaths()
    {
        Guid guid = HidInterfaceGuid;
        // The list can grow between the size query and the read; retry a few times.
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (CM_Get_Device_Interface_List_SizeW(out uint length, in guid, 0, 0) != CrSuccess)
            {
                return [];
            }

            char[] buffer = new char[length];
            int result;
            fixed (char* p = buffer)
            {
                result = CM_Get_Device_Interface_ListW(in guid, 0, p, length, 0);
            }

            if (result == CrBufferSmall)
            {
                continue;
            }

            if (result != CrSuccess)
            {
                return [];
            }

            // Multi-string: entries separated by NUL, ended by an empty entry.
            List<string> paths = [];
            int start = 0;
            for (int i = 0; i < buffer.Length; i++)
            {
                if (buffer[i] != '\0')
                {
                    continue;
                }

                if (i == start)
                {
                    break;
                }

                paths.Add(new string(buffer, start, i - start));
                start = i + 1;
            }

            return paths;
        }

        return [];
    }
}
