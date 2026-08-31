import type { NovelContextPackage, CharacterState, ForeshadowEntry } from "../contracts.js";

export interface NovelSkill {
  readonly name: "chapter-writing";
  readonly description: string;
  readonly inputContract: readonly string[];
  readonly outputContract: readonly string[];
  readonly validationRules: readonly string[];
}

export const chapterWritingSkill: NovelSkill = {
  name: "chapter-writing",
  description: "Produces a traceable chapter candidate from a frozen context package.",
  inputContract: ["chapterNumber", "chapterBrief", "acceptanceCriteria", "NovelContextPackage"],
  outputContract: ["title", "body", "referencedCharacterIds", "updatedForeshadowEntryIds"],
  validationRules: [
    "chapter number must match GoalRevision",
    "body must be non-empty",
    "referenced characters must exist in the frozen package",
    "updated foreshadows must exist in the frozen package",
  ],
};

export function renderChapterWriterPrompt(
  context: NovelContextPackage,
  chapterNumber: number,
  chapterBrief: string,
  acceptanceCriteria: readonly string[],
): string {
  return JSON.stringify({
    skill: chapterWritingSkill.name,
    chapterNumber,
    chapterBrief,
    acceptanceCriteria,
    frozenContext: {
      contextPackageId: context.contextPackageId,
      contextPackageHash: context.contentHash,
      canonSnapshot: context.canonSnapshot,
      characterStates: context.characterStates,
      foreshadowEntries: context.foreshadowEntries,
    },
  });
}

export function characterNames(states: readonly CharacterState[]): string[] {
  return states.map((state) => state.name);
}

export function openForeshadows(entries: readonly ForeshadowEntry[]): ForeshadowEntry[] {
  return entries.filter((entry) => entry.status === "open");
}
