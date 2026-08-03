using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Services.Auth;

namespace TM.Web.NovelAgentWeb.Services.AgentSessions;

public interface IAgentChatIdempotencyService
{
    Task<AgentChatResponse> ExecuteAsync(
        string sessionId,
        string message,
        string? canonicalKey,
        Func<Task<AgentChatResponse>> execute,
        CancellationToken cancellationToken = default);
}

public sealed class AgentChatIdempotencyConflictException(string message) : InvalidOperationException(message);
public sealed class AgentChatRequestInProgressException(string message) : InvalidOperationException(message);

public sealed class AgentChatIdempotencyService : IAgentChatIdempotencyService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(15);
    private readonly NovelAgentDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public AgentChatIdempotencyService(NovelAgentDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<AgentChatResponse> ExecuteAsync(
        string sessionId,
        string message,
        string? canonicalKey,
        Func<Task<AgentChatResponse>> execute,
        CancellationToken cancellationToken = default)
    {
        canonicalKey = Normalize(canonicalKey);
        if (canonicalKey == null)
            return await execute().ConfigureAwait(false);
        if (canonicalKey.Length > 160)
            throw new ArgumentException("Idempotency-Key 长度不能超过 160。", nameof(canonicalKey));

        var userId = _currentUser.GetUserId();
        var requestedSessionId = string.IsNullOrWhiteSpace(sessionId) ? "__new__" : sessionId.Trim();
        var requestHash = Sha256(message.Trim());
        var leaseOwner = Guid.NewGuid().ToString("N");
        var existing = await FindAsync(userId, requestedSessionId, canonicalKey, cancellationToken).ConfigureAwait(false);
        if (existing != null)
        {
            ValidateRequestHash(existing, requestHash);
            if (existing.Status == "completed")
                return DeserializeResponse(existing);
            if (await TryTakeOverExpiredAsync(existing, leaseOwner, cancellationToken).ConfigureAwait(false))
                return await ExecuteOwnedAsync(existing.Id, leaseOwner, execute, cancellationToken).ConfigureAwait(false);
            return await ReplayAsync(existing.Id, requestHash, cancellationToken).ConfigureAwait(false);
        }

        var now = DateTime.UtcNow;
        var receipt = new AgentChatRequestReceipt
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = userId,
            RequestedSessionId = requestedSessionId,
            CanonicalKey = canonicalKey,
            RequestHash = requestHash,
            Status = "processing",
            LeaseOwner = leaseOwner,
            LeaseExpiresAt = now.Add(LeaseDuration),
            CreatedAt = now,
            UpdatedAt = now
        };
        _db.AgentChatRequestReceipts.Add(receipt);
        try
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            _db.Entry(receipt).State = EntityState.Detached;
            existing = await FindAsync(userId, requestedSessionId, canonicalKey, cancellationToken).ConfigureAwait(false);
            if (existing == null)
                throw;
            ValidateRequestHash(existing, requestHash);
            if (existing.Status == "completed")
                return DeserializeResponse(existing);
            if (await TryTakeOverExpiredAsync(existing, leaseOwner, cancellationToken).ConfigureAwait(false))
                return await ExecuteOwnedAsync(existing.Id, leaseOwner, execute, cancellationToken).ConfigureAwait(false);
            return await ReplayAsync(existing.Id, requestHash, cancellationToken).ConfigureAwait(false);
        }

        return await ExecuteOwnedAsync(receipt.Id, leaseOwner, execute, cancellationToken).ConfigureAwait(false);
    }

    private async Task<AgentChatResponse> ExecuteOwnedAsync(
        string receiptId,
        string leaseOwner,
        Func<Task<AgentChatResponse>> execute,
        CancellationToken cancellationToken)
    {
        AgentChatResponse response;
        try
        {
            response = await execute().ConfigureAwait(false);
        }
        catch
        {
            await ReleaseFailedReceiptAsync(receiptId, leaseOwner).ConfigureAwait(false);
            throw;
        }

        var completedAt = DateTime.UtcNow;
        var responseJson = JsonSerializer.Serialize(response, JsonOptions);
        var updated = await CompleteOwnedAsync(
            receiptId,
            leaseOwner,
            response.SessionId,
            responseJson,
            completedAt,
            cancellationToken).ConfigureAwait(false);
        if (updated != 1)
            throw new AgentChatIdempotencyConflictException("聊天请求的幂等执行权已失效，响应未被重复写入。");
        return response;
    }

    private async Task<AgentChatResponse> ReplayAsync(
        string receiptId,
        string requestHash,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            _db.ChangeTracker.Clear();
            var receipt = await _db.AgentChatRequestReceipts.AsNoTracking().SingleAsync(item => item.Id == receiptId, cancellationToken)
                .ConfigureAwait(false);
            ValidateRequestHash(receipt, requestHash);
            if (receipt.Status == "completed")
                return DeserializeResponse(receipt);
            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }
        throw new AgentChatRequestInProgressException("相同幂等键的聊天请求仍在处理中，请稍后重试。");
    }

    private async Task<bool> TryTakeOverExpiredAsync(
        AgentChatRequestReceipt receipt,
        string leaseOwner,
        CancellationToken cancellationToken)
    {
        if (receipt.Status != "processing" || receipt.LeaseExpiresAt > DateTime.UtcNow)
            return false;
        var now = DateTime.UtcNow;
        if (_db.Database.IsRelational())
        {
            var updated = await _db.AgentChatRequestReceipts
                .Where(item =>
                    item.Id == receipt.Id &&
                    item.Status == "processing" &&
                    (item.LeaseExpiresAt == null || item.LeaseExpiresAt <= now))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.LeaseOwner, leaseOwner)
                    .SetProperty(item => item.LeaseExpiresAt, now.Add(LeaseDuration))
                    .SetProperty(item => item.UpdatedAt, now), cancellationToken)
                .ConfigureAwait(false);
            return updated == 1;
        }
        var tracked = await _db.AgentChatRequestReceipts.SingleAsync(item => item.Id == receipt.Id, cancellationToken);
        if (tracked.Status != "processing" || tracked.LeaseExpiresAt > now)
            return false;
        tracked.LeaseOwner = leaseOwner;
        tracked.LeaseExpiresAt = now.Add(LeaseDuration);
        tracked.UpdatedAt = now;
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task ReleaseFailedReceiptAsync(string receiptId, string leaseOwner)
    {
        if (_db.Database.IsRelational())
        {
            await _db.AgentChatRequestReceipts
                .Where(item => item.Id == receiptId && item.Status == "processing" && item.LeaseOwner == leaseOwner)
                .ExecuteDeleteAsync(CancellationToken.None)
                .ConfigureAwait(false);
            return;
        }
        var receipt = await _db.AgentChatRequestReceipts.SingleOrDefaultAsync(item =>
            item.Id == receiptId && item.Status == "processing" && item.LeaseOwner == leaseOwner);
        if (receipt == null)
            return;
        _db.AgentChatRequestReceipts.Remove(receipt);
        await _db.SaveChangesAsync(CancellationToken.None);
    }

    private async Task<int> CompleteOwnedAsync(
        string receiptId,
        string leaseOwner,
        string resolvedSessionId,
        string responseJson,
        DateTime completedAt,
        CancellationToken cancellationToken)
    {
        if (_db.Database.IsRelational())
        {
            return await _db.AgentChatRequestReceipts
                .Where(item => item.Id == receiptId && item.Status == "processing" && item.LeaseOwner == leaseOwner)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.Status, "completed")
                    .SetProperty(item => item.ResponseJson, responseJson)
                    .SetProperty(item => item.ResolvedSessionId, resolvedSessionId)
                    .SetProperty(item => item.CompletedAt, completedAt)
                    .SetProperty(item => item.UpdatedAt, completedAt)
                    .SetProperty(item => item.LeaseOwner, (string?)null)
                    .SetProperty(item => item.LeaseExpiresAt, (DateTime?)null), cancellationToken)
                .ConfigureAwait(false);
        }
        var receipt = await _db.AgentChatRequestReceipts.SingleOrDefaultAsync(item =>
            item.Id == receiptId && item.Status == "processing" && item.LeaseOwner == leaseOwner,
            cancellationToken);
        if (receipt == null)
            return 0;
        receipt.Status = "completed";
        receipt.ResponseJson = responseJson;
        receipt.ResolvedSessionId = resolvedSessionId;
        receipt.CompletedAt = completedAt;
        receipt.UpdatedAt = completedAt;
        receipt.LeaseOwner = null;
        receipt.LeaseExpiresAt = null;
        await _db.SaveChangesAsync(cancellationToken);
        return 1;
    }

    private static void ValidateRequestHash(AgentChatRequestReceipt receipt, string requestHash)
    {
        if (!string.Equals(receipt.RequestHash, requestHash, StringComparison.Ordinal))
            throw new AgentChatIdempotencyConflictException("同一幂等键不能用于不同的聊天内容。");
    }

    private static AgentChatResponse DeserializeResponse(AgentChatRequestReceipt receipt)
    {
        if (string.IsNullOrWhiteSpace(receipt.ResponseJson))
            throw new InvalidOperationException("幂等聊天回执缺少有效响应。");
        return JsonSerializer.Deserialize<AgentChatResponse>(receipt.ResponseJson, JsonOptions)
            ?? throw new InvalidOperationException("幂等聊天回执缺少有效响应。");
    }

    private Task<AgentChatRequestReceipt?> FindAsync(string userId, string sessionId, string key, CancellationToken ct) =>
        _db.AgentChatRequestReceipts.AsNoTracking().SingleOrDefaultAsync(item =>
            item.UserId == userId && item.RequestedSessionId == sessionId && item.CanonicalKey == key, ct);
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string Sha256(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
