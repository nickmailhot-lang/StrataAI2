// The existing idempotent creation contract also derives Guid values from a
// command hash; those IDs need not have RFC version/variant bits.
const uuid = '[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}';
const boardOrCard = new RegExp(`^/app/${uuid}/boards/${uuid}(?:/cards/${uuid})?/?$`, 'i');

// Navigation context is untrusted, never an authorization grant. Accept only
// existing internal Board/Card routes, without query data, fragments or redirects.
export function signInReturnPath(value: unknown): string | undefined {
  return typeof value === 'string' && boardOrCard.exec(value)?.[0] === value ? value : undefined;
}
