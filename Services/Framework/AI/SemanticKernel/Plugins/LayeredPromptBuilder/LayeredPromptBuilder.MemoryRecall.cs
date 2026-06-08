using System.Collections.Generic;
using System.Linq;
using System.Text;
using TM.Services.Modules.ProjectData.Models.TaskContexts;

namespace TM.Services.Framework.AI.SemanticKernel.Plugins
{
    public partial class LayeredPromptBuilder
    {
        #region 私有方法 - 长距离记忆召回

        private void AppendLongDistanceRecallSection(StringBuilder sb, List<LongDistanceRecallFragment>? fragments)
        {
            if (fragments == null || fragments.Count == 0)
                return;

            sb.AppendLine("<context_block source=\"long_distance_recall\">");
            sb.AppendLine("> 以下是从历史章节中检索到的与本章相关内容，请注意保持一致性。");
            sb.AppendLine();

            var groups = fragments
                .GroupBy(f => string.IsNullOrWhiteSpace(f.Category) ? "General" : f.Category)
                .OrderBy(g => g.Key switch
                {
                    "Foreshadowing" => 0,
                    "Character" => 1,
                    _ => 2
                });

            foreach (var group in groups)
            {
                var label = group.Key switch
                {
                    "Foreshadowing" => "【伏笔相关历史】",
                    "Character" => "【角色相关历史】",
                    _ => "【一般相关历史】"
                };
                sb.AppendLine($"**{label}**");
                foreach (var fragment in group)
                {
                    sb.AppendLine($"来源: {fragment.ChapterId}（相关度 {fragment.Score:F3}）");
                    sb.AppendLine(fragment.Content);
                    sb.AppendLine();
                }
            }

            sb.AppendLine("</context_block>");
            sb.AppendLine();
        }

        private void AppendFirstDescriptionSnippetsSection(StringBuilder sb, List<FirstDescriptionSnippet>? snippets)
        {
            if (snippets == null || snippets.Count == 0) return;

            sb.AppendLine("<context_block source=\"first_descriptions\">");
            sb.AppendLine("> 以下是本章涉及实体在首次出现章节的描写片段，请保持外观 / 风格的一致性。");
            sb.AppendLine();

            foreach (var s in snippets)
            {
                sb.AppendLine($"**{s.EntityName}** · 首次出现：{s.ChapterId}");
                sb.AppendLine(s.Content);
                sb.AppendLine();
            }

            sb.AppendLine("</context_block>");
            sb.AppendLine();
        }

        #endregion
    }
}
