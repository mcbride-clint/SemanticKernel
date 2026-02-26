# BlazorAgentChat

A Blazor Server application that routes natural-language questions to expert AI agents backed by PDFs, databases, REST APIs, and technical drawings. Built on [Microsoft Semantic Kernel](https://github.com/microsoft/semantic-kernel) v1.x.

## Features

- **Multi-agent routing** — LLM selects the best agent(s) for each question, returning confidence scores and reasons
- **Parallel agent execution** — multiple agents answer concurrently; failures are isolated so partial results still proceed
- **Streaming responses** — answers stream token-by-token directly to the UI
- **Conversation history** — up to 10 turns of context kept per session for multi-turn Q&A
- **File attachments** — attach PDFs, images, or text files; the system extracts content and summarizes it to inform routing
- **Technical drawing extraction** — dedicated agent extracts structured data (BOM, dimensions, tolerances, GD&T, material specs) from engineering documents and images
- **Agent selection panel** — toggle individual agents on/off per conversation
- **Markdown rendering** — assistant responses rendered with full Markdown support
- **Copy to clipboard** — one-click copy on every assistant message
- **Agent disclosure** — each answer shows which agents were consulted and their confidence
- **Debug page** — lists all loaded agents with per-agent timing, token counts, and routing confidence bars
- **Retry with backoff** — all LLM and HTTP calls automatically retry on transient failures
- **OpenTelemetry tracing** — structured activity spans for routing, agent execution, and synthesis

## Quick Start

### Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download)
- An OpenAI-compatible API endpoint (OpenAI, Azure OpenAI, or any compatible provider)

### Configuration

Edit `appsettings.json`:

```json
{
  "OpenAI": {
    "Endpoint": "https://your-endpoint/v1",
    "ApiKey":   "your-api-key",
    "ModelId":  "gpt-4o"
  }
}
```

`CaCertPath` is optional — only needed for private endpoints with self-signed TLS certificates.

### Run

```bash
dotnet run --project BlazorAgentChat.csproj
```

Open `https://localhost:49869` (or `http://localhost:49870`).

## Agent Types

### PDF Agents

Drop a folder into `Data/Agents/` containing `agent.json` and `document.pdf` — no code changes required.

```
Data/Agents/
└── MyKnowledgeBase/
    ├── agent.json     # { "Name": "...", "Description": "..." }
    └── document.pdf
```

### Database Agents

Add entries to `Data/DatabaseAgents/agents.json`. Parameters are extracted from natural language and bound as `DbParameter` (SQL-injection-safe). Swap `NoopDbConnectionFactory` for your provider in `Program.cs`.

### REST Agents

Add entries to `Data/RestAgents/agents.json` with a URL template, method, and parameter definitions. Supports path and query parameters, static headers, and configurable timeouts.

### Technical Drawing Agents

Add entries to `Data/TechnicalDrawingAgents/agents.json`. These agents accept attached engineering documents (PDFs or images) and extract:

- Title block (part number, revision, drawn by, date)
- Bill of Materials
- Dimensions and tolerances
- Material specifications and surface finish
- GD&T callouts
- Manufacturing and assembly notes

## File Attachments

Click the **📎** button to attach a file before sending a question. Supported types:

| Type | Extensions |
|------|-----------|
| Documents | `.pdf`, `.txt`, `.csv`, `.md` |
| Images | `.png`, `.jpg`, `.jpeg`, `.gif`, `.webp` |

Maximum size: **10 MB**

The attachment is processed before routing:
1. Text is extracted (for PDFs and text files) or stored as raw bytes (for images)
2. An LLM generates a brief summary of the document's contents
3. The summary is injected into the routing prompt so the correct agent(s) are selected
4. The full content (or summary for images) is passed to each selected agent

## Adding a KernelFunction Plugin

1. Create a class in `Infrastructure/SemanticKernel/Plugins/`:

```csharp
public sealed class MyPlugin
{
    [KernelFunction("function_name")]
    [Description("What this function does")]
    public string MyFunction([Description("param description")] string input)
        => result;
}
```

2. Register in `Program.cs`:

```csharp
builder.Services.AddSingleton(
    KernelPluginFactory.CreateFromObject(new MyPlugin(), "MyPlugin"));
```

## Adding OpenTelemetry Tracing

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(b => b
        .AddSource(AgentChatActivitySource.SourceName)
        .AddJaegerExporter()); // or any OTel exporter
```

Spans are emitted for: `orchestration`, `routing`, `agent_run`, and `synthesis`.

## Project Structure

```
SemanticKernel/
├── Abstractions/          # Interfaces + shared data models
├── Components/            # Blazor UI (Chat.razor, DebugAgents.razor)
├── Configuration/         # Strongly-typed options classes
├── Data/                  # Agent definition files (no compiled code)
│   ├── Agents/            # PDF agents
│   ├── DatabaseAgents/    # DB agent configs
│   ├── RestAgents/        # REST agent configs
│   └── TechnicalDrawingAgents/  # Drawing agent configs
├── Infrastructure/        # Core business logic and runners
│   ├── Attachments/       # File processing and summarization
│   ├── Database/          # DB runner and loader
│   ├── Rest/              # REST runner and loader
│   ├── SemanticKernel/    # Kernel, router, registry, plugins
│   ├── TechnicalDrawing/  # Drawing agent runner and loader
│   └── Telemetry/         # OpenTelemetry activity source
├── Models/                # UI-facing models
├── Services/              # PDF text extraction, agent loading
├── Program.cs             # App startup and DI registrations
└── appsettings.json       # Configuration template
```

See [CLAUDE.md](CLAUDE.md) for a comprehensive developer guide covering architecture, conventions, and extension points.

## Technology Stack

| Package | Purpose |
|---------|---------|
| `Microsoft.SemanticKernel` | LLM orchestration and function calling |
| `Microsoft.SemanticKernel.Connectors.OpenAI` | OpenAI-compatible API connector |
| `Markdig` | Markdown rendering in the chat UI |
| `PdfPig` | PDF text extraction |
