using BlazorAgentChat.Abstractions;
using BlazorAgentChat.Configuration;
using BlazorAgentChat.Infrastructure;
using BlazorAgentChat.Infrastructure.Database;
using BlazorAgentChat.Infrastructure.SemanticKernel;
using BlazorAgentChat.Services;

var builder = WebApplication.CreateBuilder(args);

// ── Configuration ────────────────────────────────────────────────────────────
builder.Services
    .Configure<OpenAiOptions>   (builder.Configuration.GetSection(OpenAiOptions.SectionName))
    .Configure<AgentChatOptions>(builder.Configuration.GetSection(AgentChatOptions.SectionName));

// ── Infrastructure ───────────────────────────────────────────────────────────
builder.Services.AddSingleton<KernelFactory>();

// ── Agent Sources (IAgentSource) — add more here to extend the registry ──────
// Each registered IAgentSource is automatically merged into the agent registry.
builder.Services.AddSingleton<IAgentSource, AgentLoader>();     // PDF agents (Data/Agents/)
builder.Services.AddSingleton<IAgentSource, DbAgentLoader>();   // DB agents  (Data/DatabaseAgents/agents.json)

// Keep concrete types accessible for runners that depend on them directly
builder.Services.AddSingleton<AgentLoader>();
builder.Services.AddSingleton<DbAgentLoader>();
builder.Services.AddSingleton<PdfTextExtractor>();

// ── Database connectivity ─────────────────────────────────────────────────────
// NoopDbConnectionFactory throws at runtime if a DB agent is invoked without
// a real connection factory. Replace with your provider's implementation:
//
//   builder.Services.AddSingleton<IDbConnectionFactory, SqlServerConnectionFactory>();
//
// See NoopDbConnectionFactory.cs for a complete example.
builder.Services.AddSingleton<IDbConnectionFactory, NoopDbConnectionFactory>();

// ── Runners ───────────────────────────────────────────────────────────────────
builder.Services.AddSingleton<SkAgentRunner>();     // PDF runner (concrete, used by CompositeAgentRunner)
builder.Services.AddSingleton<DbAgentRunner>();     // DB runner  (concrete, used by CompositeAgentRunner)
builder.Services.AddSingleton<IAgentRunner, CompositeAgentRunner>(); // dispatches by SourceType

// ── Abstractions bound to SK implementations ─────────────────────────────────
builder.Services.AddSingleton<IAgentRegistry,  SkAgentRegistry>();
builder.Services.AddSingleton<IAgentRouter,    SkAgentRouter>();
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
