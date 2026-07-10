// Some parts of the code are inspired by György Kőszeg's HighRes Timer: https://github.com/koszeggy/KGySoft.CoreLibraries/blob/master/KGySoft.CoreLibraries/CoreLibraries/HiResTimer.cs

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace QuickTickLib;

internal sealed class QuickTickTimerImplementation : IQuickTickTimer
{
    private readonly QuickTickHandleResources _handles;

    private volatile bool _autoReset;
    private volatile bool _skipMissedIntervals;
    private volatile bool _running;
    private double _intervalMs;
    private long _intervalTicks;

    private int _disposedState;
    private readonly object _stateLock = new();

    private CancellationTokenSource? _cancellationTokenSource;
    private ManualResetEvent? _stopEvent;
    private Thread? _completionThread;
    private ThreadPriority _threadPriority = ThreadPriority.Normal;

    internal Thread? WorkerThreadForTests => _completionThread;

    public event QuickTickElapsedEventHandler? Elapsed;

    public double Interval
    {
        get => Volatile.Read(ref _intervalMs);
        set
        {
            if (value > int.MaxValue || value < 0.5 || double.IsNaN(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Interval must be between 0.5 and int.MaxValue");
            }

            Volatile.Write(ref _intervalMs, value);
            Interlocked.Exchange(ref _intervalTicks, (long)(value * QuickTickHelper.StopwatchTicksPerMillisecond));
        }
    }

    public bool AutoReset
    {
        get => _autoReset;
        set => _autoReset = value;
    }

    public bool SkipMissedIntervals
    {
        get => _skipMissedIntervals;
        set => _skipMissedIntervals = value;
    }

    public ThreadPriority Priority
    {
        get => _threadPriority;
        set
        {
            _threadPriority = value;
            if (_completionThread is { IsAlive: true })
            {
                _completionThread.Priority = value;
            }
        }
    }

    public QuickTickTimerImplementation(double interval)
    {
        AutoReset = true;
        Interval = interval;
        _handles = new QuickTickHandleResources();
    }

    public void Start()
    {
        lock (_stateLock)
        {
            if (_running)
            {
                return;
            }

            _running = true;
            var cts = new CancellationTokenSource();

            // A leftover event exists when the previous run ended itself (AutoReset = false); its exited thread never waits on it again
            _stopEvent?.Dispose();
            var localStopEvent = new ManualResetEvent(false);

            _completionThread = new Thread(() => CompletionThreadLoop(cts, localStopEvent))
            {
                IsBackground = true,
                Priority = Priority,
                Name = "QuickTick Timer"
            };

            _cancellationTokenSource = cts;
            _stopEvent = localStopEvent;
            _completionThread.Start();
        }
    }

    public void Stop()
    {
        lock (_stateLock)
        {
            if (!_running)
            {
                return;
            }

            _running = false;
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource = null;

            Win32Interop.CancelWaitableTimer(_handles.TimerHandle);

            if (Thread.CurrentThread != _completionThread)
            {
                // Wake the completion thread out of its wait
                // If called from the same thread we know we are called from the event handler code therefore we are not waiting on the timer and don't need to signal
                _stopEvent?.Set();

                _completionThread?.Join();
            }
            _completionThread = null;

            // Safe even in the self-stop case: after the handler returns the completion thread only checks the cancellation token and exits, it never waits again
            _stopEvent?.Dispose();
            _stopEvent = null;
        }
    }

    private void CompletionThreadLoop(CancellationTokenSource localCancellationTokenSource, ManualResetEvent localStopEvent)
    {
        // The timer is a synchronization (auto-reset) timer: a satisfied wait resets it and SetWaitableTimer resets any pending signal on re-arm,
        // so no stale signal can leak across Stop/Start cycles
        var waitHandles = new[] { _handles.TimerWaitHandle, localStopEvent };

        var stopWatch = Stopwatch.StartNew();
        var nextFireTicks = Interlocked.Read(ref _intervalTicks);
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

            if (_autoReset)
            {
                var interval = Interlocked.Read(ref _intervalTicks);
                nextFireTicks += interval;

                if (_skipMissedIntervals)
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

            if (!_autoReset)
            {
                _running = false; // Same logic as System.Timers.Timer: set running=false before invoking the handler when AutoReset is disabled
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

        if (!Win32Interop.SetWaitableTimer(_handles.TimerHandle, ref dueTime, 0, IntPtr.Zero, IntPtr.Zero, false)) // Setting the period to 0 makes the timer fire exactly once
        {
            throw new InvalidOperationException($"SetWaitableTimer failed: {Marshal.GetLastWin32Error()}");
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposedState, 1) == 1)
        {
            return;
        }

        try
        {
            Stop();
        }
        finally
        {
            _handles.Dispose();
            Elapsed = null;
        }
    }
}
