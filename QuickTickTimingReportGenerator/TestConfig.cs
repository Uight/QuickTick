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

    public static TestConfig LoadConfig(string path)
    {
        if (!File.Exists(path))
        {
            Console.WriteLine("Config not found, using defaults.");
            return new TestConfig();
        }
        var json = File.ReadAllText(path);
        return JsonConvert.DeserializeObject<TestConfig>(json) ?? new TestConfig();
    }
}
