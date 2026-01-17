namespace AssetTracker.Api;

public class CreateAssetRequest
{
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public DateTime AcquiredAt { get; set; }
}
