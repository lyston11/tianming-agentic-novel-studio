using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Support;

namespace TM.Web.NovelAgentWeb.Services.Production;

public interface IOutputArtifactRecorder
{
    Task<OutputArtifactRecordResult> RecordAsync(
        OutputArtifactRecordRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record OutputArtifactRecordRequest(
    string RuntimeRunId,
    string UserId,
    string ProjectId,
    string? ChapterId,
    string? PackageId,
    string ToolName,
    string Stage,
    string Status,
    string ArtifactType,
    string ArtifactId,
    string OutputKind,
    string Summary,
    IReadOnlyList<string> UserVisibleWhere,
    bool VisibleInWorkflow,
    bool VisibleInLibrary,
    string? SourceEventType = null,
    string? SourceEventId = null,
    object? Data = null);

public sealed record OutputArtifactRecordResult(
    ProductionEvent Event,
    AgentToolProducedArtifact Artifact);
