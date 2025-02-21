using System;
using System.Runtime.InteropServices;
using System.Threading;

class HighResolutionSleep
{
    private static readonly IntPtr iocpHandle;
    private static readonly IntPtr highResKey = (IntPtr)1;
    private static IntPtr waitIocpHandle;

    static HighResolutionSleep()
    {
        // Create an IO Completion Port
        iocpHandle = CreateIoCompletionPort(new IntPtr(-1), IntPtr.Zero, IntPtr.Zero, 0);
        if (iocpHandle == IntPtr.Zero)
            throw new InvalidOperationException("Failed to create IO completion port.");

        // Create the Wait Completion Packet Handle
        int status = NtCreateWaitCompletionPacket(out waitIocpHandle, 0x1F0003, IntPtr.Zero);

        if (status != 0) // NTSTATUS success is 0
        {
            throw new InvalidOperationException($"NtCreateWaitCompletionPacket failed with status {status:X8}");
        }
    }

    //0x1F0003

    public static void Sleep(TimeSpan duration)
    {
        // Create high-resolution timer
        IntPtr timer = CreateWaitableTimerEx(IntPtr.Zero, null, 0x00000002 /* CREATE_WAITABLE_TIMER_HIGH_RESOLUTION */, 0x1F0003); // TIMER_ALL_ACCESS
        if (timer == IntPtr.Zero)
            throw new InvalidOperationException("Failed to create high-resolution waitable timer.");

        try
        {
            // Convert duration to 100-nanosecond intervals (negative for relative time)
            long dueTime = -duration.Ticks;

            if (!SetWaitableTimer(timer, ref dueTime, 0, IntPtr.Zero, IntPtr.Zero, false))
            {
                int error = Marshal.GetLastWin32Error();
                throw new InvalidOperationException("Failed to set waitable timer.");
            }

            // Associate the timer with the IO Completion Port
            int status = NtAssociateWaitCompletionPacket(waitIocpHandle, iocpHandle, timer, highResKey, IntPtr.Zero, 0, UIntPtr.Zero, IntPtr.Zero);
            if (status != 0)
                throw new InvalidOperationException($"NtAssociateWaitCompletionPacket failed with status {status}");

            // Wait for work or the timer to expire
            OVERLAPPED_ENTRY[] entries = new OVERLAPPED_ENTRY[64];
            uint numEntries = 0;

            if (!GetQueuedCompletionStatusEx(iocpHandle, entries, (uint)entries.Length, ref numEntries, uint.MaxValue, false))
                throw new InvalidOperationException("GetQueuedCompletionStatusEx failed.");

            // Process completion entries
            for (int i = 0; i < numEntries; i++)
            {
                if (entries[i].lpCompletionKey == highResKey)
                {
                    // Timer expired
                    return;
                }
                Console.WriteLine("H#");
                // Handle other I/O work if needed
            }
        }
        finally
        {
            CloseHandle(timer);
        }
    }

    // P/Invoke Declarations

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateIoCompletionPort(IntPtr FileHandle, IntPtr ExistingCompletionPort, IntPtr CompletionKey, uint NumberOfConcurrentThreads);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateWaitableTimerEx(IntPtr lpTimerAttributes, string? lpTimerName, uint dwFlags, uint dwDesiredAccess);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetWaitableTimer(IntPtr hTimer, ref long lpDueTime, int lPeriod, IntPtr pfnCompletionRoutine, IntPtr lpArgToCompletionRoutine, bool fResume);

    [DllImport("ntdll.dll", SetLastError = true)]
    private static extern int NtAssociateWaitCompletionPacket(
        IntPtr WaitCompletionPacketHandle,  // IO Completion Port
        IntPtr IoCompletionHandle,          // I/O Completion Handle
        IntPtr TargetObjectHandle,          // The timer handle
        IntPtr KeyContext,                  // User-defined key
        IntPtr ApcContext,                   // Usually NULL
        int IoStatus,                        // Expected NTSTATUS (0 = STATUS_SUCCESS)
        UIntPtr IoStatusInformation,         // Extra info (0 in most cases)
        IntPtr AlreadySignaled               // NULL if not needed
    );

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetQueuedCompletionStatusEx(IntPtr CompletionPort, [Out] OVERLAPPED_ENTRY[] lpCompletionPortEntries, uint ulCount, ref uint ulNumEntriesRemoved, uint dwMilliseconds, bool fAlertable);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("ntdll.dll", SetLastError = true)]
    private static extern int NtCreateWaitCompletionPacket(
        out IntPtr WaitCompletionPacketHandle,
        uint DesiredAccess,
        IntPtr ObjectAttributes
    );

    // Structures
    [StructLayout(LayoutKind.Sequential)]
    private struct OVERLAPPED_ENTRY
    {
        public IntPtr lpCompletionKey;
        public IntPtr lpOverlapped;
        public IntPtr Internal;
        public uint dwNumberOfBytesTransferred;
    }
}
