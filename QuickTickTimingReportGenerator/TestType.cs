using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace QuickTickTimingReportGenerator;

[JsonConverter(typeof(StringEnumConverter))]
public enum TestType
{
    QuickTickSleep,
    QuickTickTimer,
    HighResQuickTickTimer,
    KGySoft_HiResTimer
}
