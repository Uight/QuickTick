// Some parts of the code are inspired by György Kőszeg's HighRes Timer: https://github.com/koszeggy/KGySoft.CoreLibraries/blob/master/KGySoft.CoreLibraries/CoreLibraries/HiResTimer.cs

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace QuickTickLib;

internal sealed class QuickTickTimerImplementation : IQuickTickTimer
{
    private readonly QuickTickHandleResources handles;
    private static readonly IntPtr SuccessCompletionKey = new(1);

    private volatile bool autoReset;
    private volatile bool skipMissedIntervals;
    private volatile bool isRunning;
    private volatile float intervalMs;
    private long intervalTicks;
    private long nextFireTicks;
    private long lastFireTicks;
    private long totalSkippedIntervals;
    private readonly Stopwatch stopWatch = new();

    private int disposedState;
    private readonly object stateLock = new();

    private CancellationTokenSource? cancellationTokenSource;
    private Thread? completionThread;
    private ThreadPriority threadPriority = ThreadPriority.Normal;
    private QuickTickElapsedEventHandler? elapsed;

    public event QuickTickElapsedEventHandler? Elapsed
    {
        add => elapsed += value;
        remove => elapsed -= value;
    }

    public double Interval
    {
        get => intervalMs;
        set
        {
            if (value > int.MaxValue || value < 0.5 || double.IsNaN(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Interval must be between 0.5 and int.MaxValue");
            }

            intervalMs = (float)value;
            Interlocked.Exchange(ref intervalTicks, (long)(intervalMs * TimeSpan.TicksPerMillisecond));
        }
    }

    public bool AutoReset
    {
        get => autoReset;
        set => autoReset = value;
    }

    public bool SkipMissedIntervals
    {
        get => skipMissedIntervals;
        set => skipMissedIntervals = value;
    }

    public ThreadPriority Priority
    {
        get => threadPriority;
        set
        {
            threadPriority = value;
            if (completionThread is { IsAlive: true })
            {
                completionThread.Priority = value;
            }           
        }
    }

    public QuickTickTimerImplementation(double interval)
    {
        AutoReset = true;
        Interval = interval;

        handles = new QuickTickHandleResources();
    }

    public void Start()
    {
        lock (stateLock)
        {
            if (isRunning)
            {
                return;
            }
            stopWatch.Restart();

            isRunning = true;

            Interlocked.Exchange(ref nextFireTicks, Interlocked.Read(ref intervalTicks));
            Interlocked.Exchange(ref totalSkippedIntervals, 0L);
            Interlocked.Exchange(ref lastFireTicks, 0L);

            SetTimer();

            var cts = new CancellationTokenSource();

            completionThread = new Thread(() => CompletionThreadLoop(cts))
            {
                IsBackground = true,
                Priority = Priority
            };

            cancellationTokenSource = cts;
            completionThread.Start();
        }
    }

    public void Stop()
    {
        lock (stateLock)
        {
            if (!isRunning)
            {
                return;
            }

            isRunning = false;
            cancellationTokenSource?.Cancel();
            cancellationTokenSource = null;

            Win32Interop.CancelWaitableTimer(handles.TimerHandle);

            if (Thread.CurrentThread != completionThread)
            {
                // Post a dummy completion to get out of GetQueuedCompletionStatusEx in the completionThread. Use a key different to successCompletionKey
                // If called from the same thread we know we are called from the event handler code therefore we are not waiting on an event and don't need to send a dummy completion
                Win32Interop.PostQueuedCompletionStatus(handles.IocpHandle, 0, IntPtr.Zero, IntPtr.Zero);

                completionThread?.Join();
            }
            completionThread = null;

            Win32Interop.NtCancelWaitCompletionPacket(handles.WaitIocpHandle, true);
           
            stopWatch.Reset();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposedState, 1) == 1)
        {
            return;
        }

        try
        {
            Stop();
        }
        finally
        {
            elapsed = null;
            handles.Dispose();
        }
    }

    private void SetTimer()
    {
        long dueTime = Interlocked.Read(ref nextFireTicks) - stopWatch.ElapsedTicks; // Calculate absolute expiration
        dueTime = dueTime < 0 ? 0 : -dueTime; // Ensure valid time

        if (!Win32Interop.SetWaitableTimer(handles.TimerHandle, ref dueTime, 0, IntPtr.Zero, IntPtr.Zero, false)) // Setting the period to 0 makes the timer fire exactly once
        {
            throw new InvalidOperationException($"SetWaitableTimer failed: {Marshal.GetLastWin32Error()}");
        }

        int status = Win32Interop.NtAssociateWaitCompletionPacket(handles.WaitIocpHandle, handles.IocpHandle, handles.TimerHandle, SuccessCompletionKey, IntPtr.Zero, 0, IntPtr.Zero, out _);

        if (status != 0)
        {
            throw new InvalidOperationException($"NtAssociateWaitCompletionPacket failed: {status:X8}");
        }
    }

    private void CompletionThreadLoop(CancellationTokenSource localCancellationTokenSource)
    {
        while (!localCancellationTokenSource.IsCancellationRequested)
        {
            var getStatusResult = Win32Interop.GetQueuedCompletionStatus(handles.IocpHandle, out _, out var lpCompletionKey, out _, uint.MaxValue);

            // Check if we were canceled while waiting (external cancel / cancel from other thread). The secondary check should not be needed
            if (localCancellationTokenSource.IsCancellationRequested || !getStatusResult)
            {
                break;
            }

            if (lpCompletionKey != SuccessCompletionKey)
            {
                continue; // In rare timing cases e.g. when external Stop is called while in user code the old invalid completion packet to stop the wait might still be present so we ignore it;
            }

            var currentTicks = stopWatch.ElapsedTicks;
            var lastFireTicksLocal = Interlocked.Read(ref lastFireTicks);
            Interlocked.Exchange(ref lastFireTicks, currentTicks);
            var skippedIntervals = Interlocked.Read(ref totalSkippedIntervals);

            if (autoReset)
            {
                var interval = Interlocked.Read(ref intervalTicks);
                var nextTicks = Interlocked.Add(ref nextFireTicks, interval);           

                if (skipMissedIntervals)
                {
                    while (nextTicks < currentTicks)
                    {
                        nextTicks = Interlocked.Add(ref nextFireTicks, interval);
                        if (skippedIntervals < long.MaxValue)
                        {
                            skippedIntervals++;
                        }
                    }
                    Interlocked.Exchange(ref totalSkippedIntervals, skippedIntervals);
                }

                if (stopWatch.Elapsed.TotalHours >= 1)
                {
                    var remaining = nextTicks - currentTicks;
                    stopWatch.Restart();
                    Interlocked.Exchange(ref nextFireTicks, remaining);
                    Interlocked.Exchange(ref lastFireTicks, 0L);
                }

                SetTimer();
            }
            else
            {
                isRunning = false; // Same logic as in System.Timers.Timer resetting "Enabled" (here: isRunning) back before running the handler if autoReset is disabled
                stopWatch.Reset();
                localCancellationTokenSource.Cancel();
            }
    
            var timeSinceLastFire = TimeSpan.FromTicks(currentTicks - lastFireTicksLocal);
            var elapsedEventArgs = new QuickTickElapsedEventArgs(timeSinceLastFire, skippedIntervals);

            var handler = elapsed;
            handler?.Invoke(this, elapsedEventArgs);
        }
    }
}