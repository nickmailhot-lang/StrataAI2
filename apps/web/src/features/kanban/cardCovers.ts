import { notificationUuid } from '../notifications/notificationInbox';
import { dateInstantTicks } from './cardDates';
import type { AttachmentScope } from './attachments';

export type CardCoverView = AttachmentScope & { cardVersion: number; attachmentId: string | null; attachmentVersion: number | null; canEdit: boolean; isPublic: boolean };
export type CardCoverCandidate = { attachmentId: string; attachmentVersion: number; displayName: string; createdAt: string };
export type CardCoverCandidatePage = AttachmentScope & { cardVersion: number; items: CardCoverCandidate[]; nextCursor: string | null; canEdit: boolean; isPublic: boolean };
export type CardCoverIntent = { attachmentId: string | null; attachmentVersion: number | null; cardVersion: number; publicVisibilityConfirmed: boolean };
export type CardCoverChange = AttachmentScope & { cardVersion: number; attachmentId: string | null; attachmentVersion: number | null; changed: boolean };
const invalid = () => new Error('Invalid Card cover response');
const same = (value: unknown, expected: string) => notificationUuid(value) && notificationUuid(expected) && value.toLowerCase() === expected.toLowerCase();
const revision = (value: unknown): value is number => Number.isSafeInteger(value) && Number(value) > 0;
function row(value: unknown, fields: string[]) {
  if (!value || typeof value !== 'object' || Array.isArray(value)) throw invalid();
  const result = value as Record<string, unknown>;
  if (Object.keys(result).length !== fields.length || fields.some(field => !Object.hasOwn(result, field))) throw invalid();
  return result;
}
function scoped(value: unknown, scope: AttachmentScope, version: number, fields: string[]) {
  const result = row(value, fields);
  if (!same(result.organizationId, scope.organizationId) || !same(result.boardId, scope.boardId) || !same(result.cardId, scope.cardId)
    || !revision(version) || result.cardVersion !== version) throw invalid();
  return result;
}
function source(id: unknown, version: unknown) {
  if (id === null && version === null) return;
  if (!notificationUuid(id) || !revision(version) || version < 3) throw invalid();
}
function instant(value: unknown) {
  if (typeof value !== 'string') throw invalid();
  const ticks = dateInstantTicks(value) + 621355968000000000n;
  if (ticks < 0n || ticks > 3155378975999999999n || ticks % 10n !== 0n) throw invalid();
  return ticks;
}
export function parseCardCoverView(value: unknown, scope: AttachmentScope, version: number): CardCoverView {
  const result = scoped(value, scope, version, ['organizationId', 'boardId', 'cardId', 'cardVersion', 'attachmentId', 'attachmentVersion', 'canEdit', 'isPublic']);
  source(result.attachmentId, result.attachmentVersion);
  if (typeof result.canEdit !== 'boolean' || typeof result.isPublic !== 'boolean') throw invalid();
  return result as CardCoverView;
}
function position(value: unknown, scope: AttachmentScope, version: number) {
  if (typeof value !== 'string' || value.length > 160) throw invalid();
  const parts = value.split('/');
  if (parts.length !== 4 || !same(parts[0], scope.cardId) || parts[1] !== String(version)
    || !/^(0|[1-9][0-9]{0,18})$/.test(parts[2]) || !notificationUuid(parts[3])) throw invalid();
  const ticks = BigInt(parts[2]);
  if (ticks > 3155378975999999999n || ticks % 10n !== 0n) throw invalid();
  return { ticks, id: parts[3].toLowerCase() };
}
export function parseCardCoverCandidates(value: unknown, scope: AttachmentScope, version: number, previousCursor?: string): CardCoverCandidatePage {
  const result = scoped(value, scope, version, ['organizationId', 'boardId', 'cardId', 'cardVersion', 'items', 'nextCursor', 'canEdit', 'isPublic']);
  if (typeof result.canEdit !== 'boolean' || typeof result.isPublic !== 'boolean' || !Array.isArray(result.items) || result.items.length > 50) throw invalid();
  const ids = new Set<string>(); let previous = previousCursor ? position(previousCursor, scope, version) : undefined;
  for (const value of result.items) {
    const item = row(value, ['attachmentId', 'attachmentVersion', 'displayName', 'createdAt']);
    source(item.attachmentId, item.attachmentVersion);
    if (item.attachmentId === null || typeof item.displayName !== 'string' || !item.displayName || item.displayName.trim() !== item.displayName
      || item.displayName.length > 255 || /[\p{Cc}\p{Cf}]/u.test(item.displayName)) throw invalid();
    const current = { ticks: instant(item.createdAt), id: (item.attachmentId as string).toLowerCase() };
    if (ids.has(current.id) || previous && (current.ticks > previous.ticks || current.ticks === previous.ticks && current.id >= previous.id)) throw invalid();
    ids.add(current.id); previous = current;
  }
  if (result.nextCursor !== null) {
    const next = position(result.nextCursor, scope, version);
    if (result.items.length !== 50 || !previous || next.ticks !== previous.ticks || next.id !== previous.id || result.nextCursor === previousCursor) throw invalid();
  }
  return result as CardCoverCandidatePage;
}
export function parseCardCoverChange(value: unknown, scope: AttachmentScope, original: CardCoverView, intent: CardCoverIntent): CardCoverChange {
  parseCardCoverView(original, scope, original.cardVersion); source(intent.attachmentId, intent.attachmentVersion);
  if (intent.cardVersion !== original.cardVersion || !original.canEdit || typeof intent.publicVisibilityConfirmed !== 'boolean'
    || intent.attachmentId !== null && original.isPublic && !intent.publicVisibilityConfirmed) throw invalid();
  const changed = original.attachmentId === null ? intent.attachmentId !== null
    : intent.attachmentId === null || !same(intent.attachmentId, original.attachmentId);
  const result = scoped(value, scope, original.cardVersion + (changed ? 1 : 0), ['organizationId', 'boardId', 'cardId', 'cardVersion', 'attachmentId', 'attachmentVersion', 'changed']);
  if (result.changed !== changed || result.attachmentVersion !== intent.attachmentVersion
    || (intent.attachmentId === null ? result.attachmentId !== null : !same(result.attachmentId, intent.attachmentId))) throw invalid();
  return result as CardCoverChange;
}
