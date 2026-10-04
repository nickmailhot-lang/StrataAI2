import { describe, expect, it } from 'vitest';
import { createMentionHandleIntent, normalizeMentionHandle, parseMentionHandleAcknowledgment, parseMentionHandleSetting } from './mentionHandle';

const subject = '11111111-1111-1111-1111-111111111111';
const foreign = '22222222-2222-2222-2222-222222222222';
const key = '33333333-3333-3333-3333-333333333333';
const setting = { userId: subject, handle: 'alice', userVersion: 4, handleVersion: 2,
  createdAt: '2026-10-03T10:00:00.123456Z', updatedAt: '2026-10-03T10:00:00.123457+00:00' };
describe('account handle boundary', () => {
  it('matches canonical names and reserves mass/default namespaces without accepting Unicode fragments', () => {
    expect(normalizeMentionHandle('  ALICE  ', subject)).toBe('alice');
    expect(normalizeMentionHandle(`u_${subject.replaceAll('-', '')}`, subject)).toBe(`u_${subject.replaceAll('-', '')}`);
    for (const value of ['card', 'board', 'u_foreign', `u_${foreign.replaceAll('-', '')}`, 'aa', 'nïck', 'a-name', 'a.name', 'a@name', '\ufeffalice', 'x'.repeat(41)])
      expect(() => normalizeMentionHandle(value, subject)).toThrow();
  });
  it('admits only the exact freshly verified account revision and finite microsecond UTC history', () => {
    expect(parseMentionHandleSetting(setting, subject, 4)).toEqual(setting);
    expect(() => parseMentionHandleSetting(setting, foreign, 4)).toThrow();
    expect(() => parseMentionHandleSetting(setting, subject, 5)).toThrow();
    for (const patch of [{ userId: foreign }, { handle: 'ALICE' }, { handleVersion: 0 }, { handleVersion: 5 },
      { userVersion: Number.MAX_SAFE_INTEGER + 1 }, { createdAt: '2026-02-30T10:00:00Z' },
      { createdAt: '2026-10-03T10:00:00.1234567Z' }, { updatedAt: '2026-10-03T10:00:00.123455Z' },
      { updatedAt: '2026-10-03T12:00:00+02:00' }, { createdAt: '0000-01-01T00:00:00Z' },
      { handleVersion: 1 }, { email: 'private@example.test' }])
      expect(() => parseMentionHandleSetting({ ...setting, ...patch }, subject, 4)).toThrow();
  });
  it('freezes normalized original intent and accepts only its exact changed acknowledgment', () => {
    const source = { ...setting }; const intent = createMentionHandleIntent(source, ' BOB ', key);
    source.userVersion = 99; source.handle = 'other';
    expect(Object.isFrozen(intent)).toBe(true); expect(Object.isFrozen(intent.original)).toBe(true);
    expect(intent.body).toBe('{"handle":"bob","userVersion":4,"handleVersion":2}');
    const ack = { userId: subject, handle: 'bob', userVersion: 5, handleVersion: 3, changed: true };
    expect(parseMentionHandleAcknowledgment(ack, intent)).toEqual(ack);
    for (const patch of [{ userId: foreign }, { handle: 'other' }, { userVersion: 6 }, { handleVersion: 4 }, { changed: false }, { displayName: 'Private' }])
      expect(() => parseMentionHandleAcknowledgment({ ...ack, ...patch }, intent)).toThrow();
    expect(() => parseMentionHandleAcknowledgment(ack, { ...intent, body: '{}' })).toThrow();
    expect(() => createMentionHandleIntent(setting, 'bob', 'invalid')).toThrow();
  });
  it('acknowledges a no-op without invented revisions and rejects unsafe increments', () => {
    const intent = createMentionHandleIntent(setting, ' ALICE ', key);
    const ack = { userId: subject, handle: 'alice', userVersion: 4, handleVersion: 2, changed: false };
    expect(parseMentionHandleAcknowledgment(ack, intent)).toEqual(ack);
    expect(() => parseMentionHandleAcknowledgment({ ...ack, changed: true, userVersion: 5, handleVersion: 3 }, intent)).toThrow();
    expect(() => createMentionHandleIntent({ ...setting, userVersion: Number.MAX_SAFE_INTEGER }, 'bob', key)).toThrow();
  });
});
