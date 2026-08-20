using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.AgentSessions;
using TM.Web.NovelAgentWeb.Services.Auth;

namespace TM.Web.NovelAgentWeb.DataMigration;

public sealed record LegacyRecoveryPreparationResult(
    string ProjectId,
    string ArchiveDocumentId,
    string RecoveryIntentId,
    int SessionCount,
    int AgentRunCount,
    int RuntimeRunCount,
    int ToolExecutionCount,
    bool Reused);

public interface ILegacyExecutionArchiveService
{
    Task<IReadOnlyList<LegacyRecoveryPreparationResult>> PrepareUserAsync(
        string userId,
        CancellationToken cancellationToken = default);

    Task<LegacyRecoveryPreparationResult> PrepareProjectAsync(
        string projectId,
        CancellationToken cancellationToken = default);
}

public sealed class LegacyExecutionArchiveService : ILegacyExecutionArchiveService
{
    public const string ArchiveSourceType = "legacy_execution_archive";
    public const string ArchiveDocumentRole = "execution_archive";
    public const string RecoveryIntentSource = "legacy_recovery";
    private const int ChunkSize = 4000;
    private static readonly string[] ActiveLegacyStatuses =
        ["queued", "running", "Planning", "Repairing", "awaiting_confirmation"];

    private readonly NovelAgentDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IAgentSessionApplicationService _sessions;

    public LegacyExecutionArchiveService(
        NovelAgentDbContext db,
        ICurrentUserService currentUser,
        IAgentSessionApplicationService sessions)
    {
        _db = db;
        _currentUser = currentUser;
        _sessions = sessions;
    }

    public async Task<IReadOnlyList<LegacyRecoveryPreparationResult>> PrepareUserAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(_currentUser.GetUserId(), userId, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("只能归档当前用户的旧执行记录。");
        var projectIds = await _db.NovelProjects.AsNoTracking()
            .Where(project =>
                project.UserId == userId &&
                !_db.CreativeGoals.Any(goal => goal.UserId == userId && goal.ProjectId == project.Id) &&
                (_db.AgentRuns.Any(run => run.UserId == userId && run.ProjectId == project.Id) ||
                 _db.AgentRuntimeRuns.Any(run => run.UserId == userId && run.ProjectId == project.Id) ||
                 _db.AgentToolExecutions.Any(tool => tool.UserId == userId && tool.ProjectId == project.Id) ||
                 _db.AgentSessions.Any(session => session.UserId == userId && session.ProjectId == project.Id)))
            .Select(project => project.Id)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        var results = new List<LegacyRecoveryPreparationResult>(projectIds.Length);
        foreach (var projectId in projectIds)
            results.Add(await PrepareProjectAsync(projectId, cancellationToken).ConfigureAwait(false));
        return results;
    }

    public async Task<LegacyRecoveryPreparationResult> PrepareProjectAsync(
        string projectId,
        CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.GetUserId();
        var project = await _db.NovelProjects.AsNoTracking().SingleOrDefaultAsync(item =>
            item.Id == projectId && item.UserId == userId,
            cancellationToken) ?? throw new KeyNotFoundException("旧项目不存在或不属于当前用户。");
        var existingArchive = await _db.ContentDocuments.AsNoTracking()
            .Where(item =>
                item.UserId == userId &&
                item.ProjectId == projectId &&
                item.SourceType == ArchiveSourceType &&
                item.SourceId == projectId &&
                item.DocumentRole == ArchiveDocumentRole &&
                item.Status == "active")
            .OrderByDescending(item => item.Version)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        var existingIntent = await _db.CreativeIntents.AsNoTracking().SingleOrDefaultAsync(item =>
            item.UserId == userId &&
            item.ProjectId == projectId &&
            item.Source == RecoveryIntentSource &&
            item.IdempotencyKey == $"legacy-recovery:{projectId}",
            cancellationToken);
        if (existingArchive != null && existingIntent != null)
        {
            return new LegacyRecoveryPreparationResult(
                projectId,
                existingArchive.Id,
                existingIntent.Id,
                0,
                0,
                0,
                0,
                true);
        }

        var runtimeSessions = (await _sessions.ListRuntimeSessionsAsync(cancellationToken).ConfigureAwait(false))
            .Where(item => item.ActiveProjectId == projectId)
            .Select(item => new
            {
                item.SessionId,
                item.Title,
                item.Phase,
                item.ActiveProjectId,
                item.WorkingMemory.MissionPlan,
                item.RunHistory,
                item.CreatedAt,
                item.UpdatedAt
            })
            .ToArray();
        var agentRuns = await _db.AgentRuns.AsNoTracking()
            .Where(item => item.UserId == userId && item.ProjectId == projectId)
            .OrderBy(item => item.CreatedAt)
            .Select(item => new
            {
                item.Id,
                item.RunType,
                item.TargetChapterId,
                item.Status,
                item.InputParams,
                item.OutputData,
                item.OutputDocumentId,
                item.StartedAt,
                item.CompletedAt,
                item.CreatedAt,
                item.UpdatedAt
            })
            .ToArrayAsync(cancellationToken);
        var runtimeRuns = await _db.AgentRuntimeRuns
            .Where(item => item.UserId == userId && item.ProjectId == projectId)
            .OrderBy(item => item.CreatedAt)
            .ToArrayAsync(cancellationToken);
        var tools = await _db.AgentToolExecutions.AsNoTracking()
            .Where(item => item.UserId == userId && item.ProjectId == projectId)
            .OrderBy(item => item.StartedAt)
            .Select(item => new
            {
                item.Id,
                item.SessionId,
                item.RunId,
                item.ToolName,
                item.Phase,
                item.Risk,
                item.ArgumentsJson,
                item.SideEffectsJson,
                item.SemanticContractJson,
                item.Status,
                item.ResultPhase,
                item.ResultMessage,
                item.ErrorType,
                item.ErrorMessage,
                item.FailureJson,
                item.ArtifactJson,
                item.StartedAt,
                item.CompletedAt
            })
            .ToArrayAsync(cancellationToken);
        var archivedAt = DateTime.UtcNow;
        var json = JsonSerializer.Serialize(new
        {
            SchemaVersion = 1,
            Project = new { project.Id, project.Title, project.Status, project.UpdatedAt },
            ArchivedAt = archivedAt,
            Sessions = runtimeSessions,
            AgentRuns = agentRuns,
            RuntimeRuns = runtimeRuns.Select(item => new
            {
                item.Id,
                item.SessionId,
                item.Status,
                item.Mode,
                item.CurrentPhase,
                item.CurrentStep,
                item.ActiveTool,
                item.UserMessage,
                item.SourceMessageId,
                item.BudgetJson,
                item.LastMessage,
                item.ResultJson,
                item.ErrorMessage,
                item.FailureJson,
                item.StartedAt,
                item.CompletedAt,
                item.CreatedAt,
                item.UpdatedAt
            }),
            ToolExecutions = tools
        });

        IDbContextTransaction? transaction = null;
        if (_db.Database.IsRelational() && _db.Database.CurrentTransaction == null)
            transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var document = existingArchive ?? new ContentDocument
            {
                Id = Guid.NewGuid().ToString("N"),
                UserId = userId,
                ProjectId = projectId,
                SourceType = ArchiveSourceType,
                SourceId = projectId,
                DocumentRole = ArchiveDocumentRole,
                Title = $"{project.Title} 旧 Agent 执行归档",
                MimeType = "application/json",
                ContentHash = Hash(json),
                Version = 1,
                Status = "active",
                CreatedAt = archivedAt,
                UpdatedAt = archivedAt
            };
            if (existingArchive == null)
            {
                _db.ContentDocuments.Add(document);
                for (var offset = 0; offset < json.Length; offset += ChunkSize)
                {
                    var chunk = json.Substring(offset, Math.Min(ChunkSize, json.Length - offset));
                    _db.ContentChunks.Add(new ContentChunk
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        DocumentId = document.Id,
                        ChunkIndex = offset / ChunkSize,
                        ChunkText = chunk,
                        TokenCount = Math.Max(1, chunk.Length / 4),
                        CharStart = offset,
                        CharEnd = offset + chunk.Length,
                        ContentHash = Hash(chunk)
                    });
                }
            }

            var intent = existingIntent ?? new CreativeIntent
            {
                Id = Guid.NewGuid().ToString("N"),
                UserId = userId,
                ProjectId = projectId,
                IdempotencyKey = $"legacy-recovery:{projectId}",
                Source = RecoveryIntentSource,
                RawContent = "基于当前正式内容继续旧项目，并使用新的 Goal 状态机重新确认未完成目标。",
                NormalizedIntent = "创建 Recovery Goal Proposal",
                TargetScope = "project",
                Status = "candidate",
                ImpactLevel = "future_carry",
                RequiresConfirmation = true,
                ConflictStatus = "unknown",
                MetadataJson = JsonSerializer.Serialize(new
                {
                    archiveDocumentId = document.Id,
                    runtimeSessionCount = runtimeSessions.Length,
                    agentRunCount = agentRuns.Length,
                    runtimeRunCount = runtimeRuns.Length,
                    toolExecutionCount = tools.Length
                }),
                CreatedAt = archivedAt,
                UpdatedAt = archivedAt
            };
            if (existingIntent == null)
                _db.CreativeIntents.Add(intent);

            foreach (var runtime in runtimeRuns.Where(item => ActiveLegacyStatuses.Contains(item.Status)))
            {
                runtime.Status = "archived";
                runtime.CancelRequested = true;
                runtime.CompletedAt ??= archivedAt;
                runtime.LastMessage = "旧执行状态已归档，请通过 Recovery Goal 在新状态机继续。";
                runtime.UpdatedAt = archivedAt;
            }
            await _db.SaveChangesAsync(cancellationToken);
            if (transaction != null)
                await transaction.CommitAsync(cancellationToken);
            return new LegacyRecoveryPreparationResult(
                projectId,
                document.Id,
                intent.Id,
                runtimeSessions.Length,
                agentRuns.Length,
                runtimeRuns.Length,
                tools.Length,
                false);
        }
        catch
        {
            if (transaction != null)
                await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
        finally
        {
            if (transaction != null)
                await transaction.DisposeAsync();
        }
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
