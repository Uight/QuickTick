﻿using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace QuickTickLib;

[SupportedOSPlatform("windows")]
public class QuickTickTimer : IDisposable
{
    private readonly IntPtr iocpHandle;
    private readonly IntPtr waitIocpHandle;
    private readonly IntPtr timerHandle;
    private readonly IntPtr successCompletionKey = new IntPtr(1);
    private long intervalTicks;
    private double intervalMs;
    private bool autoReset;
    private bool isRunning;
    private long nextFireTime;
    private ThreadPriority priority = ThreadPriority.AboveNormal;

    private Thread? completionThread;
    public event QuickTickElapsedEventHandler? Elapsed;

    public double Interval
    {
        get => intervalMs;
        set
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException("Interval must be between 1 and int.MaxValue");
            }

            double roundedInterval = Math.Ceiling(value);
            if (roundedInterval > int.MaxValue || roundedInterval <= 0)
            {
                throw new ArgumentOutOfRangeException("Interval must be between 1 and int.MaxValue");
            }

            intervalMs = roundedInterval;
            intervalTicks = TimeSpan.FromMilliseconds(intervalMs).Ticks;
        }
    }

    public ThreadPriority Priority
    {
        get => priority;
        set
        {
            priority = value;
            if (completionThread != null)
            {
                completionThread.Priority = priority;
            }
        }
    }

    public bool AutoReset
    {
        get => autoReset;
        set => autoReset = value;
    }

    public const uint NtCreateWaitCompletionPacketAccessRights = (uint)Win32Interop.TimerAccessMask.TIMER_MODIFY_STATE | (uint)Win32Interop.TimerAccessMask.TIMER_QUERY_STATE;
    public const uint CreateWaitableTimerExWAccessRights = (uint)Win32Interop.TimerAccessMask.TIMER_MODIFY_STATE | (uint)Win32Interop.TimerAccessMask.SYNCHRONIZE;

    public QuickTickTimer(double interval)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            throw new PlatformNotSupportedException("QuickTickLib only works on windows");
        }

        Interval = interval;

        iocpHandle = Win32Interop.CreateIoCompletionPort(new IntPtr(-1), IntPtr.Zero, IntPtr.Zero, 0);
        if (iocpHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException($"CreateIoCompletionPort failed: {Marshal.GetLastWin32Error()}");
        }       

        int status = Win32Interop.NtCreateWaitCompletionPacket(out waitIocpHandle, NtCreateWaitCompletionPacketAccessRights, IntPtr.Zero);
        if (status != 0)
        {
            throw new InvalidOperationException($"NtCreateWaitCompletionPacket failed: {status:X8}");
        }       

        timerHandle = Win32Interop.CreateWaitableTimerExW(IntPtr.Zero, IntPtr.Zero, Win32Interop.CreateWaitableTimerFlag_HIGH_RESOLUTION, CreateWaitableTimerExWAccessRights);
        if (timerHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException($"CreateWaitableTimerExW failed: {Marshal.GetLastWin32Error()}");
        }
    }

    public QuickTickTimer(TimeSpan interval) : this(interval.TotalMilliseconds) {}

    public void Start()
    {
        if (isRunning) return;
 
        isRunning = true;

        nextFireTime = DateTime.UtcNow.Ticks + intervalTicks; // Compute first expiration

        SetTimer();

        completionThread = new Thread(CompletionThreadLoop)
        {
            IsBackground = true,
            Priority = priority // Allow to boost priority as it greatly increases timer precision
        };
        completionThread.Start();
    }

    public void Stop()
    {
        if (!isRunning)
        {
            return;
        }

        isRunning = false;
        if (timerHandle != IntPtr.Zero)
        {
            Win32Interop.CancelWaitableTimer(timerHandle);
        }

        // Post a dummy completion to get out of GetQueuedCompletionStatusEx in the completionThread. Use a key different to successCompletionKey
        Win32Interop.PostQueuedCompletionStatus(iocpHandle, 0, IntPtr.Zero, IntPtr.Zero);

        completionThread?.Join();
        completionThread = null;

        if (waitIocpHandle != IntPtr.Zero)
        {
            Win32Interop.NtCancelWaitCompletionPacket(waitIocpHandle, true);
        }
    }

    public void Dispose()
    {
        Stop();

        if (waitIocpHandle != IntPtr.Zero)
        {
            Win32Interop.CloseHandle(waitIocpHandle);
        }

        if (iocpHandle != IntPtr.Zero)
        {
            Win32Interop.CloseHandle(iocpHandle);
        }

        if (timerHandle != IntPtr.Zero)
        {
            Win32Interop.CloseHandle(timerHandle);
        }
    }

    private void SetTimer()
    {
        long dueTime = nextFireTime - DateTime.UtcNow.Ticks; // Calculate absolute expiration
        dueTime = dueTime < 0 ? 0 : -dueTime; // Ensure valid time

        if (!Win32Interop.SetWaitableTimer(timerHandle, ref dueTime, 0, IntPtr.Zero, IntPtr.Zero, false))
        {
            throw new InvalidOperationException($"SetWaitableTimer failed: {Marshal.GetLastWin32Error()}");
        }      

        int status = Win32Interop.NtAssociateWaitCompletionPacket(waitIocpHandle, iocpHandle, timerHandle, successCompletionKey, IntPtr.Zero, 0, IntPtr.Zero, out _);

        if (status != 0)
        {
            throw new InvalidOperationException($"NtAssociateWaitCompletionPacket failed: {status:X8}");
        }     
    }

    private void CompletionThreadLoop()
    {
        while (isRunning)
        {
            if (Win32Interop.GetQueuedCompletionStatus(iocpHandle, out _, out var lpCompletionKey, out _, uint.MaxValue))
            {
                if (lpCompletionKey == successCompletionKey)
                {
                    var actualFireTime = DateTime.UtcNow;
                    var scheduledFireTime = new DateTime(nextFireTime, DateTimeKind.Utc);

                    var elapsedEventArgs = new QuickTickElapsedEventArgs(actualFireTime, scheduledFireTime);

                    if (autoReset)
                    {
                        nextFireTime += intervalTicks;
                        SetTimer();
                    }
                    else
                    {
                        isRunning = false;
                        if (timerHandle != IntPtr.Zero)
                        {
                            Win32Interop.CancelWaitableTimer(timerHandle);
                        }
                    }

                    if (Elapsed is not null)
                    {
                        Elapsed(this, elapsedEventArgs);
                    }
                }
            }
        }
    }
}
