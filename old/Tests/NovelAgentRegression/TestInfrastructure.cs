namespace TM.Tests.NovelAgentRegression;

internal sealed class RegressionAssertException : Exception
{
    public RegressionAssertException(string message) : base(message)
    {
    }
}

internal static class Check
{
    public static void True(bool condition, string message)
    {
        if (!condition)
            throw new RegressionAssertException(message);
    }

    public static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new RegressionAssertException($"{message} Expected: {expected}; Actual: {actual}");
    }

    public static void Contains(string expectedFragment, string actual, string message)
    {
        if (actual?.Contains(expectedFragment, StringComparison.OrdinalIgnoreCase) != true)
            throw new RegressionAssertException($"{message} Missing fragment: {expectedFragment}; Actual: {actual}");
    }

    public static void DoesNotContain(string unexpectedFragment, string actual, string message)
    {
        if (actual?.Contains(unexpectedFragment, StringComparison.OrdinalIgnoreCase) == true)
            throw new RegressionAssertException($"{message} Unexpected fragment: {unexpectedFragment}; Actual: {actual}");
    }
}
