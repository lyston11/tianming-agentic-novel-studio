using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Content;

namespace TM.Web.NovelAgentWeb.Scripts;

public record LegacyContentMigrationResult(
    int ChaptersMigrated,
    int MaterialsMigrated,
    int KnowledgeMigrated,
    int StoryBibleSnapshotsMigrated,
    int AgentRunArtifactsMigrated);

public static class MigrateLegacyContentToSqlite
{
    public static async Task<LegacyContentMigrationResult> RunAsync(
        NovelAgentDbContext db,
        IContentDocumentService content,
        string storageRoot,
        ILogger logger,
        CancellationToken ct = default)
    {
        var chapters = await MigrateChaptersAsync(db, content, storageRoot, ct);
        var materials = await MigrateMaterialsAsync(db, content, storageRoot, ct);
        var knowledgeUploads = await MigrateKnowledgeUploadsAsync(db, content, storageRoot, ct);
        var storyBibleSnapshots = await MigrateStoryBibleSnapshotsAsync(db, content, ct);
        var agentRunArtifacts = await MigrateAgentRunArtifactsAsync(db, content, ct);

        logger.LogInformation(
            "Migrated legacy content into content_documents: chapters={ChapterCount}, materials={MaterialCount}, knowledgeUploads={KnowledgeUploadCount}, storyBibleSnapshots={StoryBibleSnapshotCount}, agentRunArtifacts={AgentRunArtifactCount}",
            chapters,
            materials,
            knowledgeUploads,
            storyBibleSnapshots,
            agentRunArtifacts);

        return new LegacyContentMigrationResult(
            chapters,
            materials,
            knowledgeUploads,
            storyBibleSnapshots,
            agentRunArtifacts);
    }

    private static async Task<int> MigrateChaptersAsync(
        NovelAgentDbContext db,
        IContentDocumentService content,
        string storageRoot,
        CancellationToken ct)
    {
        var migrated = 0;
        var chapters = await db.Chapters
            .Include(c => c.Project)
            .ToListAsync(ct);

        foreach (var chapter in chapters)
        {
            if (!TryResolveExistingPath(chapter.ContentPath, storageRoot, out var path))
            {
                continue;
            }

            if (await HasDocumentAsync(db, "chapter", chapter.Id, "chapter_body", ct))
            {
                continue;
            }

            var text = await File.ReadAllTextAsync(path, ct);
            await content.SaveTextAsync(
                chapter.Project.UserId,
                chapter.ProjectId,
                "chapter",
                chapter.Id,
                "chapter_body",
                chapter.Title,
                text,
                ct);
            migrated++;
        }

        return migrated;
    }

    private static async Task<int> MigrateMaterialsAsync(
        NovelAgentDbContext db,
        IContentDocumentService content,
        string storageRoot,
        CancellationToken ct)
    {
        var migrated = 0;
        var materials = await db.Materials
            .Include(m => m.Project)
            .ToListAsync(ct);

        foreach (var material in materials)
        {
            if (!TryResolveExistingPath(material.FilePath, storageRoot, out var path))
            {
                continue;
            }

            if (await HasDocumentAsync(db, "material", material.Id, "material_raw", ct))
            {
                continue;
            }

            var text = await File.ReadAllTextAsync(path, ct);
            await content.SaveTextAsync(
                material.Project?.UserId ?? material.UserId,
                material.ProjectId,
                "material",
                material.Id,
                "material_raw",
                material.Title,
                text,
                ct);
            migrated++;
        }

        return migrated;
    }

    private static async Task<int> MigrateKnowledgeUploadsAsync(
        NovelAgentDbContext db,
        IContentDocumentService content,
        string storageRoot,
        CancellationToken ct)
    {
        var migrated = 0;
        var tasks = await db.KnowledgeProcessingTasks
            .Include(t => t.Project)
            .ToListAsync(ct);

        foreach (var task in tasks)
        {
            if (!TryResolveExistingPath(task.FilePath, storageRoot, out var path))
            {
                continue;
            }

            if (await HasDocumentAsync(db, "knowledge_upload", task.Id, "upload_raw", ct))
            {
                continue;
            }

            var text = await File.ReadAllTextAsync(path, ct);
            await content.SaveTextAsync(
                task.UserId,
                task.ProjectId,
                "knowledge_upload",
                task.Id,
                "upload_raw",
                task.FileName,
                text,
                ct);
            migrated++;
        }

        return migrated;
    }

    private static async Task<int> MigrateStoryBibleSnapshotsAsync(
        NovelAgentDbContext db,
        IContentDocumentService content,
        CancellationToken ct)
    {
        var migrated = 0;
        var constitutions = await db.StoryConstitutions
            .Include(s => s.Project)
            .ToListAsync(ct);

        foreach (var constitution in constitutions)
        {
            if (await HasDocumentAsync(db, "story_bible", constitution.Id, "snapshot", ct))
            {
                continue;
            }

            var snapshot = BuildStoryBibleSnapshotJson(constitution);
            if (string.IsNullOrWhiteSpace(snapshot))
            {
                continue;
            }

            await content.SaveTextAsync(
                constitution.Project?.UserId ?? constitution.UserId,
                constitution.ProjectId,
                "story_bible",
                constitution.Id,
                "snapshot",
                "StoryBible Snapshot",
                snapshot,
                ct);
            migrated++;
        }

        return migrated;
    }

    private static async Task<int> MigrateAgentRunArtifactsAsync(
        NovelAgentDbContext db,
        IContentDocumentService content,
        CancellationToken ct)
    {
        var migrated = 0;
        var runs = await db.AgentRuns.ToListAsync(ct);

        foreach (var run in runs)
        {
            if (string.IsNullOrWhiteSpace(run.OutputData) || run.OutputData.Length < 512)
            {
                continue;
            }

            if (await HasDocumentAsync(db, "agent_run", run.Id, "artifact", ct))
            {
                continue;
            }

            await content.SaveTextAsync(
                run.UserId,
                run.ProjectId,
                "agent_run",
                run.Id,
                "artifact",
                run.RunType,
                run.OutputData,
                ct);
            migrated++;
        }

        return migrated;
    }

    private static Task<bool> HasDocumentAsync(
        NovelAgentDbContext db,
        string sourceType,
        string sourceId,
        string documentRole,
        CancellationToken ct)
    {
        return db.ContentDocuments.AnyAsync(d =>
            d.SourceType == sourceType &&
            d.SourceId == sourceId &&
            d.DocumentRole == documentRole,
            ct);
    }

    private static bool TryResolveExistingPath(string? legacyPath, string storageRoot, out string path)
    {
        path = string.Empty;
        if (string.IsNullOrWhiteSpace(legacyPath))
        {
            return false;
        }

        path = Path.IsPathRooted(legacyPath)
            ? legacyPath
            : Path.Combine(storageRoot, legacyPath);
        return File.Exists(path);
    }

    private static string BuildStoryBibleSnapshotJson(StoryConstitution constitution)
    {
        var snapshot = new
        {
            constitution.Id,
            constitution.UserId,
            constitution.ProjectId,
            constitution.Genre,
            constitution.SubGenre,
            constitution.CoreHook,
            constitution.ReaderPromise,
            constitution.GenreProfile,
            constitution.TargetAudience,
            constitution.Taboos,
            constitution.CreatedAt,
            constitution.UpdatedAt
        };

        return JsonSerializer.Serialize(snapshot);
    }
}
