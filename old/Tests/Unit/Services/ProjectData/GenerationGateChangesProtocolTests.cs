using TM.Services.Modules.ProjectData.Implementations;
using Xunit;

namespace Tests.Unit.Services.ProjectData;

public class GenerationGateChangesProtocolTests
{
    [Theory]
    [InlineData("正文\n<chapter_changes>{\"CharacterStateChanges\":[],\"ConflictProgress\":[]}")]
    [InlineData("正文\n\n---CHANGES---\n{\"CharacterStateChanges\":[],\"ConflictProgress\":[]}")]
    [InlineData("正文\n{\"CharacterStateChanges\":[],\"ConflictProgress\":[]}")]
    public void ValidateChangesProtocol_RejectsLegacyOrUnclosedChangesRegions(string content)
    {
        var gate = new GenerationGate(null!, null!, null!);

        var result = gate.ValidateChangesProtocol(content);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Contains("<chapter_changes>...</chapter_changes>", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Errors, error => error.Contains("兼容旧格式", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Errors, error => error.Contains("末尾 JSON", StringComparison.Ordinal));
    }

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

    [Theory]
    [InlineData("changes")]
    [InlineData("chapter_changes")]
    [InlineData("CHANGES")]
    [InlineData("chapterChanges")]
    public void ValidateChangesProtocol_NormalizesSingleWrappedChangesObjectInsideXmlBlock(string wrapperName)
    {
        var gate = new GenerationGate(null!, null!, null!);

        var content = $$"""
        第三章正文已经完整生成。

        <chapter_changes>
        {
            "{{wrapperName}}": {
              "CharacterStateChanges": [],
              "ConflictProgress": [],
              "NewPlotPoints": [
                {
                  "Keywords": ["银蓝邮徽", "能力边界"],
                  "Context": "主角确认银蓝邮徽只能辨认旧邮路，不能攻击或激活新能力。",
                  "InvolvedCharacters": [],
                  "Importance": "high",
                  "Storyline": "main",
                  "CausedBy": "chapter-003"
                }
              ],
            "ForeshadowingActions": [],
            "LocationStateChanges": [],
            "FactionStateChanges": [],
            "TimeProgression": [],
            "CharacterMovements": [],
            "ItemTransfers": [],
            "SecretRevealChanges": [],
            "PledgeConstraintChanges": [],
            "DeadlineConstraintChanges": []
          }
        }
        </chapter_changes>
        """;

        var result = gate.ValidateChangesProtocol(content);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.NotNull(result.Changes);
        Assert.Single(result.Changes!.NewPlotPoints);
        Assert.Contains("只能辨认旧邮路", result.Changes.NewPlotPoints[0].Context);
    }

    [Fact]
    public void ValidateChangesProtocol_NormalizesNaturalFieldChangesFromRealChapterDraft()
    {
        var gate = new GenerationGate(null!, null!, null!);

        var content = """
        沈砚举起撬棍，看见邮路幽影从楼梯口走下来。</think>

        <chapter_changes>
        {
          "CharacterStateChanges": [
            {
              "character": "沈砚",
              "changes": [
                "银蓝邮徽从身份标识状态被激活，进入持续工作状态。",
                "明确感知到邮徽能力边界：只能识别与信息溯源。"
              ]
            }
          ],
          "ConflictProgress": [
            {
              "conflict": "邮路幽影的直接威胁",
              "progress": "在沉没灯塔节点内遭遇第一只邮路幽影，冲突从探索转为即时生存威胁。"
            }
          ],
          "NewPlotPoints": [
            "邮徽激活并显现系统化任务提示。",
            "发现旧港区档案室实为遗落计划下的军用物资仓库节点。"
          ],
          "ForeshadowingActions": [
            "邮路幽影具备与邮徽相同的银蓝色眼睛，暗示同源污染。"
          ],
          "LocationStateChanges": [
            {
              "location": "沉没灯塔",
              "changes": "这里是遗落计划编号2的水下邮路节点。"
            }
          ],
          "FactionStateChanges": [],
          "TimeProgression": {
            "description": "故事从末日爆发当日白天持续到夜晚。"
          },
          "CharacterMovements": [
            {
              "character": "沈砚",
              "movements": [
                "从第七码头进入旧港区档案室。",
                "登上接驳艇前往沉没灯塔。"
              ]
            }
          ],
          "ItemTransfers": [
            {
              "character": "沈砚",
              "gains": [
                "高强度合金撬棍",
                "星渊邮路接驳艇"
              ],
              "losses": []
            }
          ],
          "SecretRevealChanges": [
            {
              "secret": "遗落计划",
              "revealLevel": "初步揭示",
              "details": "旧时代星渊邮路管理局把关键物资和信息转移至多个隐藏节点。"
            }
          ],
          "PledgeConstraintChanges": [{}],
          "DeadlineConstraintChanges": [{}]
        }
        </chapter_changes>
        """;

        var result = gate.ValidateChangesProtocol(content);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.NotNull(result.Changes);
        Assert.Contains(result.Changes!.NewPlotPoints, point => point.Context.Contains("邮徽激活"));
        Assert.Contains(result.Changes.ForeshadowingActions, action => action.Action.Contains("邮路幽影"));
        Assert.Contains(result.Changes.ItemTransfers, transfer => transfer.ItemName.Contains("撬棍"));
        Assert.Contains("第七码头", result.Changes.CharacterMovements[0].ToLocationName);
    }
}
