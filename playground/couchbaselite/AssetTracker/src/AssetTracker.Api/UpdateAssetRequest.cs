namespace AssetTracker.Api;

public class UpdateAssetRequest
{
    public string Status { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string AssignedTo { get; set; } = string.Empty;
}
