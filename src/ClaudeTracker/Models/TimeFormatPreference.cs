using System.Text.Json.Serialization;

namespace ClaudeTracker.Models;

[JsonConverter(typeof(JsonStringEnumConverter<TimeFormatPreference>))]
public enum TimeFormatPreference
{
    System,
    TwelveHour,
    TwentyFourHour
}

[JsonConverter(typeof(JsonStringEnumConverter<PopoverTimeDisplay>))]
public enum PopoverTimeDisplay
{
    RemainingTime,
    ResetTime,
    Both
}
