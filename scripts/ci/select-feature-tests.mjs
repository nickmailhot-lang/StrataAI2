import { readFileSync, writeFileSync, appendFileSync, readdirSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

export const policy = JSON.parse(readFileSync(new URL('./feature-impact.json', import.meta.url)));
export const isDocumentation = path => policy.documentationPatterns.some(pattern => new RegExp(pattern).test(path));
export function selectImpact(paths, inventory, { forceFull = false, smoke = false } = {}) {
  for (const file of policy.baselineBrowser) if (!inventory.includes(file)) throw new Error(`Missing baseline browser file: ${file}`);
  const reasons = [];
  let full = forceFull;
  let web = false;
  let runtime = smoke;
  const selected = new Set(policy.baselineBrowser);
  const features = new Set();
  for (const path of paths) {
    if (!path || path.startsWith('/') || path.split('/').includes('..')) throw new Error('Unsafe changed path');
    if (isDocumentation(path)) continue;
    runtime = true;
    web ||= path.startsWith('apps/web/') || path.startsWith('tests/browser/');
    if (policy.fullPatterns.some(pattern => new RegExp(pattern, 'i').test(path))) {
      full = true; reasons.push(`Shared/runtime/configuration change: ${path}`); continue;
    }
    if (path.startsWith('tests/browser/') && path.endsWith('.spec.ts')) {
      if (inventory.includes(path)) selected.add(path);
      else { full = true; reasons.push(`Removed browser file: ${path}`); }
      continue;
    }
    if (!path.startsWith('apps/web/src/features/')) {
      full = true; reasons.push(`Unmapped change: ${path}`); continue;
    }
    // Most specific category wins; generic Card/List/Board fallbacks come last.
    const match = policy.features.find(feature => new RegExp(feature.source, 'i').test(path.split('/').at(-1)));
    const matches = match ? [match] : [];
    if (!matches.length) { full = true; reasons.push(`Unmapped feature: ${path}`); }
    for (const feature of matches) {
      features.add(feature.name);
      if (feature.full) { full = true; reasons.push(`Real-provider/security coverage: ${feature.name}`); }
      const files = inventory.filter(file => new RegExp(feature.browser, 'i').test(file.split('/').at(-1)));
      if (!files.length) { full = true; reasons.push(`Missing browser mapping: ${feature.name}`); }
      files.forEach(file => selected.add(file));
    }
  }
  if (!paths.length && !smoke) { full = true; reasons.push('Empty or unavailable comparison'); }
  if (forceFull) reasons.push('Explicit full CI request');
  if (selected.size > policy.maxFeatureBrowserFiles) { full = true; reasons.push('Broad browser impact requires the parallel full matrix'); }
  return { schemaVersion: 1, tier: full ? 'full' : runtime ? 'feature' : 'docs', full, web: web || full, runtime: runtime && !full,
    browser: [...selected].filter(file => inventory.includes(file)).sort(),
    commands: policy.baselineCommands, features: [...features].sort(), reasons,
    changedPaths: paths, omitted: full ? [] : ['full PostgreSQL/recovery matrix', 'unaffected browser files', 'image vulnerability/SBOM scans', 'release bundle qualification'] };
}

export function changedPaths(base, head = 'HEAD') {
  if (!/^[0-9a-f]{40}$/.test(base) || !/^(HEAD|[0-9a-f]{40})$/.test(head)) throw new Error('Missing or invalid diff revision');
  // --no-renames includes both the old and new paths. No API pagination/file limit.
  return execFileSync('git', ['diff', '--no-renames', '--name-only', '-z', `${base}...${head}`], { encoding: 'utf8' }).split('\0').filter(Boolean);
}
if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const event = JSON.parse(readFileSync(process.env.GITHUB_EVENT_PATH, 'utf8'));
  const inventory = readdirSync('tests/browser').filter(name => name.endsWith('.spec.ts')).map(name => `tests/browser/${name}`);
  let paths;
  try { paths = changedPaths(event.pull_request?.base.sha ?? event.before); }
  catch { paths = ['__unknown_diff__']; }
  const plan = selectImpact(paths, inventory, {
    forceFull: event.pull_request?.labels?.some(label => label.name === 'full-ci') || process.env.FORCE_FULL === 'true',
    smoke: process.env.CI_PROFILE === 'smoke',
  });
  // Every main push runs the bounded smoke; full coverage is owned by nightly/tag/manual runs.
  if (process.env.CI_PROFILE === 'smoke') Object.assign(plan, { tier: 'feature', full: false, runtime: true, web: false, browser: policy.baselineBrowser, reasons: ['Main smoke; full qualification remains separate'] });
  plan.revision = execFileSync('git', ['rev-parse', 'HEAD'], { encoding: 'utf8' }).trim();
  writeFileSync('feature-plan.json', JSON.stringify(plan, null, 2) + '\n');
  for (const key of ['full', 'web', 'runtime']) appendFileSync(process.env.GITHUB_OUTPUT, `${key}=${plan[key]}\n`);
  appendFileSync(process.env.GITHUB_OUTPUT, `browser=${JSON.stringify(plan.browser)}\n`);
  appendFileSync(process.env.GITHUB_STEP_SUMMARY, `Feature selection for ${plan.revision}: full=${plan.full}, web=${plan.web}, runtime=${plan.runtime}.\n\nFeatures: ${plan.features.join(', ') || 'baseline'}.\n\nReasons: ${plan.reasons.join('; ') || 'mapped feature changes'}.\n\nDeferred to full sanity: ${plan.omitted.join(', ') || 'none'}.\n`);
}
