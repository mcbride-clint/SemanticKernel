using BlazorAgentChat.Abstractions;
using BlazorAgentChat.Configuration;
using BlazorAgentChat.Infrastructure;
using BlazorAgentChat.Infrastructure.Attachments;
using BlazorAgentChat.Infrastructure.Database;
using BlazorAgentChat.Infrastructure.Rest;
using BlazorAgentChat.Infrastructure.SemanticKernel;
using BlazorAgentChat.Infrastructure.SemanticKernel.Plugins;
using BlazorAgentChat.Infrastructure.TechnicalDrawing;
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

// ── Attachment processing ─────────────────────────────────────────────────────
builder.Services.AddSingleton<DocumentSummaryService>();
builder.Services.AddSingleton<IAttachmentProcessor, AttachmentProcessor>();

// ── Agent Sources (IAgentSource) — register more here to extend the registry ─
// Every IAgentSource is merged into the agent registry automatically.
builder.Services.AddSingleton<IAgentSource, AgentLoader>();                    // PDF   (Data/Agents/)
builder.Services.AddSingleton<IAgentSource, DbAgentLoader>();                  // DB    (Data/DatabaseAgents/agents.json)
builder.Services.AddSingleton<IAgentSource, RestAgentLoader>();                // REST  (Data/RestAgents/agents.json)
builder.Services.AddSingleton<IAgentSource, TechnicalDrawingAgentLoader>();    // Drawing (Data/TechnicalDrawingAgents/agents.json)

// Keep concrete types resolvable so runners can depend on them directly
builder.Services.AddSingleton<AgentLoader>();
builder.Services.AddSingleton<DbAgentLoader>();
builder.Services.AddSingleton<RestAgentLoader>();
builder.Services.AddSingleton<TechnicalDrawingAgentLoader>();
builder.Services.AddSingleton<PdfTextExtractor>();

// ── Database connectivity ─────────────────────────────────────────────────────
// Replace NoopDbConnectionFactory with your provider's implementation:
//   builder.Services.AddSingleton<IDbConnectionFactory, SqlServerConnectionFactory>();
// See NoopDbConnectionFactory.cs for a full example.
builder.Services.AddSingleton<IDbConnectionFactory, NoopDbConnectionFactory>();

// ── Runners ───────────────────────────────────────────────────────────────────
builder.Services.AddSingleton<SkAgentRunner>();               // PDF      runner
builder.Services.AddSingleton<DbAgentRunner>();               // DB       runner
builder.Services.AddSingleton<RestAgentRunner>();             // REST     runner
builder.Services.AddSingleton<TechnicalDrawingAgentRunner>(); // Drawing  runner
builder.Services.AddSingleton<IAgentRunner, CompositeAgentRunner>(); // dispatches by SourceType

// ── Abstractions bound to SK implementations ─────────────────────────────────
builder.Services.AddSingleton<IAgentRegistry, SkAgentRegistry>();
builder.Services.AddSingleton<IAgentRouter,   SkAgentRouter>();
// Scoped so each Blazor circuit gets its own LastMetadata
builder.Services.AddScoped<IOrchestrationService, SkOrchestrationService>();

// ── Razor Pages + MVC Controllers ────────────────────────────────────────────
builder.Services.AddRazorPages();
builder.Services.AddControllers();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.MapRazorPages();
app.MapControllers();

app.Run();
