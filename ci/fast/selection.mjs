import { selectImpact } from '../../scripts/ci/select-feature-tests.mjs';
import { createHash } from 'node:crypto';

// Keep the existing feature dependency map and full qualification intact. This
// policy changes which evidence blocks ordinary development, not which evidence
// is required before promoting an exact production candidate.
const deferred = [
  'complete API host regression', 'complete PostgreSQL and recovery matrix',
  'all production browser viewports and cross-feature scenarios',
  'immutable image vulnerability/SBOM and release bundle qualification',
];
const frontend = /^apps\/web\//;
const backend = /^(src\/|tests\/StrataAI\.|db\/|deploy\/|Dockerfile(?:\.api|\.worker)?$|compose[^/]*$)/;
const dotnetConfiguration = /(^|\/)(?:global\.json|Directory\.[^/]+|[^/]+\.(?:csproj|slnx)|packages\.lock\.json)$/;
const dependencies = /(^|\/)package(?:-lock)?\.json$/;
const tooling = /^(?:\.github\/|\.githooks\/|scripts\/|ci\/|tests\/|AGENTS\.md$|\.gitignore$|\.gitattributes$|\.dockerignore$|\.editorconfig$|Dockerfile|compose)/;

export function selectFeatureGate(paths, inventory, { event = 'pull-request', forceFull = false } = {}) {
  if (!['pull-request', 'nightly', 'production-candidate', 'manual', 'merge-queue'].includes(event))
    throw new Error('Unknown CI event');
  const qualification = forceFull || event === 'nightly' || event === 'production-candidate';
  if (!Array.isArray(paths) || !qualification && paths.length === 0) throw new Error('A complete changed-path comparison is required');
  for (const path of paths)
    if (typeof path !== 'string' || !path || path.startsWith('/') || path.includes('\\')
      || path.split('/').some(part => !part || part === '.' || part === '..') || /[\x00-\x1f]/.test(path))
      throw new Error('Unsafe changed path');
  const changedPaths = [...new Set(paths)].sort();
  const impact = selectImpact(changedPaths, inventory, { forceFull: qualification });
  let web = false, dotnet = false, api = false;
  for (const path of changedPaths) {
    if (/^(?:docs\/|README\.md$)/.test(path)) continue;
    if (frontend.test(path) || /^tests\/browser\//.test(path)) web = true;
    else if (backend.test(path) || dotnetConfiguration.test(path)) dotnet = api = true;
    else if (dependencies.test(path)) web = true;
    else if (!qualification && !tooling.test(path)) throw new Error(`Unmapped changed path requires dependency review: ${path}`);
  }
  const docsOnly = changedPaths.every(path => /^(?:docs\/|README\.md$)/.test(path));
  const plan = {
    schemaVersion: 1, policyVersion: 'feature-gate-2026-10-31',
    profile: qualification ? 'qualification' : docsOnly ? 'docs' : 'feature',
    event, full: qualification, contracts: true,
    web: qualification || web, dotnet: qualification || dotnet, api: qualification || api,
    // Dependency-selected browser inventory remains explicit, even when its
    // comprehensive production execution belongs to nightly/candidate testing.
    affectedBrowser: impact.browser,
    features: impact.features, changedPaths,
    dependencyReview: impact.reasons,
    deferred: qualification ? [] : [...deferred],
    qualificationRequiredBeforeProduction: true,
    targetExecutionMs: qualification ? null : 600_000,
  };
  plan.scopeFingerprint = createHash('sha256').update(JSON.stringify(plan)).digest('hex');
  return plan;
}
