# QuickTick

[![](https://img.shields.io/nuget/vpre/QuickTickLib?color=%23004880&label=NuGet&logo=NuGet)](https://www.nuget.org/packages/QuickTickLib/)
[![GitHub](https://img.shields.io/github/license/uight/quicktick?color=%231281c0)](LICENSE)

**QuickTick** is a high-precision timer library for **.NET 8.0 (Windows only)**, designed for scenarios where accurate and low-latency timing is required.

It is inspired by discussions in the [.NET Runtime issue #67088](https://github.com/dotnet/runtime/issues/67088) and is based 
on the **high-resolution timer** implemented by Microsoft's Go team, as detailed in [this blog post](https://devblogs.microsoft.com/go/high-resolution-timers-windows/).

QuickTick leverages **IO Completion Ports (IOCP)** and **NT system calls** to achieve precise and efficient timing without needing to fiddle with the system clock rate.
It enables the creation of a `Timer` and the use of `Sleep` and `Delay` functions with precision below **15.6 ms**, ensuring accurate timing without impacting other parts of your application.

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
        Console.WriteLine($"Timer elapsed! Scheduled: {e.ScheduledTime}, Actual: {e.SignalTime}");
    }
}

// The example displays output like the following:
    //Timer elapsed! Scheduled: 05.03.2025 18:52:21, Actual: 05.03.2025 18:52:21
    //Timer elapsed! Scheduled: 05.03.2025 18:52:22, Actual: 05.03.2025 18:52:22
    //Timer elapsed! Scheduled: 05.03.2025 18:52:22, Actual: 05.03.2025 18:52:22
    //Timer elapsed! Scheduled: 05.03.2025 18:52:23, Actual: 05.03.2025 18:52:23
    //Timer elapsed! Scheduled: 05.03.2025 18:52:23, Actual: 05.03.2025 18:52:23
    //Timer elapsed! Scheduled: 05.03.2025 18:52:24, Actual: 05.03.2025 18:52:24
    //Timer elapsed! Scheduled: 05.03.2025 18:52:24, Actual: 05.03.2025 18:52:24
    //Timer elapsed! Scheduled: 05.03.2025 18:52:25, Actual: 05.03.2025 18:52:25
    //Timer elapsed! Scheduled: 05.03.2025 18:52:25, Actual: 05.03.2025 18:52:25
    //Timer elapsed! Scheduled: 05.03.2025 18:52:26, Actual: 05.03.2025 18:52:26
```

### Remarks

QuickTickTimer is a timer based on IO Completion Ports for high-precision timing.
Using the windows function `NtAssociateWaitCompletionPacket` it can go below the default 
windows timer resolution of 15.6ms without needing to set the system clock rate using `TimeBeginPeriod`.
The timer therefore has no influence on the remaining program and calls like `Thread.Sleep` or `Task.Delay` are not affected.

> [!IMPORTANT]
> The `QuickTickTimer` class is only available on Windows. It is not compatible with other platforms.
> If cross-platform compatibility is required, consider using one of the built-in .net timers instead.
> For non windows platforms the built-in timers deliver a relatively high precision as well, as they are not
> based on the windows timer system and its default timing of ~15.6ms.

> [!IMPORTANT]
> The elapsed event is fired on a completion thread. This thread is not the same as the thread that started the timer.
> Make sure the execution of you elapsed event does not take longer than the interval of the timer or consider 
> using a decoupling mechanism to process the event on a different thread.

This class implements the `IDisposable` interface. When you are finished using the timer, you should dispose of it to release all associated resources.

> [!Note]
> The actual timing accuracy of the timer is mostly based on the systems thread scheduler.
> On average the system takes around 300µs to signal the timer thread after the interval finished.
> This can change based on the system load and other factors like energy saving settings.
> To improve the timing accuracy of the timer you can set the `Priority` property to `ThreadPriority.Highest`.

The timer tries to keep the average interval as close to the specified interval as possible. 
But the actual interval can vary based on the system load and other factors. 
Typically, the timer is within 0.6ms of the specified interval, but it can be off by several milliseconds in some cases.

### Constructors

#### QuickTickTimer(double interval)

```csharp
public QuickTickTimer(double interval)
```

Initializes a new instance of the `QuickTickTimer` class with the specified interval in milliseconds.

##### Parameters

- `interval` (Double): The timer interval in milliseconds. Must be greater than zero.

##### Exceptions

- `ArgumentOutOfRangeException`: Thrown if `interval` is less than or equal to zero.
- `PlatformNotSupportedException`: Thrown if the platform is not Windows.
- `InvalidOperationException`: Thrown if system API calls fail during initialization.

#### QuickTickTimer(TimeSpan interval)

```csharp
public QuickTickTimer(TimeSpan interval)
```

Initializes a new instance of the `QuickTickTimer` class with the specified interval as a `TimeSpan`.

##### Parameters

- `interval` (TimeSpan): The timer interval.

##### Exceptions

- `ArgumentOutOfRangeException`: Thrown if `interval` is less than or equal to zero.
- `PlatformNotSupportedException`: Thrown if the platform is not Windows.
- `InvalidOperationException`: Thrown if system API calls fail during initialization.

### Properties

#### Interval

```csharp
public double Interval { get; set; }
```

Gets or sets the time, in milliseconds, between timer events.

##### Exceptions

- `ArgumentOutOfRangeException`: Thrown if the value is less than or equal to zero.

#### AutoReset

```csharp
public bool AutoReset { get; set; }
```

Gets or sets whether the timer should restart after each elapsed event.

- `true`: The timer restarts automatically.
- `false`: The timer stops after firing once.

#### Priority

```csharp
public ThreadPriority Priority { get; set; }
```

Gets or sets the priority of the completion thread handling timer events.

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
    - **`SignalTime`** (`DateTime`): The actual time when the event was triggered.
    - **`ScheduledTime`** (`DateTime`): The originally scheduled time for the timer event.  

## QuickTickTiming Class

### Definition

Namespace: QuickTickLib

Provides sleep and delay functions with high precision using IO Completion Ports (IOCP) and `NtAssociateWaitCompletionPacket` for precise timing events under windows.

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

- `PlatformNotSupportedException`: Thrown if the platform is not Windows.
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

- `ArgumentOutOfRangeException`: Thrown if `interval` or `timeSpan` is less than or equal to zero milliseconds.
- `PlatformNotSupportedException`: Thrown if the platform is not Windows.
- `InvalidOperationException`: Thrown if system API calls fail during initialization.