export function isPlaybackGenerationCurrent(
  generation: number,
  currentGeneration: number,
): boolean {
  return generation === currentGeneration;
}

export class AsyncOperationQueue {
  private tail: Promise<void> = Promise.resolve();

  enqueue<T>(operation: () => Promise<T>): Promise<T> {
    const result = this.tail.then(operation, operation);
    this.tail = result.then(
      () => undefined,
      () => undefined,
    );
    return result;
  }
}
