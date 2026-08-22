namespace TM.Web.NovelAgentWeb.Services.Embedding;

public sealed class EmbeddingRuntimeStatus
{
    public string Provider { get; init; } = "bge-small-zh";
    public string Model { get; init; } = "bge-small-zh-v1.5";
    public int Dimension { get; init; } = 512;
    public string SemanticQuality { get; init; } = "model";
    public bool IsDegraded { get; init; }
    public bool RealEmbeddingsRequired { get; init; } = true;
    public string Warning { get; init; } = string.Empty;
}
