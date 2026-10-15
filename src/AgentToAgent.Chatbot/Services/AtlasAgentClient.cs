using System.Net.Http.Json;
using AgentToAgent.Chatbot.Models;

namespace AgentToAgent.Chatbot.Services;

public interface IAtlasAgentClient
{
    Task<AtlasAskResult> AskAsync(string question, CancellationToken ct);
}

/// <summary>
/// Calls the Atlas external-agent REST surface (POST {baseUrl}/ask) using the API key
/// issued by an Atlas admin — the same surface documented for any third-party agent.
/// </summary>
public class AtlasAgentClient : IAtlasAgentClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AtlasAgentClient> _logger;

    public AtlasAgentClient(HttpClient httpClient, ILogger<AtlasAgentClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<AtlasAskResult> AskAsync(string question, CancellationToken ct)
    {
        if (_httpClient.BaseAddress is null)
        {
            return AtlasAskResult.Failed("Atlas:BaseUrl is not configured.");
        }

        try
        {
            var response = await _httpClient.PostAsJsonAsync("ask", new { question }, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Atlas external-agent call failed with {Status}", response.StatusCode);
                return AtlasAskResult.Failed($"Atlas returned HTTP {(int)response.StatusCode}.");
            }

            var payload = await response.Content.ReadFromJsonAsync<AtlasAskResponse>(cancellationToken: ct);
            if (payload is null)
            {
                return AtlasAskResult.Failed("Atlas returned an empty response.");
            }

            return AtlasAskResult.Ok(payload.Answer, payload.Matches);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Atlas external-agent call threw an exception.");
            return AtlasAskResult.Failed("Could not reach Atlas right now.");
        }
    }
}
