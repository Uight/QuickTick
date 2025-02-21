using System.Diagnostics;

var stopwatch = new Stopwatch();

while (true)
{
    stopwatch.Restart();
    //Thread.Sleep(5);
    HighResolutionSleep.Sleep(TimeSpan.FromMilliseconds(5));
    var time = stopwatch.Elapsed.TotalMilliseconds;
    if (Math.Abs(time - 5) > 1)
    {
        Console.WriteLine(time);
    }
}