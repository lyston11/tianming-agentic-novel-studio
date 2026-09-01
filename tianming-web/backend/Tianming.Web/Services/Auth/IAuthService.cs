using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Models.Auth;

namespace TM.Web.NovelAgentWeb.Services.Auth;

public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(RegisterRequest request);
    Task<AuthResponse> LoginAsync(LoginRequest request);
    Task<bool> ValidateTokenAsync(string token);
    Task<UserSettings?> GetUserSettingsAsync(string userId, CancellationToken cancellationToken = default);
    Task UpdateUserSettingsAsync(UserSettings settings, CancellationToken cancellationToken = default);
}
