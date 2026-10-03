import { parseAttachmentArchivePage, parseAttachmentLifecycleChanged } from './attachmentLifecycle';
import { parseAttachmentPage, type ArchivedAttachment, type AttachmentMetadata } from './attachments';
import { dateInstantTicks } from './cardDates';

const id = (n: number) => `11111111-1111-4111-8111-${String(n).padStart(12, '0')}`;
const scope = { organizationId: id(1), boardId: id(2), cardId: id(3) }; const actor = id(4);
const created = '2026-10-03T08:00:00.123456Z'; const archived = '2026-10-03T08:01:00.123456+00:00'; const later = '2026-10-03T08:02:00.123456Z';
const original: AttachmentMetadata = { id: id(5), organizationId: id(1), cardId: id(3), uploaderId: actor,
  kind: 1, displayName: 'Retained URL', url: 'https://example.test/', mimeType: null, sizeBytes: null, scanStatus: 0, scannedAt: null,
  createdAt: created, updatedAt: created, version: 1, lifecycleState: 0, archivedAt: null, deletedAt: null, deletedBy: null };
const archivedFile: ArchivedAttachment = { ...original, lifecycleState: 1, version: 2, updatedAt: archived, archivedAt: archived };
const ack = (attachment: unknown = archivedFile, cardVersion = 11, changed = true) => ({ ...scope, cardVersion, attachment, changed });
const page = (items: unknown[] = [archivedFile]) => ({ ...scope, cardVersion: 11, canRestore: true, canDelete: false, items, nextCursor: null as string | null });

describe('attachment lifecycle response admission', () => {
  it('binds archive/restore/delete acknowledgments to immutable identity, actor, history and both revisions', () => {
    expect(parseAttachmentLifecycleChanged(ack(), scope, original, 10, actor, 'archive')).toEqual(ack());
    const restored = { ...archivedFile, lifecycleState: 0 as const, version: 3, updatedAt: later };
    expect(parseAttachmentLifecycleChanged(ack(restored, 12), scope, archivedFile, 11, actor, 'restore').attachment).toEqual(restored);
    expect(parseAttachmentPage({ ...scope, cardVersion: 12, canEdit: true, items: [restored], nextCursor: null }, scope).items).toEqual([restored]);
    const deleted = { ...archivedFile, lifecycleState: 2 as const, version: 3, updatedAt: later, deletedAt: later, deletedBy: actor };
    expect(parseAttachmentLifecycleChanged(ack(deleted, 12), scope, archivedFile, 11, actor, 'delete').attachment).toEqual(deleted);
    expect(() => parseAttachmentPage({ ...scope, cardVersion: 12, canEdit: true, items: [archivedFile], nextCursor: null }, scope)).toThrow();
    expect(() => parseAttachmentArchivePage(page([deleted]), scope)).toThrow();
  });
  it('admits same-state no-ops without fabricated revisions or timestamps', () => {
    expect(parseAttachmentLifecycleChanged(ack(archivedFile, 11, false), scope, archivedFile, 11, actor, 'archive').changed).toBe(false);
    for (const patch of [{ version: 3 }, { updatedAt: later }, { archivedAt: later }])
      expect(() => parseAttachmentLifecycleChanged(ack({ ...archivedFile, ...patch }, 11, false), scope, archivedFile, 11, actor, 'archive')).toThrow();
    expect(() => parseAttachmentLifecycleChanged(ack(archivedFile, 12, false), scope, archivedFile, 11, actor, 'archive')).toThrow();
  });
  it('rejects mismatched identity/scope/source, secret fields, history and acknowledgment claims', () => {
    for (const patch of [{ id: id(99) }, { organizationId: id(99) }, { cardId: id(99) }, { uploaderId: id(99) },
      { displayName: 'Changed' }, { url: 'https://example.test/changed' }, { storageKey: 'private/key' }, { sha256: 'a'.repeat(64) },
      { version: 3 }, { lifecycleState: 0 }, { lifecycleState: 'ARCHIVED' }, { archivedAt: null }, { deletedAt: archived }, { deletedBy: actor },
      { archivedAt: created }, { archivedAt: later }, { updatedAt: '2026-10-03T08:01:00.1234567Z' }])
      expect(() => parseAttachmentLifecycleChanged(ack({ ...archivedFile, ...patch }), scope, original, 10, actor, 'archive')).toThrow();
    for (const patch of [{ changed: false }, { changed: 'true' }, { cardVersion: 10 }, { cardVersion: 12 }, { boardId: id(99) }, { previewUrl: '/private' }])
      expect(() => parseAttachmentLifecycleChanged({ ...ack(), ...patch }, scope, original, 10, actor, 'archive')).toThrow();
    expect(() => parseAttachmentLifecycleChanged(ack(), scope, { ...original, cardId: id(99) }, 10, actor, 'archive')).toThrow();
  });
  it('requires archive-first deletion, retained history, a current actor and no restored tombstone', () => {
    const deleted = { ...archivedFile, lifecycleState: 2 as const, version: 3, updatedAt: later, deletedAt: later, deletedBy: actor };
    for (const patch of [{ deletedBy: id(99) }, { deletedBy: null }, { deletedAt: archived }, { archivedAt: null }, { archivedAt: later }, { version: 2 }])
      expect(() => parseAttachmentLifecycleChanged(ack({ ...deleted, ...patch }, 12), scope, archivedFile, 11, actor, 'delete')).toThrow();
    expect(() => parseAttachmentLifecycleChanged(ack(deleted, 12), scope, original, 11, actor, 'delete')).toThrow();
    expect(() => parseAttachmentLifecycleChanged(ack({ ...original, version: 4, updatedAt: later }, 12), scope, deleted, 11, actor, 'restore')).toThrow();
  });
  it('retains exact scanned source evidence during lifecycle changes', () => {
    const clean = { ...original, kind: 0 as const, url: null, mimeType: 'image/png' as const, sizeBytes: 1234, scanStatus: 2 as const, scannedAt: created, version: 2 };
    const result = { ...clean, lifecycleState: 1 as const, archivedAt: archived, updatedAt: archived, version: 3 };
    expect(parseAttachmentLifecycleChanged(ack(result), scope, clean, 10, actor, 'archive').attachment).toEqual(result);
    for (const patch of [{ mimeType: 'image/jpeg' }, { sizeBytes: 1235 }, { scanStatus: 3 }, { scannedAt: archived }])
      expect(() => parseAttachmentLifecycleChanged(ack({ ...result, ...patch }), scope, clean, 10, actor, 'archive')).toThrow();
  });
  it('seeks archive pages with exact microsecond and tied UUID ordering and a separate collection cursor', () => {
    const rows = Array.from({ length: 50 }, (_, i) => ({ ...archivedFile, id: id(100 - i) })); const value = page(rows);
    const ticks = dateInstantTicks(created) + 621355968000000000n;
    value.nextCursor = `archive/${scope.cardId}/${ticks}/${rows[49].id}`;
    expect(parseAttachmentArchivePage(value, scope)).toEqual(value);
    expect(parseAttachmentArchivePage(page([{ ...archivedFile, id: id(50) }]), scope, value.nextCursor).items).toHaveLength(1);
    for (const cursor of [value.nextCursor.slice(8), value.nextCursor.replace(scope.cardId, scope.boardId), `archive/${scope.cardId}/${ticks + 1n}/${rows[49].id}`,
      `archive/${scope.cardId}/${ticks}/${rows[48].id}`, 'archive/bad'])
      expect(() => parseAttachmentArchivePage({ ...value, nextCursor: cursor }, scope)).toThrow();
    expect(() => parseAttachmentArchivePage(page([rows[49]]), scope, value.nextCursor!)).toThrow();
  });
  it('refuses mixed lifecycle, duplicate/order/size errors and invalid archive capabilities', () => {
    for (const items of [[original], [archivedFile, archivedFile], [{ ...archivedFile, id: id(5) }, { ...archivedFile, id: id(6) }],
      Array.from({ length: 51 }, (_, i) => ({ ...archivedFile, id: id(100 - i) }))]) expect(() => parseAttachmentArchivePage(page(items), scope)).toThrow();
    for (const patch of [{ canRestore: 'true' }, { canDelete: 1 }, { canRestore: false, canDelete: true }, { cardVersion: 0 }, { boardId: id(99) }, { providerDetails: 'private' }])
      expect(() => parseAttachmentArchivePage({ ...page(), ...patch }, scope)).toThrow();
    expect(parseAttachmentArchivePage({ ...page([]), canRestore: false }, scope).items).toEqual([]);
  });
});
