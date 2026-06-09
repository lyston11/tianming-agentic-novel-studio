using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TM.Web.NovelAgentWeb.Models.Auth;
using TM.Web.NovelAgentWeb.Services.Auth;

namespace TM.Web.NovelAgentWeb.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        IAuthService authService,
        ICurrentUserService currentUserService,
        ILogger<AuthController> logger)
    {
        _authService = authService;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register([FromBody] RegisterRequest request)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var response = await _authService.RegisterAsync(request);
            _logger.LogInformation("User {Username} registered successfully", request.Username);

            return CreatedAtAction(nameof(Register), new { id = response.UserId }, response);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("Registration failed for {Username}: {Message}", request.Username, ex.Message);
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during registration for {Username}", request.Username);
            return StatusCode(500, new { message = "An error occurred during registration" });
        }
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login([FromBody] LoginRequest request)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var response = await _authService.LoginAsync(request);
            _logger.LogInformation("User {EmailOrUsername} logged in successfully", request.EmailOrUsername);

            return Ok(response);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning("Login failed for {EmailOrUsername}: {Message}", request.EmailOrUsername, ex.Message);
            return Unauthorized(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during login for {EmailOrUsername}", request.EmailOrUsername);
            return StatusCode(500, new { message = "An error occurred during login" });
        }
    }

    [HttpPost("validate")]
    public async Task<ActionResult<bool>> ValidateToken([FromBody] string token)
    {
        try
        {
            var isValid = await _authService.ValidateTokenAsync(token);
            return Ok(new { valid = isValid });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating token");
            return StatusCode(500, new { message = "An error occurred during token validation" });
        }
    }

    [HttpGet("me")]
    [Authorize]
    public IActionResult GetCurrentUser()
    {
        var userId = _currentUserService.GetUserId();
        var username = _currentUserService.GetUsername();
        var email = _currentUserService.GetEmail();
        var role = _currentUserService.GetRole();
        var isAdmin = _currentUserService.IsAdmin();

        return Ok(new
        {
            userId,
            username,
            email,
            role,
            isAdmin
        });
    }
}
