namespace QuickTickLib;

public class QuickTickElapsedEventArgs
{
    public TimeSpan TimeSinceLastTick { get; }
    public long SkippedIntervals { get; }  

    public QuickTickElapsedEventArgs(TimeSpan timeSinceLastTick, long skippedIntervals)
    {
        TimeSinceLastTick = timeSinceLastTick;
        SkippedIntervals = skippedIntervals;
    }
}
