using System.Diagnostics;
using QuickTickLib;

for (int i = 0; i < 5; i++)
{
    var delayStopwatch = new Stopwatch();
    delayStopwatch.Start();
    await QuickTickTiming.Delay(5);
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

timer.TimerElapsed += () => TimerElapsed();
timer.AutoReset = true;
timer.Start();
timerStopwatch.Start();

var run = true;

while (run)
{
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

void TimerElapsed()
{
    timerTimingValues.Add(timerStopwatch.Elapsed.TotalMilliseconds);
    timerStopwatch.Restart();
}