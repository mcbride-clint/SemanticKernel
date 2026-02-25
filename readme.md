# BlazorAgentChat

A Blazor Server application that routes user questions to expert AI agents backed by PDFs, databases, or REST APIs. Built on [Semantic Kernel](https://github.com/microsoft/semantic-kernel).

## Quick Start

1. Copy `appsettings.json` and fill in your OpenAI (or compatible) endpoint:

```json
{
  "OpenAI": {
    "Endpoint": "https://your-endpoint/v1",
    "ApiKey":   "your-api-key",
    "ModelId":  "gpt-4o",
    "CaCertPath": ""
  }
}
```

2. Build and run:

```bash
dotnet build BlazorAgentChat.csproj
dotnet run --project BlazorAgentChat.csproj
```

3. Open `http://localhost:5000` to chat, or `/debug` to inspect loaded agents.

---

## Agent Types

### PDF Agent

The simplest agent type — pair a PDF document with a short JSON descriptor. The full document text is embedded in the LLM's context for each question.

**File layout:**

```
Data/Agents/
└── <FolderName>/
    ├── agent.json
    └── document.pdf
```

The folder name becomes the agent ID (lowercase, spaces → hyphens).

**`agent.json` schema:**

```json
{
  "Name": "Human-readable agent name",
  "Description": "What this agent knows. Used by the router to decide when to call it."
}
```

**Example — `Data/Agents/HrHandbook/agent.json`:**

```json
{
  "Name": "HR Handbook Expert",
  "Description": "Expert in the company HR handbook. Covers leave policies, benefits, onboarding, and code of conduct."
}
```

Drop any PDF as `document.pdf` in the same folder. The app loads it automatically on startup — no code changes required.

---

### Database Agent

Database agents run a parameterized SQL query and let the LLM interpret the results. Parameters are extracted from the user's natural-language question by the LLM before the query runs.

**Config file:** `Data/DatabaseAgents/agents.json`

Add a new object to the JSON array:

```json
{
  "Id":               "unique-agent-id",
  "Name":             "Human-readable name",
  "Description":      "What data this agent has. Used by the router.",
  "ConnectionString": "Server=...;Database=...;",
  "ContextQuery":     "SELECT ... WHERE (@Param IS NULL OR col = @Param)",
  "MaxRows":          200,
  "Parameters": [
    {
      "Name":        "Param",
      "Description": "What this parameter filters. Shown to the LLM for extraction.",
      "Required":    false
    }
  ]
}
```

**Query tips:**
- Use `@ParamName` placeholders — they are injected as `DbParameter` objects (no SQL injection risk).
- Pattern `(@Param IS NULL OR col = @Param)` makes a parameter optional: the LLM sets it to `null` when the user doesn't specify a value.
- `MaxRows` caps how many rows are sent to the LLM to avoid context overflow.

**Database connectivity:**

Replace the placeholder `NoopDbConnectionFactory` with a real implementation:

```csharp
// Program.cs — swap this line:
builder.Services.AddSingleton<IDbConnectionFactory, SqlServerConnectionFactory>();
```

Implement `IDbConnectionFactory` (see `Infrastructure/Database/NoopDbConnectionFactory.cs` for the contract).

**Example:**

```json
{
  "Id":               "sales-report",
  "Name":             "Sales Report",
  "Description":      "Daily sales data by region and product. Use for revenue, volume, or trend questions.",
  "ConnectionString": "Server=db;Database=Sales;Trusted_Connection=true;",
  "ContextQuery":     "SELECT Region, Product, Amount, SaleDate FROM dbo.Sales WHERE SaleDate >= DATEADD(day, -30, GETDATE()) AND (@Region IS NULL OR Region = @Region) ORDER BY SaleDate DESC",
  "MaxRows":          300,
  "Parameters": [
    {
      "Name":        "Region",
      "Description": "Sales region to filter by (e.g. 'North', 'West'). Null returns all regions.",
      "Required":    false
    }
  ]
}
```

---

### REST Agent

REST agents call an HTTP endpoint and let the LLM interpret the JSON response. Path and query parameters are extracted from the user's question, same as DB agents.

**Config file:** `Data/RestAgents/agents.json`

Add a new object to the JSON array:

```json
{
  "Id":               "unique-agent-id",
  "Name":             "Human-readable name",
  "Description":      "What data this API exposes. Used by the router.",
  "UrlTemplate":      "https://api.example.com/resource/{PathParam}",
  "Method":           "GET",
  "MaxResponseChars": 6000,
  "Headers": {
    "Authorization": "Bearer your-static-token"
  },
  "Parameters": [
    {
      "Name":        "PathParam",
      "Description": "What this param means. Shown to the LLM for extraction.",
      "Required":    true,
      "Location":    "path"
    },
    {
      "Name":        "FilterParam",
      "Description": "An optional query filter.",
      "Required":    false,
      "Location":    "query",
      "QueryKey":    "filter"
    }
  ]
}
```

**Parameter locations:**
- `"path"` — replaces `{ParamName}` in `UrlTemplate`.
- `"query"` — appended as `?QueryKey=value` to the URL.

**`MaxResponseChars`** — truncates the raw API response before it's sent to the LLM. Prevents context overflow for large APIs.

**`Headers`** — static key/value pairs sent with every request (e.g. auth tokens). Do not put secrets in JSON committed to source control; use environment variable substitution or Secret Manager instead.

**Example — weather API:**

```json
{
  "Id":               "current-weather",
  "Name":             "Current Weather",
  "Description":      "Live weather data for any city. Use for temperature, conditions, or forecast questions.",
  "UrlTemplate":      "https://api.openweathermap.org/data/2.5/weather",
  "Method":           "GET",
  "MaxResponseChars": 3000,
  "Parameters": [
    {
      "Name":        "City",
      "Description": "City name to get weather for (e.g. 'London', 'New York').",
      "Required":    true,
      "Location":    "query",
      "QueryKey":    "q"
    }
  ]
}
```

---

## Plugins (KernelFunctions)

Plugins give the LLM the ability to call C# functions during a conversation — for real-time data, calculations, or any side-effect-free utility. They use Semantic Kernel's native function-calling mechanism.

Plugins are available to **all agent runners and the synthesizer**. The LLM decides when (and whether) to call them based on the user's question.

### Creating a Plugin

1. Create a class in `Infrastructure/SemanticKernel/Plugins/`:

```csharp
using System.ComponentModel;
using Microsoft.SemanticKernel;

namespace BlazorAgentChat.Infrastructure.SemanticKernel.Plugins;

public sealed class MyPlugin
{
    [KernelFunction("function_name")]
    [Description("One-sentence description shown to the LLM so it knows when to call this.")]
    public string MyFunction(string input)
    {
        // ... your logic
        return result;
    }
}
```

**Attribute rules:**
- `[KernelFunction("name")]` — the function name the LLM sees. Use `snake_case`.
- `[Description("...")]` on the method — tells the LLM what the function does.
- `[Description("...")]` on each parameter — tells the LLM what value to pass.
- Return type can be `string`, `int`, `Task<string>`, etc. SK serialises it automatically.

2. Register it in `Program.cs`:

```csharp
// Add alongside the existing plugin registrations:
builder.Services.AddSingleton(KernelPluginFactory.CreateFromObject(new MyPlugin(), "MyPlugin"));
```

That's all — the plugin is automatically added to every `Kernel` instance via `KernelFactory`.

### Built-in Example: `DateTimePlugin`

Located at `Infrastructure/SemanticKernel/Plugins/DateTimePlugin.cs`:

| Function | Returns |
|---|---|
| `get_current_date` | Today's date as `yyyy-MM-dd` |
| `get_current_time` | Current local time as `HH:mm:ss` |

**Usage:** Ask the chat "What's today's date?" and the LLM will automatically call `get_current_date` before answering.

### Plugin DI and Scope

- Plugins are registered as `KernelPlugin` **singletons** in the DI container.
- `KernelFactory.Create()` resolves all registered `KernelPlugin` instances and adds them to each new `Kernel`.
- If a plugin needs DI services, resolve them via the constructor and capture in a closure, or implement an intermediary class that accepts them.

**Example with a DI dependency:**

```csharp
public sealed class StockPlugin
{
    private readonly IStockService _stocks;

    public StockPlugin(IStockService stocks) => _stocks = stocks;

    [KernelFunction("get_stock_price")]
    [Description("Returns the current price for a stock ticker symbol (e.g. MSFT, AAPL).")]
    public async Task<string> GetStockPriceAsync(
        [Description("The ticker symbol")] string ticker)
    {
        var price = await _stocks.GetPriceAsync(ticker);
        return $"{ticker}: ${price:F2}";
    }
}
```

Register in `Program.cs`:

```csharp
builder.Services.AddSingleton<IStockService, YourStockService>();
builder.Services.AddSingleton(sp =>
    KernelPluginFactory.CreateFromObject(
        new StockPlugin(sp.GetRequiredService<IStockService>()), "Stocks"));
```

---

## Architecture Overview

```
User question
    │
    ▼
SkAgentRouter.SelectAgentsAsync()   ← LLM picks relevant agents by ID
    │
    ▼  (parallel)
CompositeAgentRunner.RunAsync()     ← dispatches by AgentInfo.SourceType
    ├── "pdf"      → SkAgentRunner      (document embedded in prompt)
    ├── "database" → DbAgentRunner      (SQL query → LLM interpretation)
    └── "rest"     → RestAgentRunner    (HTTP call → LLM interpretation)
    │
    ▼
SkAgentRouter.SynthesizeAsync()     ← streaming final answer
    │
    ▼
Chat.razor (streamed UI)
```

**KernelFunctions** are available during `SkAgentRunner` and `SynthesizeAsync` — both use `FunctionChoiceBehavior.Auto()`. The routing call intentionally has no tools enabled so it always returns clean JSON.

### Adding a New Agent Type

1. Implement `IAgentSource` to load configs and produce `AgentInfo` with a unique `SourceType`.
2. Implement `IAgentRunner` to handle that `SourceType`.
3. Register both in `Program.cs`.
4. Update `CompositeAgentRunner` to dispatch to the new runner.

---

## Configuration Reference

| Key | Description |
|---|---|
| `OpenAI:Endpoint` | Base URL of an OpenAI-compatible API |
| `OpenAI:ApiKey` | API key |
| `OpenAI:ModelId` | Model name (e.g. `gpt-4o`) |
| `OpenAI:CaCertPath` | Optional path to a custom CA bundle `.pem` for self-signed TLS |
| `AgentChat:AgentsDirectory` | Directory scanned for PDF agents (default: `Data/Agents`) |
