using Microsoft.Extensions.Hosting;
using TM.Services.Framework.AI.Embedding;

namespace TM.Web.NovelAgentWeb.Services.Embedding;

public static class EmbeddingServiceCollectionExtensions
{
    private const string StubProvider = "stub";
    private const string DefaultStubModel = "stub-hash-v1";
    private const int StubDimension = 512;

    public static IServiceCollection AddNovelAgentEmbedding(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var status = CreateRuntimeStatus(configuration, environment);
        ValidateRuntimeStatus(status);

        services.AddSingleton(status);
        services.AddSingleton<IMicroEmbeddingService, StubEmbeddingService>();
        services.AddHostedService<EmbeddingModeReporter>();
        return services;
    }

    public static EmbeddingRuntimeStatus CreateRuntimeStatus(
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var provider = configuration["Embedding:Provider"];
        if (string.IsNullOrWhiteSpace(provider))
            provider = StubProvider;

        provider = provider.Trim().ToLowerInvariant();
        var requireRealEmbeddings = configuration.GetValue("Embedding:RequireRealEmbeddings", false);
        var model = configuration["Embedding:Model"];

        if (provider == StubProvider)
        {
            return new EmbeddingRuntimeStatus
            {
                Provider = StubProvider,
                Model = string.IsNullOrWhiteSpace(model) ? DefaultStubModel : model.Trim(),
                Dimension = StubDimension,
                SemanticQuality = "degraded",
                IsDegraded = true,
                UsesDeterministicStub = true,
                RealEmbeddingsRequired = requireRealEmbeddings,
                Warning = "Semantic RAG is using deterministic hash vectors for local/test compatibility. Similarity scores are not model-quality embeddings."
            };
        }

        return new EmbeddingRuntimeStatus
        {
            Provider = provider,
            Model = string.IsNullOrWhiteSpace(model) ? "unconfigured" : model.Trim(),
            Dimension = configuration.GetValue("Embedding:Dimension", 0),
            SemanticQuality = "unavailable",
            IsDegraded = true,
            UsesDeterministicStub = false,
            RealEmbeddingsRequired = requireRealEmbeddings || environment.IsProduction(),
            Warning = $"Embedding provider '{provider}' is configured but no provider implementation is registered in this build."
        };
    }

    private static void ValidateRuntimeStatus(EmbeddingRuntimeStatus status)
    {
        if (status.Provider == StubProvider && status.RealEmbeddingsRequired)
        {
            throw new InvalidOperationException(
                "Embedding:RequireRealEmbeddings=true cannot be used with Embedding:Provider=stub. Configure a real embedding provider before enabling production semantic RAG.");
        }

        if (status.Provider != StubProvider)
        {
            throw new InvalidOperationException(
                $"Embedding provider '{status.Provider}' is not available in this build. Use Embedding:Provider=stub for degraded local/test mode or add a real provider implementation.");
        }
    }
}
