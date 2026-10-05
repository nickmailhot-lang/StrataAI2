import { boundedWorkRead, workRequest } from '../../api/workManagement';
import { notificationUuid as uuid } from '../notifications/notificationInbox';
import { ChangedSearchInteractionActor, parseBoardFilterInteraction, SearchInteractionAcknowledgments } from './searchInteraction';

export type BoardFilterCriteria = { keyword: string; labels: string[]; members: string[]; match: 'all' | 'any';
  completion?: 'all' | 'complete' | 'incomplete'; due?: 'all' | 'none' | 'overdue' | 'upcoming'; activity?: 'all' | 'day' | 'week' | 'month' };
export type BoardFilterChangeScope = { actor: string; organization: string; board: string };
export type BoardFilterChangeIntent = Readonly<BoardFilterChangeScope & { key: string; query: string; createdAt: number }>;
const lifetime = 24 * 60 * 60 * 1000;
export class ExpiredBoardFilterChange extends Error {}
export class BoardFilterRecoveryConflict extends Error {}
export async function verifyBoardFilterActor(actor: string, signal: AbortSignal) {
  const profile = await workRequest<{ id: unknown }>('/me', { signal });
  if (!uuid(actor) || !uuid(profile?.id) || profile.id.toLowerCase() !== actor.toLowerCase()) throw new ChangedSearchInteractionActor();
}
const fields = ['actor', 'board', 'createdAt', 'key', 'organization', 'query'];
export function boardFilterChangeCriteria(intent: BoardFilterChangeIntent): BoardFilterCriteria {
  const query = new URLSearchParams(intent.query);
  return { keyword: query.get('keyword') ?? '', labels: query.get('labels')?.split(',').filter(Boolean) ?? [],
    members: query.get('members')?.split(',').filter(Boolean) ?? [], match: query.get('match') as BoardFilterCriteria['match'],
    ...(query.get('completion') === 'all' ? {} : { completion: query.get('completion') as BoardFilterCriteria['completion'] }),
    ...(query.get('due') === 'all' ? {} : { due: query.get('due') as BoardFilterCriteria['due'] }),
    ...(query.get('activity') === 'all' ? {} : { activity: query.get('activity') as BoardFilterCriteria['activity'] }) };
}
function scopeKey(scope: BoardFilterChangeScope) {
  if (![scope.actor, scope.organization, scope.board].every(uuid)) throw new Error('Invalid filter account or scope');
  return `strataai:board-filter-change:v1:${scope.actor.toLowerCase()}:${scope.organization.toLowerCase()}:${scope.board.toLowerCase()}`;
}
function canonicalQuery(change: string, criteria: BoardFilterCriteria) {
  const lists = [criteria.labels, criteria.members];
  if (!['apply', 'clear'].includes(change) || typeof criteria.keyword !== 'string' || criteria.keyword.trim().length > 160
    || lists.some(ids => !Array.isArray(ids) || ids.length > 25 || !ids.every(uuid) || new Set(ids.map(id => id.toLowerCase())).size !== ids.length)
    || !['all', 'any'].includes(criteria.match) || !['all', 'complete', 'incomplete'].includes(criteria.completion ?? 'all')
    || !['all', 'none', 'overdue', 'upcoming'].includes(criteria.due ?? 'all') || !['all', 'day', 'week', 'month'].includes(criteria.activity ?? 'all'))
    throw new Error('Invalid filter criteria');
  const query = new URLSearchParams({ change, keyword: criteria.keyword.trim(), labels: criteria.labels.map(id => id.toLowerCase()).sort().join(','),
    members: criteria.members.map(id => id.toLowerCase()).sort().join(','), match: criteria.match,
    completion: criteria.completion ?? 'all', due: criteria.due ?? 'all', activity: criteria.activity ?? 'all' });
  if (change === 'clear' && (criteria.keyword.trim() !== '' || criteria.labels.length || criteria.members.length || criteria.match !== 'all'
    || (criteria.completion ?? 'all') !== 'all' || (criteria.due ?? 'all') !== 'all' || (criteria.activity ?? 'all') !== 'all')) throw new Error('Invalid Clear intent');
  return query.toString();
}
export function createBoardFilterChange(scope: BoardFilterChangeScope, change: 'apply' | 'clear', criteria: BoardFilterCriteria, now = Date.now()): BoardFilterChangeIntent {
  scopeKey(scope);
  if (!Number.isSafeInteger(now) || now < 0) throw new Error('Invalid filter intent clock');
  return Object.freeze({ actor: scope.actor.toLowerCase(), organization: scope.organization.toLowerCase(), board: scope.board.toLowerCase(),
    key: crypto.randomUUID(), query: canonicalQuery(change, criteria), createdAt: now });
}
function validateIntent(value: BoardFilterChangeIntent, scope: BoardFilterChangeScope, now: number) {
  if (!Number.isSafeInteger(now) || !Number.isSafeInteger(value.createdAt) || value.createdAt < 0
    || now < value.createdAt || now - value.createdAt >= lifetime) throw new ExpiredBoardFilterChange('Invalid or expired filter intent');
  const query = new URLSearchParams(value.query);
  const expected = canonicalQuery(query.get('change') ?? '', { keyword: query.get('keyword') ?? '',
    labels: query.get('labels')?.split(',').filter(Boolean) ?? [], members: query.get('members')?.split(',').filter(Boolean) ?? [],
    match: query.get('match') as BoardFilterCriteria['match'], completion: query.get('completion') as BoardFilterCriteria['completion'],
    due: query.get('due') as BoardFilterCriteria['due'], activity: query.get('activity') as BoardFilterCriteria['activity'] });
  if (Object.keys(value).sort().join(',') !== fields.join(',') || !uuid(value.key) || scopeKey(value) !== scopeKey(scope)
    || value.query !== expected) throw new Error('Invalid filter intent');
}
export function restoreBoardFilterChange(storage: Storage, scope: BoardFilterChangeScope, now = Date.now()): BoardFilterChangeIntent | undefined {
  const key = scopeKey(scope);
  try {
    const value = JSON.parse(storage.getItem(key) ?? 'null') as BoardFilterChangeIntent | null;
    if (!value) return undefined;
    validateIntent(value, scope, now);
    return Object.freeze(value);
  } catch { try { storage.removeItem(key); } catch { /* Optional session storage. */ } return undefined; }
}
export function retainBoardFilterChange(storage: Storage, intent: BoardFilterChangeIntent, now = Date.now()) {
  // One unresolved original per actor/Board; retain before the first request.
  validateIntent(intent, intent, now);
  const key = scopeKey(intent), previous = restoreBoardFilterChange(storage, intent, now);
  if (previous && JSON.stringify(previous) !== JSON.stringify(intent)) throw new BoardFilterRecoveryConflict('Recover the original filter intent first');
  if (!previous && Object.keys(storage).filter(key => key.startsWith('strataai:board-filter-change:v1:')).length >= 1000)
    throw new BoardFilterRecoveryConflict('Filter recovery storage is full');
  storage.setItem(key, JSON.stringify(intent));
}
export function discardBoardFilterChange(storage: Storage, scope: BoardFilterChangeScope) { storage.removeItem(scopeKey(scope)); }
export async function submitBoardFilterChange(intent: BoardFilterChangeIntent, signal: AbortSignal,
  acknowledgments: SearchInteractionAcknowledgments, now = Date.now()) {
  validateIntent(intent, intent, now);
  return boundedWorkRead(async currentSignal => {
    await verifyBoardFilterActor(intent.actor, currentSignal);
    const value = await workRequest<unknown>(`/boards/${encodeURIComponent(intent.board)}/cards/filter-change?${intent.query}`, {
      method: 'POST', signal: currentSignal, headers: { 'Idempotency-Key': intent.key, 'X-StrataAI-Expected-Actor': intent.actor } });
    await verifyBoardFilterActor(intent.actor, currentSignal); currentSignal.throwIfAborted(); signal.throwIfAborted();
    const source = parseBoardFilterInteraction(value, intent.actor, intent.organization, intent.board);
    return { source, firstAcknowledgment: acknowledgments.consume(source) };
  }, signal);
}
