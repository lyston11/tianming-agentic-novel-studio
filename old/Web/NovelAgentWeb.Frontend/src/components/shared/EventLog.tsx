import { useAppStore } from '../../stores/useAppStore';

export default function EventLog() {
  const eventLog = useAppStore((s) => s.eventLog);

  return (
    <div className="inspector-section">
      <h3>事件日志</h3>
      <div className="event-log">
        {eventLog.length === 0 ? (
          <div className="empty">暂无事件</div>
        ) : (
          eventLog.map((entry, i) => <div key={i}>{entry}</div>)
        )}
      </div>
    </div>
  );
}
