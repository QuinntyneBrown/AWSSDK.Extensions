namespace DocumentVault.Api;

public class Document
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long Size { get; set; }
    public List<string> Tags { get; set; } = new();
    public DateTime UploadedAt { get; set; }
}
