"""Exercise the real CI driver with bounded external-command substitutes.

This validates ordering/refusal/cleanup, not a real database or Collector.
The independent native Worker/PostgreSQL rehearsal covers runtime behavior.
"""
import importlib.util
import json
import os
from pathlib import Path
import shlex
import shutil
import subprocess
import sys
import tempfile
import unittest

REPO = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("operator_fixture_values", REPO / "tests/operator_metrics_test.py")
values = importlib.util.module_from_spec(spec)
spec.loader.exec_module(values)

STUB = r'''
import json, os, sys
from pathlib import Path
root=Path(os.environ['FIXTURE_ROOT']);kind=sys.argv[1];args=sys.argv[2:]
fault=os.environ['FIXTURE_FAULT']
def read(name,default=''): return (root/name).read_text() if (root/name).exists() else default
def write(name,value): (root/name).write_text(value)
def log(value):
 with (root/'journal').open('a') as f:f.write(value+'\n')
if kind=='sleep':sys.exit(0)
if kind=='cat':
 assert args==['/proc/sys/kernel/random/uuid']
 n=int(read('uuid','0'))+1;write('uuid',str(n));print('aaaaaaaa-aaaa-aaaa-aaaa-'+str(n).zfill(12));sys.exit(0)
if kind=='docker':
 assert args[:3]==['compose','-f','compose.release.yml']
 if 'exec' in args:
  sql=sys.stdin.read()
  if 'SELECT md5' in sql:
   n=int(read('fingerprints','0'))+1;write('fingerprints',str(n))
   missing=(fault=='missing-before' and n==1) or (fault=='missing-after' and n>1)
   print('' if missing else 'b'*32 if fault=='changed-jobs' and n>1 else 'a'*32)
  elif 'DELETE FROM background_jobs' in sql:log('delete-owned-rows')
  elif 'INSERT INTO background_jobs' in sql:log('seed-two-rows')
  else:raise AssertionError('Unexpected admin operation')
 elif 'stop' in args:
  assert args[-2:]==['stop','postgres'];log('stop-postgres');write('phase','down')
  if fault=='stop-fails':sys.exit(1)
 elif 'up' in args:
  assert args[-1] in ['worker','postgres']
  if args[-1]=='postgres':
   log('start-postgres')
   if fault=='restart-fails-once' and not (root/'restart-failed').exists():write('restart-failed','1');sys.exit(1)
   write('phase','recovered')
  elif 'scripts/ci/compose.backlog-test.yml' in args:log('scope-worker')
  else:log('restore-worker')
 else:raise AssertionError('Unexpected container operation')
 sys.exit(0)
assert kind=='curl'
url=next(a for a in args if a.startswith('http://'))
phase=read('phase','healthy');status=200
if url.endswith('/metrics'):
 available=0 if phase=='down' else 1
 if fault=='stale-healthy' and phase=='down':available=1
 if fault=='stale-unavailable' and phase=='recovered':available=0
 log('scrape-'+phase+'-'+str(available))
 text=read('metrics')
 if not available:
  text='\n'.join(line[:-1]+'0' if line.startswith('strataai_worker_scoped_backlog_available{') else line for line in text.splitlines())
 print(text);sys.exit(0)
body={'id':'11111111-1111-1111-1111-111111111111','organization':{'id':'11111111-1111-1111-1111-111111111111'},'version':1,'cardVersion':1}
if url.endswith('/healthz'):body={'status':'ok','service':'strataai-worker','timestamp':'2026-10-15T00:00:00Z'}
if url.endswith('/readyz'):
 status=503 if phase=='down' else 200
 if fault=='wrong-readiness' and phase=='down':status=200
 body={'status':'not-ready'} if status==503 else {'status':'ready','mode':'production'}
written='-w' in args or '--write-out' in args
if written and ':8088/' in url:
 if url.endswith(('/me/checklist-client-events','/me/activity-client-events')):
  status=401 if '-b' not in args else 400 if any('private-metric-fixture' in a for a in args) else 403
 elif url.endswith('/configuration'):status=409 if '-X' in args else 404
 else:status=401
if written:log('status-'+url.rsplit('/',1)[-1]+'-'+str(status))
if '-o' in args or '--output' in args:
 index=args.index('-o') if '-o' in args else args.index('--output')
 path=args[index+1]
 if path!='/dev/null':Path(path).write_text(json.dumps(body))
 if written:print(status,end='')
else:print(json.dumps(body))
'''


class OperatorFixtureDriverTests(unittest.TestCase):
    def run_driver(self, fault):
        git_bash = Path(os.environ.get("ProgramFiles", "C:/Program Files")) / "Git/bin/bash.exe"
        bash = str(git_bash) if os.name == "nt" and git_bash.is_file() else shutil.which("bash")
        self.assertIsNotNone(bash, "The CI driver contract requires Bash.")
        with tempfile.TemporaryDirectory(prefix="strataai-metrics-driver-") as temporary:
            root = Path(temporary)
            scripts = root / "scripts/ci"
            scripts.mkdir(parents=True)
            for name in ["test-operator-metrics.sh", "verify-operator-metrics.py"]:
                content = (REPO / "scripts/ci" / name).read_text()
                if name.endswith('.sh'):
                    content = content.replace('set -euo pipefail', 'set -euo pipefail\ntrap \'echo "Driver failure at line $LINENO" >&2\' ERR', 1)
                (scripts / name).write_text(content, newline="\n")
            fixture = values.OperatorEvidenceTests()
            (root / "metrics").write_text(fixture.pinned_jobs(fixture.fixture()))
            (root / "stub.py").write_text(STUB)
            commands = root / "commands"
            commands.mkdir()
            python = shlex.quote(sys.executable.replace('\\', '/'))
            stub = shlex.quote(str(root / "stub.py").replace('\\', '/'))
            for command in ["curl", "docker", "cat", "sleep"]:
                path = commands / command
                path.write_text(f'#!/usr/bin/env bash\nexec {python} {stub} {command} "$@"\n', newline="\n")
                path.chmod(0o700)
            path = commands / "python3"
            path.write_text(f'#!/usr/bin/env bash\nexec {python} "$@"\n', newline="\n")
            path.chmod(0o700)
            if fault == 'missing-substitute':
                (commands / 'curl').unlink()
            env = dict(os.environ, CI="true", FIXTURE_ROOT=str(root), FIXTURE_FAULT=fault,
                       GITHUB_ACTIONS="false" if fault == 'outside-actions' else "true",
                       GITHUB_SHA=fixture.revision, STRATAAI_BUILD_VERSION=fixture.version,
                       PATH=str(commands) + os.pathsep + os.environ["PATH"])
            env['FIXTURE_COMMANDS'] = ('/' + commands.drive[0].lower() + commands.as_posix()[2:]) if os.name == 'nt' else str(commands)
            # Fail before any script action if the shell rewrites inherited PATH.
            # A driver substitute must never fall through to real HTTP/Docker.
            launch = 'export PATH="$FIXTURE_COMMANDS:$PATH"; for cmd in curl docker cat sleep python3; do [[ "$(command -v "$cmd")" = "$FIXTURE_COMMANDS/$cmd" ]] || exit 90; done; exec bash scripts/ci/test-operator-metrics.sh'
            result = subprocess.run([bash, "-c", launch], cwd=root, env=env,
                                    capture_output=True, text=True, timeout=90)
            journal = (root / "journal").read_text().splitlines() if (root / "journal").exists() else []
            if fault in {'missing-substitute', 'outside-actions'}:
                self.assertEqual(result.returncode, 90 if fault == 'missing-substitute' else 1)
                self.assertEqual(journal, [])
                self.assertFalse((root / 'artifacts').exists())
                return
            self.assertIn("scope-worker", journal, result.stderr[-2000:])
            if fault == 'missing-before':
                self.assertNotEqual(result.returncode, 0)
                self.assertNotIn('stop-postgres', journal)
                self.assertNotIn('start-postgres', journal)
                self.assertEqual(journal[-2:], ['restore-worker', 'delete-owned-rows'])
                return
            self.assertIn("stop-postgres", journal, result.stderr[-2000:] + str(journal))
            self.assertIn("start-postgres", journal, result.stderr[-2000:])
            self.assertEqual(journal[-2:], ["restore-worker", "delete-owned-rows"], result.stderr[-2000:])
            self.assertLess(journal.index("stop-postgres"), journal.index("start-postgres"))
            if fault:
                self.assertNotEqual(result.returncode, 0)
                self.assertFalse((root / "artifacts/metrics/operator.json").exists())
            else:
                self.assertEqual(result.returncode, 0, result.stderr[-2000:])
                for name, available in [("operator.json", 1), ("backlog-unavailable.json", 0), ("backlog-recovered.json", 1)]:
                    record = json.loads((root / "artifacts/metrics" / name).read_text())
                    self.assertEqual(record["revision"], fixture.revision)
                    self.assertEqual(record["status"], "passed")
                    self.assertEqual(record["verified"]["workerBacklogObservationAvailability"], available)
                self.assertLess(journal.index("scrape-down-0"), journal.index("start-postgres"))
                self.assertGreater(journal.index("scrape-recovered-1"), journal.index("start-postgres"))

    def test_complete_driver_requires_three_stages_and_restores_owned_resources(self):
        self.run_driver("")

    def test_stale_healthy_snapshot_refuses_outage_evidence_and_restores_database(self):
        self.run_driver("stale-healthy")

    def test_stale_unavailable_snapshot_refuses_recovery_evidence(self):
        self.run_driver("stale-unavailable")

    def test_failed_first_database_restart_is_retried_only_by_cleanup(self):
        self.run_driver("restart-fails-once")

    def test_failed_stop_still_restores_database(self):
        self.run_driver("stop-fails")

    def test_incorrect_readiness_refuses_evidence(self):
        self.run_driver("wrong-readiness")

    def test_changed_job_rows_refuse_evidence(self):
        self.run_driver("changed-jobs")

    def test_missing_substitute_refuses_before_any_driver_action(self):
        self.run_driver("missing-substitute")

    def test_ci_flag_alone_cannot_start_local_http_or_container_actions(self):
        self.run_driver("outside-actions")

    def test_missing_initial_job_rows_refuse_before_database_stop(self):
        self.run_driver("missing-before")

    def test_missing_recovered_job_rows_refuse_recovery_evidence(self):
        self.run_driver("missing-after")
