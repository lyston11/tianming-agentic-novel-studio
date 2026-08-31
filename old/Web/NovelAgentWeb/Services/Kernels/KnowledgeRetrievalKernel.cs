using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Services.DomainEvents;
using TM.Web.NovelAgentWeb.Services.Rag;

namespace TM.Web.NovelAgentWeb.Services.Kernels;

public interface IChapterTargetResolver
{
    Task<string> ResolveAsync(
        string userId,
        string projectId,
        int chapterNumber,
        CancellationToken cancellationToken = default);
}

public sealed class ChapterTargetResolver : IChapterTargetResolver
{
    private readonly NovelAgentDbContext _db;

    public ChapterTargetResolver(NovelAgentDbContext db)
    {
        _db = db;
    }

    public async Task<string> ResolveAsync(
        string userId,
        string projectId,
        int chapterNumber,
        CancellationToken cancellationToken = default)
    {
        var ownsProject = await _db.NovelProjects.AsNoTracking().AnyAsync(project =>
            project.Id == projectId && project.UserId == userId,
            cancellationToken);
        if (!ownsProject)
            throw new KeyNotFoundException("章节目标项目不存在或不属于当前用户。");

        return await _db.Chapters.AsNoTracking()
            .Where(chapter => chapter.ProjectId == projectId && chapter.ChapterNumber == chapterNumber)
            .Select(chapter => chapter.Id)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"第 {chapterNumber} 章目标不存在。");
    }
}

public sealed partial class KnowledgeRetrievalKernel : IKernel
{
    private readonly IHybridRetriever _retriever;
    private readonly IChapterTargetResolver _targets;

    public KnowledgeRetrievalKernel(IHybridRetriever retriever, IChapterTargetResolver targets)
    {
        _retriever = retriever;
        _targets = targets;
    }

    public string Name => "knowledge_retrieval";

    public async Task<KernelExecutionOutput> ExecuteAsync(
        KernelExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        if (context.Claim.TaskType != "CompileChapterContext")
            throw new InvalidOperationException($"知识检索内核不支持任务：{context.Claim.TaskType}");
        var match = ChapterContextTaskPattern().Match(context.Claim.TaskId);
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out var chapterNumber))
            throw new InvalidOperationException("章节上下文任务 ID 不符合编译器协议。");
        var chapterPlan = context.Inputs.SingleOrDefault(input => input.ArtifactType == "ChapterPlan")
            ?? throw new InvalidOperationException("章节上下文编译缺少 ChapterPlan。");
        var priorSummaries = context.Inputs
            .Where(input => input.ArtifactType == "ContinuitySummary")
            .Select(input => input.ContentJson)
            .ToArray();
        var chapterId = await _targets.ResolveAsync(
            context.Claim.UserId,
            context.Claim.ProjectId,
            chapterNumber,
            cancellationToken);
        var evidence = await _retriever.RetrieveAsync(new RagRetrievalRequest(
            context.Claim.UserId,
            context.Claim.ProjectId,
            chapterPlan.ContentJson,
            string.Join("\n", priorSummaries),
            12,
            new RagSnapshotScope(
                context.GoalSnapshot.CanonVersion,
                ParseKnowledgeVersion(context.GoalSnapshot.KnowledgeVersion, context.Claim.UserId),
                context.GoalSnapshot.CreatedAt)), cancellationToken);
        var package = new ChapterContextPackageSummary
        {
            ChapterId = chapterId,
            Status = "compiled",
            PackageId = $"context:{context.Claim.TaskId}",
            ChapterBlueprints = [chapterPlan.ContentJson],
            PreviousSummaries = priorSummaries.ToList(),
            LongDistanceRecall = evidence.Items.Select(item => item.Content).ToList(),
            RagQueries = evidence.Plan.SemanticQueries.ToList(),
            HardContinuityFacts = evidence.Items
                .Where(item => item.SourceType is "continuity_summary" or "canon_change")
                .Select(item => item.Content)
                .ToList(),
            BuiltAt = DateTime.UtcNow
        };
        var run = new NovelAgentRun
        {
            RunId = context.Claim.TaskId,
            UserGoal = chapterPlan.ContentJson,
            Intent = NovelAgentIntent.GenerateChapter,
            Status = NovelAgentRunStatus.Retrieving,
            TargetChapterId = chapterId,
            ContextPackage = package
        };
        var contractJson = JsonSerializer.Serialize(new TianmingChapterContextInput(run, package));
        var evidenceJson = JsonSerializer.Serialize(evidence);
        var artifacts = new[]
        {
            Proposal("ChapterContextContract", contractJson),
            Proposal("EvidenceBundle", evidenceJson)
        };
        var domainEvent = new DomainEventProposal(
            "candidate_chapter",
            chapterId,
            1,
            "ChapterContextCompiled",
            ["@artifact:0", "@artifact:1"],
            context.Inputs.Select(input => input.Id).ToArray(),
            JsonSerializer.Serialize(new { chapterId, chapterNumber }),
            $"{context.Claim.TaskId}:context:{Hash(contractJson)}");
        return new KernelExecutionOutput(artifacts, [domainEvent]);
    }

    private static KernelArtifactProposal Proposal(string type, string json) =>
        new(type, 1, json, Hash(json), "agent", false);

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static long ParseKnowledgeVersion(string value, string userId)
    {
        if (value == "knowledge:empty")
            return 0;
        var prefix = $"knowledge:{userId}:v";
        if (!value.StartsWith(prefix, StringComparison.Ordinal) ||
            !long.TryParse(value[prefix.Length..], out var version) ||
            version < 0)
        {
            throw new InvalidOperationException("Goal 知识快照版本不符合冻结协议。");
        }
        return version;
    }

    [GeneratedRegex(@"(?:^|:)chapter-(\d+)-context$", RegexOptions.CultureInvariant)]
    private static partial Regex ChapterContextTaskPattern();
}
