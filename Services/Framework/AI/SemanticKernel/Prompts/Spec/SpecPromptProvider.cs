using System.Collections.Generic;
using System.Linq;
using TM.Framework.UI.Workspace.Services.Spec;

namespace TM.Services.Framework.AI.SemanticKernel.Prompts.Spec
{
    public static class SpecPromptProvider
    {
        public static string BuildSpecPrompt(CreativeSpec? spec)
        {
            if (spec == null) return string.Empty;

            var parts = new List<string>();

            if (!string.IsNullOrEmpty(spec.WritingStyle))
                parts.Add($"写作风格：{spec.WritingStyle}");

            if (!string.IsNullOrEmpty(spec.Pov))
                parts.Add($"叙事视角：{spec.Pov}");

            if (!string.IsNullOrEmpty(spec.Tone))
                parts.Add($"语气基调：{spec.Tone}");

            if (spec.TargetWordCount.HasValue)
                parts.Add($"目标字数：{spec.TargetWordCount.Value} 字（仅统计正文，不含标题与 <chapter_changes> 标签内的内容）");

            if (spec.DialogueRatio.HasValue)
                parts.Add($"对话比例：约{spec.DialogueRatio * 100:F0}%");

            var mustInclude = spec.MustInclude?.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
            if (mustInclude?.Length > 0)
                parts.Add($"必须包含：{string.Join("、", mustInclude)}");

            var mustAvoid = spec.MustAvoid?.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
            if (mustAvoid?.Length > 0)
                parts.Add($"避免内容：{string.Join("、", mustAvoid)}");

            var characterFocus = spec.CharacterFocus?.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
            if (characterFocus?.Length > 0)
                parts.Add($"聚焦角色：{string.Join("、", characterFocus)}");

            if (!string.IsNullOrEmpty(spec.EmotionalArc))
                parts.Add($"情感曲线：{spec.EmotionalArc}");

            if (!string.IsNullOrEmpty(spec.PlotPoints))
                parts.Add($"剧情要点：{spec.PlotPoints}");

            if (parts.Count == 0) return string.Empty;

            return "<creative_spec priority=\"highest\" source=\"project_settings\">\n" + string.Join("\n", parts) + "\n</creative_spec>";
        }
    }
}
