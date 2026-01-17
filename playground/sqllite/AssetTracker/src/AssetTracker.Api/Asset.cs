namespace AssetTracker.Api;

public class Asset
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Status { get; set; } = "available";
    public string Location { get; set; } = string.Empty;
    public string AssignedTo { get; set; } = string.Empty;
    public DateTime AcquiredAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
