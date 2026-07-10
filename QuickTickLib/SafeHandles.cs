using System.Runtime.InteropServices;

namespace QuickTickLib;

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
