using System.Collections.Generic;
using System.Text;

namespace TM.Services.Framework.AI.SemanticKernel.PromptToolFallback
{
    public static class PromptToolResultFormatter
    {
        public static string FormatSingle(PromptToolInvocationResult result)
        {
            if (result == null) return string.Empty;

            var sb = new StringBuilder();
            sb.AppendLine(PromptToolProtocol.ToolResultOpen);

            var fullName = string.IsNullOrEmpty(result.PluginName)
                ? result.FunctionName
                : $"{result.PluginName}.{result.FunctionName}";

            sb.Append("  ").Append(PromptToolProtocol.ResultToolNameOpen)
                .Append(XmlEscape(fullName))
                .Append(PromptToolProtocol.ResultToolNameClose).AppendLine();

            sb.Append("  ").Append(PromptToolProtocol.ResultContentOpen);

            if (result.Succeeded)
            {
                sb.Append(XmlEscape(result.Result ?? string.Empty));
            }
            else
            {
                sb.Append("[error] ").Append(XmlEscape(result.ErrorMessage ?? "工具调用失败"));
            }

            sb.Append(PromptToolProtocol.ResultContentClose).AppendLine();
            sb.AppendLine(PromptToolProtocol.ToolResultClose);

            return sb.ToString();
        }

        public static string FormatBatch(IEnumerable<PromptToolInvocationResult> results)
        {
            var sb = new StringBuilder();
            foreach (var r in results)
            {
                sb.Append(FormatSingle(r));
            }
            return sb.ToString();
        }

        public static string XmlEscape(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return text
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&apos;");
        }
    }
}
