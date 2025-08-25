using System.Diagnostics;

namespace QuickTickTimingReportGenerator;

public class CPUMonitor : IDisposable
{
    private readonly List<(DateTime Timestamp, double CpuUsage)> _metrics = new();
    private readonly Process _process = Process.GetCurrentProcess();
    private readonly int _processorCount = Environment.ProcessorCount;
    private CancellationTokenSource? _cts;
    private TimeSpan _lastTotalProcessorTime;
    private DateTime _lastSampleTime;

    public void Start(int intervalMs = 250)
    {
        _cts = new CancellationTokenSource();
        _lastTotalProcessorTime = _process.TotalProcessorTime;
        _lastSampleTime = DateTime.UtcNow;

        Task.Run(async () =>
        {
            while (!_cts!.IsCancellationRequested)
            {
                await Task.Delay(intervalMs, _cts.Token);
                RecordSample();
            }
        }, _cts.Token);
    }

    private void RecordSample()
    {
        DateTime now = DateTime.UtcNow;
        TimeSpan currentCpu = _process.TotalProcessorTime;

        double cpuUsage = (currentCpu - _lastTotalProcessorTime).TotalMilliseconds /
                          (now - _lastSampleTime).TotalMilliseconds *
                          100.0 / _processorCount;

        _metrics.Add((now, cpuUsage));

        _lastTotalProcessorTime = currentCpu;
        _lastSampleTime = now;
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    public double GetAverageCpuUsage()
    {
        if (_metrics.Count == 0)
            return 0.0;

        return _metrics.Average(m => m.CpuUsage);
    }

    public void Dispose() => Stop();
}
