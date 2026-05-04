using System.Collections.Concurrent;
using System.Diagnostics;
using NUnit.Framework;
using QuickTickLib;

namespace QuickTickTests;

// Tests run against all three timer implementations to verify they behave consistently.
// These tests should verify function not performance
[Platform(Include = "Win", Reason = "Timer implementations rely on Win32 APIs or Windows timing behavior")]
public class TimerBehaviorTests
{
    // Use default timing of windows (15.625) so tests are mostly predictable even with fallback timer
    // Actually stay just below the default for windows because using it exactly can lead to the fallback timer waiting double very often
    private const double IntervalMs = 15; 

    private static IEnumerable<string> AllTimerKinds() =>
    [
        "implementation",
        "fallback",
        "highResolution",
    ];

    private static IQuickTickTimer CreateTimer(string kind) => kind switch
    {
        "implementation" => new QuickTickTimerImplementation(IntervalMs),
        "fallback"       => new QuickTickTimerFallback(IntervalMs),
        "highResolution" => new HighResQuickTickTimer(IntervalMs),
        _                => throw new ArgumentException($"Unknown timer kind: {kind}", nameof(kind))
    };
    
    [TestCaseSource(nameof(AllTimerKinds))]
    public void Basic_Timer_Creation(string kind)
    {
        using var timer = CreateTimer(kind);
        var fired = new ManualResetEventSlim(false);
        timer.Elapsed += (_, _) => fired.Set();
        timer.Start();
        Assert.That(fired.Wait(TimeSpan.FromMilliseconds(200)), Is.True, $"{kind}: timer did not fire within 200 ms");
        timer.Stop();
    }

    [TestCaseSource(nameof(AllTimerKinds))]
    public void AutoReset_FiresMultipleTimes(string kind)
    {
        using var timer = CreateTimer(kind);
        var count = 0;
        timer.Elapsed += (_, _) => count++;
        timer.AutoReset = true;
        timer.Start();
        Thread.Sleep(500); // ~32 fires expected at 20 ms
        timer.Stop();
        Assert.That(count, Is.InRange(14, 37));
    }
    
    [TestCaseSource(nameof(AllTimerKinds))]
    public void AutoResetFalse_FiresExactlyOnce(string kind)
    {
        using var timer = CreateTimer(kind);
        var count = 0;
        timer.Elapsed += (_, _) => count++;
        timer.AutoReset = false;
        timer.Start();
        Thread.Sleep(500); // Extra time for any rogue second fire
        Assert.That(count, Is.EqualTo(1));
    }

    // --- Stop / Start lifecycle ---

    [TestCaseSource(nameof(AllTimerKinds))]
    public void Stop_PreventsSubsequentFires(string kind)
    {
        using var timer = CreateTimer(kind);
        var count = 0;
        var firstFired = new ManualResetEventSlim(false);
        timer.Elapsed += (_, _) => { count++; firstFired.Set(); };
        timer.Start();
        Assert.That(firstFired.Wait(TimeSpan.FromMilliseconds(500)), Is.True);
        timer.Stop();
        Thread.Sleep(150); // Give extra time for additional fires that shouldn't happen
        Assert.That(count, Is.EqualTo(1));
    }

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
    public void MultipleStarts_AreIgnored(string kind)
    {
        using var timer = CreateTimer(kind);
        var count = 0;
        timer.Elapsed += (_, _) => count++;

        var proc = Process.GetCurrentProcess();
        proc.Refresh();
        var threadsBefore = proc.Threads.Count;

        timer.Start();
        timer.Start();
        timer.Start();
        timer.Start();
        timer.Start();

        Thread.Sleep(50); // Let spawned threads settle before counting
        proc.Refresh();
        var threadsAfterMultipleStarts = proc.Threads.Count;

        Thread.Sleep(450); // Run for total ~500ms
        timer.Stop();

        Assert.That(count, Is.InRange(14, 37)); // One timer should run only, still expect ~32 fires

        Assert.That(threadsAfterMultipleStarts - threadsBefore, Is.LessThanOrEqualTo(1), $"{kind}: multiple Start() calls spawned more than 1 new thread");
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
        timer.Elapsed += (_, _) =>
        {
            // ReSharper disable once AccessToDisposedClosure
            timer.Stop();
            done.Set();
        };
        timer.Start();
        Assert.That(done.Wait(TimeSpan.FromMilliseconds(200)), Is.True, $"{kind}: Stop() from handler deadlocked or timed out");
    }
    
    [TestCaseSource(nameof(AllTimerKinds))]
    public void Dispose_StopsTimer(string kind)
    {
        var proc = Process.GetCurrentProcess();
        proc.Refresh();
        var threadsBeforeTimerCreation = proc.Threads.Count;
        
        var timer = CreateTimer(kind);
        var count = 0;
        var firstFired = new ManualResetEventSlim(false);
        timer.Elapsed += (_, _) => { count++; firstFired.Set(); };
        timer.Start();
        Assert.That(firstFired.Wait(TimeSpan.FromMilliseconds(500)), Is.True);
        timer.Dispose();
        var countAtDispose = count;
        Thread.Sleep(150);
        Assert.That(count, Is.EqualTo(countAtDispose));
        
        proc.Refresh();
        var threadsAfterDispose = proc.Threads.Count;
        Assert.That(threadsAfterDispose - threadsBeforeTimerCreation, Is.Zero, $"{kind}: Creating and disposing a timer left a ghost thread");
    }
    
    [TestCaseSource(nameof(AllTimerKinds))]
    public void SkipMissedIntervals_ReportsSkippedCount(string kind)
    {
        using var timer = CreateTimer(kind);
        var history = new ConcurrentQueue<long>();
        timer.Elapsed += (_, args) =>
        {
            history.Enqueue(args.SkippedIntervals);
            Thread.Sleep(50);
        };
        timer.SkipMissedIntervals = true;
        timer.Start();
        Thread.Sleep(400);
        timer.Stop();

        var values = history.ToArray();
        
        Assert.That(values, Has.Some.GreaterThan(0L), "skipped count should grow above zero");
        for (var i = 1; i < values.Length; i++)
        {
            Assert.That(values[i], Is.GreaterThanOrEqualTo(values[i - 1]), $"SkippedIntervals decreased at tick {i - 1}→{i} ({values[i - 1]}→{values[i]})");
        }
    }

    [TestCaseSource(nameof(AllTimerKinds))]
    public void SkipMissedIntervals_ResetsToZeroOnRestart(string kind)
    {
        using var timer = CreateTimer(kind);
        var skips = new ConcurrentQueue<long>();
        var inFirstRun = new[] { true }; 

        timer.Elapsed += (_, args) =>
        {
            skips.Enqueue(args.SkippedIntervals);
            if (inFirstRun[0])
            {
                Thread.Sleep(30); // Accumulate skips during the first run
            }
        };
        timer.SkipMissedIntervals = true;

        timer.Start();
        Thread.Sleep(200);
        timer.Stop();
        
        Assert.That(skips, Has.Some.GreaterThan(0L), "skipped count should grow above zero");
        
        while (skips.Any())
        {
            skips.TryDequeue(out _);
        }
        
        inFirstRun[0] = false;
        
        timer.Start();
        Thread.Sleep(200);
        timer.Stop();

        Assert.That(skips, Has.Count.GreaterThan(0L));
        skips.TryDequeue(out var firstSkipAfterRestart);
        Assert.That(firstSkipAfterRestart, Is.EqualTo(0L), $"{kind}: SkippedIntervals should reset to 0 after Stop()+Start()");
    }

    [TestCaseSource(nameof(AllTimerKinds))]
    public void SkipMissedIntervalsDisabled_CatchesUpMissedTicks(string kind)
    {
        using var timer = CreateTimer(kind);
        var timings = new ConcurrentQueue<double>();
        var firstCall = true;

        timer.Elapsed += (_, args) =>
        {
            timings.Enqueue(args.TimeSinceLastInterval.TotalMilliseconds);
            if (firstCall)
            {
                firstCall = false;
                Thread.Sleep(100); // Block only the first call: Make some timer fires que up
            }
        };
        timer.SkipMissedIntervals = false;
        timer.Start();
        Thread.Sleep(400);
        timer.Stop();
        
        Assert.That(timings, Has.Count.GreaterThan(10));

        timings.TryDequeue(out var firstTime);
        timings.TryDequeue(out var secondTime);
        timings.TryDequeue(out var thirdTime);
        timings.TryDequeue(out var forthTime);
        Assert.That(firstTime, Is.InRange(10.0, 35.0));
        Assert.That(secondTime, Is.InRange(70.0, 140.0));
        Assert.That(thirdTime, Is.InRange(0.0, 5.0));
        Assert.That(forthTime, Is.InRange(0.0, 5.0));
    }
    
    [TestCaseSource(nameof(AllTimerKinds))]
    public void AutoResetFalse_StartFromHandler_RestartsTimer(string kind)
    {
        using var timer = CreateTimer(kind);
        var fireCount = 0;
        var secondFired = new ManualResetEventSlim(false);

        timer.AutoReset = false;
        timer.Elapsed += (_, _) =>
        {
            fireCount++;
            if (fireCount == 1)
            {
                // ReSharper disable once AccessToDisposedClosure
                timer.Start();
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
    public void ElapsedArgs_TimeSinceLastInterval_IsValid(string kind)
    {
        using var timer = CreateTimer(kind);
        var timings = new ConcurrentBag<double>();
        timer.Elapsed += (_, args) => timings.Add(args.TimeSinceLastInterval.TotalMilliseconds);
        timer.Start();
        Thread.Sleep(400);
        timer.Stop();

        var recorded = timings.ToArray();
        Assert.That(recorded, Is.Not.Empty);
        Assert.That(recorded, Is.All.InRange(10.0, 37.0));
    }
}
