using Moq;
using TM.Web.NovelAgentWeb.Services.AgentRuntime;
using TM.Web.NovelAgentWeb.Services.Caching;
using Xunit;

namespace Tests.Unit.Services.AgentRuntime;

public class AgentRuntimeRunLeaseServiceTests
{
    [Fact]
    public async Task TryAcquireAsync_UsesRuntimeRunLockKeyAndReleasesLease()
    {
        var locks = new Mock<IDistributedLockService>();
        DistributedLockLease? capturedLease = null;
        locks.Setup(x => x.TryAcquireAsync(
                "agent_runtime:lock:run-1",
                TimeSpan.FromMinutes(10),
                It.Is<string>(owner => owner.StartsWith("agent-runtime-worker:", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, TimeSpan ttl, string owner, CancellationToken _) =>
            {
                capturedLease = new DistributedLockLease(
                    Key: key,
                    Token: $"{owner}:token",
                    Owner: owner,
                    AcquiredAt: DateTime.UtcNow,
                    ExpiresAt: DateTime.UtcNow.Add(ttl));
                return capturedLease;
            });
        var service = new AgentRuntimeRunLeaseService(locks.Object);

        var lease = await service.TryAcquireAsync("run-1");

        Assert.NotNull(lease);
        Assert.Equal("run-1", lease!.RuntimeRunId);
        Assert.Equal(capturedLease!.Token, lease.Token);

        await lease.DisposeAsync();

        locks.Verify(x => x.ReleaseAsync(
                It.Is<DistributedLockLease>(release =>
                    release.Key == "agent_runtime:lock:run-1" &&
                    release.Token == capturedLease.Token),
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
        var service = new AgentRuntimeRunLeaseService(locks.Object);

        var lease = await service.TryAcquireAsync("run-1");

        Assert.Null(lease);
    }

    [Fact]
    public async Task RenewAsync_ExtendsUnderlyingDistributedLease()
    {
        var distributedLease = new DistributedLockLease(
            Key: "agent_runtime:lock:run-1",
            Token: "token-1",
            Owner: "worker-1",
            AcquiredAt: DateTime.UtcNow,
            ExpiresAt: DateTime.UtcNow.AddMinutes(10));
        var renewedLease = distributedLease with { ExpiresAt = DateTime.UtcNow.AddMinutes(20) };
        var locks = new Mock<IDistributedLockService>();
        locks.Setup(x => x.ExtendAsync(
                distributedLease,
                TimeSpan.FromMinutes(10),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(renewedLease);
        var lease = new AgentRuntimeRunLease("run-1", distributedLease, locks.Object);

        var renewed = await lease.RenewAsync(TimeSpan.FromMinutes(10));

        Assert.True(renewed);
        Assert.Equal(renewedLease.ExpiresAt, lease.ExpiresAt);
    }

    [Fact]
    public async Task RenewAsync_ReturnsFalseWhenDistributedLeaseNoLongerOwned()
    {
        var distributedLease = new DistributedLockLease(
            Key: "agent_runtime:lock:run-1",
            Token: "token-1",
            Owner: "worker-1",
            AcquiredAt: DateTime.UtcNow,
            ExpiresAt: DateTime.UtcNow.AddMinutes(10));
        var locks = new Mock<IDistributedLockService>();
        locks.Setup(x => x.ExtendAsync(
                distributedLease,
                TimeSpan.FromMinutes(10),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((DistributedLockLease?)null);
        var lease = new AgentRuntimeRunLease("run-1", distributedLease, locks.Object);

        var renewed = await lease.RenewAsync(TimeSpan.FromMinutes(10));

        Assert.False(renewed);
    }
}
