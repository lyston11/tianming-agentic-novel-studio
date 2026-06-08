// Tests/NovelAgentRegression/RecoveryEngineTests.cs
using TM.Web.NovelAgentWeb.Support;
using Xunit;

namespace TM.Tests.NovelAgentRegression;

public sealed class RecoveryEngineTests
{
    [Fact]
    public void ToolFailureType_HasExpectedValues()
    {
        var missingPrereq = ToolFailureType.MissingPrerequisite;
        var invalidParams = ToolFailureType.InvalidParameters;
        var logicViolation = ToolFailureType.LogicConstraintViolation;
        var resourceUnavail = ToolFailureType.ResourceUnavailable;

        Assert.Equal(0, (int)missingPrereq);
        Assert.Equal(1, (int)invalidParams);
        Assert.Equal(2, (int)logicViolation);
        Assert.Equal(3, (int)resourceUnavail);
    }
}
