using System.Runtime.InteropServices;

namespace QuickTickLib;

public static class QuickTickTiming
{
    // Testing showed that waiting 0.5ms results in around 1ms average waiting while going to 0.4ms results in an average of 0.5ms wait time
    // QuickTickSleep expects 100ns (TimeSpan) ticks, same as its other caller Sleep(), so this must be derived from TimeSpan.TicksPerMillisecond
    private const long FourHundredMicroSecondInTicks = (long)(TimeSpan.TicksPerMillisecond * 0.4);

    // Settable in tests (InternalsVisibleTo("QuickTickTests")) to exercise the Thread.Sleep/Task.Delay fallback path
    internal static bool IsQuickTickSupported = QuickTickHelper.PlatformSupportsQuickTick();

    // Not async on purpose: usage errors (unsupported platform) should throw synchronously at the call site
    // ReSharper disable once MemberCanBePrivate.Global
    public static Task Delay(int millisecondsDelay, CancellationToken cancellationToken = default)
    {
        QuickTickHelper.ThrowIfUnsupportedWindowsVersion();

        if (!IsQuickTickSupported || millisecondsDelay <= 0)
        {
            return Task.Delay(millisecondsDelay, cancellationToken);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        return DelayCore(millisecondsDelay, cancellationToken);
    }

    public static Task Delay(TimeSpan delay, CancellationToken cancellationToken = default)
    {
        var milliseconds = (int)Math.Ceiling(delay.TotalMilliseconds);
        return Delay(milliseconds, cancellationToken);
    }
    
    private static async Task DelayCore(int millisecondsDelay, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        using (var timer = new QuickTickTimer(millisecondsDelay))
        {
            timer.AutoReset = false;
            timer.Elapsed += (_, _) => tcs.TrySetResult(true);
            timer.Start();
            
            // ReSharper disable once UseAwaitUsing : Can not be used since we support .NET 4.8
            using (cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken)))
            {
                await tcs.Task;
            }
        }
    }

    public static void Sleep(int millisecondsTimeout)
    {
        QuickTickHelper.ThrowIfUnsupportedWindowsVersion();

        if (!IsQuickTickSupported || millisecondsTimeout <= 0)
        {
            Thread.Sleep(millisecondsTimeout);
            return;
        }

        var sleepTimeTicks = TimeSpan.FromMilliseconds(millisecondsTimeout).Ticks;
        QuickTickSleep(sleepTimeTicks);
    }

    internal static void MinimalSleep()
    {
        if (IsQuickTickSupported)
        {       
            QuickTickSleep(FourHundredMicroSecondInTicks);
        }
        else
        {
            Thread.Sleep(1);
        }
    }
    
    /// <summary>
    /// The tickToSleep must be specified in <see cref="TimeSpan"/> ticks (100-nanosecond
    /// intervals), not <see cref="System.Diagnostics.Stopwatch"/> ticks, because WinAPI calls want it that way.
    /// </summary>
    internal static void QuickTickSleep(long tickToSleep)
    {
        using var handles = new QuickTickHandleResources();

        var sleepTimeTicks = -tickToSleep; // Negative means relative time

        if (!Win32Interop.SetWaitableTimer(handles.TimerHandle, ref sleepTimeTicks, 0, IntPtr.Zero, IntPtr.Zero, false))
        {
            throw new InvalidOperationException($"SetWaitableTimer failed: {Marshal.GetLastWin32Error()}");
        }

        handles.TimerWaitHandle.WaitOne();
    }
}