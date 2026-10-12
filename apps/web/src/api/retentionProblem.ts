const conflicts = new Set(['version_conflict', 'idempotency_key_conflict', 'idempotency_key_expired',
  'retention_policy_scope_unavailable', 'retention_policy_scope_immutable', 'retention_hold_prevents_disposition']);
const invalid = new Set(['idempotency_key_required', 'invalid_retention_policy_cursor', 'invalid_retention_policy_request',
  ...['identity', 'version', 'record_class', 'jurisdiction', 'anchor', 'retention_days', 'disposition', 'source', 'timestamps']
    .map(field => `invalid_retention_policy_${field}`)]);
export function retentionProblemCode(value: unknown, status: number): string | undefined {
  if (typeof value !== 'string') return undefined;
  if (status === 409 && conflicts.has(value) || status === 400 && invalid.has(value)
    || status === 503 && (value === 'retention_policy_source_unavailable' || value === 'retention_disposition_unavailable')
    || status === 404 && value === 'retention_policy_not_found') return value;
  return undefined;
}
