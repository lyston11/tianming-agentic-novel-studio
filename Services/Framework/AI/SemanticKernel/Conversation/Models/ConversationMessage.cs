using System;
using System.Collections.Generic;
using Microsoft.SemanticKernel.ChatCompletion;

namespace TM.Services.Framework.AI.SemanticKernel.Conversation.Models
{
    public sealed class ConversationMessage
    {
        #region 基本元信息

        public Guid RunId { get; init; }

        public AuthorRole Role { get; init; }

        public DateTime Timestamp { get; init; } = DateTime.Now;

        #endregion

        #region Layer 1: Summary（对话层）

        public string Summary { get; set; } = string.Empty;

        #endregion

        #region Layer 2: Analysis（分析层）

        public string AnalysisRaw { get; set; } = string.Empty;

        public IReadOnlyList<ThinkingBlock> AnalysisBlocks { get; set; } = Array.Empty<ThinkingBlock>();

        public bool HasAnalysis => !string.IsNullOrEmpty(AnalysisRaw);

        #endregion

        #region Layer 3: Payload（负载层）

        public MessagePayload? Payload { get; set; }

        public bool HasPayload => Payload != null;

        #endregion
    }

    public class ThinkingBlock
    {
        public string Title { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
    }
}
