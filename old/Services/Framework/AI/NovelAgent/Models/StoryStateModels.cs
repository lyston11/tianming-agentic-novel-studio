using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TM.Services.Framework.AI.NovelAgent.Models
{
    public sealed class StoryStateSnapshot
    {
        [JsonPropertyName("chapterId")]
        public string ChapterId { get; set; } = string.Empty;

        [JsonPropertyName("chapterTitle")]
        public string ChapterTitle { get; set; } = string.Empty;

        [JsonPropertyName("chapterGoal")]
        public string ChapterGoal { get; set; } = string.Empty;

        [JsonPropertyName("chapterTurn")]
        public string ChapterTurn { get; set; } = string.Empty;

        [JsonPropertyName("readerExperienceGoal")]
        public string ReaderExperienceGoal { get; set; } = string.Empty;

        [JsonPropertyName("previousChapterId")]
        public string PreviousChapterId { get; set; } = string.Empty;

        [JsonPropertyName("previousChapterSummary")]
        public string PreviousChapterSummary { get; set; } = string.Empty;

        [JsonPropertyName("activeConflicts")]
        public List<string> ActiveConflicts { get; set; } = new();

        [JsonPropertyName("activeForeshadowing")]
        public List<string> ActiveForeshadowing { get; set; } = new();

        [JsonPropertyName("foreshadowLedgerItems")]
        public List<string> ForeshadowLedgerItems { get; set; } = new();

        [JsonPropertyName("worldRules")]
        public List<string> WorldRules { get; set; } = new();

        [JsonPropertyName("characterStates")]
        public List<string> CharacterStates { get; set; } = new();

        [JsonPropertyName("characterLedgerItems")]
        public List<string> CharacterLedgerItems { get; set; } = new();

        [JsonPropertyName("usedPlotPatterns")]
        public List<string> UsedPlotPatterns { get; set; } = new();

        [JsonPropertyName("longDistanceRecall")]
        public List<string> LongDistanceRecall { get; set; } = new();

        [JsonPropertyName("similarContentFragments")]
        public List<string> SimilarContentFragments { get; set; } = new();

        [JsonPropertyName("ragSearchQueries")]
        public List<string> RagSearchQueries { get; set; } = new();

        [JsonPropertyName("warnings")]
        public List<string> Warnings { get; set; } = new();
    }
}
