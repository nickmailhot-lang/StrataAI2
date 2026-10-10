import { publicCorrelationReference } from '../../api/correlationReference';
import { useEffect, useRef, useState } from 'react';
import { apiFetch } from '../../api/apiFetch';

/** Own the entire response deadline and cancel when a recovery screen closes. */
export function useRecoveryRequest() {
  const [busy, setBusy] = useState(false);
  const pending = useRef<AbortController | undefined>(undefined);
  const attempt = useRef<{ path: string; body: string; key: string } | undefined>(undefined);
  useEffect(() => () => { pending.current?.abort(); pending.current = undefined; attempt.current = undefined; }, []);
  async function request(path: string, body: object) {
    if (pending.current) return undefined;
    const controller = new AbortController();
    pending.current = controller;
    setBusy(true);
    const serialized = JSON.stringify(body);
    const issuance = path === '/auth/password/forgot' || path === '/auth/verification/resend';
    const consumption = path === '/auth/password/reset' || path === '/auth/verify-email';
    if ((issuance || consumption) && (attempt.current?.path !== path || attempt.current.body !== serialized)) {
      attempt.current = { path, body: serialized, key: crypto.randomUUID() };
    }
    let deadline: ReturnType<typeof setTimeout> | undefined;
    let cancelled: (() => void) | undefined;
    let reference: string | null = null;
    try {
      const result = await Promise.race([
        (async () => {
          const headers: Record<string, string> = { 'Content-Type': 'application/json' };
          if (issuance || consumption) headers['Idempotency-Key'] = attempt.current!.key;
          const response = await apiFetch(path, { method: 'POST', headers,
            body: serialized, signal: controller.signal });
          reference = publicCorrelationReference(response.headers?.get('X-Correlation-ID') ?? null);
          const value: unknown = await response.json();
          return { status: response.status, value, reference };
        })(),
        new Promise<never>((_, reject) => {
          cancelled = () => reject(new Error('Recovery request cancelled'));
          controller.signal.addEventListener('abort', cancelled, { once: true });
          deadline = setTimeout(() => controller.abort(), 15_000);
        }),
      ]);
      if (pending.current !== controller || controller.signal.aborted) return undefined;
      if (issuance && result.status === 202 && recoveryObject(result.value).accepted === true) attempt.current = undefined;
      if (consumption && result.status === 200 && recoveryProfileConfirmed(result.value)
        && (path !== '/auth/verify-email' || recoveryObject(result.value).emailVerified === true)) attempt.current = undefined;
      return result;
    } catch {
      return pending.current === controller ? { status: 0, value: null, reference } : undefined;
    } finally {
      clearTimeout(deadline);
      if (cancelled) controller.signal.removeEventListener('abort', cancelled);
      if (pending.current === controller) { pending.current = undefined; setBusy(false); }
    }
  }
  return { busy, request };
}

export function recoveryObject(value: unknown): Record<string, unknown> {
  return value !== null && typeof value === 'object' && !Array.isArray(value) ? value as Record<string, unknown> : {};
}

export function recoveryProfileConfirmed(value: unknown) {
  const profile = recoveryObject(value);
  return typeof profile.id === 'string'
    && /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(profile.id)
    && typeof profile.email === 'string' && profile.email.includes('@')
    && typeof profile.version === 'number' && Number.isSafeInteger(profile.version) && profile.version > 0
    && typeof profile.emailVerified === 'boolean';
}

export function recoveryError(value: unknown, fallback: string) {
  const code = recoveryObject(value).code;
  const messages: Record<string, string> = {
    invalid_or_expired_token: 'The token is invalid or expired.',
    invalid_password: 'Use a password that meets the stated requirements.',
    identity_delivery_unavailable: 'Email is temporarily unavailable. Please retry later.',
    identity_storage_unavailable: 'The request could not be confirmed. Please retry later.',
    identity_retry_key_unavailable: 'This retry could not be confirmed. Contact support before starting another attempt.',
    rate_limit_exceeded: 'Too many requests. Please wait and retry.',
  };
  return typeof code === 'string' ? messages[code] ?? fallback : fallback;
}
