// Some parts of the code are inspired by György Kőszeg's HighRes Timer: https://github.com/koszeggy/KGySoft.CoreLibraries/blob/master/KGySoft.CoreLibraries/CoreLibraries/HiResTimer.cs

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace QuickTickLib;

internal sealed class QuickTickTimerImplementation : IQuickTickTimer
{
    private readonly QuickTickHandleResources handles;

    private static readonly IntPtr successCompletionKey = new(1);

    private volatile bool autoReset;
    private volatile bool skipMissedIntervals;
    private volatile bool isRunning;
    private volatile float intervalMs;
    private long intervalTicks;
    private long nextFireTicks;
    private long lastFireTicks;
    private long totalSkippedIntervals;
    private readonly Stopwatch stopWatch = new();

    private Thread? completionThread;
    private ThreadPriority threadPriority = ThreadPriority.Normal;
    private QuickTickElapsedEventHandler? elapsed;

    private int disposedState;

    public event QuickTickElapsedEventHandler? Elapsed
    {
        add => elapsed += value;
        remove => elapsed -= value;
    }

    public double Interval
    {
        get => intervalMs;
        set
        {
            if (value > int.MaxValue || value < 0.5 || double.IsNaN(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Interval must be between 0.5 and int.MaxValue");
            }

            intervalMs = (float)value;
            Interlocked.Exchange(ref intervalTicks, (long)(intervalMs * TimeSpan.TicksPerMillisecond));
        }
    }

    public bool AutoReset
    {
        get => autoReset;
        set => autoReset = value;
    }

    public bool SkipMissedIntervals
    {
        get => skipMissedIntervals;
        set => skipMissedIntervals = value;
    }

    public ThreadPriority Priority
    {
        get => threadPriority;
        set
        {
            threadPriority = value;
            if (completionThread != null && completionThread.IsAlive)
            {
                completionThread.Priority = value;
            }           
        }
    }

    public QuickTickTimerImplementation(double interval)
    {
        AutoReset = true;
        Interval = interval;

        handles = new QuickTickHandleResources();
    }

    public void Start()
    {
        if (isRunning)
        {
            return;
        }
        stopWatch.Restart();

        isRunning = true;

        Interlocked.Exchange(ref nextFireTicks, Interlocked.Read(ref intervalTicks));
        Interlocked.Exchange(ref totalSkippedIntervals, 0L);
        Interlocked.Exchange(ref lastFireTicks, 0L);

        SetTimer();

        completionThread = new Thread(CompletionThreadLoop)
        {
            IsBackground = true,
            Priority = Priority
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
        if (!handles.TimerHandle.IsInvalid)
        {
            Win32Interop.CancelWaitableTimer(handles.TimerHandle);
        }

        // Post a dummy completion to get out of GetQueuedCompletionStatusEx in the completionThread. Use a key different to successCompletionKey
        Win32Interop.PostQueuedCompletionStatus(handles.IocpHandle, 0, IntPtr.Zero, IntPtr.Zero);

        if (Thread.CurrentThread != completionThread)
        {
            completionThread?.Join();
        }
        completionThread = null;

        if (!handles.WaitIocpHandle.IsInvalid)
        {
            Win32Interop.NtCancelWaitCompletionPacket(handles.WaitIocpHandle, true);
        }
        stopWatch.Reset();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposedState, 1) == 1)
        {
            return;
        }

        try
        {
            Stop();
        }
        finally
        {
            elapsed = null;
            handles.Dispose();
        }
    }

    private void SetTimer()
    {
        long dueTime = Interlocked.Read(ref nextFireTicks) - stopWatch.ElapsedTicks; // Calculate absolute expiration
        dueTime = dueTime < 0 ? 0 : -dueTime; // Ensure valid time

        if (!Win32Interop.SetWaitableTimer(handles.TimerHandle, ref dueTime, 0, IntPtr.Zero, IntPtr.Zero, false))
        {
            throw new InvalidOperationException($"SetWaitableTimer failed: {Marshal.GetLastWin32Error()}");
        }

        int status = Win32Interop.NtAssociateWaitCompletionPacket(handles.WaitIocpHandle, handles.IocpHandle, handles.TimerHandle, successCompletionKey, IntPtr.Zero, 0, IntPtr.Zero, out _);

        if (status != 0)
        {
            throw new InvalidOperationException($"NtAssociateWaitCompletionPacket failed: {status:X8}");
        }
    }

    private void CompletionThreadLoop()
    {
        while (isRunning)
        {
            var getStatusResult = Win32Interop.GetQueuedCompletionStatus(handles.IocpHandle, out _, out var lpCompletionKey, out _, uint.MaxValue);

            if (!getStatusResult)
            {
                if (!Win32Interop.GetHandleInformation(handles.IocpHandle, out _))
                {
                    isRunning = false;
                    break;
                }
                continue;
            }

            if (lpCompletionKey != successCompletionKey)
            {
                continue;
            }

            var currentTicks = stopWatch.ElapsedTicks;
            var lastFireTicksLocal = Interlocked.Read(ref lastFireTicks);
            Interlocked.Exchange(ref lastFireTicks, currentTicks);
            var skippedIntervals = Interlocked.Read(ref totalSkippedIntervals);

            if (autoReset)
            {
                var interval = Interlocked.Read(ref intervalTicks);
                var nextTicks = Interlocked.Add(ref nextFireTicks, interval);           

                if (skipMissedIntervals)
                {
                    while (nextTicks < currentTicks)
                    {
                        nextTicks = Interlocked.Add(ref nextFireTicks, interval);
                        if (skippedIntervals < long.MaxValue)
                        {
                            skippedIntervals++;
                        }
                    }
                    Interlocked.Exchange(ref totalSkippedIntervals, skippedIntervals);
                }

                if (stopWatch.Elapsed.TotalHours >= 1)
                {
                    var remaining = nextTicks - currentTicks;
                    stopWatch.Restart();
                    Interlocked.Exchange(ref nextFireTicks, remaining);
                    Interlocked.Exchange(ref lastFireTicks, 0L);
                }

                SetTimer();
            }
            else
            {
                isRunning = false;
                stopWatch.Reset();
                if (!handles.TimerHandle.IsInvalid)
                {
                    Win32Interop.CancelWaitableTimer(handles.TimerHandle);
                }
            }
    
            var timeSinceLastFire = TimeSpan.FromTicks(currentTicks - lastFireTicksLocal);
            var elapsedEventArgs = new QuickTickElapsedEventArgs(timeSinceLastFire, skippedIntervals);

            var handler = elapsed;
            handler?.Invoke(this, elapsedEventArgs);
        }
    }
}
