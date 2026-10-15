namespace AgentToAgent.Chatbot.Models;

// Mirrors delivery-copilot's ExternalAgentAskResponse / UseCaseAssistantMatch JSON shape
// returned by POST {Atlas:BaseUrl}/ask.
public class AtlasAskResponse
{
    public string Answer { get; set; } = string.Empty;
    public List<AtlasMatchDto> Matches { get; set; } = new();
}

public class AtlasMatchDto
{
    public string? Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? WorkflowId { get; set; }
    public string? Link { get; set; }
    public double Score { get; set; }
}

public class AtlasAskResult
{
    public bool Succeeded { get; private init; }
    public string Answer { get; private init; } = string.Empty;
    public List<AtlasMatchDto> Matches { get; private init; } = new();
    public string? FailureReason { get; private init; }

    public static AtlasAskResult Ok(string answer, List<AtlasMatchDto> matches) =>
        new() { Succeeded = true, Answer = answer, Matches = matches };

    public static AtlasAskResult Failed(string reason) =>
        new() { Succeeded = false, FailureReason = reason };
}
