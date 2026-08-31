using System.Collections.Generic;
using TM.Services.Modules.ProjectData.Models.Contexts.Aggregates;
using System.Text.Json.Serialization;

namespace TM.Services.Modules.ProjectData.Models.Contexts
{
    public class ValidationContext
    {
        [JsonPropertyName("Design")]
        public DesignData Design { get; set; } = new();

        [JsonPropertyName("Generate")]
        public GenerateData Generate { get; set; } = new();

        [JsonPropertyName("GeneratedContent")]
        public string GeneratedContent { get; set; } = string.Empty;

        [JsonPropertyName("Rules")]
        public ValidateRules Rules { get; set; } = new();

        [JsonPropertyName("ChapterId")]
        public string ChapterId { get; set; } = string.Empty;

        [JsonPropertyName("VolumeNumber")]
        public int VolumeNumber { get; set; }

        [JsonPropertyName("ChapterNumber")]
        public int ChapterNumber { get; set; }
    }

    public class ValidateRules
    {
        [JsonPropertyName("ConsistencyRules")]
        public List<ConsistencyRule> ConsistencyRules { get; set; } = new();

        [JsonPropertyName("QualityRules")]
        public List<QualityRule> QualityRules { get; set; } = new();

        [JsonPropertyName("DataRules")]
        public List<DataRule> DataRules { get; set; } = new();

        [JsonPropertyName("OutputRules")]
        public List<OutputRule> OutputRules { get; set; } = new();
    }

    public class ConsistencyRule
    {
        [JsonPropertyName("Id")]
        public string Id { get; set; } = string.Empty;
        [JsonPropertyName("Name")]
        public string Name { get; set; } = string.Empty;
        [JsonPropertyName("Description")]
        public string Description { get; set; } = string.Empty;
        [JsonPropertyName("RuleType")]
        public string RuleType { get; set; } = string.Empty;
        [JsonPropertyName("IsEnabled")]
        public bool IsEnabled { get; set; } = true;
        [JsonPropertyName("Severity")]
        public string Severity { get; set; } = "Warning";
    }

    public class QualityRule
    {
        [JsonPropertyName("Id")]
        public string Id { get; set; } = string.Empty;
        [JsonPropertyName("Name")]
        public string Name { get; set; } = string.Empty;
        [JsonPropertyName("Description")]
        public string Description { get; set; } = string.Empty;
        [JsonPropertyName("RuleType")]
        public string RuleType { get; set; } = string.Empty;
        [JsonPropertyName("IsEnabled")]
        public bool IsEnabled { get; set; } = true;
        [JsonPropertyName("Severity")]
        public string Severity { get; set; } = "Warning";
        public Dictionary<string, object> Parameters { get; set; } = new();
    }

    public class DataRule
    {
        [JsonPropertyName("Id")]
        public string Id { get; set; } = string.Empty;
        [JsonPropertyName("Name")]
        public string Name { get; set; } = string.Empty;
        [JsonPropertyName("Description")]
        public string Description { get; set; } = string.Empty;
        [JsonPropertyName("RuleType")]
        public string RuleType { get; set; } = string.Empty;
        [JsonPropertyName("IsEnabled")]
        public bool IsEnabled { get; set; } = true;
        [JsonPropertyName("Severity")]
        public string Severity { get; set; } = "Error";
    }

    public class OutputRule
    {
        [JsonPropertyName("Id")]
        public string Id { get; set; } = string.Empty;
        [JsonPropertyName("Name")]
        public string Name { get; set; } = string.Empty;
        [JsonPropertyName("Description")]
        public string Description { get; set; } = string.Empty;
        [JsonPropertyName("RuleType")]
        public string RuleType { get; set; } = string.Empty;
        [JsonPropertyName("IsEnabled")]
        public bool IsEnabled { get; set; } = true;
        [JsonPropertyName("Severity")]
        public string Severity { get; set; } = "Info";
        public Dictionary<string, object> Parameters { get; set; } = new();
    }
}
