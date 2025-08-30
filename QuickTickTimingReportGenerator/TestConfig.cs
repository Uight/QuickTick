using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace QuickTickTimingReportGenerator;

public class TestConfig
{
    [JsonConverter(typeof(StringEnumConverter))]
    public ThreadPriority ThreadPriority { get; set; } = ThreadPriority.Normal;
    public List<double> IntervalsMs { get; set; } = [];
    public int TimeInSecondsPerTest { get; set; } = 10;
    public bool IncludeCompareToHiResTimer { get; set; } = true;
    public int WarmupIntervals { get; set; } = 25;

    public static TestConfig LoadConfig(string path)
    {
        if (!File.Exists(path))
        {
            Console.WriteLine("Config not found, using defaults.");
            var testConfig = new TestConfig
            {
                IntervalsMs = [1, 5, 50]
            };
            return testConfig;
        }
        var json = File.ReadAllText(path);
        return JsonConvert.DeserializeObject<TestConfig>(json) ?? new TestConfig();
    }
}
