using System;
using System.Linq;
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
            await Task.CompletedTask.ConfigureAwait(false);
            ExpansionConfig? loaded = null;
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
