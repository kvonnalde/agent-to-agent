using AgentToAgent.Chatbot.Models;
using AgentToAgent.Chatbot.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpClient<IAtlasAgentClient, AtlasAgentClient>((sp, client) =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var baseUrl = config["Atlas:BaseUrl"];
    if (!string.IsNullOrWhiteSpace(baseUrl))
    {
        client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
    }

    var apiKey = config["Atlas:ApiKey"];
    if (!string.IsNullOrWhiteSpace(apiKey))
    {
        client.DefaultRequestHeaders.Add("X-Atlas-Api-Key", apiKey);
    }
});

builder.Services.AddSingleton<IChatAgentService, ChatAgentService>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapPost("/api/chat", async (ChatRequest request, IChatAgentService agent, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.Message))
    {
        return Results.BadRequest(new { error = "message is required." });
    }

    var result = await agent.AskAsync(request, ct);
    return Results.Ok(result);
});

app.MapFallbackToFile("index.html");

app.Run();
