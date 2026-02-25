using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using BlazorAgentChat.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;

namespace BlazorAgentChat.Infrastructure.SemanticKernel;

public sealed class KernelFactory
{
    private readonly OpenAiOptions         _opts;
    private readonly ILoggerFactory        _loggerFactory;
    private readonly ILogger<KernelFactory> _log;

    public KernelFactory(
        IOptions<OpenAiOptions> opts,
        ILoggerFactory          loggerFactory)
    {
        _opts          = opts.Value;
        _loggerFactory = loggerFactory;
        _log           = loggerFactory.CreateLogger<KernelFactory>();
    }

    public Kernel Create()
    {
        _log.LogInformation(
            "Building Kernel. Endpoint={Endpoint} ModelId={ModelId}",
            _opts.Endpoint, _opts.ModelId);

        var httpClient = BuildHttpClient();
        var builder    = Kernel.CreateBuilder();

        // Re-use the host's ILoggerFactory so SK logs appear in the same stream
        builder.Services.AddSingleton(_loggerFactory);

        builder.AddOpenAIChatCompletion(
            modelId:    _opts.ModelId,
            apiKey:     _opts.ApiKey,
            httpClient: httpClient);

        var kernel = builder.Build();
        _log.LogDebug("Kernel built successfully.");
        return kernel;
    }

    private HttpClient BuildHttpClient()
    {
        if (string.IsNullOrWhiteSpace(_opts.CaCertPath))
        {
            _log.LogWarning("CaCertPath is empty — using default TLS validation.");
            return new HttpClient { BaseAddress = new Uri(_opts.Endpoint) };
        }

        _log.LogDebug("Loading custom CA bundle from {CaCertPath}", _opts.CaCertPath);

        var caCert  = new X509Certificate2(_opts.CaCertPath);
        var handler = new HttpClientHandler();

        handler.ServerCertificateCustomValidationCallback = (_, cert, _, _) =>
        {
            using var chain = new X509Chain();
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.Add(caCert);
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            bool valid = chain.Build(cert!);
            if (!valid)
                _log.LogWarning(
                    "TLS chain validation failed for cert subject={Subject}", cert?.Subject);
            return valid;
        };

        _log.LogInformation("Custom CA cert loaded. Subject={Subject}", caCert.Subject);
        return new HttpClient(handler) { BaseAddress = new Uri(_opts.Endpoint) };
    }
}
