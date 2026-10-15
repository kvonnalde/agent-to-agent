# Agent-to-Agent (A2A) Setup Guide

How to stand up a new **external agent** that consumes the Atlas Assistant via the
Delivery Copilot (DCP) API-key surface. `AgentToAgent.Chatbot` (in `src/`) is the
reference implementation of everything described below — copy/adapt it for each
new agent.

## 1. Concept

```
┌───────────────────────┐        X-Atlas-Api-Key        ┌───────────────────────────┐
│  External Agent        │ ─────────────────────────────▶ │  Delivery Copilot (DCP)    │
│  (this project, or any  │   POST /api/external/atlas/ask │  ExternalAgentController   │
│  other chatbot/agent)   │ ◀───────────────────────────── │  → Atlas Assistant (RAG)   │
└───────────────────────┘        answer + matches         └───────────────────────────┘
```

- DCP exposes Atlas to the outside world over two equivalent surfaces, both guarded by
  the same API key:
  - REST: `POST /api/external/atlas/ask`
  - MCP: `/mcp/atlas`
- An "external agent" is any standalone application (this chatbot, a Copilot Studio
  agent, a LangChain/Semantic Kernel agent, etc.) that holds one of these keys and
  calls in when it needs Atlas's knowledge. It does **not** need access to the DCP
  codebase, database, or network — only the key and the base URL.
- A good external agent doesn't blindly forward every message to Atlas — it decides
  *when* a question is Atlas-specific vs. general. `AgentToAgent.Chatbot` does this
  with real LLM tool/function calling (the model chooses to call the `ask_atlas_assistant`
  tool); that pattern is reusable for any agent framework that supports tool calling.

## 2. Prerequisite: issue an API key in DCP

1. Run DCP locally (or use an already-deployed environment) and sign in as an admin.
2. Go to **AI Hub → Admin → Agents tab → External Agent Access** section
   (`ExternalAgentsSection.tsx`).
3. Click **Create** and give the agent a name (e.g. `"Agent-to-Agent Chatbot"`).
4. Copy the returned API key immediately — it is shown **once** and only its hash is
   stored server-side.
5. Alternatively, for pure local dev, set `AiHub:ExternalAgent:DevSeedKey` in DCP's
   `appsettings.Development.json` to any string (e.g. `local-test-key`) and use that
   instead of creating a key via the UI. Only honored when `ASPNETCORE_ENVIRONMENT=Development`.
6. You can also manage keys via HTTP directly — see `src/Avanade.Dcp.Web/aihub.http`,
   section **"External agent access (admin-only key management)"**
   (`GET/POST/PUT/DELETE {{baseUrl}}/ai/external-agents`).

Keep the key out of source control. Each agent should get its own key so it can be
revoked/rotated independently and so DCP's usage analytics can attribute calls per agent.

## 3. Scaffold a new agent project

```powershell
dotnet new web -n <AgentName> -o src/<AgentName> --framework net8.0
cd src/<AgentName>
dotnet add package Azure.AI.OpenAI
```

Use the ASP.NET Core **Empty** template (minimal API, no MVC/Razor) — keeps the whole
agent, including its chat UI, deployable as a single App Service.

## 4. Files every agent needs

Copy these from `AgentToAgent.Chatbot` and adjust names/prompts:

| File | Purpose |
|---|---|
| `Models/AtlasModels.cs` | DTOs matching `/api/external/atlas/ask`'s response shape (`Answer`, `Matches[]` with `Id`, `Title`, `WorkflowId`, `Link`, `Score`). Keep in sync with DCP's `UseCaseAssistantResponse`. |
| `Models/ChatModels.cs` | The agent's own `/api/chat` request/response contract (`message`, `history`, `reply`, `usedAtlas`, `matches`). |
| `Services/AtlasAgentClient.cs` | Thin `HttpClient` wrapper that POSTs `{ "question": ... }` to `Atlas:BaseUrl/ask` with the `X-Atlas-Api-Key` header and maps failures to a safe fallback result instead of throwing. |
| `Services/ChatAgentService.cs` | The agent brain: builds the chat message list, registers `ask_atlas_assistant` as a `ChatTool.CreateFunctionTool(...)`, lets the model decide whether to call it, and produces the final reply. Swap the `SystemPrompt` text per agent's persona/domain. |
| `Program.cs` | Wires `AddHttpClient<IAtlasAgentClient, AtlasAgentClient>` (sets `BaseAddress` + API key header from config), registers `IChatAgentService`, serves `wwwroot/` as static files, and maps `POST /api/chat`. |
| `wwwroot/index.html`, `app.js`, `styles.css` | Minimal, dependency-free chat UI — fetches `/api/chat`, renders bubbles, shows an "Answered via Atlas" badge + match links when `usedAtlas` is true. |

## 5. Configuration contract

Every agent should expose the same two config sections (via `appsettings.json` /
`appsettings.Development.json` / environment variables / App Service app settings):

```json
{
  "AzureOpenAI": {
    "Endpoint": "https://<your-resource>.openai.azure.com/",
    "ApiKey": "",
    "ChatDeployment": "<deployment-name, not model name>"
  },
  "Atlas": {
    "BaseUrl": "https://<dcp-host>/api/external/atlas",
    "ApiKey": "<the key issued in step 2>"
  }
}
```

Notes:
- `AzureOpenAI:Endpoint` must be the **bare resource root** (no `/openai/...` suffix).
  `AzureOpenAIClient` builds the deployment path itself — a path-suffixed endpoint
  causes `404 Resource not found`.
- `ChatDeployment` is the **deployment name** you created in Azure AI Foundry/Portal,
  not the underlying model name — a mismatch also produces a 404.
- If `AzureOpenAI` values are missing, the agent should still start and `/api/chat`
  should return a friendly "not configured yet" message instead of crashing
  (see `ChatAgentService`'s nullable `_chatClient` pattern) — useful for verifying
  deployment before secrets are wired up.
- Never commit real keys. Add `appsettings.Development.json` to `.gitignore` at the
  repo root (already done here) and use Key Vault / App Service app settings in the cloud.

## 6. Local test checklist

1. Make sure DCP is running locally (or point `Atlas:BaseUrl` at a deployed instance).
2. Fill in `appsettings.Development.json` with a real Azure OpenAI endpoint/key/deployment
   and the Atlas API key from step 2.
3. `dotnet build` — catches any Azure OpenAI SDK API mismatches early.
4. `dotnet run`, open the HTTPS URL from `Properties/launchSettings.json`.
5. Ask a general question (should answer directly, `usedAtlas: false`).
6. Ask an Atlas/AI-Hub-specific question (should trigger the tool call,
   `usedAtlas: true`, and show use-case matches).
7. Optionally smoke-test `/api/chat` directly:
   ```powershell
   Invoke-RestMethod -Uri "http://localhost:<port>/api/chat" -Method Post `
     -ContentType "application/json" -Body '{"message":"hello"}'
   ```

## 7. Deploying to Azure (single App Service)

Because the agent is one ASP.NET Core project serving both the API and the static
chat UI, it deploys as a single Azure App Service:

1. `dotnet publish -c Release`
2. Create the App Service (Linux, .NET 8 runtime).
3. Set the same config keys as App Service **Application Settings**
   (`AzureOpenAI__Endpoint`, `AzureOpenAI__ApiKey`, `AzureOpenAI__ChatDeployment`,
   `Atlas__BaseUrl`, `Atlas__ApiKey`) — double underscore is the App Service convention
   for nested config sections. Prefer Key Vault references for the two `ApiKey` values.
4. Deploy the publish output (zip deploy, `az webapp deploy`, or your usual pipeline).
5. Verify `Atlas:BaseUrl` points at the **public** DCP URL (not `localhost`).

## 8. Replicating for a new agent

To stand up another external agent (different persona, different tools, same Atlas
integration):

1. Repeat steps 2–7 with a new project name and a **new** Atlas API key (don't reuse
   keys across agents).
2. Change `ChatAgentService`'s `SystemPrompt` and, if needed, add more `ChatTool`s
   beyond `ask_atlas_assistant` for other capabilities that agent needs.
3. Keep `Models/AtlasModels.cs` and `Services/AtlasAgentClient.cs` unchanged — they're
   the stable contract with DCP and shouldn't need to vary per agent.
4. Give the agent its own entry in DCP's External Agent Access admin list so usage can
   be tracked and revoked independently.
