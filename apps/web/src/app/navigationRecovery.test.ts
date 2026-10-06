import { beforeEach, expect, it } from 'vitest';
import { createNavigationIntent } from './navigationObservation';
import { completeNavigationIntent, restoreNavigationIntent, retainNavigationIntent } from './navigationRecovery';
const actor = '11111111-1111-4111-8111-111111111111';
const organization = '22222222-2222-4222-8222-222222222222';
const board = '33333333-3333-4333-8333-333333333333';
const now = Date.parse('2026-10-05T12:00:00Z');
const target = { kind: 'board' as const, organization, board, version: 3 };
beforeEach(() => sessionStorage.clear());
it('PRD-01 restores the original revision on a return visit and isolates account and entity scope', () => {
  const intent = createNavigationIntent(actor, target, now); retainNavigationIntent(sessionStorage, intent, now);
  expect(restoreNavigationIntent(sessionStorage, actor, { ...target, version: 4 }, now + 1)).toEqual(intent);
  expect(restoreNavigationIntent(sessionStorage, organization, target, now + 1)).toBeUndefined();
  expect(restoreNavigationIntent(sessionStorage, actor, { ...target, board: organization }, now + 1)).toBeUndefined();
  expect(() => retainNavigationIntent(sessionStorage, createNavigationIntent(actor, target, now + 1), now + 1)).toThrow('original');
  expect(Object.isFrozen(restoreNavigationIntent(sessionStorage, actor, target, now + 1)!.target)).toBe(true);
});
it('PRD-01 completes only the matching original and expires receipts after their 24 hour retry window', () => {
  const intent = createNavigationIntent(actor, target, now); retainNavigationIntent(sessionStorage, intent, now);
  completeNavigationIntent(sessionStorage, createNavigationIntent(actor, target, now));
  expect(restoreNavigationIntent(sessionStorage, actor, target, now + 1)).toEqual(intent);
  completeNavigationIntent(sessionStorage, intent); expect(sessionStorage.length).toBe(0);
  retainNavigationIntent(sessionStorage, intent, now);
  expect(restoreNavigationIntent(sessionStorage, actor, target, now + 86400000)).toBeUndefined();
  expect(sessionStorage.length).toBe(0);
});
it('PRD-01 refuses to evict live originals when recovery capacity is full', () => {
  for (let index = 0; index < 1000; index++) sessionStorage.setItem(`strataai:navigation:v1:retained:${index}`, 'retained');
  expect(() => retainNavigationIntent(sessionStorage, createNavigationIntent(actor, target, now), now)).toThrow('full');
  expect(sessionStorage.length).toBe(1000);
});
it('PRD-01 reclaims only expired canonical originals owned by the current account', () => {
  const old = createNavigationIntent(actor, target, now);
  retainNavigationIntent(sessionStorage, old, now);
  const foreign = createNavigationIntent(organization, target, now);
  retainNavigationIntent(sessionStorage, foreign, now);
  for (let index = 0; index < 998; index++) sessionStorage.setItem(`strataai:navigation:v1:retained:${index}`, 'retained');
  const current = createNavigationIntent(actor, { ...target, board: organization }, now + 86400000);
  retainNavigationIntent(sessionStorage, current, now + 86400000);
  expect(sessionStorage.length).toBe(1000);
  expect(restoreNavigationIntent(sessionStorage, actor, target, now + 86400000)).toBeUndefined();
  // Reading an unrelated account's original is not part of reclamation.
  expect(Object.values(sessionStorage)).toContain(JSON.stringify(foreign));
  expect(restoreNavigationIntent(sessionStorage, actor, current.target, now + 86400000)).toEqual(current);
});
