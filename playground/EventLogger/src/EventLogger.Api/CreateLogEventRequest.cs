namespace EventLogger.Api;

public class CreateLogEventRequest
{
    public string Message { get; set; } = string.Empty;
    public string Level { get; set; } = "info";
    public string Source { get; set; } = string.Empty;
    public Dictionary<string, string> Metadata { get; set; } = new();
}
