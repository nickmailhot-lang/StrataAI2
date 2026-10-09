// ARCH-02-FR-009: all feature clients share one safe Problem boundary.
// Add a code deliberately when a feature needs it; unknown response text is
// never a user-facing message, retry key, audit entry or analytics label.
const codes = new Set([
  'invalid_credentials', 'email_verification_required', 'account_unavailable',
  'idempotency_key_expired', 'idempotency_key_reused', 'identity_retry_key_unavailable',
  'email_unavailable', 'self_registration_disabled', 'identity_delivery_unavailable',
  'identity_storage_unavailable', 'invalid_or_expired_invitation', 'invalid_or_expired_token',
  'mention_handle_invalid', 'mention_handle_unavailable', 'mention_handle_claim_refused',
  'invalid_mention_cursor', 'mention_prefix_invalid',
  'invalid_comment_mentions', 'mention_targets_changed',
  'invalid_idempotency_key',
  'invalid_password', 'rate_limit_exceeded', 'invalid_organization_logo_url',
  'sole_owner', 'member_not_found', 'member_version_conflict', 'board_not_found', 'sole_board_admin',
  'version_conflict', 'organization_owner_required', 'ownership_changed',
  'organization_not_found', 'session_unavailable', 'organization_storage_unavailable',
  'invalid_access_surface',
  'attachment_upload_in_progress', 'attachment_type_not_allowed',
  'invalid_email', 'invalid_invitation_role', 'invalid_invitation_surface',
  'invalid_display_name', 'invalid_version', 'invalid_avatar_url', 'invalid_locale', 'invalid_timezone',
]);

async function boundedBody(response: Response): Promise<unknown> {
  const reader = response.body?.getReader();
  if (!reader) return undefined;
  let deadline: ReturnType<typeof setTimeout> | undefined;
  try {
    return await Promise.race([
      (async () => {
        const chunks: Uint8Array[] = []; let bytes = 0;
        while (true) {
          const chunk = await reader.read();
          if (chunk.done) break;
          bytes += chunk.value.byteLength;
          if (bytes > 16_384) return undefined;
          chunks.push(chunk.value);
        }
        const data = new Uint8Array(bytes); let offset = 0;
        for (const chunk of chunks) { data.set(chunk, offset); offset += chunk.byteLength; }
        return JSON.parse(new TextDecoder().decode(data)) as unknown;
      })(),
      new Promise<undefined>(resolve => { deadline = setTimeout(() => resolve(undefined), 5000); }),
    ]);
  } catch { return undefined; }
  finally {
    clearTimeout(deadline);
    // Do not wait for a stalled server to acknowledge body cancellation.
    void reader.cancel().catch(() => {});
  }
}

export async function normalizeApiProblem(response: Response): Promise<Response> {
  if (response.ok || response.status >= 200 && response.status < 300 || response.status < 200 || response.status > 599) return response;
  const value = await boundedBody(response);
  const candidate = value && typeof value === 'object' && 'code' in value ? value.code : undefined;
  const code = typeof candidate === 'string' && codes.has(candidate)
    && (candidate !== 'attachment_type_not_allowed' || response.status === 400) ? candidate : undefined;
  const headers = new Headers(response.headers);
  headers.delete('Content-Length'); headers.delete('Content-Encoding');
  headers.set('Content-Type', 'application/problem+json');
  return new Response(JSON.stringify({ type: 'about:blank', title: 'Unable to complete this request.', status: response.status,
    ...(code ? { code } : {}) }), { status: response.status, headers });
}
