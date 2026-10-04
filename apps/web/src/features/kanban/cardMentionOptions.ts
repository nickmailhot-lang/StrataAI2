import { normalizeMentionHandle } from '../auth/mentionHandle';
import { notificationUuid } from '../notifications/notificationInbox';
import type { AttachmentScope } from './attachments';

export type CardMentionOption = Readonly<{ userId: string; handle: string; displayName: string; handleVersion: number }>;
export type CardMentionOptions = Readonly<AttachmentScope & { cardVersion: number; prefix: string;
  items: readonly CardMentionOption[]; nextCursor: string | null }>;
const invalid = () => new Error('Invalid Card mention options');
const revision = (value: unknown): value is number => Number.isSafeInteger(value) && Number(value) > 0;
const same = (value: unknown, expected: string) => notificationUuid(value) && notificationUuid(expected) && value.toLowerCase() === expected.toLowerCase();
function record(value: unknown, fields: string[]) {
  if (!value || typeof value !== 'object' || Array.isArray(value)) throw invalid();
  const row = value as Record<string, unknown>;
  if (Object.keys(row).length !== fields.length || fields.some(field => !Object.hasOwn(row, field))) throw invalid();
  return row;
}
export function normalizeMentionPrefix(value: string) {
  if (typeof value !== 'string' || value.length > 40) throw invalid();
  const whitespace = '[\\u0009-\\u000d\\u0020\\u0085\\u00a0\\u1680\\u2000-\\u200a\\u2028\\u2029\\u202f\\u205f\\u3000]';
  const prefix = value.replace(new RegExp(`^${whitespace}+|${whitespace}+$`, 'g'), '').toLowerCase();
  if (prefix !== '' && !/^[a-z][a-z0-9_]{0,39}$/.test(prefix)) throw invalid();
  return prefix;
}
function anchor(value: unknown, scope: AttachmentScope, version: number, prefix: string) {
  if (typeof value !== 'string' || value.length > 160) throw invalid();
  const parts = value.split('/');
  if (parts.length !== 4 || parts[0] !== scope.cardId.toLowerCase() || parts[1] !== String(version) || parts[2] !== prefix
    || !/^[a-z][a-z0-9_]{2,39}$/.test(parts[3]) || ['card', 'board'].includes(parts[3]) || !parts[3].startsWith(prefix)) throw invalid();
  return parts[3];
}
// Immutable discovery metadata; a later comment producer must independently
// resolve current handles and admit recipients in its owning transaction.
export function parseCardMentionOptions(value: unknown, scope: AttachmentScope, version: number, prefix: string,
  previousCursor?: string): CardMentionOptions {
  const row = record(value, ['organizationId', 'boardId', 'cardId', 'cardVersion', 'prefix', 'items', 'nextCursor']);
  if (!same(row.organizationId, scope.organizationId) || !same(row.boardId, scope.boardId) || !same(row.cardId, scope.cardId)
    || !revision(version) || row.cardVersion !== version || normalizeMentionPrefix(prefix) !== prefix || row.prefix !== prefix
    || !Array.isArray(row.items) || row.items.length > 20) throw invalid();
  const ids = new Set<string>(); let previous = previousCursor === undefined ? undefined : anchor(previousCursor, scope, version, prefix);
  const items = row.items.map(value => {
    const item = record(value, ['userId', 'handle', 'displayName', 'handleVersion']);
    if (!notificationUuid(item.userId) || typeof item.handle !== 'string' || normalizeMentionHandle(item.handle, item.userId) !== item.handle
      || !item.handle.startsWith(prefix) || previous !== undefined && item.handle <= previous || !revision(item.handleVersion)
      || typeof item.displayName !== 'string' || !item.displayName.trim() || item.displayName.length > 120
      || ids.has(item.userId.toLowerCase())) throw invalid();
    ids.add(item.userId.toLowerCase()); previous = item.handle;
    return Object.freeze({ ...item }) as CardMentionOption;
  });
  if (row.nextCursor !== null) {
    const next = anchor(row.nextCursor, scope, version, prefix);
    if (items.length !== 20 || next !== previous || row.nextCursor === previousCursor) throw invalid();
  }
  return Object.freeze({ organizationId: scope.organizationId, boardId: scope.boardId, cardId: scope.cardId,
    cardVersion: version, prefix, items: Object.freeze(items), nextCursor: row.nextCursor as string | null });
}
