namespace QuickTickLib;

public sealed class QuickTickTimer : IQuickTickTimer
{
    private readonly IQuickTickTimer timer;

    public QuickTickTimer(double interval)
    {
        var isQuickTickSupported = QuickTickHelper.PlatformSupportsQuickTick();
        timer = isQuickTickSupported ? new QuickTickTimerImplementation(interval) : new QuickTickTimerFallback(interval);
    }

    public QuickTickTimer(TimeSpan interval) : this(interval.TotalMilliseconds) { }

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

    public event QuickTickElapsedEventHandler? Elapsed
    {
        add => timer.Elapsed += value;
        remove => timer.Elapsed -= value;
    }

    public void Start() => timer.Start();
    public void Stop() => timer.Stop();
    public void Dispose() => timer.Dispose();
}
