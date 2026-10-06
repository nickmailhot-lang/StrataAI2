import { expect, it } from 'vitest';
import { ChangedNavigationActor, NavigationAcknowledgments, parseNavigationInteraction } from './navigationInteraction';
const actor = '11111111-1111-4111-8111-111111111111';
const organization = '22222222-2222-4222-8222-222222222222';
const board = '33333333-3333-4333-8333-333333333333';
const card = '44444444-4444-4444-8444-444444444444';
const original = () => ({ eventId: actor, actorId: actor, organizationId: organization, boardId: board,
  eventType: 'CARD_OPENED', entityType: 'Card', entityId: card, version: 3,
  metadata: {}, createdAt: '2026-10-05T12:00:00.123456+00:00' });
const target = { kind: 'card' as const, organization, board, card, version: 3 };

it('PRD-01 admits canonical Card and Board observations against independently supplied targets', () => {
  expect(parseNavigationInteraction(original(), actor, target)).toEqual(original());
  const boardSource = { ...original(), eventType: 'BOARD_OPENED', entityType: 'Board', entityId: board };
  expect(parseNavigationInteraction(boardSource, actor, { kind: 'board', organization, board, version: 3 })).toEqual(boardSource);
});
it('PRD-01 admits global and Organization context with their canonical entity identities', () => {
  const global = { ...original(), eventType: 'APPLICATION_CONTEXT_CHANGED', entityType: 'ApplicationContext',
    organizationId: null, boardId: null, entityId: actor, version: 1 };
  expect(parseNavigationInteraction(global, actor, { kind: 'context', organization: null })).toEqual(global);
  const scoped = { ...global, organizationId: organization, entityType: 'Organization', entityId: organization };
  expect(parseNavigationInteraction(scoped, actor, { kind: 'context', organization })).toEqual(scoped);
});
it('PRD-01 refuses changed accounts, protected scope, revisions, clocks and metadata', () => {
  expect(() => parseNavigationInteraction({ ...original(), actorId: organization }, actor, target)).toThrow(ChangedNavigationActor);
  for (const source of [null, { ...original(), extra: 'private' }, { ...original(), metadata: { title: 'private' } },
    { ...original(), boardId: organization }, { ...original(), organizationId: board }, { ...original(), entityId: board },
    { ...original(), version: 2 }, { ...original(), createdAt: '2026-02-30T12:00:00Z' },
    { ...original(), createdAt: '2026-10-05T12:00:00+01:00' }])
    expect(() => parseNavigationInteraction(source, actor, target)).toThrow();
  expect(() => parseNavigationInteraction(original(), actor, { ...target, version: Number.MAX_SAFE_INTEGER + 1 })).toThrow();
});
it('PRD-01 deduplicates original EventIds and resets the bounded window on account change', () => {
  const consumer = new NavigationAcknowledgments(); const source = parseNavigationInteraction(original(), actor, target);
  expect(consumer.consume(source)).toBe(true); expect(consumer.consume(source)).toBe(false);
  expect(() => consumer.consume({ ...source, createdAt: '2026-10-05T12:00:01Z' })).toThrow();
  expect(consumer.consume(parseNavigationInteraction({ ...original(), actorId: organization }, organization, target))).toBe(true);
  expect(consumer.consume(source)).toBe(true);
  consumer.clear(); expect(consumer.consume(source)).toBe(true);
});
