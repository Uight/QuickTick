namespace QuickTickLib;

// Bundles everything a single timer run owns: the kernel timer handles, the stop signal, the cancellation
// source and the worker thread. Created by Start(); disposed by the worker thread itself on exit, after it
// unlinked the run under the state lock so Stop() can no longer reach these objects.
internal sealed class QuickTickTimerRun : IDisposable
{
    public readonly QuickTickHandleResources Handles = new();
    public readonly ManualResetEvent StopEvent = new(false);
    public readonly CancellationTokenSource CancellationTokenSource = new();
    public Thread Thread = null!; // Set by Start() right after construction; the thread's loop closes over this run

    public void Dispose()
    {
        StopEvent.Dispose();
        Handles.Dispose();
        // The CancellationTokenSource is deliberately not disposed: without CancelAfter() and without accessing
        // its WaitHandle property it creates no unmanaged resources, so the GC reclaims it on its own. Disposing
        // it would only risk an ObjectDisposedException on a racing Cancel() for no gain.
    }
}
