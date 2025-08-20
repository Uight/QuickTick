// Some parts of the code are inspired by György Kőszeg's HighRes Timer: https://github.com/koszeggy/KGySoft.CoreLibraries/blob/master/KGySoft.CoreLibraries/CoreLibraries/HiResTimer.cs

using System.Diagnostics;

namespace QuickTickLib;

public sealed class HighResQuickTickTimer : IQuickTickTimer
{
    private readonly long ticksPerMillisecond = Stopwatch.Frequency / 1000;

    private volatile bool autoReset;
    private volatile bool running;
    private volatile bool skipMissedIntervals;
    private volatile float intervalMs;
    private volatile float sleepThreshold = 2.0f; // 2.0f is a solid but conservative value aimed at the lowest possible cpu usage for this timer.
                                                  // 2.5f is a value that would use a bit more cpu but would safely hold the specified time in over 99.9% of all cases (tested on 3 different machines).
    private long intervalTicks;
    private ThreadPriority threadPriority = ThreadPriority.Highest;
    private CancellationTokenSource? cancellationTokenSource;
    private Thread? workerThread;
    private QuickTickElapsedEventHandler? elapsed;

    public double Interval
    {
        get => intervalMs;
        set
        {
            if (value > int.MaxValue || value < 0.5 || double.IsNaN(value))
            {
                throw new ArgumentOutOfRangeException("Interval must be between 0.5 and int.MaxValue");
            }

            intervalMs = (float)value;
            intervalTicks = (long)(intervalMs * ticksPerMillisecond);
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

    /// <summary>
    /// Defines the time that must be available towards the next timer iteration before the system starts to sleep.
    /// Increasing this time can lead to better timing but increases CPU usage as the code will then SpinWait instead.
    /// </summary>
    public double SleepThreshold
    {
        get => sleepThreshold;
        set
        {
            if (value > int.MaxValue || value <= 1.0 || double.IsNaN(value))
            {
                throw new ArgumentOutOfRangeException("SleepThreshold must be greater than 1.0 and smaller than int.MaxValue");
            }

            sleepThreshold = (float)value;
        }
    }

    public HighResQuickTickTimer(double interval)
    {
        AutoReset = true;
        Interval = interval;
    }

    public void Start()
    {
        if (running)
        {
            return;
        }

        running = true;
        var cts = new CancellationTokenSource();

        workerThread = new Thread(() => RunTimer(cts))
        {
            IsBackground = true,
            Priority = Priority
        };

        cancellationTokenSource = cts;
        workerThread.Start();
    }

    public void Stop()
    {
        if (!running)
        {
            return;
        }

        running = false;
        cancellationTokenSource?.Cancel();
    }

    private void RunTimer(CancellationTokenSource cancellationTokenSource)
    {
        var stopWatch = new Stopwatch();
        stopWatch.Start();
        var nextTriggerTicks = intervalTicks;
        var skippedIntervals = 0L;
        var lastFireTicks = 0L;

        while (!cancellationTokenSource.IsCancellationRequested)
        {      
            while (true)
            {                
                var diffTicks = nextTriggerTicks - stopWatch.ElapsedTicks;
                if (diffTicks <= 0)
                {
                    break;
                }

                if (diffTicks >= ticksPerMillisecond * sleepThreshold)
                {
                    QuickTickTiming.MinimalSleep();
                }
                else if (diffTicks >= ticksPerMillisecond)
                {
                    Thread.Yield();
                }
                else
                {
                    Thread.SpinWait(10);
                }

                if (cancellationTokenSource.IsCancellationRequested)
                {
                    stopWatch.Reset();
                    return;
                }                 
            }

            nextTriggerTicks += intervalTicks;
            var currentTicks = stopWatch.ElapsedTicks;
            var lastFireTicksLocal = lastFireTicks;
            lastFireTicks = currentTicks;

            if (skipMissedIntervals)
            {
                while (nextTriggerTicks < currentTicks)
                {
                    nextTriggerTicks += intervalTicks;
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

            var timeSinceLastFire = TimeSpan.FromTicks(currentTicks - lastFireTicksLocal);
            var elapsedEventArgs = new QuickTickElapsedEventArgs(timeSinceLastFire, skippedIntervals);

            if (!cancellationTokenSource.IsCancellationRequested)
            {
                var handler = elapsed;
                handler?.Invoke(this, elapsedEventArgs);
            }

            if (!autoReset)
            {
                running = false;
                stopWatch.Reset();
                break;
            }
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
