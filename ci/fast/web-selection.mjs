import ts from 'typescript';
import { readFileSync, readdirSync, existsSync } from 'node:fs';
import { resolve, relative, dirname } from 'node:path';

export const criticalWebTests = [
  'apps/web/src/api/apiProblem.test.ts',
  'apps/web/src/app/SurfaceAdmission.test.tsx',
  'apps/web/src/app/SurfaceAdmission.return.test.tsx',
  'apps/web/src/app/UnsavedChangesGuard.admission.test.tsx',
  'apps/web/src/features/auth/AuthPage.test.tsx',
  'apps/web/src/features/auth/AuthPage.return.test.tsx',
];

// Resolve actual source dependencies with the same TypeScript configuration as
// the application. Removed files/configuration changes conservatively select all
// web units; that never pulls the separate complete API/runtime matrix into PRs.
export function selectWebTests(root, changedPaths, baseline = criticalWebTests) {
  if (!Array.isArray(changedPaths) || !changedPaths.length) throw new Error('Explicit web comparison required');
  for (const path of changedPaths)
    if (typeof path !== 'string' || path.startsWith('/') || path.includes('\\')
      || path.split('/').some(p => !p || p === '.' || p === '..') || /[\x00-\x1f]/.test(path))
      throw new Error('Unsafe web comparison path');
  root = resolve(root);
  const sourceRoot = resolve(root, 'apps/web/src');
  const inventory = [];
  const visit = folder => {
    for (const entry of readdirSync(folder, { withFileTypes: true })) {
      if (entry.isSymbolicLink()) throw new Error('Unexpected symbolic source path');
      const file = resolve(folder, entry.name);
      if (entry.isDirectory()) visit(file);
      else inventory.push(relative(root, file));
    }
  };
  visit(sourceRoot);
  const tests = inventory.filter(p => /\.test\.tsx?$/.test(p)).sort();
  if (!tests.length || !baseline.length || baseline.some(p => !tests.includes(p)))
    throw new Error('Missing critical web test inventory');
  const configPath = resolve(root, 'apps/web/tsconfig.json');
  const config = ts.readConfigFile(configPath, ts.sys.readFile);
  if (config.error) throw new Error('Unable to read web TypeScript configuration');
  const options = ts.parseJsonConfigFileContent(config.config, ts.sys, dirname(configPath)).options;
  const fallback = changedPaths.some(p => !p.startsWith('apps/web/src/') || !inventory.includes(p));
  if (fallback) return { files: tests, selection: 'all-web-units', reason: 'Configuration, helper or removed source comparison', productionQualified: false };
  const reverse = new Map();
  for (const path of inventory.filter(p => /\.[cm]?[jt]sx?$/.test(p))) {
    const file = resolve(root, path), text = readFileSync(file, 'utf8');
    const dependencies = new Set(ts.preProcessFile(text, true, true).importedFiles.map(i => i.fileName));
    const ast = ts.createSourceFile(file, text, ts.ScriptTarget.Latest, true);
    const scan = node => {
      // Include literal dynamic/mock imports as well as regular declarations.
      if (ts.isCallExpression(node)) for (const arg of node.arguments)
        if (ts.isStringLiteralLike(arg) && /^\.\.?\//.test(arg.text)) dependencies.add(arg.text);
      ts.forEachChild(node, scan);
    };
    scan(ast);
    for (const specifier of dependencies) {
      let target = ts.resolveModuleName(specifier, file, options, ts.sys).resolvedModule?.resolvedFileName;
      if (!target && /^\.\.?\//.test(specifier)) {
        const asset = resolve(dirname(file), specifier);
        if (existsSync(asset)) target = asset;
        else throw new Error(`Unresolved local web dependency requires review: ${path}`);
      }
      if (!target) continue; // External packages are handled by all-unit lock/config changes.
      const key = relative(root, target);
      if (!reverse.has(key)) reverse.set(key, new Set());
      reverse.get(key).add(path);
    }
  }
  const affected = new Set(changedPaths), queue = [...changedPaths];
  while (queue.length) for (const consumer of reverse.get(queue.shift()) || [])
    if (!affected.has(consumer)) { affected.add(consumer); queue.push(consumer); }
  const files = [...new Set([...baseline, ...tests.filter(p => affected.has(p))])].sort();
  return { files, selection: 'source-dependencies-and-critical-units', productionQualified: false };
}
