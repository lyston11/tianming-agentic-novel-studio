using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TM.Services.Framework.AI.NovelAgent.Models
{
    public sealed class CreativeKnowledgeBaseDocument
    {
        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; set; } = 1;

        [JsonPropertyName("entries")]
        public List<CreativeKnowledgeEntry> Entries { get; set; } = new();

        [JsonPropertyName("createdAt")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [JsonPropertyName("updatedAt")]
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
    }

    public sealed class CreativeKnowledgeEntry
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        [JsonPropertyName("category")]
        public CreativeKnowledgeCategory Category { get; set; } = CreativeKnowledgeCategory.GenrePrinciple;

        [JsonPropertyName("genre")]
        public string Genre { get; set; } = string.Empty;

        [JsonPropertyName("subGenre")]
        public string SubGenre { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;

        [JsonPropertyName("tags")]
        public List<string> Tags { get; set; } = new();

        [JsonPropertyName("weight")]
        public int Weight { get; set; } = 5;

        [JsonPropertyName("source")]
        public string Source { get; set; } = "BuiltIn";

        [JsonPropertyName("createdAt")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [JsonPropertyName("updatedAt")]
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
    }

    public sealed class CreativeKnowledgeRetrievalResult
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        [JsonPropertyName("query")]
        public string Query { get; set; } = string.Empty;

        [JsonPropertyName("hits")]
        public List<CreativeKnowledgeHit> Hits { get; set; } = new();

        [JsonPropertyName("genrePrinciples")]
        public List<string> GenrePrinciples { get; set; } = new();

        [JsonPropertyName("tropeWarnings")]
        public List<string> TropeWarnings { get; set; } = new();

        [JsonPropertyName("antiTropeStrategies")]
        public List<string> AntiTropeStrategies { get; set; } = new();

        [JsonPropertyName("emotionRelationshipGuides")]
        public List<string> EmotionRelationshipGuides { get; set; } = new();

        [JsonPropertyName("projectMemory")]
        public List<string> ProjectMemory { get; set; } = new();
    }

    public sealed class CreativeKnowledgeHit
    {
        [JsonPropertyName("entry")]
        public CreativeKnowledgeEntry Entry { get; set; } = new();

        [JsonPropertyName("score")]
        public double Score { get; set; }

        [JsonPropertyName("reason")]
        public string Reason { get; set; } = string.Empty;
    }

    public sealed class CreativeKnowledgeMutationResult
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        [JsonPropertyName("entry")]
        public CreativeKnowledgeEntry? Entry { get; set; }

        [JsonPropertyName("document")]
        public CreativeKnowledgeBaseDocument? Document { get; set; }
    }

    public enum CreativeKnowledgeCategory
    {
        GenrePrinciple = 0,
        TropePattern = 1,
        AntiTropeStrategy = 2,
        ProjectUsedPattern = 3,
        ReaderPromise = 4,
        ThemeDepth = 5,
        EmotionArc = 6,
        RelationshipDynamic = 7
    }
}
