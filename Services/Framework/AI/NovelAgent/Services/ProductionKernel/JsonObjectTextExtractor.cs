using System;

namespace TM.Services.Framework.AI.NovelAgent.Services.ProductionKernel
{
    internal static class JsonObjectTextExtractor
    {
        public static string ExtractFirstObjectOrEmpty(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;

            var start = raw.IndexOf('{');
            if (start < 0)
                return string.Empty;

            var depth = 0;
            var inString = false;
            var escaped = false;

            for (var index = start; index < raw.Length; index++)
            {
                var ch = raw[index];

                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                        continue;
                    }

                    if (ch == '\\')
                    {
                        escaped = true;
                        continue;
                    }

                    if (ch == '"')
                        inString = false;

                    continue;
                }

                if (ch == '"')
                {
                    inString = true;
                    continue;
                }

                if (ch == '{')
                {
                    depth++;
                    continue;
                }

                if (ch != '}')
                    continue;

                depth--;
                if (depth == 0)
                    return raw[start..(index + 1)];
            }

            return string.Empty;
        }
    }
}
