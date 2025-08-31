using KGySoft.CoreLibraries;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using QuickTickLib;
using SkiaSharp;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace QuickTickTimingReportGenerator;

class Program
{
    static async Task Main(string[] args)
    {
        var reportDir = Path.Combine(Directory.GetCurrentDirectory(), $"QuickTickReport_{DateTime.Now:yyyyMMdd_HHmmss}");
        Directory.CreateDirectory(reportDir);
        QuestPDF.Settings.License = LicenseType.Community;

        var config = TestConfig.LoadConfig("config.json");

        var allResults = new List<TimingTestResult>();

        foreach (var duration in config.IntervalsMs)
        {
            var iterations = (int)(config.TimeInSecondsPerTest * 1000 / Math.Max(0.5 , duration));

            if (config.EnabledTests[TestType.QuickTickSleep])
            {
                Thread.Sleep(500);
                // QuickTick Sleep
                Console.WriteLine($"Running QuickTick Sleep test for {iterations} iterations with {duration}ms...");

                var sleepMonitor = new CPUMonitor();
                sleepMonitor.Start();
                var sleepSamples = RunQuickTickSleepTest(duration, iterations, config.ThreadPriority, config.WarmupIntervals);
                sleepMonitor.Stop();
                var sleepCpuUsage = sleepMonitor.GetAverageCpuUsage();
                sleepMonitor.Dispose();

                allResults.Add(new TimingTestResult($"QuickTick Sleep {duration}ms", sleepSamples));
                DrawHistogram(sleepSamples, Path.Combine(reportDir, $"histogram_QuickTickSleep_{duration}ms.png"), duration, sleepCpuUsage);
            }

            if (config.EnabledTests[TestType.Internal_QuickTickSleep])
            {
                Thread.Sleep(500);
                // Internal QuickTick Sleep
                Console.WriteLine($"Running Internal QuickTick Sleep test for {iterations} iterations with {duration}ms...");

                var internalSleepMonitor = new CPUMonitor();
                internalSleepMonitor.Start();
                var internalSleepSamples = RunInternalQuickTickSleepTest(duration, iterations, config.ThreadPriority, config.WarmupIntervals);
                internalSleepMonitor.Stop();
                var sleepCpuUsage = internalSleepMonitor.GetAverageCpuUsage();
                internalSleepMonitor.Dispose();

                allResults.Add(new TimingTestResult($"Internal_QuickTick Sleep {duration}ms", internalSleepSamples));
                DrawHistogram(internalSleepSamples, Path.Combine(reportDir, $"histogram_internal_QuickTickSleep_{duration}ms.png"), duration, sleepCpuUsage);
            }

            if (config.EnabledTests[TestType.QuickTickTimer])
            {
                Thread.Sleep(500);
                // QuickTick Timer
                Console.WriteLine($"Running QuickTick Timer test for {iterations} ticks with {duration}ms...");

                var timerMonitor = new CPUMonitor();
                timerMonitor.Start();
                var timerSamples = await RunQuickTickTimerTest(duration, iterations, false, config.WarmupIntervals, config.ThreadPriority);
                timerMonitor.Stop();
                var timerCpuUsage = timerMonitor.GetAverageCpuUsage();
                timerMonitor.Dispose();

                allResults.Add(new TimingTestResult($"QuickTick Timer {duration}ms", timerSamples));
                DrawHistogram(timerSamples, Path.Combine(reportDir, $"histogram_QuickTickTimer_{duration}ms.png"), duration, timerCpuUsage);
            }

            if (config.EnabledTests[TestType.HighResQuickTickTimer])
            {
                Thread.Sleep(500);
                // QuickTickHighResTimer test
                Console.WriteLine($"Running QuickTickHighResTimer test for {iterations} ticks with {duration}ms...");

                var quickTickHighResTimerMonitor = new CPUMonitor();
                quickTickHighResTimerMonitor.Start();
                var quickTickHighResSamples = await RunQuickTickTimerTest(duration, iterations, true, config.WarmupIntervals, config.ThreadPriority);
                quickTickHighResTimerMonitor.Stop();
                var quickTickHighResTimerCpuUsage = quickTickHighResTimerMonitor.GetAverageCpuUsage();
                quickTickHighResTimerMonitor.Dispose();

                allResults.Add(new TimingTestResult($"QuickTickHighResTimer {duration}ms", quickTickHighResSamples));
                DrawHistogram(quickTickHighResSamples, Path.Combine(reportDir, $"histogram_QuickTickHighResTimer_{duration}ms.png"), duration, quickTickHighResTimerCpuUsage);
            }

            if (config.EnabledTests[TestType.KGySoft_HiResTimer])
            {
                Thread.Sleep(500);
                // KGySoft.HiResTimer test
                Console.WriteLine($"Running KGySoft_HiResTimer test for {iterations} ticks with {duration}ms...");

                var hiResTimerMonitor = new CPUMonitor();
                hiResTimerMonitor.Start();
                var hiResSamples = await RunHiResTimerTest(duration, iterations);
                hiResTimerMonitor.Stop();
                var hiResTimerCpuUsage = hiResTimerMonitor.GetAverageCpuUsage();
                hiResTimerMonitor.Dispose();

                allResults.Add(new TimingTestResult($"KGySoft.HiResTimer {duration}ms", hiResSamples));
                DrawHistogram(hiResSamples, Path.Combine(reportDir, $"histogram_HiResTimer_{duration}ms.png"), duration, hiResTimerCpuUsage);
            }
        }

        var systemInfo = GetSystemInfo(config);
        File.WriteAllText(Path.Combine(reportDir, "system_info.txt"), systemInfo);

        var pdfPath = Path.Combine(reportDir, "QuickTick_Report.pdf");
        GeneratePdfReport(pdfPath, allResults, reportDir, systemInfo, config);

        Console.WriteLine($"Report generated at: {pdfPath}");
    }

    static Task<List<double>> RunHiResTimerTest(double intervalMs, int eventsToCapture, int warmUpIterations = 25)
    {
        var tcs = new TaskCompletionSource<List<double>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var samples = new List<double>();
        int counter = -warmUpIterations;
        var last = Stopwatch.GetTimestamp();
        var freq = Stopwatch.Frequency;
        int progressInterval = Math.Max(1, eventsToCapture / 10);

        var timer = new HiResTimer { Interval = (float)intervalMs, Enabled = false };

        timer.Elapsed += (_, __) =>
        {
            var now = Stopwatch.GetTimestamp();
            var delta = (now - last) * 1000.0 / freq;
            last = now;

            //Ignore Warmup phase; or first iteration if warmUp is zero
            if (counter <= 0)
            {
                counter++;
                return;
            }

            samples.Add(delta);

            if (counter == eventsToCapture)
            {
                timer.Stop();
                tcs.SetResult(samples);
            }

            if (counter % progressInterval == 0)
            {
                Console.WriteLine($"HiResTimer Progress: {counter * 100 / eventsToCapture}%");
            }

            counter++;
        };

        timer.Start();
        return tcs.Task;
    }

    static List<double> RunQuickTickSleepTest(double durationMs, int iterations, ThreadPriority priority, int warmUpIterations = 25)
    {
        var originalThreadPriority = Thread.CurrentThread.Priority;
        Thread.CurrentThread.Priority = priority;
        Thread.Sleep(100);

        int sleepMs = (int)Math.Round(durationMs);

        // If the double was not exactly representable
        if (Math.Abs(sleepMs - durationMs) > double.Epsilon)
        {
            Console.WriteLine($"Requested {durationMs:F3} ms, rounded to {sleepMs} ms; Sleep does not support sub millisecond timings");
        }

        var samples = new List<double>();
        int progressInterval = Math.Max(1, iterations / 10);

        for (int i = -warmUpIterations; i < iterations; i++)
        {
            var sw = Stopwatch.StartNew();
            QuickTickTiming.Sleep(sleepMs);
            sw.Stop();

            if (i < 0)
            {
                continue;
            }

            samples.Add(sw.Elapsed.TotalMilliseconds);

            if (i % progressInterval == 0)
            {
                Console.WriteLine($"Progress: {i * 100 / iterations}% ({i}/{iterations})");
            }
        }

        Console.WriteLine("Progress: 100% (done)");
        Thread.CurrentThread.Priority = originalThreadPriority;

        return samples;
    }

    static List<double> RunInternalQuickTickSleepTest(double durationMs, int iterations, ThreadPriority priority, int warmUpIterations = 25)
    {
        var originalThreadPriority = Thread.CurrentThread.Priority;
        Thread.CurrentThread.Priority = priority;
        Thread.Sleep(100);

        var sleepTicks = (int)(TimeSpan.TicksPerMillisecond * durationMs);

        var samples = new List<double>();
        int progressInterval = Math.Max(1, iterations / 10);

        for (int i = -warmUpIterations; i < iterations; i++)
        {
            var sw = Stopwatch.StartNew();
            QuickTickTiming.QuickTickSleep(sleepTicks);
            sw.Stop();

            if (i < 0)
            {
                continue;
            }

            samples.Add(sw.Elapsed.TotalMilliseconds);

            if (i % progressInterval == 0)
            {
                Console.WriteLine($"Progress: {i * 100 / iterations}% ({i}/{iterations})");
            }
        }

        Console.WriteLine("Progress: 100% (done)");
        Thread.CurrentThread.Priority = originalThreadPriority;

        return samples;
    }

    static Task<List<double>> RunQuickTickTimerTest(double intervalMs, int eventsToCapture, bool useHighRes, int warmUpIterations = 25, ThreadPriority timerPriority = ThreadPriority.Normal)
    {
        var tcs = new TaskCompletionSource<List<double>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var samples = new List<double>();
        var counter = -warmUpIterations;
        var last = Stopwatch.GetTimestamp();
        var stopwatchFreq = Stopwatch.Frequency;

        int progressInterval = Math.Max(1, eventsToCapture / 10);

        IQuickTickTimer timer;
        if (useHighRes)
        {
            timer = new HighResQuickTickTimer(intervalMs)
            {
                AutoReset = true,
                SkipMissedIntervals = false,
                Priority = timerPriority,
            };
        }
        else
        {
            timer = new QuickTickTimer(intervalMs)
            {
                AutoReset = true,
                SkipMissedIntervals = false,
                Priority = timerPriority,
            };
        }

        timer.Elapsed += (_, _) =>
        {
            var now = Stopwatch.GetTimestamp();
            var delta = (now - last) * 1000.0 / stopwatchFreq;
            last = now;

            //Ignore Warmup phase; or first iteration if warmUp is zero
            if (counter <= 0)
            {
                counter++;
                return;
            }

            samples.Add(delta);

            if (counter == eventsToCapture)
            {
                timer.Stop();
                timer.Dispose();
                tcs.SetResult(samples);
            }

            if (counter % progressInterval == 0)
            {
                Console.WriteLine($"Progress: {counter * 100 / eventsToCapture}% ({counter}/{eventsToCapture})");
            }

            counter++;         
        };

        timer.Start();
        return tcs.Task;
    }

    static void DrawHistogram(List<double> samples, string outputPath, double targetMs, double avgCpuUsage)
    {
        int width = 800, height = 500;
        int marginLeft = 60, marginBottom = 40, marginTop = 30;
        int plotWidth = width - marginLeft - 20;
        int plotHeight = height - marginTop - marginBottom;

        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);

        var textPaint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        var font = new SKFont(SKTypeface.Default, 14);

        var barPaint = new SKPaint { Color = SKColors.LightSkyBlue, Style = SKPaintStyle.Fill };
        var borderPaint = new SKPaint { Color = SKColors.Black, StrokeWidth = 1, Style = SKPaintStyle.Stroke };
        var targetLinePaint = new SKPaint { Color = SKColors.Green, StrokeWidth = 3, IsAntialias = true };
        var gaussianPaint = new SKPaint { Color = SKColors.DarkRed, StrokeWidth = 2, IsAntialias = true, Style = SKPaintStyle.Stroke };
        var avgLinePaint = new SKPaint { Color = SKColors.OrangeRed, StrokeWidth = 2, PathEffect = SKPathEffect.CreateDash(new float[] { 6, 4 }, 0) };

        // --- Fixed 0.1 ms bin width ---
        double binSize = 0.1;
        int binsEachSide = 25; // 25 left, 1 center, 25 right = 51 bins total
        int binCount = 2 * binsEachSide + 1;

        double min = targetMs - binSize * binsEachSide;
        double max = targetMs + binSize * binsEachSide;

        var bins = new int[binCount];

        foreach (var sample in samples)
        {
            if (sample < min || sample >= max) continue;
            int index = (int)((sample - min) / binSize);
            bins[index]++;
        }

        int maxBin = bins.Max();

        // --- Draw axes ---
        canvas.DrawLine(marginLeft, marginTop, marginLeft, height - marginBottom, borderPaint); // Y axis
        canvas.DrawLine(marginLeft, height - marginBottom, width - 20, height - marginBottom, borderPaint); // X axis

        int labelSkip = (int)Math.Ceiling((float)binCount / (plotWidth / 40));

        // --- Draw bars and labels ---
        for (int i = 0; i < binCount; i++)
        {
            float x = marginLeft + i * (plotWidth / (float)binCount);
            float binHeight = bins[i] / (float)maxBin * plotHeight;
            float y = height - marginBottom - binHeight;
            float barWidth = (plotWidth / (float)binCount) - 2;

            var rect = new SKRect(x, y, x + barWidth, height - marginBottom);
            canvas.DrawRect(rect, barPaint);
            canvas.DrawRect(rect, borderPaint);

            if (bins[i] > 0)
            {
                string countText = bins[i].ToString();
                canvas.DrawText(countText, x + barWidth / 2 - 10, y - 4, font, textPaint);
            }

            if (i % labelSkip == 0)
            {
                string label = $"{(min + i * binSize):0.00}";
                canvas.DrawText(label, x, height - marginBottom + 18, font, textPaint);
            }
        }

        // Bin width label
        canvas.DrawText($"Bin width: {binSize:0.000} ms", marginLeft, marginTop - 5, font, textPaint);
        canvas.DrawText("Count", 10, marginTop + 10, font, textPaint);
        canvas.DrawText("Time (ms)", width / 2 - 40, height - 5, font, textPaint);

        // --- Draw target line ---
        if (targetMs >= min && targetMs <= max)
        {
            float x = marginLeft + (float)((targetMs - min) / (max - min) * plotWidth);
            canvas.DrawLine(x, marginTop, x, height - marginBottom, targetLinePaint);
        }

        // --- Draw Gaussian curve ---
        double mean = samples.Average();
        double stdDev = Math.Sqrt(samples.Sum(x => Math.Pow(x - mean, 2)) / samples.Count);
        int gaussSteps = 1000;

        // Compute max Gaussian value within range
        double gaussMax = 0;
        for (int i = 0; i <= gaussSteps; i++)
        {
            double t = min + i * (max - min) / gaussSteps;
            double val = Gaussian(t, mean, stdDev);
            if (val > gaussMax)
            {
                gaussMax = val;
            }
        }

        SKPath path = new SKPath();
        bool first = true;

        for (int i = 0; i <= gaussSteps; i++)
        {
            double t = min + i * (max - min) / gaussSteps;
            double gNorm = Gaussian(t, mean, stdDev) / gaussMax; // normalize
            float x = marginLeft + (float)((t - min) / (max - min) * plotWidth);
            float y = height - marginBottom - (float)(gNorm * plotHeight);

            if (first) { path.MoveTo(x, y); first = false; }
            else path.LineTo(x, y);
        }

        canvas.DrawPath(path, gaussianPaint);

        // --- Draw average CPU usage legend ---
        string legend = $"Avg CPU: {avgCpuUsage:F2}%";
        canvas.DrawText(legend, width - 200, marginTop + 10, font, textPaint);

        // Save image
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.OpenWrite(outputPath);
        data.SaveTo(stream);
    }

    static double Gaussian(double x, double mean, double stdDev)
    {
        double a = 1.0 / (stdDev * Math.Sqrt(2 * Math.PI));
        double b = Math.Exp(-Math.Pow(x - mean, 2) / (2 * stdDev * stdDev));
        return a * b;
    }

    static string GetSystemInfo(TestConfig cfg)
    {
        var info =  $"OS: {RuntimeInformation.OSDescription}\n" +
                    $"Architecture: {RuntimeInformation.OSArchitecture}\n" +
                    $"Framework: {RuntimeInformation.FrameworkDescription}\n" +
                    $"Processor Count: {Environment.ProcessorCount}\n" +
                    $"64-bit OS: {Environment.Is64BitOperatingSystem}\n" +
                    $"64-bit Process: {Environment.Is64BitProcess}\n" +
                    $"Thread Priority: {cfg.ThreadPriority}\n";

        var scheme = GetWindowsPowerScheme();
        if (scheme != null)
        {
            info += $"Power Scheme: {scheme}\n";
        }

        return info;
    }

    static string? GetWindowsPowerScheme()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return null;
        }

        try
        {
            var psi = new ProcessStartInfo("powercfg", "/GETACTIVESCHEME")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            var proc = Process.Start(psi);
            proc.WaitForExit(2000);
            var output = proc.StandardOutput.ReadToEnd();
            return output.Trim();
        }
        catch 
        { 
            return "Unknown"; 
        }
    }

    static void GeneratePdfReport(string path, List<TimingTestResult> results, string reportDir, string systemInfo, TestConfig config)
    {
        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Margin(20);
                page.Header().Text("QuickTick Timing Report").FontSize(20).Bold();

                page.Content().Column(col =>
                {
                    col.Item().Text("System Information:").Bold();
                    col.Item().Text(systemInfo);
                    col.Item().Text(string.Empty);
                    col.Item().Text("TestConfig:").Bold();
                    col.Item().Text($"Time for each Test: {config.TimeInSecondsPerTest} s");
                    col.Item().Text($"Priority (used QuickTick Operations): {config.ThreadPriority}");
                    col.Item().Text($"Warmup phase: {config.WarmupIntervals} intervals");

                    var grouped = results.GroupBy(r => r.Label.Split(' ')[^1].Replace("ms", ""));

                    foreach (var group in grouped)
                    {
                        var sleep = group.FirstOrDefault(r => r.Label.StartsWith("QuickTick Sleep"));
                        var internalSleep = group.FirstOrDefault(r => r.Label.StartsWith("Internal_QuickTick Sleep"));
                        var timer = group.FirstOrDefault(r => r.Label.StartsWith("QuickTick Timer"));
                        var quickTickHighRes = group.FirstOrDefault(r => r.Label.StartsWith("QuickTickHighResTimer"));
                        var hirestimer = group.FirstOrDefault(r => r.Label.StartsWith("KGySoft.HiResTimer"));

                        col.Item().PageBreak();

                        if (sleep != null)
                        {
                            var (min, max, mean, stddev) = GetStats(sleep.Samples);
                            col.Item().Text(sleep.Label).FontSize(16).Bold();
                            col.Item().Text($"Samples: {sleep.Samples.Count}, Min: {min:F3} ms, Max: {max:F3} ms, Mean: {mean:F3} ms, StdDev: {stddev:F3} ms");

                            var imgPath = Path.Combine(reportDir, $"histogram_QuickTickSleep_{group.Key}ms.png");
                            if (File.Exists(imgPath))
                                col.Item().Image(Image.FromFile(imgPath)).FitWidth();
                        }

                        if (internalSleep != null)
                        {
                            var (min, max, mean, stddev) = GetStats(internalSleep.Samples);
                            col.Item().Text(internalSleep.Label).FontSize(16).Bold();
                            col.Item().Text($"Samples: {internalSleep.Samples.Count}, Min: {min:F3} ms, Max: {max:F3} ms, Mean: {mean:F3} ms, StdDev: {stddev:F3} ms");

                            var imgPath = Path.Combine(reportDir, $"histogram_internal_QuickTickSleep_{group.Key}ms.png");
                            if (File.Exists(imgPath))
                                col.Item().Image(Image.FromFile(imgPath)).FitWidth();
                        }

                        if (timer != null)
                        {
                            var (min, max, mean, stddev) = GetStats(timer.Samples);
                            col.Item().Text(timer.Label).FontSize(16).Bold();
                            col.Item().Text($"Samples: {timer.Samples.Count}, Min: {min:F3} ms, Max: {max:F3} ms, Mean: {mean:F3} ms, StdDev: {stddev:F3} ms");

                            var imgPath = Path.Combine(reportDir, $"histogram_QuickTickTimer_{group.Key}ms.png");
                            if (File.Exists(imgPath))
                                col.Item().Image(Image.FromFile(imgPath)).FitWidth();
                        }

                        if (quickTickHighRes != null)
                        {
                            var (min, max, mean, stddev) = GetStats(quickTickHighRes.Samples);
                            col.Item().Text(quickTickHighRes.Label).FontSize(16).Bold();
                            col.Item().Text($"Samples: {quickTickHighRes.Samples.Count}, Min: {min:F3} ms, Max: {max:F3} ms, Mean: {mean:F3} ms, StdDev: {stddev:F3} ms");

                            var imgPath = Path.Combine(reportDir, $"histogram_QuickTickHighResTimer_{group.Key}ms.png");
                            if (File.Exists(imgPath))
                                col.Item().Image(Image.FromFile(imgPath)).FitWidth();
                        }

                        if (hirestimer != null)
                        {
                            var (min, max, mean, stddev) = GetStats(hirestimer.Samples);
                            col.Item().Text(hirestimer.Label).FontSize(16).Bold();
                            col.Item().Text($"Samples: {hirestimer.Samples.Count}, Min: {min:F3} ms, Max: {max:F3} ms, Mean: {mean:F3} ms, StdDev: {stddev:F3} ms");

                            var imgPath = Path.Combine(reportDir, $"histogram_HiResTimer_{group.Key}ms.png");
                            if (File.Exists(imgPath))
                                col.Item().Image(Image.FromFile(imgPath)).FitWidth();
                        }
                    }
                });

                page.Footer().AlignCenter().Text(text =>
                {
                    text.Span("Generated on ");
                    text.Span(DateTime.Now.ToString());
                });
            });
        }).GeneratePdf(path);
    }

    static (double Min, double Max, double Mean, double StdDev) GetStats(List<double> samples)
    {
        var mean = samples.Average();
        var variance = samples.Sum(s => Math.Pow(s - mean, 2)) / samples.Count;
        return (samples.Min(), samples.Max(), mean, Math.Sqrt(variance));
    }

    record TimingTestResult(string Label, List<double> Samples);
}