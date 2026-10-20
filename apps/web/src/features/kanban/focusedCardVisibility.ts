// Red-control shim: the current Board has no initial focus reveal observer.
// Replace with the already-authored shared reveal implementation after red.
export function observeFocusedCardVisibility(
  _links: ReadonlyMap<string, HTMLAnchorElement>,
  _canReveal: () => boolean,
): () => void {
  return () => {};
}
