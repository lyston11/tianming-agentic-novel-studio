using System.Collections.Generic;
using System.Text;
using TM.Framework.UI.Workspace.RightPanel.Modes;
using TM.Framework.UI.Workspace.Services.Spec;
using TM.Services.Framework.AI.SemanticKernel.Prompts.Developer;
using TM.Services.Framework.AI.SemanticKernel.Prompts.Behavior;
using TM.Services.Framework.AI.SemanticKernel.Prompts.Dialog;
using TM.Services.Framework.AI.SemanticKernel.Prompts.Business;
using TM.Services.Framework.AI.SemanticKernel.Prompts.Spec;
using TM.Services.Framework.AI.Interfaces.Prompts;
using System;
using System.Linq;

namespace TM.Services.Framework.AI.SemanticKernel.Prompts
{
    public static class PromptLibrary
    {
        #region L1

        public static string GetDeveloperPrompt() => DeveloperPromptProvider.BaseDeveloperMessage;

        #endregion

        #region L2

        public static string GetModeTemplate(ChatMode mode) => BehaviorPromptProvider.GetModeTemplate(mode);

        public static bool IsIdentityQuestion(string input) => BehaviorPromptProvider.IsIdentityQuestion(input);

        #endregion

        #region L3

        public static string GetAnalysisAnswerSpec() => DialogPromptProvider.AnalysisAnswerSpec;

        #endregion

        #region L4

        public static string GetDialogueBusinessPrompt() => BusinessPromptProvider.DialogueBusinessPrompt;

        #endregion

        #region L5

        public static string BuildSpecPrompt(CreativeSpec? spec) => SpecPromptProvider.BuildSpecPrompt(spec);

        #endregion

        #region Build

        public static ChatPromptParts BuildPromptParts(
            ChatMode mode,
            string userInput,
            bool includeBusinessPrompt = false,
            CreativeSpec? spec = null)
        {
            var isIdentityQuestion = BehaviorPromptProvider.IsIdentityQuestion(userInput);
            var systemPrompt = BuildSystemPromptForMode(mode, includeBusinessPrompt, spec, isIdentityQuestion);
            var userPrompt = BehaviorPromptProvider.BuildUserPrompt(mode, userInput);

            return new ChatPromptParts
            {
                SystemPrompt = systemPrompt,
                UserPrompt = userPrompt
            };
        }

        private static string BuildSystemPromptForMode(
            ChatMode mode,
            bool includeBusinessPrompt,
            CreativeSpec? spec,
            bool isIdentityQuestion = false)
        {
            var sb = new StringBuilder();
            var rawSpecPrompt = includeBusinessPrompt && spec != null
                ? LoadSpecTemplateRawPrompt(spec.TemplateName)
                : null;
            var hasRawSpecPrompt = !string.IsNullOrWhiteSpace(rawSpecPrompt);
            var structuredSpecPrompt = BuildStructuredSpecPromptForBusiness(spec, hasRawSpecPrompt);

            sb.Append(GetDeveloperPrompt());

            sb.Append("\n\n");
            sb.Append(GetModeTemplate(mode));

            if (hasRawSpecPrompt)
            {
                sb.Append("\n\n");
                sb.Append("<genre_spec priority=\"highest\" source=\"prompt_library\">\n");
                sb.Append(rawSpecPrompt);
                sb.Append("\n</genre_spec>");
            }

            if (includeBusinessPrompt)
            {
                sb.Append("\n\n");
                sb.Append(GetDialogueBusinessPrompt());
            }

            if (!string.IsNullOrWhiteSpace(structuredSpecPrompt))
            {
                sb.Append("\n\n");
                sb.Append(structuredSpecPrompt);
            }

            if (!includeBusinessPrompt && !isIdentityQuestion)
            {
                sb.Append("\n\n");
                sb.Append(GetAnalysisAnswerSpec());
            }

            return sb.ToString();
        }

        private static string? LoadSpecTemplateRawPrompt(string? templateName)
        {
            if (string.IsNullOrEmpty(templateName)) return null;
            try
            {
                var repo = ServiceLocator.Get<IPromptRepository>();
                var specTemplate = repo.GetAllTemplates()
                    .FirstOrDefault(t => t.Name == templateName
                        && t.Tags != null && t.Tags.Contains("Spec"));
                return specTemplate?.SystemPrompt;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[PromptLibrary] 加载Spec模板失败: {ex.Message}");
                return null;
            }
        }

        private static string BuildStructuredSpecPromptForBusiness(CreativeSpec? spec, bool hasRawSpecPrompt)
        {
            if (spec == null)
                return string.Empty;

            if (!hasRawSpecPrompt)
                return BuildSpecPrompt(spec);

            var parts = new List<string>();

            if (!string.IsNullOrWhiteSpace(spec.WritingStyle))
                parts.Add($"项目当前写作风格（覆盖题材模板同名要求）：{spec.WritingStyle}");

            if (!string.IsNullOrWhiteSpace(spec.Pov))
                parts.Add($"项目当前叙事视角（覆盖题材模板同名要求）：{spec.Pov}");

            if (!string.IsNullOrWhiteSpace(spec.Tone))
                parts.Add($"项目当前语气基调（覆盖题材模板同名要求）：{spec.Tone}");

            if (spec.TargetWordCount.HasValue)
            {
                parts.Add($"项目当前目标字数（覆盖题材模板同名要求）：{spec.TargetWordCount.Value} 字（仅统计正文，不含标题与 <chapter_changes> 标签内的内容）");
            }

            if (spec.ParagraphLength.HasValue)
                parts.Add($"项目当前段落长度偏好：约{spec.ParagraphLength}字");

            if (spec.DialogueRatio.HasValue)
                parts.Add($"项目当前对话比例（覆盖题材模板同名要求）：约{spec.DialogueRatio * 100:F0}%");

            if (spec.MustInclude?.Any(v => !string.IsNullOrWhiteSpace(v)) == true)
                parts.Add($"项目额外必须包含：{string.Join("、", spec.MustInclude.Where(v => !string.IsNullOrWhiteSpace(v)))}");

            if (spec.MustAvoid?.Any(v => !string.IsNullOrWhiteSpace(v)) == true)
                parts.Add($"项目额外避免内容：{string.Join("、", spec.MustAvoid.Where(v => !string.IsNullOrWhiteSpace(v)))}");

            if (spec.CharacterFocus?.Any(v => !string.IsNullOrWhiteSpace(v)) == true)
                parts.Add($"项目当前聚焦角色：{string.Join("、", spec.CharacterFocus.Where(v => !string.IsNullOrWhiteSpace(v)))}");

            if (!string.IsNullOrWhiteSpace(spec.SceneDescription))
                parts.Add($"项目当前场景描述：{spec.SceneDescription}");

            if (!string.IsNullOrWhiteSpace(spec.EmotionalArc))
                parts.Add($"项目当前情感曲线：{spec.EmotionalArc}");

            if (!string.IsNullOrWhiteSpace(spec.PlotPoints))
                parts.Add($"项目当前剧情要点：{spec.PlotPoints}");

            if (parts.Count == 0)
                return string.Empty;

            return "<creative_spec_overrides priority=\"highest\" source=\"project_settings\" override_target=\"genre_spec\">\n" + string.Join("\n", parts) + "\n</creative_spec_overrides>";
        }

        #endregion

        #region Shortcuts

        public static ChatPromptParts BuildSimplePromptParts(ChatMode mode, string userInput)
        {
            return BuildPromptParts(mode, userInput, includeBusinessPrompt: false, spec: null);
        }

        #endregion
    }
}
