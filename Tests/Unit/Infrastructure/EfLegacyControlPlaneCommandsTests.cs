using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Tianming.NovelAgent.Application.Ports;
using Tianming.NovelAgent.Infrastructure;
using Tianming.NovelAgent.Infrastructure.Persistence;
using Xunit;

namespace Tests.Unit.Infrastructure;

public sealed class EfLegacyControlPlaneCommandsTests
{
    [Fact]
    public async Task SubmitGoalAsync_CommitsGoalSnapshotProductionAndBatchAsOneCommand()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var commands = fixture.CreateCommands("production-1", "batch-1");

        var result = await commands.SubmitGoalAsync(Command("{\"start\":1,\"end\":6}"));

        Assert.False(result.Existing);
        Assert.Equal("goal-1", result.GoalId);
        Assert.Equal("goal-1", (await fixture.Db.CreativeGoals.SingleAsync()).Id);
        Assert.Equal("snapshot-1", (await fixture.Db.GoalContextSnapshots.SingleAsync()).Id);
        Assert.Equal("production-1", (await fixture.Db.BookProductions.SingleAsync()).Id);
        var batch = await fixture.Db.ProductionBatches.SingleAsync();
        Assert.Equal("batch-1", batch.Id);
        Assert.Equal("user", batch.AcceptanceActor);
        Assert.Equal((1, 3), (batch.StartChapterNumber, batch.EndChapterNumber));
    }

    [Fact]
    public async Task SubmitGoalAsync_WhenProductionValidationFails_RollsBackEntireCommand()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var commands = fixture.CreateCommands();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            commands.SubmitGoalAsync(Command("{\"start\":4,\"end\":2}")));

        Assert.Empty(await fixture.Db.CreativeGoals.AsNoTracking().ToListAsync());
        Assert.Empty(await fixture.Db.GoalContextSnapshots.AsNoTracking().ToListAsync());
        Assert.Empty(await fixture.Db.BookProductions.AsNoTracking().ToListAsync());
        Assert.Empty(await fixture.Db.ProductionBatches.AsNoTracking().ToListAsync());
        Assert.Empty(fixture.Db.ChangeTracker.Entries());
    }

    private static LegacyGoalSubmissionCommand Command(string targetChapterRangeJson) => new(
        UserId: "user-1",
        ProjectId: "project-1",
        SourceSessionId: "session-1",
        IdempotencyKey: "confirm-1",
        TotalCostLimit: 20,
        GoalType: "write_book",
        CollaborationMode: "coauthor",
        HumanReadableObjective: "完成六章",
        TargetChapterRangeJson: targetChapterRangeJson,
        SuccessCriteriaJson: "[]",
        MustPreserveJson: "[]",
        MustHappenJson: "[]",
        MustNotChangeJson: "[]",
        AcceptancePolicyJson: "{}",
        ReworkPolicyJson: "{}",
        ExecutionStrategy: "interactive_batch",
        BookPlanJson: "{\"batchSize\":3}",
        Baselines: new LegacyFrozenBaselines(
            "canon-1",
            "knowledge-1",
            "quality-1",
            "style-1",
            "{}",
            "{}",
            "{}"),
        GoalId: "goal-1",
        SnapshotId: "snapshot-1");

    private sealed class SqliteFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        public AgentControlDbContext Db { get; }

        private SqliteFixture(SqliteConnection connection, AgentControlDbContext db)
        {
            _connection = connection;
            Db = db;
        }

        public static async Task<SqliteFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AgentControlDbContext>()
                .UseSqlite(connection)
                .Options;
            var db = new AgentControlDbContext(options);
            await db.Database.EnsureCreatedAsync();
            return new SqliteFixture(connection, db);
        }

        public EfLegacyControlPlaneCommands CreateCommands(params string[] generatedIds)
        {
            var clock = new FixedClock(new DateTimeOffset(2026, 8, 20, 6, 0, 0, TimeSpan.Zero));
            var ids = new SequenceIdGenerator(generatedIds);
            var unitOfWork = new EfAgentControlStore(Db, clock, ids, new Sha256ContractHasher());
            return new EfLegacyControlPlaneCommands(Db, unitOfWork, clock, ids, new AgentUserScope());
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }

    private sealed class SequenceIdGenerator(IEnumerable<string> values) : IIdGenerator
    {
        private readonly Queue<string> _values = new(values);

        public string NewId() => _values.Count > 0
            ? _values.Dequeue()
            : throw new InvalidOperationException("The test did not provide enough generated ids.");
    }
}
