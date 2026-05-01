using System.Runtime.InteropServices;

namespace QuickTickLib;

public static class QuickTickTiming
{
    // Testing showed that waiting 0.5ms results in around 1ms average waiting while going to 0.4ms results in an average of 0.5ms wait time
    private const long fourHundredMicroSecondInTicks = (long)(TimeSpan.TicksPerMillisecond * 0.4);
    private static readonly IntPtr successCompletionKey = new(1);

    public static async Task Delay(int millisecondsDelay, CancellationToken cancellationToken = default)
    {
        var isQuickTickSupported = QuickTickHelper.PlatformSupportsQuickTick();

        if (!isQuickTickSupported || millisecondsDelay <= 0)
        {
            await Task.Delay(millisecondsDelay, cancellationToken);
            return;
        }

        var tcs = new TaskCompletionSource<bool>();

        using (var timer = new QuickTickTimer(millisecondsDelay))
        {
            timer.AutoReset = false;
            timer.Elapsed += (_, _) => tcs.TrySetResult(true);
            timer.Start();

            using (cancellationToken.Register(() => tcs.TrySetCanceled()))
            {
                await tcs.Task;
            }
        }
    }

    public static async Task Delay(TimeSpan delay, CancellationToken cancellationToken = default)
    {
        var milliseconds = (int)Math.Ceiling(delay.TotalMilliseconds);
        await Delay(milliseconds, cancellationToken);
    }

    public static void Sleep(int millisecondsTimeout)
    {
        var isQuickTickSupported = QuickTickHelper.PlatformSupportsQuickTick();

        if (!isQuickTickSupported || millisecondsTimeout <= 0)
        {
            Thread.Sleep(millisecondsTimeout);
            return;
        }

        var sleepTimeTicks = TimeSpan.FromMilliseconds(millisecondsTimeout).Ticks;
        QuickTickSleep(sleepTimeTicks);
    }

    internal static void MinimalSleep()
    {
        var isQuickTickSupported = QuickTickHelper.PlatformSupportsQuickTick();
        if (isQuickTickSupported)
        {       
            QuickTickSleep(fourHundredMicroSecondInTicks);
        }
        else
        {
            Thread.Sleep(1);
        }
    }

    internal static void QuickTickSleep(long tickToSleep, QuickTickHandleResources? externalHandles = null)
    {
        QuickTickHandleResources? localHandles = null;

        try
        {
            var handles = externalHandles ?? (localHandles = new QuickTickHandleResources());

            var sleepTimeTicks = -tickToSleep; // Negative means relative time

            if (!Win32Interop.SetWaitableTimer(handles.TimerHandle, ref sleepTimeTicks, 0, IntPtr.Zero, IntPtr.Zero, false))
            {
                throw new InvalidOperationException($"SetWaitableTimer failed: {Marshal.GetLastWin32Error()}");
            }

            int ntAssociateWaitCompletionPacketStatus = Win32Interop.NtAssociateWaitCompletionPacket(handles.WaitIocpHandle, handles.IocpHandle, handles.TimerHandle, successCompletionKey, IntPtr.Zero, 0, IntPtr.Zero, out _);

            if (ntAssociateWaitCompletionPacketStatus != 0)
            {
                throw new InvalidOperationException($"NtAssociateWaitCompletionPacket failed: {ntAssociateWaitCompletionPacketStatus:X8}");
            }

            // Checking the return value and the completion key is unnessary since the IOCP is not shared and therefor we expect exactly one completion packet, which is the one we just set up above.
            // The return value would only be false if the IOCP was disposed in which case we also want to stop waiting because that could only happen when the caller disposes the handles or program is shutting down
            Win32Interop.GetQueuedCompletionStatus(handles.IocpHandle, out _, out _, out _, uint.MaxValue);
        }
        finally
        {
            localHandles?.Dispose();
        }
    }
}