using System.Text.Json.Serialization;
using TM.Services.Framework.AI.NovelAgent.Models;

namespace TM.Web.NovelAgentWeb.Services.Production;

public interface IChapterContextEnrichmentService
{
    Task<ChapterContextEnrichmentResult> BuildAsync(
        ChapterContextEnrichmentRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record ChapterContextEnrichmentRequest(
    string UserId,
    string ProjectId,
    string SessionId,
    string RunId,
    string Query,
    ChapterContextPackageSummary Package);

public sealed class ChapterContextEnrichmentResult
{
    [JsonPropertyName("knowledgeBindings")]
    public List<BoundKnowledgeSnapshot> KnowledgeBindings { get; set; } = new();

    [JsonPropertyName("hardFacts")]
    public List<string> HardFacts { get; set; } = new();

    [JsonPropertyName("previousSummaries")]
    public List<string> PreviousSummaries { get; set; } = new();

    [JsonPropertyName("characterStates")]
    public List<string> CharacterStates { get; set; } = new();

    [JsonPropertyName("activeConflicts")]
    public List<string> ActiveConflicts { get; set; } = new();

    [JsonPropertyName("sourceWarnings")]
    public List<string> SourceWarnings { get; set; } = new();
}
