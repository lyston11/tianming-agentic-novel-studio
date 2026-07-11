using Xunit;
using System.Reflection;
using TM.Web.NovelAgentWeb.Support;

namespace Tests.Unit.Support;

public class AgentMemoryUpdateTests
{
    [Fact]
    public void DefaultConstructor_InitializesAllMemoryLayers()
    {
        var update = new AgentMemoryUpdate();

        Assert.NotNull(update.SessionMemory);
        Assert.NotNull(update.ProjectMemory);
        Assert.NotNull(update.AuthorMemory);
        Assert.NotNull(update.ExecutionMemory);
    }

    [Fact]
    public void SessionMemoryUpdate_CanSetAllFields()
    {
        var sessionUpdate = new SessionMemoryUpdate
        {
            ChatSummary = "本轮对话完成了章节大纲构思",
            ExtractedPreferences = new List<string> { "保持节奏紧凑", "避免冗长环境描写" }
        };

        Assert.Equal("本轮对话完成了章节大纲构思", sessionUpdate.ChatSummary);
        Assert.Equal(2, sessionUpdate.ExtractedPreferences.Count);
    }

    [Fact]
    public void ProjectMemoryUpdate_CanSetAllFields()
    {
        var projectUpdate = new ProjectMemoryUpdate
        {
            NewConstraints = new List<string> { "不要出现现代科技元素" }
        };

        Assert.Single(projectUpdate.NewConstraints);
    }

    [Fact]
    public void AuthorMemoryUpdate_CanSetAllFields()
    {
        var authorUpdate = new AuthorMemoryUpdate
        {
            DisplayName = "lyston",
            StyleLikes = new List<string> { "细腻的心理描写" },
            StyleDislikes = new List<string> { "过于啰嗦的描写", "重复的情绪渲染" },
            ConfirmationTolerance = "auto_low_risk",
            GenreHabits = new List<string> { "玄幻", "悬疑" },
            FavoriteKnowledgeIds = new List<string> { "kb-1" }
        };

        Assert.Equal("lyston", authorUpdate.DisplayName);
        Assert.Single(authorUpdate.StyleLikes);
        Assert.Equal(2, authorUpdate.StyleDislikes.Count);
        Assert.Equal("auto_low_risk", authorUpdate.ConfirmationTolerance);
        Assert.Equal(2, authorUpdate.GenreHabits.Count);
        Assert.Single(authorUpdate.FavoriteKnowledgeIds);
    }

    [Fact]
    public void ExecutionMemoryUpdate_CanSetAllFields()
    {
        var executionUpdate = new ExecutionMemoryUpdate
        {
            ToolSuccess = "WriteChapter 成功，质量门禁通过",
            ToolFailure = null,
            ToolFailurePatterns = new List<string> { "ValidateChapterDraft: continuity" },
            KnowledgeProcessingFailures = new List<string> { "knowledge-task-1: LLM settings missing" }
        };

        Assert.Equal("WriteChapter 成功，质量门禁通过", executionUpdate.ToolSuccess);
        Assert.Null(executionUpdate.ToolFailure);
        Assert.Single(executionUpdate.ToolFailurePatterns);
        Assert.Single(executionUpdate.KnowledgeProcessingFailures);
    }

    [Fact]
    public void AgentMemoryUpdate_IntegrationTest()
    {
        var update = new AgentMemoryUpdate
        {
            SessionMemory = new SessionMemoryUpdate
            {
                ChatSummary = "测试摘要",
                ExtractedPreferences = new List<string> { "偏好1" }
            },
            ProjectMemory = new ProjectMemoryUpdate
            {
                NewConstraints = new List<string> { "约束1" }
            },
            AuthorMemory = new AuthorMemoryUpdate
            {
                StyleLikes = new List<string> { "风格喜好1" },
                StyleDislikes = new List<string> { "风格反感1" }
            },
            ExecutionMemory = new ExecutionMemoryUpdate
            {
                ToolSuccess = "成功信息",
                ToolFailure = null
            }
        };

        Assert.Equal("测试摘要", update.SessionMemory.ChatSummary);
        Assert.Single(update.ProjectMemory.NewConstraints);
        Assert.Single(update.AuthorMemory.StyleLikes);
        Assert.Equal("成功信息", update.ExecutionMemory.ToolSuccess);
    }

    [Fact]
    public void ReflectionParser_ReadsTopLevelCamelCaseMemoryUpdate()
    {
        var reflection = ParseReflection("""
        {
          "summary": "ok",
          "memoryUpdate": {
            "sessionMemory": {
              "chatSummary": "用户确认偏好",
              "extractedPreferences": ["保持节奏紧凑"]
            },
            "projectMemory": {
              "newConstraints": ["不要出现现代科技元素"],
              "unresolvedThreads": ["主角身世之谜（第15章揭示）"]
            },
            "authorMemory": {
              "displayName": "lyston",
              "styleLikes": ["细腻心理描写"],
              "styleDislikes": ["重复情绪渲染"],
              "confirmationTolerance": "auto_low_risk",
              "genreHabits": ["玄幻"],
              "favoriteKnowledgeIds": ["kb-1"]
            },
            "executionMemory": {
              "toolSuccess": "WriteChapter 成功",
              "toolFailure": null,
              "toolFailurePatterns": ["ValidateChapterDraft: continuity"],
              "knowledgeProcessingFailures": ["knowledge-task-1: missing_llm_settings"]
            },
            "usedKnowledgeIds": ["kb-1"],
            "usedTropePatterns": ["反套路-误导线索"]
          }
        }
        """);

        var update = reflection.MissionPatch.MemoryUpdate;
        Assert.NotNull(update);
        Assert.Equal("用户确认偏好", update.SessionMemory.ChatSummary);
        Assert.Contains("保持节奏紧凑", update.SessionMemory.ExtractedPreferences);
        Assert.Contains("不要出现现代科技元素", update.ProjectMemory.NewConstraints);
        Assert.Equal("lyston", update.AuthorMemory.DisplayName);
        Assert.Contains("细腻心理描写", update.AuthorMemory.StyleLikes);
        Assert.Equal("auto_low_risk", update.AuthorMemory.ConfirmationTolerance);
        Assert.Contains("玄幻", update.AuthorMemory.GenreHabits);
        Assert.Contains("kb-1", update.AuthorMemory.FavoriteKnowledgeIds);
        Assert.Equal("WriteChapter 成功", update.ExecutionMemory.ToolSuccess);
        Assert.Contains("ValidateChapterDraft: continuity", update.ExecutionMemory.ToolFailurePatterns);
        Assert.Contains("knowledge-task-1: missing_llm_settings", update.ExecutionMemory.KnowledgeProcessingFailures);
        Assert.Contains("kb-1", update.UsedKnowledgeIds);
        Assert.Contains("反套路-误导线索", update.UsedTropePatterns);
    }

    [Fact]
    public void ReflectionParser_ReadsMissionPatchSnakeCaseMemoryUpdate()
    {
        var reflection = ParseReflection("""
        {
          "summary": "ok",
          "mission_patch": {
            "memory_update": {
              "session_memory": {
                "chat_summary": "蛇形命名摘要",
                "extracted_preferences": ["避免长段说明"]
              },
              "project_memory": {
                "new_constraints": ["只使用近未来科技"]
              },
              "author_memory": {
                "display_name": "lyston",
                "confirmation_tolerance": "key_checkpoints",
                "genre_habits": ["赛博悬疑"],
                "favorite_knowledge_ids": ["kb-2"]
              },
              "execution_memory": {
                "tool_failure_patterns": ["ProcessKnowledgeFile: bad json"],
                "knowledge_processing_failures": ["task-2: parse failed"]
              },
              "used_knowledge_ids": ["kb-2"],
              "used_trope_patterns": ["套路A"]
            }
          }
        }
        """);

        var update = reflection.MissionPatch.MemoryUpdate;
        Assert.NotNull(update);
        Assert.Equal("蛇形命名摘要", update.SessionMemory.ChatSummary);
        Assert.Contains("避免长段说明", update.SessionMemory.ExtractedPreferences);
        Assert.Contains("只使用近未来科技", update.ProjectMemory.NewConstraints);
        Assert.Equal("lyston", update.AuthorMemory.DisplayName);
        Assert.Equal("key_checkpoints", update.AuthorMemory.ConfirmationTolerance);
        Assert.Contains("赛博悬疑", update.AuthorMemory.GenreHabits);
        Assert.Contains("kb-2", update.AuthorMemory.FavoriteKnowledgeIds);
        Assert.Contains("ProcessKnowledgeFile: bad json", update.ExecutionMemory.ToolFailurePatterns);
        Assert.Contains("task-2: parse failed", update.ExecutionMemory.KnowledgeProcessingFailures);
        Assert.Contains("kb-2", update.UsedKnowledgeIds);
        Assert.Contains("套路A", update.UsedTropePatterns);
    }

    [Fact]
    public void ActionPrompt_IncludesConcreteAuthorDisplayName()
    {
        var prompt = BuildActionUserPrompt(new AgentObservationContext
        {
            UserMessage = "你知道我是谁吗？",
            AuthorMemory = new AgentAuthorMemory
            {
                DisplayName = "lyston"
            }
        });

        Assert.Contains("author_memory", prompt);
        Assert.Contains("display_name", prompt);
        Assert.Contains("lyston", prompt);
    }

    [Fact]
    public void NativeToolCallingPayload_IncludesConcreteMemoryContext()
    {
        var payload = BuildNativeToolCallingPayload(new AgentObservationContext
        {
            UserMessage = "你知道我是谁吗？",
            AuthorMemory = new AgentAuthorMemory
            {
                DisplayName = "lyston",
                StyleLikes = new List<string> { "爽文" }
            },
            ProjectMemory = new AgentProjectMemory
            {
                Constraints = new List<string> { "不要硬模板" }
            }
        });

        Assert.Contains("memory_context", payload);
        Assert.Contains("display_name", payload);
        Assert.Contains("lyston", payload);
        Assert.Contains("不要硬模板", payload);
    }

    [Fact]
    public void ActionPrompt_IncludesWorkspaceKnowledgeConstraintEvidence()
    {
        var prompt = BuildActionUserPrompt(new AgentObservationContext
        {
            UserMessage = "第二章会不会违背上一章知识？",
            WorkspaceState = new AgentWorkspaceState
            {
                KnowledgeBase = new AgentWorkspaceKnowledgeState
                {
                    RecentConstraintEvidence =
                    {
                        new AgentWorkspaceKnowledgeConstraintEvidenceState
                        {
                            KnowledgeId = "knowledge-1",
                            Title = "银蓝邮徽能力边界",
                            EntryType = "HardFact",
                            ConstraintLevel = "HardConstraint",
                            PackagePolicy = "DefaultEveryChapter",
                            ProjectId = "project-1",
                            ProjectTitle = "天命旧书",
                            ChapterId = "chapter-1",
                            GateStatus = "validated",
                            EvidenceStatus = "satisfied"
                        }
                    }
                }
            }
        });

        Assert.Contains("RecentConstraintEvidence", prompt);
        Assert.Contains("knowledge-1", prompt);
        Assert.Contains("satisfied", prompt);
    }

    private static AgentReflection ParseReflection(string json)
    {
        var method = typeof(AgentPlanner).GetMethod("ParseReflection", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(nameof(AgentPlanner), "ParseReflection");
        return Assert.IsType<AgentReflection>(method.Invoke(null, new object[] { json }));
    }

    private static string BuildActionUserPrompt(AgentObservationContext context)
    {
        var method = typeof(AgentPlanner).GetMethod("BuildActionUserPrompt", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(nameof(AgentPlanner), "BuildActionUserPrompt");
        return Assert.IsType<string>(method.Invoke(null, new object[] { context }));
    }

    private static string BuildNativeToolCallingPayload(AgentObservationContext context)
    {
        var method = typeof(ProviderToolCallingClient).GetMethod("BuildPlannerPayload", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(nameof(ProviderToolCallingClient), "BuildPlannerPayload");
        return Assert.IsType<string>(method.Invoke(null, new object[] { context }));
    }
}
