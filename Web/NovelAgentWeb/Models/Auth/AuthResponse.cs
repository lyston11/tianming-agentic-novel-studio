using System.Text.Json.Serialization;

namespace TM.Web.NovelAgentWeb.Models.Auth;

public class AuthUser
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = null!;

    [JsonPropertyName("username")]
    public string Username { get; set; } = null!;

    [JsonPropertyName("email")]
    public string Email { get; set; } = null!;

    [JsonPropertyName("role")]
    public string Role { get; set; } = null!;
}

public class AuthResponse
{
    [JsonPropertyName("token")]
    public string Token { get; set; } = null!;

    [JsonPropertyName("user")]
    public AuthUser User { get; set; } = null!;

    [JsonPropertyName("expiresAt")]
    public DateTime ExpiresAt { get; set; }
}
