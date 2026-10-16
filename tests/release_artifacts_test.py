import importlib.util
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile
import textwrap
import unittest
from unittest.mock import patch
from image_labels_test import saved_image

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
            image_id=json.loads((self.security/'image-provenance.json').read_text())['imageIds'][host]
            properties={'aquasecurity:trivy:ImageID':image_id,
                'aquasecurity:trivy:Labels:org.opencontainers.image.source':'https://github.com/'+META['repository'],
                'aquasecurity:trivy:Labels:org.opencontainers.image.revision':META['commitSha'],
                'aquasecurity:trivy:Labels:org.opencontainers.image.version':META['version'],
                'aquasecurity:trivy:Labels:org.opencontainers.image.created':META['createdAt']}
            (self.security/f'strataai-{host}.cdx.json').write_text(json.dumps({'bomFormat':'CycloneDX','metadata':{'component':{'type':'container','name':META['images'][host],
                'properties':[{'name':key,'value':value} for key,value in properties.items()]}},'components':[{'type':'library','name':'synthetic'}]}))
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
        names=['compose.release.yml','compose.demo.release.yml','compose.metrics.yml','compose.attachments.yml','.env.release.example','.env.demo.example','README.md','promotion-and-rollback.md','health-check.sh','verify-release-runtime.sh','verify-https-certificate.sh','backup-postgres.sh','verify-backup-record.py','restore-postgres.sh','apply-migrations.sh','migration-stream.sh','provision-runtime-roles.sh','provision-identity-mail-role.sh','deploy/metrics/collector.yml','deploy/metrics/alerts.yml','deploy/metrics/prometheus.example.yml','db/provision-runtime-roles.sql','db/provision-identity-mail-role.sql','db/migrations/001_initial.sql']
        names.append('record-release.py')
        names.append('verify-image-labels.py')
        for name in names:
            p=self.bundle/name; p.parent.mkdir(parents=True,exist_ok=True); p.write_bytes(b'synthetic fixture')
        for name in ('compose.https.yml','deploy/https/nginx-tls.inc'):
            p=self.bundle/name; p.parent.mkdir(parents=True,exist_ok=True);p.write_bytes(b'synthetic HTTPS fixture')
        self.checksums(self.bundle)

    def test_deployment_assets_required_even_with_matching_checksums(self):
        self.make_bundle()
        for name in ('compose.https.yml','deploy/https/nginx-tls.inc','verify-release-runtime.sh','record-release.py','verify-image-labels.py','verify-https-certificate.sh','deploy/metrics/alerts.yml','deploy/metrics/prometheus.example.yml'):
            with self.subTest(name=name):
                path=self.bundle/name;original=path.read_bytes();path.unlink();self.checksums(self.bundle)
                with self.assertRaisesRegex(ValueError,'incomplete_bundle'): release.verify_bundle(self.bundle)
                path.write_bytes(original)

    def test_private_tls_files_cannot_be_release_assets(self):
        self.make_bundle()
        for name in ('fullchain.pem','privkey.KEY','identity.p12','identity.PFX'):
            with self.subTest(name=name):
                p=self.bundle/'deploy/https'/name;p.write_bytes(b'PRIVATE_TLS_FIXTURE');self.checksums(self.bundle)
                with self.assertRaisesRegex(ValueError,'private_tls_material'):release.verify_bundle(self.bundle)
                p.unlink()

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

    def reject_changed_sbom_identity(self, host, mutate):
        path=self.security/f'strataai-{host}.cdx.json'; original=path.read_bytes()
        value=json.loads(original); mutate(value['metadata']['component'])
        path.write_text(json.dumps(value)); self.checksums(self.security)
        try:
            with self.assertRaisesRegex(ValueError,'different_sbom_provenance'):
                release.verify_inputs(self.images,self.security)
        finally:
            path.write_bytes(original); self.checksums(self.security)

    def test_same_tag_sbom_with_different_actual_image_id_is_rejected(self):
        for host in release.HOSTS:
            with self.subTest(host=host):
                self.reject_changed_sbom_identity(host, lambda component: component['properties'][0].update(value='sha256:'+'f'*64))

    def test_same_tag_sbom_with_different_build_labels_is_rejected(self):
        for host in release.HOSTS:
            for index in range(1,5):
                with self.subTest(host=host,index=index):
                    self.reject_changed_sbom_identity(host, lambda component: component['properties'][index].update(value='different-private-input'))

    def test_missing_sbom_identity_property_is_rejected(self):
        for host in release.HOSTS:
            for index in range(5):
                with self.subTest(host=host,index=index):
                    self.reject_changed_sbom_identity(host, lambda component: component['properties'].pop(index))

    def test_duplicate_sbom_identity_property_is_rejected_even_if_identical(self):
        for host in release.HOSTS:
            for index in range(5):
                with self.subTest(host=host,index=index):
                    self.reject_changed_sbom_identity(host, lambda component: component['properties'].append(dict(component['properties'][index])))

    def test_malformed_sbom_identity_property_collection_is_rejected(self):
        for properties in [None,{},'different-private-input',[None]]:
            with self.subTest(properties=properties):
                self.reject_changed_sbom_identity('api', lambda component: component.update(properties=properties))

    def bind_saved_sbom(self, host, manifest_record=False, corrupt=False):
        path=self.security/f'strataai-{host}.cdx.json'; value=json.loads(path.read_text())
        properties=value['metadata']['component']['properties']
        prefix='aquasecurity:trivy:Labels:'
        labels={item['name'][len(prefix):]:item['value'] for item in properties if item['name'].startswith(prefix)}
        def damage(entries):
            return [(name, raw+b'changed' if name.startswith('blobs/') and b'"rootfs"' in raw else raw) for name,raw in entries]
        config,manifest,_=saved_image(self.images,host,{'Config':{'Labels':labels,'Env':['PUBLIC_FIXTURE_HOST='+host]}},damage if corrupt else None)
        for directory in (self.images,self.security):
            record=directory/'image-provenance.json'; data=json.loads(record.read_text())
            data['imageIds'][host]=manifest if manifest_record else config; record.write_text(json.dumps(data))
        properties[0]['value']=config if manifest_record else manifest
        path.write_text(json.dumps(value)); self.checksums(self.images); self.checksums(self.security)

    def test_sbom_manifest_id_requires_the_original_config_manifest_binding(self):
        for host in release.HOSTS:
            with self.subTest(host=host):
                self.bind_saved_sbom(host); self.assertEqual(release.verify_inputs(self.images,self.security),META)

    def test_sbom_config_id_can_match_a_retained_manifest_record(self):
        for host in release.HOSTS:
            with self.subTest(host=host):
                self.bind_saved_sbom(host,manifest_record=True); self.assertEqual(release.verify_inputs(self.images,self.security),META)

    def test_corrupt_saved_bytes_cannot_authorize_a_different_sbom_id(self):
        self.bind_saved_sbom('worker',corrupt=True)
        with self.assertRaisesRegex(ValueError,'different_sbom_provenance'): release.verify_inputs(self.images,self.security)

    def test_rehashed_bundle_cannot_substitute_both_sbom_copies(self):
        self.make_bundle()
        for directory in (self.bundle/'security',self.bundle/'sbom'):
            path=directory/'strataai-worker.cdx.json'; value=json.loads(path.read_text())
            value['metadata']['component']['properties'][0]['value']='sha256:'+'f'*64
            path.write_text(json.dumps(value))
        self.checksums(self.bundle/'security'); self.checksums(self.bundle)
        with self.assertRaisesRegex(ValueError,'different_sbom_provenance'): release.verify_bundle(self.bundle)

    def test_security_identity_from_another_run_rejected(self):
        p=self.security/'build-metadata.json'; value=json.loads(p.read_text()); value['workflowRunId']='456'; p.write_text(json.dumps(value)); self.checksums(self.security)
        with self.assertRaises(ValueError): release.verify_inputs(self.images,self.security)

    def test_complete_bundle_passes(self):
        self.make_bundle(); self.assertEqual(release.verify_bundle(self.bundle),META)

    def test_missing_operator_backup_tool_rejects_even_with_matching_checksums(self):
        self.make_bundle(); (self.bundle/'backup-postgres.sh').unlink(); self.checksums(self.bundle)
        with self.assertRaisesRegex(ValueError,'incomplete_bundle'): release.verify_bundle(self.bundle)

    def test_missing_backup_record_verifier_rejects_even_with_matching_checksums(self):
        self.make_bundle(); (self.bundle/'verify-backup-record.py').unlink(); self.checksums(self.bundle)
        with self.assertRaisesRegex(ValueError,'incomplete_bundle'): release.verify_bundle(self.bundle)

    def test_missing_operator_restore_tool_rejects_even_with_matching_checksums(self):
        self.make_bundle(); (self.bundle/'restore-postgres.sh').unlink(); self.checksums(self.bundle)
        with self.assertRaisesRegex(ValueError,'incomplete_bundle'): release.verify_bundle(self.bundle)

    def test_recovery_data_cannot_be_release_assets_even_with_matching_checksums(self):
        self.make_bundle()
        for name in ('db/accidental-backup.cms','deploy/restored.dump'):
            with self.subTest(name=name):
                path=self.bundle/name; path.parent.mkdir(parents=True,exist_ok=True);path.write_bytes(b'PRIVATE_RECOVERY_FIXTURE')
                self.checksums(self.bundle)
                with self.assertRaisesRegex(ValueError,'private_recovery_material'): release.verify_bundle(self.bundle)
                path.unlink()

    def test_release_guides_allow_local_navigation_and_exact_revision_references(self):
        self.make_bundle()
        (self.bundle/'README.md').write_text(
            '[Production](#production) [Rollback](promotion-and-rollback.md) '
            f"[Configuration](https://github.com/{META['repository']}/blob/{META['commitSha']}/docs/architecture/configuration.md#postgresql-transport) "
            '[Demo](http://localhost:8088/login)\n')
        self.checksums(self.bundle)
        self.assertEqual(release.verify_bundle(self.bundle),META)

    def test_current_operator_documents_pass_after_the_actual_assembly_rewrite(self):
        self.make_bundle()
        repo=Path(__file__).resolve().parents[1]
        (self.root/'image-artifacts').mkdir()
        shutil.copyfile(self.images/'build-metadata.json',self.root/'image-artifacts/build-metadata.json')
        for name in ('README.md','promotion-and-rollback.md'):
            shutil.copyfile(repo/'docs/release'/name,self.bundle/name)
        assembly=(repo/'.github/workflows/ci.yml').read_text().split('      - name: Assemble release bundle\n',1)[1]
        snippet=re.search(r"(?ms)^          python3 - <<'PY'\n(.*?)^          PY$",assembly).group(1)
        result=subprocess.run([sys.executable,'-c',textwrap.dedent(snippet)],cwd=self.root,capture_output=True,timeout=15)
        self.assertEqual(result.returncode,0)
        self.assertNotIn('](../architecture/',(self.bundle/'README.md').read_text())
        self.checksums(self.bundle)
        self.assertEqual(release.verify_bundle(self.bundle,self.images,self.security),META)

    def test_release_guides_reject_unbundled_files_even_with_matching_checksums(self):
        self.make_bundle()
        for target in ('../architecture/configuration.md', 'missing-guide.md', '/README.md', '%2e%2e/private-guide.md'):
            with self.subTest(target=target):
                (self.bundle/'README.md').write_text(f'[Configuration]({target})\n')
                self.checksums(self.bundle)
                with self.assertRaisesRegex(ValueError,'invalid_release_document_link'):
                    release.verify_bundle(self.bundle)

    def test_release_guides_reject_moving_or_other_revision_source_links(self):
        self.make_bundle()
        for route in ('blob/main', 'tree/main', 'blob/'+('f'*40)):
            with self.subTest(route=route):
                (self.bundle/'promotion-and-rollback.md').write_text(
                    f"[Configuration](https://github.com/{META['repository']}/{route}/docs/architecture/configuration.md)\n")
                self.checksums(self.bundle)
                with self.assertRaisesRegex(ValueError,'invalid_release_document_link'):
                    release.verify_bundle(self.bundle)

    def test_empty_operator_guide_is_not_a_complete_bundle(self):
        self.make_bundle(); (self.bundle/'README.md').write_text(' \n'); self.checksums(self.bundle)
        with self.assertRaisesRegex(ValueError,'empty_release_document'): release.verify_bundle(self.bundle)

    def test_missing_hidden_environment_example_rejected(self):
        self.make_bundle(); (self.bundle/'.env.release.example').unlink(); self.checksums(self.bundle)
        with self.assertRaises(ValueError): release.verify_bundle(self.bundle)

    def test_demo_payload_is_required_even_with_recalculated_checksums(self):
        self.make_bundle()
        for name in ('compose.demo.release.yml', '.env.demo.example'):
            path=self.bundle/name; original=path.read_bytes(); path.unlink(); self.checksums(self.bundle)
            with self.assertRaisesRegex(ValueError, 'incomplete_bundle'): release.verify_bundle(self.bundle)
            path.write_bytes(original)

    def test_private_demo_settings_are_not_release_assets(self):
        self.make_bundle(); (self.bundle/'.env.demo').write_bytes(b'PRIVATE_FIXTURE_SETTINGS'); self.checksums(self.bundle)
        with self.assertRaisesRegex(ValueError, 'unexpected_hidden_file'): release.verify_bundle(self.bundle)

    def test_missing_promotion_guide_rejected_even_with_recalculated_checksums(self):
        self.make_bundle(); (self.bundle/'promotion-and-rollback.md').unlink(); self.checksums(self.bundle)
        with self.assertRaisesRegex(ValueError, 'incomplete_bundle'): release.verify_bundle(self.bundle)

    def test_missing_identity_mail_provisioner_rejected_even_with_recalculated_checksums(self):
        self.make_bundle(); (self.bundle/'provision-identity-mail-role.sh').unlink(); self.checksums(self.bundle)
        with self.assertRaisesRegex(ValueError, 'incomplete_bundle'): release.verify_bundle(self.bundle)

    def test_missing_identity_mail_grants_rejected_even_with_recalculated_checksums(self):
        self.make_bundle(); (self.bundle/'db/provision-identity-mail-role.sql').unlink(); self.checksums(self.bundle)
        with self.assertRaisesRegex(ValueError, 'incomplete_bundle'): release.verify_bundle(self.bundle)

    def test_unexpected_root_file_rejected_even_with_recalculated_checksums(self):
        self.make_bundle(); (self.bundle/'unexpected.md').write_bytes(b'synthetic fixture'); self.checksums(self.bundle)
        with self.assertRaisesRegex(ValueError, 'unexpected_root_file'): release.verify_bundle(self.bundle)

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
