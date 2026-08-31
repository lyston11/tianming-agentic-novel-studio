import { Agent, type AgentEvent, type AgentMessage, type StreamFn } from "@mariozechner/pi-agent-core";
import type { AssistantMessage, Model } from "@mariozechner/pi-ai";
import type {
  AgentToolCall,
  AspNetAgentApi,
  ConversationRuntimeMessage,
  ConversationRuntimeRequest,
  ConversationRuntimeResponse,
} from "./contracts.js";
import { createRuntimeTools } from "./tools.js";

export type RuntimeStreamEvent =
  | { type: "token_delta"; delta: string }
  | { type: "tool_start"; toolCallId: string; toolName: string; argumentsJson: string }
  | { type: "tool_end"; toolCallId: string; toolName: string; isError: boolean; resultJson: string }
  | { type: "completed"; result: ConversationRuntimeResponse }
  | { type: "failed"; error: string };

export interface NovelPiRuntimeOptions {
  model: Model<any>;
  api: AspNetAgentApi;
  getApiKey: () => Promise<string | undefined>;
  discoverySkill: string;
  streamFn?: StreamFn;
  maxToolCalls?: number;
}

export class NovelPiRuntime {
  public constructor(private readonly options: NovelPiRuntimeOptions) {}

  public async run(
    request: ConversationRuntimeRequest,
    emit: (event: RuntimeStreamEvent) => void,
  ): Promise<ConversationRuntimeResponse> {
    const initialMessages = request.durableMessages
      .map((message) => toAgentMessage(message, this.options.model))
      .filter((message): message is AgentMessage => message !== undefined);
    const tokenDeltas: string[] = [];
    const observedToolCalls: AgentToolCall[] = [];
    let toolCallCount = 0;
    const maxToolCalls = this.options.maxToolCalls ?? 24;

    const hooks = {
      beforeToolCall: (name: string) => {
        toolCallCount++;
        if (toolCallCount > maxToolCalls) {
          throw new Error(JSON.stringify({ status: "safety_limit", tool: name, maxToolCalls }));
        }
      },
      afterToolCall: async () => {},
    };
    const tools = createRuntimeTools(request, this.options.api, hooks);
    const agent = new Agent({
      initialState: {
        systemPrompt: buildSystemPrompt(request, this.options.discoverySkill),
        model: this.options.model,
        thinkingLevel: "off",
        tools,
        messages: initialMessages,
      },
      ...(this.options.streamFn ? { streamFn: this.options.streamFn } : {}),
      getApiKey: this.options.getApiKey,
      transformContext: async (messages) => messages,
    });

    agent.subscribe((event) => forwardEvent(event, tokenDeltas, observedToolCalls, emit));
    try {
      await agent.prompt(request.message);
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error);
      emit({ type: "failed", error: message });
      throw error;
    }

    const newMessages = agent.state.messages.slice(initialMessages.length + 1);
    const assistant = [...newMessages].reverse().find((message) => message.role === "assistant");
    const assistantMessage = assistant ? assistantText(assistant as AssistantMessage) : "";
    const result: ConversationRuntimeResponse = {
      assistantMessage,
      decisionKind: "conversation",
      tokenDeltas,
      messages: newMessages.map(toDurableMessage),
      toolCalls: observedToolCalls,
      checkpoint: {
        runtime: "pi-agent-core",
        checkpointJson: JSON.stringify({
          version: 1,
          model: `${this.options.model.provider}/${this.options.model.id}`,
          durableMessageCount: request.durableMessages.length + 1 + newMessages.length,
          bindingVersion: request.binding.version,
        }),
      },
    };
    emit({ type: "completed", result });
    return result;
  }
}

function buildSystemPrompt(request: ConversationRuntimeRequest, discoverySkill: string): string {
  const binding = request.binding.state === "bound"
    ? `Bound project: ${request.binding.projectId}; binding version: ${request.binding.version}.`
    : `Conversation is Unbound; binding version: ${request.binding.version}.`;
  const projectContext = request.projectContext === undefined
    ? ""
    : `\nAuthorized project snapshot:\n${JSON.stringify(request.projectContext)}`;
  return `${request.systemInstructions}\n\n${binding}\n\n${discoverySkill}${projectContext}`;
}

function forwardEvent(
  event: AgentEvent,
  tokenDeltas: string[],
  observedToolCalls: AgentToolCall[],
  emit: (event: RuntimeStreamEvent) => void,
): void {
  if (event.type === "message_update" && event.assistantMessageEvent.type === "text_delta") {
    tokenDeltas.push(event.assistantMessageEvent.delta);
    emit({ type: "token_delta", delta: event.assistantMessageEvent.delta });
    return;
  }
  if (event.type === "tool_execution_start") {
    observedToolCalls.push({ name: event.toolName, argumentsJson: JSON.stringify(event.args) });
    emit({
      type: "tool_start",
      toolCallId: event.toolCallId,
      toolName: event.toolName,
      argumentsJson: JSON.stringify(event.args),
    });
    return;
  }
  if (event.type === "tool_execution_end") {
    emit({
      type: "tool_end",
      toolCallId: event.toolCallId,
      toolName: event.toolName,
      isError: event.isError,
      resultJson: JSON.stringify(event.result),
    });
  }
}

function toDurableMessage(message: AgentMessage): ConversationRuntimeMessage {
  if (message.role === "assistant") {
    const details = {
      stopReason: message.stopReason,
      errorMessage: message.errorMessage,
      timestamp: message.timestamp,
    };
    return {
      role: "assistant",
      content: JSON.stringify(message.content),
      detailsJson: JSON.stringify(details),
      customType: "pi.assistant.v1",
    };
  }
  if (message.role === "toolResult") {
    return {
      role: "toolResult",
      content: JSON.stringify(message.content),
      toolCallId: message.toolCallId,
      toolName: message.toolName,
      detailsJson: JSON.stringify(message.details ?? null),
      isError: message.isError,
      customType: "pi.tool-result.v1",
    };
  }
  return {
    role: "custom",
    content: JSON.stringify(message),
    customType: "pi.custom.v1",
  };
}

function toAgentMessage(
  message: ConversationRuntimeRequest["durableMessages"][number],
  model: Model<any>,
): AgentMessage | undefined {
  if (message.role === "user") {
    return { role: "user", content: message.content, timestamp: 0 };
  }
  if (message.customType === "pi.assistant.v1") {
    const details = parseJson<Record<string, unknown>>(message.detailsJson, {});
    return {
      role: "assistant",
      content: parseJson<AssistantMessage["content"]>(message.content, []),
      api: model.api,
      provider: model.provider,
      model: model.id,
      usage: emptyUsage(),
      stopReason: asStopReason(details.stopReason),
      ...(typeof details.errorMessage === "string" ? { errorMessage: details.errorMessage } : {}),
      timestamp: typeof details.timestamp === "number" ? details.timestamp : 0,
    };
  }
  if (message.customType === "pi.tool-result.v1" && message.toolCallId && message.toolName) {
    return {
      role: "toolResult",
      toolCallId: message.toolCallId,
      toolName: message.toolName,
      content: parseJson(message.content, []),
      details: parseJson(message.detailsJson, undefined),
      isError: message.isError ?? false,
      timestamp: 0,
    };
  }
  if (message.role === "assistant") {
    return {
      role: "assistant",
      content: [{ type: "text", text: message.content }],
      api: model.api,
      provider: model.provider,
      model: model.id,
      usage: emptyUsage(),
      stopReason: "stop",
      timestamp: 0,
    };
  }
  return undefined;
}

function assistantText(message: AssistantMessage): string {
  return message.content
    .filter((part) => part.type === "text")
    .map((part) => part.text)
    .join("");
}

function emptyUsage(): AssistantMessage["usage"] {
  return { input: 0, output: 0, cacheRead: 0, cacheWrite: 0, totalTokens: 0, cost: { input: 0, output: 0, cacheRead: 0, cacheWrite: 0, total: 0 } };
}

function asStopReason(value: unknown): AssistantMessage["stopReason"] {
  return value === "length" || value === "toolUse" || value === "error" || value === "aborted"
    ? value
    : "stop";
}

function parseJson<T>(value: string | undefined, fallback: T): T {
  if (!value) return fallback;
  try {
    return JSON.parse(value) as T;
  } catch {
    return fallback;
  }
}
