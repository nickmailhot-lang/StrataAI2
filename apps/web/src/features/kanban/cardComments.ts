import { notificationUuid } from '../notifications/notificationInbox';
import { dateInstantTicks } from './cardDates';
import type { AttachmentScope } from './attachments';

export type CardComment = { id: string; organizationId: string; cardId: string; authorId: string; content: string | null;
  createdAt: string; updatedAt: string; version: number; editedAt: string | null; deletedAt: string | null; deletedBy: string | null };
export type CardCommentPage = AttachmentScope & { cardVersion: number; items: CardComment[]; nextCursor: string | null; canComment: boolean };
export type CardCommentChange = AttachmentScope & { cardVersion: number; comment: CardComment; changed: boolean };
export type CommentIntent = { cardVersion: number; actor: string; original: CardComment | null; content?: string; deleting?: boolean; confirmed?: boolean };
const invalid = () => new Error('Invalid Card comment response');
const same = (value: unknown, expected: string) => notificationUuid(value) && notificationUuid(expected) && value.toLowerCase() === expected.toLowerCase();
const revision = (value: unknown): value is number => Number.isSafeInteger(value) && Number(value) > 0;
function row(value: unknown, fields: string[]) {
  if (!value || typeof value !== 'object' || Array.isArray(value)) throw invalid();
  const result = value as Record<string, unknown>;
  if (Object.keys(result).length !== fields.length || fields.some(field => !Object.hasOwn(result, field))) throw invalid();
  return result;
}
function instant(value: unknown) {
  if (typeof value !== 'string') throw invalid();
  const ticks = dateInstantTicks(value) + 621355968000000000n;
  if (ticks < 0n || ticks > 3155378975999999999n || ticks % 10n !== 0n) throw invalid();
  return ticks;
}
export function normalizeComment(value: string) {
  if (typeof value !== 'string' || value.length > 10000 || /[\p{Cc}]/u.test(value.replace(/[\r\n\t]/g, ''))) throw invalid();
  for (let i = 0; i < value.length; i++) {
    const unit = value.charCodeAt(i);
    if (unit >= 0xd800 && unit <= 0xdbff) {
      const next = value.charCodeAt(++i); if (!(next >= 0xdc00 && next <= 0xdfff)) throw invalid();
    } else if (unit >= 0xdc00 && unit <= 0xdfff) throw invalid();
  }
  // Match .NET Unicode whitespace; JavaScript trim additionally removes FEFF.
  const whitespace = '[\\u0009-\\u000d\\u0020\\u0085\\u00a0\\u1680\\u2000-\\u200a\\u2028\\u2029\\u202f\\u205f\\u3000]';
  const normalized = value.replace(/\r\n?/g, '\n').replace(new RegExp(`^${whitespace}+|${whitespace}+$`, 'g'), '');
  if (!normalized) throw invalid(); return normalized;
}
function comment(value: unknown, scope: AttachmentScope): CardComment {
  const result = row(value, ['id', 'organizationId', 'cardId', 'authorId', 'content', 'createdAt', 'updatedAt', 'version', 'editedAt', 'deletedAt', 'deletedBy']);
  if (!notificationUuid(result.id) || !notificationUuid(result.authorId) || !same(result.organizationId, scope.organizationId)
    || !same(result.cardId, scope.cardId) || !revision(result.version)) throw invalid();
  const created = instant(result.createdAt); const updated = instant(result.updatedAt);
  const edited = result.editedAt === null ? null : instant(result.editedAt);
  if (updated < created || edited !== null && (edited < created || edited > updated)) throw invalid();
  if (result.deletedAt === null) {
    if (result.deletedBy !== null || typeof result.content !== 'string' || normalizeComment(result.content) !== result.content
      || (result.version === 1 ? edited !== null || created !== updated : edited !== updated)) throw invalid();
  } else if (result.content !== null || !same(result.deletedBy, result.authorId as string) || instant(result.deletedAt) !== updated
    || result.version < 2 || (edited === null ? result.version !== 2 : result.version < 3)) throw invalid();
  return result as CardComment;
}
function scoped(value: unknown, scope: AttachmentScope, version: number, fields: string[]) {
  const result = row(value, fields);
  if (!same(result.organizationId, scope.organizationId) || !same(result.boardId, scope.boardId) || !same(result.cardId, scope.cardId)
    || !revision(version) || result.cardVersion !== version) throw invalid();
  return result;
}
function position(value: unknown, scope: AttachmentScope, version: number) {
  if (typeof value !== 'string' || value.length > 160) throw invalid();
  const parts = value.split('/');
  if (parts.length !== 4 || !same(parts[0], scope.cardId) || parts[1] !== String(version)
    || !/^(0|[1-9][0-9]{0,18})$/.test(parts[2]) || !notificationUuid(parts[3])) throw invalid();
  const ticks = BigInt(parts[2]); if (ticks > 3155378975999999999n || ticks % 10n !== 0n) throw invalid();
  return { ticks, id: parts[3].toLowerCase() };
}
export function parseCardCommentPage(value: unknown, scope: AttachmentScope, version: number, previousCursor?: string): CardCommentPage {
  const result = scoped(value, scope, version, ['organizationId', 'boardId', 'cardId', 'cardVersion', 'items', 'nextCursor', 'canComment']);
  if (typeof result.canComment !== 'boolean' || !Array.isArray(result.items) || result.items.length > 50) throw invalid();
  const ids = new Set<string>(); let previous = previousCursor ? position(previousCursor, scope, version) : undefined;
  for (const value of result.items) {
    const item = comment(value, scope); const current = { ticks: instant(item.createdAt), id: item.id.toLowerCase() };
    if (ids.has(current.id) || previous && (current.ticks > previous.ticks || current.ticks === previous.ticks && current.id >= previous.id)) throw invalid();
    ids.add(current.id); previous = current;
  }
  if (result.nextCursor !== null) {
    const next = position(result.nextCursor, scope, version);
    if (result.items.length !== 50 || !previous || next.ticks !== previous.ticks || next.id !== previous.id || result.nextCursor === previousCursor) throw invalid();
  }
  return result as CardCommentPage;
}
export function parseCardCommentChange(value: unknown, scope: AttachmentScope, intent: CommentIntent): CardCommentChange {
  if (!revision(intent.cardVersion) || !notificationUuid(intent.actor)) throw invalid();
  const original = intent.original === null ? null : comment(intent.original, scope);
  if (original && !same(original.authorId, intent.actor) || intent.deleting && (!original || !intent.confirmed)) throw invalid();
  if (!intent.deleting && original?.deletedAt !== undefined && original.deletedAt !== null) throw invalid();
  const content = intent.deleting ? null : normalizeComment(intent.content!);
  const changed = !original || (intent.deleting ? original.deletedAt === null : original.content !== content);
  const result = scoped(value, scope, intent.cardVersion + (changed ? 1 : 0), ['organizationId', 'boardId', 'cardId', 'cardVersion', 'comment', 'changed']);
  const current = comment(result.comment, scope);
  if (result.changed !== changed || !same(current.authorId, intent.actor) || current.content !== content
    || current.version !== (original ? original.version + (changed ? 1 : 0) : 1)) throw invalid();
  if (original) {
    if (!same(current.id, original.id) || instant(current.createdAt) !== instant(original.createdAt)
      || instant(current.updatedAt) < instant(original.updatedAt)) throw invalid();
    if (intent.deleting && (current.deletedAt === null || current.editedAt !== original.editedAt)) throw invalid();
    if (!changed && Object.keys(original).some(key => original[key as keyof CardComment] !== current[key as keyof CardComment])) throw invalid();
  } else if (intent.deleting || current.deletedAt !== null) throw invalid();
  return result as CardCommentChange;
}
