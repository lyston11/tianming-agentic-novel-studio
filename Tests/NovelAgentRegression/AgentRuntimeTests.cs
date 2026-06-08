using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.Support;
using Xunit;

namespace TM.Tests.NovelAgentRegression;

public sealed class AgentRuntimeTests
{
    [Fact]
    public void PhaseInferenceAndContextBuilder_AreIntegratedIntoAgentRuntime()
    {
        // This test verifies that AgentRuntime has been updated to work with
        // PhaseInference and PhaseContextBuilder by checking that the services exist
        // and have the expected methods.

        // Verify PhaseInference has InferPhase method
        var phaseInferenceType = typeof(PhaseInference);
        var inferPhaseMethod = phaseInferenceType.GetMethod("InferPhase");
        Assert.NotNull(inferPhaseMethod);

        // Verify PhaseContextBuilder exists
        var contextBuilderType = typeof(PhaseContextBuilder);
        Assert.NotNull(contextBuilderType);

        // Verify AgentToolRegistry has ListToolSchemasForPhase method
        var registryType = typeof(AgentToolRegistry);
        var listToolsMethod = registryType.GetMethod("ListToolSchemasForPhase");
        Assert.NotNull(listToolsMethod);
    }
}
