import { describe, expect, it } from 'vitest';
import { runtimeStreamRetryDelay } from './stream-retry';

describe('runtimeStreamRetryDelay', () => {
  it('starts near 1s and grows exponentially up to the 15s ceiling', () => {
    expect(runtimeStreamRetryDelay(0, () => 1)).toBe(1_200);
    expect(runtimeStreamRetryDelay(1, () => 1)).toBe(2_400);
    expect(runtimeStreamRetryDelay(3, () => 1)).toBe(9_600);
    expect(runtimeStreamRetryDelay(10, () => 1)).toBe(18_000);
    expect(runtimeStreamRetryDelay(50, () => 1)).toBe(18_000);
  });

  it('applies ±20% jitter', () => {
    expect(runtimeStreamRetryDelay(2, () => 0)).toBe(3_200);
    expect(runtimeStreamRetryDelay(2, () => 0.5)).toBe(4_000);
    expect(runtimeStreamRetryDelay(2, () => 0.9)).toBe(4_640);
  });

  it('never leaves the jitter window around the capped exponential delay', () => {
    for (let attempt = 0; attempt < 30; attempt += 1) {
      const delay = runtimeStreamRetryDelay(attempt, Math.random);
      expect(delay).toBeGreaterThanOrEqual(800);
      expect(delay).toBeLessThanOrEqual(18_000);
    }
  });
});
