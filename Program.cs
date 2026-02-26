using BlazorAgentChat.Abstractions;
using BlazorAgentChat.Configuration;
using BlazorAgentChat.Infrastructure;
using BlazorAgentChat.Infrastructure.Database;
using BlazorAgentChat.Infrastructure.Rest;
using BlazorAgentChat.Infrastructure.SemanticKernel;
using BlazorAgentChat.Infrastructure.SemanticKernel.Plugins;
using BlazorAgentChat.Infrastructure.Telemetry;
using BlazorAgentChat.Services;
using Microsoft.SemanticKernel;

var builder = WebApplication.CreateBuilder(args);

// ── Configuration ────────────────────────────────────────────────────────────
builder.Services
    .Configure<OpenAiOptions>   (builder.Configuration.GetSection(OpenAiOptions.SectionName))
    .Configure<AgentChatOptions>(builder.Configuration.GetSection(AgentChatOptions.SectionName));

// ── Infrastructure ───────────────────────────────────────────────────────────
builder.Services.AddSingleton<KernelFactory>();
builder.Services.AddSingleton<ParameterExtractor>();     // shared by DB + REST runners
builder.Services.AddSingleton<AgentChatActivitySource>(); // structured tracing spans
builder.Services.AddHttpClient();                        // IHttpClientFactory for REST runner

// ── Kernel Plugins (KernelFunction) — register more here to extend LLM capabilities ──────────
// Every KernelPlugin registered here is added to every Kernel created by KernelFactory.
builder.Services.AddSingleton(KernelPluginFactory.CreateFromObject(new DateTimePlugin(), "DateTime"));

// ── Agent Sources (IAgentSource) — register more here to extend the registry ─
// Every IAgentSource is merged into the agent registry automatically.
builder.Services.AddSingleton<IAgentSource, AgentLoader>();      // PDF   (Data/Agents/)
builder.Services.AddSingleton<IAgentSource, DbAgentLoader>();    // DB    (Data/DatabaseAgents/agents.json)
builder.Services.AddSingleton<IAgentSource, RestAgentLoader>();  // REST  (Data/RestAgents/agents.json)

// Keep concrete types resolvable so runners can depend on them directly
builder.Services.AddSingleton<AgentLoader>();
builder.Services.AddSingleton<DbAgentLoader>();
builder.Services.AddSingleton<RestAgentLoader>();
builder.Services.AddSingleton<PdfTextExtractor>();

// ── Database connectivity ─────────────────────────────────────────────────────
// Replace NoopDbConnectionFactory with your provider's implementation:
//   builder.Services.AddSingleton<IDbConnectionFactory, SqlServerConnectionFactory>();
// See NoopDbConnectionFactory.cs for a full example.
builder.Services.AddSingleton<IDbConnectionFactory, NoopDbConnectionFactory>();

// ── Runners ───────────────────────────────────────────────────────────────────
builder.Services.AddSingleton<SkAgentRunner>();     // PDF  runner (concrete, used by CompositeAgentRunner)
builder.Services.AddSingleton<DbAgentRunner>();     // DB   runner (concrete, used by CompositeAgentRunner)
builder.Services.AddSingleton<RestAgentRunner>();   // REST runner (concrete, used by CompositeAgentRunner)
builder.Services.AddSingleton<IAgentRunner, CompositeAgentRunner>(); // dispatches by SourceType

// ── Abstractions bound to SK implementations ─────────────────────────────────
builder.Services.AddSingleton<IAgentRegistry, SkAgentRegistry>();
builder.Services.AddSingleton<IAgentRouter,   SkAgentRouter>();
// Scoped so each Blazor circuit gets its own LastMetadata
builder.Services.AddScoped<IOrchestrationService, SkOrchestrationService>();

// ── Blazor ───────────────────────────────────────────────────────────────────
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<BlazorAgentChat.Components.App>()
    .AddInteractiveServerRenderMode();

app.Run();
