using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Services.Framework.AI.NovelAgent.Services.ProductionKernel;
using Xunit;

namespace Tests.Unit.Services.Production;

public sealed class ChapterFactWriterTests
{
    [Fact]
    public async Task ExtractAndPersistAsync_UsesModelJsonAndPersistsContinuityFacts()
    {
        IChapterFactWriter writer = new ChapterFactWriter();
        ChapterContinuityFacts? persisted = null;
        var request = new ChapterFactWriteRequest
        {
            Run = new NovelAgentRun
            {
                RunId = "run-1",
                TargetChapterId = "chapter-002"
            },
            ContextPackage = new ChapterContextPackageSummary
            {
                ChapterId = "chapter-002",
                HardContinuityFacts = { "主角姓名：沈砚" }
            },
            CommittedContent = "沈砚在旧邮路尽头找到银蓝邮徽。",
            CompleteAsync = (_, user, _) =>
            {
                Assert.Contains("extract_chapter_continuity_facts", user);
                return Task.FromResult("""
                {
                  "chapterId": "chapter-002",
                  "chapterTitle": "旧邮路尽头",
                  "protagonistName": "沈砚",
                  "protagonistIdentity": "旧邮差继承者",
                  "protagonistStatus": "负伤但清醒",
                  "currentLocation": "旧邮路尽头",
                  "systemState": "邮差系统只提示投递方向",
                  "equipmentState": "银蓝邮徽只能辨认邮路",
                  "keyEvents": ["沈砚找到银蓝邮徽"],
                  "endingState": "银蓝邮徽指向潮汐塔外层",
                  "nextChapterMustCarry": ["承接潮汐塔外层方向"]
                }
                """);
            },
            PersistAsync = (facts, _) =>
            {
                persisted = facts;
                return Task.FromResult(new StoryBibleCommitResult
                {
                    Success = true,
                    Message = "ok"
                });
            }
        };

        var result = await writer.ExtractAndPersistAsync(request);

        Assert.True(result.Success);
        Assert.NotNull(persisted);
        Assert.Equal("chapter-002", persisted!.ChapterId);
        Assert.Equal("run-1", persisted.SourceRunId);
        Assert.Equal("沈砚", persisted.ProtagonistName);
        Assert.Contains("承接潮汐塔外层方向", persisted.NextChapterMustCarry);
    }

    [Fact]
    public async Task ExtractAndPersistAsync_DoesNotRuleExtractWhenModelOutputIsInvalid()
    {
        IChapterFactWriter writer = new ChapterFactWriter();
        var persistCalled = false;
        var request = new ChapterFactWriteRequest
        {
            Run = new NovelAgentRun { RunId = "run-1", TargetChapterId = "chapter-003" },
            ContextPackage = new ChapterContextPackageSummary { ChapterId = "chapter-003" },
            CommittedContent = "正文存在，但模型输出不可解析时不能规则抽取伪造事实。",
            CompleteAsync = (_, _, _) => Task.FromResult("不是 JSON"),
            PersistAsync = (_, _) =>
            {
                persistCalled = true;
                return Task.FromResult(new StoryBibleCommitResult { Success = true });
            }
        };

        var result = await writer.ExtractAndPersistAsync(request);

        Assert.False(result.Success);
        Assert.False(persistCalled);
        Assert.Contains("不可解析", result.Message);
    }

    [Fact]
    public async Task ExtractAndPersistAsync_ExtractsFirstCompleteJsonObjectFromFencedModelOutput()
    {
        IChapterFactWriter writer = new ChapterFactWriter();
        ChapterContinuityFacts? persisted = null;
        var request = new ChapterFactWriteRequest
        {
            Run = new NovelAgentRun { RunId = "run-1", TargetChapterId = "chapter-004" },
            ContextPackage = new ChapterContextPackageSummary { ChapterId = "chapter-004" },
            CommittedContent = "沈砚从黑雨里逃出，银蓝邮徽只指向旧邮路。",
            CompleteAsync = (_, _, _) => Task.FromResult("""
            ```json
            {
              "chapterId": "chapter-004",
              "chapterTitle": "黑雨旧邮路",
              "protagonistName": "沈砚",
              "endingState": "银蓝邮徽指向旧邮路",
              "keyEvents": ["沈砚从黑雨中逃出"],
              "nextChapterMustCarry": ["承接银蓝邮徽只能指路"]
            }
            ```

            附注：{"ignored": true}
            """),
            PersistAsync = (facts, _) =>
            {
                persisted = facts;
                return Task.FromResult(new StoryBibleCommitResult
                {
                    Success = true,
                    Message = "ok"
                });
            }
        };

        var result = await writer.ExtractAndPersistAsync(request);

        Assert.True(result.Success, result.Message);
        Assert.NotNull(persisted);
        Assert.Equal("chapter-004", persisted!.ChapterId);
        Assert.Equal("沈砚", persisted.ProtagonistName);
        Assert.Contains("承接银蓝邮徽只能指路", persisted.NextChapterMustCarry);
    }
}
