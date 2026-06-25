using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.Support;
using Xunit;

namespace Tests.Unit.Support;

public sealed class AgentProductionStageTimeoutPolicyTests
{
    [Fact]
    public void CanonicalStages_ContainDocumentedSixteenProductionStagesInOrder()
    {
        var stages = NovelAgentProductionStages.CanonicalStages.ToList();

        Assert.Equal(
            new[]
            {
                NovelAgentProductionStages.ProjectResolved,
                NovelAgentProductionStages.KnowledgeResolved,
                NovelAgentProductionStages.KnowledgeClassified,
                NovelAgentProductionStages.StoryDesignBuilt,
                NovelAgentProductionStages.VolumePlanBuilt,
                NovelAgentProductionStages.ChapterBlueprintBuilt,
                NovelAgentProductionStages.PackageBuilt,
                NovelAgentProductionStages.DraftGenerated,
                NovelAgentProductionStages.ChangesExtracted,
                NovelAgentProductionStages.GateValidated,
                NovelAgentProductionStages.DraftRewritten,
                NovelAgentProductionStages.ReviewCompleted,
                NovelAgentProductionStages.ChapterCommitted,
                NovelAgentProductionStages.FactsPersisted,
                NovelAgentProductionStages.IndexUpdated,
                NovelAgentProductionStages.RunCompleted
            },
            stages.Select(stage => stage.Id));
        Assert.All(stages, stage => Assert.False(string.IsNullOrWhiteSpace(stage.Label)));
        Assert.Equal(1, stages[0].Order);
        Assert.Equal(16, stages[^1].Order);
    }

    [Theory]
    [InlineData(NovelAgentProductionStages.ContextPackage, NovelAgentProductionStages.PackageBuilt)]
    [InlineData(NovelAgentProductionStages.DraftGeneration, NovelAgentProductionStages.DraftGenerated)]
    [InlineData(NovelAgentProductionStages.GateValidation, NovelAgentProductionStages.GateValidated)]
    [InlineData(NovelAgentProductionStages.DraftRepair, NovelAgentProductionStages.DraftRewritten)]
    [InlineData(NovelAgentProductionStages.QualityReview, NovelAgentProductionStages.ReviewCompleted)]
    [InlineData(NovelAgentProductionStages.ChapterCommit, NovelAgentProductionStages.ChapterCommitted)]
    public void ToCanonicalStage_MapsLegacyProductionStages(string legacyStage, string canonicalStage)
    {
        Assert.Equal(canonicalStage, NovelAgentProductionStages.ToCanonicalStage(legacyStage));
    }

    [Fact]
    public void ResolveTimeout_UsesLongerDefaultForDraftGenerationOnly()
    {
        var options = new AgentProductionStageProgressOptions
        {
            StageTimeoutSeconds = 180
        };

        var draft = AgentProductionStageTimeoutPolicy.ResolveTimeout(
            NovelAgentProductionStages.DraftGeneration,
            options);
        var canonicalDraft = AgentProductionStageTimeoutPolicy.ResolveTimeout(
            NovelAgentProductionStages.DraftGenerated,
            options);
        var gate = AgentProductionStageTimeoutPolicy.ResolveTimeout(
            NovelAgentProductionStages.GateValidation,
            options);

        Assert.Equal(TimeSpan.FromSeconds(360), draft);
        Assert.Equal(TimeSpan.FromSeconds(360), canonicalDraft);
        Assert.Equal(TimeSpan.FromSeconds(180), gate);
    }

    [Fact]
    public void ResolveTimeout_AllowsExplicitDraftGenerationOverride()
    {
        var options = new AgentProductionStageProgressOptions
        {
            StageTimeoutSeconds = 120,
            DraftGenerationTimeoutSeconds = 420
        };

        var draft = AgentProductionStageTimeoutPolicy.ResolveTimeout(
            NovelAgentProductionStages.DraftGeneration,
            options);
        var canonicalDraft = AgentProductionStageTimeoutPolicy.ResolveTimeout(
            NovelAgentProductionStages.DraftGenerated,
            options);
        var context = AgentProductionStageTimeoutPolicy.ResolveTimeout(
            NovelAgentProductionStages.ContextPackage,
            options);

        Assert.Equal(TimeSpan.FromSeconds(420), draft);
        Assert.Equal(TimeSpan.FromSeconds(420), canonicalDraft);
        Assert.Equal(TimeSpan.FromSeconds(120), context);
    }
}
