# QuickTick

[![](https://img.shields.io/nuget/vpre/QuickTickLib?color=%23004880&label=NuGet&logo=NuGet)](https://www.nuget.org/packages/QuickTickLib/)
[![GitHub](https://img.shields.io/github/license/uight/quicktick?color=%231281c0)](LICENSE)

**QuickTick** is a high-precision timer library for **.NET 8.0** and **.NET Framework 4.8**, designed for scenarios where accurate and low-latency timing is required.

It is inspired by discussions in the [.NET Runtime issue #67088](https://github.com/dotnet/runtime/issues/67088) and is based 
on the **high-resolution timer** implemented by Microsoft's Go team, as detailed in [this blog post](https://devblogs.microsoft.com/go/high-resolution-timers-windows/).

QuickTick leverages **IO Completion Ports (IOCP)** and **NT system calls** on windows to achieve precise and efficient timing without needing to fiddle with the system clock rate.
It enables the creation of a `Timer` and the use of `Sleep` and `Delay` functions with precision below **15.6 ms**, ensuring accurate timing without impacting other parts of your application.

## Supported OS

QuickTick is designed primarily to improve timer resolution on Windows systems, where the default system timer has a resolution of 15.6 ms.
On Linux and most other .NET 8.0 supported platforms, this limitation does not exist, so QuickTick automatically falls back to a wrapper around the base .NET timers while keeping the same interface.

#### Windows Support

✅ Windows 11

✅ Windows Server 2019 and newer

✅ Windows 10, version 1803 or newer

QuickTick relies on specific system APIs that are only available starting with Windows 10 (1803). This is explicitly the `CreateWaitableTimerExW` function which received high resolution support in Windows 10 version 1803.

On older Windows versions, QuickTick cannot function and will throw a PlatformNotSupportedException.

#### Linux Support

✅ Fully supported (no 15.6 ms limitation).

QuickTick falls back to the standard .NET timers, which already provide high precision. All the functions the library provide can be used without needing to change settings.

#### macOS Support

⚠️ Supported, meaning the timer will run under macOS but the base .Net functions the timer falls back to are not very precise.

See the macOS timing report for more details. 

For best results, use HighResQuickTickTimer with adjusted settings. The `SleepThreshold` must be set to around 15ms to get accurate timing. This effectivly burns a whole CPU core to hold a precise timing.

## Performance Reports

This are some performance reports of the QuickTickTiming.Sleep() Function aswell as for the QuickTickTimer and the HighResQuickTickTimer including a comparison to the HighResTimer by György Kőszeg [found here](https://github.com/koszeggy/KGySoft.CoreLibraries/blob/master/KGySoft.CoreLibraries/CoreLibraries/HiResTimer.cs)

[Win 11 Energy Saving Normal Prio](https://github.com/Uight/QuickTick/tree/main/QuickTickTimingReportGenerator/Reports/QuickTick_Report_Win11_NormalPrio_EnergySaving.pdf)  
[Win 11 Ultimate Power Highest Prio](https://github.com/Uight/QuickTick/tree/main/QuickTickTimingReportGenerator/Reports/QuickTick_Report_Win11_HighestPrio_UltimatePower.pdf)  
[Ubuntu 24.04.3 LTS Highest Prio](https://github.com/Uight/QuickTick/tree/main/QuickTickTimingReportGenerator/Reports/QuickTick_Report_Ubuntu_24_04_3_HighestPrio.pdf)

## QuickTickTimer Class

### Definition

Namespace: QuickTickLib

Provides a high-resolution timer using IO Completion Ports (IOCP) and `NtAssociateWaitCompletionPacket` for 
precise timing events under windows. Similar in use to the `System.Timers.Timer` class.

```csharp
public class QuickTickTimer : IDisposable, IQuickTickTimer
```

Inheritance `Object` -> `QuickTickTimer`

Implements `IDisposable`, `IQuickTickTimer`

### Example Usage

The following example shows the usage of the QuickTickTimer with an interval of 500 ms.

```csharp
using QuickTickLib;

class Program
{
    static void Main()
    {
        using QuickTickTimer timer = new QuickTickTimer(500);
        timer.SkipMissedIntervals = false;
        timer.AutoReset = true;
        timer.Elapsed += Timer_Elapsed;
        timer.Start();

        Thread.Sleep(5000); // Run for 5 seconds
        timer.Stop();
    }

    private static void TimerElapsed(object? sender, QuickTickElapsedEventArgs elapsedArgs)
    {
        Console.WriteLine($"Now: '{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.ffffff}'; " +
                          $"Time since last tick: '{elapsedArgs.TimeSinceLastInterval.TotalMilliseconds}'; " +
                          $"Skipped: '{elapsedArgs.SkippedIntervals}';");
    }
}

// The example displays output like the following:
    // Now: '2025-08-19 11:29:39.882346'; Time since last tick: '4,699'; Skipped: '0';
    // Now: '2025-08-19 11:29:39.887346'; Time since last tick: '5,0038'; Skipped: '0';
    // Now: '2025-08-19 11:29:39.892444'; Time since last tick: '5,0977'; Skipped: '0';
    // Now: '2025-08-19 11:29:39.897339'; Time since last tick: '4,8969'; Skipped: '0';
    // Now: '2025-08-19 11:29:39.902344'; Time since last tick: '5,0008'; Skipped: '0';
    // Now: '2025-08-19 11:29:39.907342'; Time since last tick: '5,0019'; Skipped: '0';
    // Now: '2025-08-19 11:29:39.912508'; Time since last tick: '5,166'; Skipped: '0';
    // Now: '2025-08-19 11:29:39.917512'; Time since last tick: '5,0029'; Skipped: '0';
    // Now: '2025-08-19 11:29:39.922342'; Time since last tick: '4,8312'; Skipped: '0';
    // Now: '2025-08-19 11:29:39.927346'; Time since last tick: '5,0031'; Skipped: '0';
    // Now: '2025-08-19 11:29:39.932344'; Time since last tick: '4,9945'; Skipped: '0';
    // Now: '2025-08-19 11:29:39.937572'; Time since last tick: '5,2314'; Skipped: '0';
```

### Remarks

QuickTickTimer is a timer based on IO Completion Ports for high-precision timing.
Using the windows function `NtAssociateWaitCompletionPacket` it can go below the default 
windows timer resolution of 15.6 ms without needing to set the system clock rate using `TimeBeginPeriod`.
The timer therefore has no influence on the remaining program and calls like `Thread.Sleep` or `Task.Delay` are not affected.

> [!IMPORTANT]
> The `QuickTickTimer` class is able to be used cross-platform. However on systems that are not windows it falls back to
> a fallback implementation built on base .net functions, that provides the same interface.
> For non windows platforms the accuracy of the functions soly rely on the accurancy of the base `.Net` functions.
> For some platforms like Linux this works great. But for others there might be limitatons just as windows has them with the 15.6 ms timing.
> macOS for example seems to have a minimum Sleep time of around 10 ms. (See [Supported Platforms](#supported-platforms))

> [!IMPORTANT]
> The elapsed event is fired on a completion thread. This thread is not the same as the thread that started the timer.
> Make sure the execution of you elapsed event does not take longer than the interval of the timer or consider 
> using a decoupling mechanism to process the event on a different thread. This is also true for the fallback timer which altough based
> on `System.Timers.Timer`, which schedules to thread pool has a logic built in the fallback wrap that ques the events on a single event thread.

This class implements the `IDisposable` interface. When you are finished using the timer, you should dispose of it to release all associated resources.

> [!Note]
> The actual timing accuracy of the timer on **Windows** is mostly based on the systems thread scheduler aswell as the system's kernel timing.
> On average the system takes around 300 µs to signal the timer thread after the interval finished.
> The thread that waits for the timer and handles the event code normally runs with the `ThreadPriority.Normal`.
> This is normally fine and doesn’t need to be increased. Raising the priority is only recommended if the system is under heavy load and timing accuracy is noticeably affected.
> A better solution to inaccurate timing is checking your windows power settings and especially the core parking feature.
> Core parking can drastically worsen the times the timer thread needs to wake up when being signaled. You might want to turn that off.

> [!Note]
> The `QuickTickTimerImplementation` supports sub-millisecond intervals; however, the maximum effective resolution is approximately **0.5 milliseconds** due to Windows kernel timer limitations.  
> On the test machine, the minimum observed interval was around **0.518 ms**.
> You can still specify intervals that are not exact multiples of 0.5 ms — for example, **16.666... ms** for a 60 Hz timer — and the implementation will attempt to maintain that average over time.
>
> The timer strives to keep the **average interval** as close as possible to the requested value, but actual intervals may vary slightly depending on system load and other conditions.  
> Typically, deviations are within **±0.6 ms**, though in some cases they may be larger.
>
> For the **fallback timer sub millisecond intervals aren't supported** due to the nature of `System.Timers.Timer` rounding up to the next full millisecond interval. You can still specify 0.5 ms as an interval but the timer will just run with a timing of 1 ms instead.

### Constructors

#### QuickTickTimer(double interval)

```csharp
public QuickTickTimer(double interval)
```

Initializes a new instance of the `QuickTickTimer` class with the specified interval in milliseconds.

##### Parameters

- `interval` (Double): The timer interval in milliseconds.

##### Exceptions

- `ArgumentOutOfRangeException`: Thrown if `interval` is outside the valid range:
    - For QuickTick: must be ≥ 0.5 milliseconds and ≤ int.MaxValue milliseconds
    - For Fallback: must be ≥ 1 millisecond and ≤ int.MaxValue milliseconds
- `InvalidOperationException`: Thrown if system API calls fail during initialization.
- `PlatformNotSupportedException`: Throw if you try to run QuickTickLib under windows versions below version 10.

#### QuickTickTimer(TimeSpan interval)

```csharp
public QuickTickTimer(TimeSpan interval)
```

Initializes a new instance of the `QuickTickTimer` class with the specified interval as a `TimeSpan`.
Be aware that using TimeSpan.FromMilliseconds() allready rounds to full milliseconds so if you want
tp create a timer with sub millisecond timing you would need to create the timeSpan with TimeSpan.FromMicroseconds()

##### Parameters

- `interval` (TimeSpan): The timer interval.

##### Exceptions

- `ArgumentOutOfRangeException`: Thrown if `interval` is outside the valid range:
    - For QuickTick: must be ≥ 0.5 milliseconds and ≤ int.MaxValue milliseconds
    - For Fallback: must be ≥ 1 millisecond and ≤ int.MaxValue milliseconds
- `InvalidOperationException`: Thrown if system API calls fail during initialization.
- `PlatformNotSupportedException`: Throw if you try to run QuickTickLib under windows versions below version 10.

### Properties

#### IsQuickTickUsed

```csharp
public bool IsQuickTickUsed { get; }
```

Gets a value indicating whether the high-resolution QuickTick implementation is being used.

> [!Note]
> Not part of the `IQuickTickTimer` interface; Only available from the class directly

- `true`: The timer is backed by the QuickTickTimerImplementation, which offers higher precision and lower latency on windows.
- `false`: The platform does not support QuickTick, and the timer falls back to the QuickTickTimerFallback implementation. (So true on Linux for example)

#### Interval

```csharp
public double Interval { get; set; }
```

Gets or sets the time, in milliseconds, between timer events.

##### Exceptions

- `ArgumentOutOfRangeException`: Thrown if value is outside the valid range:
    - For QuickTick: must be ≥ 0.5 milliseconds and ≤ int.MaxValue milliseconds
    - For Fallback: must be ≥ 1 millisecond and ≤ int.MaxValue milliseconds

#### AutoReset

```csharp
public bool AutoReset { get; set; }
```

Gets or sets whether the timer should restart after each elapsed event. Default is true.

- `true`: The timer restarts automatically.
- `false`: The timer stops after firing once.

#### Priority

```csharp
public ThreadPriority Priority { get; set; }
```

Gets or sets the thread priority for the thread handling the timer events and event code. Default is ThreadPriority.Normal.

#### SkipMissedIntervals

```csharp
public bool SkipMissedIntervals { get; set; }
```

Gets or sets whether the timer should skip missed ticks. Default is false.

- `true`: When the timer event fires and the last event is still being processed the event is ignored and the SkippedIntervals counter is increased.
- `false`: When the timer event fires and the last event is still being processed the event is scheduled immediatly and will start as soon as the running event is finished.
           Can lead to burst of events if the system or the user event code did have a hickup. 
 
### Methods

#### Start()

```csharp
public void Start()
```

Starts the timer.

##### Exceptions

- `InvalidOperationException`: Thrown if system API calls fail when setting the timer.

#### Stop()

```csharp
public void Stop()
```

Stops the timer if it is currently running.

#### Dispose()

```csharp
public void Dispose()
```

Stops the timer and releases all associated system resources.

### Events

#### Elapsed

```csharp
public event QuickTickElapsedEventHandler Elapsed;
```

Occurs when the timer interval has elapsed.

##### Event Arguments

- `QuickTickElapsedEventArgs`: Contains information about the current interval of the timer and the SkippedTicks.
    - **`TimeSinceLastInterval`** (`TimeSpan`): The time since the last event was triggered. If its the first interval it is the time since the start of the timer.
    - **`SkippedIntervals`** (`long`): The amount of skipped intervals since the timer was last started. Is always zero if `SkipMissedIntervals` is disabled. It is clamped to `long.MaxValue` and doesnt overflow.  


## HighResQuickTickTimer Class

### Definition

Namespace: QuickTickLib

This is a high resolution timer based on the `QuickTickTiming.Sleep` function aswell as the HighResTimer by György Kőszeg [found here](https://github.com/koszeggy/KGySoft.CoreLibraries/blob/master/KGySoft.CoreLibraries/CoreLibraries/HiResTimer.cs).
The timer provides the same interface as the normal QuickTickTimer but functions completly different internally. In short the timer is a loop that always checks the time
and according to the remaining time to the next interval either sleeps, yields the thread or spin waits. It therefore needs more CPU than the regular timer, but
also has a higher precision in the timing events.

For timers with intervals over about 2.5 ms this timer behaves like the HighResTimer by György Kőszeg but needs way less CPU.
This timer also fully **supports sub millisecond intervals on all systems** as it doesn't rely on the `System.Timers.Timer` implementation.

> [!Note]
> By design this timer uses a whole thread if you set a interval below around 2.5 ms as it then doesn't sleep and instead spin waits only. 

```csharp
public class HighResQuickTickTimer : IDisposable, IQuickTickTimer
```

Inheritance `Object` -> `HighResQuickTickTimer`

Implements `IDisposable`, `IQuickTickTimer`

### Differences to QuickTickTimer

#### Priority

This timer always starts with `ThreadPriority.Highest`.

#### IsQuickTickUsed

Does not implement this property as it isn't part of the IQuickTickTimer interface. Also there is no fallback implementation for this timer. Just
the way it sleeps is switched on the different operating systems.

#### IsQuickTickUsed

```csharp
public double SleepThreshold { get; set }
```

Gets or sets the value used to determine when to start sleeping between timer ticks. Default is 2.0 ms;
You can increase the value to improve timing accuracy but this will cost more CPU.

> [!Note]
> If a sleep is to be performed a function called `MinimalSleep` sleep is called. This sleeps for 1 ms using `Thread.Sleep()` under non windows 
> systems and for 500 µs using a special `QuickTickTiming.QuickTickSleep()` function under windows. Testing for linux and windows showed, 
> that in both cases the actual sleep time stays under 2.0 ms in over 99% of all cases which is why 2.0 ms is the default sleep time.
> An even safer value is 2.5 ms as in testing 99.9% of all minimal sleep timings stayed under 2.5 ms.
> For systems like macOS where the accurancy of `Thread.Sleep()` is way worse than under linux a good value for the `SleepThreshold` is around 15 ms,
> altough it should be remembered, that this will basically use a full core.

> [!Note]
> Not part of the `IQuickTickTimer` interface; Only available from the class directly


## QuickTickTiming Class

### Definition

Namespace: QuickTickLib

Provides sleep and delay functions with high precision using IO Completion Ports (IOCP) and `NtAssociateWaitCompletionPacket` for precise timing events under windows.
Running under different platforms it just falls back to the built in .net functions.

```csharp
public static class QuickTickTiming
```

Inheritance `Object` -> `QuickTickTiming`

### Methods

#### Sleep()

```csharp
public static void Sleep(int millisecondsTimeout)
```

Blocks the current thread for the specified number of milliseconds.
If the timeout time is less than or equal to 0 ms, the function will behave the same as `Thread.Sleep()` 
will in this situation.

##### Exceptions

- `ArgumentOutOfRangeException`: Thrown if `millisecondsTimeout` less than zero.
- `InvalidOperationException`: Thrown if system API calls fail during initialization.
- `PlatformNotSupportedException`: Throw if you try to run QuickTickLib under windows versions below version 10.

#### Delay()

```csharp
public static async Task Delay(int millisecondsDelay, CancellationToken cancellationToken = default)
public static async Task Delay(TimeSpan delay, CancellationToken cancellationToken = default)
```

Asynchronously blocks the current thread for the specified duration. It allows cancellation via the CancellationToken.

- millisecondsDelay and delay specify the delay time.
- If cancellationToken is triggered, the delay will be canceled early.

##### Exceptions

- `ArgumentOutOfRangeException`: Thrown if `millisecondsDelay` or `delay` is less than zero.
- `InvalidOperationException`: Thrown if system API calls fail during initialization.
- `TaskCanceledException`: If the cancellation token was cancelled during the delay phase.
- `ObjectDisposedException`: The provided `cancellationToken` has already been disposed.
- `PlatformNotSupportedException`: Throw if you try to run QuickTickLib under windows versions below version 10.
