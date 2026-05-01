using System.Runtime.InteropServices;

namespace QuickTickLib;

internal sealed class QuickTickHandleResources : IDisposable
{
    public readonly SafeIoCompletionPortHandle IocpHandle;
    public readonly SafeWaitCompletionPacketHandle WaitIocpHandle;
    public readonly SafeTimerHandle TimerHandle;

    public QuickTickHandleResources(double interval)
    {
        try
        {
            IocpHandle = Win32Interop.CreateIoCompletionPort(new IntPtr(-1), IntPtr.Zero, IntPtr.Zero, 0);
            if (IocpHandle.IsInvalid)
            {
                throw new InvalidOperationException($"CreateIoCompletionPort failed: {Marshal.GetLastWin32Error()}");
            }

            int status = Win32Interop.NtCreateWaitCompletionPacket(out WaitIocpHandle, QuickTickHelper.NtCreateWaitCompletionPacketAccessRights, IntPtr.Zero);
            if (status != 0)
            {
                throw new InvalidOperationException($"NtCreateWaitCompletionPacket failed: {status:X8}");
            }
            if (WaitIocpHandle.IsInvalid)
            {
                throw new InvalidOperationException("NtCreateWaitCompletionPacket returned success but the handle is invalid.");
            }

            TimerHandle = Win32Interop.CreateWaitableTimerExW(IntPtr.Zero, IntPtr.Zero, Win32Interop.CreateWaitableTimerFlag_HIGH_RESOLUTION, QuickTickHelper.CreateWaitableTimerExWAccessRights);
            if (TimerHandle.IsInvalid)
            {
                throw new InvalidOperationException($"CreateWaitableTimerExW failed: {Marshal.GetLastWin32Error()}");
            }
        }
        catch
        {
            IocpHandle?.Dispose();
            WaitIocpHandle?.Dispose();
            TimerHandle?.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        WaitIocpHandle.Dispose();
        IocpHandle.Dispose();
        TimerHandle.Dispose();
    }
}
