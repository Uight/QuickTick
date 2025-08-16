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
        // Throw here as we could use the Fallback but since the Fallback relies on System.Timers.Timer which
        // is limited by windows timing. It is not possible to have a fallback for older windows Versions that behaves similar to the actual code.
        throw new PlatformNotSupportedException("QuickTickLib can not run under Windows version below version 10.");
    }
}
