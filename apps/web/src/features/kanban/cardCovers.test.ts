import { parseCardCoverCandidates, parseCardCoverChange, parseCardCoverView, type CardCoverIntent } from './cardCovers';
import { dateInstantTicks } from './cardDates';
const id = (n: number) => `11111111-1111-4111-8111-${String(n).padStart(12, '0')}`;
const scope = { organizationId: id(1), boardId: id(2), cardId: id(3) };
const at = '2026-10-03T08:00:00.123456Z';
const view = { ...scope, cardVersion: 10, attachmentId: null, attachmentVersion: null, canEdit: true, isPublic: false };
const item = (n = 100) => ({ attachmentId: id(n), attachmentVersion: 3, displayName: 'Verified image.png', createdAt: at });
const page = (items = [item()]) => ({ ...scope, cardVersion: 10, items, nextCursor: null as string | null, canEdit: true, isPublic: false });
const intent: CardCoverIntent = { attachmentId: id(100), attachmentVersion: 3, cardVersion: 10, publicVisibilityConfirmed: false };
const ack = { ...scope, cardVersion: 11, attachmentId: id(100), attachmentVersion: 3, changed: true };
describe('Card cover response admission', () => {
  it('admits current scoped empty/selected cover views without provider metadata', () => {
    expect(parseCardCoverView(view, scope, 10)).toEqual(view);
    expect(parseCardCoverView({ ...view, attachmentId: id(100), attachmentVersion: 4, canEdit: false, isPublic: true }, scope, 10).attachmentVersion).toBe(4);
    for (const patch of [{ cardVersion: 9 }, { boardId: id(99) }, { attachmentId: id(100) }, { attachmentVersion: 3 },
      { attachmentId: id(100), attachmentVersion: 2 }, { canEdit: 'true' }, { isPublic: 1 }, { storageKey: 'private/key' }, { previewUrl: '/private' }])
      expect(() => parseCardCoverView({ ...view, ...patch }, scope, 10)).toThrow();
  });
  it('accepts selection, source-preserving no-op and removal with exactly one or zero Card revisions', () => {
    expect(parseCardCoverChange(ack, scope, view, intent)).toEqual(ack);
    const selected = { ...view, attachmentId: id(100), attachmentVersion: 3 };
    expect(parseCardCoverChange({ ...ack, cardVersion: 10, changed: false }, scope, selected, intent).changed).toBe(false);
    const removed = { ...scope, cardVersion: 11, attachmentId: null, attachmentVersion: null, changed: true };
    expect(parseCardCoverChange(removed, scope, selected, { ...intent, attachmentId: null, attachmentVersion: null })).toEqual(removed);
    expect(parseCardCoverChange({ ...removed, cardVersion: 10, changed: false }, scope, view, { ...intent, attachmentId: null, attachmentVersion: null }).changed).toBe(false);
  });
  it('refuses foreign/revised acknowledgments, hidden fields and PUBLIC selection without captured consent', () => {
    for (const patch of [{ cardVersion: 10 }, { cardVersion: 12 }, { attachmentId: id(99) }, { attachmentVersion: 4 },
      { organizationId: id(99) }, { changed: false }, { sha256: 'private' }, { changed: 'true' }])
      expect(() => parseCardCoverChange({ ...ack, ...patch }, scope, view, intent)).toThrow();
    expect(() => parseCardCoverChange(ack, scope, { ...view, isPublic: true }, intent)).toThrow();
    expect(parseCardCoverChange(ack, scope, { ...view, isPublic: true }, { ...intent, publicVisibilityConfirmed: true })).toEqual(ack);
    expect(() => parseCardCoverChange(ack, scope, { ...view, canEdit: false }, intent)).toThrow();
    expect(() => parseCardCoverChange(ack, scope, view, { ...intent, cardVersion: 9 })).toThrow();
  });
  it('validates bounded candidates, immutable source revision, name and exact payload shape', () => {
    expect(parseCardCoverCandidates(page(), scope, 10)).toEqual(page());
    expect(parseCardCoverCandidates(page([]), scope, 10).items).toEqual([]);
    for (const patch of [{ attachmentId: null }, { attachmentVersion: 2 }, { displayName: ' hidden ' }, { displayName: 'a\u0000b' },
      { createdAt: 'invalid' }, { createdAt: '2026-10-03T08:00:00.1234567Z' }, { storageKey: 'private' }, { mimeType: 'image/png' }])
      expect(() => parseCardCoverCandidates(page([{ ...item(), ...patch }] as never), scope, 10)).toThrow();
    expect(() => parseCardCoverCandidates(page([item(), item()]), scope, 10)).toThrow();
    expect(() => parseCardCoverCandidates(page([item(99), item(100)]), scope, 10)).toThrow();
    expect(() => parseCardCoverCandidates(page(Array.from({ length: 51 }, (_, i) => item(200 - i))), scope, 10)).toThrow();
  });
  it('binds page cursors to Card/revision and exact descending timestamp/UUID boundaries', () => {
    const items = Array.from({ length: 50 }, (_, i) => item(200 - i));
    const ticks = dateInstantTicks(at) + 621355968000000000n;
    const cursor = `${scope.cardId}/10/${ticks}/${id(151)}`;
    expect(parseCardCoverCandidates({ ...page(items), nextCursor: cursor }, scope, 10).nextCursor).toBe(cursor);
    expect(parseCardCoverCandidates(page([item(150)]), scope, 10, cursor).items).toHaveLength(1);
    expect(() => parseCardCoverCandidates(page([item(151)]), scope, 10, cursor)).toThrow();
    for (const bad of [cursor.replace('/10/', '/9/'), cursor.replace(scope.cardId, id(99)), cursor.replace(id(151), id(152)), cursor + '/private'])
      expect(() => parseCardCoverCandidates({ ...page(items), nextCursor: bad }, scope, 10)).toThrow();
    expect(() => parseCardCoverCandidates({ ...page(), nextCursor: cursor }, scope, 10)).toThrow();
  });
});
