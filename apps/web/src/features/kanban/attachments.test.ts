import { attachmentUrl, parseAttachmentPage, parseUrlAttachmentCreated, type AttachmentMetadata, type UrlAttachment } from './attachments';
import { dateInstantTicks } from './cardDates';

const scope = { organizationId: '11111111-1111-4111-8111-111111111111', boardId: '22222222-2222-4222-8222-222222222222', cardId: '33333333-3333-4333-8333-333333333333' };
const actor = '44444444-4444-4444-8444-444444444444';
const now = '2026-10-03T08:00:00.123456+00:00';
const ticks = dateInstantTicks(now) + 621355968000000000n;
const item = (index = 1): UrlAttachment => ({ id: `55555555-5555-4555-8555-${String(index).padStart(12, '0')}`, organizationId: scope.organizationId, cardId: scope.cardId,
  uploaderId: actor, kind: 1, displayName: 'External link', mimeType: null, sizeBytes: null, url: 'https://example.test/path?q=1#part', scanStatus: 0, scannedAt: null,
  createdAt: now, updatedAt: now, version: 1, deletedAt: null });
const page = (items: AttachmentMetadata[] = [item()]) => ({ ...scope, cardVersion: 2, canEdit: true, items, nextCursor: null as string | null });
const change = () => ({ ...scope, cardVersion: 2, attachment: item() });

describe('URL attachment response admission', () => {
  it('admits scoped URL metadata and exact actor/intent acknowledgment without binary delivery state', () => {
    expect(parseAttachmentPage(page(), scope)).toEqual(page());
    expect(parseUrlAttachmentCreated(change(), scope, actor, 'External link', item().url, 1)).toEqual(change());
    expect(attachmentUrl('https://example.test/space%20here')).toBe('https://example.test/space%20here');
  });
  it.each(['javascript:alert(1)', 'data:text/html,bad', 'file:///private', 'ftp://example.test/', '/relative', 'https://user:secret@example.test/', ' https://example.test/', 'https://example.test/\n'])('rejects unsafe external URL %s', value => {
    expect(() => attachmentUrl(value)).toThrow();
    expect(() => parseAttachmentPage(page([{ ...item(), url: value }]), scope)).toThrow();
  });
  it('rejects scope/version/identity failures, binary secret fields and invalid variant or lifecycle states', () => {
    for (const patch of [
      { organizationId: scope.boardId }, { cardId: scope.boardId }, { id: 'bad' }, { uploaderId: 'bad' }, { version: 0 }, { version: Number.MAX_SAFE_INTEGER + 1 },
      { kind: 0 }, { scanStatus: 2 }, { mimeType: 'image/png' }, { sizeBytes: 50 }, { scannedAt: now }, { deletedAt: now }, { storageKey: 'private/key' },
      { displayName: 'Unsafe\nname' }, { displayName: '\u202Espoofed' }, { displayName: ' ' }, { displayName: 'x'.repeat(256) },
      { createdAt: '2026-10-03T08:00:00.1234567Z' }, { updatedAt: '2026-10-03T07:59:59Z' },
    ]) expect(() => parseAttachmentPage(page([{ ...item(), ...patch }] as UrlAttachment[]), scope)).toThrow();
    for (const patch of [{ boardId: scope.cardId }, { cardVersion: 0 }, { canEdit: 'true' }, { providerDetail: 'private' }])
      expect(() => parseAttachmentPage({ ...page(), ...patch }, scope)).toThrow();
  });
  it('keeps .NET cursor precision across 50 tied timestamps and requires cursor anchored to the final row', () => {
    const rows = Array.from({ length: 50 }, (_, i) => item(100 - i)); const first = page(rows);
    first.nextCursor = `${scope.cardId}/${ticks}/${rows[49].id}`;
    expect(parseAttachmentPage(first, scope)).toEqual(first);
    expect(parseAttachmentPage(page([item(50)]), scope, first.nextCursor).items).toHaveLength(1);
    expect(() => parseAttachmentPage(page([rows[49]]), scope, first.nextCursor!)).toThrow();
    for (const cursor of [`${scope.boardId}/${ticks}/${rows[49].id}`, `${scope.cardId}/${ticks + 1n}/${rows[49].id}`, `${scope.cardId}/${Number(ticks)}/${rows[49].id}`,
      `${scope.cardId}/${ticks}/${rows[48].id}`, `${scope.cardId}/3155378976000000000/${rows[49].id}`, 'bad'])
      expect(() => parseAttachmentPage({ ...first, nextCursor: cursor }, scope)).toThrow();
    expect(() => parseAttachmentPage({ ...page(), nextCursor: first.nextCursor }, scope)).toThrow();
  });
  it('rejects duplicate rows, incorrect ordering, oversized pages and mismatched actor/title/URL or card acknowledgment', () => {
    for (const rows of [[item(), item()], [item(1), item(2)], Array.from({ length: 51 }, (_, i) => item(100 - i))])
      expect(() => parseAttachmentPage(page(rows), scope)).toThrow();
    for (const patch of [{ uploaderId: scope.boardId }, { displayName: 'Changed' }, { url: 'https://example.test/changed' }, { version: 2 }, { updatedAt: '2026-10-03T08:00:01Z' }])
      expect(() => parseUrlAttachmentCreated({ ...change(), attachment: { ...item(), ...patch } }, scope, actor, 'External link', item().url, 1)).toThrow();
    expect(() => parseUrlAttachmentCreated({ ...change(), cardVersion: 3 }, scope, actor, 'External link', item().url, 1)).toThrow();
  });
});

describe('private file metadata admission', () => {
  const file = { ...item(2), kind: 0 as const, mimeType: 'application/pdf' as const, sizeBytes: 100003, url: null, scanStatus: 1 as const, scannedAt: null };
  it('admits mixed links/files and all canonical scan states without provider delivery fields', () => {
    expect(parseAttachmentPage(page([file, item()]), scope).items).toEqual([file, item()]);
    for (const scanStatus of [2, 3, 4] as const) {
      const terminal = { ...file, version: 2, scanStatus, scannedAt: now };
      expect(parseAttachmentPage(page([terminal]), scope).items).toEqual([terminal]);
    }
  });
  it('rejects private fields, hybrid variants, invalid measurements and inconsistent scan lifecycles', () => {
    for (const patch of [{ storageKey: 'private/key' }, { sha256: 'a'.repeat(64) }, { downloadUrl: 'https://private.test/' },
      { url: 'https://example.test/' }, { mimeType: 'text/html' }, { mimeType: null }, { sizeBytes: 0 }, { sizeBytes: 1073741825 }, { sizeBytes: 1.5 },
      { sizeBytes: '100003' }, { scanStatus: 0 }, { scanStatus: 5 }, { scanStatus: '2' }, { scannedAt: now }, { kind: 2 },
      { scanStatus: 2, scannedAt: now }, { scanStatus: 2, scannedAt: null, version: 2 },
      { scanStatus: 2, scannedAt: '2026-10-03T07:59:59Z', version: 2 },
      { scanStatus: 2, scannedAt: '2026-10-03T08:00:01Z', version: 2 },
      { scanStatus: 2, scannedAt: '2026-10-03T08:00:00.1234567Z', version: 2 }])
      expect(() => parseAttachmentPage(page([{ ...file, ...patch }] as AttachmentMetadata[]), scope)).toThrow();
    expect(() => parseUrlAttachmentCreated({ ...change(), attachment: file }, scope, actor, file.displayName, item().url, 1)).toThrow();
  });
});
