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

            var sleepTimeTicks = -tickToSleep; // negative means relative time

            if (!Win32Interop.SetWaitableTimer(handles.TimerHandle, ref sleepTimeTicks, 0, IntPtr.Zero, IntPtr.Zero, false))
            {
                throw new InvalidOperationException($"SetWaitableTimer failed: {Marshal.GetLastWin32Error()}");
            }

            int ntAssociateWaitCompletionPacketStatus = Win32Interop.NtAssociateWaitCompletionPacket(handles.WaitIocpHandle, handles.IocpHandle, handles.TimerHandle, successCompletionKey, IntPtr.Zero, 0, IntPtr.Zero, out _);

            if (ntAssociateWaitCompletionPacketStatus != 0)
            {
                throw new InvalidOperationException($"NtAssociateWaitCompletionPacket failed: {ntAssociateWaitCompletionPacketStatus:X8}");
            }

            while (true) // Loop is not strictly neccessary as there should always only be one completion packet.
            {
                if (Win32Interop.GetQueuedCompletionStatus(handles.IocpHandle, out _, out var lpCompletionKey, out _, uint.MaxValue))
                {
                    if (lpCompletionKey == successCompletionKey)
                    {
                        break;
                    }
                }
            }
        }
        finally
        {
            localHandles?.Dispose();
        }
    }
}