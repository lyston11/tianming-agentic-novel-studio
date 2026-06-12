namespace TM.Web.NovelAgentWeb.Services.Embedding;

public sealed class EmbeddingRuntimeStatus
{
    public string Provider { get; init; } = "stub";
    public string Model { get; init; } = "stub-hash-v1";
    public int Dimension { get; init; } = 512;
    public string SemanticQuality { get; init; } = "degraded";
    public bool IsDegraded { get; init; } = true;
    public bool UsesDeterministicStub { get; init; } = true;
    public bool RealEmbeddingsRequired { get; init; }
    public string Warning { get; init; } = "Semantic RAG is running with deterministic hash vectors, not model embeddings.";
}
