// Some parts of the code are inspired by György Kőszeg's HighRes Timer: https://github.com/koszeggy/KGySoft.CoreLibraries/blob/master/KGySoft.CoreLibraries/CoreLibraries/HiResTimer.cs

using System.Diagnostics;

namespace QuickTickLib;

public sealed class HighResQuickTickTimer : IQuickTickTimer
{
    private volatile bool autoReset;
    private volatile bool running;
    private volatile bool skipMissedIntervals;
    private volatile float intervalMs;
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
            intervalTicks = (long)(intervalMs * TimeSpan.TicksPerMillisecond);
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

                if (diffTicks >= TimeSpan.TicksPerMillisecond * 2)
                {
                    QuickTickTiming.MinimalSleep();
                }
                else if (diffTicks >= TimeSpan.TicksPerMillisecond)
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
