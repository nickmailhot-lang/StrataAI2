import { configurationAuthorityFallback } from './configurationAuthorityFallback';

afterEach(() => vi.useRealTimers());
it('keeps authority rechecks active throughout a silent or reconnecting stream, even after an HTTP read succeeds', async () => {
  vi.useFakeTimers(); const read = vi.fn(); const guard = configurationAuthorityFallback(read);
  await vi.advanceTimersByTimeAsync(3000); expect(read).toHaveBeenCalledTimes(3);
  guard.confirmed(); await vi.advanceTimersByTimeAsync(3000); expect(read).toHaveBeenCalledTimes(3);
  guard.unavailable(); expect(read).toHaveBeenCalledTimes(4);
  await vi.advanceTimersByTimeAsync(2000); expect(read).toHaveBeenCalledTimes(6);
  guard.dispose(); await vi.advanceTimersByTimeAsync(3000); expect(read).toHaveBeenCalledTimes(6);
});
it('coalesces repeated transport failures and ignores confirmation or pending callbacks after disposal', async () => {
  vi.useFakeTimers(); const read = vi.fn(); const guard = configurationAuthorityFallback(read);
  guard.unavailable(); guard.unavailable(); expect(vi.getTimerCount()).toBe(1);
  guard.confirmed(); expect(vi.getTimerCount()).toBe(0);
  guard.pending(); expect(vi.getTimerCount()).toBe(1);
  guard.dispose(); guard.pending(); guard.unavailable(); guard.confirmed();
  await vi.advanceTimersByTimeAsync(3000); expect(read).toHaveBeenCalledTimes(2); expect(vi.getTimerCount()).toBe(0);
});
