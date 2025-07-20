using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using QuickTickLib;
using SkiaSharp;
using System.Diagnostics;
using System.Runtime.InteropServices;

class Program
{
    static async Task Main(string[] args)
    {
        var reportDir = Path.Combine(Directory.GetCurrentDirectory(), $"QuickTickReport_{DateTime.Now:yyyyMMdd_HHmmss}");
        Directory.CreateDirectory(reportDir);
        QuestPDF.Settings.License = LicenseType.Community;

        var allResults = new List<TimingTestResult>();
        var durations = new[] { 1, 5, 50 };

        foreach (var duration in durations)
        {
            const int iterations = 500;
            Console.WriteLine($"Running QuickTick Delay test for {iterations} iterations with timing {duration}");
            var samples = await RunQuickTickDelayTest(duration, iterations);
            allResults.Add(new TimingTestResult($"Delay {duration}ms", samples));

            var imgPath = Path.Combine(reportDir, $"histogram_{duration}ms.png");
            DrawHistogram(samples, imgPath, duration);
        }

        var systemInfo = GetSystemInfo();
        File.WriteAllText(Path.Combine(reportDir, "system_info.txt"), systemInfo);

        var pdfPath = Path.Combine(reportDir, "QuickTick_Report.pdf");
        GeneratePdfReport(pdfPath, allResults, reportDir, systemInfo);

        Console.WriteLine($"Report generated at: {pdfPath}");
    }

    static async Task<List<double>> RunQuickTickDelayTest(int durationMs, int iterations)
    {
        var samples = new List<double>();
        int progressInterval = Math.Max(1, iterations / 10);

        for (int i = 0; i < iterations; i++)
        {
            var sw = Stopwatch.StartNew();
            await QuickTickTiming.Delay(durationMs);
            sw.Stop();
            samples.Add(sw.Elapsed.TotalMilliseconds);

            if (i % progressInterval == 0)
            {
                Console.WriteLine($"Progress: {i * 100 / iterations}% ({i}/{iterations})");
            }
        }

        Console.WriteLine("Progress: 100% (done)");
        return samples;
    }

    static void DrawHistogram(List<double> samples, string outputPath, double targetMs)
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

    static string GetSystemInfo()
    {
        return $"OS: {RuntimeInformation.OSDescription}\n" +
               $"Architecture: {RuntimeInformation.OSArchitecture}\n" +
               $"Framework: {RuntimeInformation.FrameworkDescription}\n" +
               $"Processor Count: {Environment.ProcessorCount}\n" +
               $"64-bit OS: {Environment.Is64BitOperatingSystem}\n" +
               $"64-bit Process: {Environment.Is64BitProcess}\n";
    }

    static void GeneratePdfReport(string path, List<TimingTestResult> results, string reportDir, string systemInfo)
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

                    foreach (var result in results)
                    {
                        var (min, max, mean, stddev) = GetStats(result.Samples);

                        col.Item().PageBreak();
                        col.Item().Text(result.Label).FontSize(16).Bold();
                        col.Item().Text($"Samples: {result.Samples.Count}, Min: {min:F3} ms, Max: {max:F3} ms, Mean: {mean:F3} ms, StdDev: {stddev:F3} ms");

                        var imgPath = Path.Combine(reportDir, $"histogram_{result.Label.Replace("Delay ", string.Empty).Replace("ms", string.Empty)}ms.png");
                        if (File.Exists(imgPath))
                        {
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