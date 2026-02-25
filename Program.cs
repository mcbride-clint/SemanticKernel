using BlazorAgentChat.Abstractions;
using BlazorAgentChat.Configuration;
using BlazorAgentChat.Infrastructure.SemanticKernel;
using BlazorAgentChat.Services;

var builder = WebApplication.CreateBuilder(args);

// ── Configuration ────────────────────────────────────────────────────────────
builder.Services
    .Configure<OpenAiOptions>   (builder.Configuration.GetSection(OpenAiOptions.SectionName))
    .Configure<AgentChatOptions>(builder.Configuration.GetSection(AgentChatOptions.SectionName));

// ── Infrastructure ───────────────────────────────────────────────────────────
builder.Services.AddSingleton<KernelFactory>();

// ── Services ─────────────────────────────────────────────────────────────────
builder.Services.AddSingleton<PdfTextExtractor>();
builder.Services.AddSingleton<AgentLoader>();

// ── Abstractions bound to SK implementations ─────────────────────────────────
builder.Services.AddSingleton<IAgentRegistry,  SkAgentRegistry>();
builder.Services.AddSingleton<IAgentRouter,    SkAgentRouter>();
builder.Services.AddSingleton<IAgentRunner,    SkAgentRunner>();
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
