// Some parts of the code are inspired by György Kőszeg's HighRes Timer: https://github.com/koszeggy/KGySoft.CoreLibraries/blob/master/KGySoft.CoreLibraries/CoreLibraries/HiResTimer.cs

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace QuickTickLib;

internal sealed class QuickTickTimerImplementation : IQuickTickTimer
{
    private readonly QuickTickHandleResources handles;
    private static readonly IntPtr CancelCompletionKey = IntPtr.Zero;

    private volatile bool autoReset;
    private volatile bool skipMissedIntervals;
    private volatile bool running;
    private volatile float intervalMs;
    private long intervalTicks;
    private int currentRunKey;

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
            if (running)
            {
                return;
            }

            running = true;
            var cts = new CancellationTokenSource();

            var key = ++currentRunKey == 0 ? ++currentRunKey : currentRunKey;
            var runKey = new IntPtr(key);

            completionThread = new Thread(() => CompletionThreadLoop(cts, runKey))
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
            if (!running)
            {
                return;
            }

            running = false;
            cancellationTokenSource?.Cancel();
            cancellationTokenSource = null;

            Win32Interop.CancelWaitableTimer(handles.TimerHandle);

            if (Thread.CurrentThread != completionThread)
            {
                // Post a dummy completion to get out of GetQueuedCompletionStatusEx in the completionThread. Use a key different to successCompletionKey
                // If called from the same thread we know we are called from the event handler code therefore we are not waiting on an event and don't need to send a dummy completion
                Win32Interop.PostQueuedCompletionStatus(handles.IocpHandle, 0, CancelCompletionKey, IntPtr.Zero);

                completionThread?.Join();
            }
            completionThread = null;

            Win32Interop.NtCancelWaitCompletionPacket(handles.WaitIocpHandle, true);
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

    private void CompletionThreadLoop(CancellationTokenSource localCancellationTokenSource, IntPtr runKey)
    {
        var stopWatch = Stopwatch.StartNew();
        var nextFireTicks = Interlocked.Read(ref intervalTicks);
        var lastFireTicks = 0L;
        var skippedIntervals = 0L;

        SetTimer(stopWatch, nextFireTicks, runKey);

        while (!localCancellationTokenSource.IsCancellationRequested)
        {
            var getStatusResult = Win32Interop.GetQueuedCompletionStatus(handles.IocpHandle, out _, out var lpCompletionKey, out _, uint.MaxValue);

            if (localCancellationTokenSource.IsCancellationRequested || !getStatusResult)
            {
                break;
            }

            if (lpCompletionKey != runKey)
            {
                // Ignore stale packet from a previous run or old cancel events; Cancel packet only used to get out of wait the actual cancel is done via the cts
                // Cancel packets can still be present when the cancel is external and happens after the GetQueuedCompletionStatus call (e.g. while user code is running)
                continue; 
            }

            var currentTicks = stopWatch.ElapsedTicks;
            var lastFireTicksLocal = lastFireTicks;
            lastFireTicks = currentTicks;

            if (autoReset)
            {
                var interval = Interlocked.Read(ref intervalTicks);
                nextFireTicks += interval;

                if (skipMissedIntervals)
                {
                    while (nextFireTicks < currentTicks)
                    {
                        nextFireTicks += interval;
                        if (skippedIntervals < long.MaxValue)
                        {
                            skippedIntervals++;
                        }
                    }
                }

                if (stopWatch.Elapsed.TotalHours >= 1)
                {
                    var remaining = nextFireTicks - currentTicks;
                    stopWatch.Restart();
                    nextFireTicks = remaining;
                    lastFireTicks = 0;
                }

                SetTimer(stopWatch, nextFireTicks, runKey);
            }
            else
            {
                running = false; // Same logic as System.Timers.Timer: set running=false before invoking the handler when AutoReset is disabled
                localCancellationTokenSource.Cancel();
            }

            var timeSinceLastFire = TimeSpan.FromTicks(currentTicks - lastFireTicksLocal);
            var elapsedEventArgs = new QuickTickElapsedEventArgs(timeSinceLastFire, skippedIntervals);

            var handler = elapsed;
            handler?.Invoke(this, elapsedEventArgs);
        }
    }
    
    private void SetTimer(Stopwatch stopWatch, long nextFireTicks, IntPtr runKey)
    {
        long dueTime = nextFireTicks - stopWatch.ElapsedTicks;
        dueTime = dueTime < 0 ? 0 : -dueTime; // Negative = relative time for SetWaitableTimer

        if (!Win32Interop.SetWaitableTimer(handles.TimerHandle, ref dueTime, 0, IntPtr.Zero, IntPtr.Zero, false)) // Setting the period to 0 makes the timer fire exactly once
        {
            throw new InvalidOperationException($"SetWaitableTimer failed: {Marshal.GetLastWin32Error()}");
        }

        int status = Win32Interop.NtAssociateWaitCompletionPacket(handles.WaitIocpHandle, handles.IocpHandle, handles.TimerHandle, runKey, IntPtr.Zero, 0, IntPtr.Zero, out _);

        if (status != 0)
        {
            throw new InvalidOperationException($"NtAssociateWaitCompletionPacket failed: {status:X8}");
        }
    }
}
