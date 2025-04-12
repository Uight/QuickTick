namespace QuickTickLib;

public interface IQuickTickTimer : IDisposable
{
    double Interval { get; set; }
    bool AutoReset { get; set; }

    event QuickTickElapsedEventHandler? Elapsed;

    void Start();
    void Stop();
}
