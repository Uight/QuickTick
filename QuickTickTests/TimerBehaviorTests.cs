using System.Collections.Concurrent;
using NUnit.Framework;
using QuickTickLib;

namespace QuickTickTests;

// Tests run against all three timer implementations to verify they behave consistently.
// These tests should verify function not performance
[Platform(Include = "Win", Reason = "Timer implementations rely on Win32 APIs or Windows timing behavior")]
public class TimerBehaviorTests
{
    private const double IntervalMs = 15.625; // Use default timing of windows so tests are mostly predictable even with fallback timer

    private static IEnumerable<string> AllTimerKinds() =>
    [
        "implementation",
        "fallback",
        "highResolution",
    ];

    private static IQuickTickTimer CreateTimer(string kind, double intervalMs = IntervalMs) => kind switch
    {
        "implementation" => new QuickTickTimerImplementation(intervalMs),
        "fallback"       => new QuickTickTimerFallback(intervalMs),
        "highResolution" => new HighResQuickTickTimer(intervalMs),
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
        Assert.That(count, Is.InRange(22, 42));
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
        timer.Start();
        timer.Start();
        timer.Start();
        Thread.Sleep(500);
        timer.Stop();
        Assert.That(count, Is.InRange(22, 42)); // One timer should run only still expect ~32 fires
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
    }
    
    [TestCaseSource(nameof(AllTimerKinds))]
    public void SkipMissedIntervals_ReportsSkippedCount(string kind)
    {
        using var timer = CreateTimer(kind, intervalMs: 10);
        var skippedCounts = new ConcurrentBag<long>();
        timer.Elapsed += (_, args) =>
        {
            skippedCounts.Add(args.SkippedIntervals);
            Thread.Sleep(50); // Deliberately blocking event code so the timer has to skip ticks
        };
        timer.SkipMissedIntervals = true;
        timer.Start();
        Thread.Sleep(250);
        timer.Stop();
        Assert.That(skippedCounts, Has.Some.GreaterThan(0L));
    }

    //Rework this to only block first call then check the timing since last tick;
    [TestCaseSource(nameof(AllTimerKinds))]
    public void SkipMissedIntervalsDisabled_DoesNotSkip(string kind)
    {
        using var timer = CreateTimer(kind, intervalMs: 10);
        var skippedCounts = new ConcurrentBag<long>();
        timer.Elapsed += (_, args) =>
        {
            skippedCounts.Add(args.SkippedIntervals);
            Thread.Sleep(50); // Slow handler — but skipping is off
        };
        timer.SkipMissedIntervals = false;
        timer.Start();
        Thread.Sleep(600);
        timer.Stop();
        Assert.That(skippedCounts, Is.All.EqualTo(0L));
    }

    // --- ElapsedEventArgs sanity ---

    [TestCaseSource(nameof(AllTimerKinds))]
    public void ElapsedArgs_TimeSinceLastInterval_IsPositive(string kind)
    {
        using var timer = CreateTimer(kind, intervalMs: 20);
        var timings = new ConcurrentBag<double>();
        timer.Elapsed += (_, args) => timings.Add(args.TimeSinceLastInterval.TotalMilliseconds);
        timer.Start();
        Thread.Sleep(400);
        timer.Stop();

        var recorded = timings.ToArray();
        Assert.That(recorded, Is.Not.Empty);
        // First fire has lastFireTicks=0 so value equals time-since-start (~20 ms).
        // Subsequent fires should be ~20 ms. Allow a wide ceiling for jitter.
        Assert.That(recorded, Is.All.InRange(0.0, 500.0));
    }
}
