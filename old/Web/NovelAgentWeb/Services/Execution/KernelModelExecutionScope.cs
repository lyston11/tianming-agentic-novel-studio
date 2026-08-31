namespace TM.Web.NovelAgentWeb.Services.Execution;

public sealed record KernelModelExecutionScope(
    string UserId,
    string ProjectId,
    string GoalId,
    string TaskId,
    string KernelName,
    int Attempt);

public interface IKernelModelExecutionScopeAccessor
{
    KernelModelExecutionScope? Current { get; }
    IDisposable Push(KernelModelExecutionScope scope);
}

public sealed class KernelModelExecutionScopeAccessor : IKernelModelExecutionScopeAccessor
{
    private static readonly AsyncLocal<KernelModelExecutionScope?> CurrentValue = new();

    public KernelModelExecutionScope? Current => CurrentValue.Value;

    public IDisposable Push(KernelModelExecutionScope scope)
    {
        var previous = CurrentValue.Value;
        CurrentValue.Value = scope;
        return new PopScope(previous);
    }

    private sealed class PopScope(KernelModelExecutionScope? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;
            CurrentValue.Value = previous;
            _disposed = true;
        }
    }
}
