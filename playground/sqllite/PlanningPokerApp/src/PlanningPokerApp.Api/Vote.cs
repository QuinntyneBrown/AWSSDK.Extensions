namespace PlanningPokerApp.Api;

public class Vote
{
    public string ParticipantName { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public DateTime VotedAt { get; set; }
}
