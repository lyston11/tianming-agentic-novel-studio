using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TM.Services.Framework.AI.NovelAgent.Models
{
    public sealed class ChapterContextPackageSummary
    {
        [JsonPropertyName("chapterId")]
        public string ChapterId { get; set; } = string.Empty;

        [JsonPropertyName("status")]
        public string Status { get; set; } = "pending";

        [JsonPropertyName("packageId")]
        public string PackageId { get; set; } = string.Empty;

        [JsonPropertyName("worldRules")]
        public List<string> WorldRules { get; set; } = new();

        [JsonPropertyName("characterStates")]
        public List<string> CharacterStates { get; set; } = new();

        [JsonPropertyName("activeConflicts")]
        public List<string> ActiveConflicts { get; set; } = new();

        [JsonPropertyName("activeForeshadowing")]
        public List<string> ActiveForeshadowing { get; set; } = new();

        [JsonPropertyName("chapterBlueprints")]
        public List<string> ChapterBlueprints { get; set; } = new();

        [JsonPropertyName("previousSummaries")]
        public List<string> PreviousSummaries { get; set; } = new();

        [JsonPropertyName("longDistanceRecall")]
        public List<string> LongDistanceRecall { get; set; } = new();

        [JsonPropertyName("ragQueries")]
        public List<string> RagQueries { get; set; } = new();

        [JsonPropertyName("hardContinuityFacts")]
        public List<string> HardContinuityFacts { get; set; } = new();

        [JsonPropertyName("knowledgeBindings")]
        public List<BoundKnowledgeSnapshot> KnowledgeBindings { get; set; } = new();

        [JsonPropertyName("designRules")]
        public List<DesignRuleSnapshot> DesignRules { get; set; } = new();

        [JsonPropertyName("persistedBlueprint")]
        public PersistedChapterBlueprintSnapshot? PersistedBlueprint { get; set; }

        [JsonPropertyName("acceptedCreativeIntents")]
        public List<AcceptedCreativeIntentSnapshot> AcceptedCreativeIntents { get; set; } = new();

        [JsonPropertyName("sourceRevisionPlans")]
        public List<RevisionPlanSnapshot> SourceRevisionPlans { get; set; } = new();

        [JsonPropertyName("warnings")]
        public List<string> Warnings { get; set; } = new();

        [JsonPropertyName("builtAt")]
        public DateTime BuiltAt { get; set; } = DateTime.Now;
    }

    public sealed class AcceptedCreativeIntentSnapshot
    {
        [JsonPropertyName("intentId")]
        public string IntentId { get; set; } = string.Empty;

        [JsonPropertyName("normalizedIntent")]
        public string NormalizedIntent { get; set; } = string.Empty;

        [JsonPropertyName("targetScope")]
        public string TargetScope { get; set; } = "project";

        [JsonPropertyName("targetChapterId")]
        public string TargetChapterId { get; set; } = string.Empty;

        [JsonPropertyName("targetVolumeId")]
        public string TargetVolumeId { get; set; } = string.Empty;

        [JsonPropertyName("targetCharacterName")]
        public string TargetCharacterName { get; set; } = string.Empty;

        [JsonPropertyName("impactLevel")]
        public string ImpactLevel { get; set; } = string.Empty;

        [JsonPropertyName("source")]
        public string Source { get; set; } = string.Empty;

        [JsonPropertyName("decisionReason")]
        public string DecisionReason { get; set; } = string.Empty;

        [JsonPropertyName("createdAt")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }

    public sealed class RevisionPlanSnapshot
    {
        [JsonPropertyName("revisionPlanId")]
        public string RevisionPlanId { get; set; } = string.Empty;

        [JsonPropertyName("planType")]
        public string PlanType { get; set; } = string.Empty;

        [JsonPropertyName("targetScope")]
        public string TargetScope { get; set; } = string.Empty;

        [JsonPropertyName("targetChapterId")]
        public string TargetChapterId { get; set; } = string.Empty;

        [JsonPropertyName("targetChapterLogicalId")]
        public string TargetChapterLogicalId { get; set; } = string.Empty;

        [JsonPropertyName("targetChapterDisplayName")]
        public string TargetChapterDisplayName { get; set; } = string.Empty;

        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("requirementsJson")]
        public string RequirementsJson { get; set; } = "[]";

        [JsonPropertyName("continuityRequirementsJson")]
        public string ContinuityRequirementsJson { get; set; } = "[]";

        [JsonPropertyName("affectedChapterIdsJson")]
        public string AffectedChapterIdsJson { get; set; } = "[]";

        [JsonPropertyName("invalidatedPackageIdsJson")]
        public string InvalidatedPackageIdsJson { get; set; } = "[]";

        [JsonPropertyName("riskLevel")]
        public string RiskLevel { get; set; } = string.Empty;

        [JsonPropertyName("recommendation")]
        public string Recommendation { get; set; } = string.Empty;
    }

    /// <summary>
    /// Snapshot of a design rule applied to a chapter production package.
    /// Aggregated from KnowledgeClassification by DesignRuleAggregationService.
    /// </summary>
    public sealed class DesignRuleSnapshot
    {
        [JsonPropertyName("ruleId")]
        public string RuleId { get; set; } = string.Empty;

        [JsonPropertyName("ruleType")]
        public string RuleType { get; set; } = string.Empty;

        [JsonPropertyName("ruleContent")]
        public string RuleContent { get; set; } = string.Empty;

        [JsonPropertyName("constraintLevel")]
        public string ConstraintLevel { get; set; } = "Reference";

        [JsonPropertyName("scope")]
        public string Scope { get; set; } = "ProjectWide";

        [JsonPropertyName("scopeTarget")]
        public string? ScopeTarget { get; set; }

        [JsonPropertyName("priority")]
        public int Priority { get; set; } = 50;

        [JsonPropertyName("version")]
        public int Version { get; set; } = 1;

        [JsonPropertyName("sourceKnowledgeIds")]
        public List<string> SourceKnowledgeIds { get; set; } = new();
    }

    /// <summary>
    /// Snapshot of a persisted ChapterBlueprint loaded from chapter_blueprints table.
    /// Replaces ad-hoc List&lt;string&gt; blueprint with structured data.
    /// </summary>
    public sealed class PersistedChapterBlueprintSnapshot
    {
        [JsonPropertyName("blueprintId")]
        public string BlueprintId { get; set; } = string.Empty;

        [JsonPropertyName("chapterId")]
        public string ChapterId { get; set; } = string.Empty;

        [JsonPropertyName("chapterIndex")]
        public int ChapterIndex { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("intent")]
        public string Intent { get; set; } = string.Empty;

        [JsonPropertyName("keyEvents")]
        public List<string> KeyEvents { get; set; } = new();

        [JsonPropertyName("characters")]
        public List<string> Characters { get; set; } = new();

        [JsonPropertyName("conflictNote")]
        public string? ConflictNote { get; set; }

        [JsonPropertyName("endingNote")]
        public string? EndingNote { get; set; }

        [JsonPropertyName("requiredKnowledgeIds")]
        public List<string> RequiredKnowledgeIds { get; set; } = new();

        [JsonPropertyName("appliedDesignRuleIds")]
        public List<string> AppliedDesignRuleIds { get; set; } = new();

        [JsonPropertyName("dependencyChapterIds")]
        public List<string> DependencyChapterIds { get; set; } = new();

        [JsonPropertyName("version")]
        public int Version { get; set; } = 1;

        [JsonPropertyName("status")]
        public string Status { get; set; } = "Draft";

        [JsonPropertyName("targetWordCount")]
        public int? TargetWordCount { get; set; }
    }

    public sealed class BoundKnowledgeSnapshot
    {
        [JsonPropertyName("knowledgeId")]
        public string KnowledgeId { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("entryType")]
        public string EntryType { get; set; } = string.Empty;

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;

        [JsonPropertyName("tags")]
        public List<string> Tags { get; set; } = new();

        [JsonPropertyName("weight")]
        public int Weight { get; set; }

        [JsonPropertyName("sourceProjectId")]
        public string SourceProjectId { get; set; } = string.Empty;

        [JsonPropertyName("projectUsageStatus")]
        public string ProjectUsageStatus { get; set; } = string.Empty;

        [JsonPropertyName("projectUsageCount")]
        public int ProjectUsageCount { get; set; }

        [JsonPropertyName("sourceSessionId")]
        public string SourceSessionId { get; set; } = string.Empty;

        [JsonPropertyName("sourceRunId")]
        public string SourceRunId { get; set; } = string.Empty;

        [JsonPropertyName("note")]
        public string Note { get; set; } = string.Empty;

        [JsonPropertyName("role")]
        public string Role { get; set; } = "Reference";

        [JsonPropertyName("scope")]
        public string Scope { get; set; } = "ProjectWide";

        [JsonPropertyName("priority")]
        public int Priority { get; set; } = 50;

        [JsonPropertyName("constraintLevel")]
        public string ConstraintLevel { get; set; } = "Reference";

        [JsonPropertyName("packagePolicy")]
        public string PackagePolicy { get; set; } = "RelevantOnly";

        [JsonPropertyName("boundVersion")]
        public string BoundVersion { get; set; } = string.Empty;

        [JsonPropertyName("usedByChapters")]
        public List<string> UsedByChapters { get; set; } = new();

        [JsonPropertyName("classificationId")]
        public string ClassificationId { get; set; } = string.Empty;

        [JsonPropertyName("classificationModel")]
        public string ClassificationModel { get; set; } = string.Empty;

        [JsonPropertyName("classificationRule")]
        public string ClassificationRule { get; set; } = string.Empty;

        [JsonPropertyName("targetEntities")]
        public List<string> TargetEntities { get; set; } = new();

        [JsonPropertyName("shouldEnterGate")]
        public bool ShouldEnterGate { get; set; }

        [JsonPropertyName("shouldEnterBlueprint")]
        public bool ShouldEnterBlueprint { get; set; }

        [JsonPropertyName("shouldEnterFactSnapshot")]
        public bool ShouldEnterFactSnapshot { get; set; }

        [JsonPropertyName("classificationConfidence")]
        public double ClassificationConfidence { get; set; }
    }

    public sealed class ChapterDirective
    {
        [JsonPropertyName("chapterId")]
        public string ChapterId { get; set; } = string.Empty;

        [JsonPropertyName("targetTitle")]
        public string TargetTitle { get; set; } = string.Empty;

        [JsonPropertyName("userGoal")]
        public string UserGoal { get; set; } = string.Empty;

        [JsonPropertyName("chapterObjective")]
        public string ChapterObjective { get; set; } = string.Empty;

        [JsonPropertyName("protagonistAnchor")]
        public string ProtagonistAnchor { get; set; } = string.Empty;

        [JsonPropertyName("systemAnchor")]
        public string SystemAnchor { get; set; } = string.Empty;

        [JsonPropertyName("mustCarry")]
        public List<string> MustCarry { get; set; } = new();

        [JsonPropertyName("sceneBeats")]
        public List<string> SceneBeats { get; set; } = new();

        [JsonPropertyName("creativeRequirements")]
        public List<string> CreativeRequirements { get; set; } = new();

        [JsonPropertyName("editorialRevisionRequirements")]
        public List<string> EditorialRevisionRequirements { get; set; } = new();

        [JsonPropertyName("knowledgeBoundaries")]
        public List<string> KnowledgeBoundaries { get; set; } = new();

        [JsonPropertyName("styleGuides")]
        public List<string> StyleGuides { get; set; } = new();

        [JsonPropertyName("worldKnowledge")]
        public List<string> WorldKnowledge { get; set; } = new();

        [JsonPropertyName("characterKnowledge")]
        public List<string> CharacterKnowledge { get; set; } = new();

        [JsonPropertyName("sceneMaterials")]
        public List<string> SceneMaterials { get; set; } = new();

        [JsonPropertyName("continuityReferences")]
        public List<string> ContinuityReferences { get; set; } = new();

        [JsonPropertyName("acceptanceRules")]
        public List<string> AcceptanceRules { get; set; } = new();
    }

    public sealed class ChapterContinuityFacts
    {
        [JsonPropertyName("chapterId")]
        public string ChapterId { get; set; } = string.Empty;

        [JsonPropertyName("chapterTitle")]
        public string ChapterTitle { get; set; } = string.Empty;

        [JsonPropertyName("protagonistName")]
        public string ProtagonistName { get; set; } = string.Empty;

        [JsonPropertyName("protagonistIdentity")]
        public string ProtagonistIdentity { get; set; } = string.Empty;

        [JsonPropertyName("protagonistStatus")]
        public string ProtagonistStatus { get; set; } = string.Empty;

        [JsonPropertyName("currentLocation")]
        public string CurrentLocation { get; set; } = string.Empty;

        [JsonPropertyName("systemState")]
        public string SystemState { get; set; } = string.Empty;

        [JsonPropertyName("equipmentState")]
        public string EquipmentState { get; set; } = string.Empty;

        [JsonPropertyName("keyEvents")]
        public List<string> KeyEvents { get; set; } = new();

        [JsonPropertyName("endingState")]
        public string EndingState { get; set; } = string.Empty;

        [JsonPropertyName("nextChapterMustCarry")]
        public List<string> NextChapterMustCarry { get; set; } = new();

        [JsonPropertyName("sourceRunId")]
        public string SourceRunId { get; set; } = string.Empty;

        [JsonPropertyName("extractedAt")]
        public DateTime ExtractedAt { get; set; } = DateTime.Now;
    }

    public sealed class GenerationGateReport
    {
        [JsonPropertyName("status")]
        public string Status { get; set; } = "pending";

        [JsonPropertyName("protocolPassed")]
        public bool ProtocolPassed { get; set; }

        [JsonPropertyName("changesDetected")]
        public bool ChangesDetected { get; set; }

        [JsonPropertyName("factSnapshotPassed")]
        public bool FactSnapshotPassed { get; set; }

        [JsonPropertyName("blueprintPassed")]
        public bool BlueprintPassed { get; set; }

        [JsonPropertyName("ragPassed")]
        public bool RagPassed { get; set; }

        [JsonPropertyName("issues")]
        public List<string> Issues { get; set; } = new();

        [JsonPropertyName("repairHints")]
        public List<string> RepairHints { get; set; } = new();

        [JsonPropertyName("knowledgeConstraintChecks")]
        public List<KnowledgeConstraintCheck> KnowledgeConstraintChecks { get; set; } = new();

        [JsonPropertyName("validatedAt")]
        public DateTime ValidatedAt { get; set; } = DateTime.Now;
    }

    public sealed class KnowledgeConstraintCheck
    {
        [JsonPropertyName("knowledgeId")]
        public string KnowledgeId { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("entryType")]
        public string EntryType { get; set; } = string.Empty;

        [JsonPropertyName("subject")]
        public string Subject { get; set; } = string.Empty;

        [JsonPropertyName("constraintLevel")]
        public string ConstraintLevel { get; set; } = string.Empty;

        [JsonPropertyName("packagePolicy")]
        public string PackagePolicy { get; set; } = string.Empty;

        [JsonPropertyName("status")]
        public string Status { get; set; } = "unknown";

        [JsonPropertyName("allowedTerms")]
        public List<string> AllowedTerms { get; set; } = new();

        [JsonPropertyName("forbiddenTerms")]
        public List<string> ForbiddenTerms { get; set; } = new();

        [JsonPropertyName("violations")]
        public List<string> Violations { get; set; } = new();
    }

    public sealed class ChapterDraftArtifact
    {
        [JsonPropertyName("artifactId")]
        public string ArtifactId { get; set; } = Guid.NewGuid().ToString("N");

        [JsonPropertyName("chapterId")]
        public string ChapterId { get; set; } = string.Empty;

        [JsonPropertyName("status")]
        public string Status { get; set; } = "draft_generated";

        [JsonPropertyName("draftContent")]
        public string DraftContent { get; set; } = string.Empty;

        [JsonPropertyName("committedContent")]
        public string CommittedContent { get; set; } = string.Empty;

        [JsonPropertyName("changesJson")]
        public string ChangesJson { get; set; } = string.Empty;

        [JsonPropertyName("hasChanges")]
        public bool HasChanges { get; set; }

        [JsonPropertyName("repairAttemptCount")]
        public int RepairAttemptCount { get; set; }

        [JsonPropertyName("generatedAt")]
        public DateTime GeneratedAt { get; set; } = DateTime.Now;

        [JsonPropertyName("committedAt")]
        public DateTime? CommittedAt { get; set; }
    }

    public sealed class DependencyImpactReport
    {
        [JsonPropertyName("status")]
        public string Status { get; set; } = "clean";

        [JsonPropertyName("changedModules")]
        public List<string> ChangedModules { get; set; } = new();

        [JsonPropertyName("impactedModules")]
        public List<string> ImpactedModules { get; set; } = new();

        [JsonPropertyName("impactedChapters")]
        public List<string> ImpactedChapters { get; set; } = new();

        [JsonPropertyName("summary")]
        public string Summary { get; set; } = string.Empty;

        [JsonPropertyName("createdAt")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
