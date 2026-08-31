/**
 * @tianming/agent-core — generic agent loop contracts.
 *
 * This layer knows nothing about the novel domain, ASP.NET, PostgreSQL,
 * React, or Web APIs. It depends only on @tianming/agent-ai and TypeBox.
 */
import type { TSchema } from "@sinclair/typebox";
import type {
  AssistantMessage,
  AssistantMessageEvent,
  Message,
  Model,
  Api,
  StreamFunction,
  SimpleStreamOptions,
  ToolResultMessage,
} from "@tianming/agent-ai";

export type {
  AssistantMessage,
  AssistantMessageEvent,
  AssistantMessageEventStream,
  Context,
  Message,
  Model,
  ToolResultMessage,
  UserMessage,
} from "@tianming/agent-ai";

/**
 * A core-level tool: name/description/schema plus an execute function.
 * The core does not understand project, chapter, Goal, or Canon semantics;
 * tools submit intents and return plain results to the model.
 */
export interface AgentCoreTool {
  name: string;
  description: string;
  parameters: TSchema;
  execute(args: unknown, options: ToolExecutionOptions): Promise<unknown> | unknown;
}

export interface ToolExecutionOptions {
  signal: AbortSignal;
  /** Report progress while executing; forwarded as tool_execution_update events. */
  onUpdate(payload?: unknown): void;
}

export type AgentStopReason = "natural_stop" | "aborted" | "max_turns_exceeded" | "error";

/**
 * Observation surface of one run. Events are NOT durable truth; hosts may
 * listen and persist messages themselves.
 *
 * Stable order for a tool turn:
 *   agent_start → turn_start → message_start → message_update* → message_end
 *   → (per tool call) tool_execution_start → tool_execution_update* → tool_execution_end
 *   → message_start(toolResult) → message_end(toolResult) → turn_end → …
 * Natural stop ends with agent_end and no further turns.
 */
export type AgentCoreEvent =
  | { type: "agent_start" }
  | { type: "agent_end"; reason: AgentStopReason }
  | { type: "turn_start"; turn: number }
  | { type: "turn_end"; turn: number }
  | { type: "message_start"; message: Message }
  | {
      type: "message_update";
      assistantMessageEvent: AssistantMessageEvent;
      partial: AssistantMessage;
    }
  | { type: "message_end"; message: Message }
  | {
      type: "tool_execution_start";
      toolCallId: string;
      toolName: string;
      args: Record<string, unknown>;
    }
  | { type: "tool_execution_update"; toolCallId: string; toolName: string; payload?: unknown }
  | {
      type: "tool_execution_end";
      toolCallId: string;
      toolName: string;
      result: unknown;
      isError: boolean;
    };

/** Generic loop state. Contains only what the loop needs — no domain fields. */
export interface AgentState {
  systemPrompt: string;
  model: Model<Api>;
  messages: Message[];
  tools: AgentCoreTool[];
  isRunning: boolean;
  streamMessage: AssistantMessage | null;
  error: string | null;
}

export interface AgentCoreOptions {
  systemPrompt: string;
  model: Model<Api>;
  tools?: AgentCoreTool[];
  messages?: Message[];
  /**
   * Model stream function. Defaults to the pi-ai streamSimple adapter from
   * @tianming/agent-ai; tests inject deterministic fakes here.
   */
  streamFn?: StreamFunction<Api, SimpleStreamOptions>;
  /** Optional context transform applied before each model call. Default: identity. */
  transformContext?: (messages: Message[]) => Message[] | Promise<Message[]>;
  /** Safety ceiling on model turns per run. Not a business stage sequence. Default: 25. */
  maxTurns?: number;
}

export interface PromptOptions {
  signal?: AbortSignal;
}

export interface RunResult {
  reason: AgentStopReason;
  turns: number;
}
