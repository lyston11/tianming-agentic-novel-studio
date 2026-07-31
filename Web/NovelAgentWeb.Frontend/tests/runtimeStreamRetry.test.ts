import assert from 'node:assert/strict';
import test from 'node:test';
import { runtimeStreamRetryDelay } from '../src/pages/agent/runtimeStreamRetry.ts';

test('runtimeStreamRetryDelay uses capped exponential backoff with bounded jitter', () => {
  assert.equal(runtimeStreamRetryDelay(0, () => 0.5), 1_000);
  assert.equal(runtimeStreamRetryDelay(3, () => 0.5), 8_000);
  assert.equal(runtimeStreamRetryDelay(20, () => 0.5), 15_000);
  assert.equal(runtimeStreamRetryDelay(0, () => 0), 800);
  assert.equal(runtimeStreamRetryDelay(0, () => 1), 1_200);
});
