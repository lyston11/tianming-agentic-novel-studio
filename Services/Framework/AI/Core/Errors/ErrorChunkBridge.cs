using System;
using TM.Services.Framework.AI.SemanticKernel.Chunk;

namespace TM.Services.Framework.AI.Core.Errors
{
    public static class ErrorChunkBridge
    {
        public static ErrorChunk ToChunk(AIRequestError error, int sequence = 0)
        {
            ArgumentNullException.ThrowIfNull(error);

            return new ErrorChunk(
                Category: error.Kind.ToString(),
                Message: error.Message)
            {
                RunId = error.RunId ?? Guid.Empty,
                Sequence = sequence,
                UserFriendlyMessage = error.UserFriendlyMessage,
                HttpStatusCode = error.HttpStatusCode,
                InnerException = error.InnerException,
                ProviderId = error.ProviderId,
                ModelId = error.ModelId,
            };
        }

        public static AIRequestError PublishToBus(
            Exception ex,
            string? providerId = null,
            string? modelId = null,
            string? endpoint = null,
            System.Threading.CancellationToken cancellationToken = default,
            Guid? runId = null,
            int sequence = 0)
        {
            ArgumentNullException.ThrowIfNull(ex);

            var error = AIErrorClassifier.Classify(
                ex, providerId, modelId, endpoint, cancellationToken, runId);
            var chunk = ToChunk(error, sequence);
            AIChunkBus.Publish(chunk);
            return error;
        }
    }
}
