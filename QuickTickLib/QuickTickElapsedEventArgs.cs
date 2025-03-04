namespace QuickTickLib;

public class QuickTickElapsedEventArgs
{
    public DateTime SignalTime { get; }       // Actual time the timer fired
    public DateTime ScheduledTime { get; }    // When the timer was supposed to fire

    public QuickTickElapsedEventArgs(DateTime signalTime, DateTime scheduledTime)
    {
        SignalTime = signalTime;
        ScheduledTime = scheduledTime;
    }
}
