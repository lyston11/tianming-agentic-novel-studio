using System;
using System.Collections.Generic;
using System.Linq;
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
                if (_relationStrengthSource == null)
                    return (direct, indirect);

                var relations = await _relationStrengthSource.LoadRelationStrengthFactsAsync().ConfigureAwait(false);
                foreach (var rel in relations)
                {
                    string? relatedId = null;
                    if (string.Equals(rel.LeftId, focusId, StringComparison.OrdinalIgnoreCase)) relatedId = rel.RightId;
                    else if (string.Equals(rel.RightId, focusId, StringComparison.OrdinalIgnoreCase)) relatedId = rel.LeftId;

                    if (relatedId is null) continue;

                    var indexItem = await BuildRelatedIndexItemAsync(relatedId, rel.Strength).ConfigureAwait(false);

                    if (indexItem == null) continue;

                    if (rel.Strength == Models.Context.RelationStrength.Strong)
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

        #endregion
    }
}
