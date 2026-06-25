using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TM.Services.Framework.AI.NovelAgent.Models
{
    public sealed class StoryFoundationRequest
    {
        public string UserSeed { get; set; } = string.Empty;
        public string Genre { get; set; } = string.Empty;
        public string SubGenre { get; set; } = string.Empty;
        public List<string> CandidateDirections { get; set; } = new();
        public List<string> ForbiddenDirections { get; set; } = new();
    }

    public sealed class ChapterCreativeRequest
    {
        public string UserGoal { get; set; } = string.Empty;
        public string ChapterId { get; set; } = string.Empty;
        public StoryCreativeConstitution? Constitution { get; set; }
        public VolumeArcPlan? VolumeArc { get; set; }
        public VolumeChapterBeat? VolumeBeat { get; set; }
        public CreativeKnowledgeRetrievalResult? CreativeKnowledge { get; set; }
        public List<string> ActiveConflicts { get; set; } = new();
        public List<string> ActiveForeshadowing { get; set; } = new();
        public List<string> CharacterStates { get; set; } = new();
        public List<string> UsedPlotPatterns { get; set; } = new();
        public List<string> SimilarContentFragments { get; set; } = new();
        public List<string> CandidateDirections { get; set; } = new();
        public List<string> ForbiddenDirections { get; set; } = new();
    }

    public sealed class MacroStoryConceptCandidate
    {
        [JsonPropertyName("candidateId")]
        public string CandidateId { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("coreHook")]
        public string CoreHook { get; set; } = string.Empty;

        [JsonPropertyName("worldCoreRule")]
        public string WorldCoreRule { get; set; } = string.Empty;

        [JsonPropertyName("mainConflictEngine")]
        public string MainConflictEngine { get; set; } = string.Empty;

        [JsonPropertyName("protagonistEngine")]
        public string ProtagonistEngine { get; set; } = string.Empty;

        [JsonPropertyName("worldbuildingBlueprint")]
        public string WorldbuildingBlueprint { get; set; } = string.Empty;

        [JsonPropertyName("progressionSystem")]
        public string ProgressionSystem { get; set; } = string.Empty;

        [JsonPropertyName("protagonistProfile")]
        public string ProtagonistProfile { get; set; } = string.Empty;

        [JsonPropertyName("pleasureLoop")]
        public string PleasureLoop { get; set; } = string.Empty;

        [JsonPropertyName("firstThreeVolumes")]
        public List<string> FirstThreeVolumes { get; set; } = new();

        [JsonPropertyName("keyCharacters")]
        public List<string> KeyCharacters { get; set; } = new();

        [JsonPropertyName("depthLayer")]
        public string DepthLayer { get; set; } = string.Empty;

        [JsonPropertyName("noveltyScore")]
        public int NoveltyScore { get; set; }

        [JsonPropertyName("sustainabilityScore")]
        public int SustainabilityScore { get; set; }

        [JsonPropertyName("typeMatchScore")]
        public int TypeMatchScore { get; set; }

        [JsonPropertyName("risks")]
        public List<string> Risks { get; set; } = new();
    }

    public sealed class ChapterCreativeBrief
    {
        [JsonPropertyName("chapterId")]
        public string ChapterId { get; set; } = string.Empty;

        [JsonPropertyName("coreIdea")]
        public string CoreIdea { get; set; } = string.Empty;

        [JsonPropertyName("conflictMove")]
        public string ConflictMove { get; set; } = string.Empty;

        [JsonPropertyName("characterChoice")]
        public string CharacterChoice { get; set; } = string.Empty;

        [JsonPropertyName("costOrConsequence")]
        public string CostOrConsequence { get; set; } = string.Empty;

        [JsonPropertyName("foreshadowingAction")]
        public string ForeshadowingAction { get; set; } = string.Empty;

        [JsonPropertyName("worldbuildingGap")]
        public string WorldbuildingGap { get; set; } = string.Empty;

        [JsonPropertyName("forbiddenPatterns")]
        public List<string> ForbiddenPatterns { get; set; } = new();

        [JsonPropertyName("knowledgeNotes")]
        public List<string> KnowledgeNotes { get; set; } = new();

        [JsonPropertyName("antiTropeStrategies")]
        public List<string> AntiTropeStrategies { get; set; } = new();

        [JsonPropertyName("patternWarnings")]
        public List<string> PatternWarnings { get; set; } = new();

        [JsonPropertyName("similarContentWarnings")]
        public List<string> SimilarContentWarnings { get; set; } = new();

        [JsonPropertyName("volumeArcNotes")]
        public List<string> VolumeArcNotes { get; set; } = new();

        [JsonPropertyName("volumeBeatRole")]
        public string VolumeBeatRole { get; set; } = string.Empty;

        [JsonPropertyName("recommendedCandidateTitle")]
        public string RecommendedCandidateTitle { get; set; } = string.Empty;

        [JsonPropertyName("recommendationReason")]
        public string RecommendationReason { get; set; } = string.Empty;

        [JsonPropertyName("selectedCandidateTitle")]
        public string SelectedCandidateTitle { get; set; } = string.Empty;

        [JsonPropertyName("selectionMode")]
        public string SelectionMode { get; set; } = string.Empty;

        [JsonPropertyName("selectionRationale")]
        public string SelectionRationale { get; set; } = string.Empty;

        [JsonPropertyName("candidates")]
        public List<PlotCandidate> Candidates { get; set; } = new();
    }

    public sealed class PlotCandidate
    {
        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("coreTwist")]
        public string CoreTwist { get; set; } = string.Empty;

        [JsonPropertyName("conflictMove")]
        public string ConflictMove { get; set; } = string.Empty;

        [JsonPropertyName("characterChoice")]
        public string CharacterChoice { get; set; } = string.Empty;

        [JsonPropertyName("costOrConsequence")]
        public string CostOrConsequence { get; set; } = string.Empty;

        [JsonPropertyName("noveltyScore")]
        public int NoveltyScore { get; set; }

        [JsonPropertyName("consistencyScore")]
        public int ConsistencyScore { get; set; }

        [JsonPropertyName("dramaScore")]
        public int DramaScore { get; set; }

        [JsonPropertyName("typeMatchScore")]
        public int TypeMatchScore { get; set; }

        [JsonPropertyName("clicheRisk")]
        public int ClicheRisk { get; set; }

        [JsonPropertyName("totalScore")]
        public int TotalScore { get; set; }

        [JsonPropertyName("recommendationReason")]
        public string RecommendationReason { get; set; } = string.Empty;

        [JsonPropertyName("knowledgeSupport")]
        public List<string> KnowledgeSupport { get; set; } = new();

        [JsonPropertyName("antiTropePlan")]
        public string AntiTropePlan { get; set; } = string.Empty;

        [JsonPropertyName("risks")]
        public List<string> Risks { get; set; } = new();
    }

    public sealed class ChapterCandidateSelectionResult
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("requiresConfirmation")]
        public bool RequiresConfirmation { get; set; }

        [JsonPropertyName("riskLevel")]
        public NovelToolRiskLevel RiskLevel { get; set; } = NovelToolRiskLevel.Medium;

        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        [JsonPropertyName("run")]
        public NovelAgentRun? Run { get; set; }
    }
}
