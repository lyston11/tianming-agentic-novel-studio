using Microsoft.Extensions.Logging.Abstractions;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.Services.Memory;
using TM.Web.NovelAgentWeb.Support;
using Xunit;

namespace Tests.Unit.Support;

public class AgentMemoryServiceTests
{
    [Fact]
    public async Task PersistAsync_AppliesReflectionMemoryUpdateToRepositoryAndSession()
    {
        var repository = new RecordingMemoryRepository
        {
            ProjectMemory = new ProjectMemory
            {
                Constraints = new List<string> { "旧约束" },
                UnresolvedThreads = new List<string> { "旧伏笔" },
                ReferencedKnowledgeIds = new List<string> { "kb-old" },
                UsedTropePatterns = new List<string> { "旧套路" }
            },
            AuthorMemory = new AuthorMemory
            {
                StyleLikes = new List<string> { "旧喜好" },
                StyleDislikes = new List<string> { "旧反感" }
            },
            ExecutionMemory = new ExecutionMemory
            {
                SuccessfulRepairNotes = new List<string> { "旧成功" },
                RepeatedBlockers = new List<string> { "旧失败" }
            }
        };
        var service = new AgentMemoryService(repository, NullLogger<AgentMemoryService>.Instance);
        var session = new AgentSession { UserId = "user-1" };
        session.WorkingMemory.SessionMemory.ShortTermPreferences.AddRange(new[]
        {
            "保持节奏紧凑",
            "保持节奏紧凑"
        });
        session.WorkingMemory.ProjectMemory = new AgentProjectMemory
        {
            ProjectId = "project-1",
            Constraints = new List<string> { "旧约束" },
            UnresolvedThreads = new List<string> { "旧伏笔" },
            ReferencedKnowledgeIds = new List<string> { "kb-old" },
            UsedTropePatterns = new List<string> { "旧套路" }
        };
        session.WorkingMemory.AuthorMemory = new AgentAuthorMemory
        {
            StyleLikes = new List<string> { "旧喜好" },
            StyleDislikes = new List<string> { "旧反感" }
        };
        session.WorkingMemory.ExecutionMemory = new AgentExecutionMemory
        {
            SuccessfulRepairNotes = new List<string> { "旧成功" },
            RepeatedBlockers = new List<string> { "旧失败" }
        };

        var reflection = new AgentReflection
        {
            MissionPatch = new AgentMissionPatch
            {
                MemoryUpdate = new AgentMemoryUpdate
                {
                    SessionMemory = new SessionMemoryUpdate
                    {
                        ChatSummary = "用户确认写作偏好",
                        ExtractedPreferences = new List<string> { "保持节奏紧凑" }
                    },
                    ProjectMemory = new ProjectMemoryUpdate
                    {
                        NewConstraints = new List<string> { "不要出现现代科技元素" },
                        UnresolvedThreads = new List<string> { "主角身世之谜（第15章揭示）" }
                    },
                    AuthorMemory = new AuthorMemoryUpdate
                    {
                        StyleLikes = new List<string> { "细腻心理描写" },
                        StyleDislikes = new List<string> { "重复情绪渲染" }
                    },
                    ExecutionMemory = new ExecutionMemoryUpdate
                    {
                        ToolSuccess = "WriteChapter 成功",
                        ToolFailure = "ValidateChapterDraft 发现节奏问题"
                    },
                    UsedKnowledgeIds = new List<string> { "kb-old", "kb-new" },
                    UsedTropePatterns = new List<string> { "旧套路", "反套路-误导线索" }
                }
            }
        };

        await service.PersistAsync(session, new NovelProjectInfo { Id = "project-1" }, new StoryBibleDocument(), reflection);

        Assert.Equal("用户确认写作偏好", session.WorkingMemory.SessionMemory.ChatSummary);
        Assert.Contains("保持节奏紧凑", session.WorkingMemory.ProjectMemory.Constraints);
        Assert.Contains("不要出现现代科技元素", session.WorkingMemory.ProjectMemory.Constraints);
        Assert.Contains("kb-new", session.WorkingMemory.ProjectMemory.ReferencedKnowledgeIds);
        Assert.Contains("反套路-误导线索", session.WorkingMemory.ProjectMemory.UsedTropePatterns);
        Assert.Contains("细腻心理描写", session.WorkingMemory.AuthorMemory.StyleLikes);
        Assert.Contains("WriteChapter 成功", session.WorkingMemory.ExecutionMemory.SuccessfulRepairNotes);

        Assert.True(repository.ProjectUpdates.TryGetValue("project.constraints", out var constraints));
        Assert.Contains("保持节奏紧凑", Assert.IsType<List<string>>(constraints));
        Assert.True(repository.ProjectUnionUpdates.TryGetValue("project.referenced_knowledge_ids", out var knowledgeIds));
        Assert.Contains("kb-new", Assert.IsType<List<string>>(knowledgeIds));
        Assert.True(repository.ProjectUnionUpdates.TryGetValue("execution.repeated_blockers", out var blockers));
        Assert.Contains("ValidateChapterDraft 发现节奏问题", Assert.IsType<List<string>>(blockers));
        Assert.True(repository.AuthorUpdates.TryGetValue("author.style_likes", out var styleLikes));
        Assert.Contains("细腻心理描写", Assert.IsType<List<string>>(styleLikes));
        Assert.False(repository.ProjectUpdates.ContainsKey("project.referenced_knowledge_ids"));
        Assert.False(repository.ProjectUpdates.ContainsKey("execution.repeated_blockers"));
    }

    [Fact]
    public async Task PersistAsync_WithNoMemoryUpdate_PersistsRuleBasedExecutionAndAuthorChanges()
    {
        var repository = new RecordingMemoryRepository();
        var service = new AgentMemoryService(repository, NullLogger<AgentMemoryService>.Instance);
        var session = new AgentSession { UserId = "user-1" };
        session.WorkingMemory.ExecutionMemory.RepeatedBlockers.Add("ValidateChapterDraft 失败：节奏拖慢");
        session.WorkingMemory.AuthorMemory.StyleDislikes.Add("文风重复");

        await service.PersistAsync(session, new NovelProjectInfo { Id = "project-1" }, new StoryBibleDocument(), new AgentReflection
        {
            QualityGate = new AgentQualityGateReport { Status = "fail", RewriteDecision = "节奏拖慢" }
        });

        Assert.True(repository.ProjectUnionUpdates.ContainsKey("execution.repeated_blockers"));
        Assert.True(repository.AuthorUnionUpdates.ContainsKey("author.style_dislikes"));
        Assert.False(repository.ProjectUpdates.ContainsKey("execution.repeated_blockers"));
        Assert.False(repository.AuthorUpdates.ContainsKey("author.style_dislikes"));
    }

    [Fact]
    public async Task PersistAsync_WithNoMemoryUpdate_MergesRepositoryListsBeforePersistingRuleBasedChanges()
    {
        var repository = new RecordingMemoryRepository
        {
            ProjectMemory = new ProjectMemory
            {
                ReferencedKnowledgeIds = new List<string> { "other-session-knowledge" },
                UsedTropePatterns = new List<string> { "other-session-pattern" }
            },
            AuthorMemory = new AuthorMemory
            {
                StyleDislikes = new List<string> { "other-session dislike" }
            },
            ExecutionMemory = new ExecutionMemory
            {
                RepeatedBlockers = new List<string> { "other-session blocker" },
                SuccessfulRepairNotes = new List<string> { "other-session repair" }
            }
        };
        var service = new AgentMemoryService(repository, NullLogger<AgentMemoryService>.Instance);
        var session = new AgentSession { UserId = "user-1" };
        session.WorkingMemory.ProjectMemory.ReferencedKnowledgeIds.Add("current-session-knowledge");
        session.WorkingMemory.ProjectMemory.UsedTropePatterns.Add("current-session-pattern");
        session.WorkingMemory.AuthorMemory.StyleDislikes.Add("current-session dislike");
        session.WorkingMemory.ExecutionMemory.RepeatedBlockers.Add("current-session blocker");
        session.WorkingMemory.ExecutionMemory.SuccessfulRepairNotes.Add("current-session repair");

        await service.PersistAsync(session, new NovelProjectInfo { Id = "project-1" }, new StoryBibleDocument(), new AgentReflection());

        Assert.True(repository.ProjectUnionUpdates.TryGetValue("execution.repeated_blockers", out var blockers));
        Assert.Contains("other-session blocker", Assert.IsType<List<string>>(blockers));
        Assert.Contains("current-session blocker", Assert.IsType<List<string>>(blockers));

        Assert.True(repository.ProjectUnionUpdates.TryGetValue("execution.successful_repairs", out var repairs));
        Assert.Contains("other-session repair", Assert.IsType<List<string>>(repairs));
        Assert.Contains("current-session repair", Assert.IsType<List<string>>(repairs));

        Assert.True(repository.AuthorUnionUpdates.TryGetValue("author.style_dislikes", out var dislikes));
        Assert.Contains("other-session dislike", Assert.IsType<List<string>>(dislikes));
        Assert.Contains("current-session dislike", Assert.IsType<List<string>>(dislikes));

        Assert.True(repository.ProjectUnionUpdates.TryGetValue("project.referenced_knowledge_ids", out var knowledgeIds));
        Assert.Contains("other-session-knowledge", Assert.IsType<List<string>>(knowledgeIds));
        Assert.Contains("current-session-knowledge", Assert.IsType<List<string>>(knowledgeIds));

        Assert.True(repository.ProjectUnionUpdates.TryGetValue("project.used_trope_patterns", out var tropePatterns));
        Assert.Contains("other-session-pattern", Assert.IsType<List<string>>(tropePatterns));
        Assert.Contains("current-session-pattern", Assert.IsType<List<string>>(tropePatterns));
    }

    private sealed class RecordingMemoryRepository : IAgentMemoryRepository
    {
        public ProjectMemory ProjectMemory { get; set; } = new();
        public SessionMemory SessionMemory { get; set; } = new();
        public AuthorMemory AuthorMemory { get; set; } = new();
        public ExecutionMemory ExecutionMemory { get; set; } = new();
        public Dictionary<string, object> ProjectUpdates { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, object> AuthorUpdates { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, object> ProjectUnionUpdates { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, object> AuthorUnionUpdates { get; } = new(StringComparer.OrdinalIgnoreCase);
        public int UpdateMemoryCallCount { get; private set; }

        public Task<ProjectMemory> GetProjectMemoryAsync(string userId, string projectId, CancellationToken ct = default)
            => Task.FromResult(ProjectMemory);

        public Task<SessionMemory> GetSessionMemoryAsync(string userId, string projectId, string sessionId, CancellationToken ct = default)
            => Task.FromResult(SessionMemory);

        public Task<AuthorMemory> GetAuthorMemoryAsync(string userId, CancellationToken ct = default)
            => Task.FromResult(AuthorMemory);

        public Task<ExecutionMemory> GetExecutionMemoryAsync(string userId, string projectId, CancellationToken ct = default)
            => Task.FromResult(ExecutionMemory);

        public Task UpdateFieldAsync(string userId, string? projectId, string memoryType, object value, CancellationToken ct = default)
        {
            (projectId == null ? AuthorUpdates : ProjectUpdates)[memoryType] = value;
            return Task.CompletedTask;
        }

        public Task UpdateMemoryAsync(string userId, string? projectId, Dictionary<string, object> updates, CancellationToken ct = default)
        {
            UpdateMemoryCallCount++;
            var target = projectId == null ? AuthorUpdates : ProjectUpdates;
            foreach (var (key, value) in updates)
                target[key] = value;
            return Task.CompletedTask;
        }

        public Task UnionMemoryAsync(string userId, string? projectId, Dictionary<string, IReadOnlyList<string>> updates, CancellationToken ct = default)
        {
            var target = projectId == null ? AuthorUnionUpdates : ProjectUnionUpdates;
            foreach (var (key, value) in updates)
                target[key] = value.ToList();
            return Task.CompletedTask;
        }

        public Task UpdateSessionMemoryAsync(string userId, string projectId, string sessionId, Dictionary<string, object> updates, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
