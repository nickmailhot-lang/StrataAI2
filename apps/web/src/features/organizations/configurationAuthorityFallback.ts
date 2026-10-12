// A successful HTTP read does not prove that a disconnected live stream can
// observe a later session/permission withdrawal. Keep admission checks active
// during that gap; the page coalesces them behind its single bounded read.
export function configurationAuthorityFallback(unavailable: () => void) {
  let disposed = false;
  let timer: ReturnType<typeof setTimeout> | undefined;
  function pending() {
    if (disposed || timer !== undefined) return;
    timer = setTimeout(() => {
      timer = undefined;
      if (disposed) return;
      unavailable(); pending();
    }, 1000);
  }
  function confirmed() { clearTimeout(timer); timer = undefined; }
  pending();
  return {
    pending,
    unavailable() { if (!disposed) { unavailable(); pending(); } },
    confirmed,
    dispose() { disposed = true; confirmed(); },
  };
}
