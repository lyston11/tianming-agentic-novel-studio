import type {
  ActivationRequest,
  ActivationResult,
  AspNetAgentApi,
  ConversationRuntimeRequest,
  ProjectCatalogItem,
} from "./contracts.js";

export class HttpAspNetAgentApi implements AspNetAgentApi {
  public constructor(
    private readonly baseUrl: string,
    private readonly apiKey: string,
    private readonly fetchImpl: typeof fetch = fetch,
  ) {
    if (!baseUrl) throw new Error("PI_RUNTIME_CALLBACK_BASE_URL is required.");
    if (!apiKey) throw new Error("PI_RUNTIME_INTERNAL_API_KEY is required.");
  }

  public async loadContext(
    request: ConversationRuntimeRequest,
  ): Promise<Pick<ConversationRuntimeRequest, "binding" | "systemInstructions" | "projectContext" | "allowedProjectTools">> {
    return this.send(
      `/internal/pi/conversations/${encodeURIComponent(request.sessionId)}/context?userId=${encodeURIComponent(request.userId)}`,
      { method: "GET" },
    );
  }

  public async listAccessibleProjects(request: ConversationRuntimeRequest): Promise<ProjectCatalogItem[]> {
    return this.send<ProjectCatalogItem[]>(
      `/internal/pi/conversations/${encodeURIComponent(request.sessionId)}/projects?userId=${encodeURIComponent(request.userId)}`,
      { method: "GET" },
    );
  }

  public async activateProjectContext(
    request: ConversationRuntimeRequest,
    activation: ActivationRequest,
  ): Promise<ActivationResult> {
    return this.send<ActivationResult>(
      `/internal/pi/conversations/${encodeURIComponent(request.sessionId)}/project-context?userId=${encodeURIComponent(request.userId)}`,
      {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify(activation),
      },
      true,
    );
  }

  private async send<T>(path: string, init: RequestInit, acceptErrorBody = false): Promise<T> {
    const response = await this.fetchImpl(new URL(path, this.baseUrl), {
      ...init,
      headers: {
        ...init.headers,
        "x-pi-runtime-key": this.apiKey,
      },
    });
    const body = (await response.json()) as T;
    if (!response.ok && !acceptErrorBody) {
      throw new Error(JSON.stringify({ status: "internal_api_error", httpStatus: response.status, body }));
    }
    return body;
  }
}
