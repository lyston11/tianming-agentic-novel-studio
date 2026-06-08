using System;
using System.Collections.Generic;
using System.Linq;
using TM.Services.Modules.ProjectData.Models.TaskContexts;

namespace TM.Services.Framework.AI.SemanticKernel.Plugins
{
    public partial class LayeredPromptBuilder
    {
        #region 私有方法 - 辅助

        private static string TruncateText(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            if (text.Length <= maxLength) return text;
            return text.Substring(0, maxLength) + "...";
        }

        private static string TruncateLine(string? text, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            if (text.Length <= maxLength) return text;
            return text.Substring(0, maxLength) + "...";
        }

        private static int EstimateTokenCount(string text)
            => TM.Framework.Common.Helpers.TokenEstimator.CountTokens(text);

        private const int MandatoryCharsCap = 20;
        private const int MandatoryLocsCap = 15;
        private const int MandatoryFactionsCap = 10;

        private static (List<string> Chars, List<string> Factions, List<string> Locs) BuildMandatoryEntities(ContentTaskContext ctx)
        {
            var bpCharSet = new HashSet<string>(StringComparer.Ordinal);
            var factionSet = new HashSet<string>(StringComparer.Ordinal);
            var bpLocSet = new HashSet<string>(StringComparer.Ordinal);

            if (ctx.Blueprints != null)
            {
                char[] sep = { ',', '\uff0c', '\u3001', ';', '\uff1b' };
                foreach (var bp in ctx.Blueprints)
                {
                    if (!string.IsNullOrWhiteSpace(bp.PovCharacter))
                        bpCharSet.Add(bp.PovCharacter.Trim());
                    foreach (var p in (bp.Cast ?? string.Empty).Split(sep, StringSplitOptions.RemoveEmptyEntries))
                    { var n = p.Trim(); if (n.Length >= 2) bpCharSet.Add(n); }
                    foreach (var p in (bp.Factions ?? string.Empty).Split(sep, StringSplitOptions.RemoveEmptyEntries))
                    { var n = p.Trim(); if (n.Length >= 2) factionSet.Add(n); }
                    foreach (var p in (bp.Locations ?? string.Empty).Split(sep, StringSplitOptions.RemoveEmptyEntries))
                    { var n = p.Trim(); if (n.Length >= 2) bpLocSet.Add(n); }
                }
            }

            var charResult = new List<string>(bpCharSet);
            var charSeen = new HashSet<string>(bpCharSet, StringComparer.Ordinal);
            void TryAddChar(string name) { if (charResult.Count < MandatoryCharsCap && !string.IsNullOrWhiteSpace(name) && charSeen.Add(name)) charResult.Add(name); }
            if (ctx.Characters != null)
                foreach (var c in ctx.Characters) TryAddChar(c.Name);
            if (ctx.ExpandedCharacters != null)
                foreach (var c in ctx.ExpandedCharacters) TryAddChar(c.Name);

            if (factionSet.Count > MandatoryFactionsCap)
                factionSet = new HashSet<string>(factionSet.Take(MandatoryFactionsCap), StringComparer.Ordinal);

            var locResult = new List<string>(bpLocSet);
            var locSeen = new HashSet<string>(bpLocSet, StringComparer.Ordinal);
            if (ctx.Locations != null)
                foreach (var l in ctx.Locations)
                    if (locResult.Count < MandatoryLocsCap && !string.IsNullOrWhiteSpace(l.Name) && locSeen.Add(l.Name)) locResult.Add(l.Name);

            return (charResult, factionSet.ToList(), locResult);
        }

        private static HashSet<string> BuildBlueprintCharNames(ContentTaskContext ctx)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            if (ctx.Blueprints == null) return set;
            char[] sep = { ',', '\uff0c', '\u3001', ';', '\uff1b' };
            foreach (var bp in ctx.Blueprints)
            {
                if (!string.IsNullOrWhiteSpace(bp.PovCharacter))
                    set.Add(bp.PovCharacter.Trim());
                foreach (var p in (bp.Cast ?? string.Empty).Split(sep, StringSplitOptions.RemoveEmptyEntries))
                { var n = p.Trim(); if (n.Length >= 2) set.Add(n); }
            }
            return set;
        }

        private static HashSet<string> BuildBlueprintFactionNames(ContentTaskContext ctx)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            if (ctx.Blueprints == null) return set;
            char[] sep = { ',', '\uff0c', '\u3001', ';', '\uff1b' };
            foreach (var bp in ctx.Blueprints)
            {
                foreach (var p in (bp.Factions ?? string.Empty).Split(sep, StringSplitOptions.RemoveEmptyEntries))
                { var n = p.Trim(); if (n.Length >= 2) set.Add(n); }
            }
            return set;
        }

        private static string ExtractHairColorTag(string? appearance)
        {
            if (string.IsNullOrWhiteSpace(appearance)) return string.Empty;
            foreach (var kw in HairColorConstants.HairColorKeywords)
                if (appearance.Contains(kw, StringComparison.OrdinalIgnoreCase))
                    return kw;
            return string.Empty;
        }

        #endregion
    }
}
