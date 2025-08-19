
namespace QuickTickLib;

public sealed class QuickTickTimer : IQuickTickTimer
{
    private readonly IQuickTickTimer timer;
    private readonly bool isQuickTickUsed;

    public QuickTickTimer(double interval)
    {
        isQuickTickUsed = QuickTickHelper.PlatformSupportsQuickTick();
        timer = isQuickTickUsed ? new QuickTickTimerImplementation(interval) : new QuickTickTimerFallback(interval);
    }

    public QuickTickTimer(TimeSpan interval) : this(interval.TotalMilliseconds) { }

    public bool IsQuickTickUsed => isQuickTickUsed;

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
        get => timer.SkipMissedIntervals; 
        set => timer.SkipMissedIntervals = value; 
    }

    public ThreadPriority Priority 
    { 
        get => timer.Priority; 
        set => timer.Priority = value; 
    }

    public event QuickTickElapsedEventHandler? Elapsed
    {
        add => timer.Elapsed += value;
        remove => timer.Elapsed -= value;
    }

    public void Start() => timer.Start();
    public void Stop() => timer.Stop();
    public void Dispose() => timer.Dispose();
}
