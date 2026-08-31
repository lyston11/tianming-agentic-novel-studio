import assert from "node:assert/strict";
import { test } from "node:test";
import { InMemoryNovelTestStore } from "./fixtures/in-memory-novel-test-store.js";
import {
  NovelAgentApplication,
  NovelCommandError,
  createReadContextTool,
  type Acceptance,
  type ActorScope,
  type CharacterState,
  type ForeshadowEntry,
} from "../src/index.js";
import { InMemoryRuntimeEventSink } from "../src/runtime/event-mapper.js";

const actor: ActorScope = { projectId: "project-1", userId: "author-1" };

async function createHarness() {
  const store = new InMemoryNovelTestStore();
  const character: CharacterState = {
    characterId: "character-1",
    name: "沈砚",
    summary: "守着旧城门的年轻抄书人。",
    status: "active",
    version: 1,
  };
  const foreshadow: ForeshadowEntry = {
    entryId: "foreshadow-1",
    label: "旧约",
    description: "一份来历不明的旧约。",
    status: "open",
    version: 1,
  };
  await store.createProject({
    ...actor,
    canonSnapshot: "第一卷开篇：旧城门下。",
    characterStates: [character],
    foreshadowEntries: [foreshadow],
  });
  const sink = new InMemoryRuntimeEventSink();
  return { store, sink, app: new NovelAgentApplication(store, sink) };
}

async function prepareConfirmed() {
  const harness = await createHarness();
  const proposalResult = await harness.app.handleConversationTurn({
    conversationId: "conversation-confirmed",
    ...actor,
    message: "写下一章：沈砚发现旧约的第一处线索",
    correlationId: "turn-confirmed",
    idempotencyKey: "turn-key-confirmed",
  });
  const commit = await harness.app.confirmGoal({
    actor,
    correlationId: "confirm-confirmed",
    proposalId: proposalResult.proposal.proposalId,
    idempotencyKey: "confirm-key-confirmed",
  });
  return { ...harness, proposal: proposalResult.proposal, commit };
}

async function prepareCandidate(scenario?: "happy" | "continuity_failure") {
  const harness = await createHarness();
  const proposalResult = await harness.app.handleConversationTurn({
    conversationId: "conversation-1",
    ...actor,
    message: "写下一章：沈砚发现旧约的第一处线索",
    correlationId: "turn-1",
    idempotencyKey: "turn-key-1",
  });
  assert.equal(proposalResult.proposal.requiresConfirmation, true);
  assert.equal(proposalResult.proposal.chapterNumber, 1);

  const beforeConfirm = await harness.app.queryWorkflow(actor);
  assert.equal(beforeConfirm.proposal?.proposalId, proposalResult.proposal.proposalId);
  assert.equal(beforeConfirm.goal, null);
  assert.equal(beforeConfirm.candidate, null);

  const commit = await harness.app.confirmGoal({
    actor,
    correlationId: "confirm-1",
    proposalId: proposalResult.proposal.proposalId,
    idempotencyKey: "confirm-key-1",
  });
  assert.equal(commit.goal.status, "confirmed");
  assert.equal(commit.task.status, "queued");
  assert.equal(commit.contextPackage.contentHash.length, 64);

  const chapter = scenario === undefined
    ? await harness.app.runChapterTask({ actor, correlationId: "chapter-1" })
    : await harness.app.runChapterTask({ actor, correlationId: "chapter-1", scenario });
  assert.equal(chapter.coreRunReason, "natural_stop");
  assert.ok(chapter.candidate);
  assert.ok(chapter.review);
  return { ...harness, proposal: proposalResult.proposal, commit, chapter };
}

test("runs the proposal-to-canon vertical slice with a frozen context", async () => {
  const harness = await prepareCandidate();
  const { candidate, review } = harness.chapter;
  assert.equal(review?.passed, true);
  assert.equal(candidate?.status, "awaiting_acceptance");
  assert.equal(candidate?.contextPackageHash, harness.commit.contextPackage.contentHash);

  const awaiting = await harness.app.queryWorkflow(actor);
  assert.equal(awaiting.production?.status, "awaiting_acceptance");
  assert.equal(awaiting.review?.passed, true);
  assert.equal(awaiting.acceptance, null);
  const chapterRunMessages = harness.sink.replay(`chapter-run-${harness.commit.task.taskId}-chapter-1`);
  assert.ok(chapterRunMessages.length > 0);
  assert.equal(chapterRunMessages[0]?.sequence, 1);
  assert.equal(chapterRunMessages.at(-1)?.eventType, "agent_end");
  assert.ok(harness.sink.messages.some((message) => message.eventType === "agent_start"));
  assert.ok(harness.sink.messages.some((message) => message.eventType === "agent_end"));
  assert.ok(harness.sink.replay(`chapter-run-${harness.commit.task.taskId}-chapter-1`, 1).length < chapterRunMessages.length);

  const accepted = await harness.app.acceptCandidate({
    actor,
    actorId: actor.userId,
    correlationId: "accept-1",
    candidateId: candidate!.candidateId,
    decision: "accepted",
    expectedCandidateVersion: candidate!.candidateVersion,
    contextPackageHash: harness.commit.contextPackage.contentHash,
    idempotencyKey: "accept-key-1",
  });
  assert.ok(accepted.canonMerge);
  assert.equal(accepted.canonMerge?.canonicalChapter.candidateId, candidate!.candidateId);
  assert.equal(accepted.canonMerge?.characterStates[0]?.version, 2);
  assert.equal(accepted.canonMerge?.foreshadowEntries[0]?.version, 2);

  const after = await harness.app.queryWorkflow(actor);
  assert.equal(after.candidate?.status, "merged");
  assert.equal(after.production?.status, "completed");
  assert.equal(after.task?.status, "completed");
  assert.equal(after.canonicalChapter?.candidateId, candidate!.candidateId);
  assert.equal(after.sourceCandidateId, candidate!.candidateId);
  assert.equal(after.sourceContextPackageHash, harness.commit.contextPackage.contentHash);

  const duplicate = await harness.app.acceptCandidate({
    actor,
    actorId: actor.userId,
    correlationId: "accept-1",
    candidateId: candidate!.candidateId,
    decision: "accepted",
    expectedCandidateVersion: candidate!.candidateVersion,
    contextPackageHash: harness.commit.contextPackage.contentHash,
    idempotencyKey: "accept-key-1",
  });
  assert.deepEqual(duplicate, accepted);
});

test("rejects proposal idempotency reuse for different content", async () => {
  const harness = await createHarness();
  const first = await harness.app.handleConversationTurn({
    conversationId: "conversation-idempotent",
    ...actor,
    message: "写第一章的旧约线索",
    correlationId: "turn-idempotent-1",
    idempotencyKey: "same-turn-key",
  });
  assert.ok(first.proposal);
  await assert.rejects(
    harness.app.handleConversationTurn({
      conversationId: "conversation-idempotent-2",
      ...actor,
      message: "写第一章的城门冲突",
      correlationId: "turn-idempotent-2",
      idempotencyKey: "same-turn-key",
    }),
    (error: unknown) => error instanceof NovelCommandError && error.code === "conflict",
  );
});

test("does not create production before proposal confirmation", async () => {
  const harness = await createHarness();
  const result = await harness.app.handleConversationTurn({
    conversationId: "conversation-2",
    ...actor,
    message: "先讨论主角如何发现旧约",
    correlationId: "turn-2",
  });
  const projection = await harness.app.queryWorkflow(actor);
  assert.equal(projection.proposal?.proposalId, result.proposal.proposalId);
  assert.equal(projection.goal, null);
  assert.equal(projection.production, null);
  assert.equal(projection.canonicalChapter, null);
});

test("blocks a continuity failure before acceptance", async () => {
  const harness = await prepareCandidate("continuity_failure");
  assert.equal(harness.chapter.review?.passed, false);
  assert.equal(harness.chapter.candidate?.status, "blocked");
  const projection = await harness.app.queryWorkflow(actor);
  assert.equal(projection.production?.status, "blocked");
  assert.equal(projection.acceptance, null);
  await assert.rejects(
    harness.app.acceptCandidate({
      actor,
      actorId: actor.userId,
      correlationId: "accept-failed-gate",
      candidateId: harness.chapter.candidate!.candidateId,
      decision: "accepted",
      expectedCandidateVersion: 1,
      contextPackageHash: harness.commit.contextPackage.contentHash,
      idempotencyKey: "accept-failed-gate",
    }),
    (error: unknown) => error instanceof NovelCommandError && error.code === "invalid_transition",
  );
});

test("records a rejection and blocks the production without merging Canon", async () => {
  const harness = await prepareCandidate();
  const candidate = harness.chapter.candidate!;
  const result = await harness.app.acceptCandidate({
    actor,
    actorId: actor.userId,
    correlationId: "reject-1",
    candidateId: candidate.candidateId,
    decision: "rejected",
    expectedCandidateVersion: candidate.candidateVersion,
    contextPackageHash: harness.commit.contextPackage.contentHash,
    idempotencyKey: "reject-key-1",
  });
  assert.equal(result.canonMerge, null);
  const projection = await harness.app.queryWorkflow(actor);
  assert.equal(projection.acceptance?.decision, "rejected");
  assert.equal(projection.candidate?.status, "rejected");
  assert.equal(projection.production?.status, "blocked");
  assert.equal(projection.canonicalChapter, null);
});

test("rejects stale versions and cross-project access without writes", async () => {
  const harness = await prepareCandidate();
  const candidate = harness.chapter.candidate!;
  await assert.rejects(
    harness.app.acceptCandidate({
      actor,
      actorId: actor.userId,
      correlationId: "accept-stale-version",
      candidateId: candidate.candidateId,
      decision: "accepted",
      expectedCandidateVersion: candidate.candidateVersion + 1,
      contextPackageHash: harness.commit.contextPackage.contentHash,
      idempotencyKey: "accept-stale-version",
    }),
    (error: unknown) => error instanceof NovelCommandError && error.code === "conflict",
  );

  const otherActor: ActorScope = { projectId: actor.projectId, userId: "other-author" };
  await assert.rejects(
    harness.app.queryWorkflow(otherActor),
    (error: unknown) => error instanceof NovelCommandError && error.code === "forbidden",
  );
  const projection = await harness.app.queryWorkflow(actor);
  assert.equal(projection.acceptance, null);
  assert.equal(projection.candidate?.status, "awaiting_acceptance");
});

test("uses the frozen package even when the mutable project snapshot changes", async () => {
  const harness = await prepareConfirmed();
  const frozen = harness.commit.contextPackage;
  const mutable = await harness.store.loadForConversation({ actor, correlationId: "mutate" });
  harness.store.contextProvider.replaceProjectSnapshot({
    ...mutable,
    canonSnapshot: "后来写入的未冻结内容。",
    characterStates: [{ ...mutable.characterStates[0]!, summary: "mutable summary" }],
  });
  const run = await harness.app.runChapterTask({ actor, correlationId: "frozen-run" });
  assert.ok(run.candidate);
  assert.equal(run.candidate?.contextPackageHash, frozen.contentHash);
  assert.equal(run.candidate?.body.includes("mutable summary"), false);
});

test("does not reopen terminal candidates and rejects forged Canon merges", async () => {
  const harness = await prepareCandidate();
  const candidate = harness.chapter.candidate!;
  await harness.app.acceptCandidate({
    actor,
    actorId: actor.userId,
    correlationId: "reject-terminal",
    candidateId: candidate.candidateId,
    decision: "rejected",
    expectedCandidateVersion: candidate.candidateVersion,
    contextPackageHash: harness.commit.contextPackage.contentHash,
    idempotencyKey: "reject-terminal-key",
  });
  await assert.rejects(
    harness.app.runChapterTask({ actor, correlationId: "rerun-terminal" }),
    (error: unknown) => error instanceof NovelCommandError && error.code === "invalid_transition",
  );

  const forgedAcceptance: Acceptance = {
    acceptanceId: "forged",
    candidateId: candidate.candidateId,
    projectId: actor.projectId,
    userId: actor.userId,
    actorId: actor.userId,
    decision: "rejected",
    expectedCandidateVersion: candidate.candidateVersion,
    contextPackageHash: harness.commit.contextPackage.contentHash,
    idempotencyKey: "forged-key",
    createdAt: new Date(0).toISOString(),
  };
  await assert.rejects(
    harness.store.mergeAcceptedCandidate({
      actor,
      candidate,
      contextPackage: harness.commit.contextPackage,
      acceptance: forgedAcceptance,
    }),
    (error: unknown) => error instanceof NovelCommandError && error.code === "invalid_transition",
  );
});

test("records malformed, model-error, and abort runs as failed tasks", async () => {
  for (const scenario of ["malformed", "error", "abort"] as const) {
    const harness = await prepareConfirmed();
    await assert.rejects(
      harness.app.runChapterTask({ actor, correlationId: `${scenario}-run`, scenario }),
      (error: unknown) => error instanceof NovelCommandError && (scenario === "abort" ? error.code === "aborted" : error.code === "model_error"),
    );
    const projection = await harness.app.queryWorkflow(actor);
    assert.equal(projection.task?.status, "failed");
    assert.equal(projection.production?.status, "failed");
    assert.equal(projection.batch?.status, "failed");
    assert.equal(projection.candidate, null);
  }
});

test("serializes concurrent confirmations and acceptance idempotency", async () => {
  const harness = await createHarness();
  const proposal = (await harness.app.handleConversationTurn({
    conversationId: "concurrent-confirm",
    ...actor,
    message: "并发确认这一章",
    correlationId: "concurrent-turn",
    idempotencyKey: "concurrent-turn-key",
  })).proposal;
  const confirmations = await Promise.all([
    harness.app.confirmGoal({ actor, correlationId: "confirm-a", proposalId: proposal.proposalId, idempotencyKey: "same-confirm-key" }),
    harness.app.confirmGoal({ actor, correlationId: "confirm-b", proposalId: proposal.proposalId, idempotencyKey: "same-confirm-key" }),
  ]);
  assert.deepEqual(confirmations[0], confirmations[1]);

  const chapter = await harness.app.runChapterTask({ actor, correlationId: "concurrent-chapter" });
  const candidate = chapter.candidate!;
  const acceptInput = {
    actor,
    actorId: actor.userId,
    candidateId: candidate.candidateId,
    decision: "accepted" as const,
    expectedCandidateVersion: candidate.candidateVersion,
    contextPackageHash: confirmations[0]!.contextPackage.contentHash,
    idempotencyKey: "same-accept-key",
  };
  const acceptances = await Promise.all([
    harness.app.acceptCandidate({ ...acceptInput, correlationId: "accept-a" }),
    harness.app.acceptCandidate({ ...acceptInput, correlationId: "accept-b" }),
  ]);
  assert.deepEqual(acceptances[0], acceptances[1]);
  assert.equal(acceptances[0]!.canonMerge?.canonicalChapter.candidateId, candidate.candidateId);
});

test("reads context through a frozen-package tool when supplied", async () => {
  const harness = await prepareConfirmed();
  const tool = createReadContextTool({
    actor,
    correlationId: "frozen-tool",
    contextPort: harness.store,
    proposalPort: harness.store,
    candidatePort: harness.store,
    contextPackage: harness.commit.contextPackage,
  });
  harness.store.contextProvider.replaceProjectSnapshot({
    ...(await harness.store.loadForConversation({ actor, correlationId: "mutable-tool" })),
    canonSnapshot: "mutable",
  });
  const result = await tool.execute({ projectId: actor.projectId }, {
    signal: new AbortController().signal,
    onUpdate: () => undefined,
  });
  assert.deepEqual(result, harness.commit.contextPackage);
});
