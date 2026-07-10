using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace QuickTickTimingReportGenerator;

public class TestConfig
{
    [JsonConverter(typeof(StringEnumConverter))]
    public ThreadPriority ThreadPriority { get; } = ThreadPriority.Normal;
    public List<double> IntervalsMs { get; private init; } = [];
    public int TimeInSecondsPerTest { get; } = 10;
    public int WarmupIntervals { get; } = 25;
    public Dictionary<TestType, bool> EnabledTests { get; private init; } = new();

    public static TestConfig LoadConfig(string path)
    {
        if (!File.Exists(path))
        {
            Console.WriteLine("Config not found, using defaults.");
            var testConfig = new TestConfig
            {
                IntervalsMs = [1, 5, 50],
                EnabledTests = new Dictionary<TestType, bool>() 
                {
                    { TestType.QuickTickSleep, true },
                    { TestType.InternalQuickTickSleep, false },
                    { TestType.QuickTickTimer, true },
                    { TestType.HighResQuickTickTimer, true },
                    { TestType.KGySoftHiResTimer, true }
                }
            };
            return testConfig;
        }
        var json = File.ReadAllText(path);
        return JsonConvert.DeserializeObject<TestConfig>(json) ?? new TestConfig();
    }
}
