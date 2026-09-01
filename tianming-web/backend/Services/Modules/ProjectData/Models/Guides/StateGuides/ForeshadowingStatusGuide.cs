using System.Collections.Generic;

namespace TM.Services.Modules.ProjectData.Models.Guides
{
    public class ForeshadowingStatusGuide
    {
        [System.Text.Json.Serialization.JsonPropertyName("Module")] public string Module { get; set; } = "ForeshadowingStatusGuide";
        [System.Text.Json.Serialization.JsonPropertyName("Summary")] public ForeshadowingSummary Summary { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("ByTier")] public Dictionary<string, TierStats> ByTier { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("PendingList")] public List<PendingForeshadowing> PendingList { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("OverdueList")] public List<OverdueForeshadowing> OverdueList { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("Foreshadowings")] public Dictionary<string, ForeshadowingStatusEntry> Foreshadowings { get; set; } = new();
    }

    public class ForeshadowingSummary
    {
        [System.Text.Json.Serialization.JsonPropertyName("Total")] public int Total { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("Setup")] public int Setup { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("Payoff")] public int Payoff { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("Pending")] public int Pending { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("CompletionRate")] public string CompletionRate { get; set; } = "0%";
    }

    public class TierStats
    {
        [System.Text.Json.Serialization.JsonPropertyName("Total")] public int Total { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("Completed")] public int Completed { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("Pending")] public int Pending { get; set; }
    }

    public class PendingForeshadowing
    {
        [System.Text.Json.Serialization.JsonPropertyName("Id")] public string Id { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Tier")] public string Tier { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Name")] public string Name { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("SetupChapter")] public string SetupChapter { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("PlannedPayoff")] public string PlannedPayoff { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Status")] public string Status { get; set; } = "pending";
        [System.Text.Json.Serialization.JsonPropertyName("Urgency")] public string Urgency { get; set; } = "normal";
    }

    public class OverdueForeshadowing
    {
        [System.Text.Json.Serialization.JsonPropertyName("Id")] public string Id { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Tier")] public string Tier { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Name")] public string Name { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("SetupChapter")] public string SetupChapter { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("PlannedPayoff")] public string PlannedPayoff { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Status")] public string Status { get; set; } = "overdue";
        [System.Text.Json.Serialization.JsonPropertyName("Suggestion")] public string Suggestion { get; set; } = string.Empty;
    }

    public class ForeshadowingStatusEntry
    {
        [System.Text.Json.Serialization.JsonPropertyName("Name")] public string Name { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Tier")] public string Tier { get; set; } = "Tier-3";
        [System.Text.Json.Serialization.JsonPropertyName("IsSetup")] public bool IsSetup { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("IsResolved")] public bool IsResolved { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("IsOverdue")] public bool IsOverdue { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("ExpectedSetupChapter")] public string ExpectedSetupChapter { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("ExpectedPayoffChapter")] public string ExpectedPayoffChapter { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("ActualSetupChapter")] public string ActualSetupChapter { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("ActualPayoffChapter")] public string ActualPayoffChapter { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("DriftWarnings")] public List<string> DriftWarnings { get; set; } = new();
    }
}
