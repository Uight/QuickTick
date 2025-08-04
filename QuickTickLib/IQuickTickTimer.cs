namespace QuickTickLib;

public interface IQuickTickTimer : IDisposable
{
    double Interval { get; set; }
    bool AutoReset { get; set; }
    bool SkipMissedIntervals { get; set; }
    ThreadPriority Priority { get; set; }

    event QuickTickElapsedEventHandler? Elapsed;

    void Start();
    void Stop();
}
