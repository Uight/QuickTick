// Some parts of the code are inspired by György Kőszeg's HighRes Timer: https://github.com/koszeggy/KGySoft.CoreLibraries/blob/master/KGySoft.CoreLibraries/CoreLibraries/HiResTimer.cs

using System.Collections.Concurrent;
using System.Timers;

namespace QuickTickLib;

internal class QuickTickTimerFallback : IQuickTickTimer
{
    private readonly System.Timers.Timer timer;
    private readonly BlockingCollection<bool> eventQueue = [];
    private volatile bool running;
    private volatile bool skipMissedIntervals;
    private ThreadPriority threadPriority = ThreadPriority.Normal;
    private readonly Thread workerThread;
    private long skippedIntervals;
    private QuickTickElapsedEventHandler? elapsed;

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
            workerThread.Priority = value;
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

        workerThread = new Thread(Run)
        {
            IsBackground = true,
            Priority = Priority
        };

        workerThread.Start();
    }

    public void Start()
    {
        timer.Start();
        running = true;
    }

    public void Stop()
    {
        running = false;
        timer.Stop();
    }

    private void OnElapsedInternal(object? sender, ElapsedEventArgs e)
    {
        eventQueue.Add(true);
    }

    private void Run()
    {
        while (true)
        {
            try
            {
                // Wait for at least one callback
                if (!eventQueue.TryTake(out _, Timeout.Infinite))
                {
                    continue;
                }

                // If skipping is enabled, drain queue and only keep the latest
                if (skipMissedIntervals && eventQueue.Count > 0)
                {
                    while (eventQueue.TryTake(out _))
                    {
                        skippedIntervals++;
                    }
                }

                var elapsedEventArgs = new QuickTickElapsedEventArgs(TimeSpan.Zero, skippedIntervals);

                if (running)
                {
                    var handler = elapsed;
                    handler?.Invoke(this, elapsedEventArgs);
                }
            }
            catch (InvalidOperationException)
            {
                // Queue completed
                break;
            }
        }
    }

    public void Dispose()
    {
        timer.Dispose();
        running = false;
        eventQueue.CompleteAdding();
    }
}
