using System.Reflection;
using TM.Web.NovelAgentWeb.Support;
using Xunit;

namespace Tests.Unit.Support;

public sealed class AgentModelJsonEnvelopeParsingTests
{
    [Fact]
    public void ToolCallingClient_ParsesFirstCompleteActionJsonObjectWhenModelAddsNotes()
    {
        var action = InvokeToolCallingEnvelopeParser("""
        ```json
        {
          "action_type": "tool_call",
          "intent": "produce_chapter",
          "tool_call": {
            "name": "ProduceChapter",
            "arguments": {
              "runId": "run-1",
              "commitPolicy": "auto_commit"
            }
          }
        }
        ```

        备注：{"ignored": true}
        """);

        Assert.NotNull(action);
        Assert.Equal(AgentActionType.ToolCall, action!.Type);
        Assert.Equal("ProduceChapter", action.ToolCall?.Name);
        Assert.Equal("run-1", action.ToolCall?.Arguments["runId"]);
    }

    [Fact]
    public void AgentCore_ParsesFirstCompleteActionJsonObjectWhenModelAddsNotes()
    {
        var action = InvokeAgentPlannerActionParser("""
        {
          "action_type": "tool_call",
          "intent": "query_content",
          "tool_call": {
            "name": "QueryProjectContent",
            "arguments": {
              "chapterNumber": "2"
            }
          }
        }

        补充：{"ignored": true}
        """);

        Assert.Equal(AgentActionType.ToolCall, action.Type);
        Assert.Equal("QueryProjectContent", action.ToolCall?.Name);
        Assert.Equal("2", action.ToolCall?.Arguments["chapterNumber"]);
    }

    private static AgentAction? InvokeToolCallingEnvelopeParser(string text)
    {
        var method = typeof(ProviderToolCallingClient).GetMethod(
            "TryParseActionTextEnvelope",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return (AgentAction?)method!.Invoke(null, new object[] { text, "unit_test" });
    }

    private static AgentAction InvokeAgentPlannerActionParser(string text)
    {
        var method = typeof(AgentPlanner).GetMethod(
            "ParseAction",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return (AgentAction)method!.Invoke(null, new object[] { text })!;
    }
}
