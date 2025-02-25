# QuickTick

QuickTick is a small library based on what was discussed in a .net runtime issue on github: https://github.com/dotnet/runtime/issues/67088
It is based on the timer the microsoft Go team implemented and that was presented in: https://devblogs.microsoft.com/go/high-resolution-timers-windows/

It is a library written in .net8.0 for windows

## QuickTickTimer

A timer class similar in use to the System.Timers.Timer but with the ability to go below 15.6ms interval without needing to set a
system clock rate using TimeBeginPeriod.
