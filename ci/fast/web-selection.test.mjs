import test from 'node:test';
import assert from 'node:assert/strict';
import { selectWebTests, criticalWebTests } from './web-selection.mjs';

test('actual Home source retains all original component/focus tests and critical admission/auth checks', () => {
  const plan = selectWebTests('.', ['apps/web/src/features/organizations/OrganizationHome.tsx']);
  for (const name of ['OrganizationHome.test.tsx','OrganizationHome.actions.test.tsx','OrganizationHome.creation-focus.test.tsx','OrganizationHome.focus.test.tsx'])
    assert.ok(plan.files.includes(`apps/web/src/features/organizations/${name}`));
  for (const path of criticalWebTests) assert.ok(plan.files.includes(path));
  assert.equal(plan.productionQualified, false);
  assert.equal(plan.selection, 'source-dependencies-and-critical-units');
});
test('removed source and dependency configuration retain all web tests without API qualification', () => {
  const removed = selectWebTests('.', ['apps/web/src/removed.ts']);
  const lock = selectWebTests('.', ['package-lock.json']);
  assert.deepEqual(removed.files, lock.files);
  assert.ok(lock.files.length > 100);
  assert.equal(lock.selection, 'all-web-units');
  assert.equal(lock.productionQualified, false);
});
test('missing critical coverage and unsafe comparisons refuse selection', () => {
  assert.throws(() => selectWebTests('.', ['package-lock.json'], ['missing.test.ts']), /critical/);
  for (const paths of [[], ['../private'], ['/private'], ['apps\\web\\src']])
    assert.throws(() => selectWebTests('.', paths));
});
