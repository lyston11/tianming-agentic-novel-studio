# Logging Guidelines

> Status: This document records conventions already implemented in this repository; it is not a planning draft.

The current backend uses Microsoft.Extensions.Logging.ILogger<T> through the
Web host and its services. Logging is structured by message templates, but the
repository does not contain a single global redaction filter. The safe logging
rules below therefore apply at every call site.

## Scenario: Web request, worker, and provider diagnostics

### 1. Scope / Trigger

Use this contract when adding logs to controllers, middleware, background
workers, persistence adapters, Redis/Qdrant integrations, or Agent runtime
bridges. A log must correlate a real operation without becoming a second copy
of its payload or credentials.

### 2. Signatures

- Inject ILogger<T> into the owning class; the repository does not use a
  separate logging facade for ordinary backend services.
- Use LogDebug, LogInformation, LogWarning, and LogError with named
  message-template properties rather than string interpolation.
- Pass the exception as the first argument to LogWarning(exception, ...) or
  LogError(exception, ...) when the exception is the event being diagnosed.
- Use HttpContext.TraceIdentifier as the request identifier exposed by
  ApiEnvelope<T>; it is not a replacement for UserId, ProjectId, or TaskId.

### 3. Contracts

- Stable dimensions use named template properties: UserId, ProjectId, SessionId,
  TaskId, Kernel, Path, StatusCode, CacheKey, CollectionName, and Url appear in
  current services where those dimensions are available.
- Keep identifiers as structured values. For example, KernelTaskWorker writes
  TaskId={TaskId} UserId={UserId} Kernel={Kernel} instead of concatenating an
  unsearchable sentence.
- Debug is for useful diagnostics such as cache hit/miss, collection existence,
  retry detail, and selected counts. It must not contain full prompts, raw
  provider bodies, or bearer tokens.
- Information is for lifecycle and durable business milestones such as a
  worker starting/stopping, a collection being created, or a user operation
  completing.
- Warning is for recoverable degradation, stale/duplicate work, an optional
  provider failure, lease loss, or an intentional fallback. Include the
  exception when it explains the warning.
- Error is for a failed operation that the caller cannot treat as successful.
  Include the exception and stable identifiers; do not log the same exception at
  every layer unless the layer adds distinct diagnostic context.
- Authentication logging currently records usernames or login identifiers and
  exception messages in AuthController. Do not expand that pattern to tokens,
  passwords, authorization headers, cookies, API keys, or serialized responses.
- There is no universal redaction service. Treat ex.Message, provider text,
  URLs, cache keys, uploaded file names, and model output as potentially
  sensitive. Log a bounded identifier or category when the full value is not
  necessary.
- The architecture test in
  old/Tests/Unit/Architecture/TargetArchitecturePurityTests.cs forbids token
  serialization and known token-printing patterns. Preserve that test whenever
  authentication code changes.

### 4. Validation & Error Matrix

| Event | Level | Required fields / behavior |
|---|---|---|
| Worker started or stopped | Information | Worker type and bounded lifecycle context |
| Cache hit/miss | Debug | Cache key only when it is non-secret; never value contents |
| Optional Redis/Qdrant failure with truth-source fallback | Warning | Provider, resource id, exception; state that fallback is active |
| Lease loss or duplicate delivery | Warning | Task/event identity and reason; do not call it success |
| Controller operation failed | Error | Exception plus project/resource id; preserve exception propagation |
| Authentication success/failure | Information/Warning/Error | Username or login identifier only; never token/password/credential |
| Client cancellation | Debug or no log at the boundary | Do not elevate normal disconnects to a fake server failure |
| Unexpected exception | Error | Trace/request id and stable resource fields; do not serialize the request |

### 5. Good / Base / Bad Cases

- Good: QdrantCollectionManager logs collection and user identifiers with
  named template properties and logs the exception when collection operations
  fail.
- Good: RedisCacheService records a warning that the SQLite truth source is
  used after a cache failure, without treating the cache failure as data loss.
- Good: KernelTaskWorker logs task, user, and kernel dimensions and lets the
  failure policy decide retry or terminal behavior.
- Base: AuthController logs a username/login identifier and an exception
  message. This existing behavior is narrower than serializing the response,
  but exception messages still require care.
- Bad: _logger.LogInformation("Bearer token: {Token}", token) or logging a
  whole request/response object that contains credentials.
- Bad: $"Task {task.Id} failed: {ex}" when a structured template can preserve
  searchable fields and the exception separately.
- Bad: logging an error and returning a successful envelope without rethrowing or
  returning the owning failure result.
- Bad: logging the same provider exception as Error in a low-level adapter and
  again as a different unrelated error in every caller.

### 6. Tests Required

- Keep AuthenticationLogging_DoesNotSerializeTokens in
  old/Tests/Unit/Architecture/TargetArchitecturePurityTests.cs green. It is a
  source-shape guard for the absence of token serialization and token snippets.
- For new security-sensitive logging, add a targeted assertion that the secret
  value is absent from the captured or source-checked log path. Do not add a
  generic logger test that does not protect a real leak boundary.
- For a worker/provider fallback, test that the fallback remains observable as a
  warning and that the operation does not claim a durable success.
- For a new structured event, test stable identifier fields or use an existing
  integration assertion that consumes the log/event contract. Parameterize
  variants when only the level or category changes.
- Before adding a logging test, state which public contract, security invariant,
  persistence transition, provider boundary, or reproduced leak would otherwise
  regress silently.

### 7. Wrong vs Correct

#### Wrong

    _logger.LogInformation("Response JSON: {Response}", JsonSerializer.Serialize(response));

This can serialize a bearer token, credentials, private user data, or a large
provider result and is specifically the kind of authentication logging the
architecture test rejects.

#### Correct

    _logger.LogError(
        exception,
        "Semantic search failed UserId={UserId} ProjectId={ProjectId}",
        userId,
        projectId);

The exception remains available to the configured logger, while the message
contains only stable scope fields needed to diagnose the failed operation.

## Common Mistakes

- Assuming named template properties automatically redact secrets; they do not.
- Passing an entire DTO or exception response to a logger for convenience.
- Using Error for an expected cache miss or client disconnect and making normal
  degradation look like a production outage.
- Adding a log-only compatibility branch for an old field or status without a
  requirement and a retirement condition.
