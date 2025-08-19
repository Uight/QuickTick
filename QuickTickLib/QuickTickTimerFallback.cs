// Some parts of the code are inspired by György Kőszeg's HighRes Timer: https://github.com/koszeggy/KGySoft.CoreLibraries/blob/master/KGySoft.CoreLibraries/CoreLibraries/HiResTimer.cs

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Timers;

namespace QuickTickLib;

internal class QuickTickTimerFallback : IQuickTickTimer
{
    private readonly System.Timers.Timer timer;
    private BlockingCollection<bool>? eventQueue;
    private volatile bool running;
    private volatile bool skipMissedIntervals;
    private ThreadPriority threadPriority = ThreadPriority.Normal;
    private Thread? workerThread;
    private long skippedIntervals;
    private QuickTickElapsedEventHandler? elapsed;
    private readonly Stopwatch stopWatch = new();
    private long lastFireTicks;

    public double Interval
    {
        get => timer.Interval;
        set
        {
            timer.Interval = value;
        }
    }

    public bool AutoReset
    {
        get => timer.AutoReset;
        set => timer.AutoReset = value;
    }

    public bool SkipMissedIntervals 
    {
        get => skipMissedIntervals;
        set
        {
            skipMissedIntervals = value;
        }
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
        if (running)
        {
            return;
        }
        stopWatch.Restart();

        running = true;
        eventQueue = [];
        skippedIntervals = 0;
        lastFireTicks = 0;

        workerThread = new Thread(() => Run(eventQueue))
        {
            IsBackground = true,
            Priority = Priority
        };

        workerThread.Start();
        timer.Start();
    }

    public void Stop()
    {
        if (!running)
        {
            return;
        }

        timer.Stop();
        running = false;
        eventQueue?.CompleteAdding();
        stopWatch.Reset();
    }

    private void OnElapsedInternal(object? sender, ElapsedEventArgs e)
    {
        eventQueue?.Add(true);
    }

    private void Run(BlockingCollection<bool> localEventQueue)
    {
        while (running)
        {
            // Wait for at least one callback
            if (!localEventQueue.TryTake(out _, Timeout.Infinite))
            {
                break; // In this case the CompleteAdding was called
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

            if (running)
            {
                var handler = elapsed;
                handler?.Invoke(this, elapsedEventArgs);
            }

            if (!AutoReset)
            {
                running = false;
                stopWatch.Reset();
                break;
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
