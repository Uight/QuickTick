using QuickTickLib;
using System;
using System.Threading;

namespace QuickTestProjectNetFramework
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Hello, World!");

            using QuickTickTimer timer = new(1000.0 / 60.0);
            timer.Elapsed += Timer_Elapsed;
            timer.Start();

            Thread.Sleep(1000); // Run for 5 seconds
            timer.Stop();
        }

        private static void Timer_Elapsed(object? sender, QuickTickElapsedEventArgs e)
        {
            var diff = e.SignalTime - e.ScheduledTime;
            Console.WriteLine($"Timer elapsed! Scheduled: {e.ScheduledTime:dd.MM.yyyy HH:mm:ss.fff}, Actual: {e.SignalTime:dd.MM.yyyy HH:mm:ss.fff} ms diff: {diff.TotalMilliseconds}");
        }
    }
}
