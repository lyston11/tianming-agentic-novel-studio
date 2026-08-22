namespace Tianming.NovelAgent.Domain.Common;

public sealed class DomainRuleException(string code, string message) : InvalidOperationException(message)
{
    public string Code { get; } = code;
}
