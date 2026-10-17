import { createHash } from 'node:crypto';
import { execFileSync } from 'node:child_process';
import { readFileSync, writeFileSync, renameSync, unlinkSync, existsSync, realpathSync } from 'node:fs';
import { resolve, relative, isAbsolute, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

export const states = ['Ready', 'In Progress', 'Implemented', 'Testing', 'Review', 'Done', 'Blocked'];
const kinds = ['Epic', 'PRD', 'Vertical Slice', 'Task', 'Test'];
const gates = ['tests', 'acceptance', 'security', 'roles', 'regressions', 'integration', 'tracking', 'review', 'ux', 'build'];
const cannotWaive = new Set(['tests', 'acceptance', 'regressions', 'integration', 'tracking', 'review']);
const next = { Ready: ['In Progress'], 'In Progress': ['Implemented'], Implemented: ['Testing'], Testing: ['Review', 'In Progress'], Review: ['Done', 'In Progress'], Done: ['In Progress'] };
const sha = value => createHash('sha256').update(value).digest('hex');
function requireThat(condition, message) { if (!condition) throw new Error(message); }
function unique(values, label) {
  requireThat(values.every(x => typeof x === 'string' && x.length > 0), `Invalid ${label} identifier`);
  requireThat(new Set(values).size === values.length, `Duplicate ${label} identifier`);
}
function within(root, path) {
  requireThat(typeof path === 'string' && path.length > 0 && !isAbsolute(path) && !/^[A-Za-z]:|\\/.test(path), `Unsafe repository evidence path: ${path}`);
  const full = resolve(root, path), rel = relative(root, full);
  requireThat(rel !== '..' && !rel.startsWith('../') && !rel.startsWith('..\\') && !isAbsolute(rel), `Evidence escapes repository: ${path}`);
  if (existsSync(full)) {
    const actual = relative(realpathSync(root), realpathSync(full));
    requireThat(actual !== '..' && !actual.startsWith('../') && !actual.startsWith('..\\') && !isAbsolute(actual), `Evidence symlink escapes repository: ${path}`);
  }
  return full;
}
function canTransition(from, to, history) {
  if (to === 'Blocked') return from !== 'Done' && from !== 'Blocked';
  if (from === 'Blocked') return history.at(-1)?.from === to;
  return next[from]?.includes(to);
}

/** Fail closed: structural state, evidence identity, acceptance and actual Git integration. */
export function validateLedger(ledger, { root, verifyGit = true } = {}) {
  requireThat(ledger.schemaVersion === 1, 'Unsupported ledger schema');
  for (const key of ['items', 'criteria', 'evidence', 'cycles']) requireThat(Array.isArray(ledger[key]), `Missing ${key}`);
  unique(ledger.items.map(x => x.id), 'work item'); unique(ledger.criteria.map(x => x.id), 'criterion'); unique(ledger.evidence.map(x => x.id), 'evidence');
  const items = new Map(ledger.items.map(x => [x.id, x])), criteria = new Map(ledger.criteria.map(x => [x.id, x])), evidence = new Map(ledger.evidence.map(x => [x.id, x]));
  const children = new Map(ledger.items.map(x => [x.id, ledger.items.filter(c => c.parentId === x.id)]));
  for (const e of evidence.values()) {
    requireThat(/^[0-9a-f]{40}$/.test(e.revision), `Evidence ${e.id} needs exact revision`);
    requireThat(['Passed', 'Failed', 'Blocked', 'Not Run', 'Skipped'].includes(e.result), `Invalid evidence result ${e.id}`);
    requireThat(typeof e.command === 'string' && e.command.length > 0, `Evidence ${e.id} missing method/command`);
    requireThat(typeof e.at === 'string' && !Number.isNaN(Date.parse(e.at)), `Evidence ${e.id} missing date`);
    requireThat(/^[0-9a-f]{64}$/.test(e.sha256), `Evidence ${e.id} needs artifact hash`);
    if (root) {
      const artifact = within(root, e.path);
      requireThat(existsSync(artifact) && sha(readFileSync(artifact)) === e.sha256, `Evidence artifact missing or changed: ${e.id}`);
    }
  }
  for (const c of criteria.values()) {
    requireThat(items.has(c.ownerId), `Orphan criterion ${c.id}`);
    requireThat(['Unverified', 'Verified'].includes(c.status), `Invalid criterion status ${c.id}`);
    requireThat(items.get(c.ownerId).acceptanceCriteria.includes(c.id), `Unlisted criterion ${c.id}`);
    requireThat(typeof c.text === 'string' && c.text.length > 0, `Empty acceptance ${c.id}`);
    requireThat(Array.isArray(c.evidenceIds), `Missing criterion evidence ${c.id}`);
    for (const id of c.evidenceIds) requireThat(evidence.has(id), `Unknown evidence ${id}`);
    if (c.status === 'Verified') {
      const revision = items.get(c.ownerId).implementation?.revision;
      requireThat(c.reviewer && c.verifiedAt && c.evidenceIds.length > 0, `Criterion ${c.id} needs reviewer/date/evidence`);
      requireThat(c.evidenceIds.every(id => evidence.get(id).result === 'Passed' && evidence.get(id).revision === revision), `Unpassed or stale criterion evidence ${c.id}`);
    }
  }
  for (const item of items.values()) {
    requireThat(kinds.includes(item.kind) && states.includes(item.state), `Invalid kind/state ${item.id}`);
    const level = kinds.indexOf(item.kind);
    requireThat(level === 0 ? item.parentId === null : items.get(item.parentId)?.kind === kinds[level - 1], `Invalid parent for ${item.id}`);
    requireThat(typeof item.required === 'boolean' && (item.mvpCritical === null || typeof item.mvpCritical === 'boolean'), `Invalid scope classification ${item.id}`);
    requireThat(item.estimate === null || Number.isFinite(item.estimate) && item.estimate > 0, `Invalid estimate ${item.id}`);
    requireThat(Array.isArray(item.dependencies), `Missing dependencies ${item.id}`);
    for (const id of item.dependencies) requireThat(id !== item.id && items.has(id), `Unknown/self dependency ${id} on ${item.id}`);
    unique(item.acceptanceCriteria, `acceptance list ${item.id}`);
    for (const id of item.acceptanceCriteria) requireThat(criteria.get(id)?.ownerId === item.id, `Invalid acceptance ownership ${id}`);
    if (item.kind !== 'Epic') requireThat(item.acceptanceCriteria.length > 0, `No measurable acceptance for ${item.id}`);
    if (item.kind === 'Test') {
      requireThat(Array.isArray(item.verifies) && item.verifies.length > 0 && item.verifies.every(id => criteria.has(id)), `Unmapped test ${item.id}`);
    }
    requireThat(item.history?.length > 0 && item.history[0].from === null && ['Ready','In Progress'].includes(item.history[0].to), `Missing/invalid initial history ${item.id}`);
    for (let i = 0; i < item.history.length; i++) {
      const h = item.history[i];
      requireThat(states.includes(h.to) && h.reason && !Number.isNaN(Date.parse(h.at)), `Invalid history ${item.id}`);
      if (i) requireThat(h.from === item.history[i - 1].to && canTransition(h.from, h.to, item.history.slice(0, i)), `Illegal lifecycle transition ${item.id}: ${h.from} -> ${h.to}`);
    }
    requireThat(item.history.at(-1).to === item.state, `State/history mismatch ${item.id}`);
    if (item.state === 'Blocked') requireThat(item.blocked?.reason && item.blocked?.owner && item.blocked?.resumeState === item.history.at(-1).from, `Blocked item ${item.id} needs reason, owner and resume state`);
    if (['Implemented', 'Testing', 'Review', 'Done'].includes(item.state)) {
      requireThat(/^[0-9a-f]{40}$/.test(item.implementation?.revision) && item.implementation.paths?.length > 0, `Implemented item ${item.id} needs source revision/paths`);
      if (root) for (const path of item.implementation.paths) requireThat(existsSync(within(root, path)), `Missing implementation path ${path}`);
      if (root && verifyGit) {
        try {
          for (const path of item.implementation.paths) execFileSync('git', ['cat-file', '-e', `${item.implementation.revision}:${path}`], {cwd:root,stdio:'pipe'});
        } catch { throw new Error(`Source revision does not contain implementation: ${item.id}`); }
      }
    }
    if (item.kind === 'Test' && item.testResult === 'Passed') {
      requireThat(item.gates?.tests?.evidenceIds?.length > 0 && item.gates.tests.evidenceIds.every(id => evidence.get(id)?.result === 'Passed' && evidence.get(id).revision === item.implementation?.revision), `Unsubstantiated test pass ${item.id}`);
    }
    if (item.state === 'Done') {
      requireThat(children.get(item.id).filter(c => c.required).every(c => c.state === 'Done'), `Required child not Done: ${item.id}`);
      requireThat(item.acceptanceCriteria.every(id => criteria.get(id).status === 'Verified'), `Unverified acceptance: ${item.id}`);
      requireThat(item.knownCriticalRegressions === 0, `Unresolved/unknown critical regressions: ${item.id}`);
      for (const name of gates) {
        const g = item.gates?.[name];
        requireThat(g && (g.status === 'Passed' || g.status === 'Not Applicable' && !cannotWaive.has(name) && g.reason?.length > 20), `Missing/unpassed gate ${name}: ${item.id}`);
        if (g.status === 'Passed') requireThat(g.evidenceIds?.length > 0 && g.evidenceIds.every(id => evidence.get(id)?.result === 'Passed' && evidence.get(id).revision === item.implementation.revision), `Stale or missing gate evidence ${name}: ${item.id}`);
      }
      requireThat(item.integration?.branch === ledger.targetBranch && /^[0-9a-f]{40}$/.test(item.integration.commit), `Missing target integration ${item.id}`);
      if (root && verifyGit) {
        try {
          execFileSync('git', ['merge-base', '--is-ancestor', item.implementation.revision, item.integration.commit], { cwd: root, stdio: 'pipe' });
          execFileSync('git', ['merge-base', '--is-ancestor', item.integration.commit, ledger.targetBranch], { cwd: root, stdio: 'pipe' });
        } catch { throw new Error(`Integration is not in ${ledger.targetBranch}: ${item.id}`); }
      }
      requireThat(item.kind !== 'Test' || item.testResult === 'Passed', `Unpassed Test ${item.id}`);
    }
  }
  // Dependency cycles in the original PRDs are retained. Parent links cannot cycle because levels are strict.
  return ledger;
}

export function validateBaseline(ledger, snapshot, audit) {
  requireThat(ledger.baselineRevision === snapshot.baselineRevision && audit.baselineRevision === snapshot.baselineRevision, 'Baseline revisions disagree');
  const expected = snapshot.definitions.map(d => /^\[([^\]]+)\]/.exec(d.title)[1]);
  const actual = ledger.items.filter(x => x.kind === 'PRD');
  unique(expected, 'source definition');
  const ids = [...Array.from({length:80}, (_,i) => `PRD-${String(i+1).padStart(2,'0')}`), ...Array.from({length:12}, (_,i) => `ARCH-${String(i+1).padStart(2,'0')}`), ...Array.from({length:8}, (_,i) => `UX-${String(i+1).padStart(3,'0')}`)];
  requireThat(ids.length === expected.length && ids.every(id => expected.includes(id)), 'Incomplete source inventory: expected 80 feature, 12 architecture and 8 UX definitions');
  requireThat(expected.length === actual.length && expected.every(id => actual.some(x => x.id === id)), 'Missing/unexpected canonical definition');
  requireThat(audit.audits.length === expected.length && expected.every(id => audit.audits.some(x => x.id === id)), 'Incomplete backlog audit');
  for (const definition of snapshot.definitions) {
    const id = /^\[([^\]]+)\]/.exec(definition.title)[1], item = actual.find(x => x.id === id);
    requireThat(item.source.bodySha256 === sha(definition.body), `Changed source body ${id}`);
    const definitions = definition.body.split(/\n(?=## )/).filter(section => /^## (?:\d+\. )?(?:Functional Requirements|Acceptance Criteria)/i.test(section));
    const sourceIds = [...new Set(definitions.flatMap(section => [...section.matchAll(/^(?:- )?(?:\*\*)?((?:[A-Z][A-Z0-9_-]*-FR-\d{3}|AC-[A-Z0-9-]+-\d{2}|ARCH-\d{2}-AC-\d{3}|UX-\d{3}-AC-\d{2}))(?:\*\*)?/gm)].map(match => match[1])))];
    // Only criteria actually defined in requirement/acceptance sections are obligations.
    const scoped = ledger.criteria.filter(c => c.ownerId === id).map(c => c.sourceId);
    requireThat(scoped.length > 0 && scoped.length === sourceIds.length && sourceIds.every(ref => scoped.includes(ref)), `Lost/unknown source obligations ${id}`);
    const frs = scoped.filter(ref => ref.includes('-FR-'));
    const sliceRefs = ledger.items.filter(x => x.parentId === id).flatMap(x => x.scopeRefs ?? []);
    requireThat(frs.every(ref => sliceRefs.includes(ref)), `Requirement missing from slices ${id}`);
  }
  return true;
}

/** Prior records are immutable; changes in lifecycle are appended, never rewritten. */
export function validateEvolution(previous, current) {
  for (const old of previous.items) {
    const item = current.items.find(x => x.id === old.id);
    requireThat(item && item.parentId === old.parentId && item.kind === old.kind, `Lost/reparented historical item ${old.id}`);
    requireThat(!old.required || item.required, `Required work silently removed ${old.id}`);
    requireThat(JSON.stringify(item.history.slice(0,old.history.length)) === JSON.stringify(old.history), `Rewritten lifecycle history ${old.id}`);
    if (old.source) requireThat(JSON.stringify(old.source) === JSON.stringify(item.source), `Changed frozen source ${old.id}`);
  }
  for (const old of previous.criteria) {
    const c = current.criteria.find(x => x.id === old.id);
    requireThat(c && c.ownerId === old.ownerId && c.text === old.text && c.sourceId === old.sourceId, `Lost/rewritten historical acceptance ${old.id}`);
  }
  for (const old of previous.evidence) requireThat(JSON.stringify(current.evidence.find(x => x.id === old.id)) === JSON.stringify(old), `Lost/rewritten evidence ${old.id}`);
  return true;
}

export function progress(ledger) {
  const byKind = Object.fromEntries(kinds.map(kind => [kind, { total: ledger.items.filter(x => x.kind === kind).length, done: ledger.items.filter(x => x.kind === kind && x.state === 'Done').length }]));
  const specs = ledger.items.filter(x => x.kind === 'PRD');
  const remaining = ledger.criteria.filter(c => c.status !== 'Verified');
  const canonical = remaining.filter(c => specs.some(x => x.id === c.ownerId));
  const active = ledger.items.filter(x => ['In Progress', 'Implemented', 'Testing', 'Review', 'Blocked'].includes(x.state));
  return {
    baselineRevision: ledger.baselineRevision,
    totalPrds: specs.filter(x => x.category !== 'architecture').length,
    prdsCompleted: specs.filter(x => x.category !== 'architecture' && x.state === 'Done').length,
    canonicalFeaturePrds: specs.filter(x => x.category === 'feature').length,
    uxPrds: specs.filter(x => x.category === 'ux').length,
    architectureTickets: specs.filter(x => x.category === 'architecture').length,
    totalWorkItems: ledger.items.length, workItemsCompleted: ledger.items.filter(x => x.state === 'Done').length, byKind,
    implementedButUnvalidated: active.filter(x => ['Implemented', 'Testing', 'Review'].includes(x.state) && (x.acceptanceCriteria.some(id => remaining.some(c => c.id === id)) || Object.values(x.gates ?? {}).some(g => !['Passed', 'Not Applicable'].includes(g.status)) || !x.integration)).map(x => x.id),
    blocked: active.filter(x => x.state === 'Blocked').map(x => ({ id: x.id, reason: x.blocked.reason, owner: x.blocked.owner })),
    remainingAcceptanceCriteria: { canonicalNumbered: canonical.filter(c => !c.sourceId.includes('-FR-')).length, canonicalFunctionalRequirements: canonical.filter(c => c.sourceId.includes('-FR-')).length, childCriteria: remaining.length - canonical.length },
    mvpCriticalOutstanding: ledger.items.filter(x => x.mvpCritical === true && x.state !== 'Done').map(x => x.id),
    mvpClassificationUnknown: ledger.items.filter(x => x.mvpCritical === null).length,
    weightedProgress: null, weightedProgressReason: 'No frozen weighted baseline with complete task estimates; counts are not percentages.',
    recentCompletedIncrements: ledger.items.filter(x => x.kind === 'Vertical Slice' && x.state === 'Done').map(x => ({ id: x.id, title: x.title, completedAt: x.history.at(-1).at, integration: x.integration })).sort((a,b) => b.completedAt.localeCompare(a.completedAt)).slice(0,10),
    inProgress: active.filter(x => x.state === 'In Progress').map(x => x.id), lastCycle: ledger.cycles.at(-1) ?? null,
    historicalEvidence: ledger.historicalEvidence ?? []
  };
}

export function transition(ledger, id, state, reason, at = new Date().toISOString(), owner = 'delivery-owner') {
  const copy = structuredClone(ledger), item = copy.items.find(x => x.id === id);
  requireThat(item && states.includes(state) && reason?.trim(), 'Transition needs existing item, valid state and recorded reason');
  requireThat(canTransition(item.state, state, item.history), `Illegal lifecycle transition ${item.state} -> ${state}`);
  const from = item.state;
  item.history.push({ at, from, to: state, reason }); item.state = state;
  if (state === 'Blocked') item.blocked = { reason, owner, resumeState: from };
  else delete item.blocked;
  return copy;
}

export function markdownReport(report) {
  const cycle = report.lastCycle;
  const lines = ['# Incremental delivery progress', '', `Baseline: \`${report.baselineRevision}\`. Counts describe the ledger, not product completion percentages.`, '',
    `PRDs: ${report.prdsCompleted}/${report.totalPrds} verified Done (${report.canonicalFeaturePrds} feature + ${report.uxPrds} UX); ${report.architectureTickets} architecture tickets tracked separately.`,
    `Work items: ${report.workItemsCompleted}/${report.totalWorkItems} Done. Remaining canonical numbered acceptance criteria: ${report.remainingAcceptanceCriteria.canonicalNumbered}; functional obligations: ${report.remainingAcceptanceCriteria.canonicalFunctionalRequirements}; child criteria: ${report.remainingAcceptanceCriteria.childCriteria}. Unverified includes unknown and partially tested work; it does not assert code is absent.`, '',
    '| Kind | Total | Done |', '| --- | ---: | ---: |', ...Object.entries(report.byKind).map(([kind, counts]) => `| ${kind} | ${counts.total} | ${counts.done} |`), '',
    `Implemented but unvalidated: ${report.implementedButUnvalidated.length}. Blocked: ${report.blocked.length}. Known MVP-critical outstanding: ${report.mvpCriticalOutstanding.length}; unclassified records: ${report.mvpClassificationUnknown}. Weighted progress: not measured.`, '',
    '## 1. Work completed', '', ...(cycle?.completed ?? ['No new increment verified Done. Integrated historical work remains recorded separately.']), '',
    '## 2. Implemented, awaiting validation', '', report.implementedButUnvalidated.join(', ') || 'None recorded in implemented lifecycle states; legacy implementation evidence still requires reconciliation.', '',
    '## 3. Current work in progress', '', ...(cycle?.inProgress ?? [report.inProgress.join(', ') || 'None recorded.']), '',
    '## 4. Blockers and dependencies', '', ...report.blocked.map(x => `${x.id}: ${x.reason} (owner: ${x.owner})`), ...(cycle?.blockers ?? []), '',
    '## 5. Next highest-priority deliverable', '', cycle?.next ?? 'Reconcile current operational MVP acceptance and select the smallest demonstrated gap.', '',
    '## 6. PRD progress changes', '', ...(cycle?.prdChanges ?? ['No parent PRD closed by this audit.']), '',
    '## 7. Completion evidence', '', ...(cycle?.evidence ?? ['See baseline-audit.json for candidate references; these are not current passing results.']), '',
    '## Recent verified completed increments', '', ...report.recentCompletedIncrements.map(x => `${x.id}: ${x.title} (${x.completedAt})`), ...(report.recentCompletedIncrements.length ? [] : ['None recorded under the new acceptance gates.']), '',
    '## Preserved historical evidence', '', ...report.historicalEvidence.map(x => `${x.id}: ${x.recordedResults} Source: ${x.source}. ${x.acceptance}`), ''];
  return lines.join('\n');
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
    const path = resolve(root, 'docs/delivery/progress.json');
    const [command = 'report', ...args] = process.argv.slice(2);
    const raw = readFileSync(path, 'utf8'); const ledger = JSON.parse(raw);
    validateLedger(ledger, { root });
    validateBaseline(ledger, JSON.parse(readFileSync(resolve(root,'docs/delivery/requirements-snapshot.json'),'utf8')), JSON.parse(readFileSync(resolve(root,'docs/delivery/baseline-audit.json'),'utf8')));
    if (args.includes('--base-ref')) {
      const ref = args[args.indexOf('--base-ref') + 1];
      requireThat(ref && !ref.startsWith('-'), 'Missing base ref');
      // Missing base is allowed only for the first ledger introduction. Invalid refs still fail.
      execFileSync('git', ['rev-parse', '--verify', ref], {cwd:root,stdio:'pipe'});
      const files = execFileSync('git',['ls-tree','--name-only',ref,'--','docs/delivery/progress.json'],{cwd:root,encoding:'utf8'});
      if (files.trim()) validateEvolution(JSON.parse(execFileSync('git',['show',`${ref}:docs/delivery/progress.json`],{cwd:root,encoding:'utf8'})),ledger);
    }
    if (command === 'validate') console.log(`Valid: ${ledger.items.length} work items; ${ledger.criteria.length} criteria. No unsupported Done claims.`);
    else if (command === 'report') console.log(args.includes('--json') ? JSON.stringify(progress(ledger), null, 2) : markdownReport(progress(ledger)));
    else if (command === 'transition') {
      const [id, state, reason] = args;
      const changed = transition(ledger, id, state, reason); validateLedger(changed, { root });
      const lock = path + '.lock'; writeFileSync(lock, '', { flag: 'wx' });
      try {
        requireThat(readFileSync(path,'utf8') === raw, 'Ledger changed during transition; reload and retry');
        writeFileSync(path + '.tmp', JSON.stringify(changed, null, 2) + '\n'); renameSync(path + '.tmp', path);
      } finally { unlinkSync(lock); }
      console.log(`${id}: ${state}`);
    } else throw new Error('Usage: node scripts/delivery-progress.mjs validate|report [--json]|transition ID "STATE" "REASON"');
  } catch (error) { console.error(error.message); process.exitCode = 1; }
}
