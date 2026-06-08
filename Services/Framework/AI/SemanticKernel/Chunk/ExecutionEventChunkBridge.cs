namespace TM.Services.Framework.AI.SemanticKernel.Chunk
{
    public static class ExecutionEventChunkBridge
    {
        public static IStreamChunk? ToChunk(ExecutionEvent? evt)
        {
            if (evt == null) return null;

            var pluginName = evt.PluginName ?? string.Empty;
            var functionName = evt.FunctionName ?? string.Empty;
            var stepIndex = evt.StepIndex ?? 0;

            return evt.EventType switch
            {
                ExecutionEventType.ToolCallStarted => new ToolCallStartChunk(
                    PluginName: pluginName,
                    FunctionName: functionName,
                    Arguments: evt.Detail,
                    StepIndex: stepIndex)
                {
                    RunId = evt.RunId,
                    Timestamp = evt.Timestamp,
                },

                ExecutionEventType.ToolCallCompleted => new ToolCallCompletedChunk(
                    PluginName: pluginName,
                    FunctionName: functionName,
                    Succeeded: evt.Succeeded ?? true,
                    Result: evt.Detail,
                    ErrorMessage: null,
                    StepIndex: stepIndex)
                {
                    RunId = evt.RunId,
                    Timestamp = evt.Timestamp,
                },

                ExecutionEventType.ToolCallFailed => new ToolCallCompletedChunk(
                    PluginName: pluginName,
                    FunctionName: functionName,
                    Succeeded: false,
                    Result: null,
                    ErrorMessage: evt.Detail,
                    StepIndex: stepIndex)
                {
                    RunId = evt.RunId,
                    Timestamp = evt.Timestamp,
                },

                _ => null,
            };
        }

        public static bool IsToolCallEvent(ExecutionEvent? evt)
        {
            if (evt == null) return false;
            return evt.EventType is ExecutionEventType.ToolCallStarted
                                  or ExecutionEventType.ToolCallCompleted
                                  or ExecutionEventType.ToolCallFailed;
        }
    }
}
