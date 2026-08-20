import { useEffect, useRef } from 'react';
import { createNovelAgentWorkflowSseConnection } from '../../../api';
import type { NovelAgentEventEnvelope } from '../../../api/types';
import { runtimeStreamRetryDelay } from '../runtimeStreamRetry';

interface NovelAgentWorkflowStreamOptions {
  projectId?: string | null;
  goalId: string;
  onWorkflowEvent: () => void | Promise<void>;
}

export function useNovelAgentWorkflowStream({
  projectId,
  goalId,
  onWorkflowEvent,
}: NovelAgentWorkflowStreamOptions) {
  const callbackRef = useRef(onWorkflowEvent);
  const lastSequenceRef = useRef(0);

  useEffect(() => {
    callbackRef.current = onWorkflowEvent;
  }, [onWorkflowEvent]);

  useEffect(() => {
    if (!projectId || !goalId) return;
    lastSequenceRef.current = 0;
    let disposed = false;
    let retryAttempt = 0;
    let retryTimer: number | null = null;
    let connection: ReturnType<typeof createNovelAgentWorkflowSseConnection> | null = null;

    const connect = () => {
      if (disposed) return;
      const cursor = lastSequenceRef.current > 0
        ? `workflow:${projectId}:${lastSequenceRef.current}`
        : null;
      connection = createNovelAgentWorkflowSseConnection(projectId, cursor);
      connection.onopen = () => {
        retryAttempt = 0;
      };
      connection.onmessage = (message) => {
        try {
          const event = JSON.parse(message.data) as NovelAgentEventEnvelope;
          if (event.streamKind !== 'Workflow' || event.streamId !== projectId) return;
          if (event.sequence <= lastSequenceRef.current) return;
          lastSequenceRef.current = event.sequence;
          if (event.transient || event.goalId !== goalId) return;
          void callbackRef.current();
        } catch {
          // Periodic query refresh remains the fallback for malformed transient data.
        }
      };
      connection.onerror = () => {
        if (disposed) return;
        connection?.close();
        retryTimer = window.setTimeout(connect, runtimeStreamRetryDelay(retryAttempt));
        retryAttempt += 1;
      };
    };

    connect();
    return () => {
      disposed = true;
      if (retryTimer != null) window.clearTimeout(retryTimer);
      connection?.close();
    };
  }, [goalId, projectId]);
}
