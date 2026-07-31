using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using Xunit;

namespace Tests.Unit.Data;

public sealed class TargetArchitectureModelTests
{
    private static readonly Type[] UserScopedExecutionEntities =
    [
        typeof(CreativeGoal),
        typeof(GoalRevision),
        typeof(TaskGraphVersion),
        typeof(KernelTask),
        typeof(KernelArtifact),
        typeof(DomainEvent),
        typeof(ModelExecution),
        typeof(CanonBranch),
        typeof(CandidateChapter),
        typeof(CandidateAcceptance),
        typeof(BranchMergeRecord),
        typeof(ContinuitySummary),
        typeof(CanonChange),
        typeof(GoalContextSnapshot),
        typeof(ModelKernelConfiguration),
        typeof(ReworkIntent),
        typeof(KnowledgeDocumentBlob),
        typeof(KnowledgeSection),
        typeof(KnowledgeChunk),
        typeof(KnowledgeEntry),
        typeof(StyleProfile),
        typeof(KnowledgeCitation),
        typeof(VectorIndexRecord)
    ];

    [Fact]
    public void TargetArchitectureEntities_AreMappedWithUserAndProjectScope()
    {
        using var db = CreateDbContext();

        foreach (var clrType in UserScopedExecutionEntities)
        {
            var entity = db.Model.FindEntityType(clrType);

            Assert.NotNull(entity);
            Assert.NotNull(entity!.FindProperty("UserId"));
            Assert.NotNull(entity.FindProperty("ProjectId"));
        }
    }

    [Fact]
    public void CreativeGoal_UsesOptimisticConcurrencyAndScopedIdempotency()
    {
        using var db = CreateDbContext();
        var entity = RequireEntity<CreativeGoal>(db);

        Assert.True(entity.FindProperty(nameof(CreativeGoal.AggregateVersion))!.IsConcurrencyToken);
        Assert.Equal(18, entity.FindProperty(nameof(CreativeGoal.TotalCostLimit))!.GetPrecision());
        Assert.Equal(6, entity.FindProperty(nameof(CreativeGoal.TotalCostLimit))!.GetScale());
        Assert.Contains(entity.GetIndexes(), index =>
            index.IsUnique &&
            PropertyNames(index).SequenceEqual(
                [nameof(CreativeGoal.UserId), nameof(CreativeGoal.ProjectId), nameof(CreativeGoal.IdempotencyKey)]));
    }

    [Fact]
    public void KernelTask_ContainsDurableClaimAndIdempotencyFields()
    {
        using var db = CreateDbContext();
        var entity = RequireEntity<KernelTask>(db);

        Assert.NotNull(entity.FindProperty(nameof(KernelTask.LeaseOwner)));
        Assert.NotNull(entity.FindProperty(nameof(KernelTask.LeaseExpiresAt)));
        Assert.NotNull(entity.FindProperty(nameof(KernelTask.Attempt)));
        Assert.Contains(entity.GetIndexes(), index =>
            index.IsUnique &&
            PropertyNames(index).SequenceEqual([nameof(KernelTask.UserId), nameof(KernelTask.IdempotencyKey)]));
    }

    [Fact]
    public void ArtifactAndEvent_AreVersionedAndIdempotent()
    {
        using var db = CreateDbContext();
        var artifact = RequireEntity<KernelArtifact>(db);
        var domainEvent = RequireEntity<DomainEvent>(db);

        Assert.NotNull(artifact.FindProperty(nameof(KernelArtifact.ContentHash)));
        Assert.NotNull(artifact.FindProperty(nameof(KernelArtifact.SchemaVersion)));
        Assert.NotNull(domainEvent.FindProperty(nameof(DomainEvent.AggregateVersion)));
        Assert.Contains(domainEvent.GetIndexes(), index =>
            index.IsUnique &&
            PropertyNames(index).SequenceEqual([nameof(DomainEvent.UserId), nameof(DomainEvent.IdempotencyKey)]));
    }

    [Fact]
    public void CandidateChapter_TracksHumanAuthorshipAndProtection()
    {
        using var db = CreateDbContext();
        var entity = RequireEntity<CandidateChapter>(db);

        Assert.NotNull(entity.FindProperty(nameof(CandidateChapter.Authorship)));
        Assert.NotNull(entity.FindProperty(nameof(CandidateChapter.IsProtected)));
        Assert.Contains(entity.GetIndexes(), index =>
            index.IsUnique &&
            PropertyNames(index).SequenceEqual(
                [nameof(CandidateChapter.UserId), nameof(CandidateChapter.BranchId), nameof(CandidateChapter.ChapterNumber), nameof(CandidateChapter.Version)]));
    }

    [Fact]
    public void ReworkIntent_UsesAttemptCountAsConcurrencyToken()
    {
        using var db = CreateDbContext();
        var entity = RequireEntity<ReworkIntent>(db);

        Assert.True(entity.FindProperty(nameof(ReworkIntent.AttemptCount))!.IsConcurrencyToken);
        Assert.NotNull(entity.FindProperty(nameof(ReworkIntent.MustNotChangeJson)));
        Assert.NotNull(entity.FindProperty(nameof(ReworkIntent.AcceptanceCriteriaJson)));
    }

    [Fact]
    public void VolumeArc_KeyEventsPreservesLongLegacyPlanningEvidence()
    {
        using var db = CreateDbContext();
        var entity = RequireEntity<VolumeArc>(db);
        var keyEvents = entity.FindProperty(nameof(VolumeArc.KeyEvents));

        Assert.NotNull(keyEvents);
        Assert.Null(keyEvents!.GetMaxLength());
        Assert.Equal("text", keyEvents.FindAnnotation(RelationalAnnotationNames.ColumnType)?.Value);
    }

    private static NovelAgentDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NovelAgentDbContext(options);
    }

    private static IEntityType RequireEntity<TEntity>(NovelAgentDbContext db) where TEntity : class =>
        db.Model.FindEntityType(typeof(TEntity))
        ?? throw new InvalidOperationException($"{typeof(TEntity).Name} is not mapped.");

    private static IReadOnlyList<string> PropertyNames(IReadOnlyIndex index) =>
        index.Properties.Select(property => property.Name).ToArray();
}
