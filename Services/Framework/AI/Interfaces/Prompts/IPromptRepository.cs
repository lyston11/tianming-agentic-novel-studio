using System.Collections.Generic;
using TM.Modules.AIAssistant.PromptTools.PromptManagement.Models;

namespace TM.Services.Framework.AI.Interfaces.Prompts
{
    public interface IPromptRepository
    {
        IReadOnlyList<PromptTemplateData> GetAllTemplates();

        IReadOnlyList<PromptTemplateData> GetTemplatesByCategory(string categoryName);

        PromptTemplateData? GetTemplateById(string id);

        void AddTemplate(PromptTemplateData template);

        void UpdateTemplate(PromptTemplateData template);

        void DeleteTemplate(string id);

        int ClearAllTemplates();

        IReadOnlyList<PromptCategory> GetAllCategories();

        PromptCategory? GetCategoryByName(string name);
    }
}
