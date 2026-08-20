# Toolchain and Framework Constraints

## Confirmed constraints

- Target runtime: C# and `.NET 10 LTS` modular monolith.
- Current repository and local SDK: `.NET 8`; a toolchain and package compatibility upgrade is mandatory before new `net10.0` projects can be referenced by Web.
- Production orchestration truth: PostgreSQL typed DAG and deterministic state machines.
- Conversation runtime: Microsoft Agent Framework behind `IConversationAgentRuntime`, replaceable by a deterministic test runtime.
- OpenAI provider: official OpenAI .NET SDK and Responses API behind a provider-neutral gateway.
- LangGraph and OpenAI Agents SDK are design references only; they do not own persisted state.

## Compatibility rules for implementation

1. Pin exact SDK/package versions only after restore/build probes on the local `.NET 10` toolchain.
2. Keep all MAF and OpenAI SDK types inside Infrastructure adapters.
3. If MAF requires preview packages, isolate preview APIs behind one adapter and preserve a no-MAF test implementation.
4. If an SDK feature differs from the provider-neutral contract, adapt or report capability absence; do not leak SDK-specific fields into Domain.
5. Do not silently replace Responses API with legacy Chat Completions for OpenAI. Any temporary compatibility bridge must be explicit and tested.

## Verification gap

The OpenAI developer-docs MCP was configured previously but is not exposed to this running thread, so no exact current SDK method or package version is asserted in planning. Implementation must verify the official API surface before editing the Adapter; this is a technical compatibility probe, not a product decision.

