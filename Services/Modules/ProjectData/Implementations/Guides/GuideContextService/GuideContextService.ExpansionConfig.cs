using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Models.Guides;
using TM.Services.Modules.ProjectData.Models.TaskContexts;
using TM.Services.Modules.ProjectData.Models.Context;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public partial class GuideContextService
    {
        #region ExpansionConfig

        private async Task<ExpansionConfig> GetExpansionConfigAsync()
        {
            var cached = _expansionConfig;
            if (cached != null)
                return cached;

            var epoch = Volatile.Read(ref _cacheEpoch);
            ExpansionConfig? loaded = null;
            var path = Path.Combine(
                StoragePathHelper.GetServicesStoragePath("Settings"),
                "context_expansion_config.json");
            if (File.Exists(path))
            {
                try
                {
                    await using var pathStream = File.OpenRead(path);
                    loaded = await JsonSerializer.DeserializeAsync<ExpansionConfig>(pathStream, JsonOptions).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[GuideContextService] 加载扩展配置失败: {ex.Message}");
                }
            }
            loaded ??= new ExpansionConfig { Enabled = false };
            if (!IsCacheEpochCurrent(epoch))
                return new ExpansionConfig { Enabled = false };

            _expansionConfig ??= loaded;
            return _expansionConfig;
        }

        private async Task<bool> IsKeySceneAsync(ContentGuideEntry? chapterGuide)
        {
            var config = await GetExpansionConfigAsync().ConfigureAwait(false);
            if (!config.Enabled || chapterGuide?.Scenes == null || chapterGuide.Scenes.Count == 0)
                return false;

            var maxCharacters = chapterGuide.Scenes.Max(s => s.CharacterIds?.Count ?? 0);
            if (maxCharacters > config.Rules.SceneCharactersThreshold)
                return true;

            foreach (var scene in chapterGuide.Scenes)
            {
                if (config.Rules.TriggerKeywords.Any(k => scene.Purpose?.Contains(k) == true))
                    return true;
            }

            return false;
        }

        private async Task TryExpandForKeySceneAsync(ContentTaskContext context, ContentGuideEntry? chapterGuide)
        {
            if (!await IsKeySceneAsync(chapterGuide).ConfigureAwait(false) || chapterGuide == null)
                return;

            var config = await GetExpansionConfigAsync().ConfigureAwait(false);

            var loadedCharIds = context.Characters.Select(c => c.Id).ToHashSet();
            var additionalCharIds = chapterGuide.Scenes
                .SelectMany(s => s.CharacterIds ?? new())
                .Distinct()
                .Where(id => !loadedCharIds.Contains(id))
                .Take(config.Limits.MaxAdditionalCharacters)
                .ToList();

            if (additionalCharIds.Count > 0)
            {
                var additionalChars = await ExtractCharactersAsync(additionalCharIds).ConfigureAwait(false);
                context.ExpandedCharacters.AddRange(additionalChars);
            }

            if (context.ExpandedCharacters.Count > 0)
            {
                context.IsKeySceneExpanded = true;
                TM.App.Log($"[GuideContextService] 关键场景扩展: +{context.ExpandedCharacters.Count}角色");
            }
        }

        #endregion
    }
}
