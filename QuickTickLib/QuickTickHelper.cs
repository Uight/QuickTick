using System.Runtime.InteropServices;

namespace QuickTickLib;

internal static class QuickTickHelper
{
    internal const uint NtCreateWaitCompletionPacketAccessRights = (uint)Win32Interop.TimerAccessMask.TIMER_MODIFY_STATE | (uint)Win32Interop.TimerAccessMask.TIMER_QUERY_STATE;
    internal const uint CreateWaitableTimerExWAccessRights = (uint)Win32Interop.TimerAccessMask.TIMER_MODIFY_STATE | (uint)Win32Interop.TimerAccessMask.SYNCHRONIZE;

    internal static bool PlatformSupportsQuickTick()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return false;
        }

        Version version;

#if NET
        version = Environment.OSVersion.Version;
#else
        // Done like this to avoid needing a manifest file specifying Windows 10 as supported OS.
        // Environment.OSVersion.Version would return MajorVersion 6 in .NET Framework without manifest file.
        version = Win32Interop.GetRealWindowsVersion();
#endif

        // Windows 10 / Server 2016+ (NT 10.0) should support all needed functions
        if (version.Major >= 10)
        {
            return true;
        }
        return false;
    }
}
