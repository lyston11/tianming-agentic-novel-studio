using System;
using TM.Services.Framework.AI.Core;
using TM.Services.Framework.AI.SemanticKernel;

namespace TM.Services.Framework.SystemIntegration
{
    public class GlobalCleanupService
    {
        private readonly AIService _aiService;
        private readonly SKChatService _skChatService;

        public GlobalCleanupService(AIService aiService, SKChatService skChatService)
        {
            _aiService = aiService;
            _skChatService = skChatService;
        }

        public bool ExecuteCleanup()
        {
            try
            {
                TM.App.Log("[GlobalCleanupService] 开始执行全局清理...");

                _aiService.ClearAllBusinessSessions();

                _skChatService.BeginDraftSession();

                TM.App.Log("[GlobalCleanupService] 全局清理完成");
                return true;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GlobalCleanupService] 全局清理失败: {ex.Message}");
                return false;
            }
        }

        public static bool Execute()
        {
            try
            {
                var service = ServiceLocator.Get<GlobalCleanupService>();
                return service.ExecuteCleanup();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GlobalCleanupService] 静态调用失败: {ex.Message}");
                return false;
            }
        }
    }
}
