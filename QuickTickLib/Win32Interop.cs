using System.Runtime.InteropServices;

namespace QuickTickLib;

internal partial class Win32Interop
{
    private const string KernelDll = "kernel32.dll";

#if NET
    [LibraryImport(KernelDll, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(
        IntPtr hObject // The handle to close
    );

    [LibraryImport(KernelDll, SetLastError = true)]
    public static partial SafeTimerHandle CreateWaitableTimerExW(
        IntPtr lpTimerAttributes,  // Optional pointer to a SECURITY_ATTRIBUTES structure // Not needed here
        IntPtr lpTimerName, // Optional pinter to the name of the timer // Not needed here
        uint dwFlags, // Flags for the timer
        uint dwDesiredAccess // Access mask for the timer object
    );

    [LibraryImport(KernelDll, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWaitableTimer(
        SafeTimerHandle hTimer, // Pointer to the timer
        ref long lpDueTime, // Due time of the timer. Positive values are absolute timestamps while negative values are a reference to the current time (In 100ns steps)
        int lPeriod, // If bigger then 0 the timer will be signaled multiple times // Seems to be less precise so we just keep reseeting the timer instead
        IntPtr pfnCompletionRoutine, // Optional pointer to a completion routine // Not needed here as we use the I/O completion package instead 
        IntPtr lpArgToCompletionRoutine, // Optional arguments for the completion routine // Not needed here
        [MarshalAs(UnmanagedType.Bool)] bool fResume // Setting for energy saving mode // Use false
    );

    [LibraryImport(KernelDll, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CancelWaitableTimer(
        SafeTimerHandle hTimer // Handle of the timer to cancel
    );
#else
    [DllImport(KernelDll, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseHandle(
          IntPtr hObject // The handle to close
      );

    [DllImport(KernelDll, SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern SafeTimerHandle CreateWaitableTimerExW(
        IntPtr lpTimerAttributes, // Optional pointer to a SECURITY_ATTRIBUTES structure
        IntPtr lpTimerName, // Optional pointer to the name of the timer
        uint dwFlags, // Flags for the timer
        uint dwDesiredAccess // Access mask for the timer object
    );

    [DllImport(KernelDll, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWaitableTimer(
        SafeTimerHandle hTimer, // Pointer to the timer
        ref long lpDueTime, // Due time (absolute or relative in 100ns steps)
        int lPeriod, // Period for recurring timer, 0 for one-shot
        IntPtr pfnCompletionRoutine, // Optional completion routine
        IntPtr lpArgToCompletionRoutine, // Optional arguments for completion routine
        [MarshalAs(UnmanagedType.Bool)] bool fResume // Energy-saving mode setting
    );

    [DllImport(KernelDll, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CancelWaitableTimer(
        SafeTimerHandle hTimer // Handle of the timer to cancel
    );

    [DllImport("ntdll.dll")]
    private static extern int RtlGetVersion(ref OSVERSIONINFOEX versionInfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct OSVERSIONINFOEX
    {
        public int dwOSVersionInfoSize;
        public int dwMajorVersion;
        public int dwMinorVersion;
        public int dwBuildNumber;
        public int dwPlatformId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szCSDVersion;
    }

    public static Version GetRealWindowsVersion()
    {
        var osVersionInfo = new OSVERSIONINFOEX();
        osVersionInfo.dwOSVersionInfoSize = Marshal.SizeOf(osVersionInfo);
        _ = RtlGetVersion(ref osVersionInfo);
        return new Version(osVersionInfo.dwMajorVersion, osVersionInfo.dwMinorVersion, osVersionInfo.dwBuildNumber);
    }
#endif

    [Flags]
    public enum TimerAccessMask : uint
    {
        DELETE = 0x00010000,
        READ_CONTROL = 0x00020000,
        SYNCHRONIZE = 0x00100000,
        WRITE_DAC = 0x00040000,
        WRITE_OWNER = 0x00080000,
        TIMER_ALL_ACCESS = 0x1F0003,
        TIMER_MODIFY_STATE = 0x0002,
        TIMER_QUERY_STATE = 0x0001, //Reserved for future
    }

    public const uint CreateWaitableTimerFlag_HIGH_RESOLUTION = 0x00000002;
}
