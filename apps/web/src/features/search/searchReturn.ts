import { notificationUuid as uuid } from '../notifications/notificationInbox';

export type SearchCriteria = { q: string; label: string; member: string; match: 'all' | 'any'; scope: 'active' | 'archived' };
export type SearchReturn = { organization: string; actor: string; entry: string; card: string;
  draft: SearchCriteria; applied: SearchCriteria };

// One tab-local, single-use return record. Never retain result content or
// metadata, counts, cursors or grants in history, storage or this record.
let retained: SearchReturn | undefined;
function criteria(value: SearchCriteria) {
  return [value.q, value.label, value.member].every(x => typeof x === 'string' && x.length <= 160)
    && ['all', 'any'].includes(value.match) && ['active', 'archived'].includes(value.scope);
}
function copy(value: SearchCriteria): SearchCriteria {
  return { q: value.q, label: value.label, member: value.member, match: value.match, scope: value.scope };
}
export function retainSearchReturn(value: SearchReturn): void {
  retained = undefined;
  if (!uuid(value.organization) || !uuid(value.actor) || !uuid(value.card)
    || !value.entry || value.entry.length > 128 || !criteria(value.draft) || !criteria(value.applied)) return;
  retained = { organization: value.organization.toLowerCase(), actor: value.actor.toLowerCase(),
    entry: value.entry, card: value.card.toLowerCase(), draft: copy(value.draft), applied: copy(value.applied) };
}
export function readSearchReturn(organization: string | undefined, entry: string): SearchReturn | undefined {
  const value = retained;
  return value?.organization === organization?.toLowerCase() && value?.entry === entry ? value : undefined;
}
export function forgetSearchReturn(organization?: string): void {
  if (!organization || retained?.organization === organization.toLowerCase()) retained = undefined;
}
