namespace TelemetryDashboard.Api;

public class CreateTelemetryPointRequest
{
    public string Name { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public double MinValue { get; set; }
    public double MaxValue { get; set; }
}
