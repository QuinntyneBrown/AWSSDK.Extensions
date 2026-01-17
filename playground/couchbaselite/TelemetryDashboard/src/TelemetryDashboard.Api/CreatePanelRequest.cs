namespace TelemetryDashboard.Api;

public class CreatePanelRequest
{
    public string Title { get; set; } = string.Empty;
    public string TelemetryPointId { get; set; } = string.Empty;
    public string DisplayType { get; set; } = "gauge";
}
