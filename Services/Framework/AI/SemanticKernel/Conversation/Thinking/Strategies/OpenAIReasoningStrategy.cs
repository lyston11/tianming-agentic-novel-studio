using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.SemanticKernel;
using OpenAI.Chat;

namespace TM.Services.Framework.AI.SemanticKernel.Conversation.Thinking.Strategies
{
    public class OpenAIReasoningStrategy : IThinkingStrategy
    {
        private readonly TagBasedStrategy _tagFallback = new();

        private static readonly string[] _reasoningFieldNames = { "reasoning_content", "thinking_content", "thinking" };

        private static readonly string[] _thinkingDeltaTypes = { "thinking_delta", "thinking" };

        private static bool _reflectionResolved;
        private static FieldInfo? _choicesField;
        private static FieldInfo? _deltaField;
        private static FieldInfo? _deltaRawDataField;
        private static FieldInfo? _deltaPatchField;
        private static MethodInfo? _patchSerializeToJsonMethod;

        public ThinkingRouteResult Extract(StreamingChatMessageContent chunk)
        {
            var reasoning = TryExtractReasoning(chunk, out bool isThinkingDeltaBlock, out var thinkingKind);
            if (isThinkingDeltaBlock && reasoning == null && !string.IsNullOrEmpty(chunk.Content))
            {
                return new ThinkingRouteResult
                {
                    ThinkingContent = chunk.Content,
                    ThinkingKind = thinkingKind ?? "Thinking",
                    AnswerContent = null
                };
            }
            if (reasoning != null)
            {
                if (!string.IsNullOrEmpty(chunk.Content))
                    _tagFallback.Extract(chunk);

                return new ThinkingRouteResult
                {
                    ThinkingContent = string.IsNullOrEmpty(reasoning) ? null : reasoning,
                    ThinkingKind = thinkingKind,
                    AnswerContent = null
                };
            }

            if (chunk.Metadata?.TryGetValue("Thinking", out var thinkingMeta) == true
                && thinkingMeta is string thinkingStr
                && !string.IsNullOrEmpty(thinkingStr))
            {
                return new ThinkingRouteResult
                {
                    ThinkingContent = thinkingStr,
                    ThinkingKind = "Thinking",
                    AnswerContent = null
                };
            }

            return _tagFallback.Extract(chunk);
        }

        public ThinkingRouteResult Flush() => _tagFallback.Flush();

        private static string? TryExtractReasoning(StreamingChatMessageContent chunk, out bool isThinkingDeltaBlock, out string? thinkingKind)
        {
            isThinkingDeltaBlock = false;
            thinkingKind = null;
            if (chunk.InnerContent is not StreamingChatCompletionUpdate update)
                return null;

            try
            {
                return TryExtractViaReflection(update, out isThinkingDeltaBlock, out thinkingKind);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[OpenAIReasoningStrategy] reasoning 提取异常（非致命）: {ex.Message}");
                return null;
            }
        }

        private static string? TryExtractViaReflection(StreamingChatCompletionUpdate update, out bool isThinkingDeltaBlock, out string? thinkingKind)
        {
            isThinkingDeltaBlock = false;
            thinkingKind = null;
            EnsureReflectionResolved(update);

            if (_choicesField == null)
                return null;

            var choices = _choicesField.GetValue(update);
            if (choices == null)
                return null;

            object? firstChoice = null;
            if (choices is System.Collections.IList list && list.Count > 0)
            {
                firstChoice = list[0];
            }
            else if (choices is System.Collections.IEnumerable enumerable)
            {
                foreach (var item in enumerable)
                {
                    firstChoice = item;
                    break;
                }
            }

            if (firstChoice == null)
                return null;

            if (_deltaField == null)
            {
                _deltaField = firstChoice.GetType().GetField("Delta",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    ?? FindFieldByNameContains(firstChoice.GetType(), "delta");
            }

            var delta = _deltaField?.GetValue(firstChoice);
            if (delta == null)
                return null;

            if (_deltaRawDataField == null && _deltaPatchField == null)
            {
                _deltaRawDataField = FindFieldByNameContains(delta.GetType(), "serializedAdditionalRawData")
                    ?? FindFieldByNameContains(delta.GetType(), "additionalRawData")
                    ?? FindFieldByNameContains(delta.GetType(), "rawData");

                if (_deltaRawDataField == null)
                {
                    _deltaPatchField = FindFieldByNameContains(delta.GetType(), "_patch")
                        ?? FindFieldByNameContains(delta.GetType(), "patch");
                }
            }

            var rawData = _deltaRawDataField?.GetValue(delta);
            if (rawData is IDictionary<string, BinaryData> binaryDict)
            {
                if (binaryDict.TryGetValue("type", out var typeBd))
                {
                    var typeStr = ExtractStringFromBinary(typeBd);
                    foreach (var t in _thinkingDeltaTypes)
                    {
                        if (string.Equals(typeStr, t, StringComparison.OrdinalIgnoreCase))
                        {
                            isThinkingDeltaBlock = true;
                            thinkingKind = "Thinking";
                            break;
                        }
                    }
                }

                foreach (var key in _reasoningFieldNames)
                {
                    if (binaryDict.TryGetValue(key, out var binaryValue))
                    {
                        thinkingKind = string.Equals(key, "reasoning_content", StringComparison.OrdinalIgnoreCase)
                            ? "Reasoning"
                            : "Thinking";
                        var jsonStr = binaryValue.ToString();
                        if (jsonStr.Length >= 2 && jsonStr[0] == '"' && jsonStr[^1] == '"')
                        {
                            try
                            {
                                return System.Text.Json.JsonSerializer.Deserialize<string>(jsonStr);
                            }
                            catch
                            {
                                return jsonStr[1..^1];
                            }
                        }
                        return jsonStr;
                    }
                }
            }

            var patchObj = _deltaPatchField?.GetValue(delta);
            if (patchObj != null)
            {
                if (_patchSerializeToJsonMethod == null)
                {
                    _patchSerializeToJsonMethod = patchObj.GetType().GetMethod(
                        "SerializeToJson",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                        Type.DefaultBinder,
                        Type.EmptyTypes,
                        null);
                }

                if (_patchSerializeToJsonMethod?.Invoke(patchObj, null) is string patchJson
                    && !string.IsNullOrEmpty(patchJson))
                {
                    try
                    {
                        using var jdoc = System.Text.Json.JsonDocument.Parse(patchJson);
                        if (jdoc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object)
                        {
                            if (jdoc.RootElement.TryGetProperty("type", out var typeProp)
                                && typeProp.ValueKind == System.Text.Json.JsonValueKind.String)
                            {
                                var typeStr = typeProp.GetString();
                                foreach (var t in _thinkingDeltaTypes)
                                {
                                    if (string.Equals(typeStr, t, StringComparison.OrdinalIgnoreCase))
                                    {
                                        isThinkingDeltaBlock = true;
                                        thinkingKind = "Thinking";
                                        break;
                                    }
                                }
                            }

                            foreach (var key in _reasoningFieldNames)
                            {
                                if (jdoc.RootElement.TryGetProperty(key, out var prop)
                                    && prop.ValueKind == System.Text.Json.JsonValueKind.String)
                                {
                                    var v = prop.GetString();
                                    if (!string.IsNullOrEmpty(v))
                                    {
                                        thinkingKind = string.Equals(key, "reasoning_content", StringComparison.OrdinalIgnoreCase)
                                            ? "Reasoning"
                                            : "Thinking";
                                        return v;
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugLogReflectOnce($"_patch JSON 解析异常（非致命）: {ex.Message}");
                    }
                }
            }

            return null;
        }

        private static int _patchLogOnceFlag;
        private static void DebugLogReflectOnce(string msg)
        {
            if (System.Threading.Interlocked.Exchange(ref _patchLogOnceFlag, 1) == 0)
                TM.App.Log($"[OpenAIReasoningStrategy] {msg}");
        }

        private static void EnsureReflectionResolved(StreamingChatCompletionUpdate update)
        {
            if (_reflectionResolved)
                return;

            _reflectionResolved = true;

            try
            {
                var updateType = update.GetType();
                var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

                _choicesField = updateType.GetField("Choices", flags)
                    ?? FindFieldByNameContains(updateType, "choices");

                TM.App.Log($"[OpenAIReasoningStrategy] 反射解析完成: choices={_choicesField?.Name ?? "null"}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[OpenAIReasoningStrategy] 反射解析失败（将使用 TagBased 兜底）: {ex.Message}");
            }
        }

        private static string? ExtractStringFromBinary(BinaryData binaryData)
        {
            var jsonStr = binaryData.ToString();
            if (string.IsNullOrEmpty(jsonStr)) return null;
            if (jsonStr.Length >= 2 && jsonStr[0] == '"' && jsonStr[^1] == '"')
            {
                try { return System.Text.Json.JsonSerializer.Deserialize<string>(jsonStr); }
                catch { return jsonStr[1..^1]; }
            }
            return jsonStr;
        }

        private static FieldInfo? FindFieldByNameContains(Type type, string namePart)
        {
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            foreach (var field in type.GetFields(flags))
            {
                if (field.Name.Contains(namePart, StringComparison.OrdinalIgnoreCase))
                    return field;
            }
            return null;
        }
    }
}
