import {
  NovelCommandError,
  clone,
  proposalContentHash,
  type ConfirmGoalProposalCommand,
  type DecideProposalCommand,
  type GoalProposal,
  type GoalProposalContent,
  type GoalProposalRevision,
  type GoalProposalStatus,
  type GoalProposalTransition,
  type ReviseProposalCommand,
} from "../contracts.js";

export interface ProposalRevisionState {
  readonly proposal: GoalProposal;
  readonly revision: GoalProposalRevision;
  readonly transition: GoalProposalTransition;
}

export interface ProposalTransitionContext {
  readonly revisionId: string;
  readonly transitionId: string;
  readonly actorId: string;
  readonly correlationId: string;
  readonly idempotencyKey: string;
  readonly occurredAt: string;
}

export function createProposedProposal(
  content: GoalProposalContent,
  proposalId: string,
  context: ProposalTransitionContext,
): ProposalRevisionState {
  const contentHash = proposalContentHash(content);
  const proposal: GoalProposal = {
    proposalId,
    ...clone(content),
    contentHash,
    status: "proposed",
    revisionId: context.revisionId,
    createdAt: context.occurredAt,
    updatedAt: context.occurredAt,
  };
  const revision = toRevision(proposal, "proposed", context.occurredAt);
  return {
    proposal,
    revision,
    transition: transition(
      proposal,
      "draft",
      "proposed",
      context,
    ),
  };
}

export function reviseProposal(
  current: GoalProposal,
  input: ReviseProposalCommand,
  context: ProposalTransitionContext,
): ProposalRevisionState {
  assertExpectedProposal(current, input.expectedProposalVersion, input.expectedContentHash);
  ensureStatus(current, ["draft", "proposed"], "revise");
  const content: GoalProposalContent = {
    projectId: current.projectId,
    conversationId: current.conversationId,
    createdBy: current.createdBy,
    ...clone(input.content),
    sourceMessageIds: clone(input.sourceMessageIds),
    proposalVersion: current.proposalVersion + 1,
  };
  const contentHash = proposalContentHash(content);
  const proposal: GoalProposal = {
    proposalId: current.proposalId,
    ...content,
    contentHash,
    status: "proposed",
    revisionId: context.revisionId,
    createdAt: current.createdAt,
    updatedAt: context.occurredAt,
  };
  const revision = toRevision(proposal, "proposed", context.occurredAt);
  return {
    proposal,
    revision,
    transition: transition(current, current.status, "proposed", context, `Revision ${proposal.proposalVersion} superseded revision ${current.proposalVersion}.`),
  };
}

export function decideProposal(
  current: GoalProposal,
  input: DecideProposalCommand,
  target: Extract<GoalProposalStatus, "rejected" | "discarded">,
  context: ProposalTransitionContext,
): ProposalRevisionState {
  assertExpectedProposal(current, input.expectedProposalVersion, input.expectedContentHash);
  ensureStatus(current, target === "discarded" ? ["draft", "proposed"] : ["proposed"], target);
  const proposal: GoalProposal = {
    ...current,
    status: target,
    decisionActorId: input.actorId ?? context.actorId,
    decisionAt: context.occurredAt,
    decisionReason: input.reason,
    updatedAt: context.occurredAt,
  };
  const revision = {
    ...toRevision(proposal, target, context.occurredAt),
    revisionId: current.revisionId,
  };
  return {
    proposal,
    revision,
    transition: transition(current, current.status, target, context, input.reason),
  };
}

export function confirmProposal(
  current: GoalProposal,
  input: ConfirmGoalProposalCommand,
  context: ProposalTransitionContext,
): ProposalRevisionState {
  assertExpectedProposal(current, input.expectedProposalVersion, input.expectedContentHash);
  ensureStatus(current, ["proposed"], "confirm");
  const proposal: GoalProposal = {
    ...current,
    status: "confirmed",
    decisionActorId: input.actorId ?? context.actorId,
    decisionAt: context.occurredAt,
    updatedAt: context.occurredAt,
  };
  const revision = {
    ...toRevision(proposal, "confirmed", context.occurredAt),
    revisionId: current.revisionId,
  };
  return {
    proposal,
    revision,
    transition: transition(current, "proposed", "confirmed", context),
  };
}

export function transition(
  proposal: GoalProposal,
  fromStatus: GoalProposalStatus,
  toStatus: GoalProposalStatus,
  context: ProposalTransitionContext,
  reason?: string,
): GoalProposalTransition {
  return {
    transitionId: context.transitionId,
    proposalId: proposal.proposalId,
    projectId: proposal.projectId,
    userId: proposal.createdBy,
    fromStatus,
    toStatus,
    proposalVersion: proposal.proposalVersion,
    contentHash: proposal.contentHash,
    actorId: context.actorId,
    correlationId: context.correlationId,
    ...(reason === undefined ? {} : { reason }),
    idempotencyKey: context.idempotencyKey,
    occurredAt: context.occurredAt,
  };
}

export function assertExpectedProposal(
  proposal: GoalProposal,
  expectedVersion: number,
  expectedHash: string,
): void {
  if (proposal.proposalVersion !== expectedVersion || proposal.contentHash !== expectedHash) {
    throw new NovelCommandError("conflict", "Proposal version or content hash is stale.");
  }
}

function ensureStatus(
  proposal: GoalProposal,
  allowed: readonly GoalProposalStatus[],
  operation: string,
): void {
  if (!allowed.includes(proposal.status)) {
    throw new NovelCommandError(
      "invalid_transition",
      `Proposal cannot ${operation} from ${proposal.status}.`,
    );
  }
}

function toRevision(
  proposal: GoalProposal,
  status: GoalProposalStatus,
  occurredAt: string,
): GoalProposalRevision {
  return {
    revisionId: proposal.revisionId,
    proposalId: proposal.proposalId,
    projectId: proposal.projectId,
    userId: proposal.createdBy,
    proposalVersion: proposal.proposalVersion,
    intent: proposal.intent,
    chapterNumber: proposal.chapterNumber,
    chapterBrief: proposal.chapterBrief,
    acceptanceCriteria: clone(proposal.acceptanceCriteria),
    executionMode: proposal.executionMode,
    sourceMessageIds: clone(proposal.sourceMessageIds),
    contentHash: proposal.contentHash,
    status,
    createdBy: proposal.createdBy,
    createdAt: occurredAt,
    ...(proposal.decisionActorId === undefined ? {} : { decisionActorId: proposal.decisionActorId }),
    ...(proposal.decisionAt === undefined ? {} : { decisionAt: proposal.decisionAt }),
    ...(proposal.decisionReason === undefined ? {} : { decisionReason: proposal.decisionReason }),
  };
}
