import { useEffect, useRef } from 'react';
import { createSseConnection } from '@/api';
import type { AgentSseEvent } from '@/api/types';
import { runtimeStreamRetryDelay } from '@/lib/stream-retry';

interface GoalProgressStreamOptions {
  goalId: string;
  sessionId?: string | null;
  onGoalEvent: () => void | Promise<void>;
}

/** Legacy goal event stream: filtered by goal_* event types for this goal. */
export function useGoalProgressStream({ goalId, sessionId, onGoalEvent }: GoalProgressStreamOptions) {
  const callbackRef = useRef(onGoalEvent);
  const lastEventIdRef = useRef<string | null>(null);

  useEffect(() => {
    callbackRef.current = onGoalEvent;
  }, [onGoalEvent]);

  useEffect(() => {
    if (!goalId || !sessionId) return;
    let disposed = false;
    let retryAttempt = 0;
    let retryTimer: number | null = null;
    let connection: ReturnType<typeof createSseConnection> | null = null;

    const connect = () => {
      if (disposed) return;
      connection = createSseConnection(sessionId, lastEventIdRef.current);
      connection.onopen = () => {
        retryAttempt = 0;
      };
      connection.onmessage = (message) => {
        try {
          const event = JSON.parse(message.data) as AgentSseEvent;
          const data = event.data && typeof event.data === 'object'
            ? event.data as { goalId?: string }
            : null;
          if (!event.type.startsWith('goal_') || data?.goalId !== goalId) return;
          if (event.eventId) lastEventIdRef.current = event.eventId;
          void callbackRef.current();
        } catch {
          // A malformed transient event is ignored; periodic query refresh remains the fallback.
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
  }, [goalId, sessionId]);
}
