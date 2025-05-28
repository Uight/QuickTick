namespace QuickTickLib;

public sealed class QuickTickTimer : IQuickTickTimer
{
    private readonly IQuickTickTimer timer;

    public QuickTickTimer(double interval, QuickTickElapsedEventHandler? elapsed = null, bool throwIfFallback = false)
    {
        var isQuickTickSupported = QuickTickHelper.PlatformSupportsQuickTick();
        if (!isQuickTickSupported && throwIfFallback) throw new PlatformNotSupportedException();
        timer = isQuickTickSupported ? new QuickTickTimerImplementation(interval) : new QuickTickTimerFallback(interval);
        Elapsed += elapsed;
    }

    public QuickTickTimer(TimeSpan interval, QuickTickElapsedEventHandler? elapsed = null, bool throwIfFallback = false) : this(interval.TotalMilliseconds, elapsed, throwIfFallback) { }

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
