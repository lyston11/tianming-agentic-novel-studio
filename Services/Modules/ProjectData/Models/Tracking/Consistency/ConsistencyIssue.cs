using System.Text.Json.Serialization;

namespace TM.Services.Modules.ProjectData.Models.Tracking
{
    public class ConsistencyIssue
    {
        [JsonPropertyName("EntityId")] public string EntityId { get; set; } = string.Empty;
        [JsonPropertyName("IssueType")] public string IssueType { get; set; } = string.Empty;
        [JsonPropertyName("Expected")] public string Expected { get; set; } = string.Empty;
        [JsonPropertyName("Actual")] public string Actual { get; set; } = string.Empty;

        public override string ToString()
        {
            var entityName = EntityNameResolver.Resolve(EntityId);
            return $"[{IssueType}] 实体: {entityName}, 期望: {Expected}, 实际: {Actual}";
        }
    }
}
