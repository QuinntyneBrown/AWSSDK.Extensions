namespace EventLogger.Api;

public class LogEvent
{
    public string Id { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Level { get; set; } = "info";
    public string Source { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new();
}
