"""Execute the maintained gate with fenced Docker/command substitutes.

This proves orchestration refusals, not a live application or real CLI check.
The independent native rehearsal executes that real boundary.
"""
import json
import os
from pathlib import Path
import re
import shlex
import shutil
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
SOURCE = (ROOT / "scripts/ci/test-release-bundle-startup.sh").read_text()
CONTRACT = re.search(r"check_release_acceptance_record\(\) \{[\s\S]*?\n\}", SOURCE).group(0)
STUB = r'''
from pathlib import Path
import hashlib,json,os,sys
root=Path(os.environ['FIXTURE_ROOT']);fault=os.environ['FIXTURE_FAULT'];kind=sys.argv[1];args=sys.argv[2:]
def log(value):
 with (root/'journal').open('a') as stream:stream.write(value+'\n')
if kind=='refresh':
 log('refresh-addresses');sys.exit(1 if fault=='address-refresh-fails' else 0)
elif kind=='docker':
 if args[0]=='compose':
  operation=args[1]
  if operation=='ps':
   host=args[-1];changed=(root/'recovered').exists() and fault==host+'-replaced'
   print(host+('-new' if changed else '-original'))
  elif operation=='stop':
   assert args[1:]==['stop','--timeout','20','worker'];log('stop-worker')
   if fault=='stop-fails':sys.exit(1)
   (root/'stopped').touch()
  elif operation=='up':
   assert args[1:]==['up','-d','--no-deps','--no-recreate','--no-build','--pull','never','--wait','--wait-timeout','120','worker'];log('recover-worker')
   if fault=='recovery-fails':sys.exit(1)
   (root/'recovered').touch()
  else:raise AssertionError('Unexpected Compose mutation')
 elif args[:2]==['inspect','--format']:
  format,container=args[2:];host=container.split('-')[0];recovered=(root/'recovered').exists()
  if format=='{{.Image}}':print('different-image' if recovered and fault=='worker-image-changed' else 'original-image')
  elif format=='{{.State.Running}}':print('true' if fault=='worker-still-running' else 'false')
  elif format=='{{.State.ExitCode}}':print('137' if fault=='bad-worker-exit' else '0')
  elif format=='{{.State.OOMKilled}}':print('true' if fault=='worker-oom' else 'false')
  elif format=='{{.Id}} {{.Image}} {{.State.StartedAt}}':print(container+' original-image '+('changed-start' if recovered and fault==host+'-restarted' else 'original-start'))
  else:raise AssertionError('Unexpected container inspection')
 else:raise AssertionError('Unexpected Docker operation')
else:
 assert kind=='python3' and args[0]=='record-release.py'
 state=Path(args[args.index('--state')+1]);initialize='--initialize' in args
 assert state.parent!=root and args[args.index('--environment')+1]=='ci-production'
 n=int((root/'records').read_text())+1 if (root/'records').exists() else 1;(root/'records').write_text(str(n));log('record-'+str(n))
 if fault=='initial-record-fails' and n==1:sys.exit(1)
 if n==1:
  assert initialize
  value={'schemaVersion':1,'environment':'ci-production','composeProject':args[args.index('--project')+1],'previous':None,'current':{'acceptedAt':'2026-10-15T00:00:00Z','build':json.loads((root/'build-metadata.json').read_bytes()),'imageIds':json.loads((root/'images/image-provenance.json').read_bytes())['imageIds'],'buildMetadataSha256':hashlib.sha256((root/'build-metadata.json').read_bytes()).hexdigest(),'imageProvenanceSha256':hashlib.sha256((root/'images/image-provenance.json').read_bytes()).hexdigest()}}
  if fault=='wrong-record-project':value['composeProject']='other-project'
  if fault=='wrong-record-image':value['current']['imageIds']['api']='sha256:'+'b'*64
  if fault=='unexpected-record-field':value['private']='not-allowed'
  state.write_text(json.dumps(value));state.with_name(state.name+'.lock').write_text('0')
 else:
  assert not initialize
  if (fault=='repeat-mutates' and n==2) or (fault=='outage-mutates' and n==3) or (fault=='recovery-mutates' and n==4):state.write_text('{}')
 if n==3:
  assert (root/'stopped').exists() and not (root/'recovered').exists()
  if fault=='accepts-unavailable':sys.exit(0)
  print('unrelated error' if fault=='unrelated-outage-failure' else 'Release acceptance was not confirmed; retain and inspect the existing release record.',file=sys.stderr);sys.exit(1)
 if fault=='final-record-fails' and n==4:sys.exit(1)
'''


class ReleaseAcceptanceDriverTests(unittest.TestCase):
    def run_driver(self, fault=""):
        bash = "C:/Program Files/Git/bin/bash.exe" if os.name == "nt" else shutil.which("bash")
        with tempfile.TemporaryDirectory(prefix="strataai-acceptance-driver-") as temporary:
            root = Path(temporary); (root / "images").mkdir(); (root / "scratch").mkdir()
            (root / "build-metadata.json").write_text(json.dumps({"commitSha": "a" * 40, "version": "1.0.0"}))
            (root / "images/image-provenance.json").write_text(json.dumps({"imageIds": {h: "sha256:" + "a" * 64 for h in ["web", "api", "worker"]}}))
            (root / "stub.py").write_text(STUB)
            python = shlex.quote(sys.executable.replace("\\", "/")); stub = shlex.quote((root / "stub.py").as_posix())
            project = "strataai-local-demo" if fault == "wrong-namespace" else "strataai-bundle-smoke-0123456789abcdef"
            (root / "driver.sh").write_text("set -euo pipefail\nproject=" + project + "\ncompose=(docker compose)\nruntime_file=runtime.txt\n"
                + "docker() { " + python + " " + stub + " docker \"$@\"; }\n"
                + "python3() { " + python + " " + stub + " python3 \"$@\"; }\n"
                + "update_runtime_addresses() { " + python + " " + stub + " refresh; }\n"
                + CONTRACT + "\ncheck_release_acceptance_record\n", newline="\n")
            result = subprocess.run([bash, "driver.sh"], cwd=root, capture_output=True, text=True, timeout=35,
                env={**os.environ, "FIXTURE_ROOT": str(root), "FIXTURE_FAULT": fault, "BASH_ENV": "",
                     "TMPDIR": (root / "scratch").as_posix(), "RUNNER_TEMP": (root / "scratch").as_posix()})
            journal = (root / "journal").read_text().splitlines() if (root / "journal").exists() else []
            if fault:
                self.assertNotEqual(result.returncode, 0, result.stdout + result.stderr)
                self.assertNotIn("Assembled release acceptance record passed", result.stdout)
            else:
                self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
                self.assertEqual(journal, ["record-1", "record-2", "stop-worker", "record-3", "recover-worker", "refresh-addresses", "record-4"])
                self.assertIn("Assembled release acceptance record passed", result.stdout)
            if fault == "wrong-namespace":
                self.assertEqual(journal, []); self.assertEqual(list((root / "scratch").iterdir()), [])

    def test_complete_gate_requires_all_record_and_recovery_stages(self): self.run_driver()

    def test_wrong_namespace_refuses_before_mutation_or_command(self): self.run_driver("wrong-namespace")

    def test_record_failures_cannot_count_as_passing_acceptance(self):
        for fault in ["initial-record-fails", "wrong-record-project", "wrong-record-image", "unexpected-record-field",
                      "repeat-mutates", "outage-mutates", "accepts-unavailable", "unrelated-outage-failure",
                      "final-record-fails", "recovery-mutates"]:
            with self.subTest(fault=fault): self.run_driver(fault)

    def test_worker_stop_and_recovery_must_preserve_original_image(self):
        for fault in ["stop-fails", "worker-still-running", "bad-worker-exit", "worker-oom", "recovery-fails", "worker-replaced", "worker-image-changed", "address-refresh-fails"]:
            with self.subTest(fault=fault): self.run_driver(fault)

    def test_unrelated_application_and_database_processes_must_not_change(self):
        for host in ["api", "web", "postgres"]:
            for suffix in ["replaced", "restarted"]:
                with self.subTest(host=host, suffix=suffix): self.run_driver(host + "-" + suffix)

    def test_actual_startup_driver_calls_phase_after_its_final_runtime_check(self):
        self.assertGreater(SOURCE.index("\ncheck_release_acceptance_record\n"), SOURCE.rindex("\ncheck_runtime\n"))
        self.assertIn('rm -f -- "$acceptance_dir/current.json" "$acceptance_dir/current.json.lock" "$acceptance_dir/before.json"', SOURCE)
        self.assertIn('rmdir -- "$acceptance_dir" || code=1', SOURCE)
