import test from 'node:test';
import assert from 'node:assert/strict';
import { selectFeatureGate } from './selection.mjs';
const inventory = ['tests/browser/board.spec.ts', 'tests/browser/card-description.spec.ts'];

test('Organization UI changes run feature checks without the complete API suite', () => {
  const plan = selectFeatureGate(['apps/web/src/features/organizations/OrganizationHome.tsx'], inventory);
  assert.equal(plan.profile, 'feature');
  assert.equal(plan.web, true);
  assert.equal(plan.dotnet, false);
  assert.equal(plan.full, false);
  assert.ok(plan.deferred.includes('complete API host regression'));
});
test('backend changes require compilation and API feature checks without full regression', () => {
  const plan = selectFeatureGate(['src/StrataAI.Api/Program.cs'], inventory);
  assert.equal(plan.dotnet, true);
  assert.equal(plan.api, true);
  assert.equal(plan.full, false);
  assert.ok(plan.deferred.includes('complete API host regression'));
});
test('CI and browser helper changes no longer start automatic full qualification', () => {
  for (const path of ['.github/workflows/feature-ci.yml', 'tests/browser/keyboardAdmission.ts', 'scripts/local/verify.mjs'])
    assert.equal(selectFeatureGate([path], inventory).full, false);
});
test('nightly and explicit production candidate retain complete qualification', () => {
  for (const event of ['nightly', 'production-candidate']) {
    const plan = selectFeatureGate(['docs/example.md'], inventory, { event });
    assert.equal(plan.full, true);
    assert.equal(plan.profile, 'qualification');
    assert.deepEqual(plan.deferred, []);
  }
});
test('explicit full request remains available', () => {
  const plan = selectFeatureGate(['docs/example.md'], inventory, { forceFull: true });
  assert.equal(plan.full, true);
  assert.equal(plan.profile, 'qualification');
});
test('documentation retains contracts without compilers', () => {
  const plan = selectFeatureGate(['docs/example.md'], inventory);
  assert.equal(plan.profile, 'docs');
  assert.equal(plan.web, false);
  assert.equal(plan.dotnet, false);
  assert.equal(plan.contracts, true);
});
test('missing diffs and unmapped paths refuse selection instead of silently omitting checks', () => {
  assert.throws(() => selectFeatureGate([], inventory), /comparison/);
  assert.throws(() => selectFeatureGate(['unknown.xyz'], inventory), /Unmapped/);
});
test('unsafe and duplicate paths cannot change qualification coverage', () => {
  for (const path of ['../secrets', '/etc/passwd', '.github/../source', 'apps\\web\\file.ts'])
    assert.throws(() => selectFeatureGate([path], inventory), /path/i);
  assert.deepEqual(selectFeatureGate(['apps/web/src/features/kanban/CardDescriptionPreview.tsx'], inventory),
    selectFeatureGate(['apps/web/src/features/kanban/CardDescriptionPreview.tsx', 'apps/web/src/features/kanban/CardDescriptionPreview.tsx'], inventory));
});
