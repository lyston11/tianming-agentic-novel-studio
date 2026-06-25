using Moq;
using TM.Web.NovelAgentWeb.Services.Caching;
using TM.Web.NovelAgentWeb.Services.Production;
using Xunit;

namespace Tests.Unit.Services.Production;

public sealed class ChapterProductionLeaseServiceTests
{
    [Fact]
    public async Task TryAcquireAsync_UsesProjectChapterLockKeyAndReleasesLease()
    {
        var locks = new Mock<IDistributedLockService>();
        DistributedLockLease? capturedLease = null;
        locks.Setup(x => x.TryAcquireAsync(
                "agent_production:chapter:project-1:chapter-002",
                TimeSpan.FromMinutes(15),
                "user-1:run-2",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, TimeSpan ttl, string owner, CancellationToken _) =>
            {
                capturedLease = new DistributedLockLease(
                    key,
                    $"{owner}:token",
                    owner,
                    DateTime.UtcNow,
                    DateTime.UtcNow.Add(ttl));
                return capturedLease;
            });
        var service = new ChapterProductionLeaseService(locks.Object);

        await using var lease = await service.TryAcquireAsync(
            "user-1",
            "project-1",
            "chapter-002",
            "run-2");

        Assert.NotNull(lease);
        Assert.Equal("agent_production:chapter:project-1:chapter-002", lease!.Key);

        await lease.DisposeAsync();

        locks.Verify(x => x.ReleaseAsync(
                It.Is<DistributedLockLease>(value =>
                    value.Key == "agent_production:chapter:project-1:chapter-002" &&
                    value.Token == capturedLease!.Token),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task TryAcquireAsync_ReturnsNullWhenDistributedLockIsBusy()
    {
        var locks = new Mock<IDistributedLockService>();
        locks.Setup(x => x.TryAcquireAsync(
                It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((DistributedLockLease?)null);
        var service = new ChapterProductionLeaseService(locks.Object);

        var lease = await service.TryAcquireAsync(
            "user-1",
            "project-1",
            "chapter-002",
            "run-2");

        Assert.Null(lease);
    }
}
