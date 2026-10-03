import { normalizeComment, parseCardCommentChange, parseCardCommentPage, type CardComment, type CommentIntent } from './cardComments';
import { dateInstantTicks } from './cardDates';
const id = (n: number) => `11111111-1111-4111-8111-${String(n).padStart(12, '0')}`;
const scope = { organizationId: id(1), boardId: id(2), cardId: id(3) };
const at = '2026-10-03T08:00:00.123456Z'; const editedAt = '2026-10-03T08:01:00.123456Z';
const item = (n = 100): CardComment => ({ id: id(n), organizationId: scope.organizationId, cardId: scope.cardId, authorId: id(4),
  content: 'Plain <script>\n🙂', createdAt: at, updatedAt: at, version: 1, editedAt: null, deletedAt: null, deletedBy: null });
const page = (items = [item()]) => ({ ...scope, cardVersion: 10, items, nextCursor: null as string | null, canComment: true });
const intent: CommentIntent = { cardVersion: 10, actor: id(4), original: null, content: ' Plain <script>\r\n🙂 ' };
const ack = { ...scope, cardVersion: 11, comment: item(), changed: true };
describe('Card comment response admission', () => {
  it('normalizes bounded Unicode plaintext without interpreting markup or mentioning recipients', () => {
    expect(normalizeComment(intent.content!)).toBe(item().content);
    expect(normalizeComment('\ufeffLiteral\ufeff')).toBe('\ufeffLiteral\ufeff');
    expect(normalizeComment(' \t@board @card @teammate\t ')).toBe('@board @card @teammate');
    expect(normalizeComment('x'.repeat(10000))).toHaveLength(10000);
    for (const bad of ['', ' \n ', 'x'.repeat(10001), 'x\u0000', 'x\u007f', 'x\ud800', 'x\udc00', 'x\ud800y']) expect(() => normalizeComment(bad)).toThrow();
  });
  it('admits bounded current pages and retained edited redacted history', () => {
    expect(parseCardCommentPage(page(), scope, 10)).toEqual(page());
    expect(parseCardCommentPage(page([]), scope, 10).items).toEqual([]);
    const edited = { ...item(), content: 'Edited', updatedAt: editedAt, editedAt, version: 2 };
    expect(parseCardCommentPage(page([edited]), scope, 10).items[0].editedAt).toBe(editedAt);
    const deleted = { ...edited, content: null, deletedAt: editedAt, deletedBy: id(4), version: 3 };
    expect(parseCardCommentPage(page([deleted]), scope, 10).items[0].content).toBeNull();
    for (const patch of [{ organizationId: id(99) }, { cardId: id(99) }, { authorId: '00000000-0000-0000-0000-000000000000' },
      { version: 0 }, { version: Number.MAX_SAFE_INTEGER + 1 }, { content: ' raw ' }, { content: 'x\r\ny' }, { content: 'x\ud800' },
      { createdAt: 'invalid' }, { createdAt: '2026-10-03T08:00:00.1234567Z' }, { updatedAt: '2026-10-02T08:00:00Z' },
      { editedAt }, { deletedBy: id(4) }, { privateEmail: 'private' }])
      expect(() => parseCardCommentPage(page([{ ...item(), ...patch }] as CardComment[]), scope, 10)).toThrow();
    for (const patch of [{ content: 'Unredacted' }, { deletedBy: null }, { deletedBy: id(99) }, { version: 2 }, { editedAt: '2026-10-04T08:00:00Z' }])
      expect(() => parseCardCommentPage(page([{ ...deleted, ...patch }] as CardComment[]), scope, 10)).toThrow();
    expect(() => parseCardCommentPage({ ...page(), boardId: id(99) }, scope, 10)).toThrow();
    expect(() => parseCardCommentPage({ ...page(), cardVersion: 9 }, scope, 10)).toThrow();
  });
  it('validates tied seek order, lookahead cursor boundaries and current Card versions', () => {
    const items = Array.from({ length: 50 }, (_, i) => item(200 - i));
    const ticks = dateInstantTicks(at) + 621355968000000000n; const cursor = `${scope.cardId}/10/${ticks}/${id(151)}`;
    expect(parseCardCommentPage({ ...page(items), nextCursor: cursor }, scope, 10).nextCursor).toBe(cursor);
    expect(parseCardCommentPage(page([item(150)]), scope, 10, cursor).items).toHaveLength(1);
    for (const items of [[item(), item()], [item(99), item(100)], Array.from({ length: 51 }, (_, i) => item(200 - i))])
      expect(() => parseCardCommentPage(page(items), scope, 10)).toThrow();
    expect(() => parseCardCommentPage(page([item(151)]), scope, 10, cursor)).toThrow();
    for (const bad of [cursor.replace('/10/', '/9/'), cursor.replace(scope.cardId, id(99)), cursor.replace(id(151), id(152)), cursor + '/private'])
      expect(() => parseCardCommentPage({ ...page(items), nextCursor: bad }, scope, 10)).toThrow();
    expect(() => parseCardCommentPage({ ...page(), nextCursor: cursor }, scope, 10)).toThrow();
  });
  it('checks author and dual revisions for create/edit/no-op and confirmed redaction acknowledgments', () => {
    expect(parseCardCommentChange(ack, scope, intent)).toEqual(ack);
    const original = item(); const edit: CommentIntent = { ...intent, original, content: 'Edited' };
    const edited = { ...original, content: 'Edited', updatedAt: editedAt, editedAt, version: 2 };
    expect(parseCardCommentChange({ ...ack, comment: edited }, scope, edit).comment).toEqual(edited);
    expect(parseCardCommentChange({ ...ack, cardVersion: 10, changed: false }, scope, { ...intent, original }).changed).toBe(false);
    const deleted = { ...edited, content: null, version: 3, deletedAt: editedAt, deletedBy: id(4) };
    const remove: CommentIntent = { cardVersion: 10, actor: id(4), original: edited, deleting: true, confirmed: true };
    expect(parseCardCommentChange({ ...ack, comment: deleted }, scope, remove).comment.content).toBeNull();
    expect(() => parseCardCommentChange({ ...ack, comment: deleted }, scope, { ...remove, confirmed: false })).toThrow();
    expect(() => parseCardCommentChange(ack, scope, { ...intent, actor: id(99) })).toThrow();
    for (const patch of [{ cardVersion: 10 }, { cardVersion: 12 }, { changed: false }, { boardId: id(99) }, { bodyCopy: 'private' },
      { comment: { ...item(), content: 'Unexpected' } }, { comment: { ...item(), version: 2 } }])
      expect(() => parseCardCommentChange({ ...ack, ...patch }, scope, intent)).toThrow();
    expect(() => parseCardCommentChange({ ...ack, comment: { ...edited, id: id(99) } }, scope, edit)).toThrow();
    expect(() => parseCardCommentChange({ ...ack, comment: deleted }, scope, { ...edit, original: deleted })).toThrow();
  });
});
