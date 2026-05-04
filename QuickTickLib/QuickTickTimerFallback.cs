// Some parts of the code are inspired by György Kőszeg's HighRes Timer: https://github.com/koszeggy/KGySoft.CoreLibraries/blob/master/KGySoft.CoreLibraries/CoreLibraries/HiResTimer.cs

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Timers;

namespace QuickTickLib;

internal sealed class QuickTickTimerFallback : IQuickTickTimer
{
    private readonly System.Timers.Timer timer;
    private BlockingCollection<bool>? eventQueue;
    private volatile bool running;
    private volatile bool skipMissedIntervals;
    private ThreadPriority threadPriority = ThreadPriority.Normal;
    private Thread? workerThread;
    private QuickTickElapsedEventHandler? elapsed;
    private readonly object stateLock = new();

    public double Interval
    {
        get => timer.Interval;
        set => timer.Interval = value;
    }

    public bool AutoReset
    {
        get => timer.AutoReset;
        set => timer.AutoReset = value;
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
            if (workerThread != null)
            {
                workerThread.Priority = value;
            }
        }
    }

    public event QuickTickElapsedEventHandler? Elapsed
    {
        add => elapsed += value;
        remove => elapsed -= value;
    }

    public QuickTickTimerFallback(double interval)
    {
        timer = new System.Timers.Timer(interval);
        timer.Elapsed += OnElapsedInternal;
        timer.AutoReset = true;
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
            eventQueue = [];

            workerThread = new Thread(() => Run(eventQueue))
            {
                IsBackground = true,
                Priority = Priority
            };

            workerThread.Start();
            timer.Start();
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
            timer.Stop();
            eventQueue?.CompleteAdding();

            if (Thread.CurrentThread != workerThread)
            {
                workerThread?.Join();
            }
            workerThread = null;
        }
    }

    private void OnElapsedInternal(object? sender, ElapsedEventArgs e)
    {
        eventQueue?.Add(true);
    }

    private void Run(BlockingCollection<bool> localEventQueue)
    {
        var stopWatch = Stopwatch.StartNew();
        var lastFireTicks = 0L;
        var skippedIntervals = 0L;

        while (running)
        {
            // Wait for at least one callback
            if (!localEventQueue.TryTake(out _, Timeout.Infinite))
            {
                break; // CompleteAdding was called
            }

            // If skipping is enabled, drain queue and only keep the latest
            if (skipMissedIntervals && localEventQueue.Count > 0)
            {
                while (localEventQueue.TryTake(out _))
                {
                    skippedIntervals++;
                }
            }

            var currentTicks = stopWatch.ElapsedTicks;
            var lastFireTicksLocal = lastFireTicks;
            lastFireTicks = currentTicks;

            if (stopWatch.Elapsed.TotalHours >= 1)
            {
                stopWatch.Restart();
                lastFireTicks = 0;
            }

            var timeSinceLastFire = TimeSpan.FromTicks(currentTicks - lastFireTicksLocal);
            var elapsedEventArgs = new QuickTickElapsedEventArgs(timeSinceLastFire, skippedIntervals);

            if (!AutoReset)
            {
                running = false; // Same logic as System.Timers.Timer: set running=false before invoking the handler when AutoReset is disabled
                var handler = elapsed;
                handler?.Invoke(this, elapsedEventArgs);
                break;
            }

            if (running)
            {
                var handler = elapsed;
                handler?.Invoke(this, elapsedEventArgs);
            }
        }
    }

    public void Dispose()
    {
        timer.Dispose();
        running = false;
        eventQueue?.CompleteAdding();
    }
}
