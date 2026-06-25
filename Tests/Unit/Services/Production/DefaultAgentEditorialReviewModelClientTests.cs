using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.Services.Production;
using Xunit;

namespace Tests.Unit.Services.Production;

public sealed class DefaultAgentEditorialReviewModelClientTests
{
    [Fact]
    public async Task ReviewAsync_ExtractsFirstCompleteJsonObjectFromFencedModelOutput()
    {
        var completion = new StaticWritingModelCompletionService("""
        ```json
        {
          "meetsUserIntent": false,
          "meetsRevisionPlan": true,
          "meetsProjectPromise": true,
          "meetsAcceptedCreativeIntents": false,
          "continuityRisk": "medium",
          "creativeFit": "low",
          "chapterPacing": "needs_revision",
          "decision": "rewrite",
          "recommendedAction": "rewrite",
          "problems": ["没有落实用户要求的打怪升级"],
          "suggestions": ["重写为怪物围攻和邮徽逃生"],
          "evidence": ["正文仍以躲藏等待为主"]
        }
        ```

        备注：{"ignored": true}
        """);
        var client = new DefaultAgentEditorialReviewModelClient(
            completion,
            NullLogger<DefaultAgentEditorialReviewModelClient>.Instance);

        var decision = await client.ReviewAsync(new AgentEditorialReviewRequest
        {
            UserId = "user-1",
            RunId = "run-1",
            ChapterId = "chapter-002",
            UserGoal = "第二章改成打怪升级",
            StoryConstitution = new StoryCreativeConstitution
            {
                ReaderPromise = "打怪升级和基地成长",
                MainPleasure = "战斗后有明确成长反馈"
            },
            ChapterContent = "沈砚躲在维修站里等待天亮。"
        });

        Assert.False(decision.MeetsUserIntent);
        Assert.True(decision.MeetsRevisionPlan);
        var serializedDecision = JsonSerializer.Serialize(decision);
        Assert.Contains("\"meetsAcceptedCreativeIntents\":false", serializedDecision);
        Assert.Contains("\"continuityRisk\":\"medium\"", serializedDecision);
        Assert.Contains("\"chapterPacing\":\"needs_revision\"", serializedDecision);
        Assert.Contains("\"recommendedAction\":\"rewrite\"", serializedDecision);
        Assert.Equal("rewrite", decision.Decision);
        Assert.Contains(decision.Problems, item => item.Contains("打怪升级", StringComparison.Ordinal));
        Assert.Equal("user-1", completion.UserId);
        Assert.Contains("总编验收模型", completion.SystemPrompt);
        Assert.Contains("storyConstitution", completion.UserPrompt);
        Assert.Contains("打怪升级和基地成长", completion.UserPrompt);
        Assert.Contains("chapterContent", completion.UserPrompt);
    }

    private sealed class StaticWritingModelCompletionService : IWritingModelCompletionService
    {
        private readonly string _response;

        public StaticWritingModelCompletionService(string response)
        {
            _response = response;
        }

        public string UserId { get; private set; } = string.Empty;
        public string SystemPrompt { get; private set; } = string.Empty;
        public string UserPrompt { get; private set; } = string.Empty;

        public Task<string> CompleteAsync(
            string userId,
            string system,
            string user,
            CancellationToken ct = default)
        {
            UserId = userId;
            SystemPrompt = system;
            UserPrompt = user;
            return Task.FromResult(_response);
        }
    }
}
