using System.Runtime.InteropServices;

namespace QuickTickLib;

internal static partial class Win32Interop
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
        IntPtr lpTimerName, // Optional pointer to the name of the timer // Not needed here
        uint dwFlags, // Flags for the timer
        uint dwDesiredAccess // Access mask for the timer object
    );

    [LibraryImport(KernelDll, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWaitableTimer(
        SafeTimerHandle hTimer, // Pointer to the timer
        ref long lpDueTime, // Timer due time. Positive values are absolute timestamps while negative values are a reference to the current time (In 100ns steps)
        int lPeriod, // If bigger than 0 the timer will be signaled multiple times // Seems to be less precise so we just keep resetting the timer instead
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
        IntPtr lpTimerAttributes, // Optional pointer to a SECURITY_ATTRIBUTES structure // Not needed here
        IntPtr lpTimerName, // Optional pointer to the name of the timer // Not needed here
        uint dwFlags, // Flags for the timer
        uint dwDesiredAccess // Access mask for the timer object
    );

    [DllImport(KernelDll, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWaitableTimer(
        SafeTimerHandle hTimer, // Pointer to the timer
        ref long lpDueTime, // Timer due time. Positive values are absolute timestamps while negative values are a reference to the current time (In 100ns steps)
        int lPeriod, // If bigger than 0 the timer will be signaled multiple times // Seems to be less precise so we just keep resetting the timer instead
        IntPtr pfnCompletionRoutine, // Optional pointer to a completion routine // Not needed here as we use the I/O completion package instead 
        IntPtr lpArgToCompletionRoutine, // Optional arguments for the completion routine // Not needed here
        [MarshalAs(UnmanagedType.Bool)] bool fResume // Setting for energy saving mode // Use false
    );

    [DllImport(KernelDll, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CancelWaitableTimer(
        SafeTimerHandle hTimer // Handle of the timer to cancel
    );

    [DllImport("ntdll.dll")]
    private static extern int RtlGetVersion(ref Osversioninfoex versionInfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct Osversioninfoex
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
        var osVersionInfo = new Osversioninfoex();
        osVersionInfo.dwOSVersionInfoSize = Marshal.SizeOf(osVersionInfo);
        _ = RtlGetVersion(ref osVersionInfo);
        return new Version(osVersionInfo.dwMajorVersion, osVersionInfo.dwMinorVersion, osVersionInfo.dwBuildNumber);
    }
#endif
}
