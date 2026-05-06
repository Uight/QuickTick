using System.Runtime.InteropServices;

namespace QuickTickLib;

internal sealed class SafeIoCompletionPortHandle : SafeHandle
{
    public SafeIoCompletionPortHandle() : base(IntPtr.Zero, true) { }

    public SafeIoCompletionPortHandle(IntPtr handle) : this()
    {
        SetHandle(handle);
    }

    public override bool IsInvalid => handle == IntPtr.Zero;

    protected override bool ReleaseHandle() => Win32Interop.CloseHandle(handle);
}

internal sealed class SafeTimerHandle : SafeHandle
{
    public SafeTimerHandle() : base(IntPtr.Zero, true) { }

    public SafeTimerHandle(IntPtr handle) : this()
    {
        SetHandle(handle);
    }

    public override bool IsInvalid => handle == IntPtr.Zero;

    protected override bool ReleaseHandle() => Win32Interop.CloseHandle(handle);
}

internal sealed class SafeWaitCompletionPacketHandle : SafeHandle
{
    public SafeWaitCompletionPacketHandle() : base(IntPtr.Zero, true) { }

    public SafeWaitCompletionPacketHandle(IntPtr handle) : this()
    {
        SetHandle(handle);
    }

    public override bool IsInvalid => handle == IntPtr.Zero;

    protected override bool ReleaseHandle() => Win32Interop.CloseHandle(handle);
}
