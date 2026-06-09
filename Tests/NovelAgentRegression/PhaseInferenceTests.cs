using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.Support;
using Xunit;

namespace TM.Tests.NovelAgentRegression;

public class PhaseInferenceTests
{
    private readonly PhaseInference _phaseInference;

    public PhaseInferenceTests()
    {
        _phaseInference = new PhaseInference();
    }

    #region ClassifyIntent Tests

    [Fact]
    public void ClassifyIntent_StatusQuery_ReturnsStatusQuery()
    {
        var session = CreateSession();

        var result = _phaseInference.ClassifyIntent("写了吗", session);

        Assert.Equal(TurnIntentType.StatusQuery, result);
    }

    [Fact]
    public void ClassifyIntent_StatusQueryWithProgress_ReturnsStatusQuery()
    {
        var session = CreateSession();

        var result = _phaseInference.ClassifyIntent("现在进度怎么样", session);

        Assert.Equal(TurnIntentType.StatusQuery, result);
    }

    [Fact]
    public void ClassifyIntent_StatusQueryWithComplete_ReturnsStatusQuery()
    {
        var session = CreateSession();

        var result = _phaseInference.ClassifyIntent("任务完成了吗", session);

        Assert.Equal(TurnIntentType.StatusQuery, result);
    }

    [Fact]
    public void ClassifyIntent_ConfirmationWithPendingToolCall_ReturnsConfirmation()
    {
        var session = CreateSession(withPendingToolCall: true);

        var result = _phaseInference.ClassifyIntent("好的", session);

        Assert.Equal(TurnIntentType.Confirmation, result);
    }

    [Fact]
    public void ClassifyIntent_ConfirmationKeyword_ReturnsConfirmation()
    {
        var session = CreateSession(withPendingToolCall: true);

        var result = _phaseInference.ClassifyIntent("可以继续", session);

        Assert.Equal(TurnIntentType.Confirmation, result);
    }

    [Fact]
    public void ClassifyIntent_ConfirmationWithoutPendingToolCall_DoesNotReturnConfirmation()
    {
        var session = CreateSession(withPendingToolCall: false);

        var result = _phaseInference.ClassifyIntent("好的", session);

        // Should be FreeChat since message is short and no pending tool call
        Assert.Equal(TurnIntentType.FreeChat, result);
    }

    [Fact]
    public void ClassifyIntent_ShortGreeting_ReturnsFreeChat()
    {
        var session = CreateSession();

        var result = _phaseInference.ClassifyIntent("你好", session);

        Assert.Equal(TurnIntentType.FreeChat, result);
    }

    [Fact]
    public void ClassifyIntent_ShortMessage_ReturnsFreeChat()
    {
        var session = CreateSession();

        var result = _phaseInference.ClassifyIntent("谢谢", session);

        Assert.Equal(TurnIntentType.FreeChat, result);
    }

    [Fact]
    public void ClassifyIntent_NewBookKeyword_ReturnsNewProjectSeed()
    {
        var session = CreateSession();

        var result = _phaseInference.ClassifyIntent("我要写一本新书", session);

        Assert.Equal(TurnIntentType.NewProjectSeed, result);
    }

    [Fact]
    public void ClassifyIntent_NewNovelKeyword_ReturnsNewProjectSeed()
    {
        var session = CreateSession();

        var result = _phaseInference.ClassifyIntent("我想写一本新小说", session);

        Assert.Equal(TurnIntentType.NewProjectSeed, result);
    }

    [Fact]
    public void ClassifyIntent_ContinueKeyword_ReturnsContinueMission()
    {
        var session = CreateSession();

        var result = _phaseInference.ClassifyIntent("继续写第三章", session);

        Assert.Equal(TurnIntentType.ContinueMission, result);
    }

    [Fact]
    public void ClassifyIntent_NextChapter_ReturnsContinueMission()
    {
        var session = CreateSession();

        var result = _phaseInference.ClassifyIntent("写下一章内容", session);

        Assert.Equal(TurnIntentType.ContinueMission, result);
    }

    [Fact]
    public void ClassifyIntent_NextTask_ReturnsContinueMission()
    {
        var session = CreateSession();

        var result = _phaseInference.ClassifyIntent("开始下一个章节", session);

        Assert.Equal(TurnIntentType.ContinueMission, result);
    }

    [Fact]
    public void ClassifyIntent_RevisionKeyword_ReturnsRevisionRequest()
    {
        var session = CreateSession();

        var result = _phaseInference.ClassifyIntent("修改第一章的内容", session);

        Assert.Equal(TurnIntentType.RevisionRequest, result);
    }

    [Fact]
    public void ClassifyIntent_FixKeyword_ReturnsRevisionRequest()
    {
        var session = CreateSession();

        var result = _phaseInference.ClassifyIntent("修复这个章节的问题", session);

        Assert.Equal(TurnIntentType.RevisionRequest, result);
    }

    [Fact]
    public void ClassifyIntent_ChangeKeyword_ReturnsRevisionRequest()
    {
        var session = CreateSession();

        var result = _phaseInference.ClassifyIntent("改一下第二章风格", session);

        Assert.Equal(TurnIntentType.RevisionRequest, result);
    }

    [Fact]
    public void ClassifyIntent_CreativeBrief_ReturnsCreativeBrief()
    {
        var session = CreateSession();

        var result = _phaseInference.ClassifyIntent("写一个关于时间旅行的故事", session);

        Assert.Equal(TurnIntentType.CreativeBrief, result);
    }

    [Fact]
    public void ClassifyIntent_LongCreativeMessage_ReturnsCreativeBrief()
    {
        var session = CreateSession();

        var result = _phaseInference.ClassifyIntent("我想写一个科幻小说，主角是一个程序员", session);

        Assert.Equal(TurnIntentType.CreativeBrief, result);
    }

    [Fact]
    public void ClassifyIntent_EmptyMessage_ReturnsFreeChat()
    {
        var session = CreateSession();

        var result = _phaseInference.ClassifyIntent("", session);

        // Empty messages are short (<10 chars) and return FreeChat
        Assert.Equal(TurnIntentType.FreeChat, result);
    }

    [Fact]
    public void ClassifyIntent_NullMessage_ReturnsFreeChat()
    {
        var session = CreateSession();

        var result = _phaseInference.ClassifyIntent(null, session);

        // Null messages are treated as empty and return FreeChat
        Assert.Equal(TurnIntentType.FreeChat, result);
    }

    #endregion

    #region InferPhase Tests

    [Fact]
    public void InferPhase_StatusQuery_ReturnsConversation()
    {
        var session = CreateSession();

        var result = _phaseInference.InferPhase("状态怎么样", session);

        Assert.Equal(ConversationPhase.Conversation, result);
    }

    [Fact]
    public void InferPhase_Confirmation_ReturnsConversation()
    {
        var session = CreateSession(withPendingToolCall: true);

        var result = _phaseInference.InferPhase("好的", session);

        Assert.Equal(ConversationPhase.Conversation, result);
    }

    [Fact]
    public void InferPhase_FreeChat_ReturnsConversation()
    {
        var session = CreateSession();

        var result = _phaseInference.InferPhase("你好", session);

        Assert.Equal(ConversationPhase.Conversation, result);
    }

    [Fact]
    public void InferPhase_CreativeBriefWithNoTask_ReturnsPlanning()
    {
        var session = CreateSession();

        var result = _phaseInference.InferPhase("写一个科幻小说", session);

        Assert.Equal(ConversationPhase.Planning, result);
    }

    [Fact]
    public void InferPhase_CreativeBriefWithUnstartedTask_ReturnsPlanning()
    {
        var session = CreateSessionWithTask(status: "unstarted");

        var result = _phaseInference.InferPhase("写第一章", session);

        Assert.Equal(ConversationPhase.Planning, result);
    }

    [Fact]
    public void InferPhase_CreativeBriefWithEmptyStatus_ReturnsPlanning()
    {
        var session = CreateSessionWithTask(status: "");

        var result = _phaseInference.InferPhase("写第一章", session);

        Assert.Equal(ConversationPhase.Planning, result);
    }

    [Fact]
    public void InferPhase_ContinueMissionWithContextReady_ReturnsCreation()
    {
        var session = CreateSessionWithTask(status: "context_ready");

        var result = _phaseInference.InferPhase("继续写", session);

        Assert.Equal(ConversationPhase.Creation, result);
    }

    [Fact]
    public void InferPhase_CreativeBriefWithContextReady_ReturnsCreation()
    {
        var session = CreateSessionWithTask(status: "context_ready");

        var result = _phaseInference.InferPhase("写一章关于冒险的故事", session);

        Assert.Equal(ConversationPhase.Creation, result);
    }

    [Fact]
    public void InferPhase_ContinueMissionWithDraftGenerated_ReturnsReview()
    {
        var session = CreateSessionWithTask(status: "draft_generated");

        var result = _phaseInference.InferPhase("继续写下一章", session);

        Assert.Equal(ConversationPhase.Review, result);
    }

    [Fact]
    public void InferPhase_ContinueMissionWithValidated_ReturnsReview()
    {
        var session = CreateSessionWithTask(status: "validated");

        var result = _phaseInference.InferPhase("继续写下一章", session);

        Assert.Equal(ConversationPhase.Review, result);
    }

    [Fact]
    public void InferPhase_RevisionRequest_ReturnsCreation()
    {
        var session = CreateSession();

        var result = _phaseInference.InferPhase("修改第一章的内容", session);

        Assert.Equal(ConversationPhase.Creation, result);
    }

    [Fact]
    public void InferPhase_RevisionRequestWithExistingTask_ReturnsCreation()
    {
        var session = CreateSessionWithTask(status: "draft_generated");

        var result = _phaseInference.InferPhase("修改这一章的风格", session);

        Assert.Equal(ConversationPhase.Creation, result);
    }

    [Fact]
    public void InferPhase_NewProjectSeed_ReturnsConversation()
    {
        var session = CreateSession();

        var result = _phaseInference.InferPhase("创建新书", session);

        // NewProjectSeed doesn't have explicit handling in InferPhase,
        // so it falls through to default
        Assert.Equal(ConversationPhase.Conversation, result);
    }

    [Fact]
    public void InferPhase_UnknownTaskStatus_ReturnsPlanning()
    {
        var session = CreateSessionWithTask(status: "some_unknown_status");

        var result = _phaseInference.InferPhase("继续写", session);

        // Unknown status falls through to Planning for CreativeBrief/ContinueMission
        Assert.Equal(ConversationPhase.Planning, result);
    }

    [Fact]
    public void InferPhase_CompletedTask_SkipsToNextTask()
    {
        var session = CreateSessionWithMultipleTasks();

        var result = _phaseInference.InferPhase("继续写", session);

        // Should pick up the second task with context_ready status
        Assert.Equal(ConversationPhase.Creation, result);
    }

    [Fact]
    public void InferPhase_AllTasksCompleted_ReturnsPlanning()
    {
        var session = CreateSessionWithAllCompletedTasks();

        var result = _phaseInference.InferPhase("继续写", session);

        // No active task, returns Planning
        Assert.Equal(ConversationPhase.Planning, result);
    }

    [Fact]
    public void InferPhase_CancelledTaskSkipped_FindsNextTask()
    {
        var session = CreateSessionWithCancelledTask();

        var result = _phaseInference.InferPhase("继续写下一章", session);

        // Should skip cancelled task and use the unstarted one
        Assert.Equal(ConversationPhase.Planning, result);
    }

    [Fact]
    public void InferPhase_EmptyMissionPlan_ReturnsPlanning()
    {
        var session = CreateSession();
        session.WorkingMemory.MissionPlan = new AgentMissionPlan
        {
            SchedulerState = new AgentTaskSchedulerState
            {
                Tasks = new List<AgentScheduledTask>()
            }
        };

        var result = _phaseInference.InferPhase("写第一章", session);

        Assert.Equal(ConversationPhase.Planning, result);
    }

    [Fact]
    public void InferPhase_NullSchedulerState_ReturnsPlanning()
    {
        var session = CreateSession();
        session.WorkingMemory.MissionPlan = new AgentMissionPlan
        {
            SchedulerState = null
        };

        var result = _phaseInference.InferPhase("继续写", session);

        Assert.Equal(ConversationPhase.Planning, result);
    }

    #endregion

    #region Helper Methods

    private AgentSession CreateSession(bool withPendingToolCall = false)
    {
        var session = new AgentSession
        {
            SessionId = "test-session",
            WorkingMemory = new AgentWorkingMemory
            {
                MissionPlan = new AgentMissionPlan
                {
                    SchedulerState = new AgentTaskSchedulerState
                    {
                        Tasks = new List<AgentScheduledTask>()
                    }
                }
            }
        };

        if (withPendingToolCall)
        {
            session.WorkingMemory.PendingToolCall = new AgentToolCall
            {
                Name = "test_tool"
            };
        }

        return session;
    }

    private AgentSession CreateSessionWithTask(string status)
    {
        var session = CreateSession();
        session.WorkingMemory.MissionPlan.SchedulerState.Tasks.Add(new AgentScheduledTask
        {
            TaskId = "task1",
            Status = status,
            TaskType = "write_chapter"
        });
        return session;
    }

    private AgentSession CreateSessionWithMultipleTasks()
    {
        var session = CreateSession();
        session.WorkingMemory.MissionPlan.SchedulerState.Tasks.AddRange(new[]
        {
            new AgentScheduledTask
            {
                TaskId = "task1",
                Status = "completed",
                TaskType = "write_chapter"
            },
            new AgentScheduledTask
            {
                TaskId = "task2",
                Status = "context_ready",
                TaskType = "write_chapter"
            },
            new AgentScheduledTask
            {
                TaskId = "task3",
                Status = "unstarted",
                TaskType = "write_chapter"
            }
        });
        return session;
    }

    private AgentSession CreateSessionWithAllCompletedTasks()
    {
        var session = CreateSession();
        session.WorkingMemory.MissionPlan.SchedulerState.Tasks.AddRange(new[]
        {
            new AgentScheduledTask
            {
                TaskId = "task1",
                Status = "completed",
                TaskType = "write_chapter"
            },
            new AgentScheduledTask
            {
                TaskId = "task2",
                Status = "completed",
                TaskType = "write_chapter"
            }
        });
        return session;
    }

    private AgentSession CreateSessionWithCancelledTask()
    {
        var session = CreateSession();
        session.WorkingMemory.MissionPlan.SchedulerState.Tasks.AddRange(new[]
        {
            new AgentScheduledTask
            {
                TaskId = "task1",
                Status = "cancelled",
                TaskType = "write_chapter"
            },
            new AgentScheduledTask
            {
                TaskId = "task2",
                Status = "unstarted",
                TaskType = "write_chapter"
            }
        });
        return session;
    }

    #endregion
}
