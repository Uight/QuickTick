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
it automatically falls back to the base .net functions on all platforms that are not Windows (10 or higher).

## QuickTickTimer Class

### Definition

Namespace: QuickTickLib

Provides a high-resolution timer using IO Completion Ports (IOCP) and `NtAssociateWaitCompletionPacket` for 
precise timing events under windows. Similar in use to the `System.Timers.Timer` class.

```csharp
public class QuickTickTimer : IDisposable
```

Inheritance `Object` -> `QuickTickTimer`

Implements `IDisposable`

### Example Usage

The following example shows the usage of the QuickTickTimer with an interval of 500ms.

```csharp
using QuickTickLib;

class Program
{
    static void Main()
    {
        using QuickTickTimer timer = new QuickTickTimer(500);
        timer.AutoReset = true;
        timer.Elapsed += Timer_Elapsed;
        timer.Start();

        Thread.Sleep(5000); // Run for 5 seconds
        timer.Stop();
    }

    private static void Timer_Elapsed(object sender, QuickTickElapsedEventArgs e)
    {
        Console.WriteLine($"Timer elapsed! Scheduled: {e.ScheduledTime:dd.MM.yyyy HH:mm:ss.fff}, Actual: {e.SignalTime:dd.MM.yyyy HH:mm:ss.fff}");
    }
}

// The example displays output like the following:
    // Timer elapsed! Scheduled: 05.03.2025 18:52:21.120, Actual: 05.03.2025 18:52:21.121
    // Timer elapsed! Scheduled: 05.03.2025 18:52:21.620, Actual: 05.03.2025 18:52:21.620
    // Timer elapsed! Scheduled: 05.03.2025 18:52:22.120, Actual: 05.03.2025 18:52:22.120
    // Timer elapsed! Scheduled: 05.03.2025 18:52:22.620, Actual: 05.03.2025 18:52:22.622
    // Timer elapsed! Scheduled: 05.03.2025 18:52:23.120, Actual: 05.03.2025 18:52:23.121
    // Timer elapsed! Scheduled: 05.03.2025 18:52:23.620, Actual: 05.03.2025 18:52:23.620
    // Timer elapsed! Scheduled: 05.03.2025 18:52:24.120, Actual: 05.03.2025 18:52:24.121
    // Timer elapsed! Scheduled: 05.03.2025 18:52:24.620, Actual: 05.03.2025 18:52:24.620
    // Timer elapsed! Scheduled: 05.03.2025 18:52:25.120, Actual: 05.03.2025 18:52:25.120
    // Timer elapsed! Scheduled: 05.03.2025 18:52:25.620, Actual: 05.03.2025 18:52:25.621
```

### Remarks

QuickTickTimer is a timer based on IO Completion Ports for high-precision timing.
Using the windows function `NtAssociateWaitCompletionPacket` it can go below the default 
windows timer resolution of 15.6ms without needing to set the system clock rate using `TimeBeginPeriod`.
The timer therefore has no influence on the remaining program and calls like `Thread.Sleep` or `Task.Delay` are not affected.

> [!IMPORTANT]
> The `QuickTickTimer` class is able to be used cross-platform. However on systems that are not windows it falls back to
> built in .net functions. Also it falls back to the built in .net function if it runs under a windows earlier that Windows 10 or equivilantly Windows Server 2016.
> For non windows platforms the built-in timers deliver a relatively high precision, as they are not
> based on the windows timer system and its default timing of ~15.6ms and therefor no specific implementation is needed.

> [!IMPORTANT]
> The elapsed event is fired on a completion thread. This thread is not the same as the thread that started the timer.
> Make sure the execution of you elapsed event does not take longer than the interval of the timer or consider 
> using a decoupling mechanism to process the event on a different thread.

This class implements the `IDisposable` interface. When you are finished using the timer, you should dispose of it to release all associated resources.

> [!Note]
> The actual timing accuracy of the timer is mostly based on the systems thread scheduler aswell as the systems kernel timing.
> On average the system takes around 300µs to signal the timer thread after the interval finished.
> On Windows, the thread that waits for the timer and handles the event code runs with the same priority as the thread that created the timer.
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

### Properties

#### IsQuickTickUsed

```csharp
public bool IsQuickTickUsed { get; }
```

Gets a value indicating whether the high-resolution QuickTick implementation is being used.

When true, the timer is backed by the QuickTickTimerImplementation, which offers higher precision and lower latency on windows.
When false, the platform does not support QuickTick, and the timer falls back to the QuickTickTimerFallback implementation.

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

- `QuickTickElapsedEventArgs`: Contains information about the scheduled and actual firing times.
    - **`SignalTime`** (`DateTime`): The actual time when the event was triggered. The time is a UTC-Timestamp.
    - **`ScheduledTime`** (`DateTime`): The originally scheduled time for the timer event. The time is a UTC-Timestamp.  

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
public static void Sleep(int sleepTimeMs)
```

Blocks the current thread for the specified number of milliseconds.
If the sleep time is less than or equal to 0ms, the function will behave the same as `Thread.Sleep()` 
will in this situation.

##### Exceptions

- `InvalidOperationException`: Thrown if system API calls fail during initialization.

#### Delay()

```csharp
public static async Task Delay(int milliseconds, CancellationToken cancellationToken = default)
public static async Task Delay(TimeSpan timeSpan, CancellationToken cancellationToken = default)
```

Asynchronously blocks the current thread for the specified duration. It allows cancellation via the CancellationToken.

- milliseconds and timeSpan specify the delay time.
- If cancellationToken is triggered, the delay will be canceled early.

##### Exceptions

- `ArgumentOutOfRangeException`: Thrown if `interval` or `timeSpan` is less than or equal to zero.
- `InvalidOperationException`: Thrown if system API calls fail during initialization.
