using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using NUnit.Framework;
using QuickTickLib;

namespace QuickTickTests;

// Tests run against all three timer implementations to verify they behave consistently.
// These tests should verify function not performance
[Platform(Exclude = "MacOsX", Reason = "macOS timing is too unpredictable for the timing ranges asserted here")]
public class TimerBehaviorTests
{
    // Use default timing of windows (15.625) so tests are mostly predictable even with fallback timer
    // Actually stay just below the default for windows because using it exactly can lead to the fallback timer waiting double very often
    private const double IntervalMs = 15;

    private static IEnumerable<string> AllTimerKinds()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // QuickTickTimerImplementation P/Invokes kernel32/ntdll in its constructor and can only run on Windows
            yield return "implementation";
        }
        else
        {
            // QuickTickTimerFallback is internal and only ever selected automatically on non-Windows platforms
            // (QuickTickTimer always picks "implementation" on supported Windows versions). Running it on
            // Windows CI too would only add ThreadPool-dispatch jitter from a shared runner without exercising
            // any path real Windows users hit — the Linux job already covers its logic against the real target OS.
            yield return "fallback";
        }
        yield return "highResolution";
    }

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
    public void SkipMissedIntervals_StopWhileLongHandlerRunning_SkippedCountResetsOnRestart(string kind)
    {
        using var timer = CreateTimer(kind);
        var skips = new ConcurrentQueue<long>();
        var handlerStarted = new ManualResetEventSlim(false);
        var firstCall = true;

        timer.AutoReset = true;
        timer.SkipMissedIntervals = true;
        timer.Elapsed += (_, args) =>
        {
            skips.Enqueue(args.SkippedIntervals);
            if (firstCall)
            {
                firstCall = false;
                handlerStarted.Set();
                Thread.Sleep(150); // Stay blocked so intervals pile up AND Stop() is called while we're here
            }
        };

        timer.Start();
        Assert.That(handlerStarted.Wait(TimeSpan.FromMilliseconds(500)), Is.True);

        Thread.Sleep(50); // Let more intervals pile up while handler is blocked (~3 extra at 15ms interval)
        timer.Stop(); // Stop() is called while the handler is still sleeping; it blocks here until handler returns

        while (skips.Any()) skips.TryDequeue(out _);

        // Restart — accumulated/piled-up events must not carry the skipped count into the new run
        timer.Start();
        Thread.Sleep(200);
        timer.Stop();

        Assert.That(skips, Has.Count.GreaterThan(0), $"{kind}: expected ticks after restart");
        skips.TryDequeue(out var firstSkipAfterRestart);
        Assert.That(firstSkipAfterRestart, Is.EqualTo(0L), $"{kind}: SkippedIntervals must reset to 0 after Stop()+Start(), even when stopped mid-handler with events piled up");
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
    public void SkipMissedIntervals_Enabled_NoBurstAfterDelay(string kind)
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
                Thread.Sleep(100); // Block first call so intervals pile up
            }
        };
        timer.SkipMissedIntervals = true;
        timer.Start();
        Thread.Sleep(400);
        timer.Stop();

        var values = timings.ToArray();
        Assert.That(values, Has.Length.GreaterThan(5));
        Assert.That(values[0], Is.InRange(10.0, 35.0));    // normal first tick
        Assert.That(values[1], Is.InRange(70.0, 140.0));   // fires after ~100ms block

        // After at most one short catch-up tick (values[2]), pacing should normalize — no burst
        var tail = values.Skip(4).ToArray();
        Assert.That(tail, Is.All.GreaterThanOrEqualTo(10.0), $"{kind}: pacing should resume normally after skip, no sustained burst");
    }

    [TestCaseSource(nameof(AllTimerKinds))]
    public void AutoResetFalse_SkipMissedIntervals_DoesNotPreventFire(string kind)
    {
        using var timer = CreateTimer(kind);
        var count = 0;
        timer.AutoReset = false;
        timer.SkipMissedIntervals = true;
        timer.Elapsed += (_, _) => count++;
        timer.Start();
        Thread.Sleep(500);
        Assert.That(count, Is.EqualTo(1), $"{kind}: AutoReset=false with SkipMissedIntervals=true should still fire exactly once");
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
