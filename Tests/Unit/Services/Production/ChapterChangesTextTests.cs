using TM.Services.Framework.AI.NovelAgent.Services.ProductionKernel;
using Xunit;

namespace Tests.Unit.Services.Production;

public sealed class ChapterChangesTextTests
{
    [Fact]
    public void ExtractChangesJson_ReadsClosedChapterChangesBlock()
    {
        var content = """
        第一章正文。
        <chapter_changes>
        {"CharacterStateChanges":["沈砚确认银蓝邮徽只能辨认邮路"],"ConflictProgress":[]}
        </chapter_changes>
        """;

        var changesJson = ChapterChangesText.ExtractChangesJson(content);

        Assert.Contains("银蓝邮徽只能辨认邮路", changesJson);
    }

    [Fact]
    public void ExtractChangesJson_ReadsUnclosedChapterChangesWhenJsonIsValid()
    {
        var content = "第一章正文。\n<chapter_changes>{\"CharacterStateChanges\":[\"沈砚抵达主管道\"],\"ConflictProgress\":[]}";

        var changesJson = ChapterChangesText.ExtractChangesJson(content);

        Assert.Contains("沈砚抵达主管道", changesJson);
    }

    [Theory]
    [InlineData("第一章正文。\n\n---CHANGES---\n{\"CharacterStateChanges\":[]}")]
    [InlineData("第一章正文。\n<changes>{\"CharacterStateChanges\":[]}</changes>")]
    public void ExtractChangesJson_RejectsLegacyAliasChangesProtocol(string content)
    {
        var changesJson = ChapterChangesText.ExtractChangesJson(content);

        Assert.Equal(string.Empty, changesJson);
    }
}
