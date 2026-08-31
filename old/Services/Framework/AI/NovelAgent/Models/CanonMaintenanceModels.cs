using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TM.Services.Framework.AI.NovelAgent.Models
{
    public sealed class CanonMaintenanceResult
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("requiresConfirmation")]
        public bool RequiresConfirmation { get; set; }

        [JsonPropertyName("riskLevel")]
        public NovelToolRiskLevel RiskLevel { get; set; } = NovelToolRiskLevel.Medium;

        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        [JsonPropertyName("importedEntries")]
        public List<CanonLedgerEntry> ImportedEntries { get; set; } = new();

        [JsonPropertyName("promotedEntries")]
        public List<CanonLedgerEntry> PromotedEntries { get; set; } = new();

        [JsonPropertyName("rejectedEntries")]
        public List<CanonLedgerEntry> RejectedEntries { get; set; } = new();

        [JsonPropertyName("conflictEntries")]
        public List<CanonLedgerEntry> ConflictEntries { get; set; } = new();

        [JsonPropertyName("run")]
        public NovelAgentRun? Run { get; set; }

        [JsonPropertyName("document")]
        public StoryBibleDocument? Document { get; set; }
    }
}
