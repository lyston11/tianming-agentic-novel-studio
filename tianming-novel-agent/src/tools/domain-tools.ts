import { Type } from "@sinclair/typebox";
import type { AgentCoreTool, ToolExecutionOptions } from "@tianming/agent-core";
import {
  NovelCommandError,
  clone,
  type ActorScope,
  type CandidateChapter,
  type ChapterCandidateIntent,
  type ContextRequest,
  type GoalProposal,
  type NovelContextPackage,
} from "../contracts.js";
import type { NovelContextPort, ProposalPort, CandidatePort } from "../ports.js";

const projectScopeSchema = Type.Object({
  projectId: Type.String({ minLength: 1 }),
});

const proposalInputSchema = Type.Object({
  projectId: Type.String({ minLength: 1 }),
  conversationId: Type.String({ minLength: 1 }),
  chapterNumber: Type.Integer({ minimum: 1 }),
  chapterBrief: Type.String({ minLength: 1 }),
  acceptanceCriteria: Type.Array(Type.String({ minLength: 1 }), { minItems: 1 }),
  sourceMessageIds: Type.Array(Type.String({ minLength: 1 }), { minItems: 1 }),
  idempotencyKey: Type.String({ minLength: 1 }),
});

const chapterIntentSchema = Type.Object({
  taskId: Type.String({ minLength: 1 }),
  projectId: Type.String({ minLength: 1 }),
  userId: Type.String({ minLength: 1 }),
  chapterNumber: Type.Integer({ minimum: 1 }),
  title: Type.String({ minLength: 1 }),
  body: Type.String({ minLength: 1 }),
  contextPackageHash: Type.String({ minLength: 1 }),
  modelProfile: Type.String({ minLength: 1 }),
  referencedCharacterIds: Type.Array(Type.String()),
  updatedForeshadowEntryIds: Type.Array(Type.String()),
  characterUpdates: Type.Array(Type.Object({
    characterId: Type.String({ minLength: 1 }),
    summary: Type.String({ minLength: 1 }),
    status: Type.Union([Type.Literal("active"), Type.Literal("missing"), Type.Literal("deceased")]),
  })),
  foreshadowUpdates: Type.Array(Type.Object({
    entryId: Type.String({ minLength: 1 }),
    status: Type.Union([Type.Literal("open"), Type.Literal("resolved")]),
    description: Type.String({ minLength: 1 }),
  })),
});

export interface DomainToolDependencies {
  readonly actor: ActorScope;
  readonly correlationId: string;
  readonly contextPort: NovelContextPort;
  readonly proposalPort: ProposalPort;
  readonly candidatePort: CandidatePort;
  readonly proposal?: GoalProposal;
  readonly contextPackage?: NovelContextPackage;
  readonly onProposalSubmissionError?: (error: unknown) => void;
}

export function createReadContextTool(dependencies: DomainToolDependencies): AgentCoreTool {
  return {
    name: "read_context",
    description: "Read the current project context for the authorized actor.",
    parameters: projectScopeSchema,
    execute: async (args: unknown) => {
      const input = args as { projectId: string };
      assertActorProject(dependencies.actor, input.projectId);
      if (dependencies.contextPackage) {
        return clone(dependencies.contextPackage);
      }
      return dependencies.contextPort.loadForConversation({
        actor: dependencies.actor,
        correlationId: dependencies.correlationId,
      } satisfies ContextRequest);
    },
  };
}

export function createProposeGoalTool(dependencies: DomainToolDependencies): AgentCoreTool {
  return {
    name: "propose_goal",
    description: "Submit a proposal-only intent; this does not create executable production state.",
    parameters: proposalInputSchema,
    execute: async (args: unknown) => {
      const input = args as {
        projectId: string;
        conversationId: string;
        chapterNumber: number;
        chapterBrief: string;
        acceptanceCriteria: string[];
        sourceMessageIds: string[];
        idempotencyKey: string;
      };
      assertActorProject(dependencies.actor, input.projectId);
      try {
        return await dependencies.proposalPort.submitProposalIntent({
          actor: dependencies.actor,
          correlationId: dependencies.correlationId,
          conversationId: input.conversationId,
          intent: "write_chapter_candidate",
          chapterNumber: input.chapterNumber,
          chapterBrief: input.chapterBrief,
          acceptanceCriteria: input.acceptanceCriteria,
          executionMode: "interactive_batch",
          sourceMessageIds: input.sourceMessageIds,
          idempotencyKey: input.idempotencyKey,
        });
      } catch (error) {
        dependencies.onProposalSubmissionError?.(error);
        throw error;
      }
    },
  };
}

export function createRequestChapterTool(dependencies: DomainToolDependencies): AgentCoreTool {
  return {
    name: "request_chapter",
    description: "Submit a candidate chapter intent for Application validation and persistence.",
    parameters: chapterIntentSchema,
    execute: async (args: unknown, options: ToolExecutionOptions) => {
      const input = args as ChapterCandidateIntent;
      assertActorProject(dependencies.actor, input.projectId);
      if (input.userId !== dependencies.actor.userId) {
        throw new NovelCommandError("forbidden", "Candidate intent user scope does not match actor.");
      }
      if (options.signal.aborted) throw new NovelCommandError("aborted", "Chapter request was aborted.");
      return dependencies.candidatePort.submitCandidateIntent(input);
    },
  };
}

export function createChapterWriterTools(dependencies: DomainToolDependencies): AgentCoreTool[] {
  return [createReadContextTool(dependencies), createRequestChapterTool(dependencies)];
}

function assertActorProject(actor: ActorScope, projectId: string): void {
  if (actor.projectId !== projectId) {
    throw new NovelCommandError("forbidden", "Tool project scope does not match actor.");
  }
}

export function candidateIntentFromUnknown(args: unknown): ChapterCandidateIntent {
  return args as ChapterCandidateIntent;
}

export function isCandidate(value: unknown): value is CandidateChapter {
  return typeof value === "object" && value !== null && "candidateId" in value;
}
