using System;
using System.Collections.Generic;
using System.Linq;
using TM.Services.Framework.AI.NovelAgent.Models;

namespace TM.Services.Framework.AI.NovelAgent.Services
{
    public sealed class CommercialRhythmChecker
    {
        private static readonly string[] PleasureKeywords =
        {
            "赢", "突破", "压制", "反杀", "翻盘", "奖励", "晋升", "掌控", "爽", "打脸",
            "破局", "胜", "夺回", "证明", "解锁"
        };

        private static readonly string[] ClueKeywords =
        {
            "线索", "证据", "钥匙", "暗示", "异常", "痕迹", "真相", "揭示", "答案",
            "伏笔", "回收", "印记", "账本"
        };

        private static readonly string[] EmotionPayoffKeywords =
        {
            "释然", "痛快", "愧疚", "动摇", "信任", "背叛", "和解", "决裂", "恐惧",
            "愤怒", "清醒", "承认", "选择", "代价"
        };

        private static readonly string[] HookKeywords =
        {
            "然而", "可就在", "下一刻", "门后", "最后", "忽然", "低语", "名字",
            "真相", "代价", "来不及", "有人", "出现", "开启"
        };

        public List<string> BuildPlanningNotes(StoryCreativeConstitution? constitution)
        {
            var notes = new List<string>();
            if (constitution == null) return notes;

            if (!string.IsNullOrWhiteSpace(constitution.CommercialRhythm))
                notes.Add($"商业节奏：{constitution.CommercialRhythm}");

            var profile = constitution.GenreProfile;
            if (profile.PleasureStrength >= 8)
                notes.Add("本章至少保留一个可感知的爽点兑现，但爽点必须绑定代价或新压力。");
            if (profile.MysteryStrength >= 8)
                notes.Add("本章线索要么新增证据，要么改变旧线索含义，避免只制造雾气。");
            if (profile.EmotionStrength >= 8)
                notes.Add("本章情绪回报必须落到选择、关系变化或不可逆代价上。");
            if (profile.PaceStrength >= 8)
                notes.Add("章末需要明确下一章驱动力：新问题、新威胁、新奖励或新倒计时。");

            return notes.Distinct().Take(6).ToList();
        }

        public IReadOnlyList<NovelAgentReviewCheck> EvaluateGeneratedChapter(
            StoryCreativeConstitution? constitution,
            ChapterCreativeBrief? brief,
            StoryStateSnapshot storyState,
            string content)
        {
            var checks = new List<NovelAgentReviewCheck>();
            if (string.IsNullOrWhiteSpace(content))
            {
                checks.Add(new NovelAgentReviewCheck
                {
                    Key = "commercial_rhythm_content_empty",
                    Name = "商业节奏基础",
                    Status = NovelAgentReviewCheckStatus.Fail,
                    RiskLevel = NovelToolRiskLevel.High,
                    Message = "正文为空，无法形成章节节奏。",
                    Suggestions = new List<string> { "先生成章节正文，再进行商业节奏审计。" }
                });
                return checks;
            }

            var profile = constitution?.GenreProfile ?? new GenreDirectionProfile();
            checks.Add(BuildKeywordCheck(
                "commercial_pleasure_payoff",
                "爽点兑现",
                content,
                PleasureKeywords,
                required: profile.PleasureStrength >= 7 || ContainsAny(brief?.RecommendedCandidateTitle, new[] { "代价", "规则", "失败", "伏笔", "关系" }),
                pass: "正文具备可感知的阶段性爽点或破局信号。",
                miss: "正文缺少阶段性爽点或破局信号，连续阅读驱动力会变弱。",
                suggestion: "补一个具体可见的推进结果：赢下一小局、拿到资源、揭开线索、迫使对手让步或解锁新能力。"));

            checks.Add(BuildKeywordCheck(
                "commercial_clue_progress",
                "线索推进",
                content,
                ClueKeywords,
                required: profile.MysteryStrength >= 7 || storyState.ActiveForeshadowing.Count > 0,
                pass: "正文具备线索、伏笔或真相推进信号。",
                miss: "正文缺少线索推进，悬疑/伏笔线可能停滞。",
                suggestion: "让旧伏笔改变含义，或新增一个能指导下一章行动的证据。"));

            checks.Add(BuildKeywordCheck(
                "commercial_emotion_payoff",
                "情绪回报",
                content,
                EmotionPayoffKeywords,
                required: profile.EmotionStrength >= 7 || storyState.CharacterStates.Count > 0,
                pass: "正文具备情绪回报、关系变化或角色选择信号。",
                miss: "正文缺少情绪回报，人物变化可能停留在事件层。",
                suggestion: "把情绪落到一个选择：信任/背叛、承认/否认、承担/逃避、和解/决裂。"));

            checks.Add(BuildEndingHookCheck(content));
            checks.Add(BuildContinuityDriveCheck(brief, storyState, content));
            return checks;
        }

        private static NovelAgentReviewCheck BuildKeywordCheck(
            string key,
            string name,
            string content,
            IReadOnlyCollection<string> keywords,
            bool required,
            string pass,
            string miss,
            string suggestion)
        {
            var evidence = ExtractEvidence(content, keywords).Take(6).ToList();
            var ok = evidence.Count > 0;
            return new NovelAgentReviewCheck
            {
                Key = key,
                Name = name,
                Status = ok
                    ? NovelAgentReviewCheckStatus.Pass
                    : required ? NovelAgentReviewCheckStatus.Warning : NovelAgentReviewCheckStatus.Pass,
                RiskLevel = required ? NovelToolRiskLevel.Medium : NovelToolRiskLevel.Low,
                Message = ok ? pass : required ? miss : "当前类型风向下不是强制节奏项。",
                Evidence = evidence,
                Suggestions = ok || !required ? new List<string>() : new List<string> { suggestion }
            };
        }

        private static NovelAgentReviewCheck BuildEndingHookCheck(string content)
        {
            var tail = content.Length <= 260 ? content : content[^260..];
            var evidence = ExtractEvidence(tail, HookKeywords).Take(5).ToList();
            var hasQuestion = tail.Contains('？') || tail.Contains('?');
            var ok = evidence.Count > 0 || hasQuestion;
            return new NovelAgentReviewCheck
            {
                Key = "commercial_ending_hook",
                Name = "章末钩子",
                Status = ok ? NovelAgentReviewCheckStatus.Pass : NovelAgentReviewCheckStatus.Warning,
                RiskLevel = NovelToolRiskLevel.Medium,
                Message = ok ? "章末具备下一章钩子。" : "章末钩子不够明确，读者可能缺少继续翻页理由。",
                Evidence = evidence,
                Suggestions = ok
                    ? new List<string>()
                    : new List<string> { "章末留下新问题、新威胁、新奖励、新倒计时或关系爆点。" }
            };
        }

        private static NovelAgentReviewCheck BuildContinuityDriveCheck(
            ChapterCreativeBrief? brief,
            StoryStateSnapshot storyState,
            string content)
        {
            var driveSignals = new List<string>();
            if (!string.IsNullOrWhiteSpace(brief?.CostOrConsequence) && HasTokenOverlap(content, brief.CostOrConsequence))
                driveSignals.Add("本章代价可延续到下一章。");
            if (storyState.ActiveConflicts.Any(c => HasTokenOverlap(content, c)))
                driveSignals.Add("主线冲突仍有延续压力。");
            if (storyState.ActiveForeshadowing.Any(f => HasTokenOverlap(content, f)))
                driveSignals.Add("伏笔线仍有后续兑现压力。");
            if (ContainsAny(content, new[] { "必须", "否则", "只剩", "明日", "三日", "倒计时", "追来", "代价" }))
                driveSignals.Add("正文含有时间/威胁/代价驱动。");

            return new NovelAgentReviewCheck
            {
                Key = "commercial_continuity_drive",
                Name = "连续阅读驱动力",
                Status = driveSignals.Count > 0 ? NovelAgentReviewCheckStatus.Pass : NovelAgentReviewCheckStatus.Warning,
                RiskLevel = NovelToolRiskLevel.Medium,
                Message = driveSignals.Count > 0
                    ? "正文保留了下一章继续推进的压力。"
                    : "正文缺少明确的下一章驱动力。",
                Evidence = driveSignals,
                Suggestions = driveSignals.Count > 0
                    ? new List<string>()
                    : new List<string> { "在结尾绑定一个未解决压力：债务追索、敌人动作、关系裂痕、线索倒计时或奖励目标。" }
            };
        }

        private static bool ContainsAny(string? content, IEnumerable<string> keywords)
        {
            if (string.IsNullOrWhiteSpace(content)) return false;
            return keywords.Any(k => content.Contains(k, StringComparison.OrdinalIgnoreCase));
        }

        private static IEnumerable<string> ExtractEvidence(string content, IEnumerable<string> keywords)
        {
            foreach (var keyword in keywords)
            {
                if (content.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    yield return keyword;
            }
        }

        private static bool HasTokenOverlap(string content, string expected)
        {
            if (string.IsNullOrWhiteSpace(content) || string.IsNullOrWhiteSpace(expected))
                return false;

            var tokens = expected
                .Split(new[] { ' ', '\t', '\r', '\n', '，', '。', '、', '；', ';', ',', '.', '：', ':', '！', '？', '/', '|' },
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(t => t.Length >= 2)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(12)
                .ToList();
            if (tokens.Count == 0)
                return content.Contains(expected.Trim(), StringComparison.OrdinalIgnoreCase);

            var hitCount = tokens.Count(t => content.Contains(t, StringComparison.OrdinalIgnoreCase));
            return hitCount >= Math.Max(1, Math.Min(3, tokens.Count / 2));
        }
    }
}
