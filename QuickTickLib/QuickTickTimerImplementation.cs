using System.Runtime.InteropServices;

namespace QuickTickLib;

internal class QuickTickTimerImplementation : IQuickTickTimer
{
    private readonly IntPtr iocpHandle;
    private readonly IntPtr waitIocpHandle;
    private readonly IntPtr timerHandle;
    private readonly IntPtr successCompletionKey = new IntPtr(1);
    private long intervalTicks;
    private double intervalMs;
    private bool autoReset;
    private bool isRunning;
    private long nextFireTime;

    private Thread? completionThread;
    public event QuickTickElapsedEventHandler? Elapsed;

    public double Interval
    {
        get => intervalMs;
        set
        {
            if (value < 1 || value > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(Interval), "Interval must be between 1 and int.MaxValue");
            }

            intervalMs = value;
            intervalTicks = (long)(intervalMs * TimeSpan.TicksPerMillisecond);
        }
    }

    public bool AutoReset
    {
        get => autoReset;
        set => autoReset = value;
    }

    public QuickTickTimerImplementation(double interval)
    {
        AutoReset = true;
        Interval = interval;

        iocpHandle = Win32Interop.CreateIoCompletionPort(new IntPtr(-1), IntPtr.Zero, IntPtr.Zero, 0);
        if (iocpHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException($"CreateIoCompletionPort failed: {Marshal.GetLastWin32Error()}");
        }

        int status = Win32Interop.NtCreateWaitCompletionPacket(out waitIocpHandle, QuickTickHelper.NtCreateWaitCompletionPacketAccessRights, IntPtr.Zero);
        if (status != 0)
        {
            throw new InvalidOperationException($"NtCreateWaitCompletionPacket failed: {status:X8}");
        }

        timerHandle = Win32Interop.CreateWaitableTimerExW(IntPtr.Zero, IntPtr.Zero, Win32Interop.CreateWaitableTimerFlag_HIGH_RESOLUTION, QuickTickHelper.CreateWaitableTimerExWAccessRights);
        if (timerHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException($"CreateWaitableTimerExW failed: {Marshal.GetLastWin32Error()}");
        }
    }

    public void Start()
    {
        if (isRunning) return;

        isRunning = true;

        nextFireTime = DateTime.UtcNow.Ticks + intervalTicks; // Compute first expiration

        SetTimer();

        completionThread = new Thread(CompletionThreadLoop)
        {
            IsBackground = true,
            Priority = Thread.CurrentThread.Priority
        };
        completionThread.Start();
    }

    public void Stop()
    {
        if (!isRunning)
        {
            return;
        }

        isRunning = false;
        if (timerHandle != IntPtr.Zero)
        {
            Win32Interop.CancelWaitableTimer(timerHandle);
        }

        // Post a dummy completion to get out of GetQueuedCompletionStatusEx in the completionThread. Use a key different to successCompletionKey
        Win32Interop.PostQueuedCompletionStatus(iocpHandle, 0, IntPtr.Zero, IntPtr.Zero);

        completionThread?.Join();
        completionThread = null;

        if (waitIocpHandle != IntPtr.Zero)
        {
            Win32Interop.NtCancelWaitCompletionPacket(waitIocpHandle, true);
        }
    }

    public void Dispose()
    {
        Stop();

        if (waitIocpHandle != IntPtr.Zero)
        {
            Win32Interop.CloseHandle(waitIocpHandle);
        }

        if (iocpHandle != IntPtr.Zero)
        {
            Win32Interop.CloseHandle(iocpHandle);
        }

        if (timerHandle != IntPtr.Zero)
        {
            Win32Interop.CloseHandle(timerHandle);
        }
    }

    private void SetTimer()
    {
        long dueTime = nextFireTime - DateTime.UtcNow.Ticks; // Calculate absolute expiration
        dueTime = dueTime < 0 ? 0 : -dueTime; // Ensure valid time

        if (!Win32Interop.SetWaitableTimer(timerHandle, ref dueTime, 0, IntPtr.Zero, IntPtr.Zero, false))
        {
            throw new InvalidOperationException($"SetWaitableTimer failed: {Marshal.GetLastWin32Error()}");
        }

        int status = Win32Interop.NtAssociateWaitCompletionPacket(waitIocpHandle, iocpHandle, timerHandle, successCompletionKey, IntPtr.Zero, 0, IntPtr.Zero, out _);

        if (status != 0)
        {
            throw new InvalidOperationException($"NtAssociateWaitCompletionPacket failed: {status:X8}");
        }
    }

    private void CompletionThreadLoop()
    {
        while (isRunning)
        {
            if (Win32Interop.GetQueuedCompletionStatus(iocpHandle, out _, out var lpCompletionKey, out _, uint.MaxValue))
            {
                if (lpCompletionKey == successCompletionKey)
                {
                    var actualFireTime = DateTime.UtcNow;
                    var scheduledFireTime = new DateTime(nextFireTime, DateTimeKind.Utc);

                    var elapsedEventArgs = new QuickTickElapsedEventArgs(actualFireTime, scheduledFireTime);

                    if (autoReset)
                    {
                        nextFireTime += intervalTicks;
                        SetTimer();
                    }
                    else
                    {
                        isRunning = false;
                        if (timerHandle != IntPtr.Zero)
                        {
                            Win32Interop.CancelWaitableTimer(timerHandle);
                        }
                    }

                    if (Elapsed is not null)
                    {
                        Elapsed(this, elapsedEventArgs);
                    }
                }
            }
        }
    }
}
