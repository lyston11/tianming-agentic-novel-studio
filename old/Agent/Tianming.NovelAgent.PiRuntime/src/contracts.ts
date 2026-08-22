export interface ConversationRuntimeRequest {
  userId: string;
  sessionId: string;
  message: string;
  attachmentIds: string[];
  correlationId: string;
  sourceUserMessageId?: string;
  binding: {
    state: "unbound" | "bound";
    projectId?: string;
    version: string;
  };
  systemInstructions: string;
  durableMessages: DurableConversationMessage[];
  projectContext?: unknown;
  allowedProjectTools: string[];
}

export interface DurableConversationMessage {
  role: string;
  content: string;
  toolCallId?: string;
  toolName?: string;
  argumentsJson?: string;
  detailsJson?: string;
  isError?: boolean;
  customType?: string;
}

export interface ConversationRuntimeResponse {
  assistantMessage: string;
  decisionKind: "conversation" | "goal_proposal";
  reason?: string;
  tokenDeltas: string[];
  messages: ConversationRuntimeMessage[];
  toolCalls: AgentToolCall[];
  checkpoint: {
    runtime: string;
    checkpointJson: string;
  };
}

export interface ConversationRuntimeMessage {
  role: "assistant" | "toolResult" | "custom";
  content: string;
  toolCallId?: string;
  toolName?: string;
  argumentsJson?: string;
  detailsJson?: string;
  isError?: boolean;
  customType?: string;
}

export interface AgentToolCall {
  name: string;
  argumentsJson: string;
}

export interface ProjectCatalogItem {
  projectId: string;
  title: string;
  status: string;
  updatedAt: string;
}

export interface ActivationRequest {
  projectId: string;
  confirmationSource: string;
  idempotencyKey: string;
  expectedBindingVersion: string;
}

export interface ActivationResult {
  code: "activated" | "already_activated" | "confirmation_required" | "project_unavailable" | "version_conflict" | "conversation_unavailable";
  projectId?: string;
  bindingVersion?: string;
  contextVersion?: string;
  reason?: string;
}

export interface AspNetAgentApi {
  loadContext(request: ConversationRuntimeRequest): Promise<Pick<ConversationRuntimeRequest, "binding" | "systemInstructions" | "projectContext" | "allowedProjectTools">>;
  listAccessibleProjects(request: ConversationRuntimeRequest): Promise<ProjectCatalogItem[]>;
  activateProjectContext(request: ConversationRuntimeRequest, activation: ActivationRequest): Promise<ActivationResult>;
}

export interface RuntimeSettings {
  model?: {
    provider: string;
    modelId: string;
    api: string;
    baseUrl: string;
    contextWindow: number;
    maxTokens: number;
  };
  apiKey?: string;
}
