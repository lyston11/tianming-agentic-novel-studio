using TM.Services.Framework.AI.NovelAgent.Models;

namespace TM.Web.NovelAgentWeb.Support;

public sealed class PhaseInference
{
    private static readonly string[] StatusQueryKeywords = new[] { "状态", "进度", "完成", "写了", "做了", "怎么样" };
    private static readonly string[] ConfirmationKeywords = new[] { "好的", "确认", "可以", "继续", "行" };
    private static readonly string[] NewProjectKeywords = new[] { "新书", "新小说", "创建" };

    public TurnIntentType ClassifyIntent(string userMessage, AgentSession session)
    {
        var message = userMessage?.Trim().ToLowerInvariant() ?? string.Empty;

        // 1. Status query
        if (StatusQueryKeywords.Any(kw => message.Contains(kw)))
            return TurnIntentType.StatusQuery;

        // 2. Confirmation
        if (session.WorkingMemory.PendingToolCall != null && ConfirmationKeywords.Any(kw => message.Contains(kw)))
            return TurnIntentType.Confirmation;

        // 3. Free chat (short message without creative keywords)
        if (message.Length < 10 && !message.Contains("写") && !message.Contains("章"))
            return TurnIntentType.FreeChat;

        // 4. New project
        if (NewProjectKeywords.Any(kw => message.Contains(kw)))
            return TurnIntentType.NewProjectSeed;

        // 5. Continue mission
        if (message.Contains("继续") || message.Contains("下一章") || message.Contains("下一个"))
            return TurnIntentType.ContinueMission;

        // 6. Revision request
        if (message.Contains("修改") || message.Contains("修复") || message.Contains("改"))
            return TurnIntentType.RevisionRequest;

        // 7. Default to creative brief
        return TurnIntentType.CreativeBrief;
    }
}
