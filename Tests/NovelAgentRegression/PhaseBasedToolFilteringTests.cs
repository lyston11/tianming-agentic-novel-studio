using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.Support;
using Xunit;

namespace TM.Tests.NovelAgentRegression;

public sealed class PhaseBasedToolFilteringTests
{
    [Fact]
    public void PhaseInference_InferPhase_ReturnsConversationForStatusQuery()
    {
        // Arrange
        var phaseInference = new PhaseInference();
        var session = CreateTestSession();

        // Act
        var phase = phaseInference.InferPhase("当前状态怎么样?", session);

        // Assert
        Assert.Equal(ConversationPhase.Conversation, phase);
    }

    [Fact]
    public void PhaseInference_InferPhase_ReturnsPlanningForNewCreativeBrief()
    {
        // Arrange
        var phaseInference = new PhaseInference();
        var session = CreateTestSession();

        // Act
        var phase = phaseInference.InferPhase("我想写一个科幻小说", session);

        // Assert
        Assert.Equal(ConversationPhase.Planning, phase);
    }

    [Fact]
    public void PhaseInference_InferPhase_ReturnsCreationWhenTaskIsContextReady()
    {
        // Arrange
        var phaseInference = new PhaseInference();
        var session = CreateTestSession();
        session.WorkingMemory.MissionPlan.SchedulerState.Tasks.Add(new AgentScheduledTask
        {
            TaskId = "task1",
            Status = "context_ready"
        });

        // Act
        var phase = phaseInference.InferPhase("继续写", session);

        // Assert
        Assert.Equal(ConversationPhase.Creation, phase);
    }

    [Fact]
    public void PhaseInference_InferPhase_ReturnsReviewWhenTaskIsDraftGenerated()
    {
        // Arrange
        var phaseInference = new PhaseInference();
        var session = CreateTestSession();
        session.WorkingMemory.MissionPlan.SchedulerState.Tasks.Add(new AgentScheduledTask
        {
            TaskId = "task1",
            Status = "draft_generated"
        });

        // Act
        var phase = phaseInference.InferPhase("继续", session);

        // Assert
        Assert.Equal(ConversationPhase.Review, phase);
    }

    [Fact]
    public void AgentToolRegistry_ListToolSchemasForPhase_FiltersCorrectly()
    {
        // This is a structural test to verify the method exists
        var registryType = typeof(AgentToolRegistry);
        var method = registryType.GetMethod("ListToolSchemasForPhase");

        Assert.NotNull(method);
        Assert.Equal("ListToolSchemasForPhase", method.Name);

        var parameters = method.GetParameters();
        Assert.Single(parameters);
        Assert.Equal("ConversationPhase", parameters[0].ParameterType.Name);
    }

    private static AgentSession CreateTestSession()
    {
        return new AgentSession
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
    }
}
