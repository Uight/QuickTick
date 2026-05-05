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
            if (!localEventQueue.TryTake(out _, Timeout.Infinite))
            {
                break; // CompleteAdding was called
            }

            if (!running)
            {
                break; // Stop() set running=false before CompleteAdding; item was already in queue
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
            var handler = elapsed;

            if (!AutoReset)
            {
                running = false;
                handler?.Invoke(this, elapsedEventArgs);
                break;
            }

            handler?.Invoke(this, elapsedEventArgs);
        }
    }

    public void Dispose()
    {
        timer.Dispose();
        running = false;
        eventQueue?.CompleteAdding();
    }
}
