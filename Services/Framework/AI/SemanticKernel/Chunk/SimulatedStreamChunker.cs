using System;
using System.Collections.Generic;

namespace TM.Services.Framework.AI.SemanticKernel.Chunk
{
    public static class SimulatedStreamChunker
    {
        public const int DefaultMaxChunkLength = 24;

        private static readonly char[] SentenceEnders = { '。', '！', '？', '.', '!', '?', '\n' };

        public static IEnumerable<IStreamChunk> Slice(
            string? answer,
            string? thinking,
            string? thinkingKind,
            Guid runId,
            int maxChunkLength = DefaultMaxChunkLength,
            bool splitAtPunctuation = true,
            int startSequence = 0)
        {
            if (maxChunkLength <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxChunkLength), "必须为正数");

            int seq = startSequence;

            if (!string.IsNullOrEmpty(thinking))
            {
                foreach (var slice in SliceText(thinking!, maxChunkLength, splitAtPunctuation))
                {
                    yield return new ThinkingDeltaChunk(slice, thinkingKind)
                    {
                        RunId = runId,
                        Sequence = seq++,
                    };
                }
            }

            if (!string.IsNullOrEmpty(answer))
            {
                foreach (var slice in SliceText(answer!, maxChunkLength, splitAtPunctuation))
                {
                    yield return new TextDeltaChunk(slice)
                    {
                        RunId = runId,
                        Sequence = seq++,
                    };
                }
            }
        }

        public static IEnumerable<string> SliceText(
            string text,
            int maxChunkLength = DefaultMaxChunkLength,
            bool splitAtPunctuation = true)
        {
            if (string.IsNullOrEmpty(text))
                yield break;
            if (maxChunkLength <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxChunkLength), "必须为正数");

            int pos = 0;
            int len = text.Length;

            while (pos < len)
            {
                int remaining = len - pos;
                if (remaining <= maxChunkLength)
                {
                    yield return text.Substring(pos);
                    yield break;
                }

                int cut;
                if (splitAtPunctuation)
                {
                    int searchStart = pos;
                    int searchEnd = pos + maxChunkLength;
                    int found = -1;
                    for (int i = searchEnd - 1; i >= searchStart; i--)
                    {
                        if (IsSentenceEnder(text[i]))
                        {
                            found = i;
                            break;
                        }
                    }

                    if (found >= 0)
                    {
                        cut = found + 1;
                    }
                    else
                    {
                        cut = pos + maxChunkLength;
                    }
                }
                else
                {
                    cut = pos + maxChunkLength;
                }

                yield return text.Substring(pos, cut - pos);
                pos = cut;
            }
        }

        private static bool IsSentenceEnder(char c)
        {
            for (int i = 0; i < SentenceEnders.Length; i++)
            {
                if (SentenceEnders[i] == c) return true;
            }
            return false;
        }
    }
}
