using System.Runtime.InteropServices;

internal class Win32Interop
{
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(
        IntPtr hObject // The handle to close
    );

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr CreateIoCompletionPort(
        IntPtr FileHandle, // A file handle or Invalid_Handle_Value
        IntPtr ExistingCompletionPort, // Handle for a open I/O completion port or null: Must be null if FileHandle is Invalid_Handle_Value
        UIntPtr CompletionKey, // CompletionKey to be added to all I/O completion packages related to the file handle  : Ignored if FileHandle is Invalid_Handle_Value
        uint NumberOfConcurrentThreads // Number of threads that can be used for completion packages. 0 to allow the number of processors in the system
    );

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
        TIMER_QUERY_STATE = 0x0001, //Reserved for future // Is needed for NtCreateWaitCompletionPacket
    }

    [DllImport("ntdll.dll", SetLastError = true)]
    public static extern int NtCreateWaitCompletionPacket(
        out IntPtr WaitCompletionPacketHandle, // A pointer to a variable that receives the wait completion packet handle
        uint DesiredAccess, // Access mask for the wait completion packet
        IntPtr ObjectAttributes // Optional pointer to  PROJECT_ATTRIBUTES // Not needed here
    );

    public const uint CreateWaitableTimerFlag_NONE = 0x00000000;
    public const uint CreateWaitableTimerFlag_MANUAL_RESET = 0x00000001;
    public const uint CreateWaitableTimerFlag_HIGH_RESOLUTION = 0x00000002;

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr CreateWaitableTimerExW(
        IntPtr lpTimerAttributes,  // Optional pointer to a SECURITY_ATTRIBUTES structure // Not needed here
        IntPtr lpTimerName, // Optional pinter to the name of the timer // Not needed here
        uint dwFlags, // Flags for the timer
        uint dwDesiredAccess // Access mask for the timer object
    );

    [DllImport("ntdll.dll", SetLastError = true)]
    public static extern int NtAssociateWaitCompletionPacket(
        IntPtr WaitCompletionPacketHandle, IntPtr IoCompletionHandle, IntPtr TargetObjectHandle,
        IntPtr KeyContext, IntPtr ApcContext, int IoStatus, UIntPtr IoStatusInformation, IntPtr AlreadySignaled
    );

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool SetWaitableTimer(
        IntPtr hTimer, ref long lpDueTime, int lPeriod, IntPtr pfnCompletionRoutine,
        IntPtr lpArgToCompletionRoutine, bool fResume
    );

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CancelWaitableTimer(
        IntPtr hTimer // Handle of the timer to cancel
    );

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetQueuedCompletionStatusEx(
        IntPtr CompletionPort, [Out] OVERLAPPED_ENTRY[] lpCompletionPortEntries,
        uint ulCount, out uint ulNumEntriesRemoved, uint dwMilliseconds, bool fAlertable
    );

    [StructLayout(LayoutKind.Sequential)]
    public struct OVERLAPPED_ENTRY
    {
        public IntPtr lpCompletionKey;
        public IntPtr lpOverlapped;
        public IntPtr Internal;
        public UIntPtr dwNumberOfBytesTransferred;
    }
}
