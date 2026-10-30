import assert from 'node:assert/strict';
import { mkdtempSync, readFileSync, realpathSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { basename, dirname, join, resolve } from 'node:path';
import { spawnSync } from 'node:child_process';
import test from 'node:test';

import { testBash as bash } from './local-test-tools.mjs';
const modulePath = resolve('scripts/ci/https-database-recovery.inc.sh').replaceAll('\\', '/');
for (const mode of ['success', 'missing-cursor', 'short-first-page', 'empty-continuation',
  'overlap', 'changed-restored-page', 'wrong-target-accepted', 'wrong-error']) {
  test(`HTTPS recovery must preserve the source-issued activity continuation: ${mode}`, () => {
    const root = mkdtempSync(join(tmpdir(), 'strataai-recovery-cursor-'));
    try {
      const items = Array.from({ length: 50 }, (_, i) => ({ eventId: `event-${i}`, organizationId: 'org', boardId: 'board' }));
      writeFileSync(join(root, 'first.json'), JSON.stringify({ organizationId: 'org', targetId: 'board', items,
        nextCursor: 'A'.repeat(80) }));
      writeFileSync(join(root, 'next.json'), JSON.stringify({ organizationId: 'org', targetId: 'board',
        items: [{ eventId: 'older-event', organizationId: 'org', boardId: 'board' }], nextCursor: null }));
      const driver = `set -euo pipefail
scratch="$PWD"
organization=org; board=board; card=card
activity_path=
request() {
  test "$1" = GET
  printf '%s\\n' "$2 $3" >> "$scratch/calls"
  if [[ "$2" == /cards/* ]]; then
    test "$3" = 400
    [ "$STRATAAI_TEST_MODE" != wrong-target-accepted ] || return 1
    if [ "$STRATAAI_TEST_MODE" = wrong-error ]; then
      printf '%s' '{"code":"unrelated_error"}' > "$scratch/response"
    else printf '%s' '{"code":"invalid_activity_cursor"}' > "$scratch/response"; fi
  elif [ "$2" = /boards/board/activity ]; then
    cp "$scratch/first.json" "$scratch/response"
    case "$STRATAAI_TEST_MODE" in
      missing-cursor) jq '.nextCursor=null' "$scratch/response" > "$scratch/mutated"; mv "$scratch/mutated" "$scratch/response" ;;
      short-first-page) jq '.items=.items[:1]' "$scratch/response" > "$scratch/mutated"; mv "$scratch/mutated" "$scratch/response" ;;
    esac
  else
    test "$2" = "/boards/board/activity?after=$(printf 'A%.0s' {1..80})"
    test "$3" = 200
    cp "$scratch/next.json" "$scratch/response"
    case "$STRATAAI_TEST_MODE" in
      empty-continuation) jq '.items=[]' "$scratch/response" > "$scratch/mutated"; mv "$scratch/mutated" "$scratch/response" ;;
      overlap) jq '.items[0].eventId="event-0"' "$scratch/response" > "$scratch/mutated"; mv "$scratch/mutated" "$scratch/response" ;;
      changed-restored-page)
        if [ -f "$scratch/activity-continuation.json" ]; then
          jq '.items[0].eventId="different-older-event"' "$scratch/response" > "$scratch/mutated"; mv "$scratch/mutated" "$scratch/response"
        fi ;;
    esac
  fi
}
source '${modulePath}'
capture_recovery_activity_cursor
check_recovery_activity_cursor
`;
      writeFileSync(join(root, 'driver.sh'), driver);
      const result = spawnSync(bash, [join(root, 'driver.sh')], { cwd: root, encoding: 'utf8', timeout: 15_000,
        env: { ...process.env, BASH_ENV: undefined, STRATAAI_TEST_MODE: mode } });
      assert.equal(result.error, undefined);
      assert.notEqual(result.status, null);
      assert.equal(result.stdout, '');
      assert.doesNotMatch(result.stderr, /command not found/);
      if (mode === 'success') {
        assert.equal(result.status, 0, result.stderr);
        const cursor = 'A'.repeat(80);
        assert.deepEqual(readFileSync(join(root, 'calls'), 'utf8').trim().split('\n'), [
          '/boards/board/activity 200', `/boards/board/activity?after=${cursor} 200`,
          `/boards/board/activity?after=${cursor} 200`, `/cards/card/activity?after=${cursor} 400`,
        ]);
      } else assert.notEqual(result.status, 0, 'Invalid continuation evidence must fail the recovery gate');
    } finally {
      assert.equal(dirname(realpathSync(root)), realpathSync(tmpdir()));
      assert.match(basename(root), /^strataai-recovery-cursor-/);
      rmSync(root, { recursive: true, force: true });
    }
  });
}
