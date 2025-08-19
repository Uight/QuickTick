namespace QuickTickLib;

public class QuickTickElapsedEventArgs
{
    public TimeSpan TimeSinceLastInterval { get; }
    public long SkippedIntervals { get; }  

    public QuickTickElapsedEventArgs(TimeSpan timeSinceLastInterval, long skippedIntervals)
    {
        TimeSinceLastInterval = timeSinceLastInterval;
        SkippedIntervals = skippedIntervals;
    }
}
