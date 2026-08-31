import type {
  AcceptCandidateCommand,
  Acceptance,
  AcceptanceResult,
  ActorScope,
  AppendMessage,
  AppendTurn,
  Batch,
  CandidateChapter,
  ChapterCandidateIntent,
  CompleteConversationTurn,
  ConfirmGoalProposalCommand,
  ConfirmGoalCommand,
  ConversationMessageRecord,
  ConversationRecord,
  ConversationQuery,
  ConversationTurnRecord,
  ContextRequest,
  ConversationTurn,
  FreezeContextRequest,
  Goal,
  GoalCommitIntent,
  GoalCommitResult,
  GoalProposal,
  GoalProposalRevision,
  GoalProposalTransition,
  GoalRevision,
  NovelContextPackage,
  NovelProjectSnapshot,
  Production,
  ReviseProposalCommand,
  DecideProposalCommand,
  Review,
  RuntimeCheckpointRecord,
  RuntimeRunRecord,
  Task,
  WorkflowProjection,
  CanonMergeResult,
  Id,
  ProjectId,
  UserId,
} from "./contracts.js";

export interface NovelProjectPort {
  createProject(input: {
    projectId: ProjectId;
    userId: UserId;
    canonSnapshot?: string;
    characterStates?: readonly NovelProjectSnapshot["characterStates"][number][];
    foreshadowEntries?: readonly NovelProjectSnapshot["foreshadowEntries"][number][];
  }): Promise<NovelProjectSnapshot>;
}

export interface NovelContextPort {
  loadForConversation(input: ContextRequest): Promise<NovelProjectSnapshot>;
  freezeForChapter(input: FreezeContextRequest): Promise<NovelContextPackage>;
}

export interface ProposalPort {
  saveProposal(proposal: GoalProposal, idempotencyKey: string): Promise<GoalProposal>;
  getProposal(proposalId: Id, actor: ActorScope): Promise<GoalProposal | null>;
  findProposalByIdempotency(actor: ActorScope, idempotencyKey: string): Promise<GoalProposal | null>;
}

/**
 * Application-owned proposal lifecycle. The confirmation result is an intent
 * for the durable control plane; it is deliberately not a Goal/Production
 * commit and cannot start a model or worker.
 */
export interface GoalProposalCommands {
  propose(input: import("./contracts.js").ProposeGoalInput): Promise<GoalProposal>;
  revise(input: ReviseProposalCommand): Promise<GoalProposal>;
  reject(input: DecideProposalCommand): Promise<GoalProposal>;
  discard(input: DecideProposalCommand): Promise<GoalProposal>;
  confirmProposal(input: ConfirmGoalProposalCommand): Promise<GoalCommitIntent>;
  listProposals(actor: ActorScope): Promise<readonly GoalProposal[]>;
  listProposalRevisions(proposalId: Id, actor: ActorScope): Promise<readonly GoalProposalRevision[]>;
  listProposalTransitions(proposalId: Id, actor: ActorScope): Promise<readonly GoalProposalTransition[]>;
  getConfirmationIntent(actor: ActorScope, idempotencyKey: string): Promise<GoalCommitIntent | null>;
}

/** Narrow durable boundary for Conversation/Turn/runtime provenance. */
export interface ConversationStore {
  ensureConversation(input: { actor: ActorScope; conversationId: Id; sessionId: Id }): Promise<ConversationRecord>;
  appendTurn(input: AppendTurn): Promise<ConversationTurnRecord>;
  completeTurn(input: CompleteConversationTurn): Promise<ConversationTurnRecord>;
  appendMessage(input: AppendMessage): Promise<ConversationMessageRecord>;
  saveRun(input: RuntimeRunRecord): Promise<RuntimeRunRecord>;
  saveCheckpoint(input: RuntimeCheckpointRecord): Promise<RuntimeCheckpointRecord>;
  findTurnByIdempotency(actor: ActorScope, idempotencyKey: string): Promise<ConversationTurnRecord | null>;
  getConversation(input: ConversationQuery): Promise<ConversationRecord | null>;
  listTurns(input: ConversationQuery): Promise<readonly ConversationTurnRecord[]>;
  listMessages(input: ConversationQuery): Promise<readonly ConversationMessageRecord[]>;
  getRun(runId: Id, actor: ActorScope): Promise<RuntimeRunRecord | null>;
  listCheckpoints(runId: Id, actor: ActorScope): Promise<readonly RuntimeCheckpointRecord[]>;
}

export interface ProductionCommandPort {
  confirmGoal(input: ConfirmGoalCommand): Promise<GoalCommitResult>;
  acceptCandidate(input: AcceptCandidateCommand): Promise<AcceptanceResult>;
}

export interface CandidatePort {
  saveCandidate(candidate: CandidateChapter): Promise<CandidateChapter>;
  getCandidate(candidateId: Id, actor: ActorScope): Promise<CandidateChapter | null>;
  saveReview(review: Review): Promise<Review>;
  getReview(candidateId: Id, actor: ActorScope): Promise<Review | null>;
  submitCandidateIntent(input: ChapterCandidateIntent): Promise<CandidateChapter>;
}

export interface TaskCommandPort {
  markTaskFailed(input: {
    actor: ActorScope;
    taskId: Id;
    correlationId: string;
  }): Promise<Task>;
}

export interface WorkflowQueryPort {
  get(request: { actor: ActorScope }): Promise<WorkflowProjection>;
}

export interface RuntimeEventSink {
  append(message: import("./contracts.js").DurableRuntimeMessage): Promise<void>;
}

export interface CanonPort {
  mergeAcceptedCandidate(input: {
    actor: ActorScope;
    candidate: CandidateChapter;
    contextPackage: NovelContextPackage;
    acceptance: Acceptance;
  }): Promise<CanonMergeResult>;
}

/**
 * Application-facing aggregate port. A real Web host can implement these
 * interfaces with a transaction/Outbox adapter without changing Novel Agent.
 */
export interface NovelApplicationPorts
  extends NovelProjectPort,
    NovelContextPort,
    ProposalPort,
    GoalProposalCommands,
    ProductionCommandPort,
    CandidatePort,
    TaskCommandPort,
    WorkflowQueryPort,
    CanonPort {
  getGoal(goalId: Id, actor: ActorScope): Promise<Goal | null>;
  getGoalRevision(goalRevisionId: Id, actor: ActorScope): Promise<GoalRevision | null>;
  getProduction(productionId: Id, actor: ActorScope): Promise<Production | null>;
  getBatch(batchId: Id, actor: ActorScope): Promise<Batch | null>;
  getTask(taskId: Id, actor: ActorScope): Promise<Task | null>;
}

export interface StoredAcceptance {
  readonly acceptance: Acceptance;
  readonly result: AcceptanceResult;
}
