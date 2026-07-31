import { useCallback, useEffect } from 'react';
import { createSseConnection, listRuntimeEvents } from '../../api';
import type { AgentSseEvent } from '../../api/types';
import { runtimeEventToSseEvent } from './runtimeEvents';
import { runtimeStreamRetryDelay } from './runtimeStreamRetry';

interface AgentRuntimeStreamOptions {
  sessionId: string | null;
  lastRuntimeEventIdRef: { current: Record<string, string> };
  addSseEvent: (event: AgentSseEvent) => void;
  handleSseEvent: (event: AgentSseEvent) => void;
  setConnected: (connected: boolean) => void;
}

export function useAgentRuntimeStream({
  sessionId,
  lastRuntimeEventIdRef,
  addSseEvent,
  handleSseEvent,
  setConnected,
}: AgentRuntimeStreamOptions) {
  const replayRuntimeEvents = useCallback(async (targetSessionId: string, afterEventId?: string | null) => {
    try {
      const events = await listRuntimeEvents({
        sessionId: targetSessionId,
        limit: 80,
        afterEventId,
      });
      events.forEach((event) => {
        handleSseEvent(runtimeEventToSseEvent(event, targetSessionId));
      });
    } catch {
      // Live events and active-run polling remain available when replay is temporarily unavailable.
    }
  }, [handleSseEvent]);

  useEffect(() => {
    if (!sessionId) return;
    let disposed = false;
    let retryAttempt = 0;
    let retryTimer: number | null = null;
    let connection: ReturnType<typeof createSseConnection> | null = null;
    setConnected(false);

    const connect = () => {
      if (disposed) return;
      const afterEventId = lastRuntimeEventIdRef.current[sessionId] || null;
      connection = createSseConnection(sessionId, afterEventId);
      connection.onopen = () => {
        retryAttempt = 0;
        setConnected(true);
        void replayRuntimeEvents(sessionId, afterEventId);
      };
      connection.onmessage = (message) => {
        try {
          const event: AgentSseEvent = JSON.parse(message.data);
          addSseEvent(event);
          handleSseEvent(event);
        } catch {
          // Malformed server events are ignored; the persisted event can still be replayed.
        }
      };
      connection.onerror = () => {
        if (disposed) return;
        setConnected(false);
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
      setConnected(false);
    };
  }, [addSseEvent, handleSseEvent, lastRuntimeEventIdRef, replayRuntimeEvents, sessionId, setConnected]);
}
