﻿using System.Diagnostics;
using QuickTickLib;

for (int i = 0; i < 5; i++)
{
    var delayStopwatch = new Stopwatch();
    delayStopwatch.Start();
    await QuickTickTiming.Delay(TimeSpan.FromMilliseconds(5));
    delayStopwatch.Stop();

    Console.WriteLine($"delay timing: {delayStopwatch.Elapsed.TotalMilliseconds}");
}

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

var timerTimingValues = new List<double>();

timer.Elapsed += TimerElapsed;
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

    var low = timerTimingValues.Min();
    var high = timerTimingValues.Max();
    var mean = timerTimingValues.Average();

    Console.WriteLine($"{low} {high} {mean}");
    timerTimingValues.Clear();
}

void TimerElapsed(object? sender, QuickTickElapsedEventArgs elapsedArgs)
{
    timerTimingValues.Add(timerStopwatch.Elapsed.TotalMilliseconds);
    timerStopwatch.Restart();
    /* Console.WriteLine($"Now: {DateTime.Now:yyyy-MM-dd HH:mm:ss.ffffff}; " +
                      $"TimerFired: {elapsedArgs.SignalTime:yyyy-MM-dd HH:mm:ss.ffffff}; " +
                      $"Expected: {elapsedArgs.ScheduledTime:yyyy-MM-dd HH:mm:ss.ffffff}"); */
}