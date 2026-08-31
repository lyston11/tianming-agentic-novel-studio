/**
 * @tianming/agent-core — minimal generic agent loop.
 *
 * Natural stop condition (Pi-style): a run ends when the current assistant
 * response contains no tool calls and the steering/follow-up queues are
 * empty. maxTurns is only a safety ceiling against unbounded loops, never a
 * business stage sequence. Events are an observation surface, not durable
 * truth; persistence belongs to the host.
 */
import {
  streamSimple,
  validateToolArguments,
  type Api,
  type AssistantMessage,
  type AssistantMessageEventStream,
  type Context,
  type Message,
  type Model,
  type SimpleStreamOptions,
  type StreamFunction,
  type TextContent,
  type ToolCall,
  type ToolResultMessage,
  type UserMessage,
} from "@tianming/agent-ai";
import {
  type AgentCoreEvent,
  type AgentCoreOptions,
  type AgentState,
  type AgentStopReason,
  type PromptOptions,
  type RunResult,
  type ToolExecutionOptions,
} from "./types.js";

const DEFAULT_MAX_TURNS = 25;
/** Reason text recorded for tool calls skipped by an incoming steering message. */
export const STEERING_SKIPPED_TEXT = "Skipped due to queued user message.";
/** Reason text recorded for tool calls skipped because the run was aborted. */
export const ABORTED_SKIPPED_TEXT = "Aborted before execution.";

export class AgentCore {
  private readonly agentState: AgentState;
  private readonly streamFn: StreamFunction<Api, SimpleStreamOptions>;
  private readonly transformContext: (messages: Message[]) => Message[] | Promise<Message[]>;
  private readonly maxTurns: number;
  private readonly listeners = new Set<(event: AgentCoreEvent) => void>();
  private readonly steeringQueue: string[] = [];
  private readonly followUpQueue: string[] = [];
  private abortController: AbortController | null = null;

  constructor(options: AgentCoreOptions) {
    this.agentState = {
      systemPrompt: options.systemPrompt,
      model: options.model,
      messages: [...(options.messages ?? [])],
      tools: [...(options.tools ?? [])],
      isRunning: false,
      streamMessage: null,
      error: null,
    };
    this.streamFn = options.streamFn ?? streamSimple;
    this.transformContext = options.transformContext ?? ((messages) => messages);
    this.maxTurns = options.maxTurns ?? DEFAULT_MAX_TURNS;
  }

  /** Live loop state (same convention as the previous runtime host). */
  public get state(): AgentState {
    return this.agentState;
  }

  /**
   * Subscribe to loop events. Returns an unsubscribe function. Listeners are
   * invoked synchronously in stable emission order.
   */
  public subscribe(listener: (event: AgentCoreEvent) => void): () => void {
    this.listeners.add(listener);
    return () => {
      this.listeners.delete(listener);
    };
  }

  /** Queue a user steering message, consumed at the next tool-execution boundary. */
  public steer(text: string): void {
    this.steeringQueue.push(text);
  }

  /** Queue a follow-up message, consumed only at a natural stop point. */
  public followUp(text: string): void {
    this.followUpQueue.push(text);
  }

  /** Append a message to the context (emits message_start/message_end). */
  public appendMessage(message: Message): void {
    this.appendInternal(message);
  }

  /** Request abort of the active run; no-op when idle. */
  public abort(): void {
    this.abortController?.abort();
  }

  /** Clear messages, queues, stream state, and errors. Rejects while running. */
  public reset(): void {
    if (this.agentState.isRunning) {
      throw new Error("AgentCore.reset() cannot be called while a run is active");
    }
    this.agentState.messages = [];
    this.agentState.streamMessage = null;
    this.agentState.error = null;
    this.steeringQueue.length = 0;
    this.followUpQueue.length = 0;
  }

  /**
   * Run one prompt through the loop until natural stop, abort, max turns, or
   * model error. Completed messages are retained even when a run ends early.
   */
  public async prompt(text: string | UserMessage, options?: PromptOptions): Promise<RunResult> {
    if (this.agentState.isRunning) {
      throw new Error("AgentCore.prompt() called while a run is already active");
    }
    const userMessage: UserMessage =
      typeof text === "string"
        ? { role: "user", content: text, timestamp: Date.now() }
        : text;

    this.agentState.isRunning = true;
    this.agentState.error = null;
    this.agentState.streamMessage = null;

    const controller = new AbortController();
    this.abortController = controller;
    const externalSignal = options?.signal ?? null;
    const forwardExternalAbort = () => controller.abort();
    if (externalSignal) {
      if (externalSignal.aborted) {
        controller.abort();
      } else {
        externalSignal.addEventListener("abort", forwardExternalAbort, { once: true });
      }
    }
    const signal = controller.signal;

    this.emit({ type: "agent_start" });
    this.appendInternal(userMessage);

    let turn = 0;
    let reason: AgentStopReason = "natural_stop";

    try {
      while (true) {
        if (signal.aborted) {
          reason = "aborted";
          break;
        }
        if (turn >= this.maxTurns) {
          reason = "max_turns_exceeded";
          break;
        }
        turn += 1;
        this.emit({ type: "turn_start", turn });

        const contextMessages = await this.transformContext(this.agentState.messages);
        const modelContext: Context = buildModelContext(this.agentState, contextMessages);

        const stream = this.streamFn(this.agentState.model, modelContext, { signal });
        const assistant = await this.consumeAssistantStream(stream, signal);
        if (assistant.stopReason === "aborted") {
          this.emit({ type: "turn_end", turn });
          reason = "aborted";
          break;
        }
        if (assistant.stopReason === "error") {
          this.agentState.error = assistant.errorMessage ?? "Model stream failed.";
          this.emit({ type: "turn_end", turn });
          reason = "error";
          break;
        }

        const toolCalls = assistant.content.filter(
          (part): part is ToolCall => part.type === "toolCall",
        );

        if (toolCalls.length === 0) {
          // Natural stop point: steering wins over follow-ups, follow-ups
          // continue the run, otherwise the run ends here.
          this.emit({ type: "turn_end", turn });
          if (this.consumeSteering()) {
            continue;
          }
          if (this.consumeFollowUps()) {
            continue;
          }
          break;
        }

        for (let index = 0; index < toolCalls.length; index += 1) {
          const toolCall = toolCalls[index];
          if (!toolCall) {
            continue;
          }
          if (this.steeringQueue.length > 0) {
            // Boundary check: queued steering skips this and remaining calls.
            for (const skipped of toolCalls.slice(index)) {
              this.skipToolCall(STEERING_SKIPPED_TEXT, skipped);
            }
            break;
          }
          if (signal.aborted) {
            for (const skipped of toolCalls.slice(index)) {
              this.skipToolCall(ABORTED_SKIPPED_TEXT, skipped);
            }
            reason = "aborted";
            break;
          }
          await this.executeTool(toolCall.id, toolCall.name, toolCall.arguments, signal);
        }

        this.emit({ type: "turn_end", turn });
        if (reason === "aborted") {
          break;
        }
        // Steering queued during the tool phase redirects the next turn
        // instead of letting the model answer the tool results.
        if (this.consumeSteering()) {
          continue;
        }
      }
    } finally {
      this.agentState.isRunning = false;
      this.agentState.streamMessage = null;
      this.abortController = null;
      this.emit({ type: "agent_end", reason });
    }

    return { reason, turns: turn };
  }

  // --- internals -----------------------------------------------------------

  private emit(event: AgentCoreEvent): void {
    for (const listener of [...this.listeners]) {
      listener(event);
    }
  }

  private appendInternal(message: Message): void {
    this.agentState.messages.push(message);
    this.emit({ type: "message_start", message });
    this.emit({ type: "message_end", message });
  }

  private consumeSteering(): boolean {
    if (this.steeringQueue.length === 0) {
      return false;
    }
    appendUserTexts(this, this.steeringQueue.splice(0));
    return true;
  }

  private consumeFollowUps(): boolean {
    if (this.followUpQueue.length === 0) {
      return false;
    }
    appendUserTexts(this, this.followUpQueue.splice(0));
    return true;
  }

  /**
   * Consume one assistant stream: forward message_update events, finalize the
   * assistant message (message_start is emitted lazily for streams that skip
   * their start event), and retain the finished message in context.
   */
  private async consumeAssistantStream(
    stream: AssistantMessageEventStream,
    signal: AbortSignal,
  ): Promise<AssistantMessage> {
    let assistant: AssistantMessage | null = null;
    let started = false;
    let lastPartial: AssistantMessage | null = null;

    try {
      for await (const event of stream) {
        if (event.type === "start") {
          lastPartial = event.partial;
          this.agentState.streamMessage = event.partial;
          this.emit({ type: "message_start", message: event.partial });
          started = true;
          continue;
        }
        if (event.type === "done") {
          assistant = event.message;
          break;
        }
        if (event.type === "error") {
          assistant = event.error;
          break;
        }
        lastPartial = event.partial;
        this.agentState.streamMessage = event.partial;
        this.emit({
          type: "message_update",
          assistantMessageEvent: event,
          partial: event.partial,
        });
      }
    } catch (error) {
      assistant = signal.aborted
        ? minimalAssistant(this.agentState.model, [], "aborted")
        : minimalAssistant(
            this.agentState.model,
            [],
            "error",
            error instanceof Error ? error.message : String(error),
          );
    }

    if (!assistant) {
      assistant =
        signal.aborted && lastPartial
          ? { ...lastPartial, stopReason: "aborted" as const }
          : minimalAssistant(
              this.agentState.model,
              [],
              signal.aborted ? "aborted" : "error",
              signal.aborted ? undefined : "Model stream ended without completion.",
            );
    }

    if (!started) {
      this.emit({ type: "message_start", message: assistant });
    }
    this.agentState.streamMessage = assistant;
    this.agentState.messages.push(assistant);
    this.emit({ type: "message_end", message: assistant });
    return assistant;
  }

  /** Execute one tool call; failures become observable error tool results. */
  private async executeTool(
    toolCallId: string,
    toolName: string,
    rawArguments: Record<string, unknown>,
    signal: AbortSignal,
  ): Promise<void> {
    this.emit({ type: "tool_execution_start", toolCallId, toolName, args: rawArguments });

    const failResult = (text: string): void => {
      this.emit({
        type: "tool_execution_end",
        toolCallId,
        toolName,
        result: text,
        isError: true,
      });
      this.appendToolResult(toolCallId, toolName, [{ type: "text", text }], true);
    };

    const tool = this.agentState.tools.find((candidate) => candidate.name === toolName);
    if (!tool) {
      failResult(`Unknown tool: ${toolName}`);
      return;
    }

    const validation = validateToolArguments(tool.parameters, rawArguments);
    if (!validation.ok) {
      failResult(`Invalid arguments for ${toolName}: ${validation.errors.join("; ")}`);
      return;
    }

    const executionOptions: ToolExecutionOptions = {
      signal,
      onUpdate: (payload?: unknown) => {
        this.emit({ type: "tool_execution_update", toolCallId, toolName, payload });
      },
    };

    try {
      const result = await tool.execute(validation.value, executionOptions);
      const text = typeof result === "string" ? result : JSON.stringify(result);
      this.emit({ type: "tool_execution_end", toolCallId, toolName, result, isError: false });
      this.appendToolResult(toolCallId, toolName, [{ type: "text", text }], false);
    } catch (error) {
      const text = signal.aborted
        ? "Aborted during execution."
        : error instanceof Error
          ? error.message
          : String(error);
      this.emit({
        type: "tool_execution_end",
        toolCallId,
        toolName,
        result: text,
        isError: true,
      });
      this.appendToolResult(toolCallId, toolName, [{ type: "text", text }], true);
    }
  }

  /**
   * Synthesize an error tool result for a tool call that will not execute
   * (steering interruption or abort). Mirrors the reference loop behavior:
   * every tool call keeps a matching tool result so the model context stays
   * well-formed for providers that require it.
   */
  private skipToolCall(reasonText: string, call: ToolCall): void {
    this.emit({
      type: "tool_execution_start",
      toolCallId: call.id,
      toolName: call.name,
      args: {},
    });
    this.emit({
      type: "tool_execution_end",
      toolCallId: call.id,
      toolName: call.name,
      result: reasonText,
      isError: true,
    });
    this.appendToolResult(call.id, call.name, [{ type: "text", text: reasonText }], true);
  }

  private appendToolResult(
    toolCallId: string,
    toolName: string,
    content: TextContent[],
    isError: boolean,
  ): void {
    const message: ToolResultMessage = {
      role: "toolResult",
      toolCallId,
      toolName,
      content,
      isError,
      timestamp: Date.now(),
    };
    this.appendInternal(message);
  }
}

function buildModelContext(state: AgentState, messages: Message[]): Context {
  // The context is a per-call view; never alias the live message array.
  const context: Context = { messages: [...messages] };
  if (state.systemPrompt) {
    context.systemPrompt = state.systemPrompt;
  }
  if (state.tools.length > 0) {
    context.tools = state.tools.map((tool) => ({
      name: tool.name,
      description: tool.description,
      parameters: tool.parameters,
    }));
  }
  return context;
}

function appendUserTexts(agent: AgentCore, texts: string[]): void {
  for (const text of texts) {
    agent.appendMessage({ role: "user", content: text, timestamp: Date.now() });
  }
}

function minimalAssistant(
  model: Model<Api>,
  content: AssistantMessage["content"],
  stopReason: AssistantMessage["stopReason"],
  errorMessage?: string,
): AssistantMessage {
  return {
    role: "assistant",
    content,
    api: model.api,
    provider: model.provider,
    model: model.id,
    usage: emptyUsage(),
    stopReason,
    ...(errorMessage !== undefined ? { errorMessage } : {}),
    timestamp: Date.now(),
  };
}

function emptyUsage(): AssistantMessage["usage"] {
  return {
    input: 0,
    output: 0,
    cacheRead: 0,
    cacheWrite: 0,
    totalTokens: 0,
    cost: { input: 0, output: 0, cacheRead: 0, cacheWrite: 0, total: 0 },
  };
}
