using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Auth;

namespace TM.Web.NovelAgentWeb.Services.Knowledge;

public sealed class KnowledgeDocumentIngestionService : IKnowledgeDocumentIngestionService
{
    private const int MaxChunkLength = 2000;
    private readonly NovelAgentDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IKnowledgeStructureModelClient _model;

    public KnowledgeDocumentIngestionService(
        NovelAgentDbContext db,
        ICurrentUserService currentUser,
        IKnowledgeStructureModelClient model)
    {
        _db = db;
        _currentUser = currentUser;
        _model = model;
    }

    public async Task<KnowledgeDocumentBlob> StoreUploadAsync(
        string projectId,
        string fileName,
        string mimeType,
        byte[] data,
        CancellationToken cancellationToken = default)
    {
        if (data.Length == 0)
            throw new ArgumentException("知识文档不能为空。", nameof(data));
        var userId = _currentUser.GetUserId();
        var projectExists = await _db.NovelProjects.AsNoTracking().AnyAsync(project =>
            project.Id == projectId && project.UserId == userId,
            cancellationToken);
        if (!projectExists)
            throw new KeyNotFoundException("项目不存在或不属于当前用户。");

        IDbContextTransaction? transaction = null;
        if (_db.Database.IsRelational())
            transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var version = (await _db.KnowledgeDocumentBlobs
                .Where(blob => blob.UserId == userId)
                .Select(blob => (long?)blob.KnowledgeVersion)
                .MaxAsync(cancellationToken) ?? 0) + 1;
            var now = DateTime.UtcNow;
            var blob = new KnowledgeDocumentBlob
            {
                Id = Guid.NewGuid().ToString("N"),
                UserId = userId,
                ProjectId = projectId,
                FileName = Path.GetFileName(fileName),
                MimeType = string.IsNullOrWhiteSpace(mimeType) ? "application/octet-stream" : mimeType,
                Data = data.ToArray(),
                ContentHash = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant(),
                KnowledgeVersion = version,
                Status = "uploaded",
                CreatedAt = now,
                UpdatedAt = now
            };
            _db.KnowledgeDocumentBlobs.Add(blob);
            await _db.SaveChangesAsync(cancellationToken);
            if (transaction != null)
                await transaction.CommitAsync(cancellationToken);
            return blob;
        }
        catch
        {
            if (transaction != null)
                await transaction.RollbackAsync(cancellationToken);
            throw;
        }
        finally
        {
            if (transaction != null)
                await transaction.DisposeAsync();
        }
    }

    public async Task FinalizeProcessingAsync(
        string documentBlobId,
        string parsedText,
        IReadOnlyList<string> logicalKnowledgeIds,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(parsedText))
            throw new ArgumentException("解析后的知识文本不能为空。", nameof(parsedText));
        var userId = _currentUser.GetUserId();
        var blob = await _db.KnowledgeDocumentBlobs.SingleOrDefaultAsync(item =>
            item.Id == documentBlobId && item.UserId == userId && item.Status == "uploaded",
            cancellationToken) ?? throw new KeyNotFoundException("知识文档原件不存在或不可处理。");
        var logicalEntries = await _db.KnowledgeBases
            .Where(entry => entry.UserId == userId && logicalKnowledgeIds.Contains(entry.Id))
            .ToListAsync(cancellationToken);
        if (logicalEntries.Count != logicalKnowledgeIds.Distinct(StringComparer.Ordinal).Count())
            throw new InvalidOperationException("提取知识条目缺失或不属于当前用户。");
        var byId = logicalEntries.ToDictionary(entry => entry.Id, StringComparer.Ordinal);
        var orderedEntries = logicalKnowledgeIds.Select(id => byId[id]).ToArray();
        var analysis = await _model.AnalyzeAsync(new KnowledgeStructureRequest(
            userId,
            blob.ProjectId,
            blob.Id,
            blob.KnowledgeVersion,
            parsedText,
            orderedEntries), cancellationToken);
        ValidateSections(analysis.Sections, parsedText);
        ValidateAbstractStyleProfile(analysis.StyleProfile);
        if (analysis.HighImpactEntryIndexes.Any(index => index < 0 || index >= orderedEntries.Length))
            throw new InvalidOperationException("结构模型返回了不存在的高影响知识条目索引。");
        var now = DateTime.UtcNow;
        var chunks = new List<KnowledgeChunk>();
        foreach (var (sectionDraft, sectionIndex) in analysis.Sections.Select((section, index) => (section, index)))
        {
            var section = new KnowledgeSection
            {
                Id = Guid.NewGuid().ToString("N"),
                UserId = userId,
                ProjectId = blob.ProjectId,
                DocumentBlobId = blob.Id,
                KnowledgeVersion = blob.KnowledgeVersion,
                SectionIndex = sectionIndex,
                Title = sectionDraft.Title,
                Summary = sectionDraft.Summary,
                Text = parsedText[sectionDraft.CharStart..sectionDraft.CharEnd],
                CharStart = sectionDraft.CharStart,
                CharEnd = sectionDraft.CharEnd,
                CreatedAt = now
            };
            _db.KnowledgeSections.Add(section);
            chunks.AddRange(CreateChunks(blob, section, chunks.Count, now));
        }
        for (var index = 0; index < chunks.Count; index++)
        {
            chunks[index].PreviousChunkId = index == 0 ? null : chunks[index - 1].Id;
            chunks[index].NextChunkId = index == chunks.Count - 1 ? null : chunks[index + 1].Id;
        }
        _db.KnowledgeChunks.AddRange(chunks);

        var highImpact = analysis.HighImpactEntryIndexes.ToHashSet();
        for (var index = 0; index < orderedEntries.Length; index++)
        {
            var logical = orderedEntries[index];
            var version = (await _db.KnowledgeEntries
                .Where(entry => entry.UserId == userId && entry.LogicalKnowledgeId == logical.Id)
                .Select(entry => (int?)entry.Version)
                .MaxAsync(cancellationToken) ?? 0) + 1;
            _db.KnowledgeEntries.Add(new KnowledgeEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                UserId = userId,
                ProjectId = blob.ProjectId,
                LogicalKnowledgeId = logical.Id,
                DocumentBlobId = blob.Id,
                KnowledgeVersion = blob.KnowledgeVersion,
                Version = version,
                SourceEntryIndex = index,
                EntryType = logical.EntryType,
                Title = logical.Title,
                Content = logical.Content,
                Summary = analysis.DocumentSummary,
                Status = highImpact.Contains(index) ? "proposed" : "active",
                CreatedAt = now
            });
        }
        _db.StyleProfiles.Add(new StyleProfile
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = userId,
            ProjectId = blob.ProjectId,
            DocumentBlobId = blob.Id,
            KnowledgeVersion = blob.KnowledgeVersion,
            Version = 1,
            ProfileKind = "abstract_features_only",
            FeaturesJson = JsonSerializer.Serialize(new
            {
                narrativeDistance = analysis.StyleProfile.NarrativeDistance,
                sentenceRhythm = analysis.StyleProfile.SentenceRhythm,
                dialogueDensity = analysis.StyleProfile.DialogueDensity,
                descriptionRatio = analysis.StyleProfile.DescriptionRatio,
                imagery = analysis.StyleProfile.Imagery,
                emotionalIntensity = analysis.StyleProfile.EmotionalIntensity,
                informationRelease = analysis.StyleProfile.InformationRelease
            }),
            Status = "active",
            CreatedAt = now
        });
        blob.Status = "processed";
        blob.UpdatedAt = now;
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static IReadOnlyList<KnowledgeChunk> CreateChunks(
        KnowledgeDocumentBlob blob,
        KnowledgeSection section,
        int startingIndex,
        DateTime now)
    {
        var result = new List<KnowledgeChunk>();
        for (var offset = 0; offset < section.Text.Length; offset += MaxChunkLength)
        {
            var length = Math.Min(MaxChunkLength, section.Text.Length - offset);
            var text = section.Text.Substring(offset, length);
            result.Add(new KnowledgeChunk
            {
                Id = Guid.NewGuid().ToString("N"),
                UserId = blob.UserId,
                ProjectId = blob.ProjectId,
                DocumentBlobId = blob.Id,
                SectionId = section.Id,
                KnowledgeVersion = blob.KnowledgeVersion,
                ChunkIndex = startingIndex + result.Count,
                Text = text,
                CharStart = section.CharStart + offset,
                CharEnd = section.CharStart + offset + length,
                ContentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant(),
                CreatedAt = now
            });
        }
        return result;
    }

    private static void ValidateSections(IReadOnlyList<KnowledgeSectionDraft> sections, string text)
    {
        if (sections.Count == 0)
            throw new InvalidOperationException("结构模型没有返回语义 Section。");
        var previousEnd = 0;
        foreach (var section in sections.OrderBy(item => item.CharStart))
        {
            if (section.CharStart < previousEnd || section.CharStart < 0 || section.CharEnd <= section.CharStart || section.CharEnd > text.Length)
                throw new InvalidOperationException("结构模型返回的 Section 区间无效或重叠。");
            previousEnd = section.CharEnd;
        }
    }

    private static void ValidateAbstractStyleProfile(AbstractStyleProfileDraft profile)
    {
        var values = new[]
        {
            profile.NarrativeDistance,
            profile.SentenceRhythm,
            profile.EmotionalIntensity,
            profile.InformationRelease
        }.Concat(profile.Imagery);
        var forbiddenInstructions = new[]
        {
            "模仿",
            "仿写",
            "续写",
            "imitate",
            "continue the original"
        };
        if (values.Any(value => forbiddenInstructions.Any(instruction =>
                value.Contains(instruction, StringComparison.OrdinalIgnoreCase))))
            throw new InvalidOperationException("StyleProfile 只能包含抽象风格特征，不能包含作者模仿或原文续写指令。");
        if (profile.DialogueDensity is < 0 or > 1 || profile.DescriptionRatio is < 0 or > 1)
            throw new InvalidOperationException("StyleProfile 的密度比例必须在 0 到 1 之间。");
    }
}
