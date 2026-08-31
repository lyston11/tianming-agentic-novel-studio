import type {
  CandidateChapter,
  GoalRevision,
  NovelContextPackage,
  Review,
  ReviewFinding,
} from "../contracts.js";

export interface ContinuityGateInput {
  readonly candidate: CandidateChapter;
  readonly goalRevision: GoalRevision;
  readonly contextPackage: NovelContextPackage;
}

/** Pure candidate preflight; the C# ChapterGatekeeper owns the production gate. */
export function runContinuityGate(input: ContinuityGateInput): Review {
  const findings: ReviewFinding[] = [];
  const characters = new Set(input.contextPackage.characterStates.map((state) => state.characterId));
  const foreshadows = new Set(input.contextPackage.foreshadowEntries.map((entry) => entry.entryId));

  if (input.candidate.chapterNumber !== input.goalRevision.chapterNumber) {
    findings.push({ code: "chapter_number_mismatch", message: "Candidate chapter number does not match the confirmed GoalRevision." });
  }
  if (input.candidate.body.trim().length === 0) {
    findings.push({ code: "empty_body", message: "Candidate body must not be empty." });
  }
  if (input.candidate.contextPackageHash !== input.contextPackage.contentHash) {
    findings.push({ code: "context_hash_mismatch", message: "Candidate was not generated from the frozen context package." });
  }
  for (const characterId of input.candidate.referencedCharacterIds) {
    if (!characters.has(characterId)) {
      findings.push({ code: "unknown_character", message: `Candidate references unknown character: ${characterId}.` });
    }
  }
  for (const entryId of input.candidate.updatedForeshadowEntryIds) {
    if (!foreshadows.has(entryId)) {
      findings.push({ code: "unknown_foreshadow", message: `Candidate updates unknown foreshadow entry: ${entryId}.` });
    }
  }
  for (const update of input.candidate.characterUpdates) {
    if (!characters.has(update.characterId)) {
      findings.push({ code: "unknown_character_update", message: `Candidate updates unknown character: ${update.characterId}.` });
    }
  }
  for (const update of input.candidate.foreshadowUpdates) {
    if (!foreshadows.has(update.entryId)) {
      findings.push({ code: "unknown_foreshadow_update", message: `Candidate updates unknown foreshadow entry: ${update.entryId}.` });
    }
  }

  return {
    reviewId: `review-${input.candidate.candidateId}`,
    candidateId: input.candidate.candidateId,
    projectId: input.candidate.projectId,
    userId: input.candidate.userId,
    gateName: "continuity",
    passed: findings.length === 0,
    findings,
    reviewVersion: 1,
    createdAt: new Date(0).toISOString(),
  };
}
