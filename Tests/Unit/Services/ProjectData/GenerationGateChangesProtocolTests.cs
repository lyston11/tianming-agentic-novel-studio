using TM.Services.Modules.ProjectData.Implementations;
using Xunit;

namespace Tests.Unit.Services.ProjectData;

public class GenerationGateChangesProtocolTests
{
    [Fact]
    public void ValidateChangesProtocol_UsesLastBalancedChangesJsonInsideXmlBlock()
    {
        var gate = new GenerationGate(null!, null!, null!);

        var content = """
        第一章正文里出现了 {黑匣子}，这不是修订记录。

        <chapter_changes>
        下面是本章修订记录：
        ```json
        {"note":"模型解释用对象，不应该被当成CHANGES"}
        ```

        {
          "CharacterStateChanges": [],
          "ConflictProgress": [],
          "NewPlotPoints": [
            {
              "Keywords": ["黑匣子", "第一次启动"],
              "Context": "主角发现沉船黑匣子能唤醒旧式机甲。",
              "InvolvedCharacters": [],
              "Importance": "high",
              "Storyline": "main",
              "CausedBy": "chapter-001"
            }
          ],
          "ForeshadowingActions": [],
          "LocationStateChanges": [],
          "FactionStateChanges": [],
          "TimeProgression": {
            "TimePeriod": "第一天",
            "ElapsedTime": "半小时",
            "KeyTimeEvent": "沉船黑匣子被启动",
            "Importance": "normal"
          },
          "CharacterMovements": [],
          "ItemTransfers": [],
          "SecretRevealChanges": [],
          "PledgeConstraintChanges": [],
          "DeadlineConstraintChanges": []
        }
        </chapter_changes>
        """;

        var result = gate.ValidateChangesProtocol(content);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.NotNull(result.Changes);
        Assert.Single(result.Changes!.NewPlotPoints);
        Assert.Contains("第一章正文里出现了", result.ContentWithoutChanges);
    }

    [Fact]
    public void ValidateChangesProtocol_NormalizesArrayOfFieldObjectsInsideXmlBlock()
    {
        var gate = new GenerationGate(null!, null!, null!);

        var content = """
        第二章正文已经完整生成。

        <chapter_changes>
        [
          { "CharacterStateChanges": [] },
          { "ConflictProgress": [] },
          {
            "NewPlotPoints": [
              {
                "Keywords": ["蓝磷骨光", "盐井"],
                "Context": "主角沿着第一章结尾的蓝磷骨光追到盐井。",
                "InvolvedCharacters": [],
                "Importance": "high",
                "Storyline": "main",
                "CausedBy": "chapter-002"
              }
            ]
          },
          { "ForeshadowingActions": [] },
          { "LocationStateChanges": [] },
          { "FactionStateChanges": [] },
          { "TimeProgression": [] },
          { "CharacterMovements": [] },
          { "ItemTransfers": [] },
          { "SecretRevealChanges": [] },
          { "PledgeConstraintChanges": [] },
          { "DeadlineConstraintChanges": [] }
        ]
        </chapter_changes>
        """;

        var result = gate.ValidateChangesProtocol(content);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.NotNull(result.Changes);
        Assert.Single(result.Changes!.NewPlotPoints);
        Assert.Contains("蓝磷骨光", result.Changes.NewPlotPoints[0].Keywords);
    }
}
