// Some parts of the code are inspired by György Kőszeg's HighRes Timer: https://github.com/koszeggy/KGySoft.CoreLibraries/blob/master/KGySoft.CoreLibraries/CoreLibraries/HiResTimer.cs

using System.Diagnostics;

namespace QuickTickLib;

public sealed class HighResQuickTickTimer : IQuickTickTimer
{
    private volatile bool _autoReset;
    private volatile bool _running;
    private volatile bool _skipMissedIntervals;
    private double _intervalMs;
    private volatile float _sleepThreshold = 1.5f; // This is a value that works on Ubuntu and Windows with appropriate power settings. Getting this value was done by extensively testing with the TimingReportGenerator
    private volatile float _yieldThreshold = 0.75f; // This is a value that works on Ubuntu and Windows with appropriate power settings. Getting this value was done by extensively testing with the TimingReportGenerator
    private long _intervalTicks;
    private ThreadPriority _threadPriority = ThreadPriority.Highest;
    private HighResQuickTickTimerRun? _currentRun;
    private Thread? _retiringThread;
    private int _disposedState;
    private readonly object _stateLock = new();

    internal Thread? WorkerThreadForTests => _currentRun?.Thread;

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
            if (_currentRun?.Thread is { IsAlive: true } workerThread)
            {
                workerThread.Priority = value;
            }
        }
    }

    public event QuickTickElapsedEventHandler? Elapsed;

    /// <summary>
    /// Defines the minimum time that must be available towards the next timer iteration for the thread to sleep.
    /// Increasing this time can lead to better timing but increases CPU usage as the code will Yield or SpinWait instead.
    /// Must be at least 1.0 and at most int.MaxValue.
    /// YieldThreshold must always be less than or equal to SleepThreshold.
    /// Setting SleepThreshold to int.MaxValue will basically disable sleeping the thread and the timer will Yield or SpinWait the thread instead.
    /// </summary>
    public double SleepThreshold
    {
        get => _sleepThreshold;
        set
        {
            if (value < 1.0 || value > int.MaxValue || double.IsNaN(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), "SleepThreshold must be greater than or equal to 1.0 and smaller than int.MaxValue.");
            }

            if (_yieldThreshold > value)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "SleepThreshold must be greater than or equal to YieldThreshold.");
            }

            _sleepThreshold = (float)value;
        }
    }

    /// <summary>
    /// Defines the minimum time that must be available towards the next timer iteration to yield the thread.
    /// Increasing this time can lead to better timing but increases CPU usage as the code will SpinWait instead.
    /// Must be at least 0.0 and at most the value of SleepThreshold. 
    /// Setting YieldThreshold equal to SleepThreshold disables yielding (goes directly to spin wait).
    /// Setting YieldThreshold equal to 0.0 will basically disable spin waiting although if no process is ready to run on this thread the behavior is almost the same.
    /// </summary>
    public double YieldThreshold
    {
        get => _yieldThreshold;
        set
        {
            if (value < 0.0 || value > _sleepThreshold || double.IsNaN(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), "YieldThreshold must be greater than or equal to 0.0 and less than or equal to SleepThreshold.");
            }

            _yieldThreshold = (float)value;
        }
    }

    public HighResQuickTickTimer(double interval)
    {
        AutoReset = true;
        Interval = interval;
    }

    public void Start()
    {
        lock (_stateLock)
        {
            ThrowIfDisposed();

            if (_running)
            {
                return;
            }

            // Construct the run before setting any state so a failing handle creation leaves the timer stopped
            var run = new HighResQuickTickTimerRun();
            run.Thread = new Thread(() => RunTimer(run))
            {
                IsBackground = true,
                Priority = Priority,
                Name = "QuickTick Timer"
            };

            _currentRun = run;
            _running = true;
            run.Thread.Start();
        }
    }

    public void Stop()
    {
        Thread? threadToJoin;

        lock (_stateLock)
        {
            if (_running)
            {
                _running = false;

                var run = _currentRun!; // While _running is true a current run always exists
                run.CancellationTokenSource.Cancel();
                _currentRun = null;

                if (Thread.CurrentThread == run.Thread)
                {
                    // Called from the Elapsed handler: the worker exits and disposes its run on its own after the handler returns
                    return;
                }

                _retiringThread = run.Thread;
            }

            threadToJoin = _retiringThread;
        }

        if (threadToJoin != null && threadToJoin != Thread.CurrentThread)
        {
            // Join outside the lock: an Elapsed handler calling Stop()/Dispose() concurrently blocks on the
            // state lock, so joining while holding it would deadlock against the very thread being joined
            threadToJoin.Join();

            lock (_stateLock)
            {
                if (_retiringThread == threadToJoin)
                {
                    _retiringThread = null;
                }
            }
        }
    }

    private void RunTimer(HighResQuickTickTimerRun run)
    {
        try
        {
            var stopWatch = Stopwatch.StartNew();
            var nextTriggerTicks = Interlocked.Read(ref _intervalTicks);
            var skippedIntervals = 0L;
            var lastFireTicks = 0L;

            while (!run.CancellationTokenSource.IsCancellationRequested)
            {
                while (true)
                {
                    var diffTicks = nextTriggerTicks - stopWatch.ElapsedTicks;
                    if (diffTicks <= 0)
                    {
                        break;
                    }

                    if (diffTicks >= QuickTickHelper.StopwatchTicksPerMillisecond * _sleepThreshold)
                    {
                        QuickTickTiming.MinimalSleep(run.Handles);
                    }
                    else if (diffTicks >= QuickTickHelper.StopwatchTicksPerMillisecond * _yieldThreshold)
                    {
                        Thread.Yield();
                    }
                    else
                    {
                        Thread.SpinWait(10);
                    }

                    if (run.CancellationTokenSource.IsCancellationRequested)
                    {
                        break;
                    }
                }

                if (run.CancellationTokenSource.IsCancellationRequested)
                {
                    break;
                }

                var currentTicks = stopWatch.ElapsedTicks;
                var lastFireTicksLocal = lastFireTicks;
                lastFireTicks = currentTicks;

                if (_autoReset)
                {
                    var interval = Interlocked.Read(ref _intervalTicks);
                    nextTriggerTicks += interval;

                    if (_skipMissedIntervals)
                    {
                        while (nextTriggerTicks < currentTicks)
                        {
                            nextTriggerTicks += interval;
                            if (skippedIntervals < long.MaxValue)
                            {
                                skippedIntervals++;
                            }
                        }
                    }

                    if (stopWatch.Elapsed.TotalHours >= 1)
                    {
                        var remaining = nextTriggerTicks - currentTicks;
                        stopWatch.Restart();
                        nextTriggerTicks = remaining;
                        lastFireTicks = 0L;
                    }
                }

                var timeSinceLastFire = TimeSpan.FromTicks(QuickTickHelper.StopwatchTicksToTimeSpanTicks(currentTicks - lastFireTicksLocal));
                var elapsedEventArgs = new QuickTickElapsedEventArgs(timeSinceLastFire, skippedIntervals);
                var handler = Elapsed;

                if (!_autoReset)
                {
                    _running = false; // Same logic as System.Timers.Timer: set running=false before invoking the handler when AutoReset is disabled
                    run.CancellationTokenSource.Cancel();
                    handler?.Invoke(this, elapsedEventArgs);
                    break;
                }

                handler?.Invoke(this, elapsedEventArgs);
            }
        }
        finally
        {
            lock (_stateLock)
            {
                // Unlink if the run ended on its own (AutoReset = false or a throwing handler); Stop() unlinks otherwise
                if (_currentRun == run)
                {
                    _currentRun = null;
                    _running = false;
                }
            }

            // Outside the lock on purpose: once the run is unlinked Stop() can no longer reach these objects,
            // so disposing them cannot race the Cancel() call that only targets the current run
            run.Dispose();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposedState, 1) == 1)
        {
            return;
        }

        Stop();
        Elapsed = null;
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposedState) == 1)
        {
            throw new ObjectDisposedException(nameof(HighResQuickTickTimer));
        }
    }
}
