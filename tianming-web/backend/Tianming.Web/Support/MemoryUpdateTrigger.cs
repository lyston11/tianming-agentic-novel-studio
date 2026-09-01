namespace TM.Web.NovelAgentWeb.Support;

public enum MemoryUpdateTrigger
{
    Lightweight,    // 每次响应 - 更新 ChatSummary
    Standard,       // 3-5轮对话 - 提取 Preferences
    ToolCall,       // 工具调用 - 更新 ExecutionMemory
    ChapterWrite,   // 章节生成 - 更新 ProjectMemory
    SessionEnd      // 会话结束 - 完整提取
}
