using System.Runtime.Versioning;

namespace QuickTickLib;

[SupportedOSPlatform("windows")]
public static class QuickTickTimer
{
    public static IQuickTickTimer Create(double interval)
    {
        return new QuickTickTimerImplementation(interval);
    }

    public static IQuickTickTimer Create(TimeSpan interval)
    {
        return new QuickTickTimerImplementation(interval.TotalMilliseconds);
    }
}
