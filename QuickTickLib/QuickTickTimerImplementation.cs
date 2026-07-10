// Some parts of the code are inspired by György Kőszeg's HighRes Timer: https://github.com/koszeggy/KGySoft.CoreLibraries/blob/master/KGySoft.CoreLibraries/CoreLibraries/HiResTimer.cs

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace QuickTickLib;

internal sealed class QuickTickTimerImplementation : IQuickTickTimer
{
    private readonly QuickTickHandleResources handles;

    private volatile bool autoReset;
    private volatile bool skipMissedIntervals;
    private volatile bool running;
    private double intervalMs;
    private long intervalTicks;

    private int disposedState;
    private readonly object stateLock = new();

    private CancellationTokenSource? cancellationTokenSource;
    private ManualResetEvent? stopEvent;
    private Thread? completionThread;
    private ThreadPriority threadPriority = ThreadPriority.Normal;

    internal Thread? WorkerThreadForTests => completionThread;

    public event QuickTickElapsedEventHandler? Elapsed;

    public double Interval
    {
        get => Volatile.Read(ref intervalMs);
        set
        {
            if (value > int.MaxValue || value < 0.5 || double.IsNaN(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Interval must be between 0.5 and int.MaxValue");
            }

            Volatile.Write(ref intervalMs, value);
            Interlocked.Exchange(ref intervalTicks, (long)(value * QuickTickHelper.StopwatchTicksPerMillisecond));
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
            if (completionThread is { IsAlive: true })
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
        lock (stateLock)
        {
            if (running)
            {
                return;
            }

            running = true;
            var cts = new CancellationTokenSource();

            // A leftover event exists when the previous run ended itself (AutoReset = false); its exited thread never waits on it again
            stopEvent?.Dispose();
            var localStopEvent = new ManualResetEvent(false);

            completionThread = new Thread(() => CompletionThreadLoop(cts, localStopEvent))
            {
                IsBackground = true,
                Priority = Priority,
                Name = "QuickTick Timer"
            };

            cancellationTokenSource = cts;
            stopEvent = localStopEvent;
            completionThread.Start();
        }
    }

    public void Stop()
    {
        lock (stateLock)
        {
            if (!running)
            {
                return;
            }

            running = false;
            cancellationTokenSource?.Cancel();
            cancellationTokenSource = null;

            Win32Interop.CancelWaitableTimer(handles.TimerHandle);

            if (Thread.CurrentThread != completionThread)
            {
                // Wake the completion thread out of its wait
                // If called from the same thread we know we are called from the event handler code therefore we are not waiting on the timer and don't need to signal
                stopEvent?.Set();

                completionThread?.Join();
            }
            completionThread = null;

            // Safe even in the self-stop case: after the handler returns the completion thread only checks the cancellation token and exits, it never waits again
            stopEvent?.Dispose();
            stopEvent = null;
        }
    }

    private void CompletionThreadLoop(CancellationTokenSource localCancellationTokenSource, ManualResetEvent localStopEvent)
    {
        // The timer is a synchronization (auto-reset) timer: a satisfied wait resets it and SetWaitableTimer resets any pending signal on re-arm,
        // so no stale signal can leak across Stop/Start cycles
        var waitHandles = new[] { handles.TimerWaitHandle, localStopEvent };

        var stopWatch = Stopwatch.StartNew();
        var nextFireTicks = Interlocked.Read(ref intervalTicks);
        var lastFireTicks = 0L;
        var skippedIntervals = 0L;

        SetTimer(stopWatch, nextFireTicks);

        while (!localCancellationTokenSource.IsCancellationRequested)
        {
            var signaledIndex = WaitHandle.WaitAny(waitHandles);

            if (localCancellationTokenSource.IsCancellationRequested || signaledIndex != 0)
            {
                break;
            }

            var currentTicks = stopWatch.ElapsedTicks;
            var lastFireTicksLocal = lastFireTicks;
            lastFireTicks = currentTicks;

            if (autoReset)
            {
                var interval = Interlocked.Read(ref intervalTicks);
                nextFireTicks += interval;

                if (skipMissedIntervals)
                {
                    while (nextFireTicks < currentTicks)
                    {
                        nextFireTicks += interval;
                        if (skippedIntervals < long.MaxValue)
                        {
                            skippedIntervals++;
                        }
                    }
                }

                if (stopWatch.Elapsed.TotalHours >= 1)
                {
                    var remaining = nextFireTicks - currentTicks;
                    stopWatch.Restart();
                    nextFireTicks = remaining;
                    lastFireTicks = 0;
                }

                SetTimer(stopWatch, nextFireTicks);
            }

            var timeSinceLastFire = TimeSpan.FromTicks(QuickTickHelper.StopwatchTicksToTimeSpanTicks(currentTicks - lastFireTicksLocal));
            var elapsedEventArgs = new QuickTickElapsedEventArgs(timeSinceLastFire, skippedIntervals);
            var handler = Elapsed;

            if (!autoReset)
            {
                running = false; // Same logic as System.Timers.Timer: set running=false before invoking the handler when AutoReset is disabled
                localCancellationTokenSource.Cancel();
                handler?.Invoke(this, elapsedEventArgs);
                break;
            }

            handler?.Invoke(this, elapsedEventArgs);
        }
    }

    private void SetTimer(Stopwatch stopWatch, long nextFireTicks)
    {
        long dueTimeStopwatchTicks = nextFireTicks - stopWatch.ElapsedTicks;
        long dueTime = dueTimeStopwatchTicks < 0 ? 0 : QuickTickHelper.StopwatchTicksToTimeSpanTicks(dueTimeStopwatchTicks);
        dueTime = -dueTime; // Negative = relative time for SetWaitableTimer, which expects 100ns units

        if (!Win32Interop.SetWaitableTimer(handles.TimerHandle, ref dueTime, 0, IntPtr.Zero, IntPtr.Zero, false)) // Setting the period to 0 makes the timer fire exactly once
        {
            throw new InvalidOperationException($"SetWaitableTimer failed: {Marshal.GetLastWin32Error()}");
        }
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
            handles.Dispose();
            Elapsed = null;
        }
    }
}
