import { notificationUuid } from '../notifications/notificationInbox';
import { dateInstantTicks } from './cardDates';

export type AttachmentScope = { organizationId: string; boardId: string; cardId: string };
// Public metadata uses Domain enum ordinals; provider keys/digests and binary
// delivery URLs are never admitted through the metadata response.
export type UrlAttachment = { id: string; organizationId: string; cardId: string; uploaderId: string;
  kind: 1; displayName: string; mimeType: null; sizeBytes: null; url: string; scanStatus: 0;
  scannedAt: null; createdAt: string; updatedAt: string; version: number; deletedAt: null };
export type FileAttachment = Omit<UrlAttachment, 'kind' | 'mimeType' | 'sizeBytes' | 'url' | 'scanStatus' | 'scannedAt'> & {
  kind: 0; mimeType: 'image/png' | 'image/jpeg' | 'image/webp' | 'application/pdf'; sizeBytes: number; url: null;
} & ({ scanStatus: 1; scannedAt: null } | { scanStatus: 2 | 3 | 4; scannedAt: string });
export type AttachmentMetadata = UrlAttachment | FileAttachment;
export type AttachmentPage = AttachmentScope & { cardVersion: number; canEdit: boolean; items: AttachmentMetadata[]; nextCursor: string | null };
export type AttachmentChange = AttachmentScope & { cardVersion: number; attachment: UrlAttachment };
export type FileAttachmentChange = AttachmentScope & { cardVersion: number; attachment: FileAttachment };
export type AttachmentUploadOptions = AttachmentScope & { cardVersion: number; maximumBytes: number; allowedMimeTypes: string[] };
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
function metadata(value: unknown, scope: AttachmentScope): AttachmentMetadata {
  const row = record(value);
  exact(row, ['id', 'organizationId', 'cardId', 'uploaderId', 'kind', 'displayName', 'mimeType', 'sizeBytes', 'url', 'scanStatus', 'scannedAt', 'createdAt', 'updatedAt', 'version', 'deletedAt']);
  if (!notificationUuid(row.id) || !notificationUuid(row.uploaderId) || !same(row.organizationId, scope.organizationId) || !same(row.cardId, scope.cardId)
    || !revision(row.version) || row.deletedAt !== null
    || typeof row.displayName !== 'string' || !row.displayName || row.displayName.trim() !== row.displayName || row.displayName.length > 255 || /[\p{Cc}\p{Cf}]/u.test(row.displayName)) throw invalid();
  const created = instant(row.createdAt); const updated = instant(row.updatedAt);
  if (updated < created || created % 10n !== 0n || updated % 10n !== 0n) throw invalid();
  if (row.kind === 1) {
    if (row.scanStatus !== 0 || row.mimeType !== null || row.sizeBytes !== null || row.scannedAt !== null) throw invalid();
    attachmentUrl(row.url);
  } else if (row.kind === 0) {
    if (typeof row.mimeType !== 'string' || !['image/png', 'image/jpeg', 'image/webp', 'application/pdf'].includes(row.mimeType)
      || !Number.isSafeInteger(row.sizeBytes) || Number(row.sizeBytes) < 1 || Number(row.sizeBytes) > 1073741824 || row.url !== null) throw invalid();
    if (row.scanStatus === 1) { if (row.scannedAt !== null) throw invalid(); }
    else if ([2, 3, 4].includes(Number(row.scanStatus)) && typeof row.scanStatus === 'number') {
      const scanned = instant(row.scannedAt);
      if (row.version < 2 || scanned < created || scanned > updated || scanned % 10n !== 0n) throw invalid();
    } else throw invalid();
  } else throw invalid();
  return row as AttachmentMetadata;
}
function scopeRecord(value: unknown, scope: AttachmentScope) {
  const row = record(value);
  if (!same(row.organizationId, scope.organizationId) || !same(row.boardId, scope.boardId) || !same(row.cardId, scope.cardId) || !revision(row.cardVersion)) throw invalid();
  return row;
}
export function parseAttachmentUploadOptions(value: unknown, scope: AttachmentScope, cardVersion: number): AttachmentUploadOptions {
  const row = scopeRecord(value, scope);
  exact(row, ['organizationId', 'boardId', 'cardId', 'cardVersion', 'maximumBytes', 'allowedMimeTypes']);
  const types = row.allowedMimeTypes;
  if (!revision(cardVersion) || row.cardVersion !== cardVersion || !Number.isSafeInteger(row.maximumBytes)
    || Number(row.maximumBytes) < 1 || Number(row.maximumBytes) > 1073741824 || !Array.isArray(types) || types.length < 1 || types.length > 4
    || types.some(value => typeof value !== 'string' || !['application/pdf', 'image/jpeg', 'image/png', 'image/webp'].includes(value))
    || new Set(types).size !== types.length || types.some((value, index) => index > 0 && value <= types[index - 1])) throw invalid();
  return row as AttachmentUploadOptions;
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
  if (attachment.kind !== 1 || !revision(cardVersion) || row.cardVersion !== cardVersion + 1 || !same(attachment.uploaderId, actor) || attachment.displayName !== title
    || attachmentUrl(attachment.url) !== attachmentUrl(url) || attachment.version !== 1 || instant(attachment.createdAt) !== instant(attachment.updatedAt)) throw invalid();
  return row as AttachmentChange;
}
export function parseFileAttachmentCreated(value: unknown, scope: AttachmentScope, actor: string, name: string, sizeBytes: number,
  cardVersion: number, allowedMimeTypes: readonly string[]): FileAttachmentChange {
  const row = scopeRecord(value, scope); exact(row, ['organizationId', 'boardId', 'cardId', 'cardVersion', 'attachment']);
  const attachment = metadata(row.attachment, scope);
  if (attachment.kind !== 0 || attachment.scanStatus !== 1 || attachment.version !== 1 || !revision(cardVersion)
    || row.cardVersion !== cardVersion + 1 || !same(attachment.uploaderId, actor) || attachment.displayName !== name
    || attachment.sizeBytes !== sizeBytes || !allowedMimeTypes.includes(attachment.mimeType)
    || instant(attachment.createdAt) !== instant(attachment.updatedAt)) throw invalid();
  return row as FileAttachmentChange;
}
