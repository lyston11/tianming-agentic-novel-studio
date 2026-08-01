using TM.Web.NovelAgentWeb.Data.Entities;

namespace TM.Web.NovelAgentWeb.Services.Canon;

public interface ICanonBranchService
{
    Task<CanonBranch> CreateAsync(
        string goalId,
        int startChapterNumber,
        int endChapterNumber,
        CancellationToken cancellationToken = default);

    Task<CandidateChapter> AddCandidateAsync(
        string branchId,
        string chapterId,
        int chapterNumber,
        string artifactId,
        string? dependsOnCandidateChapterId,
        string authorship,
        bool isProtected,
        CancellationToken cancellationToken = default);

    Task<CandidateAcceptance> AcceptAsync(
        string candidateChapterId,
        int candidateVersion,
        CancellationToken cancellationToken = default);

    Task<CandidateAcceptance> AcceptAsync(
        string candidateChapterId,
        int candidateVersion,
        string actor,
        CancellationToken cancellationToken = default);
}

public interface IPrefixMergeService
{
    Task<BranchMergeRecord> MergeAcceptedPrefixAsync(
        string branchId,
        CancellationToken cancellationToken = default);
}

public sealed class CanonMergeConflictException : InvalidOperationException
{
    public CanonMergeConflictException(
        string goalBaselineVersion,
        string currentCanonVersion,
        IReadOnlyList<int> conflictingChapterNumbers)
        : base("正式正史已发生与候选前缀冲突的变化，需要用户决定后再合并。")
    {
        GoalBaselineVersion = goalBaselineVersion;
        CurrentCanonVersion = currentCanonVersion;
        ConflictingChapterNumbers = conflictingChapterNumbers;
    }

    public string GoalBaselineVersion { get; }
    public string CurrentCanonVersion { get; }
    public IReadOnlyList<int> ConflictingChapterNumbers { get; }
}
