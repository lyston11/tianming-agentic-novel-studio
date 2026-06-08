using System;
using System.Collections.Generic;

namespace TM.Services.Modules.ProjectData.Implementations.Tracking.Rules
{
    public sealed class LedgerRuleSet
    {
        public int MaxTrustDelta { get; set; } = 30;

        public bool EnableConflictFlowCheck { get; set; }
        private List<string> _conflictStatusSequence = new();
        public List<string> ConflictStatusSequence
        {
            get => _conflictStatusSequence;
            set => _conflictStatusSequence = value ?? new List<string>();
        }

        public bool EnableLevelRegressionCheck { get; set; }
        private Dictionary<string, int> _levelTextMap = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> LevelTextMap
        {
            get => _levelTextMap;
            set => _levelTextMap = value ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }

        public bool EnableAbilityLossRequiresEvent { get; set; }
        private List<string> _abilityLossKeywords = new();
        public List<string> AbilityLossKeywords
        {
            get => _abilityLossKeywords;
            set => _abilityLossKeywords = value ?? new List<string>();
        }

        public bool EnablePledgeConstraintCheck { get; set; }

        public bool EnableDeadlineConstraintCheck { get; set; }

        public static LedgerRuleSet CreateUniversalDefault()
        {
            return new LedgerRuleSet
            {
                MaxTrustDelta = 30,
                EnableConflictFlowCheck = true,
                ConflictStatusSequence = new List<string> { "pending", "active", "climax", "resolved" },
                EnableLevelRegressionCheck = true,
                LevelTextMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                {
                    ["SSS"] = 60,
                    ["SS"] = 50,
                    ["S"] = 40,
                    ["A"] = 30,
                    ["B"] = 20,
                    ["C"] = 10,
                    ["D"] = 0,
                    ["E"] = 0,
                    ["F"] = 0,
                    ["Tier-1"] = 10,
                    ["Tier-2"] = 20,
                    ["Tier-3"] = 30,
                    ["Tier-4"] = 40,
                    ["Tier-5"] = 50
                },
                EnableAbilityLossRequiresEvent = false,
                AbilityLossKeywords = new List<string>(),
                EnablePledgeConstraintCheck = true,
                EnableDeadlineConstraintCheck = true
            };
        }
    }
}
