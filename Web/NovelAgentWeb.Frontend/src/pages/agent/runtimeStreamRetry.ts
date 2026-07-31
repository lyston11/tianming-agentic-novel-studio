const MIN_RETRY_DELAY_MS = 1_000;
const MAX_RETRY_DELAY_MS = 15_000;

export function runtimeStreamRetryDelay(
  attempt: number,
  random: () => number = Math.random,
) {
  const normalizedAttempt = Math.max(0, Math.floor(attempt));
  const exponentialDelay = Math.min(
    MAX_RETRY_DELAY_MS,
    MIN_RETRY_DELAY_MS * (2 ** normalizedAttempt),
  );
  const jitterMultiplier = 0.8 + (Math.min(1, Math.max(0, random())) * 0.4);
  return Math.round(exponentialDelay * jitterMultiplier);
}
