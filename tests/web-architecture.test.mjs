import assert from 'node:assert/strict';
import { fileURLToPath } from 'node:url';
import test from 'node:test';
import { ESLint } from 'eslint';

const webRoot = fileURLToPath(new URL('../apps/web/', import.meta.url));
const lint = new ESLint({ cwd: webRoot });
const boundaryRules = new Set(['no-restricted-globals', 'no-restricted-properties']);

async function boundaryErrors(source, filePath) {
  const [result] = await lint.lintText(source, { filePath });
  assert.equal(result.fatalErrorCount, 0, 'The fixture must parse before testing the boundary.');
  return result.messages.filter(message => boundaryRules.has(message.ruleId));
}

test('ARCH-02-FR-009 rejects direct and aliased browser fetch in feature clients', async () => {
  for (const source of [
    "export const read = () => fetch('/organizations');",
    "export const read = () => window.fetch('/organizations');",
    "export const read = () => globalThis['fetch']('/organizations');",
    "export const read = () => self.fetch('/organizations');",
    'export const request = fetch;',
    'export const request = window.fetch;',
    'export const { fetch: request } = globalThis;',
  ]) {
    const errors = await boundaryErrors(source, 'src/features/organizations/boundaryFixture.ts');
    assert.ok(errors.length > 0, `A feature bypass must fail lint: ${source}`);
    assert.ok(errors.every(error => error.severity === 2));
  }
});

test('ARCH-02-FR-009 allows the shared transport and isolated test fixtures', async () => {
  const source = "export const read = () => fetch('/organizations');";
  for (const filePath of ['src/api/apiFetch.ts', 'src/features/organizations/boundaryFixture.test.ts',
    'src/features/organizations/boundaryFixture.test.tsx']) {
    assert.deepEqual(await boundaryErrors(source, filePath), []);
  }
});

test('ARCH-02-FR-009 permits feature requests through apiFetch', async () => {
  const source = "import { apiFetch } from '../../api/apiFetch'; export const read = () => apiFetch('/organizations');";
  assert.deepEqual(await boundaryErrors(source, 'src/features/organizations/boundaryFixture.ts'), []);
});
