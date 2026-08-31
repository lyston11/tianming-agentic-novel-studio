import { createHash } from "node:crypto";

export type Id = string;
export type ProjectId = Id;
export type UserId = Id;
export type ConversationId = Id;
export type CorrelationId = Id;
export type IdempotencyKey = string;

export interface ActorScope {
  readonly userId: UserId;
  readonly projectId: ProjectId;
}

export interface SourceReference {
  readonly sourceId: Id;
  readonly sourceType: "conversation_turn" | "canon" | "character_state" | "foreshadow" | "resource";
  readonly version: number;
}

export interface VersionVector {
  readonly goalRevision: number;
  readonly canon: number;
  readonly modelProfile: string;
  readonly styleResource: string;
}

export interface CharacterState {
  readonly characterId: Id;
  readonly name: string;
  readonly summary: string;
  readonly status: "active" | "missing" | "deceased";
  readonly version: number;
  readonly updatedFromCandidateId?: Id;
  readonly updatedFromContextPackageHash?: string;
}

export interface ForeshadowEntry {
  readonly entryId: Id;
  readonly label: string;
  readonly description: string;
  readonly status: "open" | "resolved";
  readonly version: number;
  readonly updatedFromCandidateId?: Id;
  readonly updatedFromContextPackageHash?: string;
}

export interface NovelProjectSnapshot {
  readonly projectId: ProjectId;
  readonly userId: UserId;
  readonly canonSnapshot: string;
  readonly characterStates: readonly CharacterState[];
  readonly foreshadowEntries: readonly ForeshadowEntry[];
  readonly sourceReferences: readonly SourceReference[];
  readonly versionVector: VersionVector;
  readonly previousChapterNumber: number;
}

export interface ConversationTurn {
  readonly conversationId: ConversationId;
  readonly projectId: ProjectId;
  readonly userId: UserId;
  readonly message: string;
  readonly correlationId: CorrelationId;
  readonly idempotencyKey?: IdempotencyKey;
  readonly sessionId?: Id;
}

export type ConversationStatus = "active" | "archived";
export type ConversationTurnStatus = "running" | "completed" | "failed" | "aborted";
export type ConversationMessageRole = "user" | "assistant" | "tool" | "tool_result";

/** Durable conversation identity owned by the Application layer. */
export interface ConversationRecord {
  readonly conversationId: ConversationId;
  readonly projectId: ProjectId;
  readonly userId: UserId;
  readonly sessionId: Id;
  readonly status: ConversationStatus;
  readonly currentTurnSequence: number;
  readonly createdAt: string;
  readonly updatedAt: string;
}

/** Append-only user turn plus its durable run/result provenance. */
export interface ConversationTurnRecord {
  readonly turnId: Id;
  readonly conversationId: ConversationId;
  readonly projectId: ProjectId;
  readonly userId: UserId;
  readonly sessionId: Id;
  readonly sequence: number;
  readonly message: string;
  readonly inputHash: string;
  readonly idempotencyKey: IdempotencyKey;
  readonly correlationId: CorrelationId;
  readonly causationId?: Id;
  readonly sourceMessageIds: readonly Id[];
  readonly assistantMessageId?: Id;
  readonly runtimeRunId?: Id;
  readonly proposalId?: Id;
  readonly runReason?: "natural_stop" | "aborted" | "max_turns_exceeded" | "error";
  readonly status: ConversationTurnStatus;
  readonly errorCode?: NovelCommandError["code"];
  readonly createdAt: string;
  readonly occurredAt: string;
}

export interface ConversationMessageRecord {
  readonly messageId: Id;
  readonly conversationId: ConversationId;
  readonly turnId: Id;
  readonly projectId: ProjectId;
  readonly userId: UserId;
  readonly role: ConversationMessageRole;
  readonly content: string;
  readonly sourceMessageIds: readonly Id[];
  readonly runId?: Id;
  readonly toolName?: string;
  readonly createdAt: string;
  readonly occurredAt: string;
}

export type RuntimeRunStatus = "running" | "completed" | "failed" | "aborted" | "max_turns_exceeded";

export interface RuntimeRunRecord {
  readonly runId: Id;
  readonly conversationId: ConversationId;
  readonly turnId: Id;
  readonly projectId: ProjectId;
  readonly userId: UserId;
  readonly runtime: string;
  readonly modelProfile: string;
  readonly status: RuntimeRunStatus;
  readonly checkpointId?: Id;
  readonly errorCode?: NovelCommandError["code"];
  readonly errorMessage?: string;
  readonly createdAt: string;
  readonly startedAt: string;
  readonly finishedAt?: string;
}

/** A checkpoint reference is execution metadata, never business state. */
export interface RuntimeCheckpointRecord {
  readonly checkpointId: Id;
  readonly runId: Id;
  readonly conversationId: ConversationId;
  readonly turnId: Id;
  readonly projectId: ProjectId;
  readonly userId: UserId;
  readonly runtime: string;
  readonly reference: string;
  readonly stateHash: string;
  readonly version: number;
  readonly recoverable: false;
  readonly createdAt: string;
}

export type GoalIntent = "write_chapter_candidate";
export type ExecutionMode = "interactive_batch";

export type GoalProposalStatus =
  | "draft"
  | "proposed"
  | "confirmed"
  | "rejected"
  | "superseded"
  | "discarded";

export interface GoalProposal {
  readonly proposalId: Id;
  readonly projectId: ProjectId;
  readonly conversationId: ConversationId;
  readonly createdBy: UserId;
  readonly intent: GoalIntent;
  readonly chapterNumber: number;
  readonly chapterBrief: string;
  readonly acceptanceCriteria: readonly string[];
  readonly executionMode: ExecutionMode;
  readonly requiresConfirmation: true;
  readonly sourceMessageIds: readonly Id[];
  readonly proposalVersion: number;
  readonly contentHash: string;
  readonly status: GoalProposalStatus;
  readonly revisionId: Id;
  readonly decisionActorId?: Id;
  readonly decisionAt?: string;
  readonly decisionReason?: string;
  readonly goalId?: Id;
  readonly goalRevisionId?: Id;
  readonly createdAt: string;
  readonly updatedAt: string;
}

/** Fields covered by the proposal content hash; lifecycle metadata is excluded. */
export interface GoalProposalContent {
  readonly projectId: ProjectId;
  readonly conversationId: ConversationId;
  readonly createdBy: UserId;
  readonly intent: GoalIntent;
  readonly chapterNumber: number;
  readonly chapterBrief: string;
  readonly acceptanceCriteria: readonly string[];
  readonly executionMode: ExecutionMode;
  readonly requiresConfirmation: true;
  readonly sourceMessageIds: readonly Id[];
  readonly proposalVersion: number;
}

export interface GoalProposalRevision {
  readonly revisionId: Id;
  readonly proposalId: Id;
  readonly projectId: ProjectId;
  readonly userId: UserId;
  readonly proposalVersion: number;
  readonly intent: GoalIntent;
  readonly chapterNumber: number;
  readonly chapterBrief: string;
  readonly acceptanceCriteria: readonly string[];
  readonly executionMode: ExecutionMode;
  readonly sourceMessageIds: readonly Id[];
  readonly contentHash: string;
  readonly status: GoalProposalStatus;
  readonly createdBy: UserId;
  readonly createdAt: string;
  readonly decisionActorId?: Id;
  readonly decisionAt?: string;
  readonly decisionReason?: string;
}

export type ProposalRevision = GoalProposalRevision;

export interface GoalProposalTransition {
  readonly transitionId: Id;
  readonly proposalId: Id;
  readonly projectId: ProjectId;
  readonly userId: UserId;
  readonly fromStatus: GoalProposalStatus;
  readonly toStatus: GoalProposalStatus;
  readonly proposalVersion: number;
  readonly contentHash: string;
  readonly actorId: Id;
  readonly correlationId: CorrelationId;
  readonly causationId?: Id;
  readonly idempotencyKey: IdempotencyKey;
  readonly reason?: string;
  readonly occurredAt: string;
}

export interface GoalCommitIntent {
  readonly intentId: Id;
  readonly proposalId: Id;
  readonly proposalVersion: number;
  readonly proposalContentHash: string;
  readonly projectId: ProjectId;
  readonly userId: UserId;
  readonly actorId: Id;
  readonly correlationId: CorrelationId;
  readonly causationId?: Id;
  readonly idempotencyKey: IdempotencyKey;
  readonly status: "confirmed";
  readonly createdAt: string;
}

export type GoalStatus = "proposed" | "confirmed" | "superseded" | "cancelled";
export type ProductionStatus =
  | "running"
  | "waiting"
  | "paused"
  | "blocked"
  | "completed"
  | "failed"
  | "cancelled"
  | "awaiting_acceptance";
export type BatchStatus = ProductionStatus;
export type TaskStatus = "queued" | "running" | "awaiting_acceptance" | "completed" | "blocked" | "failed";

export interface Goal {
  readonly goalId: Id;
  readonly projectId: ProjectId;
  readonly userId: UserId;
  readonly status: GoalStatus;
  readonly proposalId: Id;
  readonly currentRevision: number;
  readonly createdAt: string;
}

export interface GoalRevision {
  readonly goalRevisionId: Id;
  readonly goalId: Id;
  readonly projectId: ProjectId;
  readonly userId: UserId;
  readonly revision: number;
  readonly chapterNumber: number;
  readonly chapterBrief: string;
  readonly acceptanceCriteria: readonly string[];
  readonly sourceMessageIds: readonly Id[];
  readonly proposalContentHash: string;
  readonly createdAt: string;
}

export interface Production {
  readonly productionId: Id;
  readonly goalId: Id;
  readonly goalRevisionId: Id;
  readonly projectId: ProjectId;
  readonly userId: UserId;
  readonly status: ProductionStatus;
  readonly currentBatchId: Id;
  readonly createdAt: string;
}

export interface Batch {
  readonly batchId: Id;
  readonly productionId: Id;
  readonly goalRevisionId: Id;
  readonly projectId: ProjectId;
  readonly userId: UserId;
  readonly status: BatchStatus;
  readonly createdAt: string;
}

export interface Task {
  readonly taskId: Id;
  readonly batchId: Id;
  readonly productionId: Id;
  readonly goalRevisionId: Id;
  readonly projectId: ProjectId;
  readonly userId: UserId;
  readonly chapterNumber: number;
  readonly status: TaskStatus;
  readonly taskVersion: number;
  readonly createdAt: string;
}

export interface NovelContextPackage {
  readonly contextPackageId: Id;
  readonly goalRevisionId: Id;
  readonly taskId: Id;
  readonly projectId: ProjectId;
  readonly userId: UserId;
  readonly canonSnapshot: string;
  readonly characterStates: readonly CharacterState[];
  readonly foreshadowEntries: readonly ForeshadowEntry[];
  readonly sourceReferences: readonly SourceReference[];
  readonly versionVector: VersionVector;
  readonly contentHash: string;
  readonly createdAt: string;
}

export interface ChapterCandidateIntent {
  readonly taskId: Id;
  readonly projectId: ProjectId;
  readonly userId: UserId;
  readonly chapterNumber: number;
  readonly title: string;
  readonly body: string;
  readonly contextPackageHash: string;
  readonly modelProfile: string;
  readonly referencedCharacterIds: readonly Id[];
  readonly updatedForeshadowEntryIds: readonly Id[];
  readonly characterUpdates: readonly CharacterUpdateIntent[];
  readonly foreshadowUpdates: readonly ForeshadowUpdateIntent[];
}

export interface CharacterUpdateIntent {
  readonly characterId: Id;
  readonly summary: string;
  readonly status: "active" | "missing" | "deceased";
}

export interface ForeshadowUpdateIntent {
  readonly entryId: Id;
  readonly status: "open" | "resolved";
  readonly description: string;
}

export type CandidateStatus = "generated" | "awaiting_acceptance" | "blocked" | "rejected" | "merged";

export interface CandidateChapter {
  readonly candidateId: Id;
  readonly taskId: Id;
  readonly projectId: ProjectId;
  readonly userId: UserId;
  readonly chapterNumber: number;
  readonly title: string;
  readonly body: string;
  readonly contextPackageHash: string;
  readonly modelProfile: string;
  readonly candidateVersion: number;
  readonly status: CandidateStatus;
  readonly referencedCharacterIds: readonly Id[];
  readonly updatedForeshadowEntryIds: readonly Id[];
  readonly characterUpdates: readonly CharacterUpdateIntent[];
  readonly foreshadowUpdates: readonly ForeshadowUpdateIntent[];
  readonly createdAt: string;
}

export interface ReviewFinding {
  readonly code: string;
  readonly message: string;
}

export interface Review {
  readonly reviewId: Id;
  readonly candidateId: Id;
  readonly projectId: ProjectId;
  readonly userId: UserId;
  readonly gateName: "continuity";
  readonly passed: boolean;
  readonly findings: readonly ReviewFinding[];
  readonly reviewVersion: number;
  readonly createdAt: string;
}

export type AcceptanceDecision = "accepted" | "rejected";

export interface Acceptance {
  readonly acceptanceId: Id;
  readonly candidateId: Id;
  readonly projectId: ProjectId;
  readonly userId: UserId;
  readonly actorId: Id;
  readonly decision: AcceptanceDecision;
  readonly expectedCandidateVersion: number;
  readonly contextPackageHash: string;
  readonly idempotencyKey: IdempotencyKey;
  readonly createdAt: string;
}

export interface CanonicalChapter {
  readonly canonicalChapterId: Id;
  readonly projectId: ProjectId;
  readonly userId: UserId;
  readonly candidateId: Id;
  readonly candidateVersion: number;
  readonly chapterNumber: number;
  readonly title: string;
  readonly body: string;
  readonly contextPackageHash: string;
  readonly createdAt: string;
}

export interface CanonMergeResult {
  readonly canonicalChapter: CanonicalChapter;
  readonly characterStates: readonly CharacterState[];
  readonly foreshadowEntries: readonly ForeshadowEntry[];
  readonly projectionVersion: number;
}

export interface AcceptanceResult {
  readonly acceptance: Acceptance;
  readonly canonMerge: CanonMergeResult | null;
}

export interface GoalCommitResult {
  readonly goal: Goal;
  readonly goalRevision: GoalRevision;
  readonly production: Production;
  readonly batch: Batch;
  readonly task: Task;
  readonly contextPackage: NovelContextPackage;
}

export interface WorkflowProjection {
  readonly projectId: ProjectId;
  readonly userId: UserId;
  readonly proposal: GoalProposal | null;
  readonly proposals: readonly GoalProposal[];
  readonly goal: Goal | null;
  readonly goalRevision: GoalRevision | null;
  readonly production: Production | null;
  readonly batch: Batch | null;
  readonly task: Task | null;
  readonly contextPackage: NovelContextPackage | null;
  readonly candidate: CandidateChapter | null;
  readonly review: Review | null;
  readonly acceptance: Acceptance | null;
  readonly canonicalChapter: CanonicalChapter | null;
  readonly characterStates: readonly CharacterState[];
  readonly foreshadowEntries: readonly ForeshadowEntry[];
  readonly projectionVersion: number;
  readonly sourceCandidateId: Id | null;
  readonly sourceCandidateVersion: number | null;
  readonly sourceContextPackageHash: string | null;
}

export type RuntimeEventType =
  | "agent_start"
  | "agent_end"
  | "turn_start"
  | "turn_end"
  | "message_start"
  | "message_update"
  | "message_end"
  | "tool_execution_start"
  | "tool_execution_update"
  | "tool_execution_end";

export interface DurableRuntimeMessage {
  readonly messageId: Id;
  readonly runId: Id;
  readonly taskId: Id;
  readonly projectId: ProjectId;
  readonly userId: UserId;
  readonly sequence: number;
  readonly eventType: RuntimeEventType;
  readonly payload: unknown;
  readonly correlationId: CorrelationId;
  readonly occurredAt: string;
}

export interface ContextRequest {
  readonly actor: ActorScope;
  readonly correlationId: CorrelationId;
}

export interface FreezeContextRequest extends ContextRequest {
  readonly goalRevisionId: Id;
  readonly taskId: Id;
}

export interface ProposeGoalInput extends ContextRequest {
  readonly conversationId: ConversationId;
  readonly intent: GoalIntent;
  readonly chapterNumber: number;
  readonly chapterBrief: string;
  readonly acceptanceCriteria: readonly string[];
  readonly executionMode: ExecutionMode;
  readonly sourceMessageIds: readonly Id[];
  readonly idempotencyKey: IdempotencyKey;
}

export interface ConfirmGoalProposalCommand extends ContextRequest {
  readonly proposalId: Id;
  readonly expectedProposalVersion: number;
  readonly expectedContentHash: string;
  readonly actorId?: Id;
  readonly idempotencyKey: IdempotencyKey;
}

export interface ReviseProposalCommand extends ContextRequest {
  readonly proposalId: Id;
  readonly expectedProposalVersion: number;
  readonly expectedContentHash: string;
  readonly content: Omit<GoalProposalContent, "projectId" | "conversationId" | "createdBy" | "proposalVersion" | "sourceMessageIds">;
  readonly sourceMessageIds: readonly Id[];
  readonly idempotencyKey: IdempotencyKey;
}

export interface DecideProposalCommand extends ContextRequest {
  readonly proposalId: Id;
  readonly expectedProposalVersion: number;
  readonly expectedContentHash: string;
  readonly reason: string;
  readonly actorId?: Id;
  readonly idempotencyKey: IdempotencyKey;
}

export interface ConfirmGoalCommand extends ContextRequest {
  readonly proposalId: Id;
  readonly idempotencyKey: IdempotencyKey;
  readonly expectedProposalVersion?: number;
  readonly expectedContentHash?: string;
  readonly actorId?: Id;
}

export interface RequestChapterCommand extends ContextRequest {
  readonly taskId: Id;
  readonly intent: ChapterCandidateIntent;
}

export interface AcceptCandidateCommand extends ContextRequest {
  readonly candidateId: Id;
  readonly actorId: Id;
  readonly decision: AcceptanceDecision;
  readonly expectedCandidateVersion: number;
  readonly contextPackageHash: string;
  readonly idempotencyKey: IdempotencyKey;
}

export interface AppendTurn {
  readonly actor: ActorScope;
  readonly conversationId: ConversationId;
  readonly sessionId: Id;
  readonly message: string;
  readonly idempotencyKey: IdempotencyKey;
  readonly correlationId: CorrelationId;
  readonly sourceMessageIds?: readonly Id[];
  readonly inputHash?: string;
}

export interface CompleteConversationTurn {
  readonly actor: ActorScope;
  readonly turnId: Id;
  readonly status: ConversationTurnStatus;
  readonly runReason?: "natural_stop" | "aborted" | "max_turns_exceeded" | "error";
  readonly assistantMessageId?: Id;
  readonly runtimeRunId?: Id;
  readonly proposalId?: Id;
  readonly errorCode?: NovelCommandError["code"];
}

export interface AppendMessage {
  readonly actor: ActorScope;
  readonly conversationId: ConversationId;
  readonly turnId: Id;
  readonly messageId: Id;
  readonly role: ConversationMessageRole;
  readonly content: string;
  readonly sourceMessageIds?: readonly Id[];
  readonly runId?: Id;
  readonly toolName?: string;
  readonly occurredAt?: string;
}

export interface ConversationQuery {
  readonly actor: ActorScope;
  readonly conversationId: ConversationId;
}

export interface QueryWorkflowRequest {
  readonly actor: ActorScope;
}

export class NovelCommandError extends Error {
  public readonly code:
    | "not_found"
    | "forbidden"
    | "invalid_argument"
    | "invalid_transition"
    | "conflict"
    | "model_error"
    | "aborted"
    | "cancelled";

  public constructor(
    code: NovelCommandError["code"],
    message: string,
  ) {
    super(message);
    this.name = "NovelCommandError";
    this.code = code;
  }
}

export function canonicalJson(value: unknown): string {
  return JSON.stringify(sortCanonical(value));
}

export function sha256(value: unknown): string {
  return createHash("sha256").update(canonicalJson(value)).digest("hex");
}

export function proposalContentHash(input: GoalProposalContent): string {
  return sha256(input);
}

export function contextPackageContentHash(input: Omit<NovelContextPackage, "contentHash" | "createdAt" | "contextPackageId">): string {
  return sha256(input);
}

function sortCanonical(value: unknown): unknown {
  if (Array.isArray(value)) return value.map(sortCanonical);
  if (value !== null && typeof value === "object") {
    const sorted: Record<string, unknown> = {};
    for (const key of Object.keys(value as Record<string, unknown>).sort()) {
      const item = (value as Record<string, unknown>)[key];
      if (item !== undefined) sorted[key] = sortCanonical(item);
    }
    return sorted;
  }
  return value;
}

export function clone<T>(value: T): T {
  return JSON.parse(JSON.stringify(value)) as T;
}
