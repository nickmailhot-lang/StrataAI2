import { readFileSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { createHash } from 'node:crypto';
export function qualificationRecord(metadata, provenance, env, checksum) {
  if (!/^[a-f0-9]{40}$/.test(metadata.commitSha) || metadata.commitSha !== env.GITHUB_SHA || metadata.workflowRunId !== env.GITHUB_RUN_ID)
    throw new Error('Qualification revision/run mismatch');
  if (!/^[a-f0-9]{64}$/.test(checksum)) throw new Error('Invalid bundle checksum digest');
  return { schemaVersion: 1, qualification: 'full-sanity', revision: metadata.commitSha,
    repository: metadata.repository, workflowRunId: metadata.workflowRunId,
    workflowRunAttempt: metadata.workflowRunAttempt, version: metadata.version,
    images: provenance, bundleManifestSha256: checksum,
    runUrl: `https://github.com/${metadata.repository}/actions/runs/${metadata.workflowRunId}` };
}
if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const record = qualificationRecord(JSON.parse(readFileSync('image-artifacts/build-metadata.json')),
    JSON.parse(readFileSync('image-artifacts/image-provenance.json')), process.env,
    createHash('sha256').update(readFileSync('bundle/SHA256SUMS')).digest('hex'));
  writeFileSync('full-qualification.json', JSON.stringify(record, null, 2) + '\n');
}
