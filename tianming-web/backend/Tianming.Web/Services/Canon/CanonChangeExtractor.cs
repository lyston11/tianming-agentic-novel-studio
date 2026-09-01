using System.Text.Json;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;

namespace TM.Web.NovelAgentWeb.Services.Canon;

public sealed class CanonChangeExtractor
{
    private readonly NovelAgentDbContext _db;

    public CanonChangeExtractor(NovelAgentDbContext db)
    {
        _db = db;
    }

    public IReadOnlyList<CanonChange> CreateCandidates(
        CandidateChapter candidate,
        IReadOnlyList<CanonChangeDraft> approvedChanges)
    {
        var changes = approvedChanges.Select(change => new CanonChange
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = candidate.UserId,
            ProjectId = candidate.ProjectId,
            ChapterId = candidate.ChapterId,
            ChapterVersionId = $"candidate:{candidate.Id}",
            BranchId = candidate.BranchId,
            ChangeType = change.ChangeType,
            Subject = change.Subject,
            ChangeJson = JsonSerializer.Serialize(new
            {
                description = change.Description,
                sourcePriority = "chapter_body"
            }),
            EvidenceRefsJson = JsonSerializer.Serialize(new[] { change.Evidence }),
            Status = "candidate",
            CreatedAt = DateTime.UtcNow
        }).ToArray();
        _db.CanonChanges.AddRange(changes);
        return changes;
    }
}
