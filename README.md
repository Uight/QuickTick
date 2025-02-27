# QuickTick

[![](https://img.shields.io/nuget/vpre/QuickTickLib?color=%23004880&label=NuGet&logo=NuGet)](https://www.nuget.org/packages/QuickTickLib/)
[![GitHub](https://img.shields.io/github/license/uight/quicktick?color=%231281c0)](LICENSE)

QuickTick is a small library based on what was discussed in a .net runtime issue on github: https://github.com/dotnet/runtime/issues/67088
It is based on the timer the microsoft Go team implemented and that was presented in: https://devblogs.microsoft.com/go/high-resolution-timers-windows/

It is a library written in .net8.0 for windows only

## QuickTickTimer

A timer class similar in use to the System.Timers.Timer but with the ability to go below 15.6ms interval without needing to set a
system clock rate using TimeBeginPeriod.

*Dont expect the timer to have perfect precision (Windows is not a RealTime System). Example: 
Setting a timer to 5ms leads to most iteration being around 5ms but some also way higher.
In my tests the timer was between 4 and 6ms for over 98% of all intervals but the absolute max was 29ms. 
However the timer keeps an average interval of exactly 5ms.* 

## QuickTickTiming

provides async Delay function based on the QuickTickTimer.
provides a sleep function similar to Thread.Sleep().

### TODO
* Check priority for the completionThread. 
* Decouple the completionThread from invoking the TimerElapsed events. Could allow higher prio of completion thread 
	=> (use a seperate processing thread? couple with ConcurrentQueue<Action> eventQueue and AutoResetEvent?)
    => Use SynchronizingObject?
