using System;
using TM.Services.Framework.AI.Core.Capabilities.Builders;
using TM.Services.Framework.AI.Middleware;
using TM.Services.Framework.AI.Middleware.Builtins;

namespace TM.Services.Framework.AI.Core.Capabilities
{
    public static class CapabilityServices
    {
        private static readonly object _lock = new();
        private static Lazy<ProviderCapabilityResolver> _default = new(BuildDefault);
        private static ProviderCapabilityResolver? _override;
        private static Lazy<ThinkingParameterBuilderRegistry> _defaultBuilders = new(BuildDefaultBuilders);
        private static ThinkingParameterBuilderRegistry? _buildersOverride;
        private static Lazy<AIRequestPipeline> _defaultPipeline = new(BuildDefaultPipeline);
        private static AIRequestPipeline? _pipelineOverride;

        public static ProviderCapabilityResolver DefaultResolver
        {
            get
            {
                lock (_lock)
                {
                    return _override ?? _default.Value;
                }
            }
        }

        public static IDisposable OverrideForTesting(ProviderCapabilityResolver resolver)
        {
            ArgumentNullException.ThrowIfNull(resolver);
            lock (_lock)
            {
                var previous = _override;
                _override = resolver;
                return new Restorer(() =>
                {
                    lock (_lock)
                    {
                        _override = previous;
                    }
                });
            }
        }

        private static ProviderCapabilityResolver BuildDefault()
        {
            return new ProviderCapabilityResolver(InMemoryProviderCapabilityRegistry.CreateBuiltIn());
        }

        public static ThinkingParameterBuilderRegistry DefaultBuilders
        {
            get
            {
                lock (_lock)
                {
                    return _buildersOverride ?? _defaultBuilders.Value;
                }
            }
        }

        public static IDisposable OverrideBuildersForTesting(ThinkingParameterBuilderRegistry registry)
        {
            ArgumentNullException.ThrowIfNull(registry);
            lock (_lock)
            {
                var previous = _buildersOverride;
                _buildersOverride = registry;
                return new Restorer(() =>
                {
                    lock (_lock)
                    {
                        _buildersOverride = previous;
                    }
                });
            }
        }

        private static ThinkingParameterBuilderRegistry BuildDefaultBuilders()
        {
            return ThinkingParameterBuilderRegistry.CreateBuiltIn();
        }

        public static AIRequestPipeline DefaultPipeline
        {
            get
            {
                lock (_lock)
                {
                    return _pipelineOverride ?? _defaultPipeline.Value;
                }
            }
        }

        public static IDisposable OverridePipelineForTesting(AIRequestPipeline pipeline)
        {
            ArgumentNullException.ThrowIfNull(pipeline);
            lock (_lock)
            {
                var previous = _pipelineOverride;
                _pipelineOverride = pipeline;
                return new Restorer(() =>
                {
                    lock (_lock)
                    {
                        _pipelineOverride = previous;
                    }
                });
            }
        }

        private static AIRequestPipeline BuildDefaultPipeline()
        {
            return new AIRequestPipeline(new IAIRequestMiddleware[]
            {
                new ErrorNormalizeMiddleware(),
                new TraceMiddleware(),
                new UsageMetricsMiddleware(),
                new ThinkingRequestMiddleware(),
                new ReasoningExtractionMiddleware(),
                new KeyRotationMiddleware(),
                new RetryFallbackMiddleware(),
            });
        }

        private sealed class Restorer : IDisposable
        {
            private readonly Action _onDispose;
            private bool _disposed;

            public Restorer(Action onDispose) => _onDispose = onDispose;

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _onDispose();
            }
        }
    }
}
