namespace TM.Web.NovelAgentWeb.Services.Rag;

public sealed class ReciprocalRankFusion
{
    private const double RankConstant = 60d;
    private const double ScoreTieBreakWeight = 0.000001d;

    public IReadOnlyList<FusedRetrievalCandidate> Fuse(
        IEnumerable<RetrievalCandidate> candidates,
        int limit)
    {
        if (limit <= 0)
            return [];

        return candidates
            .Where(candidate =>
                !string.IsNullOrWhiteSpace(candidate.SourceType) &&
                !string.IsNullOrWhiteSpace(candidate.SourceId) &&
                !string.IsNullOrWhiteSpace(candidate.Channel) &&
                candidate.Rank > 0)
            .GroupBy(candidate => (candidate.SourceType, candidate.SourceId))
            .Select(group =>
            {
                var ordered = group
                    .GroupBy(candidate => candidate.Channel, StringComparer.Ordinal)
                    .Select(channel => channel
                        .OrderBy(candidate => candidate.Rank)
                        .ThenByDescending(candidate => candidate.Score)
                        .First())
                    .OrderBy(candidate => candidate.Channel, StringComparer.Ordinal)
                    .ToArray();
                var score = ordered.Sum(candidate =>
                    (1d / (RankConstant + candidate.Rank)) +
                    (Math.Max(0d, candidate.Score) * ScoreTieBreakWeight));
                return new FusedRetrievalCandidate(
                    group.Key.SourceType,
                    group.Key.SourceId,
                    score,
                    ordered.Select(candidate => candidate.Channel)
                        .Distinct(StringComparer.Ordinal)
                        .ToArray(),
                    ordered.Select(candidate => candidate.Metadata).FirstOrDefault(metadata => metadata != null));
            })
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.SourceType, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.SourceId, StringComparer.Ordinal)
            .Take(limit)
            .ToArray();
    }
}
