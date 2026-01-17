namespace TelemetryDashboard.Api;

public class DashboardPanel
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string TelemetryPointId { get; set; } = string.Empty;
    public string DisplayType { get; set; } = "gauge";
    public int Position { get; set; }
    public DateTime CreatedAt { get; set; }
}
