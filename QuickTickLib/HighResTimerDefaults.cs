namespace QuickTickLib;

// Default spin/sleep thresholds for HighResQuickTickTimer, chosen once per process for the sleep capability
// tier the platform lands on. The tiers matter because SleepThreshold must cover the worst-case overshoot of
// the tier's sleep mechanism; users can still override both values through the timer's properties.
internal static class HighResTimerDefaults
{
    internal static readonly float SleepThreshold;
    internal static readonly float YieldThreshold;

    static HighResTimerDefaults()
    {
        if (QuickTickTiming.IsQuickTickSupported)
        {
            // Windows waitable timers: extensive TimingReportGenerator testing showed MinimalSleep stays
            // under 1.5 ms in over 99% of all cases (with appropriate power settings)
            SleepThreshold = 1.5f;
            YieldThreshold = 0.75f;
        }
        else if (QuickTickTiming.IsClockNanosleepSupported)
        {
            // clock_nanosleep typically overshoots by only tens of microseconds on default kernels;
            // provisional values until validated with TimingReportGenerator runs on Linux
            SleepThreshold = 1.0f;
            YieldThreshold = 0.5f;
        }
        else
        {
            // Thread.Sleep tier (e.g. macOS, where Sleep(1) can take 10+ ms): sleeping is only worth it far
            // from the deadline; 15 ms matches the guidance the README previously gave for manual tuning
            SleepThreshold = 15f;
            YieldThreshold = 0.75f;
        }
    }
}
