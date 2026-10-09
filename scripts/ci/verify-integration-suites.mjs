import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { resolve } from 'node:path';
import { parseDocument } from 'yaml';
import { shardCount } from './verify-browser-shards.mjs';

export const suites = ['commands', 'browser-foundation', 'browser-notifications', 'browser-full'];
const evidenceScopes = JSON.parse(readFileSync(new URL('./evidence-artifacts.json', import.meta.url), 'utf8'));

function readAttachmentSources() {
  return {
    script: readFileSync(new URL('./test-attachment-image-pipeline.sh', import.meta.url), 'utf8'),
    background: readFileSync(new URL('../../playwright.attachment.config.ts', import.meta.url), 'utf8'),
    lifecycle: readFileSync(new URL('../../playwright.attachment-lifecycle.config.ts', import.meta.url), 'utf8'),
  };
}

function verifyAttachmentPipeline(sources) {
  const script = sources.script.replace(/\r\n/g, '\n');
  assert.match(script, /^#!\/usr\/bin\/env bash\nset -Eeuo pipefail\n/, 'Attachment pipeline must fail on failed commands');
  assert.doesNotMatch(script, /^\s*set\s+\+/m, 'Attachment pipeline may not disable strict shell options');
  const phases = [
    { key: 'background', config: 'playwright.attachment.config.ts', fixture: 'browser-fixture', file: 'board-background-pipeline.case.ts', output: 'attachment-pipeline' },
    { key: 'lifecycle', config: 'playwright.attachment-lifecycle.config.ts', fixture: 'archive-browser-fixture', file: 'attachment-source-archive-pipeline.case.ts', output: 'attachment-source-archive-pipeline' },
  ];
  const calls = script.split('\n').filter(line => /\bnpx\s+playwright\s+test\b/.test(line) && !line.trimStart().startsWith('#'));
  assert.deepEqual(calls, phases.map(phase => `  npx playwright test --config ${phase.config}`),
    'Both intact attachment phases must run once in order, without filters or ignored failures');
  for (const phase of phases) {
    const block = [
      `STRATAAI_ATTACHMENT_BROWSER_FIXTURE="$scratch/${phase.fixture}" STRATAAI_E2E_RATE_PACING=1 STRATAAI_E2E_RELEASE_HEADERS=1 \\`,
      `  npx playwright test --config ${phase.config}`,
    ].join('\n');
    assert.ok(script.includes(block), `Attachment ${phase.key} phase must retain its owned fixture, pacing and release headers`);
    const expected = [
      "import { defineConfig } from '@playwright/test';",
      "import release from './playwright.config';",
      `export default defineConfig({ ...release, testMatch: '${phase.file}', outputDir: 'test-results/${phase.output}' });`,
    ].join('\n');
    // Formatting changes are harmless. New overrides require explicit review:
    // grep/retry/timeout changes must not silently narrow the inherited policy.
    assert.equal(sources[phase.key].replace(/\s/g, ''), expected.replace(/\s/g, ''),
      `Attachment ${phase.key} configuration must select its intact case file and inherit release policy`);
  }
}

export function readWorkflow(source) {
  const document = parseDocument(source, { uniqueKeys: true });
  assert.equal(document.errors.length, 0, 'Workflow must be valid YAML without duplicate keys');
  return document.toJS();
}

// Deliberately accept only the small condition language used by this matrix.
// New conditions require review, rather than silently losing mandatory checks.
function owners(step) {
  if (!step.if || ['always()', 'failure()'].includes(step.if)) return suites;
  const expression = step.if.replace(/^(always\(\)|failure\(\)) && /, '');
  assert.match(expression, /^\(matrix\.suite == '[a-z-]+'(?: \|\| matrix\.suite == '[a-z-]+')*\)$/,
    `Unsupported integration condition: ${step.name ?? step.uses}`);
  const selected = [...expression.matchAll(/matrix\.suite == '([a-z-]+)'/g)].map(match => match[1]);
  assert.equal(new Set(selected).size, selected.length, 'Duplicate suite condition');
  assert.ok(selected.every(suite => suites.includes(suite)), 'Unknown suite condition');
  return selected;
}

export function verifyIntegrationSuites(workflow, registry, attachmentSources = readAttachmentSources()) {
  const jobs = workflow.jobs;
  const job = jobs['container-integration'];
  const executions = [
    ...suites.filter(suite => suite !== 'browser-full').map(suite => ({ suite, shard: 1, totalShards: 1 })),
    ...Array.from({ length: shardCount }, (_, index) => ({ suite: 'browser-full', shard: index + 1, totalShards: shardCount })),
  ];
  assert.deepEqual(job.strategy.matrix, { include: executions }, 'All integration groups and complete browser shards must run');
  assert.equal(job.name, 'container-integration (${{ matrix.suite }}, ${{ matrix.shard }}/${{ matrix.totalShards }})');
  const fullBrowser = job.steps.find(step => step.name === 'Authenticated browser E2E against exact release images');
  assert.equal(fullBrowser.run, 'npx playwright test --shard=${{ matrix.shard }}/${{ matrix.totalShards }}');
  assert.deepEqual(fullBrowser.env, { STRATAAI_E2E_RELEASE_HEADERS: '1', STRATAAI_E2E_RATE_PACING: '1' });
  const attachment = job.steps.find(step => step.name === 'Attachment upload and isolated Worker image publication through explicit private test providers');
  assert.deepEqual(owners(attachment), ['browser-foundation']);
  assert.equal(attachment.run, 'bash scripts/ci/test-attachment-image-pipeline.sh', 'Enabled attachment pipeline must be a mandatory, unfiltered invocation');
  assert.equal(attachment['continue-on-error'] ?? false, false);
  verifyAttachmentPipeline(attachmentSources);
  const strictNotifications = job.steps.find(step => step.name === 'Strict verified-account assignment mention and reminder native delivery');
  assert.deepEqual(owners(strictNotifications), ['browser-notifications']);
  assert.deepEqual(strictNotifications.env, { STRATAAI_E2E_VERIFY_NOTIFICATION_ACCOUNTS: '1', STRATAAI_E2E_RATE_PACING: '1' });
  const strictCommand = "npx playwright test tests/browser/card-assignment-notifications.spec.ts tests/browser/comment-mentions.spec.ts tests/browser/comment-mass-mentions.spec.ts tests/browser/card-reminders.spec.ts tests/browser/notification-center.spec.ts --grep 'native assignment|native selected teammate|native confirmed groups|actual Worker due reminder|recipient inbox recovers'";
  assert.equal(strictNotifications.run, [
    'set -euo pipefail',
    ...['api', 'worker'].map(host => `test "$(docker compose -f compose.release.yml exec -T ${host} printenv STRATAAI_AUTH_REQUIRE_VERIFIED_EMAIL)" = true`),
    strictCommand, '',
  ].join('\n'), 'Strict producer and consumer coverage requires verified accounts in both hosts');
  const strictWatches = job.steps.find(step => step.name === 'Strict verified-account watch producers through native private inboxes');
  assert.deepEqual(owners(strictWatches), ['browser-notifications']);
  assert.deepEqual(strictWatches.env, { STRATAAI_E2E_VERIFY_WATCH_ACCOUNTS: '1', STRATAAI_E2E_VERIFY_NOTIFICATION_ACCOUNTS: '1', STRATAAI_E2E_RATE_PACING: '1' });
  assert.equal(strictWatches.run, [
    'set -euo pipefail',
    ...['api', 'worker'].map(host => `test "$(docker compose -f compose.release.yml exec -T ${host} printenv STRATAAI_AUTH_REQUIRE_VERIFIED_EMAIL)" = true`),
    'npx playwright test tests/browser/watch-cross-board-notifications.spec.ts tests/browser/watch-activity-matrix.spec.ts tests/browser/watch-subscriptions.spec.ts', '',
  ].join('\n'), 'Strict watch producers and personal controls must retain verified admission in both hosts');
  const strictDeparture = job.steps.find(step => step.name === 'Strict verified-account Organization departure and account continuity');
  assert.deepEqual(owners(strictDeparture), ['browser-notifications']);
  assert.deepEqual(strictDeparture.env, { STRATAAI_E2E_VERIFY_NOTIFICATION_ACCOUNTS: '1', STRATAAI_E2E_RATE_PACING: '1' });
  assert.equal(strictDeparture.run, [
    'set -euo pipefail',
    ...['api', 'worker'].map(host => `test "$(docker compose -f compose.release.yml exec -T ${host} printenv STRATAAI_AUTH_REQUIRE_VERIFIED_EMAIL)" = true`),
    'npx playwright test tests/browser/organization-departure.spec.ts tests/browser/organization-departure-account.spec.ts', '',
  ].join('\n'), 'Complete native departure and account replacement require verified admission in both hosts');
  const strictBoards = job.steps.find(step => step.name === 'Strict verified-account Board management and personal preferences');
  assert.deepEqual(owners(strictBoards), ['browser-notifications']);
  assert.deepEqual(strictBoards.env, { STRATAAI_E2E_VERIFY_NOTIFICATION_ACCOUNTS: '1', STRATAAI_E2E_RATE_PACING: '1' });
  assert.equal(strictBoards.run, [
    'set -euo pipefail',
    ...['api', 'worker'].map(host => `test "$(docker compose -f compose.release.yml exec -T ${host} printenv STRATAAI_AUTH_REQUIRE_VERIFIED_EMAIL)" = true`),
    'npx playwright test tests/browser/board.spec.ts tests/browser/board-metadata.spec.ts tests/browser/board-copy.spec.ts tests/browser/board-lifecycle.spec.ts tests/browser/board-archive-account.spec.ts tests/browser/board-star.spec.ts tests/browser/activity-history.spec.ts tests/browser/board-background-client.spec.ts tests/browser/organization-board-live.spec.ts tests/browser/board-visibility.spec.ts tests/browser/board-members.spec.ts tests/browser/board-admin-live.spec.ts tests/browser/card-labels.spec.ts tests/browser/label-filter-live.spec.ts', '',
  ].join('\n'), 'Complete native Board management requires verified admission in both hosts');
  const browserCoverage = jobs['web-quality'].steps.find(step => step.name === 'Verify complete browser shard coverage');
  assert.equal(browserCoverage?.run, 'node --test tests/browser-shards.test.mjs\nnode scripts/ci/verify-browser-shards.mjs\n');
  assert.equal(browserCoverage.if, undefined);
  assert.equal(browserCoverage['continue-on-error'] ?? false, false);

  assert.equal(job.strategy['fail-fast'], false, 'One failing group must not cancel diagnostic work in another');
  assert.equal(job.strategy['max-parallel'], 4);
  assert.equal(job['continue-on-error'] ?? false, false, 'Integration failures must block release');
  assert.deepEqual(job.needs, ['metadata', 'build-images-once']);
  assert.equal(job.env.STRATAAI_BUILD_REVISION, '${{ needs.metadata.outputs.revision }}');
  assert.equal(job.env.STRATAAI_BUILD_VERSION, '${{ needs.metadata.outputs.version }}');
  for (const name of ['web-quality', 'dotnet-quality', 'postgres-integration']) {
    assert.ok(jobs[name].needs.includes('metadata'), 'Source checks require verified initial metadata');
  }
  for (const [jobName, summaryName, uploadName] of [
    ['web-quality', 'Summarize web source test results', 'Retain web source test results'],
    ['dotnet-quality', 'Summarize .NET source test results', 'Retain .NET source test results'],
  ]) {
    const steps = jobs[jobName].steps;
    const summary = steps.find(step => step.name === summaryName);
    const upload = steps.find(step => step.name === uploadName);
    assert.equal(summary?.if, 'always()', 'Retain result summaries after test failures');
    assert.equal(summary['continue-on-error'] ?? false, false);
    assert.ok(summary.run.includes('--metadata source-inputs/build-metadata.json'));
    assert.ok(summary.run.includes('--output artifacts/source-tests/'));
    assert.equal(upload?.if, 'always()');
    assert.equal(upload.with.path, 'artifacts/source-tests/', 'Never publish raw assertion reports');
    assert.ok(steps.find(step => step.name === 'Download source evidence identity')?.with.name === 'strataai-build-metadata-${{ github.sha }}');
  }
  const webTests = jobs['web-quality'].steps.find(step => step.name === 'Unit and component tests').run;
  assert.ok(webTests.includes('--reporter=junit'));
  assert.ok(webTests.includes('--outputFile.junit="$RUNNER_TEMP/source-tests-raw/web.xml"'));
  for (const [name, file] of [['Domain tests', 'domain.trx'], ['API host tests', 'api.trx']]) {
    const run = jobs['dotnet-quality'].steps.find(step => step.name === name).run;
    assert.ok(run.includes(`--report-trx --report-trx-filename ${file}`));
    assert.ok(!run.includes('--filter'), 'CI source suites must remain unfiltered');
  }
  assert.ok(jobs['source-quality-gate'].needs.includes('metadata'));
  const sourceGate = jobs['source-quality-gate'].steps[0];
  assert.equal(sourceGate.env.METADATA_RESULT, '${{ needs.metadata.result }}');
  assert.match(sourceGate.run, /for result in .*"\$METADATA_RESULT"/);
  const build = jobs['build-images-once'];
  assert.deepEqual(build.needs, ['metadata', 'source-quality-gate']);
  assert.equal(build.env.STRATAAI_BUILD_REVISION, '${{ needs.metadata.outputs.revision }}');
  assert.equal(build.env.STRATAAI_BUILD_VERSION, '${{ needs.metadata.outputs.version }}');
  assert.equal(build.env.STRATAAI_BUILD_SOURCE, 'https://github.com/${{ needs.metadata.outputs.repository }}');
  assert.equal(build.env.STRATAAI_BUILD_CREATED, '${{ needs.metadata.outputs.created_at }}');
  for (const host of ['web', 'api', 'worker']) {
    const step = build.steps.find(value => value.name === `Build ${host === 'api' ? 'API' : host === 'web' ? 'web' : 'Worker'} image`);
    assert.ok(step.run.includes('--build-arg STRATAAI_BUILD_REVISION="$STRATAAI_BUILD_REVISION"'));
    assert.ok(step.run.includes('--build-arg STRATAAI_BUILD_VERSION="$STRATAAI_BUILD_VERSION"'));
    assert.ok(step.run.includes('--build-arg STRATAAI_BUILD_SOURCE="$STRATAAI_BUILD_SOURCE"'));
    assert.ok(step.run.includes('--build-arg STRATAAI_BUILD_CREATED="$STRATAAI_BUILD_CREATED"'));
  }
  const archive = build.steps.find(step => step.name === 'Export exact built images');
  const realScanner = build.steps.find(step => step.name === 'Verify real scanner transport in the exact Worker image');
  assert.equal(realScanner?.run, 'bash scripts/ci/test-real-attachment-scanner.sh');
  assert.equal(realScanner.if, undefined, 'Real scanner image verification is mandatory');
  assert.equal(realScanner['continue-on-error'] ?? false, false);
  assert.equal(realScanner.env.STRATAAI_WORKER_IMAGE, 'strataai-worker:${{ github.sha }}');
  assert.ok(build.steps.indexOf(realScanner) > build.steps.findIndex(step => step.name === 'Build Worker image'));
  assert.ok(build.steps.indexOf(realScanner) < build.steps.indexOf(archive), 'Real scanner verification precedes export');
  const provenance = build.steps.find(step => step.name === 'Verify exact image provenance before export');
  assert.equal(provenance?.shell, 'bash');
  assert.equal(provenance.if, undefined, 'Image provenance verification must not be conditional');
  assert.ok(provenance.run.includes('set -euo pipefail'));
  assert.match(provenance.run, /docker image inspect\s+"strataai-web:\$\{GITHUB_SHA\}"\s+"strataai-api:\$\{GITHUB_SHA\}"\s+"strataai-worker:\$\{GITHUB_SHA\}"\s+> build-inputs\/image-inspection.json/);
  assert.ok(provenance.run.includes('python3 scripts/ci/verify-image-labels.py --metadata build-inputs/build-metadata.json --images build-inputs/image-inspection.json --output build-inputs/image-provenance.json'));
  assert.ok(!provenance.run.includes('||'), 'Image provenance refusal cannot be swallowed');
  assert.equal(provenance['continue-on-error'] ?? false, false);
  assert.ok(build.steps.indexOf(provenance) > build.steps.findIndex(step => step.name === 'Build Worker image'));
  assert.ok(build.steps.indexOf(provenance) < build.steps.indexOf(archive));
  const sourceProvenance = jobs['web-quality'].steps.find(step => step.name === 'Verify source test report completeness and privacy');
  assert.equal(sourceProvenance.if, undefined);
  assert.equal(sourceProvenance['continue-on-error'] ?? false, false);
  assert.ok(sourceProvenance.run.includes("python3 -m unittest discover -s tests -p 'image_labels_test.py'"));
  assert.match(archive.run, /cp build-inputs\/build-metadata.json image-artifacts\/build-metadata.json/);
  assert.match(archive.run, /cp build-inputs\/image-provenance.json image-artifacts\/image-provenance.json/);
  assert.match(archive.run, /sha256sum .*build-metadata.json image-provenance.json > SHA256SUMS/);
  const bundle = jobs['release-bundle'].steps.find(step => step.name === 'Assemble release bundle');
  assert.match(bundle.run, /cp image-artifacts\/build-metadata.json bundle\/build-metadata.json/);
  assert.ok(!bundle.run.includes('cat > bundle/build-metadata.json'), 'Release must preserve the original metadata document');
  const releaseJob = jobs['release-bundle'];
  assert.deepEqual(releaseJob.needs, ['metadata', 'required-ci']);
  assert.equal(releaseJob.env.STRATAAI_BUILD_VERSION, '${{ needs.metadata.outputs.version }}');
  const releaseSteps = releaseJob.steps;
  const releaseSecurity = releaseSteps.find(step => step.name === 'Download exact tested security evidence');
  assert.equal(releaseSecurity?.with.name, 'security-evidence-${{ github.sha }}');
  assert.equal(releaseSecurity.with.path, 'security-artifacts');
  const inputVerify = releaseSteps.find(step => step.name === 'Verify tested release inputs');
  assert.equal(inputVerify?.run, 'python3 scripts/ci/verify-release-artifacts.py inputs --images image-artifacts --security security-artifacts');
  const bundleVerify = releaseSteps.find(step => step.name === 'Verify release bundle completeness and checksums');
  assert.equal(bundleVerify?.run, 'python3 scripts/ci/verify-release-artifacts.py bundle --path bundle --images image-artifacts --security security-artifacts');
  assert.ok(bundle.run.includes('cp -R security-artifacts bundle/security'));
  assert.ok(bundle.run.includes('cp image-artifacts/SHA256SUMS image-artifacts/build-metadata.json image-artifacts/image-provenance.json bundle/images/'));
  assert.ok(bundle.run.includes('for component in strataai-web strataai-api strataai-worker metrics-collector; do'));
  assert.ok(bundle.run.includes('cp "security-artifacts/${component}.cdx.json" "bundle/sbom/${component}.cdx.json"'));
  assert.ok(bundle.run.includes('find . -type f ! -path ./SHA256SUMS'), 'Root checksums must cover nested security manifest');
  const releaseUpload = releaseSteps.find(step => step.name === 'Upload runnable release bundle');
  const startup = releaseSteps.find(step => step.name === 'Verify assembled bundle startup without source or SDKs');
  const startupSource = jobs['web-quality'].steps.find(step => step.name === 'Verify mandatory immutable integration coverage');
  assert.ok(startupSource.run.includes('node --test tests/release-bundle-startup.test.mjs'));
  assert.equal(startupSource.if, undefined);
  assert.equal(startupSource['continue-on-error'] ?? false, false);
  const afterStartup = releaseSteps.find(step => step.name === 'Verify bundle unchanged after startup smoke');
  assert.equal(startup?.run, 'bash scripts/ci/test-release-bundle-startup.sh bundle');
  assert.equal(startup['timeout-minutes'], 12);
  assert.equal(afterStartup?.run, bundleVerify.run);
  assert.equal(releaseUpload.with.path, 'bundle/');
  assert.equal(releaseUpload.with['include-hidden-files'], true, 'Retain the required environment example');
  assert.equal(releaseUpload.with['if-no-files-found'], 'error');
  for (const step of [inputVerify, bundle, bundleVerify, startup, afterStartup, releaseUpload]) {
    assert.equal(step.if, undefined, 'Release checks may not be conditional');
    assert.equal(step['continue-on-error'] ?? false, false);
  }
  assert.ok(releaseSteps.indexOf(releaseSecurity) < releaseSteps.indexOf(inputVerify));
  assert.ok(releaseSteps.indexOf(inputVerify) < releaseSteps.indexOf(bundle));
  assert.ok(releaseSteps.indexOf(bundle) < releaseSteps.indexOf(bundleVerify));
  assert.ok(releaseSteps.indexOf(bundleVerify) < releaseSteps.indexOf(releaseUpload));
  assert.ok(releaseSteps.indexOf(bundleVerify) < releaseSteps.indexOf(startup));
  assert.ok(releaseSteps.indexOf(startup) < releaseSteps.indexOf(releaseUpload), 'Smoke the assembled payload before retaining it');
  assert.ok(releaseSteps.indexOf(startup) < releaseSteps.indexOf(afterStartup));
  assert.ok(releaseSteps.indexOf(afterStartup) < releaseSteps.indexOf(releaseUpload), 'Refuse changed payloads or leftover private files before upload');
  assert.ok(releaseSteps.every(step => !/docker (?:build|buildx build)/.test(step.run ?? '')), 'Bundle cannot rebuild application images');

  const security = jobs.security;
  for (const consumer of [jobs['container-integration'], security]) {
    const loaded = consumer.steps.find(step => step.name === 'Load exact built images');
    assert.equal(loaded?.shell, 'bash');
    assert.equal(loaded.if, undefined, 'Every image consumer must verify loaded provenance');
    assert.equal(loaded['continue-on-error'] ?? false, false);
    const lines = loaded.run.replace(/\r\n/g, '\n').trim().split('\n');
    assert.deepEqual(lines, [
      'set -euo pipefail',
      ...['web', 'api', 'worker'].map(host => `gunzip -c image-artifacts/strataai-${host}.tar.gz | docker load`),
      'docker image inspect "strataai-web:${GITHUB_SHA}" "strataai-api:${GITHUB_SHA}" "strataai-worker:${GITHUB_SHA}" > image-artifacts/loaded-image-inspection.json',
      'python3 scripts/ci/verify-image-labels.py --metadata image-artifacts/build-metadata.json --images image-artifacts/loaded-image-inspection.json --expected image-artifacts/image-provenance.json',
    ], 'Load every retained archive and verify complete provenance before executing or scanning images');
  }
  assert.deepEqual(security.needs, ['metadata', 'build-images-once']);
  const securitySteps = security.steps;
  const securityDownload = securitySteps.findIndex(step => step.name === 'Download exact built images');
  const securityVerify = securitySteps.findIndex(step => step.name === 'Verify security input integrity and retain build identity');
  assert.ok(securityDownload >= 0 && securityVerify > securityDownload);
  assert.equal(securitySteps[securityDownload].with.name, 'strataai-images-${{ github.sha }}');
  assert.equal(securitySteps[securityDownload].with.path, 'image-artifacts');
  const securityInput = securitySteps[securityVerify];
  assert.equal(securityInput['continue-on-error'] ?? false, false);
  assert.equal(securityInput.if, undefined, 'Security input verification is mandatory');
  assert.equal(securityInput.env.STRATAAI_BUILD_VERSION, '${{ needs.metadata.outputs.version }}');
  assert.ok(securityInput.run.includes('set -euo pipefail'));
  assert.ok(securityInput.run.includes('(cd image-artifacts && sha256sum --check SHA256SUMS)'));
  assert.ok(securityInput.run.includes('.repository == $repository and .commitSha == $revision and .imageTag == $revision and .workflowRunId == $run and .version == $version'));
  assert.ok(securityInput.run.includes('cp image-artifacts/build-metadata.json security-artifacts/build-metadata.json'));
  assert.ok(securityInput.run.includes('cp image-artifacts/image-provenance.json security-artifacts/image-provenance.json'));
  for (const name of ['Install locked web dependencies', 'Load exact built images', 'Generate SBOMs', 'Secret scan', 'Block fixed Critical container vulnerabilities']) {
    assert.ok(securitySteps.findIndex(step => step.name === name) > securityVerify, 'Verify security inputs before audits or image loading');
  }
  for (const name of ['Generate SBOMs', 'Block fixed Critical container vulnerabilities']) {
    assert.ok(securitySteps.findIndex(step => step.name === 'Load exact built images') < securitySteps.findIndex(step => step.name === name),
      'Verify loaded image provenance before image scans or SBOM generation');
  }
  const securityUpload = securitySteps.find(step => step.name === 'Upload SBOMs and security evidence');
  assert.equal(securityUpload.if, 'always()');
  assert.equal(securityUpload.with.path, 'security-artifacts/');
  const securityChecksums = securitySteps.find(step => step.name === 'Bind retained security evidence checksums');
  assert.equal(securityChecksums?.if, 'always()');
  assert.equal(securityChecksums['continue-on-error'] ?? false, false);
  assert.ok(securityChecksums.run.includes('sha256sum > SHA256SUMS'));
  assert.ok(securitySteps.indexOf(securityChecksums) < securitySteps.indexOf(securityUpload));

  for (const step of securitySteps) {
    assert.ok(!/docker (?:build|buildx build)|compose[^\n]*\bbuild\b/.test(step.run ?? ''), 'Security may not rebuild release images');
  }
  const named = job.steps.filter(step => step.name);
  const download = named.find(step => step.name === 'Download exact built images');
  assert.equal(download?.with?.name, 'strataai-images-${{ github.sha }}', 'Use the current exact-SHA image artifact');
  assert.equal(download.with.path, 'image-artifacts');
  assert.equal(named.find(step => step.name === 'Verify image archive checksums')?.run,
    'cd image-artifacts && sha256sum --check SHA256SUMS');
  const load = named.find(step => step.name === 'Load exact built images')?.run;
  for (const image of ['web', 'api', 'worker']) {
    assert.ok(load?.includes(`gunzip -c image-artifacts/strataai-${image}.tar.gz | docker load`), 'Load all three retained image archives');
  }
  assert.equal(new Set(named.map(step => step.name)).size, named.length, 'Duplicate mandatory step');
  assert.deepEqual(named.map(step => step.name).sort(), Object.keys(registry).sort(), 'Missing or unregistered integration step');
  for (const step of named) {
    assert.deepEqual([...owners(step)].sort(), [...registry[step.name]].sort(), `Wrong ownership: ${step.name}`);
    assert.equal(step['continue-on-error'] ?? false, false, `Mandatory step cannot ignore failures: ${step.name}`);
  }
  const evidenceUploads = named.filter(step => step.uses?.startsWith('actions/upload-artifact@'));
  assert.deepEqual(evidenceUploads.map(step => step.name).sort(), Object.keys(evidenceScopes).sort(), 'Every integration artifact must have an explicit payload scope');
  for (const upload of evidenceUploads) {
    const scope = evidenceScopes[upload.name];
    const prepare = named.find(step => step.name === `Prepare ${upload.name}`);
    assert.ok(prepare, 'Every artifact needs provenance staging');
    assert.equal(prepare.if, upload.if, 'Failure/always evidence must use matching staging conditions');
    assert.equal(named.indexOf(prepare) + 1, named.indexOf(upload), 'Stage immediately before upload');
    const entries = scope.entries.map(([source, destination]) => ` --entry "${source}" "${destination}"`).join('');
    assert.equal(prepare.run, `python3 scripts/ci/prepare-evidence-artifact.py --metadata image-artifacts/build-metadata.json --output ${scope.output}${entries}`);
    assert.equal(upload.with.path, scope.output, 'Upload only staged evidence with canonical metadata');
    assert.equal(upload.with.name, scope.artifact, 'Preserve artifact identity');
  }
  const artifactNames = new Set();
  for (const { suite, shard } of executions) {
    const steps = job.steps.filter(step => owners(step).includes(suite));
    const index = name => steps.findIndex(step => step.name === name);
    const precedes = (first, second) => assert.ok(index(first) >= 0 && index(second) > index(first),
      `${suite}: ${first} must precede ${second}`);
    precedes('Download exact built images', 'Verify image archive checksums');
    precedes('Verify image archive checksums', 'Load exact built images');
    precedes('Load exact built images', 'Start release topology');
    precedes('Provision least-privilege runtime database roles', 'Verify production account defaults and prepare isolated auth fixture');
    if (suite.startsWith('browser-')) {
      precedes('Verify production account defaults and prepare isolated auth fixture', 'Install browser test dependencies');
    }
    if (suite === 'browser-notifications') {
      precedes('Prepare isolated identity mail transport and ephemeral signing keys', 'Identity mail against exact API/Worker images and restricted database role');
      precedes('Identity mail against exact API/Worker images and restricted database role', 'Strict verified-account watch producers through native private inboxes');
      precedes('Desktop and mobile accessible keyboard verification/recovery through Worker delivery', 'Strict verified-account Organization departure and account continuity');
      precedes('Strict verified-account Organization departure and account continuity', 'Strict verified-account watch producers through native private inboxes');
      precedes('Strict verified-account Organization departure and account continuity', 'Strict verified-account Board management and personal preferences');
      precedes('Strict verified-account Board management and personal preferences', 'Strict verified-account watch producers through native private inboxes');
    }
    if (suite === 'browser-full') {
      for (const producer of [
        'Invitation-backed closed registration and atomic expiry against exact release API',
        'Verified-email invitation discovery and retry-safe acceptance',
        'Board administrator demotion continuity and atomic recovery',
      ]) {
        precedes('Verify production account defaults and prepare isolated auth fixture', producer);
        precedes(producer, 'Prepare isolated identity mail transport and ephemeral signing keys');
        precedes(producer, 'Authenticated browser E2E against exact release images');
      }
      precedes('Prepare isolated identity mail transport and ephemeral signing keys', 'Restore isolated unverified-email account fixture for profile browser tests');
      precedes('Restore isolated unverified-email account fixture for profile browser tests', 'Enable automatic Organization metadata and invitation authority routing for native browser acceptance');
      precedes('Enable automatic Organization metadata and invitation authority routing for native browser acceptance', 'Authenticated browser E2E against exact release images');
    }
    assert.equal(steps.at(-1).name, 'Stop release topology');
    assert.equal(steps.at(-1).if, 'always()');
    for (const step of steps) {
      assert.ok(!/docker (?:build|buildx build)|compose[^\n]*\bbuild\b/.test(step.run ?? ''), 'Integration may not rebuild release images');
      if (step.uses?.startsWith('actions/upload-artifact@')) {
        const name = step.with.name.replace('${{ matrix.suite }}', suite).replace('${{ matrix.shard }}', String(shard));
        assert.ok(!artifactNames.has(name), `Artifact collision across independent jobs: ${name}`);
        artifactNames.add(name);
      }
    }
  }
  assert.equal(jobs['required-ci'].name, 'required-ci');
  assert.equal(jobs['required-ci'].if, 'always()');
  assert.deepEqual(jobs['required-ci'].needs, ['metadata', 'source-quality-gate', 'build-images-once', 'container-integration', 'security']);
  const gate = jobs['required-ci'].steps.find(step => step.name === 'Require every mandatory CI stage');
  assert.equal(gate.env.CONTAINER_RESULT, '${{ needs.container-integration.result }}');
  assert.equal(gate.env.METADATA_RESULT, '${{ needs.metadata.result }}');
  assert.match(gate.run, /for result in .*"\$METADATA_RESULT"/);
  assert.match(gate.run, /for result in .*"\$CONTAINER_RESULT"/);
  assert.match(gate.run, /if \[ "\$result" != "success" \]; then[\s\S]*exit 1/);
  assert.ok(jobs['release-bundle'].needs.includes('required-ci'));
  return { groups: suites.length, executions: executions.length, registeredSteps: named.length };
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const result = verifyIntegrationSuites(
    readWorkflow(readFileSync('.github/workflows/ci.yml', 'utf8')),
    JSON.parse(readFileSync('scripts/ci/integration-suites.json', 'utf8')),
  );
  console.log(`Mandatory integration coverage: ${result.groups} groups/${result.executions} isolated executions, ${result.registeredSteps} registered steps`);
}
