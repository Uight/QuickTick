using System.Runtime.InteropServices;

namespace QuickTickLib;

internal static partial class LinuxInterop
{
    private const string LibcDll = "libc";

    private const int ClockMonotonic = 1; // CLOCK_MONOTONIC; same value on all Linux architectures
    private const int TimerAbstime = 1; // TIMER_ABSTIME flag for clock_nanosleep: request is an absolute deadline
    private const int Eintr = 4; // Interrupted by a signal; the sleep must simply be retried
    private const long NanosecondsPerSecond = 1_000_000_000;

    // tv_sec (time_t) and tv_nsec are C longs in the default ABI: 8 bytes on 64-bit Linux but 4 bytes on
    // 32-bit targets like linux-arm, so nint is the only field type that matches both layouts
    [StructLayout(LayoutKind.Sequential)]
    private struct Timespec
    {
        public nint tv_sec;
        public nint tv_nsec;
    }

#if NET
    [LibraryImport(LibcDll, EntryPoint = "clock_gettime", SetLastError = true)]
    private static partial int ClockGettime(
        int clockId, // The clock to read // CLOCK_MONOTONIC here, immune to wall-clock adjustments
        ref Timespec tp // Receives the current time of that clock
    );

    // No SetLastError: unlike most libc functions clock_nanosleep does not use errno, it returns the error number directly
    [LibraryImport(LibcDll, EntryPoint = "clock_nanosleep")]
    private static partial int ClockNanosleep(
        int clockId, // The clock to sleep against // CLOCK_MONOTONIC here
        int flags, // TIMER_ABSTIME to treat request as an absolute deadline // Makes EINTR retries lose no time
        ref Timespec request, // The deadline to sleep until
        IntPtr remain // Receives the remaining time when interrupted // Not needed with TIMER_ABSTIME, retry with the same deadline instead
    );
#else
    [DllImport(LibcDll, EntryPoint = "clock_gettime", SetLastError = true)]
    private static extern int ClockGettime(
        int clockId, // The clock to read // CLOCK_MONOTONIC here, immune to wall-clock adjustments
        ref Timespec tp // Receives the current time of that clock
    );

    // No SetLastError: unlike most libc functions clock_nanosleep does not use errno, it returns the error number directly
    [DllImport(LibcDll, EntryPoint = "clock_nanosleep")]
    private static extern int ClockNanosleep(
        int clockId, // The clock to sleep against // CLOCK_MONOTONIC here
        int flags, // TIMER_ABSTIME to treat request as an absolute deadline // Makes EINTR retries lose no time
        ref Timespec request, // The deadline to sleep until
        IntPtr remain // Receives the remaining time when interrupted // Not needed with TIMER_ABSTIME, retry with the same deadline instead
    );
#endif

    // A capability probe instead of an OS-name check: every Linux able to load a supported .NET runtime has
    // clock_nanosleep in libc (glibc since 2.17 — the runtime's own minimum —, musl and bionic always had it)
    // and FreeBSD comes along for free, while macOS lacks the symbol entirely. Probing once here keeps a missing
    // symbol from surfacing later as a DllNotFoundException on a timer worker thread.
    internal static bool PlatformSupportsPreciseSleep()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return false;
        }

        try
        {
            // An absolute deadline of 0 lies in the past, so this returns immediately; a zero result proves
            // both that the symbol resolves and that the monotonic clock accepts sleep requests
            var deadline = default(Timespec);
            return ClockNanosleep(ClockMonotonic, TimerAbstime, ref deadline, IntPtr.Zero) == 0;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    /// <summary>
    /// The ticksToSleep must be specified in <see cref="TimeSpan"/> ticks (100-nanosecond intervals),
    /// not <see cref="System.Diagnostics.Stopwatch"/> ticks, same as QuickTickTiming.QuickTickSleep.
    /// </summary>
    internal static void PreciseSleep(long ticksToSleep)
    {
        var now = default(Timespec);
        if (ClockGettime(ClockMonotonic, ref now) != 0)
        {
            // Cannot realistically fail for a probed, always-present clock; fail fast like SetWaitableTimer failures
            throw new InvalidOperationException($"clock_gettime failed: {Marshal.GetLastWin32Error()}");
        }

        // Sleep to an absolute deadline on the monotonic clock: a retry after EINTR reuses the same deadline
        // and therefore loses no time, unlike relative sleeps which would restart the full duration
        var totalNanoseconds = now.tv_nsec + ticksToSleep % TimeSpan.TicksPerSecond * 100;
        var deadline = new Timespec
        {
            tv_sec = (nint)(now.tv_sec + ticksToSleep / TimeSpan.TicksPerSecond + totalNanoseconds / NanosecondsPerSecond),
            tv_nsec = (nint)(totalNanoseconds % NanosecondsPerSecond)
        };

        int result;
        do
        {
            result = ClockNanosleep(ClockMonotonic, TimerAbstime, ref deadline, IntPtr.Zero);
        } while (result == Eintr);

        if (result != 0)
        {
            // Cannot realistically fail with a probed clock and valid arguments; fail fast like SetWaitableTimer failures
            throw new InvalidOperationException($"clock_nanosleep failed: {result}");
        }
    }
}
