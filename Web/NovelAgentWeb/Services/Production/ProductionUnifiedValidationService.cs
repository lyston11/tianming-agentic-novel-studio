using Microsoft.EntityFrameworkCore;
using TM.Framework.Common.Helpers;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Support;

namespace TM.Web.NovelAgentWeb.Services.Production;

public sealed class ProductionUnifiedValidationService : IUnifiedValidationService
{
    private const int MinimumCommittedContentChars = 100;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IGeneratedContentService _contentService;
    private readonly string _userId;
    private readonly string _projectId;

    public ProductionUnifiedValidationService(
        IServiceScopeFactory scopeFactory,
        IGeneratedContentService contentService,
        string userId,
        string projectId)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _contentService = contentService ?? throw new ArgumentNullException(nameof(contentService));
        _userId = userId ?? string.Empty;
        _projectId = projectId ?? string.Empty;
    }

    public async Task<ChapterValidationResult> ValidateChapterAsync(
        string chapterId,
        CancellationToken ct = default)
    {
        var result = new ChapterValidationResult
        {
            ChapterId = chapterId?.Trim() ?? string.Empty,
            ValidatedTime = DateTime.Now
        };

        if (string.IsNullOrWhiteSpace(_projectId))
            AddIssue(result, "project_scope", "Error", "缺少项目上下文，不能执行章节一致性校验。", "先绑定或创建项目。");

        if (string.IsNullOrWhiteSpace(result.ChapterId))
        {
            AddIssue(result, "chapter_identity", "Error", "章节 ID 为空，不能执行章节一致性校验。", "传入明确的章节 ID。");
            Complete(result);
            return result;
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
        var projectUserId = await db.NovelProjects
            .AsNoTracking()
            .Where(project => project.Id == _projectId)
            .Select(project => project.UserId)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(projectUserId))
            AddIssue(result, "project_scope", "Error", $"项目 {_projectId} 不存在。", "先绑定或创建真实项目。");

        var chapter = await db.Chapters
            .AsNoTracking()
            .Where(item => item.ProjectId == _projectId && item.Id == result.ChapterId)
            .Select(item => new
            {
                item.Id,
                item.Title,
                item.ChapterNumber,
                item.WordCount,
                item.Status,
                item.VolumeId
            })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (chapter == null)
        {
            AddIssue(result, "chapter_identity", "Error", $"项目中不存在章节 {result.ChapterId}。", "确认章节已创建并属于当前项目。");
            Complete(result);
            return result;
        }

        var volume = string.IsNullOrWhiteSpace(chapter.VolumeId)
            ? null
            : await db.Volumes
                .AsNoTracking()
                .Where(item => item.ProjectId == _projectId && item.Id == chapter.VolumeId)
                .Select(item => new { item.VolumeNumber, item.Title })
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);

        result.ChapterTitle = chapter.Title ?? string.Empty;
        result.ChapterNumber = chapter.ChapterNumber > 0
            ? chapter.ChapterNumber
            : ChapterParserHelper.ParseChapterId(chapter.Id)?.chapterNumber ?? 0;
        result.VolumeNumber = volume?.VolumeNumber
            ?? ChapterParserHelper.ParseChapterId(chapter.Id)?.volumeNumber
            ?? 0;
        result.VolumeName = volume?.Title ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(_userId) &&
            !string.Equals(projectUserId, _userId, StringComparison.OrdinalIgnoreCase))
        {
            AddIssue(result, "project_scope", "Error", "章节所属用户与当前工作区用户不一致。", "停止提交并重新绑定正确项目。");
        }

        if (string.IsNullOrWhiteSpace(result.ChapterTitle) ||
            string.Equals(result.ChapterTitle, result.ChapterId, StringComparison.OrdinalIgnoreCase))
        {
            AddIssue(result, "chapter_metadata", "Warning", "章节标题缺失或仍是技术 ID。", "提交章节时写入可读章节名。");
        }

        if (result.ChapterNumber <= 0)
            AddIssue(result, "chapter_metadata", "Warning", "章节序号缺失。", "修正章节编号，保证工作流和书城排序稳定。");

        if (result.VolumeNumber <= 0)
            AddIssue(result, "chapter_metadata", "Warning", "章节未能解析出所属卷。", "提交章节时绑定卷信息。");

        var content = await _contentService.GetChapterAsync(result.ChapterId).ConfigureAwait(false) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(content))
        {
            AddIssue(result, "chapter_content", "Error", "章节正文未写入书城内容存储。", "先提交正文，再执行生成后复盘。");
        }
        else if (content.Trim().Length < MinimumCommittedContentChars)
        {
            AddIssue(result, "chapter_content", "Warning", $"章节正文过短，当前 {content.Trim().Length} 字。", "重新生成或扩写章节正文。");
        }

        if (chapter.WordCount <= 0 && !string.IsNullOrWhiteSpace(content))
            AddIssue(result, "chapter_metadata", "Warning", "章节字数统计未更新。", "提交正文后同步章节字数。");

        Complete(result);
        return result;
    }

    public async Task<VolumeValidationResult> ValidateVolumeAsync(
        int volumeNumber,
        CancellationToken ct = default)
    {
        var result = new VolumeValidationResult
        {
            VolumeNumber = volumeNumber,
            ValidatedTime = DateTime.Now
        };

        if (volumeNumber <= 0 || string.IsNullOrWhiteSpace(_projectId))
            return result;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
        var volume = await db.Volumes
            .AsNoTracking()
            .Where(item => item.ProjectId == _projectId && item.VolumeNumber == volumeNumber)
            .Select(item => new { item.Id, item.Title })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (volume == null)
            return result;

        result.VolumeName = volume.Title ?? string.Empty;
        var chapters = await db.Chapters
            .AsNoTracking()
            .Where(item => item.ProjectId == _projectId && item.VolumeId == volume.Id)
            .OrderBy(item => item.ChapterNumber)
            .Select(item => new { item.Id })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        foreach (var chapter in chapters)
            result.ChapterResults.Add(await ValidateChapterAsync(chapter.Id, ct).ConfigureAwait(false));

        return result;
    }

    public async Task<bool> NeedsRepublishAsync()
    {
        if (string.IsNullOrWhiteSpace(_projectId))
            return false;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
        var query = db.OutboxEvents
            .AsNoTracking()
            .Where(evt => evt.ProjectId == _projectId && evt.Status != "completed");
        if (!string.IsNullOrWhiteSpace(_userId))
            query = query.Where(evt => evt.UserId == _userId);

        return await query.AnyAsync().ConfigureAwait(false);
    }

    private static void AddIssue(
        ChapterValidationResult result,
        string module,
        string severity,
        string message,
        string suggestion)
    {
        if (!result.IssuesByModule.TryGetValue(module, out var issues))
        {
            issues = new List<ValidationIssue>();
            result.IssuesByModule[module] = issues;
        }

        issues.Add(new ValidationIssue
        {
            Type = module,
            Severity = severity,
            Message = message,
            Suggestion = suggestion,
            EntityId = result.ChapterId,
            EntityName = result.ChapterTitle,
            Location = result.ChapterId
        });
    }

    private static void Complete(ChapterValidationResult result)
    {
        result.OverallResult = result.HasErrors
            ? "失败"
            : result.HasWarnings
                ? "警告"
                : "通过";
    }
}
