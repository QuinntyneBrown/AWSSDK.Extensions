namespace PlanningPokerApp.Api;

public class PokerSession
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string CurrentStory { get; set; } = string.Empty;
    public bool IsRevealed { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<Vote> Votes { get; set; } = new();
}
