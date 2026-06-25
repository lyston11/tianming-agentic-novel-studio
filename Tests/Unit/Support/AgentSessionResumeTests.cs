using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Services.Memory;
using TM.Web.NovelAgentWeb.Services.AgentRuntime;
using TM.Web.NovelAgentWeb.Services.AgentSessions;
using TM.Web.NovelAgentWeb.Services.AgentTools;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Support;
using Xunit;

namespace Tests.Unit.Support;

public class AgentSessionResumeTests
{
    [Fact]
    public async Task SaveAndReloadSession_PreservesPendingConfirmationForResume()
    {
        await using var db = CreateDb();
        var manager = new AgentSessionManager(db, FixedUser("user-1"));
        var session = new AgentSession
        {
            SessionId = "session-1",
            UserId = "user-1",
            ActiveProjectId = "project-1",
            Phase = "awaiting_confirmation",
            WorkingMemory = new AgentWorkingMemory
            {
                PendingToolCall = new AgentToolCall
                {
                    Name = "ProduceChapter",
                    Arguments = new Dictionary<string, string>
                    {
                        ["runId"] = "run-1",
                        ["commitPolicy"] = "auto_commit"
                    }
                },
                PendingConfirmation = new AgentPendingConfirmation
                {
                    ToolCall = new AgentToolCall
                    {
                        Name = "ProduceChapter",
                        Arguments = new Dictionary<string, string>
                        {
                            ["runId"] = "run-1",
                            ["commitPolicy"] = "auto_commit"
                        }
                    },
                    Risk = "High",
                    ProjectId = "project-1",
                    RunId = "run-1",
                    ImpactSummary = "执行章节生产闭环并提交书城"
                }
            }
        };

        await manager.SaveSessionAsync(session);

        var restored = await manager.GetSessionAsync("session-1");

        Assert.NotNull(restored);
        Assert.NotNull(restored!.WorkingMemory.PendingToolCall);
        Assert.Equal("ProduceChapter", restored.WorkingMemory.PendingToolCall!.Name);
        Assert.NotNull(restored.WorkingMemory.PendingConfirmation);
        Assert.Equal("ProduceChapter", restored.WorkingMemory.PendingConfirmation!.ToolCall!.Name);
        Assert.Equal("run-1", restored.WorkingMemory.PendingConfirmation.RunId);
    }

    [Fact]
    public async Task ResumeAsync_ReturnsToolCacheSnapshotAndPendingConfirmation()
    {
        await using var db = CreateDb();
        var manager = new AgentSessionManager(db, FixedUser("user-1"));
        var session = new AgentSession
        {
            SessionId = "session-1",
            UserId = "user-1",
            ActiveProjectId = "project-1",
            ActiveRunId = "run-1",
            Phase = "chapter_candidates",
            DiscoveredPhase = "Planning",
            ToolSearchCacheVersion = "project=1|session=1",
            LastToolSearchAt = DateTime.UtcNow,
            DiscoveredTools = new List<ToolSchema>
            {
                new()
                {
                    Name = "PlanChapter",
                    Description = "规划章节",
                    Risk = "Medium",
                    Parameters = new Dictionary<string, string> { ["creativeBrief"] = "string" }
                }
            },
            WorkingMemory = new AgentWorkingMemory
            {
                PendingConfirmation = new AgentPendingConfirmation
                {
                    ToolCall = new AgentToolCall
                    {
                        Name = "ProduceChapter",
                        Arguments = new Dictionary<string, string> { ["commitPolicy"] = "auto_commit" }
                    },
                    ProjectId = "project-1",
                    RunId = "run-1"
                }
            }
        };
        await manager.SaveSessionAsync(session);

        var toolCache = new Mock<IToolSearchCacheService>();
        toolCache
            .Setup(x => x.GetAsync(
                It.Is<AgentSession>(s => s.SessionId == "session-1"),
                "Planning",
                It.Is<string>(signature => !string.IsNullOrWhiteSpace(signature)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolSearchCacheLookup(
                new List<ToolSchema>
                {
                    new()
                    {
                        Name = "PlanChapter",
                        Description = "规划章节",
                        Risk = "Medium",
                        Parameters = new Dictionary<string, string> { ["creativeBrief"] = "string" }
                    }
                },
                "sqlite-snapshot"));

        var toolLedger = new Mock<IAgentToolExecutionLedger>();
        toolLedger
            .Setup(x => x.GetRecentAsync("user-1", "session-1", "project-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new AgentToolExecutionSnapshot
                {
                    Id = "tool-execution-1",
                    ToolName = "PlanChapter",
                    Status = "succeeded",
                    Phase = "Planning",
                    ResultPhase = "chapter_candidates",
                    ResultMessage = "章节候选已生成",
                    StartedAt = DateTime.UtcNow.AddSeconds(-2),
                    CompletedAt = DateTime.UtcNow
                }
            });

        var runtimeRuns = new AgentRuntimeRunService(db);
        var runtimeEvents = new AgentRuntimeEventService(db);
        var activeRun = await runtimeRuns.CreateQueuedAsync(new CreateAgentRuntimeRunRequest(
            UserId: "user-1",
            SessionId: "session-1",
            ProjectId: "project-1",
            UserMessage: "继续生成章节候选"));
        await runtimeEvents.AppendAsync(new CreateAgentRuntimeEventRequest(
            activeRun.Id,
            "user-1",
            "session-1",
            "project-1",
            "run_update",
            "正在等待模型生成正文与修订记录。",
            new { status = "running", phase = "draft_generation" },
            Stage: "draft_generation",
            Status: "running",
            ArtifactType: "chapter_draft",
            ArtifactId: "chapter-002",
            DisplaySurface: AgentRuntimeEventSurface.Workflow,
            DisplayPolicy: AgentRuntimeEventDisplayPolicy.Timeline));
        var registry = new AgentToolRegistry(
            UserSettingsTestFactory.CreateDbBacked(),
            new ServiceCollection().BuildServiceProvider(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AgentToolRegistry>.Instance);
        var service = new AgentSessionResumeService(manager, toolCache.Object, toolLedger.Object, runtimeRuns, runtimeEvents, registry);

        var response = await service.ResumeAsync("session-1");

        Assert.Equal("session-1", response.SessionId);
        Assert.Equal("project-1", response.ActiveProjectId);
        Assert.Equal(activeRun.Id, response.ActiveRunId);
        Assert.True(response.ToolSearchCacheFresh);
        Assert.Equal("sqlite-snapshot", response.ToolSearchCacheSource);
        Assert.Equal("Planning", response.DiscoveredPhase);
        Assert.Single(response.DiscoveredTools);
        Assert.Equal("PlanChapter", response.DiscoveredTools[0].Name);
        Assert.True(response.HasPendingConfirmation);
        Assert.NotNull(response.PendingConfirmation);
        Assert.Equal("ProduceChapter", response.PendingConfirmation!.ToolCall!.Name);
        Assert.Single(response.RecentToolExecutions);
        Assert.Equal("PlanChapter", response.RecentToolExecutions[0].ToolName);
        Assert.Equal("chapter_candidates", response.RecentToolExecutions[0].ResultPhase);
        Assert.Single(response.RecentRuntimeEvents);
        Assert.Equal("run_update", response.RecentRuntimeEvents[0].Type);
        Assert.Equal(activeRun.Id, response.RecentRuntimeEvents[0].RunId);
        Assert.Equal("draft_generation", response.RecentRuntimeEvents[0].Stage);
        Assert.Equal("running", response.RecentRuntimeEvents[0].Status);
        Assert.Equal("chapter_draft", response.RecentRuntimeEvents[0].ArtifactType);
        Assert.Equal("chapter-002", response.RecentRuntimeEvents[0].ArtifactId);
        Assert.Equal(AgentRuntimeEventSurface.Workflow, response.RecentRuntimeEvents[0].DisplaySurface);
        Assert.Equal(AgentRuntimeEventDisplayPolicy.Timeline, response.RecentRuntimeEvents[0].DisplayPolicy);
        Assert.Equal("running", response.RecentRuntimeEvents[0].Data.GetProperty("status").GetString());
    }

    [Fact]
    public async Task SaveSessionAsync_DoesNotStoreChatHistoryInSessionData()
    {
        await using var db = CreateDb();
        var manager = new AgentSessionManager(db, FixedUser("user-1"));
        var session = new AgentSession
        {
            SessionId = "session-1",
            UserId = "user-1",
            ActiveProjectId = "project-1",
            ChatHistory =
            {
                new AgentConversationTurn
                {
                    Role = "user",
                    Content = "这段聊天正文必须只在 chat turns 表里作为 truth"
                }
            }
        };

        await manager.SaveSessionAsync(session);

        var row = await db.AgentSessions.SingleAsync(s => s.Id == "session-1");
        Assert.DoesNotContain("chatHistory", row.SessionData);
        Assert.DoesNotContain("这段聊天正文必须只在 chat turns 表里作为 truth", row.SessionData);
    }

    [Fact]
    public async Task SaveSessionAsync_DoesNotStoreToolSchemaPayloadInSessionData()
    {
        await using var db = CreateDb();
        var manager = new AgentSessionManager(db, FixedUser("user-1"));
        var session = new AgentSession
        {
            SessionId = "session-1",
            UserId = "user-1",
            ActiveProjectId = "project-1",
            DiscoveredPhase = "Planning",
            ToolSearchCacheVersion = "project=1",
            LastToolSearchAt = DateTime.UtcNow,
            DiscoveredTools =
            {
                new ToolSchema
                {
                    Name = "PlanChapter",
                    Description = "工具 schema 必须只在 Redis/tool cache 中作为热状态",
                    Risk = "Medium"
                }
            }
        };

        await manager.SaveSessionAsync(session);

        var row = await db.AgentSessions.SingleAsync(s => s.Id == "session-1");
        Assert.Contains("toolSearchCache", row.SessionData);
        Assert.Contains("project=1", row.SessionData);
        Assert.DoesNotContain("PlanChapter", row.SessionData);
        Assert.DoesNotContain("工具 schema 必须只在 Redis/tool cache 中作为热状态", row.SessionData);
    }

    [Fact]
    public async Task SaveSessionAsync_DoesNotStoreLongTermMemorySnapshotsInSessionData()
    {
        await using var db = CreateDb();
        var manager = new AgentSessionManager(db, FixedUser("user-1"));
        var session = new AgentSession
        {
            SessionId = "session-1",
            UserId = "user-1",
            ActiveProjectId = "project-1",
            WorkingMemory = new AgentWorkingMemory
            {
                CurrentGoal = "short-goal-can-resume",
                ProjectMemory = new AgentProjectMemory { LongTermGoal = "project-long-memory-must-not-be-in-session-data" },
                AuthorMemory = new AgentAuthorMemory { StyleLikes = { "author-long-memory-must-not-be-in-session-data" } },
                ExecutionMemory = new AgentExecutionMemory { SuccessfulRepairNotes = { "execution-long-memory-must-not-be-in-session-data" } }
            }
        };

        await manager.SaveSessionAsync(session);

        var row = await db.AgentSessions.SingleAsync(s => s.Id == "session-1");
        Assert.DoesNotContain("workingMemory", row.SessionData);
        Assert.DoesNotContain("short-goal-can-resume", row.SessionData);
        Assert.DoesNotContain("project-long-memory-must-not-be-in-session-data", row.SessionData);
        Assert.DoesNotContain("author-long-memory-must-not-be-in-session-data", row.SessionData);
        Assert.DoesNotContain("execution-long-memory-must-not-be-in-session-data", row.SessionData);
    }

    [Fact]
    public async Task SaveSessionAsync_StoresOnlyMissionPointersInSessionData()
    {
        await using var db = CreateDb();
        var manager = new AgentSessionManager(db, FixedUser("user-1"));
        var session = new AgentSession
        {
            SessionId = "session-1",
            UserId = "user-1",
            ActiveProjectId = "project-1",
            ActiveRunId = "run-1",
            Phase = "chapter_candidates",
            WorkingMemory = new AgentWorkingMemory
            {
                Mission = new AgentMissionState
                {
                    CurrentGoal = "完整 MissionState 不应该写入 session_data",
                    PendingQuestion = "完整开放问题不应该写入 session_data"
                },
                MissionPlan = new AgentMissionPlan
                {
                    MissionId = "mission-1",
                    ProjectId = "project-1",
                    ProjectTitle = "测试小说",
                    CurrentRunId = "run-1",
                    Stage = "chapter",
                    Status = "active",
                    ActiveChapterId = "chapter-001",
                    ActiveTurnId = "turn-1",
                    ActiveToolTransactionId = "tool-tx-1",
                    ArtifactCursor = "artifact-cursor-1",
                    SchedulerState = new AgentTaskSchedulerState
                    {
                        ActiveTaskId = "task-1",
                        ActiveRunId = "run-1",
                        ActiveChapterId = "chapter-001",
                        Tasks =
                        {
                            new AgentScheduledTask
                            {
                                TaskId = "task-1",
                                NextAction = "这个任务树正文不应该写入 session_data"
                            }
                        }
                    },
                    BookTaskTree = new AgentBookTaskTree
                    {
                        Title = "完整书级任务树不应该写入 session_data",
                        Volumes =
                        {
                            new AgentVolumeTask
                            {
                                VolumeId = "volume-001",
                                Chapters =
                                {
                                    new AgentChapterTask
                                    {
                                        ChapterId = "chapter-001",
                                        UserVisibleStatus = "完整章节任务树不应该写入 session_data"
                                    }
                                }
                            }
                        }
                    },
                    ToolTransactions =
                    {
                        new ToolTransaction
                        {
                            ToolName = "PlanChapter",
                            ExecutionState = "完整工具交易不应该写入 session_data"
                        }
                    },
                    ActiveArtifacts =
                    {
                        new AgentToolArtifact
                        {
                            ArtifactId = "artifact-1",
                            Summary = "完整 artifact payload 不应该写入 session_data"
                        }
                    }
                }
            }
        };

        await manager.SaveSessionAsync(session);

        var row = await db.AgentSessions.SingleAsync(s => s.Id == "session-1");
        Assert.Contains("missionPointer", row.SessionData);
        Assert.Contains("mission-1", row.SessionData);
        Assert.Contains("task-1", row.SessionData);
        Assert.Contains("chapter-001", row.SessionData);
        Assert.DoesNotContain("missionPlan", row.SessionData);
        Assert.DoesNotContain("MissionPlan", row.SessionData);
        Assert.DoesNotContain("完整 MissionState 不应该写入 session_data", row.SessionData);
        Assert.DoesNotContain("这个任务树正文不应该写入 session_data", row.SessionData);
        Assert.DoesNotContain("完整书级任务树不应该写入 session_data", row.SessionData);
        Assert.DoesNotContain("完整工具交易不应该写入 session_data", row.SessionData);
        Assert.DoesNotContain("完整 artifact payload 不应该写入 session_data", row.SessionData);
    }

    [Fact]
    public async Task GetSessionAsync_HydratesChatHistoryFromRepositoryHotWindow()
    {
        await using var db = CreateDb();
        db.AgentSessions.Add(new TM.Web.NovelAgentWeb.Data.Entities.AgentSession
        {
            Id = "session-1",
            UserId = "user-1",
            ProjectId = "project-1",
            Title = "会话",
            SessionData = "{\"phase\":\"Planning\"}"
        });
        await db.SaveChangesAsync();

        var chat = new Mock<IChatHistoryRepository>();
        chat.Setup(x => x.GetHotWindowAsync("user-1", "project-1", "session-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new ChatHistoryTurnDto("user", "Redis 热窗口用户消息", DateTime.UtcNow),
                new ChatHistoryTurnDto("assistant", "Redis 热窗口回复", DateTime.UtcNow)
            });
        var manager = new AgentSessionManager(db, FixedUser("user-1"), chat.Object);

        var restored = await manager.GetSessionAsync("session-1");

        Assert.NotNull(restored);
        Assert.Equal(2, restored!.ChatHistory.Count);
        Assert.Equal("Redis 热窗口用户消息", restored.ChatHistory[0].Content);
        Assert.Equal("Redis 热窗口回复", restored.ChatHistory[1].Content);
    }

    private static NovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NovelAgentDbContext(options);
    }

    private static ICurrentUserService FixedUser(string userId)
    {
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(x => x.GetUserId()).Returns(userId);
        currentUser.Setup(x => x.TryGetUserId()).Returns(userId);
        currentUser.Setup(x => x.IsAuthenticated()).Returns(true);
        currentUser.Setup(x => x.IsAdmin()).Returns(false);
        return currentUser.Object;
    }
}
