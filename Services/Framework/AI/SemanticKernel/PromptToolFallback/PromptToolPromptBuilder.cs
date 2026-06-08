using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.SemanticKernel;

namespace TM.Services.Framework.AI.SemanticKernel.PromptToolFallback
{
    public static class PromptToolPromptBuilder
    {
        public static string BuildToolInstructions(IEnumerable<KernelFunction> allowedFunctions)
        {
            var list = allowedFunctions?.ToList() ?? new List<KernelFunction>();
            if (list.Count == 0)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            sb.AppendLine(PromptToolProtocol.ToolInstructionsHeader);
            sb.AppendLine("可用工具列表：");
            sb.AppendLine();

            foreach (var fn in list)
            {
                AppendFunction(sb, fn);
            }

            sb.AppendLine();
            sb.AppendLine(PromptToolProtocol.SafetyConstraints);
            return sb.ToString();
        }

        public static string BuildSingleFunction(KernelFunction function)
        {
            var sb = new StringBuilder();
            AppendFunction(sb, function);
            return sb.ToString();
        }

        private static void AppendFunction(StringBuilder sb, KernelFunction fn)
        {
            if (fn == null) return;

            var meta = fn.Metadata;
            var pluginName = string.IsNullOrEmpty(meta.PluginName) ? string.Empty : meta.PluginName + ".";

            sb.Append("- ").Append(pluginName).Append(meta.Name);
            if (!string.IsNullOrWhiteSpace(meta.Description))
            {
                sb.Append("：").Append(meta.Description.Trim());
            }
            sb.AppendLine();

            if (meta.Parameters == null || meta.Parameters.Count == 0)
            {
                sb.AppendLine("  参数：（无）");
                return;
            }

            sb.AppendLine("  参数：");
            foreach (var p in meta.Parameters)
            {
                var typeName = p.ParameterType?.Name ?? "string";
                var required = p.IsRequired ? " (必填)" : string.Empty;
                sb.Append("    - ").Append(p.Name).Append(": ").Append(typeName).Append(required);
                if (!string.IsNullOrWhiteSpace(p.Description))
                {
                    sb.Append(" — ").Append(p.Description.Trim());
                }
                sb.AppendLine();
            }
        }
    }
}
