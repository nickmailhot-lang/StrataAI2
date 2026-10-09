import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location("release_artifacts", Path(__file__).parents[1] / "scripts/ci/verify-release-artifacts.py")
release = importlib.util.module_from_spec(spec); spec.loader.exec_module(release)
SHA = "a" * 40
META = {"repository": "example/StrataAI2", "commitSha": SHA, "shortSha": SHA[:12], "workflowRunId": "123", "workflowRunNumber": "1",
        "workflowRunAttempt": "1", "version": "0.1.0-1", "releaseVersion": None, "imageTag": SHA, "createdAt": "2026-10-08T00:00:00.000Z",
        "images": {host: f"strataai-{host}:{SHA}" for host in release.HOSTS}}


class ReleaseArtifactTests(unittest.TestCase):
    def setUp(self):
        self.folder = tempfile.TemporaryDirectory(prefix="strataai-release-"); self.addCleanup(self.folder.cleanup)
        self.root = Path(self.folder.name); self.images = self.root/'images'; self.security = self.root/'security'; self.bundle = self.root/'bundle'
        self.images.mkdir(); self.security.mkdir()
        raw=(json.dumps(META, indent=2)+'\n').encode()
        for directory in (self.images,self.security):
            (directory/'build-metadata.json').write_bytes(raw)
            (directory/'image-provenance.json').write_text(json.dumps({'schemaVersion':1,'build':META,
                'imageIds':{host:'sha256:'+str(i+1)*64 for i,host in enumerate(release.HOSTS)}}),encoding='utf-8')
        for host in release.HOSTS:
            (self.images/f'strataai-{host}.tar.gz').write_bytes(b'synthetic image transport fixture')
            (self.security/f'strataai-{host}.cdx.json').write_text(json.dumps({'bomFormat':'CycloneDX','metadata':{'component':{'type':'container','name':META['images'][host]}},'components':[{'type':'library','name':'synthetic'}]}))
        (self.security/'metrics-collector.cdx.json').write_text(json.dumps({'bomFormat':'CycloneDX','metadata':{'component':{'type':'container','name':'synthetic receiver'}},'components':[{'type':'library','name':'synthetic'}]}))
        self.checksums(self.images); self.checksums(self.security)
        self.environment=patch.dict(os.environ,{},clear=True); self.environment.start(); self.addCleanup(self.environment.stop)

    def checksums(self,root):
        paths=sorted(p for p in root.rglob('*') if p.is_file() and p!=root/'SHA256SUMS')
        (root/'SHA256SUMS').write_text(''.join(release.digest(p)+'  '+p.relative_to(root).as_posix()+'\n' for p in paths),newline='\n')

    def make_bundle(self):
        self.bundle.mkdir(); shutil.copytree(self.images,self.bundle/'images')
        shutil.copyfile(self.images/'build-metadata.json',self.bundle/'build-metadata.json'); shutil.copytree(self.security,self.bundle/'security')
        (self.bundle/'sbom').mkdir()
        for p in self.security.glob('*.cdx.json'): shutil.copyfile(p,self.bundle/'sbom'/p.name)
        names=['compose.release.yml','compose.metrics.yml','compose.attachments.yml','.env.release.example','README.md','health-check.sh','apply-migrations.sh','migration-stream.sh','provision-runtime-roles.sh','deploy/metrics/collector.yml','db/provision-runtime-roles.sql','db/migrations/001_initial.sql']
        for name in names:
            p=self.bundle/name; p.parent.mkdir(parents=True,exist_ok=True); p.write_bytes(b'synthetic fixture')
        self.checksums(self.bundle)

    def test_matching_inputs_pass(self): self.assertEqual(release.verify_inputs(self.images,self.security),META)

    def test_provenance_is_required_even_with_recalculated_checksums(self):
        (self.images/'image-provenance.json').unlink(); self.checksums(self.images)
        with self.assertRaises(OSError): release.verify_inputs(self.images,self.security)

    def test_security_cannot_substitute_another_tested_image_identity(self):
        path=self.security/'image-provenance.json'; value=json.loads(path.read_text()); value['imageIds']['worker']='sha256:'+'f'*64
        path.write_text(json.dumps(value)); self.checksums(self.security)
        with self.assertRaises(ValueError): release.verify_inputs(self.images,self.security)

    def test_bundle_cannot_replace_all_provenance_records_and_recalculate_checksums(self):
        self.make_bundle()
        for directory in (self.bundle/'images',self.bundle/'security'):
            path=directory/'image-provenance.json'; value=json.loads(path.read_text()); value['imageIds']['worker']='sha256:'+'f'*64
            path.write_text(json.dumps(value)); self.checksums(directory)
        self.checksums(self.bundle)
        with self.assertRaises(ValueError): release.verify_bundle(self.bundle,self.images,self.security)

    def test_changed_archive_rejected(self):
        (self.images/'strataai-worker.tar.gz').write_bytes(b'changed')
        with self.assertRaises(ValueError): release.verify_inputs(self.images,self.security)

    def test_missing_sbom_rejected_even_with_recalculated_checksums(self):
        (self.security/'strataai-api.cdx.json').unlink(); self.checksums(self.security)
        with self.assertRaises((ValueError,OSError)): release.verify_inputs(self.images,self.security)

    def test_sbom_from_another_commit_rejected(self):
        p=self.security/'strataai-api.cdx.json'; value=json.loads(p.read_text()); value['metadata']['component']['name']='strataai-api:'+('b'*40); p.write_text(json.dumps(value)); self.checksums(self.security)
        with self.assertRaises(ValueError): release.verify_inputs(self.images,self.security)

    def test_security_identity_from_another_run_rejected(self):
        p=self.security/'build-metadata.json'; value=json.loads(p.read_text()); value['workflowRunId']='456'; p.write_text(json.dumps(value)); self.checksums(self.security)
        with self.assertRaises(ValueError): release.verify_inputs(self.images,self.security)

    def test_complete_bundle_passes(self):
        self.make_bundle(); self.assertEqual(release.verify_bundle(self.bundle),META)

    def test_missing_hidden_environment_example_rejected(self):
        self.make_bundle(); (self.bundle/'.env.release.example').unlink(); self.checksums(self.bundle)
        with self.assertRaises(ValueError): release.verify_bundle(self.bundle)

    def test_unexpected_hidden_secret_file_rejected(self):
        self.make_bundle(); (self.bundle/'.env').write_bytes(b'PRIVATE_SECRET'); self.checksums(self.bundle)
        with self.assertRaises(ValueError): release.verify_bundle(self.bundle)

    def test_changed_sbom_copy_rejected_even_when_outer_checksum_matches(self):
        self.make_bundle(); p=self.bundle/'sbom/strataai-web.cdx.json'; value=json.loads(p.read_text()); value['components'].append({'name':'changed'}); p.write_text(json.dumps(value)); self.checksums(self.bundle)
        with self.assertRaises(ValueError): release.verify_bundle(self.bundle)

    def test_checksum_paths_and_duplicate_rows_rejected(self):
        p=self.images/'SHA256SUMS'; original=p.read_text()
        for invalid in (original+original.splitlines()[0]+'\n','a'*64+'  ../escape\n',original.replace('strataai-web.tar.gz','/absolute')):
            with self.subTest(invalid=invalid):
                p.write_text(invalid)
                with self.assertRaises(ValueError): release.verify_checksums(self.images)

    def test_unlisted_extra_file_rejected(self):
        (self.images/'extra').write_bytes(b'extra')
        with self.assertRaises(ValueError): release.verify_checksums(self.images)

    def test_empty_image_rejected_even_with_matching_checksums(self):
        (self.images/'strataai-web.tar.gz').write_bytes(b''); self.checksums(self.images)
        with self.assertRaises(ValueError): release.verify_inputs(self.images,self.security)

    def test_cli_mismatched_identity_fails_without_echoing_private_input(self):
        environment=os.environ.copy(); environment['GITHUB_RUN_ID']='PRIVATE_WRONG_RUN'
        result=subprocess.run([sys.executable,str(Path(release.__file__)),'inputs','--images',str(self.images),'--security',str(self.security)],env=environment,capture_output=True)
        self.assertNotEqual(result.returncode,0); self.assertNotIn(b'PRIVATE_WRONG_RUN',result.stdout+result.stderr)

    def test_changed_image_copy_rejected_when_outer_bundle_hashes_are_recalculated(self):
        self.make_bundle()
        # Preserve the original image evidence independently from the newly
        # generated bundle hashes, then simulate corruption before bundling.
        shutil.copyfile(self.images/'SHA256SUMS',self.bundle/'images/SHA256SUMS')
        shutil.copyfile(self.images/'build-metadata.json',self.bundle/'images/build-metadata.json')
        (self.bundle/'images/strataai-api.tar.gz').write_bytes(b'changed copied image')
        self.checksums(self.bundle)
        with self.assertRaises(ValueError): release.verify_bundle(self.bundle)

    def test_replaced_image_manifest_is_rejected_against_original_inputs(self):
        self.make_bundle()
        (self.bundle/'images/strataai-api.tar.gz').write_bytes(b'replaced image')
        self.checksums(self.bundle/'images'); self.checksums(self.bundle)
        with self.assertRaises(ValueError): release.verify_bundle(self.bundle,self.images,self.security)

    def test_final_bundle_matches_original_inputs(self):
        self.make_bundle()
        self.assertEqual(release.verify_bundle(self.bundle,self.images,self.security),META)

    def test_changed_security_manifest_is_rejected_against_original_inputs(self):
        self.make_bundle()
        (self.bundle/'security/extra-scan.json').write_bytes(b'{"synthetic":true}')
        self.checksums(self.bundle/'security'); self.checksums(self.bundle)
        with self.assertRaises(ValueError): release.verify_bundle(self.bundle,self.images,self.security)

    def test_copied_image_identity_cannot_diverge_from_bundle(self):
        self.make_bundle(); p=self.bundle/'images/build-metadata.json'; value=json.loads(p.read_text()); value['workflowRunId']='456'; p.write_text(json.dumps(value))
        self.checksums(self.bundle/'images'); self.checksums(self.bundle)
        with self.assertRaises(ValueError): release.verify_bundle(self.bundle)
