namespace QuickTickLib;

// Bundles everything a single high-res timer run owns: the cached sleep timer handles (where the platform
// has them), the stop signal, the cancellation source and the worker thread. Created by Start(); disposed
// by the worker thread itself on exit, after it unlinked the run under the state lock so Stop() can no
// longer reach these objects. The stop event only wakes the long sleep-until-near-deadline phase early;
// the short sleep/yield/spin phases never block longer than one MinimalSleep and just poll the token.
internal sealed class HighResQuickTickTimerRun : IDisposable
{
    // Null where the platform has no Win32 waitable timers (this timer runs everywhere);
    // the sleep phases fall back to clock_nanosleep or Thread.Sleep on their own then
    public readonly QuickTickHandleResources? Handles = QuickTickTiming.IsQuickTickSupported ? new QuickTickHandleResources() : null;
    public readonly ManualResetEvent StopEvent = new(false);
    public readonly CancellationTokenSource CancellationTokenSource = new();
    public Thread Thread = null!; // Set by Start() right after construction; the thread's loop closes over this run

    // Prebuilt for the long sleep phase so the timer loop stays allocation-free; null without a kernel timer
    public readonly WaitHandle[]? LongSleepWaitHandles;

    public HighResQuickTickTimerRun()
    {
        if (Handles != null)
        {
            LongSleepWaitHandles = new[] { Handles.TimerWaitHandle, StopEvent };
        }
    }

    public void Dispose()
    {
        StopEvent.Dispose();
        Handles?.Dispose();
        // The CancellationTokenSource is deliberately not disposed: without CancelAfter() and without accessing
        // its WaitHandle property it creates no unmanaged resources, so the GC reclaims it on its own. Disposing
        // it would only risk an ObjectDisposedException on a racing Cancel() for no gain.
    }
}
