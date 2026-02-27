using System.Text.Json.Serialization;

namespace ClaudeTracker.Models;

/// <summary>Which usage metric to display in the tray icon.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MenuBarMetricType
{
    Session,
    Week,
    Api
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MenuBarIconStyle
{
    Battery,
    ProgressBar,
    Percentage,
    Ring,
    Compact
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum APIDisplayMode
{
    Remaining,
    Used,
    Both
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WeekDisplayMode
{
    Percentage,
    Tokens
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MultiProfileIconStyle
{
    Concentric,
    ProgressBar,
    Compact
}

public class MetricIconConfig
{
    [JsonPropertyName("metricType")]
    public MenuBarMetricType MetricType { get; set; }

    [JsonPropertyName("isEnabled")]
    public bool IsEnabled { get; set; }

    [JsonPropertyName("iconStyle")]
    public MenuBarIconStyle IconStyle { get; set; } = MenuBarIconStyle.Battery;

    [JsonPropertyName("order")]
    public int Order { get; set; }

    [JsonPropertyName("weekDisplayMode")]
    public WeekDisplayMode WeekDisplayMode { get; set; } = WeekDisplayMode.Percentage;

    [JsonPropertyName("apiDisplayMode")]
    public APIDisplayMode ApiDisplayMode { get; set; } = APIDisplayMode.Remaining;

    [JsonPropertyName("showNextSessionTime")]
    public bool ShowNextSessionTime { get; set; }

    public static MetricIconConfig SessionDefault => new()
    {
        MetricType = MenuBarMetricType.Session,
        IsEnabled = true,
        IconStyle = MenuBarIconStyle.Battery,
        Order = 0,
        ShowNextSessionTime = false
    };

    public static MetricIconConfig WeekDefault => new()
    {
        MetricType = MenuBarMetricType.Week,
        IsEnabled = false,
        IconStyle = MenuBarIconStyle.Battery,
        Order = 1,
        WeekDisplayMode = WeekDisplayMode.Percentage
    };

    public static MetricIconConfig ApiDefault => new()
    {
        MetricType = MenuBarMetricType.Api,
        IsEnabled = false,
        IconStyle = MenuBarIconStyle.Battery,
        Order = 2,
        ApiDisplayMode = APIDisplayMode.Remaining
    };
}

public class MultiProfileDisplayConfig
{
    [JsonPropertyName("iconStyle")]
    public MultiProfileIconStyle IconStyle { get; set; } = MultiProfileIconStyle.Concentric;

    [JsonPropertyName("showWeek")]
    public bool ShowWeek { get; set; } = true;

    [JsonPropertyName("showProfileLabel")]
    public bool ShowProfileLabel { get; set; } = true;

    [JsonPropertyName("useSystemColor")]
    public bool UseSystemColor { get; set; }
}

public class MenuBarIconConfiguration
{
    [JsonPropertyName("monochromeMode")]
    public bool MonochromeMode { get; set; }

    [JsonPropertyName("showIconNames")]
    public bool ShowIconNames { get; set; } = true;

    [JsonPropertyName("showRemainingPercentage")]
    public bool ShowRemainingPercentage { get; set; }

    [JsonPropertyName("useCustomColor")]
    public bool UseCustomColor { get; set; }

    [JsonPropertyName("customColorHex")]
    public string? CustomColorHex { get; set; }

    [JsonPropertyName("metrics")]
    public List<MetricIconConfig> Metrics { get; set; } = new()
    {
        MetricIconConfig.SessionDefault,
        MetricIconConfig.WeekDefault,
        MetricIconConfig.ApiDefault
    };

    public IEnumerable<MetricIconConfig> EnabledMetrics =>
        Metrics.Where(m => m.IsEnabled).OrderBy(m => m.Order);

    public MetricIconConfig? GetConfig(MenuBarMetricType metricType) =>
        Metrics.FirstOrDefault(m => m.MetricType == metricType);

    public void UpdateConfig(MetricIconConfig config)
    {
        var index = Metrics.FindIndex(m => m.MetricType == config.MetricType);
        if (index >= 0) Metrics[index] = config;
    }

    public static MenuBarIconConfiguration Default => new();
}
