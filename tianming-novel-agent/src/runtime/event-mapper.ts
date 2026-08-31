import type { AgentCoreEvent } from "@tianming/agent-core";
import type { ActorScope, CorrelationId, DurableRuntimeMessage, Id, RuntimeEventType } from "../contracts.js";
import type { RuntimeEventSink } from "../ports.js";

export interface RuntimeEventContext {
  readonly runId: Id;
  readonly taskId: Id;
  readonly projectId: string;
  readonly userId: string;
  readonly correlationId: CorrelationId;
  readonly occurredAt?: string;
}

export class InMemoryRuntimeEventSink implements RuntimeEventSink {
  public readonly messages: DurableRuntimeMessage[] = [];

  public async append(message: DurableRuntimeMessage): Promise<void> {
    if (this.messages.some((item) => item.runId === message.runId && item.sequence === message.sequence)) return;
    this.messages.push({ ...message });
  }

  public replay(runId: Id, afterSequence = 0): DurableRuntimeMessage[] {
    return this.messages
      .filter((message) => message.runId === runId && message.sequence > afterSequence)
      .sort((left, right) => left.sequence - right.sequence)
      .map((message) => ({ ...message }));
  }
}

export class RuntimeEventMapper {
  private readonly nextSequence = new Map<Id, number>();

  public constructor(private readonly sink: RuntimeEventSink) {}

  public async map(event: AgentCoreEvent, context: RuntimeEventContext): Promise<DurableRuntimeMessage> {
    const sequence = (this.nextSequence.get(context.runId) ?? 0) + 1;
    this.nextSequence.set(context.runId, sequence);
    const { type, ...payload } = event;
    const message: DurableRuntimeMessage = {
      messageId: `${context.runId}:${sequence}`,
      runId: context.runId,
      taskId: context.taskId,
      projectId: context.projectId,
      userId: context.userId,
      sequence,
      eventType: type as RuntimeEventType,
      payload,
      correlationId: context.correlationId,
      occurredAt: context.occurredAt ?? new Date(0).toISOString(),
    };
    await this.sink.append(message);
    return message;
  }

  public reset(runId: Id): void {
    this.nextSequence.delete(runId);
  }
}

export function actorRuntimeContext(actor: ActorScope, taskId: string, correlationId: string, runId: string): RuntimeEventContext {
  return { runId, taskId, projectId: actor.projectId, userId: actor.userId, correlationId };
}
