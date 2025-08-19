# QuickTick

[![](https://img.shields.io/nuget/vpre/QuickTickLib?color=%23004880&label=NuGet&logo=NuGet)](https://www.nuget.org/packages/QuickTickLib/)
[![GitHub](https://img.shields.io/github/license/uight/quicktick?color=%231281c0)](LICENSE)

**QuickTick** is a high-precision timer library for **.NET 8.0** and **.NET Framework 4.8**, designed for scenarios where accurate and low-latency timing is required.

It is inspired by discussions in the [.NET Runtime issue #67088](https://github.com/dotnet/runtime/issues/67088) and is based 
on the **high-resolution timer** implemented by Microsoft's Go team, as detailed in [this blog post](https://devblogs.microsoft.com/go/high-resolution-timers-windows/).

QuickTick leverages **IO Completion Ports (IOCP)** and **NT system calls** on windows to achieve precise and efficient timing without needing to fiddle with the system clock rate.
It enables the creation of a `Timer` and the use of `Sleep` and `Delay` functions with precision below **15.6 ms**, ensuring accurate timing without impacting other parts of your application.
QuickTick only really has an effect when used on a windows systems. On most other platforms supported by **.NET 8.0** you dont really need a more precise timer, as
systems like linux dont have the **15.6 ms** limitation from the start. To allow the usage of QuickTick in cross-platform projects 
it automatically falls back to a fallback implementation that is a wrapper around the base .net functions with the same interface.

Since the system API's the QuickTick implementation uses are not available in Windows versions previous to Windows 10 and the fallback implementations
would also not work due to the **15.6 ms** limitation and therefor if the implementation is used under Windows versions previous to windows 10 a **PlatformNotSupportedException** is thrown.

## QuickTickTimer Class

### Definition

Namespace: QuickTickLib

Provides a high-resolution timer using IO Completion Ports (IOCP) and `NtAssociateWaitCompletionPacket` for 
precise timing events under windows. Similar in use to the `System.Timers.Timer` class.

```csharp
public class QuickTickTimer : IDisposable
```

Inheritance `Object` -> `QuickTickTimer`

Implements `IDisposable`, `IQuickTickTimer`

### Example Usage

The following example shows the usage of the QuickTickTimer with an interval of 500ms.

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
windows timer resolution of 15.6ms without needing to set the system clock rate using `TimeBeginPeriod`.
The timer therefore has no influence on the remaining program and calls like `Thread.Sleep` or `Task.Delay` are not affected.

> [!IMPORTANT]
> The `QuickTickTimer` class is able to be used cross-platform. However on systems that are not windows it falls back to
> a fallback implementation built on base .net functions, that provides the same interface.
> For non windows platforms the built-in timers deliver a relatively high precision, as they are not
> based on the windows timer system and its default timing of ~15.6ms and therefor no specific implementation is needed.

> [!IMPORTANT]
> The elapsed event is fired on a completion thread. This thread is not the same as the thread that started the timer.
> Make sure the execution of you elapsed event does not take longer than the interval of the timer or consider 
> using a decoupling mechanism to process the event on a different thread. This is also true for the fallback timer which altough based
> on `System.Timers.Timer`, which schedules to thread pool has a logic built in the fallback wrap that ques the events on the same thread.

This class implements the `IDisposable` interface. When you are finished using the timer, you should dispose of it to release all associated resources.

> [!Note]
> The actual timing accuracy of the timer is mostly based on the systems thread scheduler aswell as the system's kernel timing.
> On average the system takes around 300µs to signal the timer thread after the interval finished.
> The thread that waits for the timer and handles the event code normally runs with the `ThreadPriority.Normal`.
> This is normally fine and doesn’t need to be increased. Raising the priority is only recommended if the system is under heavy load and timing accuracy is noticeably affected.
> A better solution to inaccurate timing is checking your windows power settings and especially the core parking feature.
> Core parking can drastically worsen the times the timer thread needs to wake up when beaing signaled. You might want to turn that off.

> [!Note]
> The `QuickTickTimerImplementation` supports sub-millisecond intervals; however, the maximum effective resolution is approximately **0.5 milliseconds** due to Windows kernel timer limitations.  
> On the test machine, the minimum observed interval was around **0.518 ms**.
> You can still specify intervals that are not exact multiples of 0.5 ms — for example, **16.666... ms** for a 60 Hz timer — and the implementation will attempt to maintain that average over time.
>
> The timer strives to keep the **average interval** as close as possible to the requested value, but actual intervals may vary slightly depending on system load and other conditions.  
> Typically, deviations are within **±0.6 ms**, though in some cases they may be larger.
>
> The fallback timer while accepting sub millisecond intervals doesnt support it due to the nature of `System.Timers.Timer` rounding up to the next full millisecond interval.

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
If the timeout time is less than or equal to 0ms, the function will behave the same as `Thread.Sleep()` 
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
