// Use the exact public identifier contract from CorrelationIdMiddleware.
export function publicCorrelationReference(value: string | null): string | null {
  return typeof value === 'string' && value.length > 0 && value.length <= 64
    && !/[^A-Za-z0-9._-]/.test(value) ? value : null;
}
