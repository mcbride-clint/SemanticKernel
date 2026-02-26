# CLAUDE.md — BlazorAgentChat Codebase Guide

## Project Overview

**BlazorAgentChat** is a Blazor Server application that routes user natural-language questions to expert AI agents backed by PDFs, databases, or REST APIs. Built on [Microsoft Semantic Kernel](https://github.com/microsoft/semantic-kernel) v1.x.

- **Framework:** .NET 8.0 / C# 12, Blazor Server (Interactive Server Components)
- **Root namespace:** `BlazorAgentChat`
- **Solution:** `SemanticKernel.sln` → single project `BlazorAgentChat.csproj`
- **Dev URL:** `https://localhost:49869` / `http://localhost:49870`

---

## Repository Layout

```
SemanticKernel/
├── Abstractions/              # Interfaces + shared data models
│   └── Models/                # AgentInfo, AgentResponse, OrchestrationMetadata
├── Components/                # Blazor UI
│   ├── Layout/                # MainLayout.razor, NavMenu.razor
│   ├── Pages/                 # Chat.razor (/), DebugAgents.razor (/debug)
│   ├── App.razor, Routes.razor, _Imports.razor
├── Configuration/             # Strongly-typed options classes
├── Data/                      # Agent definition files (no compiled code)
│   ├── Agents/                # PDF agents: <FolderName>/agent.json + document.pdf
│   ├── DatabaseAgents/        # agents.json array of DB agent configs
│   └── RestAgents/            # agents.json array of REST agent configs
├── Infrastructure/            # Core business logic
│   ├── SemanticKernel/        # Kernel, router, registry, runners, plugins
│   │   └── Plugins/           # KernelFunction classes (DateTimePlugin, etc.)
│   ├── Database/              # DbAgentRunner, DbAgentLoader, IDbConnectionFactory
│   └── Rest/                  # RestAgentRunner, RestAgentLoader
├── Models/                    # UI-facing models (ChatEntry)
├── Services/                  # Utilities: PdfTextExtractor, AgentLoader
├── Properties/                # launchSettings.json
├── wwwroot/                   # Static assets (app.css)
├── Program.cs                 # App startup + all DI registrations
├── appsettings.json           # Configuration template (fill in OpenAI creds)
└── appsettings.Development.json  # Verbose logging overrides for dev
```

---

## Build & Run

```bash
# Build
dotnet build BlazorAgentChat.csproj

# Run (reads appsettings.json for API config)
dotnet run --project BlazorAgentChat.csproj

# Run in watch mode (hot reload)
dotnet watch --project BlazorAgentChat.csproj
```

There are no test projects currently in the solution.

### Required Configuration

Before running, populate `appsettings.json`:

```json
{
  "OpenAI": {
    "Endpoint":   "https://your-endpoint/v1",
    "ApiKey":     "your-api-key",
    "ModelId":    "gpt-4o",
    "CaCertPath": ""
  }
}
```

`CaCertPath` is optional — only needed for private endpoints with self-signed TLS certificates.

---

## Architecture: The Orchestration Pipeline

Every user question flows through three stages:

```
Chat.razor  →  SkOrchestrationService.AskAsync()
                    │
                    ├─ 1. ROUTE  ─  SkAgentRouter.SelectAgentsAsync()
                    │               (LLM picks agent IDs from available list)
                    │
                    ├─ 2. RUN    ─  CompositeAgentRunner.RunAsync() [parallel]
                    │               ├─ "pdf"      → SkAgentRunner
                    │               ├─ "database" → DbAgentRunner
                    │               └─ "rest"     → RestAgentRunner
                    │
                    └─ 3. SYNTHESIZE  ─  SkAgentRouter.SynthesizeAsync()
                                         (streaming final answer to UI)
```

**Key design rules:**
- The routing LLM call has **no tools/plugins** — it must return clean JSON.
- `SkAgentRunner` and `SynthesizeAsync` both use `FunctionChoiceBehavior.Auto()` so KernelFunctions are available.
- Agents run **in parallel** in step 2 via `Task.WhenAll`.
- The final answer is **streamed token by token** using `IAsyncEnumerable<string>`.

---

## Core Abstractions (Interfaces)

All in `Abstractions/`:

| Interface | Responsibility |
|---|---|
| `IOrchestrationService` | Full pipeline: route → run → synthesize. Scoped per Blazor circuit. |
| `IAgentRouter` | LLM-based agent selector + response synthesizer. |
| `IAgentRegistry` | Thread-safe lookup of all loaded agents. |
| `IAgentSource` | Loads one category of agents (PDF, DB, REST). |
| `IAgentRunner` | Executes a single agent given a question. |
| `IDbConnectionFactory` | Abstracts database provider (SQL Server, PostgreSQL, etc.). |

### Data Models

| Type | Location | Purpose |
|---|---|---|
| `AgentInfo` | `Abstractions/Models/` | Immutable record: Id, Name, Description, PdfText, SourceType |
| `AgentResponse` | `Abstractions/Models/` | Single agent result: Content, EstimatedTokens, Elapsed |
| `OrchestrationMetadata` | `Abstractions/Models/` | CorrelationId, SelectedAgentIds, TotalElapsed |
| `ChatEntry` | `Models/` | UI chat message: Role (User/Assistant/System), Content, Timestamp |
| `DatabaseAgentConfig` | `Infrastructure/Database/` | DB agent: ConnectionString, ContextQuery, Parameters |
| `RestAgentConfig` | `Infrastructure/Rest/` | REST agent: UrlTemplate, Method, Parameters, Headers |

---

## Configuration Classes

Located in `Configuration/`:

```csharp
// Bound from "OpenAI" section
public sealed class OpenAiOptions
{
    public string Endpoint    { get; init; }
    public string ApiKey      { get; init; }
    public string ModelId     { get; init; } = "gpt-4o";
    public string CaCertPath  { get; init; }
}

// Bound from "AgentChat" section
public sealed class AgentChatOptions
{
    public string AgentsDirectory { get; init; } = "Data/Agents";
}
```

---

## Dependency Injection Map (Program.cs)

All registrations live in `Program.cs`. Key entries:

```csharp
// Options
.Configure<OpenAiOptions>(cfg.GetSection("OpenAI"))
.Configure<AgentChatOptions>(cfg.GetSection("AgentChat"))

// Kernel factory (creates SK Kernel with all plugins)
.AddSingleton<KernelFactory>()

// Plugins (KernelFunction singletons)
.AddSingleton(KernelPluginFactory.CreateFromObject(new DateTimePlugin(), "DateTime"))

// Agent sources (IAgentSource — all are merged by the registry)
.AddSingleton<IAgentSource, AgentLoader>()      // PDF agents
.AddSingleton<IAgentSource, DbAgentLoader>()    // DB agents
.AddSingleton<IAgentSource, RestAgentLoader>()  // REST agents

// Registry (merges all IAgentSource implementations)
.AddSingleton<IAgentRegistry, SkAgentRegistry>()

// Runners
.AddSingleton<IAgentRunner, CompositeAgentRunner>()  // dispatcher
.AddSingleton<SkAgentRunner>()
.AddSingleton<DbAgentRunner>()
.AddSingleton<RestAgentRunner>()

// Database (swap NoopDbConnectionFactory for a real one)
.AddSingleton<IDbConnectionFactory, NoopDbConnectionFactory>()

// Router + orchestration
.AddSingleton<IAgentRouter, SkAgentRouter>()
.AddScoped<IOrchestrationService, SkOrchestrationService>()
```

`IOrchestrationService` is **Scoped** (one instance per Blazor circuit). Everything else is Singleton.

---

## Agent Types

### PDF Agents

**Location:** `Data/Agents/<FolderName>/`

Each subdirectory becomes an agent. The folder name becomes the agent ID (lowercase, spaces → hyphens).

Required files:
- `agent.json` — `{ "Name": "...", "Description": "..." }`
- `document.pdf` — the source document

The entire PDF text is embedded in the system prompt at runtime. No code changes needed to add a new PDF agent.

### Database Agents

**Location:** `Data/DatabaseAgents/agents.json`

```json
[{
  "Id":               "unique-id",
  "Name":             "Human name",
  "Description":      "What data this covers (used by the router LLM)",
  "ConnectionString": "Server=...;Database=...;",
  "ContextQuery":     "SELECT ... WHERE (@Param IS NULL OR col = @Param)",
  "MaxRows":          200,
  "Parameters": [{
    "Name":        "Param",
    "Description": "What this filters",
    "Required":    false
  }]
}]
```

**Important:** Use `@ParamName` placeholders — values are bound as `DbParameter` (no SQL injection risk). Pattern `(@P IS NULL OR col = @P)` makes a parameter optional.

**To connect a real database:** Implement `IDbConnectionFactory` and register it in `Program.cs` instead of `NoopDbConnectionFactory`.

### REST Agents

**Location:** `Data/RestAgents/agents.json`

```json
[{
  "Id":               "unique-id",
  "Name":             "Human name",
  "Description":      "What this API exposes",
  "UrlTemplate":      "https://api.example.com/resource/{PathParam}",
  "Method":           "GET",
  "TimeoutSeconds":   30,
  "MaxResponseChars": 6000,
  "StaticHeaders":    { "Authorization": "Bearer token" },
  "Parameters": [{
    "Name":        "PathParam",
    "Description": "What this is",
    "Required":    true,
    "Location":    "path"
  }, {
    "Name":        "Filter",
    "Description": "Optional filter",
    "Required":    false,
    "Location":    "query",
    "QueryKey":    "filter"
  }]
}]
```

Parameter `Location`: `"path"` replaces `{Name}` in the URL template; `"query"` appends `?QueryKey=value`.

---

## KernelFunction Plugins

Plugins give the LLM the ability to call C# functions (real-time data, calculations, etc.). They are available in `SkAgentRunner` and `SynthesizeAsync` (both use `FunctionChoiceBehavior.Auto()`).

### Adding a New Plugin

1. Create a class in `Infrastructure/SemanticKernel/Plugins/`:

```csharp
using System.ComponentModel;
using Microsoft.SemanticKernel;

namespace BlazorAgentChat.Infrastructure.SemanticKernel.Plugins;

public sealed class MyPlugin
{
    [KernelFunction("function_name")]        // snake_case name the LLM sees
    [Description("What this function does")] // shown to the LLM
    public string MyFunction(
        [Description("What this param means")] string input)
    {
        return result;
    }
}
```

2. Register in `Program.cs`:

```csharp
builder.Services.AddSingleton(
    KernelPluginFactory.CreateFromObject(new MyPlugin(), "MyPlugin"));
```

`KernelFactory` automatically adds all registered `KernelPlugin` singletons to every new `Kernel` instance.

### Built-in Plugin: DateTimePlugin

`Infrastructure/SemanticKernel/Plugins/DateTimePlugin.cs`

| KernelFunction | Returns |
|---|---|
| `get_current_date` | `yyyy-MM-dd` |
| `get_current_time` | `HH:mm:ss` |

---

## Adding a New Agent Type (Extension Points)

1. Implement `IAgentSource` — loads configs, returns `List<AgentInfo>` with a unique `SourceType` string.
2. Implement `IAgentRunner` — executes one agent for a question.
3. Register both in `Program.cs`.
4. Add a case to `CompositeAgentRunner`'s dispatch switch on `AgentInfo.SourceType`.

---

## Blazor Components

| Component | Route | Purpose |
|---|---|---|
| `Chat.razor` | `/` | Main streaming chat UI |
| `DebugAgents.razor` | `/debug` | Lists all loaded agents; LLM ping test |
| `MainLayout.razor` | — | Flex layout with sidebar |
| `NavMenu.razor` | — | Links to Chat and Debug pages |

**Chat.razor streaming pattern:**
```csharp
await foreach (var token in Orchestrator.AskAsync(question, _cts.Token))
{
    assistantEntry.Content += token;
    StateHasChanged();      // re-render per token
    await Task.Yield();     // yield to Blazor render loop
}
```

The `IOrchestrationService` is injected as a scoped service — one instance per Blazor circuit (user session).

---

## Key Implementation Details

### KernelFactory

`Infrastructure/SemanticKernel/KernelFactory.cs`

- Creates a new `Kernel` per call via `Create()`.
- Adds all registered `KernelPlugin` singletons from DI.
- Supports custom CA certificate for private OpenAI-compatible endpoints (`OpenAiOptions.CaCertPath`).
- Uses `AddOpenAIChatCompletion` with the configured endpoint/key/modelId.

### ParameterExtractor

`Infrastructure/SemanticKernel/ParameterExtractor.cs`

- Makes a lightweight LLM call to extract structured parameters from natural language.
- Returns `Dictionary<string, string?>` — `null` for parameters the user did not specify.
- Used by both `DbAgentRunner` and `RestAgentRunner`.

### SkAgentRouter

`Infrastructure/SemanticKernel/SkAgentRouter.cs`

- `SelectAgentsAsync` — system prompt lists all available agents; LLM returns JSON array of agent IDs. **No tools enabled** on this call.
- `SynthesizeAsync` — streams the final answer combining all agent responses; **tools enabled** (`FunctionChoiceBehavior.Auto()`).

### CompositeAgentRunner

`Infrastructure/SemanticKernel/CompositeAgentRunner.cs`

Dispatches by `AgentInfo.SourceType`:
- `"pdf"` → `SkAgentRunner`
- `"database"` → `DbAgentRunner`
- `"rest"` → `RestAgentRunner`
- default → `SkAgentRunner`

### DbAgentRunner

`Infrastructure/Database/DbAgentRunner.cs`

1. Extract parameters with `ParameterExtractor`.
2. Validate required parameters (throws if missing).
3. Execute parameterized SQL via `IDbConnectionFactory`.
4. Cap rows at `MaxRows` (default 500) and warn if truncated.
5. Send result table to LLM for natural-language interpretation.

### RestAgentRunner

`Infrastructure/Rest/RestAgentRunner.cs`

1. Extract parameters with `ParameterExtractor`.
2. Substitute path params into `UrlTemplate`; append query params.
3. Send HTTP request with static headers and configurable timeout.
4. Truncate response body at `MaxResponseChars` (default 8000).
5. Send body to LLM for interpretation.

---

## Naming Conventions

- **Namespaces:** Match directory structure under `BlazorAgentChat.*`
- **Interfaces:** `I` prefix — `IAgentRunner`, `IAgentRouter`, etc.
- **Implementations:** Named after what they implement minus the `I` — `SkAgentRunner`, `SkAgentRouter`; or descriptive — `CompositeAgentRunner`, `NoopDbConnectionFactory`
- **Records:** Immutable data containers use `sealed record` — `AgentInfo`, `AgentResponse`, `DatabaseAgentConfig`
- **Options classes:** Suffix `Options` — `OpenAiOptions`, `AgentChatOptions`; use `const string SectionName` for config key
- **KernelFunction names:** `snake_case` (e.g., `get_current_date`)
- **Agent IDs:** `kebab-case` derived from folder/config names
- **C# features:** `nullable enable`, `implicit usings`, file-scoped namespaces, primary constructors where appropriate

---

## Security Conventions

- **SQL parameters:** Always use `DbParameter` binding — never string interpolation in queries.
- **API secrets:** Do not commit real API keys or tokens in JSON files. Use environment variables, .NET Secret Manager, or Azure Key Vault in production.
- **Context limits:** `MaxRows` (DB) and `MaxResponseChars` (REST) prevent LLM context overflow — always set these on new agent configs.
- **Parameter extraction:** LLM extracts parameters from user questions; actual DB/HTTP calls use only the extracted structured values, not raw user input.

---

## Logging Conventions

- All infrastructure classes accept `ILogger<T>` via constructor injection.
- Use `LogInformation` for normal operation milestones (agent loaded, query executed).
- Use `LogDebug` for detailed tracing (token counts, elapsed times).
- Use `LogWarning` for recoverable issues (truncated results, missing optional params).
- Use `LogError` for failures with exceptions.
- Include correlation IDs from `OrchestrationMetadata.CorrelationId` in log messages where available.

**Log levels by namespace (appsettings.json):**
```json
"LogLevel": {
  "BlazorAgentChat":                "Debug",
  "Microsoft.SemanticKernel":       "Warning",
  "System.Net.Http.HttpClient":     "Warning"
}
```
Development overrides these to `Trace` / `Debug`.

---

## Configuration Reference

| Key | Default | Description |
|---|---|---|
| `OpenAI:Endpoint` | — | Base URL of OpenAI-compatible API |
| `OpenAI:ApiKey` | — | API key |
| `OpenAI:ModelId` | `gpt-4o` | LLM model name |
| `OpenAI:CaCertPath` | `""` | Path to `.pem` CA bundle for self-signed TLS |
| `AgentChat:AgentsDirectory` | `Data/Agents` | Directory scanned for PDF agents |

---

## NuGet Dependencies

| Package | Version | Purpose |
|---|---|---|
| `Microsoft.SemanticKernel` | `1.*` | Core LLM orchestration |
| `Microsoft.SemanticKernel.Agents.Core` | `1.*` | Agent management |
| `Microsoft.SemanticKernel.Connectors.OpenAI` | `1.*` | OpenAI API connector |
| `PdfPig` | `0.*` | PDF text extraction |

---

## Common Tasks for AI Assistants

### Add a PDF Agent
1. Create `Data/Agents/<AgentFolderName>/agent.json` with `Name` and `Description`.
2. Place `document.pdf` in the same folder.
3. No code changes needed — agents are loaded at startup.

### Add a Database Agent
1. Add an entry to `Data/DatabaseAgents/agents.json`.
2. Use `@ParamName` placeholders in `ContextQuery`.
3. Implement and register `IDbConnectionFactory` if not already done.

### Add a REST Agent
1. Add an entry to `Data/RestAgents/agents.json`.
2. Define `UrlTemplate`, `Method`, `Parameters`, and optionally `StaticHeaders`.

### Add a KernelFunction Plugin
1. Create a class in `Infrastructure/SemanticKernel/Plugins/` with `[KernelFunction]` methods.
2. Register with `KernelPluginFactory.CreateFromObject(...)` in `Program.cs`.

### Swap the Database Provider
1. Implement `IDbConnectionFactory` opening the appropriate `DbConnection`.
2. Replace `NoopDbConnectionFactory` registration in `Program.cs`.

### Add a New Agent Type (e.g., GraphQL)
1. Create `IAgentSource` implementation → `RegisterAsGraphQlAgent()` etc.
2. Create `IAgentRunner` implementation.
3. Register both in `Program.cs`.
4. Add dispatch case in `CompositeAgentRunner`.

### Extend Chat UI
- All UI lives in `Components/Pages/Chat.razor` and `Components/Layout/`.
- Styling is in `wwwroot/app.css` — BEM-like class names tied to component roles.
- `StateHasChanged()` + `Task.Yield()` is the streaming update pattern.
