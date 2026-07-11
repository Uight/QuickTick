namespace QuickTickLib;

// Default spin/sleep thresholds for HighResQuickTickTimer, chosen once per process for the sleep capability
// tier the platform lands on. The tiers matter because SleepThreshold must cover the worst-case overshoot of
// the tier's sleep mechanism; users can still override both values through the timer's properties.
internal static class HighResTimerDefaults
{
    internal static readonly float SleepThreshold;
    internal static readonly float YieldThreshold;

    // Minimum time before the deadline at which the long sleep-until-near-deadline block must wake, as a floor
    // under 2 x SleepThreshold. The long sleep mechanism is not the chunk sleep mechanism: where there is no
    // kernel timer the long block is a timed event wait with Thread.Sleep-like overshoot, so with sub-millisecond
    // thresholds 2 x SleepThreshold alone would be thinner than that overshoot and ticks would fire late.
    internal static readonly float LongSleepWakeMarginMs;

    static HighResTimerDefaults()
    {
        if (QuickTickTiming.IsQuickTickSupported)
        {
            // Windows waitable timers: extensive TimingReportGenerator testing showed MinimalSleep stays
            // under 1.5 ms in over 99% of all cases (with appropriate power settings)
            SleepThreshold = 1.5f;
            YieldThreshold = 0.75f;
            LongSleepWakeMarginMs = 0f; // The long sleep uses the same high-resolution kernel timer, 2 x SleepThreshold covers it
        }
        else if (QuickTickTiming.IsClockNanosleepSupported)
        {
            // From Linux testing with the 250 µs clock_nanosleep chunk; provisional until validated with TimingReportGenerator runs
            SleepThreshold = 0.5f;
            YieldThreshold = 0.3f;
            LongSleepWakeMarginMs = 3f; // Timed event waits stayed under 2 ms in Linux testing; 3 ms keeps a full millisecond of slack
        }
        else
        {
            // Thread.Sleep tier (e.g. macOS, where Sleep(1) can take 10+ ms): sleeping is only worth it far
            // from the deadline; 15 ms matches the guidance the README previously gave for manual tuning
            SleepThreshold = 15f;
            YieldThreshold = 0.75f;
            LongSleepWakeMarginMs = 15f; // Same overshoot class as the sleep itself
        }
    }
}
