using System.Diagnostics;

var stopwatch = new Stopwatch();

var timer = new HighResTimer();

var test = new List<double>();

timer.TimerElapsed += () => TimerElapsed();
timer.Start(TimeSpan.FromMilliseconds(5));
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