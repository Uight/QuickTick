using System.Runtime.Versioning;

namespace QuickTickLib;

[SupportedOSPlatform("windows")]
public static class QuickTickTiming
{
    public static async Task Delay(int milliseconds, CancellationToken cancellationToken = default)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        using (var timer = new QuickTickTimer(milliseconds))
        {
            timer.AutoReset = false;
            timer.TimerElapsed += () => tcs.TrySetResult(true);
            timer.Start();

            using (cancellationToken.Register(() => tcs.TrySetCanceled()))
            {
                await tcs.Task; 
            }
        }
    }

    public static async Task Delay(TimeSpan timeSpan, CancellationToken cancellationToken = default)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        using (var timer = new QuickTickTimer(timeSpan))
        {
            timer.AutoReset = false;
            timer.TimerElapsed += () => tcs.TrySetResult(true);
            timer.Start();

            using (cancellationToken.Register(() => tcs.TrySetCanceled()))
            {
                await tcs.Task;
            }
        }
    }
}
