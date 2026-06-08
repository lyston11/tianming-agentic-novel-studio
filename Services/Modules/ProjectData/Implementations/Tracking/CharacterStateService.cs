using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Models.Guides;
using TM.Services.Modules.ProjectData.Models.Tracking;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public class CharacterStateService
    {
        private readonly GuideManager _guideManager;

        private static readonly string[] _escalationKeywords =
        {
            "首次", "第一次", "突破", "觉醒", "死亡", "牺牲", "背叛", "结盟",
            "失忆", "失去", "变心", "决裂", "和解", "永久", "不可逆",
            "升级", "破境", "化神", "晋升", "晋级", "反目", "绝交", "重逢",
            "拜师", "出师", "入门", "逐出", "认主", "解封", "归隐"
        };

        #region 构造函数

        public CharacterStateService(GuideManager guideManager)
        {
            _guideManager = guideManager;
        }

        #endregion

        private const string BaseFileName = "character_state_guide.json";
        private static string VolumeFileName(string chapterId) =>
            GuideManager.GetVolumeFileName(BaseFileName,
                ChapterParserHelper.ParseChapterIdOrDefault(chapterId).volumeNumber);

        public async Task<CharacterState?> GetCharacterStateAtAsync(string characterId, string chapterId)
        {
            var volNumbers = _guideManager.GetExistingVolumeNumbers(BaseFileName);
            var targetVol = ChapterParserHelper.ParseChapterIdOrDefault(chapterId).volumeNumber;
            foreach (var vol in volNumbers.Where(v => v <= targetVol).OrderByDescending(v => v))
            {
                var guide = await _guideManager.GetGuideAsync<CharacterStateGuide>(
                    GuideManager.GetVolumeFileName(BaseFileName, vol)).ConfigureAwait(false);
                if (!guide.Characters.TryGetValue(characterId, out var entry) || entry.StateHistory.Count == 0)
                    continue;
                var state = BinarySearchState(entry.StateHistory, chapterId);
                if (state != null) return state;
            }
            return null;
        }

        private CharacterState? BinarySearchState(List<CharacterState> history, string targetChapterId)
        {
            int left = 0, right = history.Count - 1;
            int resultIndex = -1;

            while (left <= right)
            {
                int mid = (left + right) / 2;
                int cmp = CompareChapterId(history[mid].Chapter, targetChapterId);

                if (cmp <= 0)
                {
                    resultIndex = mid;
                    left = mid + 1;
                }
                else
                {
                    right = mid - 1;
                }
            }

            return resultIndex >= 0 ? history[resultIndex] : null;
        }

        private int CompareChapterId(string a, string b)
        {
            return ChapterParserHelper.CompareChapterId(a, b);
        }

        public async Task UpdateCharacterStateAsync(string chapterId, CharacterStateChange change)
        {
            var volFile = VolumeFileName(chapterId);
            var guide = await _guideManager.GetGuideAsync<CharacterStateGuide>(volFile).ConfigureAwait(false);

            if (!guide.Characters.TryGetValue(change.CharacterId, out var characterEntry))
            {
                var displayName = await TryResolveCharacterDisplayNameAsync(change.CharacterId).ConfigureAwait(false) ?? change.CharacterId;
                characterEntry = new CharacterStateEntry
                {
                    Name = displayName
                };
                guide.Characters[change.CharacterId] = characterEntry;
                TM.App.Log($"[CharacterState] 自动注册新角色: {change.CharacterId} (Name={displayName})");
            }

            var lastState = characterEntry.StateHistory.LastOrDefault();
            var newState = new CharacterState
            {
                Chapter = chapterId,
                Phase = GetPhaseFromChapter(chapterId),
                Level = !string.IsNullOrWhiteSpace(change.NewLevel) ? change.NewLevel : (lastState?.Level ?? ""),
                Abilities = MergeAbilities(lastState?.Abilities, change.NewAbilities, change.LostAbilities),
                Relationships = MergeRelationships(lastState?.Relationships, change.RelationshipChanges),
                MentalState = !string.IsNullOrWhiteSpace(change.NewMentalState) ? change.NewMentalState : (lastState?.MentalState ?? ""),
                KeyEvent = change.KeyEvent,
                Importance = string.IsNullOrWhiteSpace(change.Importance) ? "normal" : change.Importance,
                CausedBy = change.CausedBy ?? string.Empty
            };

            if (string.Equals(newState.Importance, "normal", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(newState.KeyEvent)
                && newState.KeyEvent.Length >= 20
                && _escalationKeywords.Any(k => newState.KeyEvent.Contains(k)))
            {
                newState.Importance = "important";
                TM.App.Log($"[CharacterState] 自动升级为important: {newState.KeyEvent[..Math.Min(30, newState.KeyEvent.Length)]}");
            }

            characterEntry.StateHistory.Add(newState);

            if (characterEntry.StateHistory.Count > 1)
            {
                var prev = characterEntry.StateHistory[characterEntry.StateHistory.Count - 2];
                if (ChapterParserHelper.CompareChapterId(newState.Chapter, prev.Chapter) < 0)
                    characterEntry.StateHistory.Sort((a, b) => ChapterParserHelper.CompareChapterId(a.Chapter, b.Chapter));
            }

            _guideManager.MarkDirty(volFile);
            TM.App.Log($"[CharacterState] 已更新 {change.CharacterId} 在 {chapterId} 的状态");
        }

        public async Task RemoveChapterDataAsync(string chapterId)
        {
            var volFile = VolumeFileName(chapterId);
            var guide = await _guideManager.GetGuideAsync<CharacterStateGuide>(volFile).ConfigureAwait(false);
            var modified = false;

            foreach (var (_, entry) in guide.Characters)
            {
                var removed = entry.StateHistory.RemoveAll(s =>
                    string.Equals(s.Chapter, chapterId, StringComparison.Ordinal));
                if (removed > 0)
                {
                    modified = true;
                    entry.StateHistory.Sort((a, b) => ChapterParserHelper.CompareChapterId(a.Chapter, b.Chapter));
                }
            }

            if (modified)
            {
                _guideManager.MarkDirty(volFile);
                TM.App.Log($"[CharacterState] 已移除章节 {chapterId} 的状态记录并重排历史顺序");
            }
        }

        private (int vol, int ch) ParseChapterId(string chapterId)
        {
            var result = ChapterParserHelper.ParseChapterIdOrDefault(chapterId);
            return (result.volumeNumber, result.chapterNumber);
        }

        private string GetPhaseFromChapter(string chapterId)
        {
            var (vol, _) = ParseChapterId(chapterId);
            try
            {
                var totalVols = ServiceLocator.Get<TM.Modules.Generate.Elements.VolumeDesign.Services.VolumeDesignService>()
                    .GetAllVolumeDesigns()
                    .Count();
                if (totalVols <= 0) totalVols = 4;
                var ratio = (float)vol / totalVols;
                if (ratio <= 0.25f) return "起";
                if (ratio <= 0.50f) return "承";
                if (ratio <= 0.75f) return "转";
                return "合";
            }
            catch
            {
                return vol switch { 1 => "起", 2 => "承", 3 => "转", _ => "合" };
            }
        }

        private List<string> MergeAbilities(
            List<string>? existing,
            List<string>? newAbilities,
            List<string>? lostAbilities)
        {
            var result = new HashSet<string>(existing ?? new List<string>(), StringComparer.OrdinalIgnoreCase);

            if (newAbilities != null)
            {
                foreach (var ability in newAbilities.Where(a => !string.IsNullOrWhiteSpace(a)))
                {
                    result.Add(ability.Trim());
                }
            }

            if (lostAbilities != null)
            {
                foreach (var ability in lostAbilities.Where(a => !string.IsNullOrWhiteSpace(a)))
                {
                    result.Remove(ability.Trim());
                }
            }

            return result.ToList();
        }

        private Dictionary<string, RelationshipState> MergeRelationships(
            Dictionary<string, RelationshipState>? existing,
            Dictionary<string, RelationshipChange>? changes)
        {
            var result = existing != null
                ? new Dictionary<string, RelationshipState>(existing)
                : new Dictionary<string, RelationshipState>();

            if (changes != null)
            {
                foreach (var (targetId, change) in changes)
                {
                    if (result.ContainsKey(targetId))
                    {
                        if (!string.IsNullOrWhiteSpace(change.Relation))
                            result[targetId].Relation = change.Relation;
                        result[targetId].Trust += change.TrustDelta;
                        if (!string.IsNullOrWhiteSpace(change.EmotionPhase))
                            result[targetId].EmotionPhase = change.EmotionPhase;
                    }
                    else
                    {
                        result[targetId] = new RelationshipState
                        {
                            Relation = change.Relation,
                            Trust = change.TrustDelta,
                            EmotionPhase = change.EmotionPhase
                        };
                    }
                }
            }

            return result;
        }

        private static async Task<string?> TryResolveCharacterDisplayNameAsync(string characterId)
        {
            try
            {
                var elementsPath = Path.Combine(
                    StoragePathHelper.GetProjectConfigPath(), "Design", "elements.json");
                if (!File.Exists(elementsPath)) return null;

                var json = await File.ReadAllTextAsync(elementsPath).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (!root.TryGetProperty("data", out var data)) return null;
                if (!data.TryGetProperty("characterrules", out var charModule)) return null;
                if (!charModule.TryGetProperty("character_rules", out var characters)) return null;

                foreach (var item in characters.EnumerateArray())
                {
                    var id = item.TryGetProperty("Id", out var idProp) ? idProp.GetString() : null;
                    if (string.Equals(id, characterId, StringComparison.OrdinalIgnoreCase))
                    {
                        var name = item.TryGetProperty("Name", out var nameProp) ? nameProp.GetString() : null;
                        return string.IsNullOrWhiteSpace(name) ? null : name;
                    }
                }
            }
            catch (Exception ex) { TM.App.Log($"[CharacterState] 读取角色名失败: {ex.Message}"); }
            return null;
        }
    }
}
