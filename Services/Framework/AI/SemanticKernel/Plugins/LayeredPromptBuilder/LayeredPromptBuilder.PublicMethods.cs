using System.Text;
using TM.Framework.UI.Workspace.Services.Spec;
using TM.Services.Modules.ProjectData.Models.TaskContexts;
using TM.Services.Modules.ProjectData.Models.Tracking;

namespace TM.Services.Framework.AI.SemanticKernel.Plugins
{
    public partial class LayeredPromptBuilder
    {
        #region 公开方法

        public string BuildLayeredPrompt(
            ContentTaskContext taskContext,
            FactSnapshot factSnapshot,
            CreativeSpec? spec)
        {
            var sb = new StringBuilder();

            sb.AppendLine("<chapter_generation_task>");
            sb.AppendLine();
            AppendChangesFormatReminder(sb);

            AppendFactLedgerSection(sb, factSnapshot);

            AppendLongDistanceRecallSection(sb, taskContext.LongDistanceRecallFragments);

            AppendFirstDescriptionSnippetsSection(sb, taskContext.FirstDescriptionSnippets);

            AppendTaskSection(sb, taskContext, spec);

            AppendChapterTailSection(sb, taskContext);

            sb.AppendLine("> ⚠ 正文末尾的变更摘要（CHANGES协议）格式、字段定义及校验规则已在系统提示词中完整定义，请严格遵守。");
            sb.AppendLine();

            AppendTailEntityChecklist(sb, taskContext);

            AppendWordCountAnchor(sb, taskContext, spec);

            AppendPrefilledChangesTemplate(sb, factSnapshot, taskContext.ContextIds);

            sb.AppendLine("</chapter_generation_task>");

            return sb.ToString();
        }

        #endregion
    }
}
