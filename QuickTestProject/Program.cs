using QuickTickLib;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

for (int i = 0; i < 5; i++)
{
    var delayStopwatch = new Stopwatch();
    delayStopwatch.Start();
    await QuickTickTiming.Delay(TimeSpan.FromMilliseconds(5));
    delayStopwatch.Stop();

    Console.WriteLine($"delay timing: {delayStopwatch.Elapsed.TotalMilliseconds}");
}

var delayWithCancelStopwatch = new Stopwatch();
using var cts = new CancellationTokenSource();
_ = Task.Run(() =>
{
    Thread.Sleep(45); // Use sub 46.8ms time (smaller 3*15.6) to get cancel around 50ms later
    cts.Cancel();
});
delayWithCancelStopwatch.Start();
try
{
    await QuickTickTiming.Delay(TimeSpan.FromMilliseconds(100), cts.Token);
}
catch (TaskCanceledException)
{
    // Ignore
}
delayWithCancelStopwatch.Stop();

Console.WriteLine($"delay with cancel after 50ms timing: {delayWithCancelStopwatch.Elapsed.TotalMilliseconds}");


for (int i = 0; i < 5; i++)
{
    var sleepStopwatch = new Stopwatch();
    sleepStopwatch.Start();
    QuickTickTiming.Sleep(5);
    sleepStopwatch.Stop();

    Console.WriteLine($"sleep timing: {sleepStopwatch.Elapsed.TotalMilliseconds}");
}

var timerStopwatch = new Stopwatch();

using var timer = new QuickTickTimer(5);

var timerTimingValues = new ConcurrentBag<double>();

timer.Elapsed += TimerElapsed;
timer.SkipMissedTicks = false;
timer.AutoReset = true;
timer.Start();
timer.Stop(); // Just for testing
timer.Start();
timerStopwatch.Start();

var run = true;

while (run)
{
    if (Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.Escape)
    {
        run = false;
        break;
    }

    Thread.Sleep(1000);

    if (timerTimingValues.Count == 0)
    {
        continue;
    }

    var drained = new List<double>();
    while (timerTimingValues.TryTake(out var value))
    {
        drained.Add(value);
    }

    if (drained.Count > 0)
    {
        var low = drained.Min();
        var high = drained.Max();
        var mean = drained.Average();

        Console.WriteLine($"{low} {high} {mean}");
    }
}

void TimerElapsed(object? sender, QuickTickElapsedEventArgs elapsedArgs)
{
    timerTimingValues.Add(timerStopwatch.Elapsed.TotalMilliseconds);
    timerStopwatch.Restart();
    /*Console.WriteLine($"Now: '{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.ffffff}'; " +
                      $"Time since last tick: '{elapsedArgs.TimeSinceLastTick.TotalMilliseconds}'; " +
                      $"Skipped: '{elapsedArgs.SkippedIntervals}';");*/
}