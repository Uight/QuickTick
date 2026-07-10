using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace QuickTickLib;

internal sealed class QuickTickHandleResources : IDisposable
{
    public readonly SafeTimerHandle TimerHandle;
    public readonly WaitHandle TimerWaitHandle;

    public QuickTickHandleResources()
    {
        TimerHandle = Win32Interop.CreateWaitableTimerExW(IntPtr.Zero, IntPtr.Zero, QuickTickHelper.CreateWaitableTimerFlagHighResolution, QuickTickHelper.CreateWaitableTimerExWAccessRights);
        if (TimerHandle.IsInvalid)
        {
            throw new InvalidOperationException($"CreateWaitableTimerExW failed: {Marshal.GetLastWin32Error()}");
        }

        TimerWaitHandle = new TimerWaitHandleView(TimerHandle);
    }

    public void Dispose()
    {
        TimerWaitHandle.Dispose();
        TimerHandle.Dispose();
    }

    // Exposes the kernel timer handle to managed waits (WaitOne/WaitAny) without owning it;
    // TimerHandle stays the single owner, both are created and disposed together by this class
    private sealed class TimerWaitHandleView : WaitHandle
    {
        public TimerWaitHandleView(SafeTimerHandle timerHandle)
        {
            SafeWaitHandle = new SafeWaitHandle(timerHandle.DangerousGetHandle(), false);
        }
    }
}
