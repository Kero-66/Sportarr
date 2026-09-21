import { describe, expect, it } from 'vitest';
import { AsyncOperationQueue, isPlaybackGenerationCurrent } from './streamPlaybackGeneration';

describe('isPlaybackGenerationCurrent', () => {
  it('rejects a startup completion after cleanup advances the generation', () => {
    expect(isPlaybackGenerationCurrent(4, 4)).toBe(true);
    expect(isPlaybackGenerationCurrent(4, 5)).toBe(false);
  });

  it('runs cleanup before a replacement startup', async () => {
    const queue = new AsyncOperationQueue();
    const events: string[] = [];
    let releaseStart: (() => void) | undefined;
    const startGate = new Promise<void>((resolve) => {
      releaseStart = resolve;
    });

    const staleStart = queue.enqueue(async () => {
      events.push('stale-start');
      await startGate;
      events.push('stale-finished');
    });
    const cleanup = queue.enqueue(async () => {
      events.push('cleanup');
    });
    const replacement = queue.enqueue(async () => {
      events.push('replacement-start');
    });

    await Promise.resolve();
    expect(events).toEqual(['stale-start']);

    releaseStart?.();
    await Promise.all([staleStart, cleanup, replacement]);
    expect(events).toEqual([
      'stale-start',
      'stale-finished',
      'cleanup',
      'replacement-start',
    ]);
  });
});
