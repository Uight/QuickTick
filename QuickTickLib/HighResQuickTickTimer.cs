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
    private volatile float sleepThreshold = 1.5f; // This is a value that works on Ubuntu and Windows with appropriate power settings. Getting this value was done by extensivly testing with the TimingReportGenerator
    private volatile float yieldThreshold = 0.75f; // This is a value that works on Ubuntu and Windows with appropriate power settings. Getting this value was done by extensivly testing with the TimingReportGenerator
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
    /// Defines the minimum time that must be available towards the next timer iteration for the thread to sleep.
    /// Increasing this time can lead to better timing but increases CPU usage as the code will Yield or SpinWait instead.
    /// Must be at least 1.0 and at most int.MaxValue.
    /// YieldThreshold must always be less than or equal to SleepThreshold.
    /// Setting SleepThreshold to int.MaxValue will basically disable sleeping the thread and the timer will Yield or SpinWait the thread instead.
    /// </summary>
    public double SleepThreshold
    {
        get => sleepThreshold;
        set
        {
            if (value < 1.0 || value > int.MaxValue || double.IsNaN(value))
            {
                throw new ArgumentOutOfRangeException("SleepThreshold must be greater than or equal to 1.0 and smaller than int.MaxValue.");
            }

            if (yieldThreshold > value)
            {
                throw new ArgumentOutOfRangeException("SleepThreshold must be greater than or equal to YieldThreshold.");
            }

            sleepThreshold = (float)value;
        }
    }

    /// <summary>
    /// Defines the minimum time that must be available towards the next timer iteration to yield the thread.
    /// Increasing this time can lead to better timing but increases CPU usage as the code will SpinWait instead.
    /// Must be at least 0.0 and at most the value of SleepThreshold. 
    /// Setting YieldThreshold equal to SleepThreshold disables yielding (goes directly to spin wait).
    /// Setting YieldThreshold equal to 0.0 will basically disable spin waiting altough if no process is ready to run on this thread the behavior is almost the same.
    /// </summary>
    public double YieldThreshold
    {
        get => yieldThreshold;
        set
        {
            if (value < 0.0 || value > sleepThreshold || double.IsNaN(value))
            {
                throw new ArgumentOutOfRangeException("YieldThreshold must be greater than or equal to 0.0 and less than or equal to SleepThreshold.");
            }

            yieldThreshold = (float)value;
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
                else if (diffTicks >= ticksPerMillisecond * yieldThreshold)
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
