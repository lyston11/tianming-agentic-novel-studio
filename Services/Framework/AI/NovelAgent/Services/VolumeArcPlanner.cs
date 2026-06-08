using System;
using System.Collections.Generic;
using System.Linq;
using TM.Services.Framework.AI.NovelAgent.Models;

namespace TM.Services.Framework.AI.NovelAgent.Services
{
    public sealed class VolumeArcPlanner
    {
        public VolumeArcPlan BuildPlan(
            VolumeArcPlanningRequest request,
            StoryCreativeConstitution? constitution,
            CreativeKnowledgeRetrievalResult? knowledge)
        {
            var safeRequest = request ?? new VolumeArcPlanningRequest();
            var volumeId = string.IsNullOrWhiteSpace(safeRequest.VolumeId)
                ? "vol1"
                : safeRequest.VolumeId.Trim();
            var title = string.IsNullOrWhiteSpace(safeRequest.VolumeTitle)
                ? $"{volumeId}：第一轮规则验证"
                : safeRequest.VolumeTitle.Trim();
            var expectedCount = Math.Clamp(safeRequest.ExpectedChapterCount, 6, 40);
            var genre = constitution?.Genre ?? string.Empty;
            var promise = FirstNonEmpty(
                safeRequest.UserGoal,
                constitution?.ReaderPromise,
                "建立本卷核心困境，并让主角以可追踪代价完成一次状态跃迁。");
            var conflict = FirstNonEmpty(
                constitution?.MainConflictEngine,
                "主角目标、世界规则和对手利益持续挤压。");

            var plan = new VolumeArcPlan
            {
                VolumeId = volumeId,
                Title = title,
                StartChapterId = string.IsNullOrWhiteSpace(safeRequest.StartChapterId)
                    ? $"{volumeId}_ch1"
                    : safeRequest.StartChapterId.Trim(),
                EndChapterId = string.IsNullOrWhiteSpace(safeRequest.EndChapterId)
                    ? $"{volumeId}_ch{expectedCount}"
                    : safeRequest.EndChapterId.Trim(),
                ExpectedChapterCount = expectedCount,
                Status = VolumeArcStatus.Proposed,
                VolumePromise = promise,
                EntryState = $"主角带着未解决的核心缺口进入本卷：{FirstNonEmpty(constitution?.ProtagonistEngine, "能力、认知或关系存在短板。")}",
                ExitState = $"本卷结束时必须改变至少一个长期变量：身份、阵营、世界规则认知或关系结构。",
                CoreQuestion = $"在“{promise}”压力下，主角愿意付出什么代价继续前进？",
                MainConflictUpgrade = $"将主线冲突从局部目标升级为卷级压力：{conflict}",
                MidpointReversal = BuildMidpointReversal(constitution, knowledge),
                Climax = BuildClimax(constitution, genre),
                AftermathHook = "卷末胜利必须留下新债务、新规则或新敌我边界，作为下一卷开口。",
                ChapterBeats = BuildBeats(expectedCount, promise, conflict),
                ForeshadowingPlan = BuildForeshadowing(expectedCount),
                CharacterArcPlan = BuildCharacterArcs(constitution),
                WorldbuildingIncrements = BuildWorldbuildingIncrements(constitution, knowledge),
                MustAvoid = BuildMustAvoid(constitution, knowledge)
            };

            return plan;
        }

        private static List<VolumeChapterBeat> BuildBeats(
            int expectedCount,
            string promise,
            string conflict)
        {
            var roles = new[]
            {
                "开卷钩子",
                "规则展示",
                "第一次错误胜利",
                "代价显形",
                "关系或阵营挤压",
                "中段反转",
                "失败后重组",
                "伏笔回收",
                "卷末压迫升级",
                "高潮选择",
                "余波与新钩子"
            };

            var beats = new List<VolumeChapterBeat>();
            for (var i = 1; i <= expectedCount; i++)
            {
                var ratio = expectedCount == 1 ? 1 : (double)(i - 1) / (expectedCount - 1);
                var role = roles[Math.Min(roles.Length - 1, (int)Math.Round(ratio * (roles.Length - 1)))];
                beats.Add(new VolumeChapterBeat
                {
                    Index = i,
                    Role = role,
                    Goal = BuildBeatGoal(role, promise),
                    Turn = BuildBeatTurn(role, conflict),
                    Cost = BuildBeatCost(role)
                });
            }

            return beats;
        }

        private static string BuildBeatGoal(string role, string promise)
        {
            return role switch
            {
                "开卷钩子" => $"用一个不可忽视的事件兑现本卷承诺：{promise}",
                "规则展示" => "展示本卷关键规则如何制造限制，而不是只做背景说明。",
                "第一次错误胜利" => "让主角得到短期进展，但埋下错误判断或未来债务。",
                "中段反转" => "推翻主角对目标、敌人或规则的一个关键判断。",
                "高潮选择" => "让主角用主动选择而非巧合解决卷级压力。",
                "余波与新钩子" => "确认本卷状态改变，并打开下一卷更大的问题。",
                _ => "推进一个可追踪的冲突变量，避免原地踏步。"
            };
        }

        private static string BuildBeatTurn(string role, string conflict)
        {
            return role switch
            {
                "代价显形" => "前几章的胜利开始反噬，主角必须承认旧方案不够用。",
                "关系或阵营挤压" => "外部冲突转化为立场或关系冲突。",
                "失败后重组" => "失败迫使主角调整策略、盟友或价值判断。",
                "伏笔回收" => "旧细节改变当前判断，而不是只解释背景。",
                "卷末压迫升级" => $"把卷内冲突推向不可回避：{conflict}",
                _ => "让信息、关系、资源或规则认知发生变化。"
            };
        }

        private static string BuildBeatCost(string role)
        {
            return role switch
            {
                "第一次错误胜利" => "留下被对手利用的误判。",
                "代价显形" => "损失资源、信任、时间窗口或身份安全。",
                "中段反转" => "旧计划作废，并暴露更深层威胁。",
                "高潮选择" => "以不可逆代价换取卷级状态变化。",
                _ => "至少产生一个后续章节可追踪的小后果。"
            };
        }

        private static List<VolumeForeshadowPlan> BuildForeshadowing(int expectedCount)
        {
            return new List<VolumeForeshadowPlan>
            {
                new()
                {
                    Name = "规则异常",
                    Setup = "前 1/4 卷投放一个看似小的规则异常。",
                    Payoff = "中段反转时证明异常不是漏洞，而是深层规则入口。",
                    PayoffBeatIndex = Math.Max(2, expectedCount / 2)
                },
                new()
                {
                    Name = "关系裂痕",
                    Setup = "前半卷让一个盟友或对手的选择留下解释空间。",
                    Payoff = "卷末把该选择转化为立场变化或代价回收。",
                    PayoffBeatIndex = Math.Max(3, expectedCount - 2)
                },
                new()
                {
                    Name = "代价账本",
                    Setup = "每次阶段胜利后记录一个小代价。",
                    Payoff = "高潮前让多个小代价汇合成必须处理的主压力。",
                    PayoffBeatIndex = Math.Max(4, expectedCount - 1)
                }
            };
        }

        private static List<VolumeCharacterArc> BuildCharacterArcs(StoryCreativeConstitution? constitution)
        {
            return new List<VolumeCharacterArc>
            {
                new()
                {
                    CharacterName = "主角",
                    StartingBelief = FirstNonEmpty(constitution?.ProtagonistEngine, "只要目标正确，就能靠能力推进。"),
                    Pressure = "世界规则、对手利益和关系代价同时压迫。",
                    Choice = "接受不完美胜利，并主动承担后续债务。",
                    ChangedState = "从被动应对规则，转为主动利用规则并承认代价。"
                },
                new()
                {
                    CharacterName = "核心对手/镜像角色",
                    StartingBelief = "认为主角只能在既定规则内挣扎。",
                    Pressure = "主角的选择破坏其原有判断。",
                    Choice = "升级手段或暴露更高层立场。",
                    ChangedState = "从局部阻碍升级为卷级冲突代表。"
                }
            };
        }

        private static List<string> BuildWorldbuildingIncrements(
            StoryCreativeConstitution? constitution,
            CreativeKnowledgeRetrievalResult? knowledge)
        {
            var increments = new List<string>
            {
                FirstNonEmpty(constitution?.WorldCoreRule, "明确一个能制造代价的世界核心规则。"),
                "补充一个限制主角行动的制度、组织或资源流通规则。",
                "补充一个卷末可反噬的隐藏代价。"
            };
            increments.AddRange(knowledge?.GenrePrinciples.Take(2) ?? Enumerable.Empty<string>());
            return increments
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(6)
                .ToList();
        }

        private static List<string> BuildMustAvoid(
            StoryCreativeConstitution? constitution,
            CreativeKnowledgeRetrievalResult? knowledge)
        {
            var avoid = new List<string>
            {
                "禁止卷中只靠临时新设定解决危机。",
                "禁止连续章节复用同一种胜利结构。",
                "禁止卷末高潮没有代价或状态变化。"
            };
            avoid.AddRange(constitution?.ForbiddenDirections ?? Enumerable.Empty<string>());
            avoid.AddRange(knowledge?.TropeWarnings.Take(3) ?? Enumerable.Empty<string>());
            return avoid
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(10)
                .ToList();
        }

        private static string BuildMidpointReversal(
            StoryCreativeConstitution? constitution,
            CreativeKnowledgeRetrievalResult? knowledge)
        {
            if ((constitution?.GenreProfile?.MysteryStrength ?? 0) >= 7)
                return "中段揭示主角追查的目标本身被误导，但前文至少有一个可回看的公平证据。";
            if ((constitution?.GenreProfile?.PleasureStrength ?? 0) >= 8)
                return "中段胜利转化为更高层敌人或规则的注意，爽点之后立刻产生反噬压力。";
            return knowledge?.AntiTropeStrategies.FirstOrDefault()
                   ?? "中段把外部冲突转化为角色选择困境。";
        }

        private static string BuildClimax(
            StoryCreativeConstitution? constitution,
            string genre)
        {
            if ((constitution?.GenreProfile?.EmotionStrength ?? 0) >= 8)
                return "卷末高潮必须让主角在正确答案和重要关系之间做不可逆选择。";
            if (genre.Contains("悬疑") || (constitution?.GenreProfile?.MysteryStrength ?? 0) >= 8)
                return "卷末高潮回收核心证据，同时打开更高层真相。";
            return "卷末高潮让主角以主动代价完成阶段胜利，并把冲突升级到下一卷。";
        }

        private static string FirstNonEmpty(params string?[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
            }

            return string.Empty;
        }
    }
}
