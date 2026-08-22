using TM.Web.NovelAgentWeb.Data.Entities;

namespace TM.Web.NovelAgentWeb.Services.Knowledge;

public sealed record KnowledgeSectionDraft(
    string Title,
    string Summary,
    int CharStart,
    int CharEnd);

public sealed record AbstractStyleProfileDraft(
    string NarrativeDistance,
    string SentenceRhythm,
    double DialogueDensity,
    double DescriptionRatio,
    IReadOnlyList<string> Imagery,
    string EmotionalIntensity,
    string InformationRelease);

public sealed record KnowledgeStructureAnalysis(
    string DocumentSummary,
    IReadOnlyList<KnowledgeSectionDraft> Sections,
    AbstractStyleProfileDraft StyleProfile,
    IReadOnlyList<int> HighImpactEntryIndexes);

public sealed record KnowledgeStructureRequest(
    string UserId,
    string ProjectId,
    string DocumentBlobId,
    long KnowledgeVersion,
    string Text,
    IReadOnlyList<KnowledgeBase> ExtractedEntries);

public interface IKnowledgeStructureModelClient
{
    Task<KnowledgeStructureAnalysis> AnalyzeAsync(
        KnowledgeStructureRequest request,
        CancellationToken cancellationToken = default);
}

public interface IKnowledgeDocumentIngestionService
{
    Task<KnowledgeDocumentBlob> StoreUploadAsync(
        string projectId,
        string fileName,
        string mimeType,
        byte[] data,
        CancellationToken cancellationToken = default);

    Task FinalizeProcessingAsync(
        string documentBlobId,
        string parsedText,
        IReadOnlyList<string> logicalKnowledgeIds,
        CancellationToken cancellationToken = default);
}
