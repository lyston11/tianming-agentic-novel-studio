namespace TM.Web.NovelAgentWeb.Support;

public enum ConversationPhase
{
    Conversation = 0,  // 状态查询、确认、闲聊
    Planning = 1,      // 规划章节、选择候选、构建上下文
    Creation = 2,      // 生成正文、修复草稿
    Review = 3,        // 质量评审、门禁检查
}
