namespace TodoApp.Api;

public class CreateTodoItemRequest
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}
