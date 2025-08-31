using System.Runtime.InteropServices;

namespace QuickTickLib;

public static class QuickTickTiming
{
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
            timer.Elapsed += (object? _, QuickTickElapsedEventArgs _) => tcs.TrySetResult(true);
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
            const long halfMillisecondInTicks = TimeSpan.TicksPerMillisecond / 2;
            QuickTickSleep(halfMillisecondInTicks);
        }
        else
        {
            Thread.Sleep(1);
        }
    }

    internal static void QuickTickSleep(long tickToSleep)
    {
        var sleepTimeTicks = -tickToSleep; // negative means relative time

        var iocpHandle = Win32Interop.CreateIoCompletionPort(new IntPtr(-1), IntPtr.Zero, IntPtr.Zero, 0);
        if (iocpHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException($"CreateIoCompletionPort failed: {Marshal.GetLastWin32Error()}");
        }

        IntPtr waitIocpHandle;
        var ntCreateWaitCompletionPacketStatus = Win32Interop.NtCreateWaitCompletionPacket(out waitIocpHandle, QuickTickHelper.NtCreateWaitCompletionPacketAccessRights, IntPtr.Zero);
        if (ntCreateWaitCompletionPacketStatus != 0)
        {
            throw new InvalidOperationException($"NtCreateWaitCompletionPacket failed: {ntCreateWaitCompletionPacketStatus:X8}");
        }

        var timerHandle = Win32Interop.CreateWaitableTimerExW(IntPtr.Zero, IntPtr.Zero, Win32Interop.CreateWaitableTimerFlag_HIGH_RESOLUTION, QuickTickHelper.CreateWaitableTimerExWAccessRights);
        if (timerHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException($"CreateWaitableTimerExW failed: {Marshal.GetLastWin32Error()}");
        }

        var successCompletionKey = new IntPtr(1);

        if (!Win32Interop.SetWaitableTimer(timerHandle, ref sleepTimeTicks, 0, IntPtr.Zero, IntPtr.Zero, false))
        {
            throw new InvalidOperationException($"SetWaitableTimer failed: {Marshal.GetLastWin32Error()}");
        }

        int ntAssociateWaitCompletionPacketStatus = Win32Interop.NtAssociateWaitCompletionPacket(waitIocpHandle, iocpHandle, timerHandle, successCompletionKey, IntPtr.Zero, 0, IntPtr.Zero, out _);

        if (ntAssociateWaitCompletionPacketStatus != 0)
        {
            throw new InvalidOperationException($"NtAssociateWaitCompletionPacket failed: {ntAssociateWaitCompletionPacketStatus:X8}");
        }

        while (true) // Loop is not strictly neccessary as there should always only be one completion packet.
        {
            if (Win32Interop.GetQueuedCompletionStatus(iocpHandle, out _, out var lpCompletionKey, out _, uint.MaxValue))
            {
                if (lpCompletionKey == successCompletionKey)
                {
                    break;
                }
            }
        }

        if (waitIocpHandle != IntPtr.Zero)
        {
            Win32Interop.CloseHandle(waitIocpHandle);
        }

        if (iocpHandle != IntPtr.Zero)
        {
            Win32Interop.CloseHandle(iocpHandle);
        }

        if (timerHandle != IntPtr.Zero)
        {
            Win32Interop.CloseHandle(timerHandle);
        }
    }
}