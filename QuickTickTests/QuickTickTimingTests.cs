using System.Diagnostics;
using NUnit.Framework;
using QuickTickLib;

namespace QuickTickTests;

[Platform(Include = "Win", Reason = "Timer implementations rely on Win32 APIs or Windows timing behavior")]
public class QuickTickTimingTests
{
    [Test]
    public void Sleep_PositiveDuration_SleepsApproximatelyCorrectTime()
    {
        var sw = Stopwatch.StartNew();
        QuickTickTiming.Sleep(50);
        sw.Stop();
        Assert.That(sw.Elapsed.TotalMilliseconds, Is.InRange(30.0, 100.0));
    }

    [Test]
    public void Sleep_ZeroDuration_ReturnsImmediately()
    {
        var sw = Stopwatch.StartNew();
        QuickTickTiming.Sleep(0);
        sw.Stop();
        Assert.That(sw.Elapsed.TotalMilliseconds, Is.LessThan(20.0));
    }

    [Test]
    public void Delay_PositiveDuration_CompletesApproximatelyOnTime()
    {
        var sw = Stopwatch.StartNew();
        QuickTickTiming.Delay(50).GetAwaiter().GetResult();
        sw.Stop();
        Assert.That(sw.Elapsed.TotalMilliseconds, Is.InRange(30.0, 100.0));
    }

    [Test]
    public void Delay_ZeroDuration_CompletesImmediately()
    {
        var sw = Stopwatch.StartNew();
        QuickTickTiming.Delay(0).GetAwaiter().GetResult();
        sw.Stop();
        Assert.That(sw.Elapsed.TotalMilliseconds, Is.LessThan(20.0));
    }

    [Test]
    public void Delay_TimeSpanOverload_CompletesApproximatelyOnTime()
    {
        var sw = Stopwatch.StartNew();
        QuickTickTiming.Delay(TimeSpan.FromMilliseconds(50)).GetAwaiter().GetResult();
        sw.Stop();
        Assert.That(sw.Elapsed.TotalMilliseconds, Is.InRange(30.0, 100.0));
    }

    [Test]
    public void Delay_PreCancelledToken_PositiveDuration_ThrowsImmediately()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var sw = Stopwatch.StartNew();
        Assert.CatchAsync<OperationCanceledException>(() => QuickTickTiming.Delay(5000, cts.Token));
        sw.Stop();
        Assert.That(sw.Elapsed.TotalMilliseconds, Is.LessThan(200.0), "Should cancel immediately, not run the full 5 s delay");
    }

    [Test]
    public void Delay_PreCancelledToken_ZeroDuration_ThrowsImmediately()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var sw = Stopwatch.StartNew();
        Assert.CatchAsync<OperationCanceledException>(() => QuickTickTiming.Delay(0, cts.Token));
        sw.Stop();
        Assert.That(sw.Elapsed.TotalMilliseconds, Is.LessThan(200.0));
    }

    [Test]
    public void Delay_TokenCancelledDuringWait_ThrowsWhenCancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(50);

        var sw = Stopwatch.StartNew();
        Assert.CatchAsync<OperationCanceledException>(() => QuickTickTiming.Delay(5000, cts.Token));
        sw.Stop();
        Assert.That(sw.Elapsed.TotalMilliseconds, Is.InRange(30.0, 200.0), "Should cancel after ~50 ms, not run the full 5 s delay");
    }
}

// Tests that exercise the Thread.Sleep / Task.Delay fallback path by replacing the platform check delegate.
// Marked NonParallelizable because they mutate a static field on QuickTickTiming.
[NonParallelizable]
public class QuickTickTimingFallbackPathTests
{
    private static readonly bool OriginalValue = QuickTickTiming.IsQuickTickSupported;

    [SetUp]
    public void SetUp() => QuickTickTiming.IsQuickTickSupported = false;

    [TearDown]
    public void TearDown() => QuickTickTiming.IsQuickTickSupported = OriginalValue;

    [Test]
    public void Sleep_PositiveDuration_FallsBackToThreadSleep()
    {
        var sw = Stopwatch.StartNew();
        QuickTickTiming.Sleep(50);
        sw.Stop();
        Assert.That(sw.Elapsed.TotalMilliseconds, Is.InRange(30.0, 150.0));
    }

    [Test]
    public void Delay_PositiveDuration_FallsBackToTaskDelay()
    {
        var sw = Stopwatch.StartNew();
        QuickTickTiming.Delay(50).GetAwaiter().GetResult();
        sw.Stop();
        Assert.That(sw.Elapsed.TotalMilliseconds, Is.InRange(30.0, 150.0));
    }

    [Test]
    public void Delay_TimeSpanOverload_FallsBackToTaskDelay()
    {
        var sw = Stopwatch.StartNew();
        QuickTickTiming.Delay(TimeSpan.FromMilliseconds(50)).GetAwaiter().GetResult();
        sw.Stop();
        Assert.That(sw.Elapsed.TotalMilliseconds, Is.InRange(30.0, 150.0));
    }

    [Test]
    public void Delay_PreCancelledToken_ThrowsImmediately()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var sw = Stopwatch.StartNew();
        Assert.CatchAsync<OperationCanceledException>(() => QuickTickTiming.Delay(5000, cts.Token));
        sw.Stop();
        Assert.That(sw.Elapsed.TotalMilliseconds, Is.LessThan(200.0));
    }

    [Test]
    public void Delay_TokenCancelledDuringWait_ThrowsWhenCancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(50);

        var sw = Stopwatch.StartNew();
        Assert.CatchAsync<OperationCanceledException>(() => QuickTickTiming.Delay(5000, cts.Token));
        sw.Stop();
        Assert.That(sw.Elapsed.TotalMilliseconds, Is.InRange(30.0, 200.0));
    }
}
