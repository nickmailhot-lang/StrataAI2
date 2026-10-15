import assert from 'node:assert/strict';
import test from 'node:test';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { sessionCookie, expectedCardEvent, checkPage, assertAnonymousDenied } from '../scripts/ci/check-https-live-board.mjs';

const id = n => `${String(n).repeat(8)}-1111-1111-1111-111111111111`;
const scope = { organization: id(1), board: id(2), card: id(3), actor: id(4) };
const event = () => ({ organizationId: scope.organization, boardId: scope.board, entityId: scope.card,
  actorId: scope.actor, entityType: 'Card', eventType: 'CARD_UPDATED', version: 2,
  eventId: id(5), sequence: '1', createdAt: '2026-10-15T00:00:00Z', metadata: {} });
const page = () => ({ cursor: '1', pending: false, hasMore: false, resetRequired: false, events: [event()] });
const jar = '#HttpOnly_localhost\tFALSE\t/\tTRUE\t2000000000\tstrataai_session\tfixture_opaque_session_1234567890\n';
const denied = () => ({ handshake: true, closed: true, protocolFailure: false, buffer: '', frames: [] });
test('an admitted, closed, content-free anonymous connection proves denial', () => assertAnonymousDenied(denied()));
for (const [field, value] of [['handshake',false],['closed',false],['protocolFailure',true],['buffer','partial private output'],['frames',[page()]]])
  test(`unsafe anonymous ${field} cannot count as denial`, () => assert.throws(() => assertAnonymousDenied({ ...denied(), [field]: value })));
for (const [name, args] of [['missing arguments', []], ['nonlocal target', ['https://foreign.example.test:443','unused',...Object.values(scope)]]])
  test(`CLI refuses ${name} before cookie or network access`, () => {
    const result = spawnSync(process.execPath, [fileURLToPath(new URL('../scripts/ci/check-https-live-board.mjs', import.meta.url)), ...args],
      { encoding: 'utf8', timeout: 5000 });
    assert.equal(result.status, 1); assert.ok(!result.stdout.includes('passed:'));
    assert.match(result.stderr, /HTTPS live Board contract failed/);
  });

test('normal Secure curl cookie and exact event are admitted', () => {
  assert.equal(sessionCookie(jar), 'strataai_session=fixture_opaque_session_1234567890');
  assert.equal(expectedCardEvent(event(), scope, 2), true); checkPage(page(), scope);
});
for (const [name, change] of [
  ['missing session', value => value.replace('strataai_session', 'other')],
  ['foreign host', value => value.replace('localhost', 'foreign.example.test')],
  ['insecure cookie', value => value.replace('\tTRUE\t', '\tFALSE\t')],
  ['different cookie path', value => value.replace('\t/\t', '\t/other\t')],
  ['ambiguous sessions', value => value + value],
  ['header injection', value => value.replace('fixture_opaque_session_1234567890', 'bad; injected=value')],
]) test(`private cookie fixture rejects ${name}`, () => assert.throws(() => sessionCookie(change(jar))));

for (const [field, value] of [['organizationId',id(6)],['boardId',id(6)],['entityId',id(6)],['actorId',id(6)],
  ['entityType','Board'],['eventType','CARD_CREATED'],['version',1]])
  test(`an unrelated ${field} cannot satisfy delivered-edit evidence`, () => {
    assert.equal(expectedCardEvent({ ...event(), [field]: value }, scope, 2), false);
  });

for (const [name, change] of [
  ['private field', value => { value.events[0].title = 'Private'; }],
  ['private metadata', value => { value.events[0].metadata.title = 'Private'; }],
  ['foreign Organization', value => { value.events[0].organizationId = id(6); }],
  ['foreign Board', value => { value.events[0].boardId = id(6); }],
  ['duplicate event', value => { value.events.push({ ...value.events[0] }); }],
  ['event beyond cursor', value => { value.events[0].sequence = '2'; }],
  ['malformed event ID', value => { value.events[0].eventId = '-'.repeat(36); }],
]) test(`live-page evidence rejects ${name}`, () => { const value = page(); change(value); assert.throws(() => checkPage(value, scope)); });
