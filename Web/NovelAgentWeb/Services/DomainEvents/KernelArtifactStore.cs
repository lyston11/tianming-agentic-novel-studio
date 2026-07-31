using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Goals;

namespace TM.Web.NovelAgentWeb.Services.DomainEvents;

public sealed class KernelArtifactStore : IKernelArtifactStore
{
    private readonly NovelAgentDbContext _db;

    public KernelArtifactStore(NovelAgentDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<string>> AddOrReuseAsync(
        KernelTaskClaim claim,
        IReadOnlyList<KernelArtifactProposal> proposals,
        CancellationToken cancellationToken = default)
    {
        var ids = new List<string>(proposals.Count);
        foreach (var proposal in proposals)
        {
            var existingId = await _db.KernelArtifacts
                .AsNoTracking()
                .Where(artifact =>
                    artifact.UserId == claim.UserId &&
                    artifact.TaskId == claim.TaskId &&
                    artifact.ArtifactType == proposal.ArtifactType &&
                    artifact.SchemaVersion == proposal.SchemaVersion &&
                    artifact.ContentHash == proposal.ContentHash)
                .Select(artifact => artifact.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (existingId != null)
            {
                ids.Add(existingId);
                continue;
            }

            var artifact = new KernelArtifact
            {
                Id = Guid.NewGuid().ToString("N"),
                UserId = claim.UserId,
                ProjectId = claim.ProjectId,
                GoalId = claim.GoalId,
                TaskId = claim.TaskId,
                BranchId = claim.BranchId,
                ArtifactType = proposal.ArtifactType,
                SchemaVersion = proposal.SchemaVersion,
                ContentJson = proposal.ContentJson,
                ContentHash = proposal.ContentHash,
                Status = "adopted",
                Authorship = proposal.Authorship,
                IsProtected = proposal.IsProtected,
                CreatedAt = DateTime.UtcNow
            };
            _db.KernelArtifacts.Add(artifact);
            ids.Add(artifact.Id);
        }

        return ids;
    }
}
