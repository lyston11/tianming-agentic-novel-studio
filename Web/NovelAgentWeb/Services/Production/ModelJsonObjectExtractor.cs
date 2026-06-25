namespace TM.Web.NovelAgentWeb.Services.Production;

public static class ModelJsonObjectExtractor
{
    public static string ExtractFirstObject(string text, string emptyMessage, string missingObjectMessage)
    {
        return ExtractFirstBalancedJsonValue(
            text,
            emptyMessage,
            missingObjectMessage,
            allowArray: false);
    }

    public static string ExtractFirstValue(string text, string emptyMessage, string missingValueMessage)
    {
        return ExtractFirstBalancedJsonValue(
            text,
            emptyMessage,
            missingValueMessage,
            allowArray: true);
    }

    private static string ExtractFirstBalancedJsonValue(
        string text,
        string emptyMessage,
        string missingValueMessage,
        bool allowArray)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException(emptyMessage);

        for (var candidateStart = 0; candidateStart < text.Length; candidateStart++)
        {
            var first = text[candidateStart];
            if (first != '{' && (!allowArray || first != '['))
                continue;

            var extracted = TryExtractBalancedJsonValue(text, candidateStart);
            if (!string.IsNullOrWhiteSpace(extracted))
                return extracted;
        }

        throw new InvalidOperationException(missingValueMessage);
    }

    private static string TryExtractBalancedJsonValue(string text, int start)
    {
        var stack = new Stack<char>();
        var inString = false;
        var escaped = false;

        for (var index = start; index < text.Length; index++)
        {
            var ch = text[index];

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
                stack.Push('}');
                continue;
            }

            if (ch == '[')
            {
                stack.Push(']');
                continue;
            }

            if (ch is not ('}' or ']'))
                continue;

            if (stack.Count == 0 || stack.Pop() != ch)
                return string.Empty;

            if (stack.Count == 0)
                return text[start..(index + 1)];
        }

        return string.Empty;
    }
}
