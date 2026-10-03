import { notificationUuid } from '../notifications/notificationInbox';
import { dateInstantTicks } from './cardDates';
import { parseLifecycleAttachmentMetadata, type ArchivedAttachment, type AttachmentScope, type LifecycleAttachment } from './attachments';

export type AttachmentArchivePage = AttachmentScope & { cardVersion: number; items: ArchivedAttachment[];
  nextCursor: string | null; canRestore: boolean; canDelete: boolean };
export type AttachmentLifecycleChange = AttachmentScope & { cardVersion: number; attachment: LifecycleAttachment; changed: boolean };
export type AttachmentLifecycleAction = 'archive' | 'restore' | 'delete';
const invalid = () => new Error('Invalid attachment lifecycle response');
const same = (value: unknown, expected: string) => notificationUuid(value) && notificationUuid(expected) && value.toLowerCase() === expected.toLowerCase();
const revision = (value: unknown): value is number => Number.isSafeInteger(value) && Number(value) > 0;
const ticks = (value: string) => dateInstantTicks(value) + 621355968000000000n;
function scoped(value: unknown, scope: AttachmentScope, fields: string[]) {
  if (!value || typeof value !== 'object' || Array.isArray(value)) throw invalid();
  const row = value as Record<string, unknown>;
  if (Object.keys(row).length !== fields.length || fields.some(field => !Object.hasOwn(row, field))
    || !same(row.organizationId, scope.organizationId) || !same(row.boardId, scope.boardId) || !same(row.cardId, scope.cardId)
    || !revision(row.cardVersion)) throw invalid();
  return row;
}
function position(value: unknown, card: string) {
  if (typeof value !== 'string' || value.length > 140) throw invalid();
  const parts = value.split('/');
  if (parts.length !== 4 || parts[0] !== 'archive' || !same(parts[1], card)
    || !/^(0|[1-9][0-9]{0,18})$/.test(parts[2]) || !notificationUuid(parts[3])) throw invalid();
  const valueTicks = BigInt(parts[2]);
  if (valueTicks > 3155378975999999999n || valueTicks % 10n !== 0n) throw invalid();
  return { ticks: valueTicks, id: parts[3].toLowerCase() };
}
export function parseAttachmentArchivePage(value: unknown, scope: AttachmentScope, previousCursor?: string): AttachmentArchivePage {
  const row = scoped(value, scope, ['organizationId', 'boardId', 'cardId', 'cardVersion', 'items', 'nextCursor', 'canRestore', 'canDelete']);
  if (typeof row.canRestore !== 'boolean' || typeof row.canDelete !== 'boolean' || row.canDelete && !row.canRestore
    || !Array.isArray(row.items) || row.items.length > 50) throw invalid();
  const items = row.items.map(value => parseLifecycleAttachmentMetadata(value, scope, 1) as ArchivedAttachment);
  const ids = new Set<string>(); let previous = previousCursor ? position(previousCursor, scope.cardId) : undefined;
  for (const item of items) {
    const current = { ticks: ticks(item.createdAt), id: item.id.toLowerCase() };
    if (ids.has(current.id) || previous && (current.ticks > previous.ticks || current.ticks === previous.ticks && current.id >= previous.id)) throw invalid();
    ids.add(current.id); previous = current;
  }
  if (row.nextCursor !== null) {
    const next = position(row.nextCursor, scope.cardId);
    if (items.length !== 50 || !previous || next.ticks !== previous.ticks || next.id !== previous.id || row.nextCursor === previousCursor) throw invalid();
  }
  return row as AttachmentArchivePage;
}
export function parseAttachmentLifecycleChanged(value: unknown, scope: AttachmentScope, original: LifecycleAttachment,
  cardVersion: number, actor: string, action: AttachmentLifecycleAction): AttachmentLifecycleChange {
  const row = scoped(value, scope, ['organizationId', 'boardId', 'cardId', 'cardVersion', 'attachment', 'changed']);
  parseLifecycleAttachmentMetadata(original, scope, original.lifecycleState);
  const next = action === 'archive' ? 1 : action === 'restore' ? 0 : 2;
  if (original.lifecycleState === 2 || next === 2 && original.lifecycleState !== 1 || !revision(cardVersion) || !notificationUuid(actor)
    || typeof row.changed !== 'boolean' || row.changed !== (original.lifecycleState !== next)) throw invalid();
  const attachment = parseLifecycleAttachmentMetadata(row.attachment, scope, next);
  if (!same(attachment.id, original.id) || !same(attachment.uploaderId, original.uploaderId) || attachment.kind !== original.kind
    || attachment.displayName !== original.displayName || attachment.mimeType !== original.mimeType || attachment.sizeBytes !== original.sizeBytes
    || attachment.url !== original.url || attachment.scanStatus !== original.scanStatus || ticks(attachment.createdAt) !== ticks(original.createdAt)
    || (attachment.scannedAt === null ? original.scannedAt !== null : original.scannedAt === null || ticks(attachment.scannedAt) !== ticks(original.scannedAt))
    || row.cardVersion !== cardVersion + (row.changed ? 1 : 0) || attachment.version !== original.version + (row.changed ? 1 : 0)
    || ticks(attachment.updatedAt) < ticks(original.updatedAt) || !row.changed && ticks(attachment.updatedAt) !== ticks(original.updatedAt)) throw invalid();
  if (action === 'archive' && row.changed) {
    if (attachment.archivedAt === null || ticks(attachment.archivedAt) !== ticks(attachment.updatedAt)) throw invalid();
  } else if (attachment.archivedAt === null ? original.archivedAt !== null
    : original.archivedAt === null || ticks(attachment.archivedAt) !== ticks(original.archivedAt)) throw invalid();
  if (action === 'delete' && !same(attachment.deletedBy, actor)) throw invalid();
  return row as AttachmentLifecycleChange;
}
