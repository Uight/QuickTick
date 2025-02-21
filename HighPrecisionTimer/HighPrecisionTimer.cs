using System;
using System.Runtime.InteropServices;
using System.Threading;

public class HighResTimer : IDisposable
{
    private IntPtr iocpHandle;
    private IntPtr waitIocpHandle;
    private IntPtr timerHandle;
    private readonly IntPtr highResKey;
    private bool isRunning;
    private Thread completionThread;
    private long intervalTicks;

    public event Action? TimerElapsed;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateIoCompletionPort(
        IntPtr FileHandle, IntPtr ExistingCompletionPort, IntPtr CompletionKey, uint NumberOfConcurrentThreads
    );

    [DllImport("ntdll.dll", SetLastError = true)]
    private static extern int NtCreateWaitCompletionPacket(
        out IntPtr WaitCompletionPacketHandle, uint DesiredAccess, IntPtr ObjectAttributes
    );

    [DllImport("ntdll.dll", SetLastError = true)]
    private static extern int NtAssociateWaitCompletionPacket(
        IntPtr WaitCompletionPacketHandle, IntPtr IoCompletionHandle, IntPtr TargetObjectHandle,
        IntPtr KeyContext, IntPtr ApcContext, int IoStatus, UIntPtr IoStatusInformation, IntPtr AlreadySignaled
    );

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateWaitableTimerExW(
        IntPtr lpTimerAttributes, IntPtr lpTimerName, uint dwFlags, uint dwDesiredAccess
    );

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetWaitableTimer(
        IntPtr hTimer, ref long lpDueTime, int lPeriod, IntPtr pfnCompletionRoutine,
        IntPtr lpArgToCompletionRoutine, bool fResume
    );

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetQueuedCompletionStatusEx(
        IntPtr CompletionPort, [Out] OVERLAPPED_ENTRY[] lpCompletionPortEntries,
        uint ulCount, out uint ulNumEntriesRemoved, uint dwMilliseconds, bool fAlertable
    );

    [StructLayout(LayoutKind.Sequential)]
    private struct OVERLAPPED_ENTRY
    {
        public IntPtr lpCompletionKey;
        public IntPtr lpOverlapped;
        public IntPtr Internal;
        public UIntPtr dwNumberOfBytesTransferred;
    }

    /// <summary>
    /// Current implementaiton has a slight drift. e.g. 5ms has an average of 5.1 ms
    /// Lowest possible time seems to be around 1.5ms when setting it to 1ms.
    /// Implementation with resetting the time using a timestampt for next event dont drift but cause some 0 time issues.
    /// </summary>
    public HighResTimer()
    {
        // Create the IOCP handle
        iocpHandle = CreateIoCompletionPort(new IntPtr(-1), IntPtr.Zero, IntPtr.Zero, 0);
        if (iocpHandle == IntPtr.Zero)
            throw new InvalidOperationException($"CreateIoCompletionPort failed: {Marshal.GetLastWin32Error()}");

        // Create the Wait Completion Packet Handle
        int status = NtCreateWaitCompletionPacket(out waitIocpHandle, 0x1F0003, IntPtr.Zero);
        if (status != 0)
            throw new InvalidOperationException($"NtCreateWaitCompletionPacket failed: {status:X8}");

        // Create the high-resolution timer
        timerHandle = CreateWaitableTimerExW(IntPtr.Zero, IntPtr.Zero, 0x2 /* HIGH_RES */, 0x1F0003);
        if (timerHandle == IntPtr.Zero)
            throw new InvalidOperationException($"CreateWaitableTimerExW failed: {Marshal.GetLastWin32Error()}");

        highResKey = new IntPtr(1); // Arbitrary key for completion events
    }

    public void Start(TimeSpan interval)
    {
        if (isRunning) return;
        intervalTicks = interval.Ticks;
        isRunning = true;

        SetTimer(); // Start with the correct absolute time

        completionThread = new Thread(CompletionThreadLoop) { IsBackground = true };
        completionThread.Start();
    }

    public void Stop()
    {
        isRunning = false;
        completionThread?.Join();
    }

    private void SetTimer()
    {
        long dueTime = -intervalTicks; // Negative value means relative time
        int periodMs = (int)(intervalTicks / TimeSpan.TicksPerMillisecond);

        if (!SetWaitableTimer(timerHandle, ref dueTime, periodMs, IntPtr.Zero, IntPtr.Zero, false))
            throw new InvalidOperationException($"SetWaitableTimer failed: {Marshal.GetLastWin32Error()}");

        int status = NtAssociateWaitCompletionPacket(
            waitIocpHandle, iocpHandle, timerHandle, highResKey, IntPtr.Zero, 0, UIntPtr.Zero, IntPtr.Zero
        );

        if (status != 0)
            throw new InvalidOperationException($"NtAssociateWaitCompletionPacket failed: {status:X8}");
    }

    private void CompletionThreadLoop()
    {
        OVERLAPPED_ENTRY[] entries = new OVERLAPPED_ENTRY[64];

        while (isRunning)
        {
            if (GetQueuedCompletionStatusEx(iocpHandle, entries, 64, out uint count, uint.MaxValue, false))
            {
                for (int i = 0; i < count; i++)
                {
                    if (entries[i].lpCompletionKey == highResKey)
                    {
                        TimerElapsed?.Invoke(); // Trigger event when the timer expires

                        var pointer = IntPtr.Zero;

                        int status = NtAssociateWaitCompletionPacket(
    waitIocpHandle, iocpHandle, timerHandle, highResKey, IntPtr.Zero, 0, UIntPtr.Zero, IntPtr.Zero);

                        if (status != 0)
                            throw new InvalidOperationException($"NtAssociateWaitCompletionPacket failed: {status:X8}");
                    }
                }
            }
        }
    }

    public void Dispose()
    {
        isRunning = false;
        completionThread?.Join();

        if (timerHandle != IntPtr.Zero)
            Marshal.FreeHGlobal(timerHandle);

        if (waitIocpHandle != IntPtr.Zero)
            Marshal.FreeHGlobal(waitIocpHandle);

        if (iocpHandle != IntPtr.Zero)
            Marshal.FreeHGlobal(iocpHandle);
    }
}
