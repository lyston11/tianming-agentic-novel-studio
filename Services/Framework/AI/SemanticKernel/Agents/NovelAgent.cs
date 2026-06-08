#pragma warning disable SKEXP0110
#pragma warning disable SKEXP0130

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;
using TM.Services.Framework.AI.SemanticKernel.Agents.Wrappers;
using TM.Services.Framework.AI.SemanticKernel.Chunk;

namespace TM.Services.Framework.AI.SemanticKernel.Agents
{
    public sealed class NovelAgent
    {
        private readonly ChatCompletionAgent _agent;
        private readonly List<AIContextProvider> _contextProviders;

        public NovelAgent(Kernel kernel, string providerType, IEnumerable<AIContextProvider>? contextProviders = null)
        {
            _thinkingWrapper = new ThinkingStreamWrapper(providerType);
            _contextProviders = contextProviders?.ToList() ?? new List<AIContextProvider>();

            _agent = new ChatCompletionAgent
            {
                Kernel = kernel,
                Name = "NovelAssistant",
            };
        }

        public async IAsyncEnumerable<IStreamChunk> InvokeStreamingAsync(
            ChatHistory chatHistory,
            PromptExecutionSettings settings,
            Guid runId,
            [EnumeratorCancellation] CancellationToken ct)
        {
            var thread = _contextProviders.Count > 0
                ? new ChatHistoryAgentThread(chatHistory)
                {
                    AIContextProviders = new AggregateAIContextProvider(_contextProviders)
                }
                : new ChatHistoryAgentThread(chatHistory);

            var options = new AgentInvokeOptions
            {
                KernelArguments = new KernelArguments(settings)
            };

            var agentStream = _agent.InvokeStreamingAsync(thread, options, ct);

            await foreach (var chunk in _thinkingWrapper.WrapAgentStreamAsync(agentStream, runId, ct).ConfigureAwait(false))
            {
                yield return chunk;
            }
        }

        private ThinkingStreamWrapper _thinkingWrapper;
    }
}
