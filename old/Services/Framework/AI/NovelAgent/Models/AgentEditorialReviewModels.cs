using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace TM.Services.Framework.AI.NovelAgent.Models
{
    public sealed class AgentEditorialReviewRequest
    {
        [JsonPropertyName("userId")]
        public string UserId { get; set; } = string.Empty;

        [JsonPropertyName("runId")]
        public string RunId { get; set; } = string.Empty;

        [JsonPropertyName("chapterId")]
        public string ChapterId { get; set; } = string.Empty;

        [JsonPropertyName("userGoal")]
        public string UserGoal { get; set; } = string.Empty;

        [JsonPropertyName("chapterBrief")]
        public ChapterCreativeBrief? ChapterBrief { get; set; }

        [JsonPropertyName("storyConstitution")]
        public StoryCreativeConstitution? StoryConstitution { get; set; }

        [JsonPropertyName("acceptedCreativeIntents")]
        public List<AcceptedCreativeIntentSnapshot> AcceptedCreativeIntents { get; set; } = new();

        [JsonPropertyName("sourceRevisionPlans")]
        public List<RevisionPlanSnapshot> SourceRevisionPlans { get; set; } = new();

        [JsonPropertyName("chapterContent")]
        public string ChapterContent { get; set; } = string.Empty;
    }

    public sealed class AgentEditorialReviewDecision
    {
        [JsonPropertyName("meetsUserIntent")]
        public bool MeetsUserIntent { get; set; }

        [JsonPropertyName("meetsRevisionPlan")]
        public bool MeetsRevisionPlan { get; set; }

        [JsonPropertyName("meetsProjectPromise")]
        public bool MeetsProjectPromise { get; set; }

        [JsonPropertyName("meetsAcceptedCreativeIntents")]
        public bool MeetsAcceptedCreativeIntents { get; set; } = true;

        [JsonPropertyName("continuityRisk")]
        public string ContinuityRisk { get; set; } = string.Empty;

        [JsonPropertyName("creativeFit")]
        public string CreativeFit { get; set; } = string.Empty;

        [JsonPropertyName("chapterPacing")]
        public string ChapterPacing { get; set; } = string.Empty;

        [JsonPropertyName("decision")]
        public string Decision { get; set; } = string.Empty;

        [JsonPropertyName("recommendedAction")]
        public string RecommendedAction { get; set; } = string.Empty;

        [JsonPropertyName("problems")]
        public List<string> Problems { get; set; } = new();

        [JsonPropertyName("suggestions")]
        public List<string> Suggestions { get; set; } = new();

        [JsonPropertyName("evidence")]
        public List<string> Evidence { get; set; } = new();
    }

    public interface IAgentEditorialReviewModelClient
    {
        Task<AgentEditorialReviewDecision> ReviewAsync(
            AgentEditorialReviewRequest request,
            CancellationToken ct = default);
    }
}
