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
                ReferencedKnowledgeIds = new List<string> { "kb-old" },
                UsedTropePatterns = new List<string> { "旧套路" }
            },
            AuthorMemory = new AuthorMemory
            {
                DisplayName = "旧称呼",
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
            ReferencedKnowledgeIds = new List<string> { "kb-old" },
            UsedTropePatterns = new List<string> { "旧套路" }
        };
        session.WorkingMemory.AuthorMemory = new AgentAuthorMemory
        {
            DisplayName = "旧称呼",
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
                        NewConstraints = new List<string> { "不要出现现代科技元素" }
                    },
                    AuthorMemory = new AuthorMemoryUpdate
                    {
                        DisplayName = "lyston",
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
        Assert.Equal("lyston", session.WorkingMemory.AuthorMemory.DisplayName);
        Assert.Contains("细腻心理描写", session.WorkingMemory.AuthorMemory.StyleLikes);
        Assert.Contains("WriteChapter 成功", session.WorkingMemory.ExecutionMemory.SuccessfulRepairNotes);

        Assert.True(repository.ProjectUnionUpdates.TryGetValue("project.constraints", out var constraints));
        Assert.Contains("保持节奏紧凑", Assert.IsType<List<string>>(constraints));
        Assert.True(repository.ProjectUnionUpdates.TryGetValue("project.referenced_knowledge_ids", out var knowledgeIds));
        Assert.Contains("kb-new", Assert.IsType<List<string>>(knowledgeIds));
        Assert.True(repository.ProjectUnionUpdates.TryGetValue("execution.repeated_blockers", out var blockers));
        Assert.Contains("ValidateChapterDraft 发现节奏问题", Assert.IsType<List<string>>(blockers));
        Assert.True(repository.AuthorUnionUpdates.TryGetValue("author.style_likes", out var styleLikes));
        Assert.Contains("细腻心理描写", Assert.IsType<List<string>>(styleLikes));
        Assert.True(repository.AuthorUpdates.TryGetValue("author.display_name", out var displayName));
        Assert.Equal("lyston", Assert.IsType<string>(displayName));
        Assert.False(repository.ProjectUpdates.ContainsKey("project.constraints"));
        Assert.False(repository.AuthorUpdates.ContainsKey("author.style_likes"));
        Assert.False(repository.ProjectUpdates.ContainsKey("project.referenced_knowledge_ids"));
        Assert.False(repository.ProjectUpdates.ContainsKey("execution.repeated_blockers"));
    }

    [Fact]
    public async Task PersistAsync_RecordsPromotionWhenSessionPreferenceSedimentsIntoProjectConstraint()
    {
        var repository = new RecordingMemoryRepository();
        var service = new AgentMemoryService(repository, NullLogger<AgentMemoryService>.Instance);
        var session = new AgentSession
        {
            SessionId = "session-1",
            UserId = "user-1",
            RuntimeRunId = "runtime-run-1"
        };
        session.WorkingMemory.SessionMemory.ShortTermPreferences.AddRange(new[]
        {
            "章节要打怪升级",
            "章节要打怪升级"
        });

        await service.PersistAsync(session, new NovelProjectInfo { Id = "project-1" }, new StoryBibleDocument(), new AgentReflection
        {
            MissionPatch = new AgentMissionPatch
            {
                MemoryUpdate = new AgentMemoryUpdate
                {
                    SessionMemory = new SessionMemoryUpdate
                    {
                        ExtractedPreferences = new List<string> { "章节要打怪升级" }
                    }
                }
            }
        });

        var promotion = Assert.Single(repository.MemoryPromotions);
        Assert.Equal("user-1", promotion.UserId);
        Assert.Equal("project-1", promotion.ProjectId);
        Assert.Equal("session-1", promotion.SessionId);
        Assert.Equal("runtime-run-1", promotion.RunId);
        Assert.Equal("session", promotion.SourceScope);
        Assert.Equal("project", promotion.TargetScope);
        Assert.Equal("session.short_term_preferences", promotion.SourceMemoryKey);
        Assert.Equal("project.constraints", promotion.TargetMemoryKey);
        Assert.Equal("preference_sedimentation_threshold", promotion.PromotionReason);
        using var payload = System.Text.Json.JsonDocument.Parse(promotion.PayloadJson);
        Assert.Equal("章节要打怪升级", payload.RootElement.GetProperty("preference").GetString());
        Assert.Equal(3, payload.RootElement.GetProperty("threshold").GetInt32());
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

    [Fact]
    public async Task PersistAsync_PersistsAuthorAndExecutionMemoryFieldClosures()
    {
        var repository = new RecordingMemoryRepository
        {
            AuthorMemory = new AuthorMemory
            {
                GenreHabits = new List<string> { "旧题材" },
                FavoriteKnowledgeIds = new List<string> { "kb-old" },
                ConfirmationTolerance = "key_checkpoints"
            },
            ExecutionMemory = new ExecutionMemory
            {
                ToolFailurePatterns = new List<string> { "旧工具失败模式" },
                KnowledgeProcessingFailures = new List<string> { "旧知识处理失败" }
            }
        };
        var service = new AgentMemoryService(repository, NullLogger<AgentMemoryService>.Instance);
        var session = new AgentSession { UserId = "user-1" };
        session.WorkingMemory.AuthorMemory = new AgentAuthorMemory
        {
            GenreHabits = new List<string> { "旧题材" },
            FavoriteKnowledgeIds = new List<string> { "kb-old" },
            ConfirmationTolerance = "key_checkpoints"
        };
        session.WorkingMemory.ExecutionMemory = new AgentExecutionMemory
        {
            ToolFailurePatterns = new List<string> { "旧工具失败模式" },
            KnowledgeProcessingFailures = new List<string> { "旧知识处理失败" }
        };

        await service.PersistAsync(session, new NovelProjectInfo { Id = "project-1" }, new StoryBibleDocument(), new AgentReflection
        {
            MissionPatch = new AgentMissionPatch
            {
                MemoryUpdate = new AgentMemoryUpdate
                {
                    AuthorMemory = new AuthorMemoryUpdate
                    {
                        ConfirmationTolerance = "auto_low_risk",
                        GenreHabits = new List<string> { "赛博悬疑" },
                        FavoriteKnowledgeIds = new List<string> { "kb-new" }
                    },
                    ExecutionMemory = new ExecutionMemoryUpdate
                    {
                        ToolFailurePatterns = new List<string> { "ValidateChapterDraft: continuity" },
                        KnowledgeProcessingFailures = new List<string> { "knowledge-task-1: missing_llm_settings" }
                    }
                }
            }
        });

        Assert.Equal("auto_low_risk", session.WorkingMemory.AuthorMemory.ConfirmationTolerance);
        Assert.Contains("赛博悬疑", session.WorkingMemory.AuthorMemory.GenreHabits);
        Assert.Contains("kb-new", session.WorkingMemory.AuthorMemory.FavoriteKnowledgeIds);
        Assert.Contains("ValidateChapterDraft: continuity", session.WorkingMemory.ExecutionMemory.ToolFailurePatterns);
        Assert.Contains("knowledge-task-1: missing_llm_settings", session.WorkingMemory.ExecutionMemory.KnowledgeProcessingFailures);

        Assert.True(repository.AuthorUpdates.TryGetValue("author.confirmation_tolerance", out var confirmationTolerance));
        Assert.Equal("auto_low_risk", Assert.IsType<string>(confirmationTolerance));
        Assert.True(repository.AuthorUnionUpdates.TryGetValue("author.genre_habits", out var genreHabits));
        Assert.Contains("赛博悬疑", Assert.IsType<List<string>>(genreHabits));
        Assert.True(repository.AuthorUnionUpdates.TryGetValue("author.favorite_knowledge_ids", out var favoriteKnowledgeIds));
        Assert.Contains("kb-new", Assert.IsType<List<string>>(favoriteKnowledgeIds));
        Assert.True(repository.ProjectUnionUpdates.TryGetValue("execution.tool_failures", out var toolFailures));
        Assert.Contains("ValidateChapterDraft: continuity", Assert.IsType<List<string>>(toolFailures));
        Assert.True(repository.ProjectUnionUpdates.TryGetValue("execution.knowledge_processing_failures", out var knowledgeFailures));
        Assert.Contains("knowledge-task-1: missing_llm_settings", Assert.IsType<List<string>>(knowledgeFailures));
    }

    [Fact]
    public async Task PersistProjectlessAsync_PersistsAuthorDisplayNameFromReflection()
    {
        var repository = new RecordingMemoryRepository();
        var service = new AgentMemoryService(repository, NullLogger<AgentMemoryService>.Instance);
        var session = new AgentSession
        {
            SessionId = "session-1",
            UserId = "user-1"
        };

        await service.PersistProjectlessAsync(session, new AgentReflection
        {
            MissionPatch = new AgentMissionPatch
            {
                MemoryUpdate = new AgentMemoryUpdate
                {
                    AuthorMemory = new AuthorMemoryUpdate
                    {
                        DisplayName = "lyston"
                    }
                }
            }
        });

        Assert.Equal("lyston", session.WorkingMemory.AuthorMemory.DisplayName);
        Assert.True(repository.AuthorUpdates.TryGetValue("author.display_name", out var displayName));
        Assert.Equal("lyston", Assert.IsType<string>(displayName));
    }

    [Fact]
    public async Task PersistProjectlessAsync_UnionsLongTermAuthorListsFromReflection()
    {
        var repository = new RecordingMemoryRepository
        {
            AuthorMemory = new AuthorMemory
            {
                StyleLikes = new List<string> { "旧风格偏好" },
                GenreHabits = new List<string> { "旧类型习惯" },
                FavoriteKnowledgeIds = new List<string> { "kb-old" }
            }
        };
        var service = new AgentMemoryService(repository, NullLogger<AgentMemoryService>.Instance);
        var session = new AgentSession
        {
            SessionId = "session-1",
            UserId = "user-1"
        };

        await service.PersistProjectlessAsync(session, new AgentReflection
        {
            MissionPatch = new AgentMissionPatch
            {
                MemoryUpdate = new AgentMemoryUpdate
                {
                    AuthorMemory = new AuthorMemoryUpdate
                    {
                        StyleLikes = new List<string> { "喜欢快节奏升级" },
                        ConfirmationTolerance = "auto_low_risk",
                        GenreHabits = new List<string> { "末世打怪升级" },
                        FavoriteKnowledgeIds = new List<string> { "kb-new" }
                    }
                }
            }
        });

        Assert.True(repository.AuthorUpdates.TryGetValue("author.confirmation_tolerance", out var confirmationTolerance));
        Assert.Equal("auto_low_risk", Assert.IsType<string>(confirmationTolerance));
        Assert.True(repository.AuthorUnionUpdates.TryGetValue("author.style_likes", out var styleLikes));
        Assert.Contains("喜欢快节奏升级", Assert.IsType<List<string>>(styleLikes));
        Assert.True(repository.AuthorUnionUpdates.TryGetValue("author.genre_habits", out var genreHabits));
        Assert.Contains("末世打怪升级", Assert.IsType<List<string>>(genreHabits));
        Assert.True(repository.AuthorUnionUpdates.TryGetValue("author.favorite_knowledge_ids", out var favoriteKnowledgeIds));
        Assert.Contains("kb-new", Assert.IsType<List<string>>(favoriteKnowledgeIds));
        Assert.False(repository.AuthorUpdates.ContainsKey("author.style_likes"));
        Assert.False(repository.AuthorUpdates.ContainsKey("author.genre_habits"));
        Assert.False(repository.AuthorUpdates.ContainsKey("author.favorite_knowledge_ids"));
    }

    [Fact]
    public async Task PersistAsync_PersistsSessionRuntimeMemoryToSessionMemoryRepository()
    {
        var repository = new RecordingMemoryRepository();
        var service = new AgentMemoryService(repository, NullLogger<AgentMemoryService>.Instance);
        var session = new AgentSession
        {
            SessionId = "session-1",
            UserId = "user-1"
        };
        session.WorkingMemory.CurrentGoal = "推进第一章候选";
        session.WorkingMemory.OpenQuestions.Add("主角第一次失败的代价是什么？");
        session.WorkingMemory.UserPreferences.Add("避免现代网络梗");
        session.WorkingMemory.RecentObservations.Add(new AgentRuntimeObservation
        {
            ToolName = "PlanChapter",
            Message = "生成了三个章节候选",
            Phase = "chapter_candidates"
        });
        session.WorkingMemory.PendingToolCall = new AgentToolCall { Name = "SelectChapterCandidate" };
        session.WorkingMemory.LastDecision = new AgentDecision { Intent = "select_chapter_candidate" };

        await service.PersistAsync(session, new NovelProjectInfo { Id = "project-1" }, new StoryBibleDocument(), new AgentReflection());

        Assert.Equal("user-1", repository.LastSessionUserId);
        Assert.Equal("project-1", repository.LastSessionProjectId);
        Assert.Equal("session-1", repository.LastSessionId);
        Assert.True(repository.SessionUpdates.TryGetValue("session.current_goal", out var currentGoal));
        Assert.Equal("推进第一章候选", Assert.IsType<string>(currentGoal));
        Assert.True(repository.SessionUpdates.TryGetValue("session.open_questions", out var openQuestions));
        Assert.Contains("主角第一次失败的代价是什么？", Assert.IsType<List<string>>(openQuestions));
        Assert.True(repository.SessionUpdates.TryGetValue("session.short_term_preferences", out var preferences));
        Assert.Contains("避免现代网络梗", Assert.IsType<List<string>>(preferences));
        Assert.True(repository.SessionUpdates.TryGetValue("session.recent_observations", out var observations));
        Assert.Contains("PlanChapter: 生成了三个章节候选", Assert.IsType<List<string>>(observations));
        Assert.True(repository.SessionUpdates.TryGetValue("session.pending_tool_name", out var pendingToolName));
        Assert.Equal("SelectChapterCandidate", Assert.IsType<string>(pendingToolName));
        Assert.True(repository.SessionUpdates.TryGetValue("session.last_intent", out var lastIntent));
        Assert.Equal("select_chapter_candidate", Assert.IsType<string>(lastIntent));
    }

    [Fact]
    public async Task PersistAsync_NormalizesRepeatedSessionMemoryObservationPrefixes()
    {
        var repository = new RecordingMemoryRepository();
        var service = new AgentMemoryService(repository, NullLogger<AgentMemoryService>.Instance);
        var session = new AgentSession
        {
            SessionId = "session-1",
            UserId = "user-1"
        };
        session.WorkingMemory.RecentObservations.Add(new AgentRuntimeObservation
        {
            ObservationType = "session_memory",
            Message = "session_memory: session_memory: 用户希望主角叫陈默",
            Phase = "memory_restore",
            Success = true
        });
        session.WorkingMemory.RecentObservations.Add(new AgentRuntimeObservation
        {
            ObservationType = "session_memory",
            Message = "用户希望主角叫陈默",
            Phase = "memory_restore",
            Success = true
        });

        await service.PersistAsync(session, new NovelProjectInfo { Id = "project-1" }, new StoryBibleDocument(), new AgentReflection());

        Assert.True(repository.SessionUpdates.TryGetValue("session.recent_observations", out var observationsValue));
        var observations = Assert.IsType<List<string>>(observationsValue);
        var observation = Assert.Single(observations);
        Assert.Equal("session_memory: 用户希望主角叫陈默", observation);
        Assert.DoesNotContain("session_memory: session_memory", observation);
    }

    [Fact]
    public async Task HydrateAsync_RestoresSessionMemoryIntoRuntimeWorkingMemory()
    {
        var repository = new RecordingMemoryRepository
        {
            SessionMemory = new SessionMemory
            {
                CurrentGoal = "缓存里的当前目标",
                OpenQuestions = new List<string> { "缓存里的问题" },
                ShortTermPreferences = new List<string> { "缓存里的偏好" },
                RecentObservations = new List<string> { "缓存里的观察" },
                PendingToolName = "PlanChapter",
                LastIntent = "plan_chapter"
            }
        };
        var service = new AgentMemoryService(repository, NullLogger<AgentMemoryService>.Instance);
        var session = new AgentSession
        {
            SessionId = "session-1",
            UserId = "user-1"
        };

        await service.HydrateAsync(session, new NovelProjectInfo { Id = "project-1" }, new StoryBibleDocument());

        Assert.Equal("缓存里的当前目标", session.WorkingMemory.CurrentGoal);
        Assert.Contains("缓存里的问题", session.WorkingMemory.OpenQuestions);
        Assert.Contains("缓存里的偏好", session.WorkingMemory.UserPreferences);
        Assert.Equal("缓存里的当前目标", session.WorkingMemory.SessionMemory.CurrentGoal);
        Assert.Equal("PlanChapter", session.WorkingMemory.SessionMemory.PendingToolName);
        Assert.Equal("plan_chapter", session.WorkingMemory.SessionMemory.LastIntent);
    }

    [Fact]
    public async Task HydrateAsync_PassesRuntimeRunIdToAllMemoryReads()
    {
        var repository = new RecordingMemoryRepository();
        var service = new AgentMemoryService(repository, NullLogger<AgentMemoryService>.Instance);
        var session = new AgentSession
        {
            SessionId = "session-1",
            UserId = "user-1",
            RuntimeRunId = "runtime-run-1"
        };

        await service.HydrateAsync(session, new NovelProjectInfo { Id = "project-1" }, new StoryBibleDocument());

        Assert.Equal("runtime-run-1", repository.LastSessionReadRunId);
        Assert.Equal("runtime-run-1", repository.LastProjectReadRunId);
        Assert.Equal("runtime-run-1", repository.LastAuthorReadRunId);
        Assert.Equal("runtime-run-1", repository.LastExecutionReadRunId);
    }

    [Fact]
    public async Task HydrateProjectlessAsync_PassesRuntimeRunIdToSessionAuthorAndExecutionReads()
    {
        var repository = new RecordingMemoryRepository();
        var service = new AgentMemoryService(repository, NullLogger<AgentMemoryService>.Instance);
        var session = new AgentSession
        {
            SessionId = "session-1",
            UserId = "user-1",
            RuntimeRunId = "runtime-run-1"
        };

        await service.HydrateProjectlessAsync(session);

        Assert.Equal("runtime-run-1", repository.LastSessionReadRunId);
        Assert.Equal("runtime-run-1", repository.LastAuthorReadRunId);
        Assert.Equal("runtime-run-1", repository.LastExecutionReadRunId);
    }

    [Fact]
    public async Task HydrateProjectlessAsync_RestoresSessionAuthorAndExecutionMemory()
    {
        var repository = new RecordingMemoryRepository
        {
            SessionMemory = new SessionMemory
            {
                CurrentGoal = "先闲聊确认设定偏好",
                OpenQuestions = new List<string> { "用户是否想从旧项目继续？" },
                ShortTermPreferences = new List<string> { "不要自动绑定项目" },
                RecentObservations = new List<string> { "用户强调由 Agent 决策项目" },
                PendingToolName = "QueryWorkspaceState",
                LastIntent = "workspace_question"
            },
            AuthorMemory = new AuthorMemory
            {
                StyleDislikes = new List<string> { "硬规则路由" },
                ConfirmationTolerance = "auto_low_risk"
            },
            ExecutionMemory = new ExecutionMemory
            {
                RepeatedBlockers = new List<string> { "No active project in session" },
                ToolFailurePatterns = new List<string> { "projectless status query should use workspace state" }
            }
        };
        var service = new AgentMemoryService(repository, NullLogger<AgentMemoryService>.Instance);
        var session = new AgentSession
        {
            SessionId = "session-1",
            UserId = "user-1"
        };

        await service.HydrateProjectlessAsync(session);

        Assert.Equal("先闲聊确认设定偏好", session.WorkingMemory.CurrentGoal);
        Assert.Contains("不要自动绑定项目", session.WorkingMemory.UserPreferences);
        Assert.Contains("硬规则路由", session.WorkingMemory.AuthorMemory.StyleDislikes);
        Assert.Equal("auto_low_risk", session.WorkingMemory.AuthorMemory.ConfirmationTolerance);
        Assert.Contains("No active project in session", session.WorkingMemory.ExecutionMemory.RepeatedBlockers);
        Assert.Contains("projectless status query should use workspace state", session.WorkingMemory.ExecutionMemory.ToolFailurePatterns);
        Assert.DoesNotContain("No active project in session", session.WorkingMemory.ProjectMemory.Constraints);
    }

    [Fact]
    public async Task PersistProjectlessAsync_PersistsSessionAuthorAndExecutionMemoryButSkipsProjectMemory()
    {
        var repository = new RecordingMemoryRepository();
        var service = new AgentMemoryService(repository, NullLogger<AgentMemoryService>.Instance);
        var session = new AgentSession
        {
            SessionId = "session-1",
            UserId = "user-1"
        };
        session.WorkingMemory.CurrentGoal = "理解用户想先聊天还是开书";
        session.WorkingMemory.UserPreferences.Add("低风险工具自动执行");

        var reflection = new AgentReflection
        {
            MissionPatch = new AgentMissionPatch
            {
                MemoryUpdate = new AgentMemoryUpdate
                {
                    SessionMemory = new SessionMemoryUpdate
                    {
                        ExtractedPreferences = new List<string> { "由大模型自己决定项目绑定" }
                    },
                    ProjectMemory = new ProjectMemoryUpdate
                    {
                        NewConstraints = new List<string> { "不应写入无项目阶段" }
                    },
                    AuthorMemory = new AuthorMemoryUpdate
                    {
                        StyleDislikes = new List<string> { "死规则关键词路由" }
                    },
                    ExecutionMemory = new ExecutionMemoryUpdate
                    {
                        ToolFailure = "No active project in session",
                        ToolFailurePatterns = new List<string> { "无项目状态查询误走项目工具" }
                    }
                }
            }
        };

        await service.PersistProjectlessAsync(session, reflection);

        Assert.Equal("user-1", repository.LastSessionUserId);
        Assert.Equal("projectless", repository.LastSessionProjectId);
        Assert.Equal("session-1", repository.LastSessionId);
        Assert.True(repository.SessionUpdates.TryGetValue("session.current_goal", out var currentGoal));
        Assert.Equal("理解用户想先聊天还是开书", Assert.IsType<string>(currentGoal));
        Assert.True(repository.SessionUpdates.TryGetValue("session.short_term_preferences", out var preferences));
        Assert.Contains("由大模型自己决定项目绑定", Assert.IsType<List<string>>(preferences));
        Assert.True(repository.AuthorUnionUpdates.TryGetValue("author.style_dislikes", out var dislikes));
        Assert.Contains("死规则关键词路由", Assert.IsType<List<string>>(dislikes));
        Assert.True(repository.ProjectlessExecutionUnionUpdates.TryGetValue("execution.repeated_blockers", out var blockers));
        Assert.Contains("No active project in session", Assert.IsType<List<string>>(blockers));
        Assert.True(repository.ProjectlessExecutionUnionUpdates.TryGetValue("execution.tool_failures", out var failures));
        Assert.Contains("无项目状态查询误走项目工具", Assert.IsType<List<string>>(failures));
        Assert.Empty(repository.ProjectUpdates);
        Assert.Empty(repository.ProjectUnionUpdates);
    }

    private sealed class RecordingMemoryRepository : IAgentMemoryRepository
    {
        public ProjectMemory ProjectMemory { get; set; } = new();
        public SessionMemory SessionMemory { get; set; } = new();
        public AuthorMemory AuthorMemory { get; set; } = new();
        public ExecutionMemory ExecutionMemory { get; set; } = new();
        public Dictionary<string, object> ProjectUpdates { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, object> AuthorUpdates { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, object> SessionUpdates { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, object> ProjectUnionUpdates { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, object> AuthorUnionUpdates { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, object> ProjectlessExecutionUnionUpdates { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<MemoryPromotionRecord> MemoryPromotions { get; } = new();
        public int UpdateMemoryCallCount { get; private set; }
        public string LastProjectReadRunId { get; private set; } = string.Empty;
        public string LastSessionReadRunId { get; private set; } = string.Empty;
        public string LastAuthorReadRunId { get; private set; } = string.Empty;
        public string LastExecutionReadRunId { get; private set; } = string.Empty;
        public string LastSessionUserId { get; private set; } = string.Empty;
        public string LastSessionProjectId { get; private set; } = string.Empty;
        public string LastSessionId { get; private set; } = string.Empty;

        public Task<ProjectMemory> GetProjectMemoryAsync(string userId, string projectId, CancellationToken ct = default, string? runId = null, string? sessionId = null)
        {
            LastProjectReadRunId = runId ?? string.Empty;
            return Task.FromResult(ProjectMemory);
        }

        public Task<SessionMemory> GetSessionMemoryAsync(string userId, string projectId, string sessionId, CancellationToken ct = default, string? runId = null)
        {
            LastSessionReadRunId = runId ?? string.Empty;
            return Task.FromResult(SessionMemory);
        }

        public Task<AuthorMemory> GetAuthorMemoryAsync(string userId, CancellationToken ct = default, string? runId = null, string? sessionId = null)
        {
            LastAuthorReadRunId = runId ?? string.Empty;
            return Task.FromResult(AuthorMemory);
        }

        public Task<ExecutionMemory> GetExecutionMemoryAsync(string userId, string projectId, CancellationToken ct = default, string? runId = null, string? sessionId = null)
        {
            LastExecutionReadRunId = runId ?? string.Empty;
            return Task.FromResult(ExecutionMemory);
        }

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
            var target = projectId == null
                ? AuthorUnionUpdates
                : string.Equals(projectId, "projectless", StringComparison.OrdinalIgnoreCase)
                    ? ProjectlessExecutionUnionUpdates
                    : ProjectUnionUpdates;
            foreach (var (key, value) in updates)
                target[key] = value.ToList();
            return Task.CompletedTask;
        }

        public Task UpdateSessionMemoryAsync(string userId, string projectId, string sessionId, Dictionary<string, object> updates, CancellationToken ct = default)
        {
            LastSessionUserId = userId;
            LastSessionProjectId = projectId;
            LastSessionId = sessionId;
            foreach (var (key, value) in updates)
                SessionUpdates[key] = value;
            return Task.CompletedTask;
        }

        public Task RecordMemoryPromotionAsync(MemoryPromotionRecord record, CancellationToken ct = default)
        {
            MemoryPromotions.Add(record);
            return Task.CompletedTask;
        }
    }
}
