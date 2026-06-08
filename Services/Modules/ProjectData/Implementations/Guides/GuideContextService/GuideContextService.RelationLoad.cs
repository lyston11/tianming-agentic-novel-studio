using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public partial class GuideContextService
    {
        #region RelationLoad

        public async Task<(List<Models.Index.IndexItem> Direct, List<Models.Index.IndexItem> Indirect)>
            GetRelatedEntitiesAsync(string focusId, string layer)
        {
            var direct = new List<Models.Index.IndexItem>();
            var indirect = new List<Models.Index.IndexItem>();

            try
            {
                var relationsPath = Path.Combine(
                    StoragePathHelper.GetStorageRoot(),
                    "Modules", "Design", "Elements", "CharacterRules", "relationships.json");

                if (!File.Exists(relationsPath))
                    return (direct, indirect);

                await using var relationsStream = File.OpenRead(relationsPath);
                var relations = await JsonSerializer.DeserializeAsync<List<Dictionary<string, JsonElement>>>(relationsStream, JsonOptions).ConfigureAwait(false);

                if (relations == null)
                    return (direct, indirect);

                foreach (var rel in relations)
                {
                    var char1 = GetJsonString(rel, "Character1Id");
                    var char2 = GetJsonString(rel, "Character2Id");
                    var strength = GetJsonString(rel, "RelationshipType");

                    string? relatedId = null;
                    if (char1 == focusId) relatedId = char2;
                    else if (char2 == focusId) relatedId = char1;

                    if (relatedId is null) continue;

                    var relStrength = DetermineStrength(strength);
                    var indexItem = await BuildRelatedIndexItemAsync(relatedId, relStrength).ConfigureAwait(false);

                    if (indexItem == null) continue;

                    if (relStrength == Models.Context.RelationStrength.Strong)
                    {
                        if (direct.Count < 5)
                            direct.Add(indexItem);
                    }
                    else
                    {
                        if (indirect.Count < 10)
                            indirect.Add(indexItem);
                    }
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GuideContextService] 获取关联实体失败: {ex.Message}");
            }

            return (direct, indirect);
        }

        private Models.Context.RelationStrength DetermineStrength(string relationshipType)
        {
            var strongTypes = new[] { "师徒", "血亲", "宿敌", "挚友", "恋人", "主仆" };
            var mediumTypes = new[] { "同门", "盟友", "对手", "同伴" };

            if (strongTypes.Any(t => relationshipType?.Contains(t) == true))
                return Models.Context.RelationStrength.Strong;
            if (mediumTypes.Any(t => relationshipType?.Contains(t) == true))
                return Models.Context.RelationStrength.Medium;
            return Models.Context.RelationStrength.Weak;
        }

        private async Task<Models.Index.IndexItem?> BuildRelatedIndexItemAsync(
            string entityId, Models.Context.RelationStrength strength)
        {
            try
            {
                await InitializeCacheAsync().ConfigureAwait(false);

                if (!_characterCache.TryGetValue(entityId, out var profile) || profile == null)
                    return null;

                var briefParts = new List<string>();
                if (!string.IsNullOrWhiteSpace(profile.Identity)) briefParts.Add(profile.Identity);
                if (!string.IsNullOrWhiteSpace(profile.Want)) briefParts.Add($"目标:{profile.Want}");
                if (!string.IsNullOrWhiteSpace(profile.Need)) briefParts.Add($"需求:{profile.Need}");

                var deepParts = new List<string>();
                if (!string.IsNullOrWhiteSpace(profile.FlawBelief)) deepParts.Add(profile.FlawBelief);
                if (!string.IsNullOrWhiteSpace(profile.GrowthPath)) deepParts.Add(profile.GrowthPath);
                if (!string.IsNullOrWhiteSpace(profile.SpecialAbilities)) deepParts.Add(profile.SpecialAbilities);

                return new Models.Index.IndexItem
                {
                    Id = profile.Id,
                    Name = profile.Name,
                    Type = profile.CharacterType,
                    BriefSummary = TruncateString(string.Join("；", briefParts), 30),
                    DeepSummary = TruncateString(string.Join("。", deepParts), 80),
                    RelationStrength = strength.ToString()
                };
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GuideContextService] 构建关联实体索引失败: {ex.Message}");
                return null;
            }
        }

        private static string GetJsonString(Dictionary<string, JsonElement> dict, string key)
        {
            if (dict.TryGetValue(key, out var element) && element.ValueKind == JsonValueKind.String)
                return element.GetString() ?? string.Empty;
            return string.Empty;
        }

        #endregion
    }
}
