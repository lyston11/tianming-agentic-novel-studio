namespace TM.Web.NovelAgentWeb.Services.Embedding;

public sealed class EmbeddingHealthResponse
{
    public string Provider { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public int Dimension { get; init; }
    public string SemanticQuality { get; init; } = string.Empty;
    public bool Degraded { get; init; }
    public bool DeterministicStub { get; init; }
    public bool RealEmbeddingsRequired { get; init; }
    public string Warning { get; init; } = string.Empty;

    public static EmbeddingHealthResponse From(EmbeddingRuntimeStatus status)
    {
        return new EmbeddingHealthResponse
        {
            Provider = status.Provider,
            Model = status.Model,
            Dimension = status.Dimension,
            SemanticQuality = status.SemanticQuality,
            Degraded = status.IsDegraded,
            DeterministicStub = status.UsesDeterministicStub,
            RealEmbeddingsRequired = status.RealEmbeddingsRequired,
            Warning = status.Warning
        };
    }
}
