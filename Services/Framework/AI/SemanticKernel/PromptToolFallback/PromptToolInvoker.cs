using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;

namespace TM.Services.Framework.AI.SemanticKernel.PromptToolFallback
{
    public sealed class PromptToolInvoker
    {
        public async Task<PromptToolInvocationResult> InvokeAsync(
            Kernel kernel,
            PromptToolCall call,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(kernel);
            ArgumentNullException.ThrowIfNull(call);

            var function = ResolveFunction(kernel, call.ToolName);
            if (function == null)
            {
                return new PromptToolInvocationResult(
                    Succeeded: false,
                    Result: null,
                    ErrorMessage: $"工具不存在: {call.ToolName}",
                    PluginName: ExtractPluginName(call.ToolName),
                    FunctionName: ExtractFunctionName(call.ToolName));
            }

            KernelArguments args;
            try
            {
                args = ParseArguments(call.ArgumentsJson, function.Metadata);
            }
            catch (Exception ex)
            {
                return new PromptToolInvocationResult(
                    Succeeded: false,
                    Result: null,
                    ErrorMessage: $"参数解析失败: {ex.Message}",
                    PluginName: function.PluginName ?? string.Empty,
                    FunctionName: function.Name);
            }

            try
            {
                var result = await function.InvokeAsync(kernel, args, ct).ConfigureAwait(false);
                var text = result.GetValue<string>() ?? result.ToString() ?? string.Empty;

                if (IsGatingRejection(text))
                {
                    return new PromptToolInvocationResult(
                        Succeeded: false,
                        Result: null,
                        ErrorMessage: text.Trim(),
                        PluginName: function.PluginName ?? string.Empty,
                        FunctionName: function.Name);
                }

                return new PromptToolInvocationResult(
                    Succeeded: true,
                    Result: text,
                    ErrorMessage: null,
                    PluginName: function.PluginName ?? string.Empty,
                    FunctionName: function.Name);
            }
            catch (Exception ex)
            {
                return new PromptToolInvocationResult(
                    Succeeded: false,
                    Result: null,
                    ErrorMessage: ex.Message,
                    PluginName: function.PluginName ?? string.Empty,
                    FunctionName: function.Name);
            }
        }

        private static bool IsGatingRejection(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            var t = text.TrimStart();
            return t.StartsWith("[工具调用已暂停", StringComparison.Ordinal)
                || t.StartsWith("[Edit模式禁止", StringComparison.Ordinal)
                || t.StartsWith("[总通道禁止", StringComparison.Ordinal)
                || t.StartsWith("[用户取消了", StringComparison.Ordinal);
        }

        private static KernelFunction? ResolveFunction(Kernel kernel, string toolName)
        {
            if (string.IsNullOrWhiteSpace(toolName)) return null;

            var pluginName = ExtractPluginName(toolName);
            var functionName = ExtractFunctionName(toolName);

            if (!string.IsNullOrEmpty(pluginName))
            {
                if (kernel.Plugins.TryGetPlugin(pluginName, out var plugin)
                    && plugin.TryGetFunction(functionName, out var fn))
                {
                    return fn;
                }
            }

            foreach (var plugin in kernel.Plugins)
            {
                if (plugin.TryGetFunction(functionName, out var fn))
                {
                    return fn;
                }
            }

            return null;
        }

        private static string ExtractPluginName(string toolName)
        {
            var dot = toolName.IndexOf('.');
            return dot > 0 ? toolName.Substring(0, dot) : string.Empty;
        }

        private static string ExtractFunctionName(string toolName)
        {
            var dot = toolName.IndexOf('.');
            return dot >= 0 ? toolName.Substring(dot + 1) : toolName;
        }

        public static KernelArguments ParseArguments(string? argumentsJson, KernelFunctionMetadata metadata)
        {
            var args = new KernelArguments();
            if (string.IsNullOrWhiteSpace(argumentsJson)) return args;

            using var doc = JsonDocument.Parse(argumentsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return args;

            var paramByName = metadata.Parameters?.ToDictionary(
                p => p.Name,
                p => p,
                StringComparer.OrdinalIgnoreCase) ?? new Dictionary<string, KernelParameterMetadata>();

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (paramByName.TryGetValue(prop.Name, out var paramMeta))
                {
                    args[paramMeta.Name] = ConvertJsonValue(prop.Value, paramMeta.ParameterType);
                }
                else
                {
                    args[prop.Name] = prop.Value.ValueKind switch
                    {
                        JsonValueKind.String => prop.Value.GetString(),
                        JsonValueKind.Number => prop.Value.TryGetInt64(out var l) ? (object)l : prop.Value.GetDouble(),
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        JsonValueKind.Null => null!,
                        _ => prop.Value.GetRawText(),
                    };
                }
            }

            return args;
        }

        private static object? ConvertJsonValue(JsonElement value, Type? targetType)
        {
            if (targetType == null || targetType == typeof(object) || targetType == typeof(string))
            {
                return value.ValueKind switch
                {
                    JsonValueKind.String => value.GetString(),
                    JsonValueKind.Number => value.GetRawText(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    JsonValueKind.Null => null,
                    _ => value.GetRawText(),
                };
            }

            if (targetType == typeof(int) || targetType == typeof(int?))
                return value.TryGetInt32(out var i) ? i : (object?)null;
            if (targetType == typeof(long) || targetType == typeof(long?))
                return value.TryGetInt64(out var l) ? l : (object?)null;
            if (targetType == typeof(double) || targetType == typeof(double?))
                return value.TryGetDouble(out var d) ? d : (object?)null;
            if (targetType == typeof(bool) || targetType == typeof(bool?))
                return value.ValueKind == JsonValueKind.True ? true
                     : value.ValueKind == JsonValueKind.False ? false
                     : (object?)null;
            if (targetType.IsEnum)
            {
                if (value.ValueKind == JsonValueKind.String && Enum.TryParse(targetType, value.GetString(), true, out var enumVal))
                    return enumVal;
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize(value.GetRawText(), targetType);
            }
            catch
            {
                return value.GetRawText();
            }
        }
    }

    public sealed record PromptToolInvocationResult(
        bool Succeeded,
        string? Result,
        string? ErrorMessage,
        string PluginName,
        string FunctionName);
}
