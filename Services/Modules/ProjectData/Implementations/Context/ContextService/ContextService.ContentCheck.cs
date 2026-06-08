using System.Collections.Generic;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Modules.ProjectData.Models.Design.Characters;
using TM.Services.Modules.ProjectData.Models.Design.Worldview;
using TM.Services.Modules.ProjectData.Models.Design.Plot;
using TM.Services.Modules.ProjectData.Models.Design.Factions;
using TM.Services.Modules.ProjectData.Models.Design.Location;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public partial class ContextService : IContextService
    {
        #region ContentCheck

        private static int ExtractVolumeNumberFromText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;

            var digitMatch = VolDigitExtractRegex.Match(text);
            if (digitMatch.Success && int.TryParse(digitMatch.Groups[1].Value, out var n) && n > 0)
                return n;

            var chineseMap = new Dictionary<char, int>
            {
                ['一'] = 1,
                ['二'] = 2,
                ['三'] = 3,
                ['四'] = 4,
                ['五'] = 5,
                ['六'] = 6,
                ['七'] = 7,
                ['八'] = 8,
                ['九'] = 9,
                ['十'] = 10
            };
            var chineseMatch = VolCnNumRegex.Match(text);
            if (chineseMatch.Success)
            {
                var chStr = chineseMatch.Groups[1].Value;
                if (chStr.Length == 1 && chineseMap.TryGetValue(chStr[0], out var cv))
                    return cv;
                if (chStr.Length == 2)
                {
                    if (chStr[0] == '十' && chineseMap.TryGetValue(chStr[1], out var cv2))
                        return 10 + cv2;
                    if (chStr[1] == '十' && chineseMap.TryGetValue(chStr[0], out var cv3))
                        return cv3 * 10;
                }
                if (chStr.Length == 3 && chStr[1] == '十'
                    && chineseMap.TryGetValue(chStr[0], out var tens)
                    && chineseMap.TryGetValue(chStr[2], out var ones))
                    return tens * 10 + ones;
            }
            return 0;
        }

        private static bool HasWorldRulesContent(WorldRulesData data)
        {
            return !string.IsNullOrWhiteSpace(data.Name) ||
                   !string.IsNullOrWhiteSpace(data.OneLineSummary) ||
                   !string.IsNullOrWhiteSpace(data.PowerSystem) ||
                   !string.IsNullOrWhiteSpace(data.HardRules);
        }

        private static bool HasCharacterContent(CharacterRulesData data)
        {
            return !string.IsNullOrWhiteSpace(data.Name) ||
                   !string.IsNullOrWhiteSpace(data.Identity) ||
                   !string.IsNullOrWhiteSpace(data.Want) ||
                   !string.IsNullOrWhiteSpace(data.Need);
        }

        private static bool HasFactionContent(FactionRulesData data)
        {
            return !string.IsNullOrWhiteSpace(data.Name) ||
                   !string.IsNullOrWhiteSpace(data.Goal) ||
                   !string.IsNullOrWhiteSpace(data.FactionType);
        }

        private static bool HasLocationContent(LocationRulesData data)
        {
            return !string.IsNullOrWhiteSpace(data.Name) ||
                   !string.IsNullOrWhiteSpace(data.Description) ||
                   !string.IsNullOrWhiteSpace(data.Terrain);
        }

        private static bool HasPlotContent(PlotRulesData data)
        {
            return !string.IsNullOrWhiteSpace(data.Name) ||
                   !string.IsNullOrWhiteSpace(data.OneLineSummary) ||
                   !string.IsNullOrWhiteSpace(data.Goal);
        }

        #endregion
    }
}
