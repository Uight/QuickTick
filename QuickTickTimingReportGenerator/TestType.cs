using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace QuickTickTimingReportGenerator;

[JsonConverter(typeof(StringEnumConverter))]
public enum TestType
{
    QuickTickSleep,
    Internal_QuickTickSleep,
    QuickTickTimer,
    HighResQuickTickTimer,
    KGySoft_HiResTimer
}
