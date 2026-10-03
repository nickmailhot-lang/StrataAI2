import { notificationUuid } from '../notifications/notificationInbox';
import { dateInstantTicks } from './cardDates';

export type AttachmentScope = { organizationId: string; boardId: string; cardId: string };
// Current internal URL-only API uses Domain enum ordinals. Unknown kinds and
// scan states fail closed until their production/UI paths are implemented.
export type UrlAttachment = { id: string; organizationId: string; cardId: string; uploaderId: string;
  kind: 1; displayName: string; mimeType: null; sizeBytes: null; url: string; scanStatus: 0;
  scannedAt: null; createdAt: string; updatedAt: string; version: number; deletedAt: null };
export type AttachmentPage = AttachmentScope & { cardVersion: number; canEdit: boolean; items: UrlAttachment[]; nextCursor: string | null };
export type AttachmentChange = AttachmentScope & { cardVersion: number; attachment: UrlAttachment };
const invalid = () => new Error('Invalid attachment response');
const same = (value: unknown, expected: string) => notificationUuid(value) && notificationUuid(expected) && value.toLowerCase() === expected.toLowerCase();
const revision = (value: unknown): value is number => Number.isSafeInteger(value) && Number(value) > 0;
const epochTicks = 621355968000000000n;
function record(value: unknown): Record<string, unknown> {
  if (!value || typeof value !== 'object' || Array.isArray(value)) throw invalid();
  return value as Record<string, unknown>;
}
function exact(row: Record<string, unknown>, fields: string[]) {
  if (Object.keys(row).length !== fields.length || fields.some(field => !Object.hasOwn(row, field))) throw invalid();
}
function instant(value: unknown): bigint {
  if (typeof value !== 'string') throw invalid();
  return dateInstantTicks(value) + epochTicks;
}
export function attachmentUrl(value: unknown): string {
  if (typeof value !== 'string' || !value || value.length > 2048 || value.trim() !== value || /[\p{Cc}]/u.test(value)) throw invalid();
  let url: URL;
  try { url = new URL(value); } catch { throw invalid(); }
  if (!['http:', 'https:'].includes(url.protocol) || !url.hostname || url.username || url.password || url.href.length > 2048) throw invalid();
  return url.href;
}
function metadata(value: unknown, scope: AttachmentScope): UrlAttachment {
  const row = record(value);
  exact(row, ['id', 'organizationId', 'cardId', 'uploaderId', 'kind', 'displayName', 'mimeType', 'sizeBytes', 'url', 'scanStatus', 'scannedAt', 'createdAt', 'updatedAt', 'version', 'deletedAt']);
  if (!notificationUuid(row.id) || !notificationUuid(row.uploaderId) || !same(row.organizationId, scope.organizationId) || !same(row.cardId, scope.cardId)
    || !revision(row.version) || row.kind !== 1 || row.scanStatus !== 0 || row.mimeType !== null || row.sizeBytes !== null || row.scannedAt !== null || row.deletedAt !== null
    || typeof row.displayName !== 'string' || !row.displayName || row.displayName.trim() !== row.displayName || row.displayName.length > 255 || /[\p{Cc}\p{Cf}]/u.test(row.displayName)) throw invalid();
  attachmentUrl(row.url);
  if (instant(row.updatedAt) < instant(row.createdAt) || instant(row.createdAt) % 10n !== 0n) throw invalid();
  return row as UrlAttachment;
}
function scopeRecord(value: unknown, scope: AttachmentScope) {
  const row = record(value);
  if (!same(row.organizationId, scope.organizationId) || !same(row.boardId, scope.boardId) || !same(row.cardId, scope.cardId) || !revision(row.cardVersion)) throw invalid();
  return row;
}
function position(value: unknown, cardId: string) {
  if (typeof value !== 'string' || value.length > 128) throw invalid();
  const parts = value.split('/');
  if (parts.length !== 3 || !same(parts[0], cardId) || !/^(0|[1-9][0-9]{0,18})$/.test(parts[1]) || !notificationUuid(parts[2])) throw invalid();
  const ticks = BigInt(parts[1]);
  if (ticks > 3155378975999999999n || ticks % 10n !== 0n) throw invalid();
  return { ticks, id: parts[2].toLowerCase() };
}
const before = (left: { ticks: bigint; id: string }, right: { ticks: bigint; id: string }) => left.ticks < right.ticks || left.ticks === right.ticks && left.id < right.id;
export function parseAttachmentPage(value: unknown, scope: AttachmentScope, previousCursor?: string): AttachmentPage {
  const row = scopeRecord(value, scope); exact(row, ['organizationId', 'boardId', 'cardId', 'cardVersion', 'items', 'nextCursor', 'canEdit']);
  if (typeof row.canEdit !== 'boolean' || !Array.isArray(row.items) || row.items.length > 50) throw invalid();
  const items = row.items.map(value => metadata(value, scope)); const ids = new Set<string>();
  let previous = previousCursor ? position(previousCursor, scope.cardId) : undefined;
  for (const item of items) {
    const current = { ticks: instant(item.createdAt), id: item.id.toLowerCase() };
    if (ids.has(current.id) || previous && !before(current, previous)) throw invalid();
    ids.add(current.id); previous = current;
  }
  if (row.nextCursor !== null) {
    const next = position(row.nextCursor, scope.cardId);
    if (items.length !== 50 || !previous || next.ticks !== previous.ticks || next.id !== previous.id || row.nextCursor === previousCursor) throw invalid();
  }
  return row as AttachmentPage;
}
export function parseUrlAttachmentCreated(value: unknown, scope: AttachmentScope, actor: string, title: string, url: string, cardVersion: number): AttachmentChange {
  const row = scopeRecord(value, scope); exact(row, ['organizationId', 'boardId', 'cardId', 'cardVersion', 'attachment']);
  const attachment = metadata(row.attachment, scope);
  if (!revision(cardVersion) || row.cardVersion !== cardVersion + 1 || !same(attachment.uploaderId, actor) || attachment.displayName !== title
    || attachmentUrl(attachment.url) !== attachmentUrl(url) || attachment.version !== 1 || instant(attachment.createdAt) !== instant(attachment.updatedAt)) throw invalid();
  return row as AttachmentChange;
}
