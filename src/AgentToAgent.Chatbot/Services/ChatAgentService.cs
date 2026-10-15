using System.ClientModel;
using System.Text.Json;
using AgentToAgent.Chatbot.Models;
using Azure.AI.OpenAI;
using OpenAI.Chat;

namespace AgentToAgent.Chatbot.Services;

public interface IChatAgentService
{
    Task<ChatResponse> AskAsync(ChatRequest request, CancellationToken ct);
}

/// <summary>
/// A "usual agent": general-purpose chatbot that calls the ask_atlas_assistant tool
/// (backed by Atlas's external-agent API) whenever the model decides a question is
/// about Atlas / the AI Hub platform, otherwise answers directly from its own knowledge.
/// </summary>
public class ChatAgentService : IChatAgentService
{
    private const string SystemPrompt =
        "You are a helpful general-purpose assistant. Answer everyday questions directly " +
        "from your own knowledge. When the user asks about Atlas, the AI Hub, the Delivery " +
        "Copilot use-case portfolio, or anything specific to that platform, call the " +
        "ask_atlas_assistant tool instead of guessing, and base your answer on its result. " +
        "Keep answers concise and friendly.";

    private static readonly ChatTool AskAtlasTool = ChatTool.CreateFunctionTool(
        functionName: "ask_atlas_assistant",
        functionDescription: "Asks the Atlas Assistant about the AI Hub / Delivery Copilot use-case portfolio, platform configuration, or delivery guidance.",
        functionParameters: BinaryData.FromString("""
        {
          "type": "object",
          "properties": {
            "question": {
              "type": "string",
              "description": "The user's question, rephrased as a standalone question for Atlas."
            }
          },
          "required": ["question"]
        }
        """));

    private readonly ChatClient? _chatClient;
    private readonly IAtlasAgentClient _atlasClient;
    private readonly ILogger<ChatAgentService> _logger;

    public ChatAgentService(IConfiguration configuration, IAtlasAgentClient atlasClient, ILogger<ChatAgentService> logger)
    {
        _atlasClient = atlasClient;
        _logger = logger;

        var endpoint = configuration["AzureOpenAI:Endpoint"];
        var apiKey = configuration["AzureOpenAI:ApiKey"];
        var deployment = configuration["AzureOpenAI:ChatDeployment"];

        if (!string.IsNullOrWhiteSpace(endpoint) && !string.IsNullOrWhiteSpace(apiKey) && !string.IsNullOrWhiteSpace(deployment))
        {
            var azureClient = new AzureOpenAIClient(new Uri(endpoint), new ApiKeyCredential(apiKey));
            _chatClient = azureClient.GetChatClient(deployment);
        }
    }

    public async Task<ChatResponse> AskAsync(ChatRequest request, CancellationToken ct)
    {
        if (_chatClient is null)
        {
            return new ChatResponse
            {
                Reply = "Azure OpenAI isn't configured yet. Set AzureOpenAI:Endpoint, AzureOpenAI:ApiKey " +
                         "and AzureOpenAI:ChatDeployment in appsettings.Development.json, then restart.",
            };
        }

        var messages = new List<ChatMessage> { new SystemChatMessage(SystemPrompt) };
        foreach (var turn in request.History ?? new List<ChatTurn>())
        {
            messages.Add(turn.Role == "assistant"
                ? new AssistantChatMessage(turn.Content)
                : new UserChatMessage(turn.Content));
        }
        messages.Add(new UserChatMessage(request.Message));

        var options = new ChatCompletionOptions();
        options.Tools.Add(AskAtlasTool);

        ChatCompletion completion;
        try
        {
            completion = await _chatClient.CompleteChatAsync(messages, options, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Azure OpenAI chat completion failed.");
            return new ChatResponse { Reply = "Sorry, I couldn't reach the language model. Check the Azure OpenAI configuration." };
        }

        var usedAtlas = false;
        var matches = new List<AtlasMatchDto>();

        if (completion.FinishReason == ChatFinishReason.ToolCalls)
        {
            messages.Add(new AssistantChatMessage(completion));

            foreach (var toolCall in completion.ToolCalls)
            {
                if (toolCall.FunctionName != "ask_atlas_assistant")
                {
                    messages.Add(new ToolChatMessage(toolCall.Id, "Unknown tool."));
                    continue;
                }

                var question = ExtractQuestionArgument(toolCall.FunctionArguments) ?? request.Message;
                var result = await _atlasClient.AskAsync(question, ct);
                usedAtlas = true;

                if (result.Succeeded)
                {
                    matches = result.Matches;
                    messages.Add(new ToolChatMessage(toolCall.Id,
                        JsonSerializer.Serialize(new { answer = result.Answer, matches = result.Matches })));
                }
                else
                {
                    messages.Add(new ToolChatMessage(toolCall.Id, JsonSerializer.Serialize(new { error = result.FailureReason })));
                }
            }

            try
            {
                completion = await _chatClient.CompleteChatAsync(messages, options, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Azure OpenAI follow-up chat completion failed.");
                return new ChatResponse { Reply = "Atlas answered, but I couldn't finish summarising the response.", UsedAtlas = usedAtlas, Matches = matches };
            }
        }

        var replyText = string.Concat(completion.Content.Select(part => part.Text));

        return new ChatResponse
        {
            Reply = string.IsNullOrWhiteSpace(replyText) ? "(no response)" : replyText,
            UsedAtlas = usedAtlas,
            Matches = matches,
        };
    }

    private static string? ExtractQuestionArgument(BinaryData functionArguments)
    {
        try
        {
            using var args = JsonDocument.Parse(functionArguments.ToString());
            return args.RootElement.TryGetProperty("question", out var q) && q.ValueKind == JsonValueKind.String
                ? q.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
