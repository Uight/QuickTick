// Some parts of the code are inspired by György Kőszeg's HighRes Timer: https://github.com/koszeggy/KGySoft.CoreLibraries/blob/master/KGySoft.CoreLibraries/CoreLibraries/HiResTimer.cs

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Timers;

namespace QuickTickLib;

internal sealed class QuickTickTimerFallback : IQuickTickTimer
{
    private readonly System.Timers.Timer _timer;
    private BlockingCollection<bool>? _eventQueue;
    private volatile bool _running;
    private volatile bool _skipMissedIntervals;
    private int _disposedState;
    private ThreadPriority _threadPriority = ThreadPriority.Normal;
    private CancellationTokenSource? _cancellationTokenSource;
    private Thread? _workerThread;
    private readonly object _stateLock = new();

    internal Thread? WorkerThreadForTests => _workerThread;

    public double Interval
    {
        get => _timer.Interval;
        set => _timer.Interval = value;
    }

    public bool AutoReset
    {
        get => _timer.AutoReset;
        set => _timer.AutoReset = value;
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
            if (_workerThread is { IsAlive: true })
            {
                _workerThread.Priority = value;
            }
        }
    }

    public event QuickTickElapsedEventHandler? Elapsed;

    public QuickTickTimerFallback(double interval)
    {
        _timer = new System.Timers.Timer(interval);
        _timer.Elapsed += OnElapsedInternal;
        _timer.AutoReset = true;
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
            var queue = new BlockingCollection<bool>();

            _workerThread = new Thread(() => Run(cts, queue))
            {
                IsBackground = true,
                Priority = Priority,
                Name = "QuickTick Timer"
            };

            _cancellationTokenSource = cts;
            _eventQueue = queue;
            _workerThread.Start();
            _timer.Start();
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
            _timer.Stop();
            _eventQueue?.CompleteAdding();

            if (Thread.CurrentThread != _workerThread)
            {
                _workerThread?.Join();
            }
            _workerThread = null;
        }
    }

    // Internal for tests: called directly to simulate the timer callback racing Stop() without System.Timers.Timer swallowing exceptions
    internal void OnElapsedInternal(object? sender, ElapsedEventArgs e)
    {
        // Use tryAdd because the timer could still fire after Stop() which would cause exception because Stop() also call CompleteAdding() on the queue
        _eventQueue?.TryAdd(true);
    }

    private void Run(CancellationTokenSource localCancellationTokenSource, BlockingCollection<bool> localEventQueue)
    {
        var stopWatch = Stopwatch.StartNew();
        var lastFireTicks = 0L;
        var skippedIntervals = 0L;

        while (!localCancellationTokenSource.IsCancellationRequested)
        {
            if (!localEventQueue.TryTake(out _, Timeout.Infinite))
            {
                break; // CompleteAdding was called
            }

            if (localCancellationTokenSource.IsCancellationRequested)
            {
                break;
            }

            var currentTicks = stopWatch.ElapsedTicks;
            var lastFireTicksLocal = lastFireTicks;
            lastFireTicks = currentTicks;

            if (AutoReset)
            {
                // If skipping is enabled, drain queue and only keep the latest
                if (_skipMissedIntervals && localEventQueue.Count > 0)
                {
                    while (localEventQueue.TryTake(out _))
                    {
                        if (skippedIntervals < long.MaxValue)
                        {
                            skippedIntervals++;
                        }
                    }
                }

                if (stopWatch.Elapsed.TotalHours >= 1)
                {
                    stopWatch.Restart();
                    lastFireTicks = 0;
                }
            }

            var timeSinceLastFire = TimeSpan.FromTicks(QuickTickHelper.StopwatchTicksToTimeSpanTicks(currentTicks - lastFireTicksLocal));
            var elapsedEventArgs = new QuickTickElapsedEventArgs(timeSinceLastFire, skippedIntervals);
            var handler = Elapsed;

            if (!AutoReset)
            {
                _running = false; // Same logic as System.Timers.Timer: set running=false before invoking the handler when AutoReset is disabled
                localCancellationTokenSource.Cancel();
                handler?.Invoke(this, elapsedEventArgs);
                break;
            }

            handler?.Invoke(this, elapsedEventArgs);
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
            _timer.Dispose();
            Elapsed = null;
        }
    }
}
