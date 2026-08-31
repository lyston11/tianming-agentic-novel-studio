using TM.Web.NovelAgentWeb.Support;

namespace TM.Web.NovelAgentWeb.Services.Production;

public sealed class OutputArtifactRecorder : IOutputArtifactRecorder
{
    public const string EventType = "tool_output_artifact_recorded";

    private readonly IProductionEventWriter _events;

    public OutputArtifactRecorder(IProductionEventWriter events)
    {
        _events = events;
    }

    public async Task<OutputArtifactRecordResult> RecordAsync(
        OutputArtifactRecordRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.ArtifactType) || string.IsNullOrWhiteSpace(request.ArtifactId))
            throw new InvalidOperationException("Output artifact requires artifactType and artifactId.");

        var userVisibleWhere = request.UserVisibleWhere
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var evt = await _events.AppendChapterStageAsync(
                new AppendChapterProductionEventRequest(
                    RuntimeRunId: request.RuntimeRunId,
                    UserId: request.UserId,
                    ProjectId: request.ProjectId,
                    ChapterId: request.ChapterId,
                    PackageId: request.PackageId,
                    EventType: EventType,
                    Stage: request.Stage,
                    Status: request.Status,
                    Message: request.Summary,
                    ArtifactType: request.ArtifactType,
                    ArtifactId: request.ArtifactId,
                    Data: new
                    {
                        toolName = request.ToolName,
                        outputKind = request.OutputKind,
                        visibleInWorkflow = request.VisibleInWorkflow,
                        visibleInLibrary = request.VisibleInLibrary,
                        userVisibleWhere,
                        sourceEventType = request.SourceEventType ?? string.Empty,
                        sourceEventId = request.SourceEventId ?? string.Empty,
                        data = request.Data
                    }),
                cancellationToken)
            .ConfigureAwait(false);

        return new OutputArtifactRecordResult(
            evt,
            new AgentToolProducedArtifact
            {
                ArtifactType = request.ArtifactType,
                ArtifactId = request.ArtifactId,
                OutputKind = request.OutputKind,
                UserVisibleWhere = userVisibleWhere,
                Summary = request.Summary
            });
    }
}
