using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace Mk8.Drava.Configuration;

[SupportedOSPlatform("windows")]
internal static partial class WindowsPrivateFile
{
    [LibraryImport("kernel32.dll", EntryPoint = "CreateDirectoryW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateDirectoryNative(string path, nint security);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial SafeFileHandle OpenNative(string path, uint access, uint share, nint security, uint disposition, uint flags, nint template);

    [LibraryImport("kernel32.dll", EntryPoint = "GetFileType", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint ReadFileType(SafeFileHandle handle);

    [LibraryImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ReadAttributes(SafeFileHandle handle, int informationClass, out AttributeInformation information, uint length);

    [LibraryImport("advapi32.dll", EntryPoint = "GetSecurityInfo")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint ReadSecurity(SafeFileHandle handle, int objectType, uint information, out nint owner, out nint group, out nint dacl, out nint sacl, out nint descriptor);

    [LibraryImport("advapi32.dll", EntryPoint = "GetSecurityDescriptorLength")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint DescriptorLength(nint descriptor);

    [StructLayout(LayoutKind.Sequential)]
    private struct AttributeInformation
    {
        public uint Attributes;
        public uint ReparseTag;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public int Length;
        public nint Descriptor;
        public int InheritHandle;
    }
}
