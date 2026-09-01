using System.ComponentModel.DataAnnotations;

namespace TM.Web.NovelAgentWeb.Models.Auth;

public class LoginRequest
{
    [Required(ErrorMessage = "Email or username is required")]
    public string EmailOrUsername { get; set; } = null!;

    [Required(ErrorMessage = "Password is required")]
    public string Password { get; set; } = null!;
}
