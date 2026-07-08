using System.Diagnostics;
using System.Runtime.InteropServices;

namespace QuickTickLib;

internal static class QuickTickHelper
{
    // Converts Stopwatch ticks to TimeSpan/Win32 ticks (100 ns units).
    private static readonly double StopwatchTicksToTimeSpanTicksFactor = TimeSpan.TicksPerSecond / (double)Stopwatch.Frequency;
    internal static readonly long StopwatchTicksPerMillisecond = Stopwatch.Frequency / 1000;
    internal const uint NtCreateWaitCompletionPacketAccessRights = (uint)Win32Interop.TimerAccessMask.TIMER_MODIFY_STATE | (uint)Win32Interop.TimerAccessMask.TIMER_QUERY_STATE;
    internal const uint CreateWaitableTimerExWAccessRights = (uint)Win32Interop.TimerAccessMask.TIMER_MODIFY_STATE | (uint)Win32Interop.TimerAccessMask.SYNCHRONIZE;
    private const int MinRequiredWindowsBuildNumber = 17134; // This is the build number of Windows 10 Version 1803
    private static readonly Version? WindowsVersion = GetWindowsVersion();
    
    // Current Windows 10 versions / Server 2019+ will support QuickTick. The minimal supported Windows version
    // is Version 1803 of Windows 10 as in that version the HighPrecision flag was added to CreateWaitableTimerExW which this library relies on
    internal static bool PlatformSupportsQuickTick()
    {
        return WindowsVersion is { Major: >= 10, Build: >= MinRequiredWindowsBuildNumber };
    }

    // True for Windows versions too old to run QuickTick. We can't fall back to the Fallback implementation for these since it relies on
    // System.Timers.Timer which is limited by windows timing, so it can not behave similar to the actual code.
    private static bool IsUnsupportedWindowsVersion()
    {
        return WindowsVersion is not null && !PlatformSupportsQuickTick();
    }

    internal static void ThrowIfUnsupportedWindowsVersion()
    {
        if (IsUnsupportedWindowsVersion())
        {
            throw new PlatformNotSupportedException("QuickTickLib can not run under Windows version below version 10.");
        }
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
    
    internal static long StopwatchTicksToTimeSpanTicks(long stopwatchTicks)
    {
        return (long)(stopwatchTicks * StopwatchTicksToTimeSpanTicksFactor);
    }
}
