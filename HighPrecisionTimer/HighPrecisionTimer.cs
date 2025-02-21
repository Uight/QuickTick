using System.Runtime.InteropServices;

public class HighResTimer : IDisposable
{
    private IntPtr iocpHandle;
    private IntPtr waitIocpHandle;
    private IntPtr timerHandle;
    private readonly IntPtr highResKey;
    private bool isRunning;
    private Thread completionThread;
    private long intervalTicks;
    private long nextFireTime;

    public event Action? TimerElapsed;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateIoCompletionPort(
        IntPtr FileHandle, // A file handle or Invalid_Handle_Value
        IntPtr ExistingCompletionPort, // Handle for a open I/O completion port or null: Must be null if FileHandle is Invalid_Handle_Value
        UIntPtr CompletionKey, // CompletionKey to be added to all I/O completion packages related to the file handle  : Ignored if FileHandle is Invalid_Handle_Value
        uint NumberOfConcurrentThreads // Number of threads that can be used for completion packages. 0 to allow the number of processors in the system
    );

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(
        IntPtr hObject // The handle to close
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

    public HighResTimer()
    {
        iocpHandle = CreateIoCompletionPort(new IntPtr(-1), IntPtr.Zero, UIntPtr.Zero, 0);
        if (iocpHandle == IntPtr.Zero)
            throw new InvalidOperationException($"CreateIoCompletionPort failed: {Marshal.GetLastWin32Error()}");

        int status = NtCreateWaitCompletionPacket(out waitIocpHandle, 0x1F0003, IntPtr.Zero);
        if (status != 0)
            throw new InvalidOperationException($"NtCreateWaitCompletionPacket failed: {status:X8}");

        timerHandle = CreateWaitableTimerExW(IntPtr.Zero, IntPtr.Zero, 0x2 /* HIGH_RES */, 0x1F0003);
        if (timerHandle == IntPtr.Zero)
            throw new InvalidOperationException($"CreateWaitableTimerExW failed: {Marshal.GetLastWin32Error()}");

        highResKey = new IntPtr(1);
    }

    public void Start(TimeSpan interval)
    {
        if (isRunning) return;
        intervalTicks = interval.Ticks;
        isRunning = true;

        nextFireTime = DateTime.UtcNow.Ticks + intervalTicks; // Compute first expiration

        SetTimer();

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
        long dueTime = nextFireTime - DateTime.UtcNow.Ticks; // Calculate absolute expiration
        dueTime = dueTime < 0 ? 0 : -dueTime; // Ensure valid time

        if (!SetWaitableTimer(timerHandle, ref dueTime, 0, IntPtr.Zero, IntPtr.Zero, false))
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
                        nextFireTime += intervalTicks;
                        SetTimer();

                        TimerElapsed?.Invoke();
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
        {
            CloseHandle(iocpHandle);
        }
    }
}
