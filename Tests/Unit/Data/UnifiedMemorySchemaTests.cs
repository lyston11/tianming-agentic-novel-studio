using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using Xunit;

namespace Tests.Unit.Data;

public class UnifiedMemorySchemaTests
{
    [Fact]
    public async Task DbContext_CanPersistUnifiedMemoryAndKnowledgeUsageRows()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using var db = new NovelAgentDbContext(options);
        db.Users.Add(new User { Id = "user-1", Username = "u", Email = "u@example.com", PasswordHash = "h", Role = "author" });
        db.NovelProjects.Add(new NovelProject { Id = "project-a", UserId = "user-1", Title = "A" });
        db.NovelProjects.Add(new NovelProject { Id = "project-b", UserId = "user-1", Title = "B" });
        db.KnowledgeBases.Add(new KnowledgeBase { Id = "knowledge-1", ProjectId = "project-a", EntryType = "ReaderPromise", Title = "代价", Content = "胜利要有代价" });

        db.AgentChatTurns.Add(new AgentChatTurn { Id = "turn-1", SessionId = "session-1", UserId = "user-1", ProjectId = "project-a", TurnIndex = 1, Role = "user", Content = "记住这个设定" });
        db.AgentChatSummaries.Add(new AgentChatSummary { Id = "summary-1", SessionId = "session-1", UserId = "user-1", ProjectId = "project-a", StartTurn = 1, EndTurn = 10, SummaryType = "summary", Content = "用户确认设定" });
        db.AgentMemoryEvents.Add(new AgentMemoryEvent { Id = "event-1", UserId = "user-1", ProjectId = "project-a", SessionId = "session-1", SourceType = "knowledge_used", TriggerType = "tool_call", MemoryScope = "project", MemoryKey = "project.referenced_knowledge_ids", PayloadJson = """{"knowledgeId":"knowledge-1"}""" });
        db.AgentMemoryVersions.Add(new AgentMemoryVersion { UserId = "user-1", ProjectId = "project-a", SessionId = "session-1", Scope = "project", Version = 1 });
        db.ProjectKnowledgeUsages.Add(new ProjectKnowledgeUsage { Id = "usage-a", UserId = "user-1", ProjectId = "project-a", KnowledgeId = "knowledge-1", Status = "referenced", UsageCount = 1 });
        db.ProjectKnowledgeUsages.Add(new ProjectKnowledgeUsage { Id = "usage-b", UserId = "user-1", ProjectId = "project-b", KnowledgeId = "knowledge-1", Status = "imported", UsageCount = 0 });

        await db.SaveChangesAsync();

        Assert.Equal("referenced", await db.ProjectKnowledgeUsages.Where(x => x.ProjectId == "project-a").Select(x => x.Status).SingleAsync());
        Assert.Equal("imported", await db.ProjectKnowledgeUsages.Where(x => x.ProjectId == "project-b").Select(x => x.Status).SingleAsync());
        Assert.Equal(1, await db.AgentMemoryVersions.Where(x => x.Scope == "project").Select(x => x.Version).SingleAsync());
    }
}
