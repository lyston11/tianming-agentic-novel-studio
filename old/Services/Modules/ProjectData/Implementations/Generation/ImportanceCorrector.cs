using System;
using System.Collections.Generic;
using TM.Services.Modules.ProjectData.Models.Tracking;

namespace TM.Services.Modules.ProjectData.Implementations.Generation
{
    public static class ImportanceCorrector
    {

        private static readonly string[] CriticalCharKeywords =
        {
            "死亡", "陨落", "殒命", "身死", "牺牲", "战死", "重伤",
            "濒死", "垂死", "重创", "残废", "残疾", "断臂", "失忆",
            "失去修为", "废功", "散功", "修为尽废", "境界崩塌", "道基破碎",
            "魂飞魄散", "神魂俱灭", "魂魄消散", "肉身毁灭", "肉身崩毁",
            "入魔", "走火入魔"
        };

        private static readonly string[] ImportantCharKeywords =
        {
            "突破", "晋级", "觉醒", "结契", "成婚", "背叛", "叛变", "出走",
            "决裂", "认主", "拜师", "收徒", "封印解除", "境界跌落",
            "升级", "破境", "晋升", "晋阶", "顿悟", "开窍", "入门", "出师",
            "结拜", "反目", "绝交", "和解", "重逢", "认亲", "复仇",
            "解封", "归隐", "堕魔", "黑化", "心境突破", "境界回落"
        };

        private static readonly string[] CriticalFactionKeywords =
        {
            "覆灭", "灭亡", "解散", "叛离", "并入", "被灭", "宗门覆灭",
            "灭门", "灭宗", "除名", "覆没", "瓦解", "分裂", "崩溃",
            "崩塌", "破产", "倒闭", "被吞并"
        };

        private static readonly string[] ImportantFactionKeywords =
        {
            "成立", "合并", "结盟", "交战", "宣战", "投降",
            "扩张", "收缩", "迁移", "改名", "更名", "归附", "臣服",
            "停战", "议和", "开战", "反叛", "背叛", "夺权", "换主",
            "改组", "重组", "招安", "联盟"
        };

        private static readonly string[] ConflictImportantStatusKeywords =
        {
            "解决", "结束", "完结", "收束", "已结", "闭环", "落幕",
            "告终", "平息", "化解", "消弭", "了结", "失败", "破裂",
            "失控", "终止", "取消"
        };

        private const int MaxCriticalPerChapter = 3;

        private static readonly Dictionary<string, string> ImportanceValueMap = new(StringComparer.OrdinalIgnoreCase)
        {
            ["关键"] = "critical", ["严重"] = "critical", ["极重要"] = "critical",
            ["至关重要"] = "critical", ["决定性"] = "critical", ["核心"] = "critical",
            ["重要"] = "important", ["较重要"] = "important", ["重大"] = "important",
            ["普通"] = "normal", ["一般"] = "normal", ["常规"] = "normal",
            ["次要"] = "normal", ["轻微"] = "normal", ["不重要"] = "normal"
        };

        public static void Correct(ChapterChanges? changes)
        {
            if (changes == null) return;

            try
            {
                NormalizeAllImportance(changes);
                CorrectCharacterStateChanges(changes);
                CorrectConflictProgress(changes);
                CorrectFactionStateChanges(changes);
                EnforceCriticalCap(changes);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ImportanceCorrector] 校正异常（不影响落盘）: {ex.Message}");
            }
        }

        private static string NormalizeImportance(string? importance)
        {
            if (string.IsNullOrWhiteSpace(importance)) return "normal";
            var trimmed = importance.Trim();
            var lower = trimmed.ToLowerInvariant();
            if (lower == "critical" || lower == "important" || lower == "normal") return lower;
            if (ImportanceValueMap.TryGetValue(trimmed, out var mapped)) return mapped;
            return "normal";
        }

        private static void NormalizeAllImportance(ChapterChanges changes)
        {
            foreach (var c in changes.CharacterStateChanges ?? new()) c.Importance = NormalizeImportance(c.Importance);
            foreach (var c in changes.ConflictProgress ?? new()) c.Importance = NormalizeImportance(c.Importance);
            foreach (var p in changes.NewPlotPoints ?? new()) p.Importance = NormalizeImportance(p.Importance);
            foreach (var l in changes.LocationStateChanges ?? new()) l.Importance = NormalizeImportance(l.Importance);
            foreach (var f in changes.FactionStateChanges ?? new()) f.Importance = NormalizeImportance(f.Importance);
            if (changes.TimeProgression != null) changes.TimeProgression.Importance = NormalizeImportance(changes.TimeProgression.Importance);
            foreach (var m in changes.CharacterMovements ?? new()) m.Importance = NormalizeImportance(m.Importance);
            foreach (var s in changes.SecretRevealChanges ?? new()) s.Importance = NormalizeImportance(s.Importance);
            foreach (var t in changes.ItemTransfers ?? new()) t.Importance = NormalizeImportance(t.Importance);
            foreach (var p in changes.PledgeConstraintChanges ?? new()) p.Importance = NormalizeImportance(p.Importance);
            foreach (var d in changes.DeadlineConstraintChanges ?? new()) d.Importance = NormalizeImportance(d.Importance);
        }

        private static void CorrectCharacterStateChanges(ChapterChanges changes)
        {
            foreach (var c in changes.CharacterStateChanges ?? new())
            {
                if (string.IsNullOrWhiteSpace(c.KeyEvent)) continue;
                var kv = c.KeyEvent;

                if (ContainsAny(kv, CriticalCharKeywords))
                {
                    c.Importance = "critical";
                }
                else if (!string.Equals(c.Importance, "critical", StringComparison.OrdinalIgnoreCase) && ContainsAny(kv, ImportantCharKeywords))
                {
                    c.Importance = "important";
                }
            }
        }

        private static void CorrectConflictProgress(ChapterChanges changes)
        {
            foreach (var c in changes.ConflictProgress ?? new())
            {
                if (string.Equals(c.Importance, "critical", StringComparison.OrdinalIgnoreCase)) continue;
                var status = c.NewStatus ?? string.Empty;
                var lower = status.ToLowerInvariant();
                if (lower == "resolved" || lower == "failed" || lower == "ended" ||
                    lower == "done" || lower == "closed" || lower == "finished" ||
                    lower == "complete" || lower == "completed" ||
                    ContainsAny(status, ConflictImportantStatusKeywords))
                    c.Importance = "important";
            }
        }

        private static void CorrectFactionStateChanges(ChapterChanges changes)
        {
            foreach (var f in changes.FactionStateChanges ?? new())
            {
                if (string.IsNullOrWhiteSpace(f.Event)) continue;
                var ev = f.Event;

                if (ContainsAny(ev, CriticalFactionKeywords))
                    f.Importance = "critical";
                else if (!string.Equals(f.Importance, "critical", StringComparison.OrdinalIgnoreCase) && ContainsAny(ev, ImportantFactionKeywords))
                    f.Importance = "important";
            }
        }

        private static void EnforceCriticalCap(ChapterChanges changes)
        {
            var criticals = new List<Action>();

            foreach (var c in changes.CharacterStateChanges ?? new())
                if (string.Equals(c.Importance, "critical", StringComparison.OrdinalIgnoreCase))
                    criticals.Add(() => c.Importance = "important");

            foreach (var c in changes.ConflictProgress ?? new())
                if (string.Equals(c.Importance, "critical", StringComparison.OrdinalIgnoreCase))
                    criticals.Add(() => c.Importance = "important");

            foreach (var f in changes.FactionStateChanges ?? new())
                if (string.Equals(f.Importance, "critical", StringComparison.OrdinalIgnoreCase))
                    criticals.Add(() => f.Importance = "important");

            if (criticals.Count > MaxCriticalPerChapter)
            {
                for (int i = MaxCriticalPerChapter; i < criticals.Count; i++)
                    criticals[i]();
                TM.App.Log($"[ImportanceCorrector] critical 超限({criticals.Count})，已将后{criticals.Count - MaxCriticalPerChapter}条降为 important");
            }
        }

        private static bool ContainsAny(string text, string[] keywords)
        {
            foreach (var kw in keywords)
                if (text.Contains(kw, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }
    }
}
