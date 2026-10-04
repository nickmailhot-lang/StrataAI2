import { normalizeMentionPrefix, parseCardMentionOptions } from './cardMentionOptions';

const id = (n: number) => `11111111-1111-4111-8111-${String(n).padStart(12, '0')}`;
const scope = { organizationId: id(1), boardId: id(2), cardId: id(3) };
const member = (n = 1) => ({ userId: id(100 + n), handle: `member_${String(n).padStart(2, '0')}`, displayName: 'Same name', handleVersion: 2 });
const page = (items = [member()]) => ({ ...scope, cardVersion: 10, prefix: 'member_', items, nextCursor: null as string | null });

describe('current Card teammate discovery admission', () => {
  it('normalizes literal prefix with server whitespace and rejects malformed/unbounded input', () => {
    expect(normalizeMentionPrefix(' MEMBER_ ')).toBe('member_'); expect(normalizeMentionPrefix('')).toBe('');
    expect(normalizeMentionPrefix('a')).toBe('a'); expect(normalizeMentionPrefix('a'.repeat(40))).toHaveLength(40);
    for (const input of ['a'.repeat(41), '@member', '1name', 'nïck', 'member-name', '\ufeffmember', 'member\nname'])
      expect(() => normalizeMentionPrefix(input)).toThrow();
  });
  it('admits current custom/default handles and freezes copied metadata against later mutation', () => {
    const source = page(); const admitted = parseCardMentionOptions(source, scope, 10, 'member_');
    source.items[0].handle = 'replacement'; source.items.push(member(2));
    expect(admitted.items[0].handle).toBe('member_01'); expect(admitted.items).toHaveLength(1);
    expect(Object.isFrozen(admitted)).toBe(true); expect(Object.isFrozen(admitted.items)).toBe(true); expect(Object.isFrozen(admitted.items[0])).toBe(true);
    const current = { ...member(), handle: `u_${member().userId.replaceAll('-', '')}` };
    expect(parseCardMentionOptions({ ...page([current]), prefix: '' }, scope, 10, '').items[0].handle).toBe(current.handle);
    expect(parseCardMentionOptions(page([]), scope, 10, 'member_').items).toHaveLength(0);
  });
  it('refuses foreign/stale scope, private fields, incorrect identities, ordering and unbounded metadata', () => {
    for (const patch of [{ organizationId: id(90) }, { boardId: id(90) }, { cardId: id(90) }, { cardVersion: 9 },
      { prefix: 'other_' }, { email: 'private' }, { items: Array.from({ length: 21 }, (_, i) => member(i)) }])
      expect(() => parseCardMentionOptions({ ...page(), ...patch }, scope, 10, 'member_')).toThrow();
    for (const patch of [{ userId: '00000000-0000-0000-0000-000000000000' }, { handle: 'member_OTHER' }, { handle: 'board' },
      { handle: `u_${id(99).replaceAll('-', '')}` }, { handleVersion: 0 }, { handleVersion: Number.MAX_SAFE_INTEGER + 1 },
      { displayName: ' ' }, { displayName: 'x'.repeat(121) }, { email: 'private' }])
      expect(() => parseCardMentionOptions(page([{ ...member(), ...patch }]), scope, 10, 'member_')).toThrow();
    expect(() => parseCardMentionOptions(page([member(2), member(1)]), scope, 10, 'member_')).toThrow();
    expect(() => parseCardMentionOptions(page([member(), { ...member(2), userId: member().userId }]), scope, 10, 'member_')).toThrow();
  });
  it('binds lookahead and continuation to exact Card/revision/prefix/last handle without repeated anchors', () => {
    const items = Array.from({ length: 20 }, (_, i) => member(i + 1)); const cursor = `${scope.cardId}/10/member_/member_20`;
    expect(parseCardMentionOptions({ ...page(items), nextCursor: cursor }, scope, 10, 'member_').nextCursor).toBe(cursor);
    expect(parseCardMentionOptions(page([member(21)]), scope, 10, 'member_', cursor).items).toHaveLength(1);
    expect(() => parseCardMentionOptions(page([member(20)]), scope, 10, 'member_', cursor)).toThrow();
    for (const bad of [cursor.replace('/10/', '/9/'), cursor.replace(scope.cardId, id(90)), cursor.replace('member_/', 'other_/'),
      cursor.replace('member_20', 'member_19'), cursor + '/private', 'x'.repeat(161)])
      expect(() => parseCardMentionOptions({ ...page(items), nextCursor: bad }, scope, 10, 'member_')).toThrow();
    expect(() => parseCardMentionOptions({ ...page(), nextCursor: cursor }, scope, 10, 'member_')).toThrow();
  });
});
