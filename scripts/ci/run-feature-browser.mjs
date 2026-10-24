import { readdirSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import { policy } from './select-feature-tests.mjs';
const files = JSON.parse(process.env.STRATAAI_SELECTED_BROWSER ?? 'null');
const inventory = new Set(readdirSync('tests/browser').filter(name => name.endsWith('.spec.ts')).map(name => `tests/browser/${name}`));
if (!Array.isArray(files) || !files.length || !policy.baselineBrowser.every(file => files.includes(file)) || files.some(file => !inventory.has(file)))
  throw new Error('Invalid browser selection or missing critical baseline');
// Fixed executable plus argv: filenames never become shell commands/grep patterns.
// Escape dots because Playwright treats file arguments as regular expressions.
const result = spawnSync('npx', ['--no-install', 'playwright', 'test', ...files.map(file => file.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'))], { stdio: 'inherit', env: process.env });
if (result.error) throw result.error;
process.exitCode = result.status ?? 1;
