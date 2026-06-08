namespace TM.Services.Framework.AI.SemanticKernel.Conversation.Helpers
{
    public static class ConversationSummarizer
    {
        #region Plan 模式摘要

        public static string ForPlanGenerated(int stepCount)
        {
            return $"已生成创作计划，共 {stepCount} 个步骤。\n请在左侧「执行计划」面板查看详细步骤，确认后点击「开始执行」。";
        }

        public static string ForPlanParseFailed()
        {
            return "[提示] 计划解析失败，请重新描述您的需求。";
        }

        #endregion

        #region 执行完成摘要

        public static string ForExecutionCompleted(string? chapterTitle, string? chapterId, string traceText)
        {
            if (!string.IsNullOrEmpty(chapterTitle))
            {
                return $"「{chapterTitle}」生成完毕，{traceText}。";
            }
            else if (!string.IsNullOrEmpty(chapterId))
            {
                return $"章节 {chapterId} 生成完毕，{traceText}。";
            }
            else
            {
                return $"创作任务执行完毕，{traceText}。";
            }
        }

        public static string ForExecutionCompleted(string? chapterTitle, string? chapterId, ExecutionTraceSummary summary)
        {
            return ForExecutionCompleted(chapterTitle, chapterId, summary.ToSummaryText());
        }

        #endregion

        #region 错误摘要

        public static string ForExecutionNotStarted()
        {
            return "当前已有任务在执行中，请稍后再试。";
        }

        public static string ForExecutionCancelled()
        {
            return "创作任务已取消。";
        }

        #endregion
    }
}
