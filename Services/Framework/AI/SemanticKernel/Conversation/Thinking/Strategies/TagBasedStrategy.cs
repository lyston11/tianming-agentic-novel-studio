using System;
using System.Text;
using Microsoft.SemanticKernel;

namespace TM.Services.Framework.AI.SemanticKernel.Conversation.Thinking.Strategies
{
    public class TagBasedStrategy : IThinkingStrategy
    {
        private readonly StringBuilder _buffer = new();
        private TagParseState _state = TagParseState.Content;

        private bool _prefixSniffActive = true;
        private const int PrefixSniffMaxLen = 4096;

        private enum TagParseState
        {
            Content,
            InThinking,
            BetweenTags,
            InAnswer,
            Done
        }

        private static readonly string[] ThinkingOpenTags =
        {
            "<seed:think>", "<thinking>", "<reasoning>", "<analysis>", "<thought>", "<think>"
        };
        private static readonly string[] ThinkingCloseTags =
        {
            "</seed:think>", "</thinking>", "</reasoning>", "</analysis>", "</thought>", "</think>"
        };
        private const string AnswerOpenTag = "<answer>";
        private const string AnswerCloseTag = "</answer>";

        private static readonly System.Text.RegularExpressions.Regex NoiseLinePattern = new(
            @"^(?:Thought\s+for\s+[\d\.]+\s*s|Thinking\.{2,})\s*$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

        private string? _activeCloseTag;
        private string? _activeKind;

        public ThinkingRouteResult Extract(StreamingChatMessageContent chunk)
        {
            var text = chunk.Content;
            if (string.IsNullOrEmpty(text))
                return default;

            _buffer.Append(text);
            return ParseBuffer();
        }

        public ThinkingRouteResult Flush()
        {
            if (_buffer.Length == 0)
                return default;

            var remaining = _buffer.ToString();
            _buffer.Clear();

            return _state switch
            {
                TagParseState.InThinking => new ThinkingRouteResult
                {
                    ThinkingContent = remaining,
                    ThinkingKind = _activeKind ?? "Reasoning",
                    AnswerContent = null
                },
                TagParseState.InAnswer or TagParseState.Done => new ThinkingRouteResult
                {
                    ThinkingContent = null,
                    AnswerContent = remaining
                },
                _ => new ThinkingRouteResult
                {
                    ThinkingContent = null,
                    AnswerContent = string.IsNullOrWhiteSpace(remaining) ? null : remaining
                }
            };
        }

        private ThinkingRouteResult ParseBuffer()
        {
            StringBuilder? thinkingOut = null;
            StringBuilder? answerOut = null;
            string? thinkingKindOut = null;

            var text = _buffer.ToString();
            var consumed = 0;

            if (_prefixSniffActive && _state == TagParseState.Content)
            {
                var (orphanClosePos, orphanCloseTag) = FindFirst(text, 0, ThinkingCloseTags);
                var (openPos, _) = FindFirst(text, 0, ThinkingOpenTags);
                var answerOpenPos = IndexOfCI(text, AnswerOpenTag, 0);

                bool orphanCloseHit = orphanClosePos >= 0
                    && (openPos < 0 || orphanClosePos < openPos)
                    && (answerOpenPos < 0 || orphanClosePos < answerOpenPos);

                if (orphanCloseHit)
                {
                    if (orphanClosePos > 0)
                    {
                        var thinkingPart = StripHighConfidenceNoise(text.Substring(0, orphanClosePos));
                        if (!string.IsNullOrEmpty(thinkingPart))
                        {
                            (thinkingOut ??= new()).Append(thinkingPart);
                            thinkingKindOut ??= GetKindForCloseTag(orphanCloseTag);
                        }
                    }
                    consumed = orphanClosePos + orphanCloseTag.Length;
                    _state = TagParseState.BetweenTags;
                    _prefixSniffActive = false;
                }
                else if (openPos >= 0 || answerOpenPos >= 0)
                {
                    _prefixSniffActive = false;
                }
                else if (text.Length >= PrefixSniffMaxLen)
                {
                    _prefixSniffActive = false;
                }
                else
                {
                    return default;
                }
            }

            while (consumed < text.Length)
            {
                switch (_state)
                {
                    case TagParseState.Content:
                        {
                            var (tagPos, tag) = FindFirst(text, consumed, ThinkingOpenTags);
                            var answerPos = IndexOfCI(text, AnswerOpenTag, consumed);

                            if (tagPos >= 0 && (answerPos < 0 || tagPos <= answerPos))
                            {
                                if (tagPos > consumed)
                                {
                                    var before = text.Substring(consumed, tagPos - consumed);
                                    before = StripHighConfidenceNoise(before);
                                    if (!string.IsNullOrEmpty(before))
                                        (answerOut ??= new()).Append(before);
                                }
                                _activeCloseTag = GetMatchingCloseTag(tag);
                                _activeKind = GetKindForOpenTag(tag);
                                consumed = tagPos + tag.Length;
                                _state = TagParseState.InThinking;
                            }
                            else if (answerPos >= 0)
                            {
                                if (answerPos > consumed)
                                {
                                    var before = text.Substring(consumed, answerPos - consumed);
                                    before = StripHighConfidenceNoise(before);
                                    if (!string.IsNullOrEmpty(before))
                                        (answerOut ??= new()).Append(before);
                                }
                                consumed = answerPos + AnswerOpenTag.Length;
                                _state = TagParseState.InAnswer;
                            }
                            else
                            {
                                var tail = text.Substring(consumed);
                                var safeCut = FindSafeCut(tail, ThinkingOpenTags, AnswerOpenTag);
                                if (safeCut < tail.Length)
                                {
                                    if (safeCut > 0)
                                    {
                                        var safe = tail.Substring(0, safeCut);
                                        safe = StripHighConfidenceNoise(safe);
                                        if (!string.IsNullOrEmpty(safe))
                                            (answerOut ??= new()).Append(safe);
                                    }
                                    consumed += safeCut;
                                    goto done;
                                }
                                else
                                {
                                    var output = StripHighConfidenceNoise(tail);
                                    if (!string.IsNullOrEmpty(output))
                                        (answerOut ??= new()).Append(output);
                                    consumed = text.Length;
                                }
                            }
                            break;
                        }

                    case TagParseState.InThinking:
                        {
                            var closeTag = _activeCloseTag ?? "</think>";
                            var end = IndexOfCI(text, closeTag, consumed);
                            if (end >= 0)
                            {
                                if (end > consumed)
                                {
                                    (thinkingOut ??= new()).Append(text, consumed, end - consumed);
                                    thinkingKindOut ??= _activeKind ?? "Reasoning";
                                }
                                consumed = end + closeTag.Length;
                                _state = TagParseState.BetweenTags;
                            }
                            else
                            {
                                var tail = text.Substring(consumed);
                                var safeCut = FindSafeCutSingle(tail, closeTag);
                                if (safeCut < tail.Length)
                                {
                                    if (safeCut > 0)
                                    {
                                        (thinkingOut ??= new()).Append(tail, 0, safeCut);
                                        thinkingKindOut ??= _activeKind ?? "Reasoning";
                                    }
                                    consumed += safeCut;
                                    goto done;
                                }
                                else
                                {
                                    (thinkingOut ??= new()).Append(tail);
                                    thinkingKindOut ??= _activeKind ?? "Reasoning";
                                    consumed = text.Length;
                                }
                            }
                            break;
                        }

                    case TagParseState.BetweenTags:
                        {
                            var answerPos = IndexOfCI(text, AnswerOpenTag, consumed);
                            var (thinkPos, thinkTag) = FindFirst(text, consumed, ThinkingOpenTags);

                            if (answerPos >= 0 && (thinkPos < 0 || answerPos <= thinkPos))
                            {
                                consumed = answerPos + AnswerOpenTag.Length;
                                _state = TagParseState.InAnswer;
                            }
                            else if (thinkPos >= 0)
                            {
                                _activeCloseTag = GetMatchingCloseTag(thinkTag);
                                _activeKind = GetKindForOpenTag(thinkTag);
                                consumed = thinkPos + thinkTag.Length;
                                _state = TagParseState.InThinking;
                            }
                            else
                            {
                                var tail = text.Substring(consumed);
                                var safeCut = FindSafeCut(tail, ThinkingOpenTags, AnswerOpenTag);
                                if (safeCut < tail.Length)
                                {
                                    if (safeCut > 0)
                                    {
                                        var safe = tail.Substring(0, safeCut).TrimStart('\r', '\n');
                                        if (!string.IsNullOrEmpty(safe))
                                            (answerOut ??= new()).Append(safe);
                                    }
                                    consumed += safeCut;
                                    goto done;
                                }
                                else
                                {
                                    var output = tail.TrimStart('\r', '\n');
                                    if (!string.IsNullOrEmpty(output))
                                        (answerOut ??= new()).Append(output);
                                    consumed = text.Length;
                                }
                            }
                            break;
                        }

                    case TagParseState.InAnswer:
                        {
                            var end = IndexOfCI(text, AnswerCloseTag, consumed);
                            if (end >= 0)
                            {
                                if (end > consumed)
                                    (answerOut ??= new()).Append(text, consumed, end - consumed);
                                consumed = end + AnswerCloseTag.Length;
                                _state = TagParseState.Done;
                            }
                            else
                            {
                                var tail = text.Substring(consumed);
                                var safeCut = FindSafeCutSingle(tail, AnswerCloseTag);
                                if (safeCut < tail.Length)
                                {
                                    if (safeCut > 0)
                                        (answerOut ??= new()).Append(tail, 0, safeCut);
                                    consumed += safeCut;
                                    goto done;
                                }
                                else
                                {
                                    (answerOut ??= new()).Append(tail);
                                    consumed = text.Length;
                                }
                            }
                            break;
                        }

                    case TagParseState.Done:
                    default:
                        {
                            if (consumed < text.Length)
                            {
                                (answerOut ??= new()).Append(text, consumed, text.Length - consumed);
                            }
                            consumed = text.Length;
                            goto done;
                        }
                }
            }

        done:
            if (consumed > 0 && consumed <= _buffer.Length)
            {
                _buffer.Remove(0, consumed);
            }

            return new ThinkingRouteResult
            {
                ThinkingContent = thinkingOut?.ToString(),
                ThinkingKind = thinkingKindOut,
                AnswerContent = answerOut?.ToString()
            };
        }

        #region 辅助方法

        private static int IndexOfCI(string source, string value, int startIndex)
        {
            if (startIndex >= source.Length) return -1;
            return source.IndexOf(value, startIndex, StringComparison.OrdinalIgnoreCase);
        }

        private static (int Index, string Tag) FindFirst(string source, int startIndex, string[] tags)
        {
            var bestIdx = -1;
            var bestTag = string.Empty;
            foreach (var tag in tags)
            {
                var idx = IndexOfCI(source, tag, startIndex);
                if (idx >= 0 && (bestIdx < 0 || idx < bestIdx))
                {
                    bestIdx = idx;
                    bestTag = tag;
                }
            }
            return (bestIdx, bestTag);
        }

        private static string GetMatchingCloseTag(string openTag)
        {
            if (string.IsNullOrEmpty(openTag) || openTag.Length < 3 || openTag[0] != '<' || openTag[^1] != '>')
                return "</think>";
            return "</" + openTag.Substring(1);
        }

        private static string GetKindForOpenTag(string openTag)
        {
            if (string.IsNullOrEmpty(openTag)) return "Reasoning";
            return openTag.ToLowerInvariant() switch
            {
                "<analysis>" => "Analysis",
                "<thought>" => "Thought",
                "<reasoning>" => "Reasoning",
                "<seed:think>" => "SeedThink",
                "<thinking>" => "Thinking",
                _ => "Reasoning"
            };
        }

        private static string GetKindForCloseTag(string closeTag)
        {
            if (string.IsNullOrEmpty(closeTag) || !closeTag.StartsWith("</", StringComparison.Ordinal))
                return "Reasoning";
            var open = "<" + closeTag.Substring(2);
            return GetKindForOpenTag(open);
        }

        private static int FindSafeCut(string tail, string[] tags1, string singleTag)
        {
            var lastLt = tail.LastIndexOf('<');
            if (lastLt < 0) return tail.Length;

            var suffix = tail.Substring(lastLt);
            foreach (var tag in tags1)
            {
                if (tag.StartsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    return lastLt;
            }
            if (singleTag.StartsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return lastLt;

            return tail.Length;
        }

        private static int FindSafeCutSingle(string tail, string closeTag)
        {
            var lastLt = tail.LastIndexOf('<');
            if (lastLt < 0) return tail.Length;

            var suffix = tail.Substring(lastLt);
            if (closeTag.StartsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return lastLt;

            return tail.Length;
        }

        private static string StripHighConfidenceNoise(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            var trimmed = text.TrimStart();
            if (trimmed.Length == 0) return text;

            var eol = trimmed.IndexOf('\n');
            var firstLine = eol >= 0 ? trimmed.Substring(0, eol) : trimmed;

            if (NoiseLinePattern.IsMatch(firstLine))
            {
                if (eol < 0) return string.Empty;
                var rest = trimmed.Substring(eol + 1);
                return string.IsNullOrWhiteSpace(rest) ? string.Empty : rest;
            }

            return text;
        }

        #endregion
    }
}
