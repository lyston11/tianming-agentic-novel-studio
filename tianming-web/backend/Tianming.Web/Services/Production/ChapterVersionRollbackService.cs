using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Services.Framework.AI.NovelAgent.Services.ProductionKernel;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;

namespace TM.Web.NovelAgentWeb.Services.Production;

public sealed class ChapterVersionRollbackService : IChapterVersionRollbackService
{
    private static readonly JsonSerializerOptions PayloadJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly NovelAgentDbContext _db;
    private readonly IProductionTruthStore _truthStore;
    private readonly IProductionEventWriter _events;
    private readonly IOutputArtifactRecorder? _outputArtifacts;

    public ChapterVersionRollbackService(
        NovelAgentDbContext db,
        IProductionTruthStore truthStore,
        IProductionEventWriter events,
        IOutputArtifactRecorder? outputArtifacts = null)
    {
        _db = db;
        _truthStore = truthStore;
        _events = events;
        _outputArtifacts = outputArtifacts;
    }

    public async Task<RollbackChapterVersionResult> RollbackAsync(
        RollbackChapterVersionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.UserId) ||
            string.IsNullOrWhiteSpace(request.ProjectId) ||
            string.IsNullOrWhiteSpace(request.ChapterId) ||
            string.IsNullOrWhiteSpace(request.TargetVersionId))
        {
            return new RollbackChapterVersionResult
            {
                Success = false,
                Message = "缺少 userId、projectId、chapterId 或 targetVersionId，无法回滚章节版本。"
            };
        }

        var chapter = await _db.Chapters
            .Include(item => item.Project)
            .FirstOrDefaultAsync(item =>
                    item.Id == request.ChapterId &&
                    item.ProjectId == request.ProjectId,
                cancellationToken)
            .ConfigureAwait(false);
        if (chapter == null)
        {
            return new RollbackChapterVersionResult
            {
                Success = false,
                Message = "未找到当前项目中的章节。"
            };
        }

        if (!string.Equals(chapter.Project.UserId, request.UserId, StringComparison.OrdinalIgnoreCase))
        {
            return new RollbackChapterVersionResult
            {
                Success = false,
                Message = "当前用户无权回滚该章节。"
            };
        }

        var targetVersion = await _db.ChapterVersions
            .FirstOrDefaultAsync(version =>
                    version.Id == request.TargetVersionId &&
                    version.UserId == request.UserId &&
                    version.ProjectId == request.ProjectId &&
                    version.ChapterId == request.ChapterId,
                cancellationToken)
            .ConfigureAwait(false);
        if (targetVersion == null)
        {
            return new RollbackChapterVersionResult
            {
                Success = false,
                Message = "未找到可回滚的目标章节版本。"
            };
        }

        var targetDocument = await _db.ContentDocuments
            .FirstOrDefaultAsync(document =>
                    document.Id == targetVersion.ContentDocumentId &&
                    document.UserId == request.UserId &&
                    document.ProjectId == request.ProjectId,
                cancellationToken)
            .ConfigureAwait(false);
        if (targetDocument == null)
        {
            return new RollbackChapterVersionResult
            {
                Success = false,
                Message = "目标章节版本缺少正文文档，无法回滚。"
            };
        }

        var idempotencyKey = Normalize(request.IdempotencyKey);
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var existingResult = await TryReadIdempotentRollbackResultAsync(
                    request,
                    targetVersion,
                    idempotencyKey,
                    cancellationToken)
                .ConfigureAwait(false);
            if (existingResult != null)
                return existingResult;
        }

        var now = DateTime.UtcNow;
        chapter.CurrentDocumentId = targetVersion.ContentDocumentId;
        chapter.Title = targetVersion.Title;
        chapter.WordCount = targetVersion.WordCount;
        chapter.Status = targetVersion.Status;
        chapter.UpdatedAt = now;

        var chapterDocuments = await _db.ContentDocuments
            .Where(document =>
                document.UserId == request.UserId &&
                document.ProjectId == request.ProjectId &&
                document.SourceType == "chapter" &&
                document.SourceId == request.ChapterId &&
                document.DocumentRole == "chapter_body")
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var document in chapterDocuments)
        {
            document.Status = string.Equals(document.Id, targetVersion.ContentDocumentId, StringComparison.OrdinalIgnoreCase)
                ? "active"
                : "archived";
            document.UpdatedAt = now;
        }

        var invalidatedPackages = await InvalidateAffectedPackagesAsync(
                request,
                chapter,
                targetVersion,
                now,
                cancellationToken)
            .ConfigureAwait(false);

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var committedContent = await ReadDocumentTextAsync(targetVersion.ContentDocumentId, cancellationToken)
            .ConfigureAwait(false);
        var runtimeRunId = FirstNonEmpty(request.RuntimeRunId, targetVersion.RuntimeRunId, $"rollback:{targetVersion.Id}");
        var rollbackEvent = await _events.AppendChapterStageAsync(
                new AppendChapterProductionEventRequest(
                    RuntimeRunId: runtimeRunId,
                    UserId: request.UserId,
                    ProjectId: request.ProjectId,
                    ChapterId: request.ChapterId,
                    PackageId: targetVersion.PackageId,
                    EventType: "chapter_version_rolled_back",
                    Stage: "version_rollback",
                    Status: "completed",
                    Message: $"章节已回滚到 v{targetVersion.VersionNumber}，下游生产包已标记过期。",
                    ArtifactType: "chapter_version",
                    ArtifactId: targetVersion.Id,
                    Data: new
                    {
                        reason = request.Reason,
                        idempotencyKey,
                        targetVersionId = targetVersion.Id,
                        targetVersionNumber = targetVersion.VersionNumber,
                        currentDocumentId = targetVersion.ContentDocumentId,
                        invalidatedPackageIds = invalidatedPackages.Select(package => package.Id).ToArray()
                    }),
                cancellationToken)
            .ConfigureAwait(false);

        if (_outputArtifacts != null)
        {
            await _outputArtifacts.RecordAsync(
                    new OutputArtifactRecordRequest(
                        RuntimeRunId: runtimeRunId,
                        UserId: request.UserId,
                        ProjectId: request.ProjectId,
                        ChapterId: request.ChapterId,
                        PackageId: targetVersion.PackageId,
                        ToolName: "RollbackChapterVersion",
                        Stage: "version_rollback",
                        Status: "completed",
                        ArtifactType: "chapter_version_rollback",
                        ArtifactId: targetVersion.Id,
                        OutputKind: "FinalArtifact",
                        Summary: $"已回滚到 v{targetVersion.VersionNumber}，书城当前正文已切换，下游生产包已标记过期。",
                        UserVisibleWhere: new[] { "小说书城", "创作工作流" },
                        VisibleInWorkflow: true,
                        VisibleInLibrary: true,
                        SourceEventType: rollbackEvent.EventType,
                        SourceEventId: rollbackEvent.Id,
                        Data: new
                        {
                            reason = request.Reason,
                            idempotencyKey,
                            targetVersionId = targetVersion.Id,
                            targetVersionNumber = targetVersion.VersionNumber,
                            currentDocumentId = targetVersion.ContentDocumentId,
                            invalidatedPackageIds = invalidatedPackages.Select(package => package.Id).ToArray()
                        }),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        await EnqueuePostRollbackOutboxAsync(
                request,
                targetVersion,
                committedContent,
                cancellationToken)
            .ConfigureAwait(false);

        return new RollbackChapterVersionResult
        {
            Success = true,
            Message = $"已回滚到 v{targetVersion.VersionNumber}，并使 {invalidatedPackages.Count} 个受影响生产包失效。",
            ProjectId = request.ProjectId,
            ChapterId = request.ChapterId,
            CurrentVersionId = targetVersion.Id,
            CurrentVersionNumber = targetVersion.VersionNumber,
            CurrentDocumentId = targetVersion.ContentDocumentId,
            RuntimeRunId = FirstNonEmpty(request.RuntimeRunId, targetVersion.RuntimeRunId),
            InvalidatedPackageIds = invalidatedPackages.Select(package => package.Id).ToList()
        };
    }

    private async Task<RollbackChapterVersionResult?> TryReadIdempotentRollbackResultAsync(
        RollbackChapterVersionRequest request,
        ChapterVersion targetVersion,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var existingEvent = await _db.ProductionEvents
            .AsNoTracking()
            .Where(evt =>
                evt.UserId == request.UserId &&
                evt.ProjectId == request.ProjectId &&
                evt.ChapterId == request.ChapterId &&
                evt.EventType == "chapter_version_rolled_back" &&
                evt.Stage == "version_rollback" &&
                evt.Status == "completed" &&
                evt.ArtifactType == "chapter_version" &&
                evt.ArtifactId == targetVersion.Id &&
                evt.DataJson != null &&
                evt.DataJson.Contains(idempotencyKey))
            .OrderByDescending(evt => evt.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (existingEvent == null)
            return null;

        var invalidatedPackageIds = ReadStringArray(existingEvent.DataJson, "invalidatedPackageIds");
        var versionNumber = GetJsonInt(existingEvent.DataJson, "targetVersionNumber");
        var currentDocumentId = FirstNonEmpty(GetJsonString(existingEvent.DataJson, "currentDocumentId"), targetVersion.ContentDocumentId);

        return new RollbackChapterVersionResult
        {
            Success = true,
            Message = $"已回滚到 v{FirstNonZero(versionNumber, targetVersion.VersionNumber)}，本次为重复请求，已复用上一次回滚结果。",
            ProjectId = request.ProjectId,
            ChapterId = request.ChapterId,
            CurrentVersionId = targetVersion.Id,
            CurrentVersionNumber = FirstNonZero(versionNumber, targetVersion.VersionNumber),
            CurrentDocumentId = currentDocumentId,
            RuntimeRunId = FirstNonEmpty(request.RuntimeRunId, targetVersion.RuntimeRunId),
            InvalidatedPackageIds = invalidatedPackageIds
        };
    }

    private async Task<List<TianmingPackage>> InvalidateAffectedPackagesAsync(
        RollbackChapterVersionRequest request,
        Chapter chapter,
        ChapterVersion targetVersion,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var downstreamChapterIds = await _db.Chapters
            .AsNoTracking()
            .Where(item =>
                item.ProjectId == request.ProjectId &&
                item.ChapterNumber >= chapter.ChapterNumber)
            .Select(item => item.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var downstreamSet = downstreamChapterIds.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var packages = await _db.TianmingPackages
            .Where(package =>
                package.UserId == request.UserId &&
                package.ProjectId == request.ProjectId &&
                package.ChapterId != null &&
                downstreamSet.Contains(package.ChapterId))
            .OrderBy(package => package.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var invalidated = new List<TianmingPackage>();
        foreach (var package in packages)
        {
            if (string.Equals(package.Id, targetVersion.PackageId, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!string.Equals(package.Status, "stale", StringComparison.OrdinalIgnoreCase))
            {
                package.Status = "stale";
                package.UpdatedAt = now;
            }

            invalidated.Add(package);
        }

        return invalidated;
    }

    private async Task<string> ReadDocumentTextAsync(string documentId, CancellationToken cancellationToken)
    {
        var chunks = await _db.ContentChunks
            .AsNoTracking()
            .Where(chunk => chunk.DocumentId == documentId)
            .OrderBy(chunk => chunk.ChunkIndex)
            .Select(chunk => chunk.ChunkText)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return string.Join(string.Empty, chunks);
    }

    private async Task EnqueuePostRollbackOutboxAsync(
        RollbackChapterVersionRequest request,
        ChapterVersion targetVersion,
        string committedContent,
        CancellationToken cancellationToken)
    {
        var runtimeRunId = FirstNonEmpty(request.RuntimeRunId, targetVersion.RuntimeRunId, $"rollback:{targetVersion.Id}");
        await _truthStore.EnqueueOutboxAsync(
                new EnqueueOutboxEventRequest(
                    UserId: request.UserId,
                    ProjectId: request.ProjectId,
                    RuntimeRunId: runtimeRunId,
                    EventType: "index_chapter_content",
                    AggregateType: "chapter_version",
                    AggregateId: targetVersion.Id,
                    PayloadJson: "{}"),
                cancellationToken)
            .ConfigureAwait(false);

        await _truthStore.EnqueueOutboxAsync(
                new EnqueueOutboxEventRequest(
                    UserId: request.UserId,
                    ProjectId: request.ProjectId,
                    RuntimeRunId: runtimeRunId,
                    EventType: "extract_chapter_continuity_facts",
                    AggregateType: "chapter",
                    AggregateId: request.ChapterId,
                    PayloadJson: JsonSerializer.Serialize(new
                    {
                        run = new NovelAgentRun
                        {
                            RunId = runtimeRunId,
                            TargetChapterId = request.ChapterId,
                            UserGoal = request.Reason ?? "章节版本回滚后重新沉淀事实。"
                        },
                        contextPackage = new ChapterContextPackageSummary
                        {
                            ChapterId = request.ChapterId,
                            PackageId = targetVersion.PackageId ?? string.Empty
                        },
                        committedContent
                    }, PayloadJsonOptions)),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static int FirstNonZero(params int[] values) =>
        values.FirstOrDefault(value => value > 0);

    private static string Normalize(string? value) =>
        value?.Trim() ?? string.Empty;

    private static int GetJsonInt(string? json, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(json))
            return 0;

        using var document = TryParseJson(json);
        if (document == null ||
            document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty(propertyName, out var property))
        {
            return 0;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var number))
            return number;
        return property.ValueKind == JsonValueKind.String &&
               int.TryParse(property.GetString(), out number)
            ? number
            : 0;
    }

    private static string GetJsonString(string? json, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(json))
            return string.Empty;

        using var document = TryParseJson(json);
        if (document == null ||
            document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty(propertyName, out var property))
        {
            return string.Empty;
        }

        return property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : property.ToString();
    }

    private static List<string> ReadStringArray(string? json, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new List<string>();

        using var document = TryParseJson(json);
        if (document == null ||
            document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.Array)
        {
            return new List<string>();
        }

        return property
            .EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToList();
    }

    private static JsonDocument? TryParseJson(string? json)
    {
        try
        {
            return string.IsNullOrWhiteSpace(json) ? null : JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
