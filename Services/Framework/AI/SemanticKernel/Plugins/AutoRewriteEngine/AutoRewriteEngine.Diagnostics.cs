using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TM.Framework.UI.Workspace.Services.Spec;
using TM.Services.Framework.AI.Interfaces.Prompts;
using TM.Services.Modules.ProjectData.Implementations;
using TM.Services.Modules.ProjectData.Models.Guides;
using TM.Services.Modules.ProjectData.Models.TaskContexts;
using TM.Services.Modules.ProjectData.Models.Tracking;

namespace TM.Services.Framework.AI.SemanticKernel.Plugins
{
    public partial class AutoRewriteEngine
    {
        private static void AppendValidEntityHint(
            StringBuilder sb,
            List<string> failures,
            string fieldName,
            List<string>? validEntries,
            params string[] keywords)
        {
            if (validEntries == null || validEntries.Count == 0) return;
            if (keywords == null || keywords.Length == 0) return;
            bool hasError = failures.Any(f => keywords.Any(k => !string.IsNullOrWhiteSpace(k) && f.Contains(k, StringComparison.OrdinalIgnoreCase)));
            if (!hasError) return;

            var distinct = validEntries.Distinct().ToList();
            sb.AppendLine();
            sb.AppendLine($"⚠ **{fieldName}纠正**：本章合法实体列表如下（优先直接复制括号内的 ShortId；不确定可写名称由系统解析；禁止自造/猜测）：");
            sb.AppendLine($"  {string.Join("、", distinct)}");
            sb.AppendLine("  请优先从上方列表复制对应的 ShortId。 ");
        }

        private static List<string> GetValidEntityHints(List<(string? name, string? id)>? pairs)
        {
            if (pairs == null) return new List<string>();
            return pairs
                .Where(p => !string.IsNullOrWhiteSpace(p.name) && !string.IsNullOrWhiteSpace(p.id))
                .Select(p => $"{p.name}（{p.id}）")
                .Distinct()
                .ToList();
        }

        private static readonly char[] _bpSeparators = { ',', '，', '、', ';', '；' };

        private static int CountBlueprintEntities(ContentTaskContext ctx)
        {
            if (ctx.Blueprints == null || ctx.Blueprints.Count == 0) return 0;
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var bp in ctx.Blueprints)
            {
                foreach (var sep in new[] { bp.Cast, bp.Locations, bp.Factions })
                {
                    if (string.IsNullOrWhiteSpace(sep)) continue;
                    foreach (var p in sep.Split(_bpSeparators, StringSplitOptions.RemoveEmptyEntries))
                    {
                        var n = p.Trim();
                        if (n.Length >= 2) names.Add(n);
                    }
                }
                if (!string.IsNullOrWhiteSpace(bp.PovCharacter)) names.Add(bp.PovCharacter.Trim());
            }
            return names.Count;
        }

        private static string SummarizeFailuresForProgress(List<string> failures)
        {
            if (failures.Count == 0) return "未知原因";
            var cleaned = failures
                .Take(2)
                .Select(f => FeedbackNameRegex.Replace(f, string.Empty).Trim())
                .Where(f => !string.IsNullOrWhiteSpace(f))
                .ToList();
            var summary = string.Join("；", cleaned);
            if (failures.Count > 2) summary += $"（共{failures.Count}项）";
            return summary;
        }

        private static List<string> CheckBlueprintCompliance(string content, ContentTaskContext ctx)
        {
            var missing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(content) || ctx.Blueprints == null || ctx.Blueprints.Count == 0)
                return new List<string>();

            foreach (var bp in ctx.Blueprints)
            {
                ExtractBlueprintMissing(content, bp.Cast, missing);
                ExtractBlueprintMissing(content, bp.Locations, missing);
                ExtractBlueprintMissing(content, bp.Factions, missing);
                if (!string.IsNullOrWhiteSpace(bp.PovCharacter))
                {
                    var name = bp.PovCharacter.Trim();
                    if (name.Length >= 2 && !content.Contains(name, StringComparison.OrdinalIgnoreCase))
                        missing.Add(name);
                }
            }
            return missing.ToList();
        }

        private static void ExtractBlueprintMissing(string content, string? raw, HashSet<string> missing)
        {
            if (string.IsNullOrWhiteSpace(raw)) return;
            foreach (var part in raw.Split(_bpSeparators, StringSplitOptions.RemoveEmptyEntries))
            {
                var name = part.Trim();
                if (name.Length < 2) continue;
                if (content.Contains(name, StringComparison.OrdinalIgnoreCase)) continue;
                var shortName = EntityNameNormalizeHelper.StripBracketAnnotation(name);
                if (!string.IsNullOrWhiteSpace(shortName) && shortName != name
                    && shortName.Length >= 2 && content.Contains(shortName, StringComparison.OrdinalIgnoreCase)) continue;
                missing.Add(name);
            }
        }

        private static int CountEffectiveChars(string text) => WordCountHelper.CountRaw(text);

        internal static string? DetectContentRepetition(string content, string? previousChapterTail)
        {
            if (string.IsNullOrWhiteSpace(content) || content.Length < 200) return null;

            var paragraphs = content.Split(new[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .Where(p => p.Length >= 40)
                .ToList();

            const int window = 5;
            for (int i = 0; i < paragraphs.Count; i++)
            {
                var limit = Math.Min(i + window, paragraphs.Count);
                for (int j = i + 1; j < limit; j++)
                {
                    var sim = ComputeCharBigramJaccard(paragraphs[i], paragraphs[j]);
                    if (sim > 0.6)
                    {
                        return $"正文第{i + 1}段与第{j + 1}段高度重复（相似度{sim:P0}），禁止段落复读，每段必须推进新内容";
                    }
                }
            }

            if (content.Length >= 600)
            {
                var mid = content.Length / 2;
                var firstHalf = content[..mid];
                var secondHalf = content[mid..];
                var halfSim = ComputeCharBigramJaccard(firstHalf, secondHalf);
                if (halfSim > 0.5)
                {
                    return $"正文前半段与后半段高度重复（相似度{halfSim:P0}），内容整体复读，每个场景必须推进新内容";
                }
            }

            if (!string.IsNullOrWhiteSpace(previousChapterTail) && previousChapterTail.Length >= 100)
            {
                var tailEnd = previousChapterTail.Length > 300
                    ? previousChapterTail[^300..]
                    : previousChapterTail;
                var contentStart = content.Length > 500
                    ? content[..500]
                    : content;
                var overlapSim = ComputeCharBigramJaccard(tailEnd, contentStart);
                if (overlapSim > 0.4)
                {
                    return $"正文开头与上一章尾部高度重叠（相似度{overlapSim:P0}），禁止情节重置，必须从上一章结尾状态直接向前推进";
                }
            }

            return null;
        }

        private static double ComputeCharBigramJaccard(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return 0;

            var bigramsA = new HashSet<string>();
            for (int i = 0; i < a.Length - 1; i++)
            {
                if (!char.IsWhiteSpace(a[i]) && !char.IsWhiteSpace(a[i + 1]))
                    bigramsA.Add(a.Substring(i, 2));
            }

            var bigramsB = new HashSet<string>();
            for (int i = 0; i < b.Length - 1; i++)
            {
                if (!char.IsWhiteSpace(b[i]) && !char.IsWhiteSpace(b[i + 1]))
                    bigramsB.Add(b.Substring(i, 2));
            }

            if (bigramsA.Count == 0 || bigramsB.Count == 0) return 0;

            int intersection = 0;
            foreach (var bg in bigramsA)
            {
                if (bigramsB.Contains(bg)) intersection++;
            }
            var union = bigramsA.Count + bigramsB.Count - intersection;
            return union > 0 ? (double)intersection / union : 0;
        }

        private static string StripChangesSection(string content)
            => GenerationGate.StripChangesSection(content);

        internal static DesignElementNames BuildDesignElementNames(ContentTaskContext ctx)
        {
            var names = new DesignElementNames();

            if (ctx.Characters != null)
            {
                foreach (var c in ctx.Characters)
                {
                    if (!string.IsNullOrWhiteSpace(c.Name))
                        names.CharacterNames.Add(c.Name);
                }
            }

            if (ctx.Locations != null)
            {
                foreach (var loc in ctx.Locations)
                {
                    if (!string.IsNullOrWhiteSpace(loc.Name))
                        names.LocationNames.Add(loc.Name);
                }
            }

            if (ctx.ExpandedCharacters != null)
            {
                foreach (var c in ctx.ExpandedCharacters)
                {
                    if (!string.IsNullOrWhiteSpace(c.Name) && !names.CharacterNames.Contains(c.Name))
                        names.CharacterNames.Add(c.Name);
                }
            }

            if (ctx.Blueprints != null)
            {
                foreach (var bp in ctx.Blueprints)
                {
                    if (!string.IsNullOrWhiteSpace(bp.PovCharacter))
                    {
                        var pov = bp.PovCharacter.Trim();
                        if (!names.CharacterNames.Contains(pov))
                            names.CharacterNames.Add(pov);
                        if (!names.PovCharacterNames.Contains(pov))
                            names.PovCharacterNames.Add(pov);
                    }
                    AddBlueprintTextEntities(bp.Cast, names.CharacterNames);
                    AddBlueprintTextEntities(bp.Locations, names.LocationNames);
                    AddBlueprintTextEntities(bp.Factions, names.FactionNames);
                }
            }

            return names;
        }

        private static readonly char[] _bpNameSeparators = { ',', '\uff0c', '\u3001', ';', '\uff1b' };

        private static void AddBlueprintTextEntities(string? raw, List<string> target)
        {
            if (string.IsNullOrWhiteSpace(raw)) return;
            foreach (var part in raw.Split(_bpNameSeparators, StringSplitOptions.RemoveEmptyEntries))
            {
                var name = part.Trim();
                if (name.Length >= 2 && !target.Contains(name))
                    target.Add(name);
            }
        }

        private static string BuildSystemPromptWithSpec(CreativeSpec? spec, FactSnapshot? factSnapshot = null, ContextIdCollection? contextIds = null)
        {
            var sb = new StringBuilder();

            IPromptRepository? repo = null;
            try { repo = ServiceLocator.Get<IPromptRepository>(); }
            catch (Exception ex) { TM.App.Log($"[AutoRewriteEngine] IPromptRepository resolve failed: {ex.Message}"); }

            if (spec != null && !string.IsNullOrEmpty(spec.TemplateName) && repo != null)
            {
                try
                {
                    var specTemplate = repo.GetAllTemplates()
                        .FirstOrDefault(t => t.Name == spec.TemplateName
                            && t.Tags != null && t.Tags.Contains("Spec"));
                    if (specTemplate != null && !string.IsNullOrWhiteSpace(specTemplate.SystemPrompt))
                    {
                        sb.AppendLine("<genre_spec priority=\"highest\" source=\"prompt_library\">");
                        sb.AppendLine(specTemplate.SystemPrompt);
                        sb.AppendLine("</genre_spec>");
                        sb.AppendLine();
                        if (InfoLogDedup.ShouldLog($"AutoRewriteEngine:SpecInjected:{specTemplate.Name}"))
                            TM.App.Log($"[AutoRewriteEngine] 已注入Spec模板原文: {specTemplate.Name}");
                    }
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[AutoRewriteEngine] 加载Spec模板失败: {ex.Message}");
                }
            }

            if (spec != null)
            {
                var specFragment = spec.BuildPromptFragment();
                if (!string.IsNullOrWhiteSpace(specFragment))
                {
                    sb.AppendLine("<creative_spec_overrides override_target=\"genre_spec\" priority=\"highest\">");
                    sb.AppendLine(specFragment);
                    sb.AppendLine("</creative_spec_overrides>");
                    sb.AppendLine();
                }
            }

            sb.Append(GetEnabledBusinessPrompt(repo));

            sb.AppendLine();
            sb.Append(LayeredPromptBuilder.GetChangesRequirementBlock(contextIds, factSnapshot));

            return sb.ToString();
        }

        private static string GetEnabledBusinessPrompt(IPromptRepository? repo = null)
        {
            try
            {
                repo ??= ServiceLocator.Get<IPromptRepository>();
                var templates = repo.GetTemplatesByCategory("业务提示词");
                var enabled = templates
                    .Where(t => t.IsEnabled && !string.IsNullOrWhiteSpace(t.SystemPrompt))
                    .OrderByDescending(t => t.IsDefault)
                    .FirstOrDefault();
                if (enabled != null)
                {
                    if (InfoLogDedup.ShouldLog($"AutoRewriteEngine:BusinessPrompt:{enabled.Id}"))
                        TM.App.Log($"[AutoRewriteEngine] 使用业务提示词模板: {enabled.Name} ({enabled.Id})");
                    return enabled.SystemPrompt;
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[AutoRewriteEngine] prompt repo err, fallback: {ex.Message}");
            }

            TM.App.Log("[AutoRewriteEngine] no template, fallback");
            return Prompts.Business.BusinessPromptProvider.GenerationBusinessPrompt;
        }

        private static bool IsProxyRoleRefusal(string refusalFragment)
        {
            if (string.IsNullOrEmpty(refusalFragment)) return false;
            ReadOnlySpan<string> proxyMarkers = new[]
            {
                "Cursor AI 代码编辑器",
                "Cursor AI",
                "Windsurf",
                "GitHub Copilot",
                "Codeium",
                "Tabnine",
                "代码编辑器",
                "不在我的服务范围",
                "不在我的职责范围",
                "超出了我的服务范围",
                "超出我的服务范围",
                "我的职责是专门为",
                "我只能回答与",
                "我只能处理与",
            };
            foreach (var marker in proxyMarkers)
            {
                if (refusalFragment.Contains(marker, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private static string? DetectModelRefusal(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return null;
            ReadOnlySpan<string> patterns = new[]
            {
                "I'm sorry, but I cannot",
                "I'm sorry, but I can't",
                "I am sorry, but I cannot",
                "I cannot assist with",
                "I can't assist with",
                "I'm unable to assist",
                "I am unable to assist",
                "I cannot and will not",
                "I can't and won't",
                "I apologize, but I cannot",
                "I apologize, but I can't",
                "I'm not able to",
                "I am not able to",
                "Sorry, I cannot",
                "Sorry, I can't",
                "As an AI, I cannot",
                "As an AI assistant, I cannot",
            };
            foreach (var pattern in patterns)
            {
                var idx = content.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                {
                    var endIdx = Math.Min(idx + 60, content.Length);
                    return content[idx..endIdx].Replace('\n', ' ').Trim();
                }
            }

            ReadOnlySpan<string> chinesePatterns = new[]
            {
                "Cursor AI 代码编辑器",
                "Cursor AI",
                "Windsurf",
                "GitHub Copilot",
                "Codeium",
                "Tabnine",
                "代码编辑器",
                "不在我的服务范围",
                "不在我的职责范围",
                "超出了我的服务范围",
                "超出我的服务范围",
                "我的职责是专门为",
                "我只能回答与",
                "我只能处理与",
                "我无法协助",
                "我不能协助",
                "我无法帮助",
                "抱歉，我无法",
                "对不起，我无法",
                "很抱歉，我无法",
                "我不被允许",
                "我被设计为",
            };
            foreach (var pattern in chinesePatterns)
            {
                var idx = content.IndexOf(pattern, StringComparison.Ordinal);
                if (idx >= 0)
                {
                    var endIdx = Math.Min(idx + 60, content.Length);
                    return content[idx..endIdx].Replace('\n', ' ').Trim();
                }
            }
            return null;
        }

    }
}

