import type { Context } from "@tianming/agent-core";
import type {
  ActorScope,
  ContextRequest,
  FreezeContextRequest,
  NovelContextPackage,
  NovelProjectSnapshot,
} from "../contracts.js";
import { contextPackageContentHash, clone, NovelCommandError } from "../contracts.js";
import type { NovelContextPort } from "../ports.js";

export class InMemoryContextProvider implements NovelContextPort {
  private readonly projects = new Map<string, NovelProjectSnapshot>();
  private readonly packages = new Map<string, NovelContextPackage>();
  private packageCounter = 0;

  public async createProject(input: {
    projectId: string;
    userId: string;
    canonSnapshot?: string;
    characterStates?: readonly NovelProjectSnapshot["characterStates"][number][];
    foreshadowEntries?: readonly NovelProjectSnapshot["foreshadowEntries"][number][];
  }): Promise<NovelProjectSnapshot> {
    if (this.projects.has(input.projectId)) {
      throw new NovelCommandError("conflict", `Project already exists: ${input.projectId}`);
    }
    const snapshot: NovelProjectSnapshot = {
      projectId: input.projectId,
      userId: input.userId,
      canonSnapshot: input.canonSnapshot ?? "",
      characterStates: clone(input.characterStates ?? []),
      foreshadowEntries: clone(input.foreshadowEntries ?? []),
      sourceReferences: [],
      versionVector: {
        goalRevision: 0,
        canon: 0,
        modelProfile: "fake-chapter-writer-v1",
        styleResource: "default-v1",
      },
      previousChapterNumber: 0,
    };
    this.projects.set(input.projectId, snapshot);
    return clone(snapshot);
  }

  public async loadForConversation(input: ContextRequest): Promise<NovelProjectSnapshot> {
    const snapshot = this.projects.get(input.actor.projectId);
    this.assertScope(snapshot, input.actor);
    return clone(snapshot);
  }

  public async freezeForChapter(input: FreezeContextRequest): Promise<NovelContextPackage> {
    const snapshot = this.projects.get(input.actor.projectId);
    this.assertScope(snapshot, input.actor);
    const packageWithoutHash: Omit<NovelContextPackage, "contentHash" | "createdAt" | "contextPackageId"> = {
      goalRevisionId: input.goalRevisionId,
      taskId: input.taskId,
      projectId: snapshot.projectId,
      userId: snapshot.userId,
      canonSnapshot: snapshot.canonSnapshot,
      characterStates: clone(snapshot.characterStates),
      foreshadowEntries: clone(snapshot.foreshadowEntries),
      sourceReferences: clone(snapshot.sourceReferences),
      versionVector: clone(snapshot.versionVector),
    };
    const packageValue: NovelContextPackage = {
      ...packageWithoutHash,
      contextPackageId: `ctx-${++this.packageCounter}`,
      contentHash: contextPackageContentHash(packageWithoutHash),
      createdAt: new Date(0).toISOString(),
    };
    this.packages.set(packageValue.taskId, packageValue);
    return clone(packageValue);
  }

  public getFrozen(taskId: string): NovelContextPackage | null {
    const value = this.packages.get(taskId);
    return value ? clone(value) : null;
  }

  public toModelContext(snapshot: NovelProjectSnapshot): Context {
    return {
      systemPrompt: JSON.stringify({
        projectId: snapshot.projectId,
        canonSnapshot: snapshot.canonSnapshot,
        characterStates: snapshot.characterStates,
        foreshadowEntries: snapshot.foreshadowEntries,
        previousChapterNumber: snapshot.previousChapterNumber,
      }),
      messages: [],
    };
  }

  public replaceProjectSnapshot(snapshot: NovelProjectSnapshot): void {
    this.projects.set(snapshot.projectId, clone(snapshot));
  }

  private assertScope(snapshot: NovelProjectSnapshot | undefined, actor: ActorScope): asserts snapshot is NovelProjectSnapshot {
    if (!snapshot) throw new NovelCommandError("not_found", `Project not found: ${actor.projectId}`);
    if (snapshot.userId !== actor.userId) {
      throw new NovelCommandError("forbidden", "Actor is not authorized for this project.");
    }
  }
}
