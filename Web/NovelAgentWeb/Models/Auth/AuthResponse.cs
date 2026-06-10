namespace TM.Web.NovelAgentWeb.Models.Auth;

public class AuthUser
{
    public string Id { get; set; } = null!;
    public string Username { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string Role { get; set; } = null!;
}

public class AuthResponse
{
    public string Token { get; set; } = null!;
    public AuthUser User { get; set; } = null!;
    public DateTime ExpiresAt { get; set; }
}
