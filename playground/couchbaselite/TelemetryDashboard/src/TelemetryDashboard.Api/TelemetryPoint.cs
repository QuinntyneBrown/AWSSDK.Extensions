namespace TelemetryDashboard.Api;

public class TelemetryPoint
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public double Value { get; set; }
    public double MinValue { get; set; }
    public double MaxValue { get; set; }
    public DateTime Timestamp { get; set; }
}
