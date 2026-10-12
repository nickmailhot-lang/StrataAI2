// Fixed local messages only. Server titles, reasons and identifiers are private.
export const retentionHoldDeletionNotice = 'Permanent deletion is blocked by a retention hold. Ask an authorized Records administrator to review it.';
export const retentionDispositionUnavailableNotice = 'Retention requirements could not be confirmed. The original deletion is retained; retry that same request when access and storage are available.';
export function retentionDispositionNotice(status: number, code: unknown): string | undefined {
  if (status === 409 && code === 'retention_hold_prevents_disposition') return retentionHoldDeletionNotice;
  if (status === 503 && code === 'retention_disposition_unavailable') return retentionDispositionUnavailableNotice;
  return undefined;
}
