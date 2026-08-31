using System.Text;
using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.DTOs;
using Tianming.NovelAgent.Infrastructure.Persistence;
using TM.Web.NovelAgentWeb.Services.AgentRuntime;
using TM.Web.NovelAgentWeb.Services.Caching;
using TM.Web.NovelAgentWeb.Services.Production;
using TM.Web.NovelAgentWeb.Services.Rework;
using TM.Web.NovelAgentWeb.Services.Settings;
using TM.Web.NovelAgentWeb.Support;
using TM.Web.NovelAgentWeb.Services.VectorStore;

namespace TM.Tests.NovelAgentRegression.E2E;

/// <summary>
/// Custom WebApplicationFactory for E2E tests.
/// Configures test environment with in-memory SQLite database and test services.
/// </summary>
public class TestWebApplicationFactory : WebApplicationFactory<TM.Web.NovelAgentWeb.Program>
{
    private const string TestJwtSecretKey = "test-secret-key-with-at-least-32-characters-for-security";
    private SqliteConnection? _connection;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:SecretKey"] = TestJwtSecretKey,
                ["JwtSettings:Issuer"] = "NovelAgentWeb",
                ["JwtSettings:Audience"] = "NovelAgentWeb",
                ["JwtSettings:ExpiryDays"] = "7"
            });
        });

        builder.ConfigureServices(services =>
        {
            // Replace both the concrete PostgreSQL context and its NovelAgentDbContext alias.
            // The previous fixture only removed DbContextOptions<NovelAgentDbContext>,
            // while production registers DbContextOptions<PostgresNovelAgentDbContext>;
            // that silently connected E2E tests to the configured PostgreSQL instance.
            services.RemoveAll<DbContextOptions<PostgresNovelAgentDbContext>>();
            services.RemoveAll<PostgresNovelAgentDbContext>();
            services.RemoveAll<DbContextOptions<NovelAgentDbContext>>();
            services.RemoveAll<NovelAgentDbContext>();

            // Create in-memory SQLite connection that persists for the lifetime of the factory
            _connection = new SqliteConnection("Data Source=:memory:");
            _connection.Open();

            // Add test DbContext with in-memory SQLite
            services.AddDbContext<NovelAgentDbContext>(options =>
            {
                options.UseSqlite(_connection);
            });

            // The control-plane context must share the same test database as the
            // legacy context: production registers it against PostgreSQL, and
            // without this replacement every ILegacyControlPlaneCommands call
            // (e.g. chapter-acceptance artifacts) tries a live connection.
            // EF Core 8+ keeps provider configuration as IDbContextOptionsConfiguration
            // singletons that plain options removal does not clear.
            services.RemoveAll<DbContextOptions<AgentControlDbContext>>();
            services.RemoveAll<AgentControlDbContext>();
            services.RemoveAll(typeof(Microsoft.EntityFrameworkCore.Infrastructure.IDbContextOptionsConfiguration<AgentControlDbContext>));
            services.AddDbContext<AgentControlDbContext>((serviceProvider, options) =>
            {
                options.UseSqlite(_connection);
                options.AddInterceptors(serviceProvider.GetRequiredService<AgentUserScopeConnectionInterceptor>());
            });

            services.RemoveAll<IVectorStore>();
            services.AddSingleton<IVectorStore, NoopVectorStore>();
            services.RemoveAll<IDistributedCacheService>();
            services.AddSingleton<IDistributedCacheService, NoopDistributedCacheService>();
            services.RemoveAll<IDistributedLockService>();
            services.AddSingleton<IDistributedLockService, InMemoryDistributedLockService>();
            services.RemoveAll<IAgentRuntimeEventFanout>();
            services.AddSingleton<IAgentRuntimeEventFanout, NoopAgentRuntimeEventFanout>();
            services.RemoveAll<IAgentRuntimeEventStreamConsumer>();
            services.AddSingleton<IAgentRuntimeEventStreamConsumer, NoopAgentRuntimeEventStreamConsumer>();
            services.RemoveAll<ILlmConnectionHealthService>();
            services.AddSingleton<ILlmConnectionHealthService, E2ELlmConnectionHealthService>();
            services.RemoveAll<IWritingModelCompletionService>();
            services.AddScoped<IWritingModelCompletionService, E2EWritingModelCompletionService>();
            services.RemoveAll<IReworkIntentModelClient>();
            services.AddScoped<IReworkIntentModelClient, E2EReworkIntentModelClient>();
            services.RemoveAll<IHostedService>();

            // Override JWT configuration for tests
            services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestJwtSecretKey)),
                    ValidateIssuer = true,
                    ValidIssuer = "NovelAgentWeb",
                    ValidateAudience = true,
                    ValidAudience = "NovelAgentWeb",
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                };
            });

            // Program.cs owns schema migration during test host startup.
            // Pre-creating the in-memory SQLite schema here makes Migrate()
            // attempt to create tables that already exist.
        });

        // Use test environment
        builder.UseEnvironment("Testing");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _connection?.Close();
            _connection?.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal sealed class E2ELlmConnectionHealthService : ILlmConnectionHealthService
{
    public Task<LlmConnectionHealthResult> CheckAsync(
        LlmConnectionHealthInput input,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(LlmConnectionHealthResult.Ready(input.Normalize(), 200));
}

internal sealed class E2EWritingModelCompletionService : IWritingModelCompletionService
{
    private const string EmptyChangesJson = """
    {"CharacterStateChanges":[],"ConflictProgress":[],"NewPlotPoints":[],"ForeshadowingActions":[],"LocationStateChanges":[],"FactionStateChanges":[],"TimeProgression":null,"CharacterMovements":[],"ItemTransfers":[],"SecretRevealChanges":[],"PledgeConstraintChanges":[],"DeadlineConstraintChanges":[]}
    """;

    public Task<string> CompleteAsync(
        string userId,
        string system,
        string user,
        CancellationToken ct = default)
    {
        var reviewRewriteScenario = E2EWritingModelScenarios.RequiresAgentReviewRewrite(userId);
        E2EWritingModelScenarios.RecordCall(userId, system, user, reviewRewriteScenario);
        if (system.Contains("总编验收模型", StringComparison.Ordinal))
        {
            if (reviewRewriteScenario &&
                !user.Contains("按评审补足战斗反馈", StringComparison.Ordinal))
            {
                return Task.FromResult("""
                {"meetsUserIntent":false,"meetsRevisionPlan":true,"meetsProjectPromise":false,"creativeFit":"low","decision":"rewrite","problems":["战斗反馈不足，章节没有形成清晰的打怪升级收益。"],"suggestions":["按评审补足战斗反馈","保留沈砚、银蓝邮徽和废城邮局设定","补出代价后果和章末钩子"],"evidence":["正文只有逃生与发现，缺少胜负反馈和升级感。"]}
                """);
            }

            return Task.FromResult("""
            {"meetsUserIntent":true,"meetsRevisionPlan":true,"meetsProjectPromise":true,"creativeFit":"high","decision":"pass","problems":[],"suggestions":[],"evidence":["章节围绕废土幸存者、旧邮徽和邮路异常展开。"]}
            """);
        }

        if (system.Contains("长篇小说事实沉淀模型", StringComparison.Ordinal))
        {
            if (user.Contains("chapter-002", StringComparison.OrdinalIgnoreCase) ||
                user.Contains("第二章", StringComparison.Ordinal))
            {
                return Task.FromResult("""
                {
                  "chapterId": "chapter-002",
                  "chapterTitle": "第二章 分拣台的回声",
                  "protagonistName": "沈砚",
                  "protagonistIdentity": "废土幸存者",
                  "protagonistStatus": "承接分拣台第二次敲击声后，确认银蓝邮徽只能指引旧邮路不能主动攻击",
                  "currentLocation": "废城邮局分拣区",
                  "systemState": "银蓝邮徽仍只能指路和识别邮路裂隙，不能主动攻击",
                  "equipmentState": "银蓝邮徽",
                  "keyEvents": ["沈砚追查分拣台第二次敲击声", "怪物围堵废城邮局", "银蓝邮徽指出旧邮路缝隙帮助沈砚脱身"],
                  "endingState": "沈砚带着新的投递线索进入盐鸦驿站方向",
                  "nextChapterMustCarry": ["第三章必须承接盐鸦驿站方向", "银蓝邮徽仍不能主动攻击"]
                }
                """);
            }

            return Task.FromResult("""
            {
              "chapterId": "chapter-001",
              "chapterTitle": "第一章 旧邮路的蓝光",
              "protagonistName": "沈砚",
              "protagonistIdentity": "废土幸存者",
              "protagonistStatus": "刚用银蓝邮徽从巡游怪物追击中脱身",
              "currentLocation": "废城邮局",
              "systemState": "银蓝邮徽已显现旧邮路指引能力，但不能主动攻击",
              "equipmentState": "银蓝邮徽",
              "keyEvents": ["沈砚在废城邮局醒来", "巡游怪物逼近", "银蓝邮徽指示旧邮路逃生方向"],
              "endingState": "废弃分拣台传出第二次敲击声，像有人等待投递",
              "nextChapterMustCarry": ["第二章必须承接分拣台第二次敲击声", "银蓝邮徽不能主动攻击，只能指示旧邮路"]
            }
            """);
        }

        if (user.Contains("chapter-002", StringComparison.OrdinalIgnoreCase) ||
            user.Contains("第二章", StringComparison.Ordinal))
        {
            var hasContinuityPack = user.Contains("上一章主角", StringComparison.Ordinal) &&
                                    user.Contains("沈砚", StringComparison.Ordinal) &&
                                    user.Contains("下一章必须承接", StringComparison.Ordinal) &&
                                    user.Contains("分拣台第二次敲击声", StringComparison.Ordinal) &&
                                    user.Contains("银蓝邮徽不能主动攻击", StringComparison.Ordinal);

            if (!hasContinuityPack)
            {
                return Task.FromResult($"""
                第二章 荒原枪火

                赵临在荒原列车上醒来，手里的黑铁枪发出灼热光芒。他没有见过什么废城邮局，也不知道分拣台响过几次。

                <chapter_changes>
                {EmptyChangesJson}
                </chapter_changes>
                """);
            }

            return Task.FromResult($"""
            第二章 分拣台的回声

            分拣台第二次敲击声落下时，沈砚没有立刻冲过去。他还记得上一刻银蓝邮徽只给过方向，从没有替他撕开怪物的喉咙；这枚邮徽不能主动攻击，只能在旧邮路残存的缝隙里指出一条活路。

            废城邮局外，巡游怪物被敲击声引回，碎铁链拖过门槛，像要把整座分拣区拖进灰尘。沈砚压低呼吸，把银蓝邮徽贴近投递口，蓝光没有刺向怪物，只沿着地面绕开血锈，照出一排几乎被掩埋的旧邮袋编号。

            他顺着编号推开卡死的分拣柜，柜后露出窄得只能侧身通过的邮路暗槽。怪物的爪子砸碎柜门时，沈砚已经钻入暗槽深处，背后仍能听见那张分拣台一下一下回敲，像在确认他终于接下了第二次投递。

            等他从暗槽另一端爬出，远处盐霜覆盖的门牌在蓝光里浮现出“盐鸦驿站”四个字。沈砚握紧银蓝邮徽，明白自己没有得到一件武器，而是被迫接进了一条会把活人送向未知代价的旧邮路。

            <chapter_changes>
            {EmptyChangesJson}
            </chapter_changes>
            """);
        }

        var knowledgeScene = user.Contains("盐鸦驿站", StringComparison.Ordinal) &&
                             user.Contains("逆风邮旗", StringComparison.Ordinal)
            ? "蓝光在巷口折向北面，照出一块被盐霜啃白的门牌：盐鸦驿站。沈砚没有把邮徽当成武器，只按知识库硬事实里记下的旧规矩低头穿过门廊。下一瞬，屋顶那面逆风邮旗无声升起，证明这条蓝光邮路被正确承接。"
            : string.Empty;

        if (reviewRewriteScenario &&
            user.Contains("AgentReview修订要求", StringComparison.Ordinal))
        {
            return Task.FromResult($"""
            第一章 旧邮路的蓝光

            沈砚在废城邮局的塌墙下醒来时，巡游怪物已经把铁皮招牌撞得翻卷。他没有把银蓝邮徽当成武器，只借它照出的旧邮路轨迹绕进投递窗口后的窄巷。

            怪物追到巷口时，沈砚按评审补足战斗反馈：他先诱使怪物撞碎废弃邮柜，再借倒塌的分拣架卡住怪物的前肢，最后用邮徽蓝光确认逃生路线，完成一次明确的生存反击。胜利不是力量暴涨，而是他第一次掌握了旧邮路的使用边界。

            代价也随之出现。银蓝邮徽没有主动攻击，却抽走沈砚指尖的温度，令他短暂失去右手知觉；他明白每一次借路都要付出身体代价。章末，废弃分拣台里传出第二次敲击声，像有人在黑暗里等待下一次投递。

            <chapter_changes>
            {EmptyChangesJson}
            </chapter_changes>
            """);
        }

        if (user.Contains("RevisionPlanRebuildE2E", StringComparison.Ordinal) ||
            user.Contains("按已采纳修订计划重建第一章", StringComparison.Ordinal))
        {
            return Task.FromResult($"""
            第一章 旧邮路的蓝光

            RevisionPlanRebuildE2E 按修订计划改写。沈砚在废城邮局醒来时，旧版里只是钻进窄巷逃生；这一次，巡游怪物直接撞塌投递窗口，把半座分拣区压成铁灰。

            他没有把银蓝邮徽当成攻击武器，而是借蓝光看见旧邮路夹缝的位置，诱使怪物撞进废弃邮柜。柜架塌落的一刻，沈砚拖着半截撬棍撑住门轴，让怪物的前肢被卡在锈铁之间，完成一次明确的生存反击。

            胜利带来的不是力量暴涨，而是边界确认：银蓝邮徽只能指路，不能替他杀敌；每一次借路都会抽走指尖温度。章末，废弃分拣台里传出第二次敲击声，像有人在黑暗里等待投递。

            <chapter_changes>
            {EmptyChangesJson}
            </chapter_changes>
            """);
        }

        if (reviewRewriteScenario)
        {
            return Task.FromResult($"""
            第一章 旧邮路的蓝光

            AgentReviewRewriteE2E 初稿标记。沈砚在废城邮局的塌墙下醒来时，巡游怪物拖着碎铁链经过街口。他攥紧银蓝邮徽，借蓝光找到投递窗口后的窄巷，避开怪物的嗅探。

            他确认旧邮路仍然存在，也确认邮徽不能主动攻击。可这一版正文只写了逃生和发现，没有补出清晰的胜负反馈、代价后果和升级感。

            章末，废弃分拣台里传出第二次敲击声，像有人在黑暗里等待投递。

            <chapter_changes>
            {EmptyChangesJson}
            </chapter_changes>
            """);
        }

        return Task.FromResult($"""
        第一章 旧邮路的蓝光

        沈砚在废城邮局的塌墙下醒来时，风正从铁皮招牌的裂缝里灌进来。远处的巡游怪物拖着碎铁链经过街口，每一步都把积灰震成灰白色的浪。

        他攥紧掌心那枚银蓝邮徽。邮徽没有攻击任何东西，也没有赐给他新的能力，只在怪物转身时泛起极淡的蓝光，像是在辨认某条被尘封多年的旧邮路。

        沈砚顺着蓝光指向的投递窗口钻进后巷，避开巡游怪物的嗅探，第一次确认这座废城里还残留着一条能救命的邮路。他没有变强，却知道自己必须活着把下一封不存在的信送出去。

        {knowledgeScene}

        章末，废弃分拣台里传出第二次敲击声，像有人在黑暗里等待投递。

        <chapter_changes>
        {EmptyChangesJson}
        </chapter_changes>
        """);
    }
}

internal sealed class E2EReworkIntentModelClient : IReworkIntentModelClient
{
    public Task<ReworkIntentDraft> CompileAsync(
        ReworkIntentCompilationContext context,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new ReworkIntentDraft(
            context.SelectionStart.HasValue ? "selection" : "chapter",
            "动机不清",
            "强化行动因果和读者理解",
            ["人物身份", "章节结局"],
            ["行动过程", "局部表达"],
            ["与问题无关的正文", "人工保护版本"],
            ["行动动机有正文证据", "不破坏保留项"],
            "copy",
            "只影响当前候选章节的局部表达。"));
}

internal static class E2EWritingModelScenarios
{
    private static readonly ConcurrentDictionary<string, byte> AgentReviewRewriteUsers = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentQueue<string> RecentCalls = new();

    public static void MarkAgentReviewRewriteUser(string userId)
    {
        if (!string.IsNullOrWhiteSpace(userId))
            AgentReviewRewriteUsers[userId] = 1;
    }

    public static bool RequiresAgentReviewRewrite(string userId) =>
        !string.IsNullOrWhiteSpace(userId) && AgentReviewRewriteUsers.ContainsKey(userId);

    public static void RecordCall(string userId, string system, string user, bool matchedScenario)
    {
        var kind = system.Contains("总编验收模型", StringComparison.Ordinal)
            ? "review"
            : system.Contains("长篇小说事实沉淀模型", StringComparison.Ordinal)
                ? "fact"
                : "write";
        RecentCalls.Enqueue($"{kind}: userId={userId}; matched={matchedScenario}; hasFeedback={user.Contains("AgentReview修订要求", StringComparison.Ordinal)}; hasFixed={user.Contains("按评审补足战斗反馈", StringComparison.Ordinal)}; hasMarker={user.Contains("AgentReviewRewriteE2E", StringComparison.Ordinal)}");
        while (RecentCalls.Count > 20 && RecentCalls.TryDequeue(out _))
        {
        }
    }

    public static string DumpRecentCalls() => string.Join(" | ", RecentCalls);
}

internal sealed class NoopVectorStore : IVectorStore
{
    public Task InitializeUserCollectionAsync(string userId, CancellationToken ct = default) => Task.CompletedTask;
    public Task UpsertVectorsAsync(string userId, List<VectorData> vectors, CancellationToken ct = default) => Task.CompletedTask;
    public Task<List<SearchResult>> SearchSimilarAsync(
        string userId,
        float[] queryVector,
        int topK = 10,
        Dictionary<string, object>? filters = null,
        CancellationToken ct = default) =>
        Task.FromResult(new List<SearchResult>());

    public Task DeleteUserCollectionAsync(string userId, CancellationToken ct = default) => Task.CompletedTask;
    public Task<bool> CollectionExistsAsync(string userId, CancellationToken ct = default) => Task.FromResult(true);
    public Task<CollectionInfo?> GetCollectionInfoAsync(string userId, CancellationToken ct = default) => Task.FromResult<CollectionInfo?>(null);
    public Task DeleteVectorsByFilterAsync(string userId, Dictionary<string, object> filters, CancellationToken ct = default) => Task.CompletedTask;
}

internal sealed class NoopDistributedCacheService : IDistributedCacheService
{
    public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) where T : class => Task.FromResult<T?>(null);
    public Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken ct = default) where T : class => Task.CompletedTask;
    public Task RemoveAsync(string key, CancellationToken ct = default) => Task.CompletedTask;
    public Task RemoveByPrefixAsync(string keyPrefix, CancellationToken ct = default) => Task.CompletedTask;
    public Task<bool> ExistsAsync(string key, CancellationToken ct = default) => Task.FromResult(false);
}

internal sealed class InMemoryDistributedLockService : IDistributedLockService
{
    public Task<DistributedLockLease?> TryAcquireAsync(
        string key,
        TimeSpan ttl,
        string owner,
        CancellationToken ct = default)
    {
        var normalizedTtl = ttl <= TimeSpan.Zero ? TimeSpan.FromMinutes(5) : ttl;
        var now = DateTime.UtcNow;
        return Task.FromResult<DistributedLockLease?>(new DistributedLockLease(
            key,
            Guid.NewGuid().ToString("N"),
            owner,
            now,
            now.Add(normalizedTtl)));
    }

    public Task<DistributedLockLease?> ExtendAsync(
        DistributedLockLease lease,
        TimeSpan ttl,
        CancellationToken ct = default)
    {
        var normalizedTtl = ttl <= TimeSpan.Zero ? TimeSpan.FromMinutes(5) : ttl;
        return Task.FromResult<DistributedLockLease?>(lease with { ExpiresAt = DateTime.UtcNow.Add(normalizedTtl) });
    }

    public Task ReleaseAsync(DistributedLockLease lease, CancellationToken ct = default) => Task.CompletedTask;
}

internal sealed class NoopAgentRuntimeEventFanout : IAgentRuntimeEventFanout
{
    public Task PublishAsync(string userId, string sessionId, AgentSseEvent evt, CancellationToken ct = default) => Task.CompletedTask;

    public Task<IReadOnlyList<AgentSseEvent>> ReplayAsync(
        string userId,
        string sessionId,
        string? afterEventId = null,
        int limit = 100,
        CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<AgentSseEvent>>(Array.Empty<AgentSseEvent>());
}

internal sealed class NoopAgentRuntimeEventStreamConsumer : IAgentRuntimeEventStreamConsumer
{
    public Task EnsureConsumerGroupAsync(string userId, string sessionId, string groupName, CancellationToken ct = default) => Task.CompletedTask;

    public Task<IReadOnlyList<AgentRuntimeStreamEvent>> ReadGroupAsync(
        string userId,
        string sessionId,
        string groupName,
        string consumerName,
        int count = 20,
        CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<AgentRuntimeStreamEvent>>(Array.Empty<AgentRuntimeStreamEvent>());

    public Task<IReadOnlyList<AgentRuntimeStreamEvent>> ClaimPendingAsync(
        string userId,
        string sessionId,
        string groupName,
        string consumerName,
        TimeSpan minIdleTime,
        int count = 20,
        CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<AgentRuntimeStreamEvent>>(Array.Empty<AgentRuntimeStreamEvent>());

    public Task<bool> AcknowledgeAsync(
        string userId,
        string sessionId,
        string groupName,
        string streamId,
        CancellationToken ct = default) =>
        Task.FromResult(true);
}
