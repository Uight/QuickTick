using System.Diagnostics;
using System.Timers;

var stopwatch = new Stopwatch();

HighResTimer timer = new HighResTimer();
timer.TimerElapsed += () => TimerElapsed();
timer.Start(TimeSpan.FromMilliseconds(5));
stopwatch.Start();

while (true)
{
    //stopwatch.Restart();
    //Thread.Sleep(5);
    //HighPrecisionSleep.Sleep(TimeSpan.FromMilliseconds(5));
    //var time = stopwatch.Elapsed.TotalMilliseconds;
    //if (Math.Abs(time - 5) > 1)
    {
        //Console.WriteLine(time);
    }
}

void TimerElapsed()
{
    stopwatch.Stop();
    var time = stopwatch.Elapsed;
    Console.WriteLine(time.TotalMilliseconds);
    stopwatch.Restart();
}