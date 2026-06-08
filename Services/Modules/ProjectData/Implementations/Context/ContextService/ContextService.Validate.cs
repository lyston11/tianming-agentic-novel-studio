using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Modules.ProjectData.Models.Contexts;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public partial class ContextService : IContextService
    {
        #region Validate

        public async Task<ValidationContext> GetValidationContextAsync(string chapterId)
        {
            TM.App.Log($"[ContextService] 构建ValidationContext: chapterId={chapterId}");

            var designTask = LoadPackagedDesignDataAsync();
            var generateTask = LoadPackagedGenerateDataAsync();
            var hasParsed = TryParseChapterId(chapterId, out int vol, out int ch);
            var contentTask = hasParsed ? LoadGeneratedContentAsync(vol, ch) : Task.FromResult(string.Empty);
            await Task.WhenAll(designTask, generateTask, contentTask).ConfigureAwait(false);

            var context = new ValidationContext
            {
                ChapterId = chapterId,
                Design = await designTask.ConfigureAwait(false),
                Generate = await generateTask.ConfigureAwait(false),
                Rules = new ValidateRules()
            };

            if (hasParsed)
            {
                context.VolumeNumber = vol;
                context.ChapterNumber = ch;
                context.GeneratedContent = await contentTask.ConfigureAwait(false);
            }

            TM.App.Log($"[ContextService] ValidationContext构建完成");
            return context;
        }

        #endregion
    }
}
