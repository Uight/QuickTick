using System.Timers;

namespace QuickTickLib;

internal class QuickTickTimerFallback : IQuickTickTimer
{
    private readonly System.Timers.Timer timer;
    private DateTime nextFireTime;

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

    public event QuickTickElapsedEventHandler? Elapsed;

    public QuickTickTimerFallback(double interval)
    {
        timer = new System.Timers.Timer();
        Interval = interval;
        timer.Elapsed += OnElapsedInternal;
    }

    public void Start()
    {
        nextFireTime = DateTime.UtcNow.AddMilliseconds(Interval); // Compute first expiration
        timer.Start();
    }

    public void Stop()
    {
        timer.Stop();
    }

    private void OnElapsedInternal(object? sender, ElapsedEventArgs e)
    {
        var scheduledFireTime = nextFireTime;
        nextFireTime = nextFireTime.AddMilliseconds(Interval);
        Elapsed?.Invoke(this, new QuickTickElapsedEventArgs(DateTime.UtcNow, scheduledFireTime));
    }

    public void Dispose()
    {
        timer.Dispose();
    }
}
