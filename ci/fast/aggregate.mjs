import { createHash } from 'node:crypto';

// This aggregate is intentionally independent of the existing full-qualification
// validator. A development pass can never become a production release receipt.
export function requireGateResults({ plan, results, expectedScopeFingerprint, expectedHead, observedHead, startedAt, finishedAt }) {
  if (!plan || plan.schemaVersion !== 1 || !['docs','feature','qualification'].includes(plan.profile))
    throw new Error('Invalid gate plan');
  for (const field of ['full','contracts','web','dotnet','api'])
    if (typeof plan[field] !== 'boolean') throw new Error(`Invalid gate selection: ${field}`);
  if (!plan.contracts || plan.full !== (plan.profile === 'qualification')) throw new Error('Contradictory gate selection');
  if (!/^[0-9a-f]{40}$/.test(expectedHead) || observedHead !== expectedHead) throw new Error('Candidate revision changed');
  if (!/^[0-9a-f]{64}$/.test(expectedScopeFingerprint) || plan.scopeFingerprint !== expectedScopeFingerprint)
    throw new Error('Candidate scope changed');
  const { scopeFingerprint, ...scope } = plan;
  if (createHash('sha256').update(JSON.stringify(scope)).digest('hex') !== scopeFingerprint)
    throw new Error('Candidate scope was modified after selection');
  const selected = { contracts: true, web: !plan.full && plan.web, dotnet: !plan.full && plan.dotnet,
    api: !plan.full && plan.api, full: plan.full };
  if (!results || Object.keys(results).some(name => !Object.hasOwn(selected, name))) throw new Error('Unexpected gate job');
  for (const [name, needed] of Object.entries(selected))
    if (results[name] !== (needed ? 'success' : 'skipped')) throw new Error(`Required gate result mismatch: ${name}`);
  const parse = value => typeof value === 'string' && /^\d{4}-\d{2}-\d{2}T.*Z$/.test(value) ? Date.parse(value) : NaN;
  const start = parse(startedAt), finish = parse(finishedAt), elapsedMs = finish - start;
  if (!Number.isFinite(elapsedMs) || elapsedMs < 0) throw new Error('Missing or invalid gate timing');
  if (!plan.full && (plan.targetExecutionMs !== 600_000 || elapsedMs >= plan.targetExecutionMs))
    throw new Error('Feature gate exceeded its execution budget');
  return { revision: expectedHead, scopeFingerprint: plan.scopeFingerprint, profile: plan.profile, elapsedMs,
    result: 'Passed', productionQualified: false,
    qualification: plan.full ? 'Full workflow must independently emit exact candidate qualification.' : 'Feature evidence only; full nightly/candidate qualification remains separate.' };
}
