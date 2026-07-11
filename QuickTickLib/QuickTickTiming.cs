using System.Runtime.InteropServices;

namespace QuickTickLib;

public static class QuickTickTiming
{
    // Testing showed that waiting 0.5ms results in around 1ms average waiting while going to 0.4ms results in an average of 0.5ms wait time
    // QuickTickSleep expects 100ns (TimeSpan) ticks, same as its other caller Sleep(), so this must be derived from TimeSpan.TicksPerMillisecond
    private const long FourHundredMicroSecondInTicks = (long)(TimeSpan.TicksPerMillisecond * 0.4);

    // Settable in tests (InternalsVisibleTo("QuickTickTests")) to exercise the Thread.Sleep/Task.Delay fallback path
    internal static bool IsQuickTickSupported = QuickTickHelper.PlatformSupportsQuickTick();

    // Settable in tests (InternalsVisibleTo("QuickTickTests")) to exercise the Thread.Sleep fallback tier
    internal static bool IsClockNanosleepSupported = LinuxInterop.PlatformSupportsPreciseSleep();

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

    // cachedHandles can be null when the platform has no waitable timers (or a test flipped IsQuickTickSupported
    // after the caller created its run); the caller keeps ownership and disposes them
    internal static void MinimalSleep(QuickTickHandleResources? cachedHandles)
    {
        if (IsQuickTickSupported)
        {
            if (cachedHandles != null)
            {
                QuickTickSleep(cachedHandles, FourHundredMicroSecondInTicks);
            }
            else
            {
                QuickTickSleep(FourHundredMicroSecondInTicks);
            }
        }
        else if (IsClockNanosleepSupported)
        {
            LinuxInterop.PreciseSleep(FourHundredMicroSecondInTicks);
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
        QuickTickSleep(handles, tickToSleep);
    }

    private static void QuickTickSleep(QuickTickHandleResources handles, long tickToSleep)
    {
        var sleepTimeTicks = -tickToSleep; // Negative means relative time

        if (!Win32Interop.SetWaitableTimer(handles.TimerHandle, ref sleepTimeTicks, 0, IntPtr.Zero, IntPtr.Zero, false))
        {
            throw new InvalidOperationException($"SetWaitableTimer failed: {Marshal.GetLastWin32Error()}");
        }

        handles.TimerWaitHandle.WaitOne();
    }

    // Interruptible variant for the long sleep-until-near-deadline phase of HighResQuickTickTimer:
    // waitHandles must contain the wait handle of handles' timer plus the event that wakes the sleep early.
    // Waking early is safe because SetWaitableTimer resets a still-pending timer signal on the next call.
    internal static void InterruptibleQuickTickSleep(QuickTickHandleResources handles, WaitHandle[] waitHandles, long tickToSleep)
    {
        var sleepTimeTicks = -tickToSleep; // Negative means relative time

        if (!Win32Interop.SetWaitableTimer(handles.TimerHandle, ref sleepTimeTicks, 0, IntPtr.Zero, IntPtr.Zero, false))
        {
            throw new InvalidOperationException($"SetWaitableTimer failed: {Marshal.GetLastWin32Error()}");
        }

        WaitHandle.WaitAny(waitHandles);
    }
}