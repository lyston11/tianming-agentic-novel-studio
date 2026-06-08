using System.Collections.Generic;
using TM.Framework.UI.Workspace.RightPanel.Modes;
using TM.Services.Framework.AI.SemanticKernel.Conversation.Mapping;
using TM.Services.Framework.AI.SemanticKernel.Conversation.Parsing;

namespace TM.Services.Framework.AI.SemanticKernel.Conversation.Config
{
    public static class ModeProfileRegistry
    {
        private static readonly Dictionary<ChatMode, ConversationModeProfile> _profiles;

        static ModeProfileRegistry()
        {
            _profiles = new Dictionary<ChatMode, ConversationModeProfile>
            {
                [ChatMode.Channel] = CreateChannelProfile(),
                [ChatMode.Edit] = CreateEditProfile(),
                [ChatMode.Plan] = CreatePlanProfile(),
                [ChatMode.Agent] = CreateAgentProfile()
            };
        }

        public static ConversationModeProfile GetProfile(ChatMode mode)
        {
            if (_profiles.TryGetValue(mode, out var profile))
            {
                return profile;
            }

            TM.App.Log($"[ModeProfileRegistry] 未知模式 {mode}，使用 Channel 配置");
            return _profiles[ChatMode.Channel];
        }

        public static IReadOnlyDictionary<ChatMode, ConversationModeProfile> All => _profiles;

        #region Profile 工厂方法

        private static ConversationModeProfile CreateChannelProfile()
        {
            return new ConversationModeProfile
            {
                Mode = ChatMode.Channel,
                Mapper = new ChannelModeMapper(),
                ExecutionResultMapper = null,
                DisplayPolicy = new ConversationDisplayPolicy
                {
                    SummarySelector = msg => msg.Summary,
                    ShowAnalysis = true,
                    AnalysisExpandedByDefault = false,
                    DefaultPayloadTarget = null,
                    HideRawContentInBubble = false
                },
                RequiresExecutionEngine = false,
                Description = "总通道 - 基础参数基座 / fallback"
            };
        }

        private static ConversationModeProfile CreateEditProfile()
        {
            return new ConversationModeProfile
            {
                Mode = ChatMode.Edit,
                Mapper = new ChannelModeMapper(),
                ExecutionResultMapper = null,
                DisplayPolicy = new ConversationDisplayPolicy
                {
                    SummarySelector = msg => msg.Summary,
                    ShowAnalysis = true,
                    AnalysisExpandedByDefault = false,
                    DefaultPayloadTarget = null,
                    HideRawContentInBubble = false
                },
                RequiresExecutionEngine = false,
                Description = "Edit 模式 - 查询与知识库编辑"
            };
        }

        private static ConversationModeProfile CreatePlanProfile()
        {
            return new ConversationModeProfile
            {
                Mode = ChatMode.Plan,
                Mapper = new PlanModeMapper(new PlanStepParser()),
                ExecutionResultMapper = new PlanExecutionResultMapper(),
                DisplayPolicy = new ConversationDisplayPolicy
                {
                    SummarySelector = msg =>
                    {
                        if (msg.Payload is Models.PlanPayload plan && plan.Steps.Count > 0)
                        {
                            return Helpers.ConversationSummarizer.ForPlanGenerated(plan.Steps.Count);
                        }
                        return msg.Summary;
                    },
                    ShowAnalysis = true,
                    AnalysisExpandedByDefault = false,
                    DefaultPayloadTarget = "ExecutionPlan",
                    HideRawContentInBubble = true
                },
                RequiresExecutionEngine = true,
                Description = "计划模式 - 生成计划后执行"
            };
        }

        private static ConversationModeProfile CreateAgentProfile()
        {
            return new ConversationModeProfile
            {
                Mode = ChatMode.Agent,
                Mapper = new AgentModeMapper(),
                ExecutionResultMapper = new AgentExecutionResultMapper(),
                DisplayPolicy = new ConversationDisplayPolicy
                {
                    SummarySelector = msg => msg.Summary,
                    ShowAnalysis = true,
                    AnalysisExpandedByDefault = false,
                    DefaultPayloadTarget = "ExecutionPanel",
                    HideRawContentInBubble = false
                },
                RequiresExecutionEngine = true,
                Description = "代理模式 - 直接执行任务"
            };
        }

        #endregion
    }
}
