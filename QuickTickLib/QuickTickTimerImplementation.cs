// Some parts of the code are inspired by György Kőszeg's HighRes Timer: https://github.com/koszeggy/KGySoft.CoreLibraries/blob/master/KGySoft.CoreLibraries/CoreLibraries/HiResTimer.cs

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace QuickTickLib;

internal sealed class QuickTickTimerImplementation : IQuickTickTimer
{
    private volatile bool _autoReset;
    private volatile bool _skipMissedIntervals;
    private volatile bool _running;
    private double _intervalMs;
    private long _intervalTicks;

    private int _disposedState;
    private readonly object _stateLock = new();

    private QuickTickTimerRun? _currentRun;
    private Thread? _retiringThread;
    private ThreadPriority _threadPriority = ThreadPriority.Normal;

    internal Thread? WorkerThreadForTests => _currentRun?.Thread;

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
            if (_currentRun?.Thread is { IsAlive: true } workerThread)
            {
                workerThread.Priority = value;
            }
        }
    }

    public QuickTickTimerImplementation(double interval)
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
            var run = new QuickTickTimerRun();
            run.Thread = new Thread(() => WorkerThreadLoop(run))
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
                run.StopEvent.Set(); // Wake the worker thread out of its wait
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

    private void WorkerThreadLoop(QuickTickTimerRun run)
    {
        try
        {
            // The timer is a synchronization (auto-reset) timer owned exclusively by this run: a satisfied wait
            // resets it, so every SetTimer/WaitAny pair consumes exactly one signal and no other run can interfere
            var waitHandles = new[] { run.Handles.TimerWaitHandle, run.StopEvent };

            var stopWatch = Stopwatch.StartNew();
            var nextFireTicks = Interlocked.Read(ref _intervalTicks);
            var lastFireTicks = 0L;
            var skippedIntervals = 0L;

            SetTimer(run, stopWatch, nextFireTicks);

            while (!run.CancellationTokenSource.IsCancellationRequested)
            {
                var signaledIndex = WaitHandle.WaitAny(waitHandles);

                if (run.CancellationTokenSource.IsCancellationRequested || signaledIndex != 0)
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

                    SetTimer(run, stopWatch, nextFireTicks);
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
            // so disposing them cannot race the Cancel()/Set() calls that only target the current run
            run.Dispose();
        }
    }

    private static void SetTimer(QuickTickTimerRun run, Stopwatch stopWatch, long nextFireTicks)
    {
        long dueTimeStopwatchTicks = nextFireTicks - stopWatch.ElapsedTicks;
        long dueTime = dueTimeStopwatchTicks < 0 ? 0 : QuickTickHelper.StopwatchTicksToTimeSpanTicks(dueTimeStopwatchTicks);
        dueTime = -dueTime; // Negative = relative time for SetWaitableTimer, which expects 100ns units

        if (!Win32Interop.SetWaitableTimer(run.Handles.TimerHandle, ref dueTime, 0, IntPtr.Zero, IntPtr.Zero, false)) // Setting the period to 0 makes the timer fire exactly once
        {
            // With the Start()-after-Dispose guard in place this cannot realistically fail (valid handle, valid arguments).
            // If it ever does, the exception propagates on the worker thread where user code cannot catch it — deliberate,
            // matching the documented behavior of exceptions thrown from Elapsed handlers.
            throw new InvalidOperationException($"SetWaitableTimer failed: {Marshal.GetLastWin32Error()}");
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposedState, 1) == 1)
        {
            return;
        }

        // All kernel resources are owned by the runs and disposed by their worker threads; there is nothing else to release
        Stop();
        Elapsed = null;
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposedState) == 1)
        {
            throw new ObjectDisposedException(GetType().Name);
        }
    }
}
