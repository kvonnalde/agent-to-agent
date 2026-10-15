namespace AgentToAgent.Chatbot.Models;

public class ChatTurn
{
    public string Role { get; set; } = "user"; // "user" | "assistant"
    public string Content { get; set; } = string.Empty;
}

public class ChatRequest
{
    public string Message { get; set; } = string.Empty;
    public List<ChatTurn>? History { get; set; }
}

public class ChatResponse
{
    public string Reply { get; set; } = string.Empty;
    public bool UsedAtlas { get; set; }
    public List<AtlasMatchDto> Matches { get; set; } = new();
}
