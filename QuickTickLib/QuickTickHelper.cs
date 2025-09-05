using System.Runtime.InteropServices;

namespace QuickTickLib;

internal static class QuickTickHelper
{
    internal const uint NtCreateWaitCompletionPacketAccessRights = (uint)Win32Interop.TimerAccessMask.TIMER_MODIFY_STATE | (uint)Win32Interop.TimerAccessMask.TIMER_QUERY_STATE;
    internal const uint CreateWaitableTimerExWAccessRights = (uint)Win32Interop.TimerAccessMask.TIMER_MODIFY_STATE | (uint)Win32Interop.TimerAccessMask.SYNCHRONIZE;
    private const int MinRequiredWindowsBuildNumber = 17134; // This is the build number of Windows 10 Version 1803
    private static readonly Version? windowsVersion = GetWindowsVersion();

    internal static bool PlatformSupportsQuickTick()
    {
        if (windowsVersion is null)
        {
            return false;
        }

        // Current Windows 10 versions / Server 2019+ will support this functions. The minimal supported windows version
        // is Version 1803 of Windows 10 as in that version the Highprecision flag was added to CreateWaitableTimerExW which this library relies on
        if (windowsVersion.Major >= 10 && windowsVersion.Build >= MinRequiredWindowsBuildNumber)
        {
            return true;
        }
        // Throw here as we could use the Fallback but since the Fallback relies on System.Timers.Timer which
        // is limited by windows timing. It is not possible to have a fallback for older windows Versions that behaves similar to the actual code.
        throw new PlatformNotSupportedException("QuickTickLib can not run under Windows version below version 10.");
    }

    private static Version? GetWindowsVersion()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return null;
        }
#if NET
        return Environment.OSVersion.Version;
#else
        // Done like this to avoid needing a manifest file specifying Windows 10 as supported OS.
        // Environment.OSVersion.Version would return MajorVersion 6 in .NET Framework without manifest file.
        return Win32Interop.GetRealWindowsVersion();
#endif
    }
}
