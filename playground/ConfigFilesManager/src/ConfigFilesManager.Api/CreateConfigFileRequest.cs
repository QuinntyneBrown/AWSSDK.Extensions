namespace ConfigFilesManager.Api;

public class CreateConfigFileRequest
{
    public string Name { get; set; } = string.Empty;
    public string FileType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}
