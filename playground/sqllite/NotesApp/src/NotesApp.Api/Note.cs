namespace NotesApp.Api;

public class Note
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Color { get; set; } = "default";
    public DateTime CreatedAt { get; set; }
    public DateTime ModifiedAt { get; set; }
}
