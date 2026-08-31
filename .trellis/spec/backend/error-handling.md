# Error Handling

> Status: This document records conventions already implemented in this repository; it is not a planning draft.

The backend currently combines the old/Web/NovelAgentWeb host, the four
old/Agent/Tianming.NovelAgent.* projects, and the newer
tianming-novel-agent TypeScript adapter. The rules below describe behavior that
exists in those paths. A planned migration or task PRD is not evidence that a
new HTTP status or durable error state already exists.

## Scenario: Web API and Agent command failures

### 1. Scope / Trigger

Use this contract for controller commands, Application/Domain ports, background
kernel work, AgentCore tool execution, and code that translates a failure into
an API envelope or a durable task result.

### 2. Signatures

- GlobalExceptionMiddleware.InvokeAsync(HttpContext context) is the Web
  catch-all boundary in old/Web/NovelAgentWeb/Middleware/GlobalExceptionMiddleware.cs.
- ApiEnvelope<T>.Ok(T data, string requestId) and
  ApiEnvelope<T>.Fail(ApiErrorDto error, string requestId) define the JSON
  envelope in old/Web/NovelAgentWeb/DTOs/ApiEnvelope.cs.
- AgentToolExecutionResult(Name, Succeeded, Message, Confirmation, Error) is
  the typed tool outcome in
  old/Agent/Tianming.NovelAgent.Application/Ports/AgentPorts.cs.
- KernelTaskFailurePolicy.Decide(taskType, attempt, maxAttempts, category)
  owns the background retry/terminal decision in
  old/Web/NovelAgentWeb/Services/Goals/KernelTaskFailurePolicy.cs.
- AgentChatIdempotencyService.ExecuteAsync(sessionId, message, canonicalKey,
  execute) owns the current chat idempotency protocol in
  old/Web/NovelAgentWeb/Services/AgentSessions/AgentChatIdempotencyService.cs.

There is no repository-wide generic Result<T> monad. Expected outcomes use
typed records with flags or status values; invalid boundaries and unexpected
failures use exceptions. Do not introduce a second result hierarchy just to
make one new endpoint look cleaner.

### 3. Contracts

- A successful Web response is wrapped in success, data, requestId, serverTime,
  apiVersion, toolSchemaVersion, agentLoopVersion, and kernelVersion. Failure
  details live under error.
- ApiEnvelopeResultFilter wraps ordinary ObjectResult and JsonResult responses.
  It intentionally skips SSE, ForbidResult, ChallengeResult, and other
  responses whose protocol is not a JSON envelope.
- KeyNotFoundException is mapped by the global middleware to HTTP 404 with code
  HTTP_404. Controllers may also return NotFound(...) directly, as in
  old/Web/NovelAgentWeb/Controllers/StoryBibleController.cs.
- forbidden means the authenticated caller is not allowed to access the
  resource. Existing services commonly throw UnauthorizedAccessException and
  controllers commonly return Forbid(). The global middleware has no special
  UnauthorizedAccessException branch, so an exception that escapes a controller
  is not automatically a 403. Preserve the local controller mapping.
- conflict means the request cannot replace an existing result or version.
  AgentChatIdempotencyConflictException is explicitly mapped to HTTP 409 with
  AGENT_CHAT_IDEMPOTENCY_CONFLICT. CanonMergeConflictException currently
  inherits InvalidOperationException and therefore follows the existing HTTP
  400 mapping; do not describe it as a 409 unless its mapping is changed and
  tested in the same task.
- invalid_transition means a domain state machine rejects the requested
  transition. The TypeScript domain exposes this code through NovelCommandError;
  the current C# middleware does not inspect DomainRuleException.Code and maps
  that exception through InvalidOperationException to HTTP 400.
- cancelled is not a successful result. KernelTaskWorker rethrows an
  OperationCanceledException when the stopping token is cancelled. The Web
  middleware has no dedicated cancellation branch, so an exception that reaches
  it falls through to the current HTTP 500 catch-all. Do not catch cancellation
  and fabricate a completed task, response, or idempotency receipt.
- The current code has no durable outcome_unknown HTTP mapping. If a new task
  introduces that state, it must define its persisted owner and observable API
  contract explicitly; a retry or success response is not a substitute.
- A chat idempotency key is bound to a SHA-256 request hash. Same user/session,
  same canonical key, and same payload replays the stored response. The same
  key with a different payload throws AgentChatIdempotencyConflictException.
  A still-processing receipt is polled up to the existing limit and then maps
  to AGENT_CHAT_IN_PROGRESS with HTTP 409 and Retry-After: 1.
- KernelTaskFailurePolicy classifies HttpRequestException, TimeoutException,
  TaskCanceledException, DbUpdateConcurrencyException, and NpgsqlException
  with IsTransient == true as transient. It applies the existing exponential
  backoff while attempts remain, except where task semantics require a human
  decision. A stopping-token cancellation must not be silently reclassified.
- AgentCore tool failures remain observable. Unknown tools, parameter failures,
  thrown tool exceptions, and an aborted tool call produce a
  tool_execution_end event with isError: true and a matching
  ToolResultMessage.isError: true. A caller must not turn that result into a
  normal assistant success merely because the process continued.
- The TypeScript domain uses the explicit error codes not_found, forbidden,
  invalid_argument, conflict, invalid_transition, model_error, cancelled, and
  aborted in tianming-novel-agent/src/contracts.ts. Keep those codes stable when
  that boundary is the owner.

### 4. Validation & Error Matrix

| Condition | Current behavior | Required handling |
|---|---|---|
| Missing resource | KeyNotFoundException or controller NotFound | Return 404/not-found; do not write state |
| Cross-user/project access | Service UnauthorizedAccessException or controller Forbid | Preserve forbidden semantics and avoid leaking resource data |
| Same idempotency key, same payload | Stored response replay | Return the original result without executing the command again |
| Same idempotency key, different payload | AgentChatIdempotencyConflictException | Return conflict/409; never replace the receipt |
| Invalid state transition | C# InvalidOperationException mapping or TS invalid_transition | Reject without partial success |
| User cancellation | OperationCanceledException propagation | Preserve cancellation; do not mark success |
| Transient Npgsql/provider failure | Transient failure category in the worker policy | Retry only under the policy and attempt budget |
| Unknown/failed tool | isError: true event and tool result | Keep failure visible to the model/runtime and logs |
| Unexpected exception | HTTP 500 HTTP_500 at the Web boundary | Preserve the exception for diagnostics; do not fake data |

### 5. Good / Base / Bad Cases

- Good: StoryBibleController maps a missing story-bible resource to its
  documented not-found response, while unauthorized project access follows the
  controller's forbidden path.
- Good: AgentChatIdempotencyService hashes the request, replays a completed
  receipt, and rejects a different payload under the same key.
- Good: KernelTaskWorker lets the failure policy decide retry versus terminal
  handling and rethrows stopping-token cancellation.
- Base: an InvalidOperationException from an existing service becomes the
  current HTTP_400 envelope. This is a compatibility fact, not proof that all
  domain conflicts have a dedicated status.
- Bad: catch (Exception) { return Success(...); }. This hides provider,
  persistence, and cancellation failures and makes the client believe work was
  committed.
- Bad: catching OperationCanceledException with ordinary exceptions and writing
  a completed task receipt.
- Bad: mapping every UnauthorizedAccessException to a generic 500 response in
  a controller that already has an explicit Forbid() contract.
- Bad: retrying PrefixMerge or an acceptance gate as if it were a harmless
  network read when the failure policy requires a decision.

### 6. Tests Required

- old/Tests/Unit/Middleware/GlobalExceptionMiddlewareTests.cs must continue to
  assert status code, success == false, request id, error code, and message for
  400 and 500 paths.
- old/Tests/Unit/Services/AgentSessions/AgentChatIdempotencyServiceTests.cs
  must cover replay, same-key/different-payload conflict, and expired receipt
  takeover without executing the callback twice.
- old/Tests/Unit/Services/Goals/KernelTaskFailurePolicyTests.cs must cover
  transient retry, exhausted recoverable work, fatal termination, and task-type
  attempt limits.
- old/Tests/Unit/Architecture/TargetArchitecturePurityTests.cs protects the
  authentication logging boundary: tokens must not be serialized, printed, or
  substringed for diagnostics.
- tianming-agent-core/test/agent-core.test.ts must assert isError for unknown
  tools, validation failures, thrown tools, and aborted calls.
- Any new status mapping needs a negative assertion that the old status is no
  longer emitted, plus a test for no partial persistence. Do not add a broad
  exception test that only proves a catch block exists.

### 7. Wrong vs Correct

#### Wrong

    try
    {
        await scheduler.CompleteAsync(claim, ct);
    }
    catch (Exception)
    {
        return ApiEnvelope<object>.Ok(new { status = "completed" });
    }

This reports success after an unknown persistence outcome and discards the
failure category.

#### Correct

    try
    {
        return await scheduler.CompleteAsync(claim, ct);
    }
    catch (OperationCanceledException)
    {
        throw;
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Kernel task completion failed TaskId={TaskId}", claim.TaskId);
        throw;
    }

The caller keeps cancellation and persistence failure observable. A retry, a
terminal task result, or an explicit unknown-outcome contract must be selected
by the owning worker/application boundary and covered by a targeted test.

## Common Mistakes

- Assuming DomainRuleException.Code is automatically serialized by the Web
  middleware; the current middleware only branches on exception types.
- Treating a task that reached agent_end as a committed production result.
- Logging and returning an exception message without checking whether a token,
  credential, or provider response was included in it.
- Adding a new compatibility catch branch for an old status without a stated
  requirement, migration, version, and retirement condition.
