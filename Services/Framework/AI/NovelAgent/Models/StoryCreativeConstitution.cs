using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TM.Services.Framework.AI.NovelAgent.Models
{
    public sealed class StoryCreativeConstitution
    {
        [JsonPropertyName("genre")]
        public string Genre { get; set; } = string.Empty;

        [JsonPropertyName("subGenre")]
        public string SubGenre { get; set; } = string.Empty;

        [JsonPropertyName("readerPromise")]
        public string ReaderPromise { get; set; } = string.Empty;

        [JsonPropertyName("coreHook")]
        public string CoreHook { get; set; } = string.Empty;

        [JsonPropertyName("coreTheme")]
        public string CoreTheme { get; set; } = string.Empty;

        [JsonPropertyName("mainPleasure")]
        public string MainPleasure { get; set; } = string.Empty;

        [JsonPropertyName("secondaryPleasure")]
        public string SecondaryPleasure { get; set; } = string.Empty;

        [JsonPropertyName("worldCoreRule")]
        public string WorldCoreRule { get; set; } = string.Empty;

        [JsonPropertyName("mainConflictEngine")]
        public string MainConflictEngine { get; set; } = string.Empty;

        [JsonPropertyName("protagonistEngine")]
        public string ProtagonistEngine { get; set; } = string.Empty;

        [JsonPropertyName("noveltyPoint")]
        public string NoveltyPoint { get; set; } = string.Empty;

        [JsonPropertyName("depthLayer")]
        public string DepthLayer { get; set; } = string.Empty;

        [JsonPropertyName("forbiddenDirections")]
        public List<string> ForbiddenDirections { get; set; } = new();

        [JsonPropertyName("commercialRhythm")]
        public string CommercialRhythm { get; set; } = string.Empty;

        [JsonPropertyName("genreProfile")]
        public GenreDirectionProfile GenreProfile { get; set; } = new();
    }

    public sealed class GenreDirectionProfile
    {
        [JsonPropertyName("pleasureStrength")]
        public int PleasureStrength { get; set; }

        [JsonPropertyName("mysteryStrength")]
        public int MysteryStrength { get; set; }

        [JsonPropertyName("emotionStrength")]
        public int EmotionStrength { get; set; }

        [JsonPropertyName("worldbuildingStrength")]
        public int WorldbuildingStrength { get; set; }

        [JsonPropertyName("ensembleStrength")]
        public int EnsembleStrength { get; set; }

        [JsonPropertyName("depthStrength")]
        public int DepthStrength { get; set; }

        [JsonPropertyName("paceStrength")]
        public int PaceStrength { get; set; }

        [JsonPropertyName("strategy")]
        public string Strategy { get; set; } = string.Empty;

        [JsonPropertyName("riskWarnings")]
        public List<string> RiskWarnings { get; set; } = new();
    }
}
