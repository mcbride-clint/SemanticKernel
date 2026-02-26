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
│   └── Models/                # AgentInfo, AgentResponse, AgentSelection,
│                              # AgentRunResult, ConversationTurn, OrchestrationMetadata
├── Components/                # Blazor UI
│   ├── Layout/                # MainLayout.razor, NavMenu.razor
│   ├── Pages/                 # Chat.razor (/), DebugAgents.razor (/debug)
│   ├── App.razor, Routes.razor, _Imports.razor
├── Configuration/             # Strongly-typed options classes
├── Data/                      # Agent definition files (no compiled code)
│   ├── Agents/                # PDF agents: <FolderName>/agent.json + document.pdf
│   ├── DatabaseAgents/        # agents.json array of DB agent configs
│   ├── RestAgents/            # agents.json array of REST agent configs
│   └── TechnicalDrawingAgents/ # agents.json array of drawing agent configs
├── Infrastructure/            # Core business logic
│   ├── RetryHelper.cs         # Exponential-backoff retry utility (static)
│   ├── SemanticKernel/        # Kernel, router, registry, runners, plugins
│   │   └── Plugins/           # KernelFunction classes (DateTimePlugin, etc.)
│   ├── Attachments/           # AttachmentProcessor, DocumentSummaryService
│   ├── Database/              # DbAgentRunner, DbAgentLoader, IDbConnectionFactory
│   ├── Rest/                  # RestAgentRunner, RestAgentLoader
│   ├── TechnicalDrawing/      # TechnicalDrawingAgentRunner, Loader, Config
│   └── Telemetry/             # AgentChatActivitySource (structured tracing)
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
Chat.razor  →  [optional] IAttachmentProcessor.ProcessAsync()   ← new: file upload
                    │               (extracts text from PDF/text; summarizes via LLM)
                    │
                    └─ SkOrchestrationService.AskAsync(question, ct, enabledIds, attachment?)
                            │
                            ├─ 1. ROUTE  ─  SkAgentRouter.SelectAgentsAsync()
                            │               (LLM returns weighted JSON: id + confidence + reason)
                            │               Attachment summary injected into routing prompt.
                            │
                            ├─ 2. RUN    ─  CompositeAgentRunner.RunAsync() [parallel, isolated]
                            │               ├─ "pdf"              → SkAgentRunner
                            │               ├─ "database"         → DbAgentRunner
                            │               ├─ "rest"             → RestAgentRunner
                            │               └─ "technical-drawing"→ TechnicalDrawingAgentRunner
                            │               Each runner receives attachment; uses vision or text path.
                            │               Each runner has retry-with-backoff on LLM calls.
                            │               Per-agent errors are isolated — partial results proceed.
                            │
                            └─ 3. SYNTHESIZE  ─  SkAgentRouter.SynthesizeAsync()
                                                 (streams answer + injects conversation history)
```

**Key design rules:**
- The routing LLM call has **no tools/plugins** — it must return clean JSON.
- `SkAgentRunner` and `SynthesizeAsync` both use `FunctionChoiceBehavior.Auto()` so KernelFunctions are available.
- Agents run **in parallel** in step 2 via `Task.WhenAll`. Failures are caught per-agent; partial results proceed to synthesis.
- The final answer is **streamed token by token** using `IAsyncEnumerable<string>`.
- Up to 10 conversation turns are stored per Blazor circuit; history is passed to `SynthesizeAsync` for multi-turn context.
- `AgentChatActivitySource` emits `Activity` spans for each stage (picked up by OpenTelemetry exporters).
- Attachment processing (text extraction + LLM summarization) happens **before** `AskAsync` is called, in the Blazor component. The processed `AttachedDocument` is passed through the full pipeline.

---

## Core Abstractions (Interfaces)

All in `Abstractions/`:

| Interface | Responsibility |
|---|---|
| `IOrchestrationService` | Full pipeline: route → run → synthesize. Scoped per Blazor circuit. |
| `IAgentRouter` | LLM-based agent selector + response synthesizer. |
| `IAgentRegistry` | Thread-safe lookup of all loaded agents. |
| `IAgentSource` | Loads one category of agents (PDF, DB, REST, drawing, etc.). |
| `IAgentRunner` | Executes a single agent given a question + optional attachment. |
| `IAttachmentProcessor` | Processes uploaded files: extracts text, generates LLM summary. |
| `IDbConnectionFactory` | Abstracts database provider (SQL Server, PostgreSQL, etc.). |

### Data Models

| Type | Location | Purpose |
|---|---|---|
| `AgentInfo` | `Abstractions/Models/` | Immutable record: Id, Name, Description, PdfText, SourceType |
| `AgentSelection` | `Abstractions/Models/` | Router output: AgentId, Confidence (0–1), optional Reason |
| `AgentResponse` | `Abstractions/Models/` | Single agent result: Content, EstimatedTokens, Elapsed |
| `AgentRunResult` | `Abstractions/Models/` | Per-agent execution outcome: Success, ErrorMessage, Elapsed, Tokens |
| `ConversationTurn` | `Abstractions/Models/` | A history turn: Role ("user"/"assistant"), Content |
| `OrchestrationMetadata` | `Abstractions/Models/` | CorrelationId, SelectedAgents (with confidence), TotalElapsed, AgentResults |
| `AttachedDocument` | `Abstractions/Models/` | User-uploaded file: FileName, ContentType, Bytes, ExtractedText, Summary |
| `ChatEntry` | `Models/` | UI chat message: Id (Guid), Role, Content (mutable), AgentInfo (mutable), AttachmentName |
| `DatabaseAgentConfig` | `Infrastructure/Database/` | DB agent: ConnectionString, ContextQuery, Parameters |
| `RestAgentConfig` | `Infrastructure/Rest/` | REST agent: UrlTemplate, Method, Parameters, Headers |
| `TechnicalDrawingAgentConfig` | `Infrastructure/TechnicalDrawing/` | Drawing agent: Id, Name, Description, RequiresAttachment |

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
.AddSingleton<AgentChatActivitySource>()   // structured tracing spans

// Plugins (KernelFunction singletons)
.AddSingleton(KernelPluginFactory.CreateFromObject(new DateTimePlugin(), "DateTime"))

// Attachment processing
.AddSingleton<DocumentSummaryService>()
.AddSingleton<IAttachmentProcessor, AttachmentProcessor>()

// Agent sources (IAgentSource — all are merged by the registry)
.AddSingleton<IAgentSource, AgentLoader>()                    // PDF agents
.AddSingleton<IAgentSource, DbAgentLoader>()                  // DB agents
.AddSingleton<IAgentSource, RestAgentLoader>()                // REST agents
.AddSingleton<IAgentSource, TechnicalDrawingAgentLoader>()    // Drawing agents

// Registry (merges all IAgentSource implementations)
.AddSingleton<IAgentRegistry, SkAgentRegistry>()

// Runners
.AddSingleton<IAgentRunner, CompositeAgentRunner>()  // dispatcher
.AddSingleton<SkAgentRunner>()
.AddSingleton<DbAgentRunner>()
.AddSingleton<RestAgentRunner>()
.AddSingleton<TechnicalDrawingAgentRunner>()

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

### Technical Drawing Agents

**Location:** `Data/TechnicalDrawingAgents/agents.json`

```json
[{
  "Id":                 "technical-drawing-extractor",
  "Name":               "Technical Drawing & Spec Extractor",
  "Description":        "Extracts structured data from technical drawings, engineering specifications...",
  "RequiresAttachment": true
}]
```

These agents require an attached file. `TechnicalDrawingAgentRunner` uses a **vision path** (multimodal `ImageContent` in `ChatMessageContentItemCollection`) when the attachment is an image, and a **text path** when the attachment has extractable text. If no attachment is provided, the runner returns a helpful message asking the user to attach a document.

The system prompt extracts: title block, BOM, dimensions/tolerances, material specs, surface finish, GD&T callouts, and manufacturing notes.

---

## Attachment Pipeline

When a user attaches a file in Chat.razor:

1. **`IAttachmentProcessor.ProcessAsync(fileName, contentType, stream)`**
   - Reads all bytes from the stream.
   - For PDFs: calls `PdfTextExtractor.ExtractFromBytes()` to extract text.
   - For text/csv/md: decodes as UTF-8.
   - For images: stores raw bytes (no text extraction).
   - Calls `DocumentSummaryService.SummarizeAsync()` — makes a lightweight LLM call to generate a structured summary (document type, key data, what agents it might be useful for).
   - Returns an `AttachedDocument` record.

2. **`SkAgentRouter.SelectAgentsAsync()`** — attachment summary is appended to the routing prompt, guiding the LLM to select appropriate agents (e.g., `technical-drawing-extractor` for engineering docs).

3. **All runners** receive the `AttachedDocument?` parameter and include relevant content in their user message to the LLM.

### AttachedDocument Properties

```csharp
public sealed record AttachedDocument(
    string FileName,
    string ContentType,
    byte[] Bytes,
    string ExtractedText,
    string Summary)
{
    public bool HasText  => !string.IsNullOrWhiteSpace(ExtractedText);
    public bool IsImage  => ContentType.StartsWith("image/", ...);
    public bool IsPdf    => ContentType == "application/pdf" || FileName.EndsWith(".pdf");
    public long SizeBytes => Bytes.LongLength;
}
```

### Supported File Types (Chat.razor `InputFile` accept list)
- Documents: `.pdf`, `.txt`, `.csv`, `.md`
- Images: `.png`, `.jpg`, `.jpeg`, `.gif`, `.webp`
- Max size: 10 MB

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
| `Chat.razor` | `/` | Main streaming chat UI with agent selection panel |
| `DebugAgents.razor` | `/debug` | Lists all agents; per-agent timing & token counts; LLM ping |
| `MainLayout.razor` | — | Flex layout with sidebar |
| `NavMenu.razor` | — | Links to Chat and Debug pages |

**Chat.razor features:**
- **Agent selection panel** — collapsible panel showing all agents grouped by type with checkboxes. Only enabled agents are passed to `AskAsync`.
- **Markdown rendering** — assistant messages rendered via `Markdig.Markdown.ToHtml` into `MarkupString`.
- **Copy to clipboard** — per-message copy button using `IJSRuntime`; shows "✓ Copied" for 2 seconds.
- **Agent disclosure** — inline badge after each assistant message: `"Consulted: Tax Guide (95%) — Direct tax question"`.

**Chat.razor streaming pattern:**
```csharp
var enabledSet = (IReadOnlySet<string>)_enabledIds;
await foreach (var token in Orchestrator.AskAsync(question, _cts.Token, enabledSet))
{
    assistantEntry.Content += token;
    StateHasChanged();      // re-render per token
    await Task.Yield();     // yield to Blazor render loop
}
// After streaming: set mutable AgentInfo for inline disclosure
assistantEntry.AgentInfo = "Consulted: " + ...;
```

---

## Key Implementation Details

### KernelFactory

`Infrastructure/SemanticKernel/KernelFactory.cs`

- Creates a new `Kernel` per call via `Create()`.
- Adds all registered `KernelPlugin` singletons from DI.
- Supports custom CA certificate for private OpenAI-compatible endpoints (`OpenAiOptions.CaCertPath`).
- Uses `AddOpenAIChatCompletion` with the configured endpoint/key/modelId.

### RetryHelper

`Infrastructure/RetryHelper.cs`

Static utility used by all runners and `SkAgentRouter` for transient LLM / HTTP failures:
- Up to 3 attempts, backoff at 1s / 2s / 4s.
- Retries on `HttpRequestException`, `TimeoutException`, `TaskCanceledException` (timeouts).
- Never retries `OperationCanceledException` (user cancellation).

```csharp
var result = await RetryHelper.ExecuteAsync(
    async ck => await chatService.GetChatMessageContentAsync(history, settings, kernel, ck),
    _log, "operation-name", maxAttempts: 3, ct);
```

### AgentChatActivitySource

`Infrastructure/Telemetry/AgentChatActivitySource.cs`

Singleton that emits `System.Diagnostics.Activity` spans for:
- `orchestration` (tagged with `correlation_id`)
- `routing` (tagged with `available_agents`)
- `agent_run` (tagged with `agent.id`, `agent.name`, `agent.source_type`)
- `synthesis` (tagged with `agent_response_count`)

These are automatically picked up by any registered OpenTelemetry exporter. Register with `AddSource("BlazorAgentChat")`.

### ParameterExtractor

`Infrastructure/ParameterExtractor.cs`

- Makes a lightweight LLM call to extract structured parameters from natural language.
- Returns `Dictionary<string, string?>` — `null` for parameters the user did not specify.
- Used by both `DbAgentRunner` and `RestAgentRunner`.

### SkAgentRouter

`Infrastructure/SemanticKernel/SkAgentRouter.cs`

- `SelectAgentsAsync` — returns `IReadOnlyList<AgentSelection>` with confidence scores (0–1) and optional reasons. LLM prompt asks for `[{"id":"...","confidence":0.9,"reason":"..."}]`. Falls back to `[]` on JSON parse failure. Uses `RetryHelper` for transient errors.
- `SynthesizeAsync` — prepends prior `ConversationTurn` history as alternating user/assistant messages before the current question. Tools enabled (`FunctionChoiceBehavior.Auto()`).

### SkOrchestrationService

`Infrastructure/SemanticKernel/SkOrchestrationService.cs`

Scoped per Blazor circuit. Key behaviours:
- **Conversation history** — maintains `List<ConversationTurn>` (max 10 turns / 5 exchanges). History is passed to `SynthesizeAsync`; updated after each successful response.
- **Agent filtering** — optional `enabledAgentIds` parameter filters the agent pool before routing.
- **Per-agent error isolation** — `RunAgentSafeAsync` wraps each runner call; failures return a failed `AgentRunResult` without aborting other agents. Synthesis proceeds with whatever agents succeeded.
- **Activity spans** — `AgentChatActivitySource` wraps each pipeline stage.
- **`LastMetadata`** — exposes `OrchestrationMetadata` with `SelectedAgents` (with confidence), `AgentResults` (per-agent timing/tokens/errors), and `TotalElapsed`.

### CompositeAgentRunner

`Infrastructure/CompositeAgentRunner.cs`

Dispatches by `AgentInfo.SourceType`:
- `"pdf"` → `SkAgentRunner`
- `"database"` → `DbAgentRunner`
- `"rest"` → `RestAgentRunner`
- default → `SkAgentRunner`

### DbAgentRunner / RestAgentRunner

Both wrap their final LLM interpretation call in `RetryHelper.ExecuteAsync` for transient failure resilience.

---

## Naming Conventions

- **Namespaces:** Match directory structure under `BlazorAgentChat.*`
- **Interfaces:** `I` prefix — `IAgentRunner`, `IAgentRouter`, etc.
- **Implementations:** Named after what they implement minus the `I` — `SkAgentRunner`, `SkAgentRouter`; or descriptive — `CompositeAgentRunner`, `NoopDbConnectionFactory`
- **Records:** Immutable data containers use `sealed record` — `AgentInfo`, `AgentResponse`, `AgentSelection`, `ConversationTurn`
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
- **Markdown rendering:** `MarkupString` bypasses Blazor HTML encoding. Content comes from the LLM (trusted), not from raw user input. For public-facing deployments add HTML sanitization before rendering.

---

## Logging Conventions

- All infrastructure classes accept `ILogger<T>` via constructor injection.
- Use `LogInformation` for normal operation milestones (agent loaded, query executed).
- Use `LogDebug` for detailed tracing (token counts, elapsed times).
- Use `LogWarning` for recoverable issues (truncated results, missing optional params, retry attempts).
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
| `Markdig` | `0.*` | Markdown → HTML rendering in Chat.razor |
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

### Add a Technical Drawing Agent
1. Add an entry to `Data/TechnicalDrawingAgents/agents.json` with `Id`, `Name`, `Description`, and `RequiresAttachment: true`.
2. No code changes needed — the agent uses the shared `TechnicalDrawingAgentRunner`.
3. Customize the system prompt in `TechnicalDrawingAgentRunner` if specialized extraction logic is needed.

### Change Supported Attachment Types
- Update the `accept` attribute on `InputFile` in `Chat.razor`.
- Update `AttachmentProcessor.ProcessAsync` to handle the new MIME type (extract text, store bytes, etc.).
- If the type needs vision support, ensure `AttachedDocument.IsImage` returns `true` for it.

### Add a KernelFunction Plugin
1. Create a class in `Infrastructure/SemanticKernel/Plugins/` with `[KernelFunction]` methods.
2. Register with `KernelPluginFactory.CreateFromObject(...)` in `Program.cs`.

### Swap the Database Provider
1. Implement `IDbConnectionFactory` opening the appropriate `DbConnection`.
2. Replace `NoopDbConnectionFactory` registration in `Program.cs`.

### Add a New Agent Type (e.g., GraphQL)
1. Create `IAgentSource` implementation → returns `AgentInfo` records with unique `SourceType`.
2. Create `IAgentRunner` implementation.
3. Register both in `Program.cs`.
4. Add dispatch case in `CompositeAgentRunner`.

### Add OpenTelemetry Tracing
1. Add `OpenTelemetry.Extensions.Hosting` and an exporter (e.g., `OpenTelemetry.Exporter.Jaeger`).
2. In `Program.cs`:
```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(b => b
        .AddSource(AgentChatActivitySource.SourceName)
        .AddJaegerExporter());
```

### Extend Chat UI
- All UI lives in `Components/Pages/Chat.razor` and `Components/Layout/`.
- Styling is in `wwwroot/app.css` — BEM-like class names tied to component roles.
- `StateHasChanged()` + `Task.Yield()` is the streaming update pattern.
- Agent selection state (`_enabledIds: HashSet<string>`) lives in `Chat.razor`. Pass it to `AskAsync` as the third parameter.
- Attachment state (`_processedAttachment`, `_pendingAttachmentName`) is managed in `Chat.razor`. `InputFile` triggers `HandleFileSelected` which calls `IAttachmentProcessor` and sets `_processedAttachment`. This is passed to `AskAsync` as the fourth parameter and cleared after send.
