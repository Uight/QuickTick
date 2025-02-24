using System.Diagnostics;
using QuickTick;

var stopwatch = new Stopwatch();

using var timer = new QuickTickTimer(5);

var test = new List<double>();

timer.TimerElapsed += () => TimerElapsed();
timer.Start();
stopwatch.Start();

while (true)
{
    Thread.Sleep(1000);
    var low = test.Min();
    var high = test.Max();
    var mean = test.Average();

    Console.WriteLine($"{low} {high} {mean}");
    test.Clear();
}

void TimerElapsed()
{
    test.Add(stopwatch.Elapsed.TotalMilliseconds);
    stopwatch.Restart();
}