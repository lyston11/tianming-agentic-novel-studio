using System;
using System.Reflection;
using System.Linq;
using TM.Services.Framework.AI.Core;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Framework.AI.Interfaces.Prompts;

namespace TM.Services.Modules.ProjectData.Implementations
{
    [Obfuscation(Feature = "controlflow", Exclude = true, ApplyToMembers = true)]
    public partial class UnifiedValidationService : IUnifiedValidationService
    {
        private readonly IContextService _contextService;
        private readonly IGeneratedContentService _contentService;
        private readonly IPublishService _publishService;
        private readonly IValidationSummaryService _validationSummaryService;
        private readonly TM.Services.Modules.VersionTracking.VersionTrackingService _versionTrackingService;
        private readonly AIService _aiService;
        private readonly IGuideContextService _guideContextService;
        private readonly GenerationGate _generationGate;
        private readonly IPromptRepository _promptRepository;
        private readonly string _ruleSignature;

        private const string SystemModuleName = "System";
        private const int ChapterPreviewLength = 1000;

        private const int ValidationBatchSize = 2;

        public UnifiedValidationService(
            IContextService contextService,
            IGeneratedContentService contentService,
            IPublishService publishService,
            IValidationSummaryService validationSummaryService,
            TM.Services.Modules.VersionTracking.VersionTrackingService versionTrackingService,
            AIService aiService,
            IGuideContextService guideContextService,
            GenerationGate generationGate,
            IPromptRepository promptRepository)
        {
            _contextService = contextService;
            _contentService = contentService;
            _publishService = publishService;
            _validationSummaryService = validationSummaryService;
            _versionTrackingService = versionTrackingService;
            _aiService = aiService;
            _guideContextService = guideContextService;
            _generationGate = generationGate;
            _promptRepository = promptRepository;
            _ruleSignature = BuildRulesSignature();

            TM.App.Log("[UnifiedValidationService] 初始化完成");
        }

        private string? GetValidationTemplateSystemPrompt()
        {
            try
            {
                var templates = _promptRepository.GetTemplatesByCategory("校验");
                var enabled = templates
                    .Where(t => t.IsEnabled && !string.IsNullOrWhiteSpace(t.SystemPrompt))
                    .OrderByDescending(t => t.IsDefault)
                    .ThenByDescending(t => t.IsBuiltIn)
                    .FirstOrDefault();
                if (enabled != null)
                {
                    if (InfoLogDedup.ShouldLog($"UnifiedValidation:Template:{enabled.Id}"))
                        TM.App.Log($"[UnifiedValidationService] 使用校验模板: {enabled.Name} ({enabled.Id})");
                    return enabled.SystemPrompt;
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[UnifiedValidationService] 加载校验模板失败: {ex.Message}");
            }
            return null;
        }

    }
}
