import { EventEmitter } from 'node:events';
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { trackInvitationAdmission, trackBoardHistoryChanges } from './invitationAdmissionTracker.ts';

function fixture(board, protectedReadPath) {
  const page = new EventEmitter(); let path = '/previous'; page.url = () => `https://example.test${path}`;
  const target = board ? '/app/org/boards/board/invite' : '/app/org/invite';
  const read = board ? '/boards/board' : '/organizations/org/members/actor';
  const tracker = trackInvitationAdmission(page, 'org', 'actor', board, target, protectedReadPath);
  const socket = new EventEmitter(); socket.url = () => `https://example.test${board ? '/boards/live' : '/organizations/live/metadata'}`;
  page.emit('websocket', socket);
  const send = (invocationId, scope = board ?? 'org') => socket.emit('framesent', { payload: JSON.stringify({ type: 4, target: 'Watch', arguments: [scope], invocationId }) + '\u001e' });
  const head = (invocationId, value = { cursor: 'opaque', resetRequired: false, events: [] }) => socket.emit('framereceived', {
    payload: JSON.stringify({ type: 2, invocationId, item: board ? value : { organizationId: 'org', userId: 'actor', page: value } }) + '\u001e' });
  const request = () => { const value = { method: () => 'GET', url: () => `https://example.test${read}` }; page.emit('request', value); return value; };
  const response = (value, status = 200) => page.emit('response', { request: () => value, status: () => status });
  return { page, socket, tracker, send, head, request, response, navigate: () => { path = target; } };
}

for (const board of [undefined, 'board']) {
  test(`admission excludes previous-screen heads and reads (Board=${!!board})`, () => {
    const f = fixture(board); f.send('old'); f.head('old'); const old = f.request();
    f.navigate(); f.send('current'); const preHead = f.request(); f.head('current');
    f.response(old); f.response(preHead); assert.equal(f.tracker.ready(), false);
    f.response(f.request()); assert.equal(f.tracker.ready(), true); assert.equal(f.tracker.reads(), 1);
    f.head('current'); assert.equal(f.tracker.heads(), 1);
  });
  test(`fresh heads fence an earlier in-flight admission read (Board=${!!board})`, () => {
    const f = fixture(board); f.navigate(); f.send('first'); f.head('first'); f.response(f.request());
    const held = f.request(); f.send('recovered'); f.head('recovered'); f.response(held);
    assert.equal(f.tracker.ready(), false); assert.equal(f.tracker.heads(), 2);
    f.response(f.request(), 503); assert.equal(f.tracker.ready(), false);
    f.response(f.request()); assert.equal(f.tracker.ready(), true); assert.equal(f.tracker.reads(), 2);
  });
}
test('foreign scopes, malformed heads and failed reads cannot satisfy admission', () => {
  const f = fixture('board'); f.navigate(); f.send('foreign', 'another-board'); f.head('foreign');
  f.send('valid'); f.head('valid', {}); assert.equal(f.tracker.heads(), 0);
  f.head('valid'); const failed = f.request(); f.page.emit('requestfailed', failed); f.response(failed);
  assert.equal(f.tracker.ready(), false);
});
test('Organization heads retain actor and Organization envelope admission', () => {
  const f = fixture(undefined); f.navigate(); f.send('valid');
  for (const item of [{ organizationId: 'org', userId: 'other-actor', page: { cursor: 'opaque', resetRequired: false, events: [] } },
    { organizationId: 'other-org', userId: 'actor', page: { cursor: 'opaque', resetRequired: false, events: [] } }])
    f.socket.emit('framereceived', { payload: JSON.stringify({ type: 2, invocationId: 'valid', item }) + '\u001e' });
  assert.equal(f.tracker.heads(), 0); f.head('valid'); f.response(f.request()); assert.equal(f.tracker.ready(), true);
});

test('history admission requires its protected collection after the head', () => {
  const f = fixture(undefined, '/organizations/org/invitations');
  f.navigate(); f.send('history'); f.head('history');
  f.response(f.request()); assert.equal(f.tracker.ready(), false);
  const history = { method: () => 'GET', url: () => 'https://example.test/organizations/org/invitations' };
  f.page.emit('request', history); f.response(history, 503); assert.equal(f.tracker.ready(), false);
  const current = { method: () => 'GET', url: () => 'https://example.test/organizations/org/invitations' };
  f.page.emit('request', current); f.response(current); assert.equal(f.tracker.ready(), true);
});


function historyFixture() {
  const page = new EventEmitter(); page.url = () => 'https://example.test/app/org/boards/board/invitations';
  const tracker = trackBoardHistoryChanges(page, 'org', 'board', '/app/org/boards/board/invitations');
  const socket = new EventEmitter(); socket.url = () => 'https://example.test/boards/live'; page.emit('websocket', socket);
  socket.emit('framesent', { payload: JSON.stringify({ type: 4, target: 'Watch', arguments: ['board'], invocationId: 'watch' }) });
  const event = { eventId: 'source', eventType: 'BOARD_MEMBER_INVITED', organizationId: 'org', boardId: 'board' };
  const frame = (events = [], invocationId = 'watch') => socket.emit('framereceived', { payload: JSON.stringify({
    type: 2, invocationId, item: { cursor: 'opaque', pending: false, resetRequired: false, events },
  }) });
  const request = (path = '/boards/board/invitations') => {
    const value = { method: () => 'GET', url: () => 'https://example.test' + path }; page.emit('request', value); return value;
  };
  const response = (value, status = 200) => page.emit('response', { request: () => value, status: () => status });
  return { tracker, frame, event, request, response };
}

test('Board history cannot admit consent with a read started before the actual invitation event', () => {
  const f = historyFixture(); f.frame(); const old = f.request(); f.frame([f.event]); f.response(old);
  assert.equal(f.tracker.count('BOARD_MEMBER_INVITED'), 1); assert.equal(f.tracker.settled('BOARD_MEMBER_INVITED', 1), false);
  f.response(f.request()); assert.equal(f.tracker.settled('BOARD_MEMBER_INVITED', 1), true);
});
test('Board history deduplicates replayed frames without requiring an invented new read', () => {
  const f = historyFixture(); f.frame([f.event]); f.response(f.request()); f.frame([f.event]);
  assert.equal(f.tracker.count('BOARD_MEMBER_INVITED'), 1); assert.equal(f.tracker.ready(), true);
});
test('Board history rejects foreign Board and unobserved Watch frames', () => {
  const f = historyFixture(); f.frame([f.event], 'other'); f.frame([{ ...f.event, boardId: 'foreign' }]); f.response(f.request());
  assert.equal(f.tracker.count('BOARD_MEMBER_INVITED'), 0); assert.equal(f.tracker.ready(), false);
});
test('Board history requires its own successful protected read after the latest change', () => {
  const f = historyFixture(); f.frame([f.event]); f.response(f.request('/boards/board')); f.response(f.request(), 503);
  assert.equal(f.tracker.ready(), false); f.response(f.request()); assert.equal(f.tracker.ready(), true);
  f.frame([{ ...f.event, eventId: 'second', eventType: 'INVITATION_ACCEPTED' }]); assert.equal(f.tracker.ready(), false);
});
