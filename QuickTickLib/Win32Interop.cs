using System.Runtime.InteropServices;

namespace QuickTickLib;

internal class Win32Interop
{
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(
        IntPtr hObject // The handle to close
    );

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr CreateIoCompletionPort(
        IntPtr FileHandle, // A file handle or Invalid_Handle_Value // Not needed here => use Invalid_Handle_Value
        IntPtr ExistingCompletionPort, // Handle for a open I/O completion port or null // Not needed here
        IntPtr CompletionKey, // Optional CompletionKey to be added to all I/O completion packages related to the file handle // Not needed here
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
        IntPtr WaitCompletionPacketHandle, // Handle to a wait completion package
        IntPtr IoCompletionHandle, // Handle to the I/O completion port
        IntPtr TargetObjectHandle, // A handle to a waitable object // Here the timer
        IntPtr KeyContext, // Optional key context // Not needed here 
        IntPtr ApcContext, // Optional apc context // Not needed here
        int IoStatus, // Status that would be returned on call to NtRemoveIoCompletion
        IntPtr IoStatusInformation, // Status information that would be returned on call to NtRemoveIoCompletion
        out bool AlreadySignaled // Indicates wether the target object was allready signaled // Not needed here
    );

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool SetWaitableTimer(
        IntPtr hTimer, // Pointer to the timer
        ref long lpDueTime, // Due time of the timer. Positive values are absolute timestamps while negative values are a reference to the current time (In 100ns steps)
        int lPeriod, // If bigger then 0 the timer will be signaled multiple times // Seems to be less precise so we just keep reseeting the timer instead
        IntPtr pfnCompletionRoutine, // Optional pointer to a completion routine // Not needed here as we use the I/O completion package instead 
        IntPtr lpArgToCompletionRoutine, // Optional arguments for the completion routine // Not needed here
        bool fResume // Setting for energy saving mode // Use false
    );

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CancelWaitableTimer(
        IntPtr hTimer // Handle of the timer to cancel
    );

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetQueuedCompletionStatusEx(
    IntPtr CompletionPort, // Handle to the I/O completion port
    [Out] OVERLAPPED_ENTRY[] lpCompletionPortEntries, // Pointer to a pre-allocated array of OVERLAPPED_ENTRY which is then filled by the function
    uint ulCount,  // Length of the pre-allocated array of OVERLAPPED_ENTRY
    out uint ulNumEntriesRemoved, // Actual number of OVERLAPPED_ENTRY which was read
    uint dwMilliseconds, // Timeout for the wait operation, uint.MaxValue means Infinite
    bool fAlertable // False => Wait for timeout; True => Do an alertable wait
    );

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool PostQueuedCompletionStatus(
        IntPtr completionPort, // Handle to the I/O completion port
        uint numberOfBytesTransferred, // Value that is returned as lpNumberOfBytesTransferred on GetQueuedCompletionStatus
        IntPtr completionKey, // Value thats returned as lpCompletionKey on GetQueuedCompletionStatus
        IntPtr lpOverlapped // Value thats returned as lpOverlapped on GetQueuedCompletionStatus
    );

    [StructLayout(LayoutKind.Sequential)]
    public struct OVERLAPPED_ENTRY
    {
        public IntPtr lpCompletionKey;
        public IntPtr lpOverlapped;
        public IntPtr Internal;
        public uint dwNumberOfBytesTransferred;
    }
}
