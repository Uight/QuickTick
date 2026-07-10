
namespace QuickTickLib;

public sealed class QuickTickTimer : IQuickTickTimer
{
    private readonly IQuickTickTimer _timer;
    private readonly bool _isQuickTickUsed;

    public QuickTickTimer(double interval)
    {
        QuickTickHelper.ThrowIfUnsupportedWindowsVersion();
        _isQuickTickUsed = QuickTickHelper.PlatformSupportsQuickTick();
        _timer = _isQuickTickUsed ? new QuickTickTimerImplementation(interval) : new QuickTickTimerFallback(interval);
    }

    public QuickTickTimer(TimeSpan interval) : this(interval.TotalMilliseconds) { }

    public bool IsQuickTickUsed => _isQuickTickUsed;

    public double Interval
    {
        get => _timer.Interval;
        set => _timer.Interval = value;
    }

    public bool AutoReset
    {
        get => _timer.AutoReset;
        set => _timer.AutoReset = value;
    }
    public bool SkipMissedIntervals 
    { 
        get => _timer.SkipMissedIntervals; 
        set => _timer.SkipMissedIntervals = value; 
    }

    public ThreadPriority Priority 
    { 
        get => _timer.Priority; 
        set => _timer.Priority = value; 
    }

    public event QuickTickElapsedEventHandler? Elapsed
    {
        add => _timer.Elapsed += value;
        remove => _timer.Elapsed -= value;
    }

    public void Start() => _timer.Start();
    public void Stop() => _timer.Stop();
    public void Dispose() => _timer.Dispose();
}
