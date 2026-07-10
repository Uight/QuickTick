namespace QuickTickLib;

[Flags]
internal enum TimerAccessMask : uint
{
    Delete = 0x00010000,
    ReadControl = 0x00020000,
    Synchronize = 0x00100000,
    WriteDac = 0x00040000,
    WriteOwner = 0x00080000,
    TimerAllAccess = 0x1F0003,
    TimerModifyState = 0x0002,
    TimerQueryState = 0x0001, //Reserved for future
}
