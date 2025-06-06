﻿using System.Runtime.InteropServices;

namespace QuickTickLib;

public static class QuickTickTiming
{
    public static async Task Delay(int milliseconds, CancellationToken cancellationToken = default)
    {
        var isQuickTickSupported = QuickTickHelper.PlatformSupportsQuickTick();

        if (!isQuickTickSupported)
        {
            await Task.Delay(milliseconds, cancellationToken);
            return;
        }

        var tcs = new TaskCompletionSource<bool>();

        using (var timer = new QuickTickTimer(milliseconds))
        {
            timer.AutoReset = false;
            timer.Elapsed += (object? _, QuickTickElapsedEventArgs _) => tcs.TrySetResult(true);
            timer.Start();

            using (cancellationToken.Register(() => tcs.TrySetCanceled()))
            {
                try
                {
                    await tcs.Task;
                }
                catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // Ignore
                }
            }
        }
    }

    public static async Task Delay(TimeSpan timeSpan, CancellationToken cancellationToken = default)
    {
        var milliseconds = (int)Math.Ceiling(timeSpan.TotalMilliseconds);
        await Delay(milliseconds, cancellationToken);
    }

    public static void Sleep(int sleepTimeMs)
    {
        var isQuickTickSupported = QuickTickHelper.PlatformSupportsQuickTick();

        if (sleepTimeMs <= 0 || !isQuickTickSupported)
        {
            Thread.Sleep(sleepTimeMs);
            return;
        }

        var sleepTimeTicks = -TimeSpan.FromMilliseconds(sleepTimeMs).Ticks; // negative means relative time

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