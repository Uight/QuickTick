using System.Diagnostics;
using System.Runtime.InteropServices;
using NUnit.Framework;
using QuickTickLib;

namespace QuickTickTests;

// Lifecycle tests for Start/Stop/Dispose semantics, races and guards across all three timer implementations.
// Unlike TimerBehaviorTests these are logic tests without tight timing assertions, so the fallback
// implementation is exercised on every platform here, not only on non-Windows.
public class TimerLifecycleTests
{
    private const double IntervalMs = 15;

    private static IEnumerable<string> AllTimerKinds()
    {
        yield return "fallback";
        yield return "highResolution";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // QuickTickTimerImplementation P/Invokes kernel32 in its constructor and can only run on Windows.
            // Deliberately last: its Start_AfterDispose failure mode is a process crash which would otherwise
            // hide the results of the other kinds (see the note on that test).
            yield return "implementation";
        }
    }

    private static IQuickTickTimer CreateTimer(string kind, double intervalMs = IntervalMs) => kind switch
    {
        "implementation" => new QuickTickTimerImplementation(intervalMs),
        "fallback"       => new QuickTickTimerFallback(intervalMs),
        "highResolution" => new HighResQuickTickTimer(intervalMs),
        _                => throw new ArgumentException($"Unknown timer kind: {kind}", nameof(kind))
    };

    private static Thread? GetWorkerThread(IQuickTickTimer timer) => timer switch
    {
        QuickTickTimerImplementation impl => impl.WorkerThreadForTests,
        QuickTickTimerFallback fallback   => fallback.WorkerThreadForTests,
        HighResQuickTickTimer highRes     => highRes.WorkerThreadForTests,
        _                                 => throw new ArgumentException($"Unknown timer type: {timer.GetType()}", nameof(timer))
    };

    [TestCaseSource(nameof(AllTimerKinds))]
    public void MultipleStops_DoNotThrow(string kind)
    {
        using var timer = CreateTimer(kind);
        timer.Start();
        timer.Stop();
        timer.Stop();
        timer.Stop();
    }

    [TestCaseSource(nameof(AllTimerKinds))]
    [Platform(Exclude = "MacOsX", Reason = "the fire-count range needs predictable timing which macOS does not provide")]
    public void MultipleStarts_AreIgnored(string kind)
    {
        using var timer = CreateTimer(kind);
        var count = 0;
        timer.Elapsed += (_, _) => count++;

        timer.Start();
        var workerAfterFirstStart = GetWorkerThread(timer);
        Assert.That(workerAfterFirstStart, Is.Not.Null, $"{kind}: Start() should have spawned a worker thread");

        timer.Start();
        timer.Start();
        timer.Start();
        timer.Start();

        var workerAfterMultipleStarts = GetWorkerThread(timer);
        Assert.That(ReferenceEquals(workerAfterFirstStart, workerAfterMultipleStarts), Is.True, $"{kind}: subsequent Start() calls replaced the worker thread instead of being ignored");
        Assert.That(workerAfterFirstStart!.IsAlive, Is.True, $"{kind}: subsequent Start() calls killed the running worker thread");

        Thread.Sleep(500); // Run for ~500ms
        timer.Stop();

        Assert.That(count, Is.InRange(14, 37)); // One timer should run only, still expect ~32 fires
    }

    [TestCaseSource(nameof(AllTimerKinds))]
    public void StartAfterStop_FiresAgain(string kind)
    {
        using var timer = CreateTimer(kind);
        var count = 0;
        timer.Elapsed += (_, _) => count++;

        timer.Start();
        Thread.Sleep(100);
        timer.Stop();
        var countFirstRun = count;
        Assert.That(countFirstRun, Is.GreaterThan(0), $"{kind}: expected fires during first run");

        timer.Start();
        Thread.Sleep(100);
        timer.Stop();
        Assert.That(count, Is.GreaterThan(countFirstRun), $"{kind}: expected fires during second run");
    }

    [TestCaseSource(nameof(AllTimerKinds))]
    public void StopFromHandler_DoesNotDeadlock(string kind)
    {
        using var timer = CreateTimer(kind);
        var done = new ManualResetEventSlim(false);
        timer.Elapsed += (sender, _) =>
        {
            ((IQuickTickTimer)sender!).Stop();
            done.Set();
        };
        timer.Start();
        Assert.That(done.Wait(TimeSpan.FromMilliseconds(200)), Is.True, $"{kind}: Stop() from handler deadlocked or timed out");
    }

    [TestCaseSource(nameof(AllTimerKinds))]
    public void AutoResetFalse_StartFromHandler_RestartsTimer(string kind)
    {
        using var timer = CreateTimer(kind);
        var fireCount = 0;
        var secondFired = new ManualResetEventSlim(false);

        timer.AutoReset = false;
        timer.Elapsed += (sender, _) =>
        {
            fireCount++;
            if (fireCount == 1)
            {
                ((IQuickTickTimer)sender!).Start();
            }
            else
            {
                secondFired.Set();
            }
        };

        timer.Start();
        Assert.That(secondFired.Wait(TimeSpan.FromMilliseconds(500)), Is.True, $"{kind}: Start() from AutoReset=false handler should restart the timer");
        timer.Stop();
    }

    [TestCaseSource(nameof(AllTimerKinds))]
    public void Stop_BlocksUntilCurrentHandlerCompletes(string kind)
    {
        using var timer = CreateTimer(kind);
        var handlerStarted  = new ManualResetEventSlim(false);
        var handlerFinished = new ManualResetEventSlim(false);

        timer.Elapsed += (_, _) =>
        {
            handlerStarted.Set();
            Thread.Sleep(150);
            handlerFinished.Set();
        };

        timer.Start();
        Assert.That(handlerStarted.Wait(TimeSpan.FromMilliseconds(500)), Is.True, "handler should start within 500 ms");

        timer.Stop();
        Assert.That(handlerFinished.IsSet, Is.True, $"{kind}: Stop() must block until the handler completes (thread join)");
    }

    [TestCaseSource(nameof(AllTimerKinds))]
    public void Stop_DuringLongWaitForDistantDeadline_ReturnsPromptly(string kind)
    {
        // With a 10 s interval the worker sits in its long wait (kernel timer wait, blocking take, or the
        // high-res timer's sleep-until-near-deadline); Stop() must wake that wait instead of blocking in
        // the worker join until the full interval runs out
        using var timer = CreateTimer(kind, 10_000);
        timer.Start();
        Thread.Sleep(100); // Let the worker enter its long wait

        var sw = Stopwatch.StartNew();
        timer.Stop();
        sw.Stop();

        Assert.That(sw.Elapsed.TotalMilliseconds, Is.LessThan(1000), $"{kind}: Stop() did not interrupt the long wait promptly");
    }

    [TestCaseSource(nameof(AllTimerKinds))]
    public void Dispose_StopsTimer(string kind)
    {
        var timer = CreateTimer(kind);
        var count = 0;
        var firstFired = new ManualResetEventSlim(false);
        timer.Elapsed += (_, _) => { count++; firstFired.Set(); };
        timer.Start();
        Assert.That(firstFired.Wait(TimeSpan.FromMilliseconds(500)), Is.True);

        var workerThread = GetWorkerThread(timer);
        Assert.That(workerThread, Is.Not.Null, $"{kind}: Start() should have spawned a worker thread");

        timer.Dispose();
        var countAtDispose = count;
        Thread.Sleep(150);
        Assert.That(count, Is.EqualTo(countAtDispose));

        Assert.That(workerThread!.IsAlive, Is.False, $"{kind}: Dispose() left the worker thread running");
    }

    [TestCaseSource(nameof(AllTimerKinds))]
    public void Stop_RacingStopCalledFromHandler_DoesNotDeadlock(string kind)
    {
        // Deterministic repro of the deadlock: thread A calls Stop(), takes the state lock and blocks in
        // the worker join; meanwhile the worker is inside the Elapsed handler and calls Stop() itself,
        // which blocks on the state lock held by A. A joins a thread that waits on A's lock forever.
        var timer = CreateTimer(kind);
        var inHandler = new ManualResetEventSlim(false);
        var handlerStopReturned = new ManualResetEventSlim(false);
        var firstCall = 1;

        timer.Elapsed += (sender, _) =>
        {
            if (Interlocked.Exchange(ref firstCall, 0) == 0)
            {
                return;
            }

            inHandler.Set();
            Thread.Sleep(150); // Give the foreign thread time to enter Stop() and block in the worker join
            ((IQuickTickTimer)sender!).Stop();
            handlerStopReturned.Set();
        };

        timer.Start();
        Assert.That(inHandler.Wait(TimeSpan.FromMilliseconds(500)), Is.True, $"{kind}: timer did not fire");

        // The timer is passed as task state instead of being captured: the closure would otherwise be flagged
        // for capturing a variable that is disposed in the outer scope
        var foreignStop = Task.Factory.StartNew(state => ((IQuickTickTimer)state!).Stop(), timer);

        Assert.That(foreignStop.Wait(TimeSpan.FromSeconds(3)), Is.True, $"{kind}: Stop() deadlocked against Stop() called from the Elapsed handler");
        Assert.That(handlerStopReturned.Wait(TimeSpan.FromSeconds(1)), Is.True, $"{kind}: Stop() called from the handler never returned");

        // No 'using': disposing a deadlocked timer would hang the test on failure, so only dispose on success
        timer.Dispose();
    }

    [TestCaseSource(nameof(AllTimerKinds))]
    public void Start_AfterDispose_ThrowsObjectDisposedException(string kind)
    {
        // NOTE while this test is red: for "implementation" it does not merely fail — the started worker
        // thread hits the disposed timer handle and the unhandled exception terminates the whole test
        // process. That process crash is exactly the bug this test guards against. Run it isolated via
        // --filter until the fix is in.
        var timer = CreateTimer(kind);
        timer.Start();
        timer.Dispose();

        Assert.Throws<ObjectDisposedException>(timer.Start, $"{kind}: Start() after Dispose() must throw synchronously");

        var worker = GetWorkerThread(timer);
        Assert.That(worker is { IsAlive: true }, Is.False, $"{kind}: Start() after Dispose() spawned or left a live worker thread");
    }

    [TestCaseSource(nameof(AllTimerKinds))]
    public void StopAndDispose_AfterDispose_AreNoOps(string kind)
    {
        var timer = CreateTimer(kind);
        timer.Start();
        timer.Dispose();

        Assert.DoesNotThrow(timer.Stop, $"{kind}: Stop() on a disposed timer must be a silent no-op");
        Assert.DoesNotThrow(timer.Dispose, $"{kind}: double Dispose() must be a silent no-op");
    }

    [TestCaseSource(nameof(AllTimerKinds))]
    public void Dispose_FromHandler_CompletesAndWorkerExits(string kind)
    {
        var timer = CreateTimer(kind);
        var disposedFromHandler = new ManualResetEventSlim(false);
        var worker = new Thread?[1];

        timer.Elapsed += (sender, _) =>
        {
            if (disposedFromHandler.IsSet)
            {
                return;
            }

            var self = (IQuickTickTimer)sender!;
            worker[0] = GetWorkerThread(self);
            self.Dispose();
            disposedFromHandler.Set();
        };

        timer.Start();
        Assert.That(disposedFromHandler.Wait(TimeSpan.FromMilliseconds(500)), Is.True, $"{kind}: Dispose() from the handler did not complete");

        Assert.That(worker[0], Is.Not.Null, $"{kind}: worker thread was not captured in the handler");
        var workerExited = SpinWait.SpinUntil(() => !worker[0]!.IsAlive, TimeSpan.FromSeconds(1));
        Assert.That(workerExited, Is.True, $"{kind}: worker thread did not exit after Dispose() from the handler");
    }

    [TestCaseSource(nameof(AllTimerKinds))]
    public void ConcurrentStartStop_FromTwoThreads_TimerRemainsFunctional(string kind)
    {
        // Guards the Stop() teardown against Start() sneaking in mid-teardown (two workers sharing one timer)
        var timer = CreateTimer(kind);

        // The timer is passed as task state instead of being captured, see Stop_RacingStopCalledFromHandler_DoesNotDeadlock
        var end = DateTime.UtcNow.AddMilliseconds(300);
        var starter = Task.Factory.StartNew(state => { while (DateTime.UtcNow < end) { ((IQuickTickTimer)state!).Start(); } }, timer);
        var stopper = Task.Factory.StartNew(state => { while (DateTime.UtcNow < end) { ((IQuickTickTimer)state!).Stop(); } }, timer);

        Assert.That(Task.WaitAll([starter, stopper], TimeSpan.FromSeconds(10)), Is.True, $"{kind}: concurrent Start()/Stop() hammering hung");

        timer.Stop();
        var fired = new ManualResetEventSlim(false);
        timer.Elapsed += (_, _) => fired.Set();
        timer.Start();
        Assert.That(fired.Wait(TimeSpan.FromMilliseconds(500)), Is.True, $"{kind}: timer no longer fires after concurrent Start()/Stop() hammering");

        // No 'using': the hammering tasks only end via the assert timeout when they hang, in which case
        // disposing the timer would hang the test as well, so only dispose on success
        timer.Dispose();
    }

    [Test]
    public void Fallback_ElapsedCallbackRacingStop_DoesNotThrow()
    {
        // System.Timers.Timer can still deliver a queued callback after Stop(). In production that callback
        // runs inside System.Timers.Timer, which silently swallows exceptions from Elapsed handlers — this
        // calls the callback directly so a throw actually surfaces.
        using var timer = new QuickTickTimerFallback(IntervalMs);
        timer.Start();
        timer.Stop(); // Calls CompleteAdding() on the event queue
        
        // ReSharper disable once AccessToDisposedClosure
        Assert.DoesNotThrow(() => timer.OnElapsedInternal(null, null!), "fallback: the timer callback must tolerate racing Stop()");
    }
}
