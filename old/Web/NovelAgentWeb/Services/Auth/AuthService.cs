using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Models.Auth;
using TM.Web.NovelAgentWeb.Services.Caching;

namespace TM.Web.NovelAgentWeb.Services.Auth;

public class AuthService : IAuthService
{
    private readonly NovelAgentDbContext _context;
    private readonly JwtTokenGenerator _tokenGenerator;
    private readonly IConfiguration _configuration;
    private readonly IMemoryCacheService _cache;
    private readonly ILogger<AuthService> _logger;
    private const int BcryptWorkFactor = 12;
    private static readonly TimeSpan UserSettingsCacheDuration = TimeSpan.FromMinutes(5);

    public AuthService(
        NovelAgentDbContext context,
        JwtTokenGenerator tokenGenerator,
        IConfiguration configuration,
        IMemoryCacheService cache,
        ILogger<AuthService> logger)
    {
        _context = context;
        _tokenGenerator = tokenGenerator;
        _configuration = configuration;
        _cache = cache;
        _logger = logger;
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request)
    {
        // Validate username uniqueness
        if (await _context.Users.AnyAsync(u => u.Username == request.Username))
        {
            throw new InvalidOperationException("Username already exists");
        }

        // Validate email uniqueness
        if (await _context.Users.AnyAsync(u => u.Email == request.Email))
        {
            throw new InvalidOperationException("Email already exists");
        }

        // Hash password with BCrypt (cost factor 12)
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password, BcryptWorkFactor);

        // Create user with transaction
        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            // Create user entity
            var user = new User
            {
                Id = Guid.NewGuid().ToString(),
                Username = request.Username,
                Email = request.Email,
                PasswordHash = passwordHash,
                Role = "author", // Default role
                StorageQuotaMb = 5120, // 5GB default
                ApiCallQuota = 10000,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            if (_context.Database.IsRelational() &&
                _context.Database.GetDbConnection() is NpgsqlConnection)
            {
                await _context.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT set_config('app.current_user_id', {user.Id}, true)");
            }

            // Create default UserSettings
            var userSettings = new UserSettings
            {
                UserId = user.Id,
                LlmProvider = null,
                LlmApiKeyEncrypted = null,
                LlmBaseUrl = null,
                LlmModel = null,
                LlmTemperature = 0.7f,
                LlmMaxTokens = 4096,
                EmbeddingProvider = "local",
                EmbeddingModel = "bge-small-zh-v1.5",
                AgentDefaultRisk = "Medium",
                AgentLoopAutoProceed = true,
                AgentLoopMaxSteps = 12,
                DefaultGenre = "玄幻",
                DefaultChapterWordCount = 3000,
                Theme = "dark",
                Language = "zh-CN"
            };

            _context.UserSettings.Add(userSettings);
            await _context.SaveChangesAsync();

            await transaction.CommitAsync();

            // Generate JWT token
            var (token, expiresAt) = _tokenGenerator.GenerateToken(user);

            return new AuthResponse
            {
                Token = token,
                User = new AuthUser
                {
                    Id = user.Id,
                    Username = user.Username,
                    Email = user.Email,
                    Role = user.Role
                },
                ExpiresAt = expiresAt
            };
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request)
    {
        // Find user by email or username
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Email == request.EmailOrUsername || u.Username == request.EmailOrUsername);

        if (user == null)
        {
            throw new UnauthorizedAccessException("Invalid credentials");
        }

        // Verify password with BCrypt
        if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            throw new UnauthorizedAccessException("Invalid credentials");
        }

        // Check if user is active
        if (!user.IsActive)
        {
            throw new UnauthorizedAccessException("User account is inactive");
        }

        // Update last login timestamp
        user.LastLoginAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        // Generate JWT token
        var (token, expiresAt) = _tokenGenerator.GenerateToken(user);

        return new AuthResponse
        {
            Token = token,
            User = new AuthUser
            {
                Id = user.Id,
                Username = user.Username,
                Email = user.Email,
                Role = user.Role
            },
            ExpiresAt = expiresAt
        };
    }

    public async Task<bool> ValidateTokenAsync(string token)
    {
        try
        {
            var jwtSettings = _configuration.GetSection("JwtSettings");
            var secretKey = jwtSettings["SecretKey"] ?? throw new InvalidOperationException("JWT SecretKey is not configured");
            var issuer = jwtSettings["Issuer"] ?? "NovelAgentWeb";
            var audience = jwtSettings["Audience"] ?? "NovelAgentWeb";

            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes(secretKey);

            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateIssuer = true,
                ValidIssuer = issuer,
                ValidateAudience = true,
                ValidAudience = audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            };

            tokenHandler.ValidateToken(token, validationParameters, out _);
            return await Task.FromResult(true);
        }
        catch
        {
            return false;
        }
    }

    public async Task<UserSettings?> GetUserSettingsAsync(string userId, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"usersettings:{userId}";

        return await _cache.GetOrSetAsync(
            cacheKey,
            async () =>
            {
                var settings = await _context.UserSettings
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken);

                _logger.LogDebug("Loaded UserSettings from database for user {UserId}", userId);
                return settings;
            },
            UserSettingsCacheDuration,
            cancellationToken);
    }

    public async Task UpdateUserSettingsAsync(UserSettings settings, CancellationToken cancellationToken = default)
    {
        var existing = await _context.UserSettings
            .FirstOrDefaultAsync(s => s.UserId == settings.UserId, cancellationToken);

        if (existing == null)
        {
            throw new KeyNotFoundException($"UserSettings for user {settings.UserId} not found");
        }

        // Update settings
        _context.Entry(existing).CurrentValues.SetValues(settings);
        await _context.SaveChangesAsync(cancellationToken);

        // Invalidate cache
        var cacheKey = $"usersettings:{settings.UserId}";
        _cache.Remove(cacheKey);

        _logger.LogInformation("Updated UserSettings for user {UserId} and invalidated cache", settings.UserId);
    }
}
