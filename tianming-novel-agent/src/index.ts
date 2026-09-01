// Public surface of the adapter layer only: loop wiring, tool definitions,
// context provider, event mapper, role/skill assets and the Application-facing
// port interfaces. The novel domain types in ./contracts.js and the preflight
// in ./domain/continuity-gate.js are internal: the durable domain truth lives
// in the C# control plane, so this package must not re-publish a domain API
// (task 09-01-demote-ts-novel-agent-to-adapter, R3/R6). The deterministic test
// fakes in ./runtime/fake-model.js are likewise internal; import the module
// directly in tests.
export * from "./ports.js";
export * from "./context/context-provider.js";
export * from "./roles/chapter-writer.js";
export * from "./skills/chapter-writing.js";
export * from "./tools/domain-tools.js";
export * from "./runtime/event-mapper.js";
export * from "./application/novel-agent-application.js";
