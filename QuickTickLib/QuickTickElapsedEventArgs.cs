namespace QuickTickLib;

public class QuickTickElapsedEventArgs
{
    public TimeSpan TimeSinceLastTick { get; }
    public long SkippedTicks { get; }  

    public QuickTickElapsedEventArgs(TimeSpan timeSinceLastTick, long skippedTicks)
    {
        TimeSinceLastTick = timeSinceLastTick;
        SkippedTicks = skippedTicks;
    }
}
