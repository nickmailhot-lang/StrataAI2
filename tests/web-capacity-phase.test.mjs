import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import vm from 'node:vm';
import ts from 'typescript';

const folder = new URL('../apps/web/src/features/kanban/', import.meta.url);
const capacity = readFileSync(new URL('BoardWindow.capacity.test.tsx', folder), 'utf8').replaceAll('\r\n', '\n');
const functional = readFileSync(new URL('BoardWindow.test.tsx', folder), 'utf8').replaceAll('\r\n', '\n');

// Collect real source registrations without running test bodies or lifecycle hooks.
// Even opt-in discovery must not allocate a large dataset.
function collect(source, flag) {
  let enabled = true;
  const cases = [], allocations = [], hooks = [];
  const register = (name, body) => {
    assert.equal(typeof body, 'function');
    cases.push({ name, enabled });
  };
  register.each = rows => (name, body) => rows.forEach(row => register(name.replace('%s', String(row)), body));
  const describe = (name, body) => body();
  describe.runIf = condition => (name, body) => {
    const prior = enabled; enabled = prior && condition;
    try { body(); } finally { enabled = prior; }
  };
  const array = new Proxy(Array, { get(target, key) {
    if (key !== 'from') return Reflect.get(target, key);
    return (input, ...args) => {
      assert.ok(input.length <= 128, 'Capacity datasets must be allocated only inside deferred lifecycle hooks.');
      allocations.push(input.length);
      return Array.from(input, ...args);
    };
  } });
  const hook = body => { assert.equal(typeof body, 'function'); hooks.push({ enabled }); };
  const context = vm.createContext({ exports: {},
    require: name => name === 'node:process' ? { env: { STRATAAI_POST_MVP_LOAD_TESTS: flag } } : {},
    Array: array, Map, Set, describe, it: register, beforeAll: hook, beforeEach: hook, afterEach: hook,
    vi: { fn: () => () => {}, mock: () => {} } });
  const js = ts.transpileModule(source, { compilerOptions: {
    module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2023, jsx: ts.JsxEmit.ReactJSX,
  } }).outputText;
  vm.runInContext(js, context, { timeout: 1000 });
  return { cases, allocations, hooks };
}

test('MVP source discovery defers all original Board capacity cases without allocating their datasets', () => {
  for (const flag of [undefined, '0', 'true', '01']) {
    const result = collect(capacity, flag);
    assert.equal(result.cases.length, 24);
    assert.ok(result.cases.every(row => !row.enabled));
    assert.deepEqual(result.allocations, []);
    assert.ok(result.hooks.every(row => !row.enabled));
  }
});

test('post-MVP explicit opt-in collects the original capacity cases without executing them', () => {
  const result = collect(capacity, '1');
  assert.equal(result.cases.length, 24);
  assert.ok(result.cases.every(row => row.enabled));
  assert.deepEqual(result.allocations, []);
  assert.ok(result.hooks.every(row => row.enabled));
  for (const name of ['bounds mounted work for 5000 Cards and renders the canonical end after scrolling',
    'bounds mounted Lists at 200 and restores a retained Board scroll position after remount',
    'tabs forward and backward across a window boundary in canonical order',
    'waits for the canonical boundary handle instead of skipping it during temporary disablement']) {
    assert.ok(result.cases.some(row => row.name === name));
  }
});

test('small virtualization and keyboard correctness cases remain active in every phase', () => {
  for (const flag of [undefined, '0', '1']) {
    const result = collect(functional, flag);
    assert.equal(result.cases.length, 24);
    assert.ok(result.cases.every(row => row.enabled));
    assert.ok(result.hooks.every(row => row.enabled));
    assert.deepEqual(result.allocations, [128, 32]);
    const originals = collect(capacity, flag).cases.map(row => row.name);
    const small = result.cases.map(row => row.name);
    assert.equal(originals.filter(name => small.includes(name)).length, 21);
  }
});

test('phase guard detects eager allocation and accidental default activation', () => {
  const eager = capacity.replace('beforeAll(() => {', '(() => {').replace(
    'length: 200 }, (_, i) => ({ id: `list-${i}` }));\n});',
    'length: 200 }, (_, i) => ({ id: `list-${i}` }));\n})();');
  assert.notEqual(eager, capacity);
  assert.throws(() => collect(eager, '0'), /Capacity datasets/);
  const active = capacity.replace("env.STRATAAI_POST_MVP_LOAD_TESTS === '1'", 'Boolean(env)');
  assert.notEqual(active, capacity);
  assert.ok(collect(active, '0').cases.some(row => row.enabled));
});
