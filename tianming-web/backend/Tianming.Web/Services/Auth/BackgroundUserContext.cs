namespace TM.Web.NovelAgentWeb.Services.Auth;

public interface IBackgroundUserContext
{
    IDisposable Push(string userId, string username = "background-agent", string email = "", string role = "author");
    BackgroundUserSnapshot? Current { get; }
}

public sealed record BackgroundUserSnapshot(string UserId, string Username, string Email, string Role);

public sealed class BackgroundUserContext : IBackgroundUserContext
{
    private static readonly AsyncLocal<BackgroundUserSnapshot?> CurrentValue = new();

    public BackgroundUserSnapshot? Current => CurrentValue.Value;

    public IDisposable Push(string userId, string username = "background-agent", string email = "", string role = "author")
    {
        var previous = CurrentValue.Value;
        CurrentValue.Value = new BackgroundUserSnapshot(userId, username, email, role);
        return new PopWhenDisposed(previous);
    }

    private sealed class PopWhenDisposed : IDisposable
    {
        private readonly BackgroundUserSnapshot? _previous;
        private bool _disposed;

        public PopWhenDisposed(BackgroundUserSnapshot? previous)
        {
            _previous = previous;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            CurrentValue.Value = _previous;
            _disposed = true;
        }
    }
}
