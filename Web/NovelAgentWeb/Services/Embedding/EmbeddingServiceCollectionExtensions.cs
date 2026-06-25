using Microsoft.Extensions.Hosting;
using TM.Services.Framework.AI.Embedding;

namespace TM.Web.NovelAgentWeb.Services.Embedding;

public static class EmbeddingServiceCollectionExtensions
{
    private const string BgeProvider = "bge-small-zh";
    private const string DefaultBgeModel = "bge-small-zh-v1.5";
    private const int BgeDimension = 512;

    public static IServiceCollection AddNovelAgentEmbedding(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var status = CreateRuntimeStatus(configuration, environment);
        ValidateRuntimeStatus(status);

        services.AddSingleton(status);
        services.AddSingleton<IMicroEmbeddingService, BgeSmallZhEmbeddingService>();
        services.AddHostedService<EmbeddingModeReporter>();
        return services;
    }

    public static EmbeddingRuntimeStatus CreateRuntimeStatus(
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var provider = configuration["Embedding:Provider"];
        if (string.IsNullOrWhiteSpace(provider))
            provider = BgeProvider;

        provider = NormalizeProvider(provider);
        var requireRealEmbeddings = true;
        var model = configuration["Embedding:Model"];

        if (provider == BgeProvider)
        {
            return new EmbeddingRuntimeStatus
            {
                Provider = BgeProvider,
                Model = string.IsNullOrWhiteSpace(model) ? DefaultBgeModel : model.Trim(),
                Dimension = BgeDimension,
                SemanticQuality = "model",
                IsDegraded = false,
                RealEmbeddingsRequired = requireRealEmbeddings,
                Warning = string.Empty
            };
        }

        return new EmbeddingRuntimeStatus
        {
            Provider = provider,
            Model = string.IsNullOrWhiteSpace(model) ? "unconfigured" : model.Trim(),
            Dimension = configuration.GetValue("Embedding:Dimension", 0),
            SemanticQuality = "unavailable",
            IsDegraded = true,
            RealEmbeddingsRequired = requireRealEmbeddings,
            Warning = $"Embedding provider '{provider}' is configured but no provider implementation is registered in this build."
        };
    }

    private static void ValidateRuntimeStatus(EmbeddingRuntimeStatus status)
    {
        if (status.Provider == "stub")
        {
            throw new InvalidOperationException(
                "Embedding provider 'stub' is not supported: deterministic stub embeddings are not available in the production runtime.");
        }

        if (status.Provider != BgeProvider)
        {
            throw new InvalidOperationException(
                $"Embedding provider '{status.Provider}' is not available in this build. Configure Embedding:Provider={BgeProvider}.");
        }

        using var probe = new BgeSmallZhEmbeddingService();
        if (!probe.IsModelReady())
        {
            throw new InvalidOperationException(
                "BGE embedding model files are missing from the runtime output. Ensure bge-small-zh-v1.5 model.onnx and vocab.txt are copied to the application output.");
        }
    }

    private static string NormalizeProvider(string provider)
    {
        var normalized = provider.Trim().ToLowerInvariant();
        return normalized is "bge-small-zh-v1.5" or "bge" ? BgeProvider : normalized;
    }
}
