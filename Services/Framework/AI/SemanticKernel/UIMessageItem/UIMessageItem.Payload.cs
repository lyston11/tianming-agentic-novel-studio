using System;
using System.ComponentModel;
using System.Text.Json;
using TM.Services.Framework.AI.SemanticKernel.Conversation.Models;

namespace TM.Services.Framework.AI.SemanticKernel
{
    public partial class UIMessageItem : INotifyPropertyChanged
    {
        #region Payload 持久化

        public PayloadType PayloadType { get; set; } = PayloadType.None;

        public string? PayloadJson { get; set; }

        public ConversationMessage? SourceMessage { get; private set; }

        public void ApplyFromConversationMessage(ConversationMessage msg)
        {
            SourceMessage = msg;

            Content = msg.Summary;
            ThinkingContent = msg.AnalysisRaw;

            if (msg.Payload != null)
            {
                PayloadType = msg.Payload.Type;
                PayloadJson = JsonSerializer.Serialize(msg.Payload, JsonHelper.Compact);
            }
        }

        public ConversationMessage ToConversationMessage()
        {
            if (SourceMessage != null)
                return SourceMessage;

            return new ConversationMessage
            {
                Role = Role,
                Timestamp = Timestamp,
                Summary = Content,
                AnalysisRaw = ThinkingContent,
                AnalysisBlocks = ThinkingBlocks,
                Payload = RestorePayload()
            };
        }

        public MessagePayload? RestorePayload()
        {
            if (string.IsNullOrEmpty(PayloadJson) || PayloadType == PayloadType.None)
                return null;

            try
            {
                return PayloadType switch
                {
                    PayloadType.Plan => JsonSerializer.Deserialize<PlanPayload>(PayloadJson),
                    PayloadType.AgentExecution => JsonSerializer.Deserialize<AgentPayload>(PayloadJson),
                    _ => null
                };
            }
            catch (Exception ex)
            {
                TM.App.Log($"[UIMessageItem] Payload 反序列化失败: {ex.Message}");
                return null;
            }
        }

        public bool HasPayload => PayloadType != PayloadType.None && !string.IsNullOrEmpty(PayloadJson);

        #endregion
    }
}
