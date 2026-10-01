export type InvitationInput = { email: string; surface: 'INTERNAL' | 'PORTAL'; targetRole: string };
export type InvitationIntent = { key: string; input: InvitationInput };
const prefix = 'strataai:invitation-create:v1:';
export const invitationIntentKey = (actor: string, organization: string) => `${prefix}${actor}:${organization}`;
export const validInvitationKey = (value: unknown): value is string => typeof value === 'string'
  && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value)
  && value !== '00000000-0000-0000-0000-000000000000';
export const invitationRoles = (surface: string) => surface === 'INTERNAL' ? ['MEMBER', 'ADMIN', 'OWNER']
  : ['OWNER', 'CO_OWNER', 'TENANT', 'OCCUPANT', 'AUTHORIZED_REPRESENTATIVE', 'OTHER'];
export function readInvitationIntent(storageKey: string): InvitationIntent | undefined {
  const saved = sessionStorage.getItem(storageKey); if (saved === null) return;
  const value = JSON.parse(saved) as InvitationIntent;
  if (!value || !validInvitationKey(value.key) || !value.input || typeof value.input.email !== 'string'
    || !value.input.email.trim() || value.input.email.length > 320 || !['INTERNAL', 'PORTAL'].includes(value.input.surface)
    || !invitationRoles(value.input.surface).includes(value.input.targetRole)) throw new Error('Invalid saved invitation intent');
  return value;
}
export function saveInvitationIntent(storageKey: string, intent: InvitationIntent) {
  sessionStorage.setItem(storageKey, JSON.stringify(intent));
  // Do not send a mutation if this browser did not retain the retry intent.
  if (sessionStorage.getItem(storageKey) !== JSON.stringify(intent)) throw new Error('Invitation intent was not retained');
}
export function forgetInvitationIntents() {
  try {
    for (let index = sessionStorage.length - 1; index >= 0; index--) {
      const key = sessionStorage.key(index); if (key?.startsWith(prefix)) sessionStorage.removeItem(key);
    }
  } catch { /* Sign-out acknowledgment must not depend on local draft storage. */ }
}
