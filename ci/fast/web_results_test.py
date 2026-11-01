import json
import hashlib
import tempfile
import unittest
from pathlib import Path
from web_results import validate

SOURCE = 'apps/web/src/features/auth/AuthPage.landmark.test.tsx'
XML = '''<testsuites tests="1" failures="0" errors="0"><testsuite name="src/features/auth/AuthPage.landmark.test.tsx" tests="1" failures="0" errors="0" skipped="0"><testcase classname="src/features/auth/AuthPage.landmark.test.tsx" name="Private fixture argument"/></testsuite></testsuites>'''
DEFERRED = json.loads(Path(__file__).with_name('web-deferred.json').read_text())


class WebResultsTests(unittest.TestCase):
    def check(self, xml=XML, files=None, discovery=None):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'web.xml'; path.write_text(xml)
            return validate({'root':'/workspace','files': files or [SOURCE]}, path, DEFERRED,discovery or [{'file':'/workspace/'+SOURCE,'name':'Private fixture argument'}])

    def test_selected_report_passes_without_private_case_names(self):
        result = self.check()
        self.assertEqual(result['passed'], 1)
        self.assertFalse(result['productionQualified'])
        self.assertNotIn('Private fixture', str(result))

    def test_missing_selected_file_and_wrong_classname_refuse(self):
        with self.assertRaises(ValueError): self.check(files=[SOURCE, 'apps/web/src/Missing.test.ts'])
        with self.assertRaises(ValueError): self.check(XML.replace('classname="src/', 'classname="other/'))

    def test_failed_errored_and_unreviewed_skips_refuse(self):
        for child in ['failure', 'error', 'skipped']:
            with self.assertRaises(ValueError): self.check(XML.replace('name="Private fixture argument"/>', f'name="Private fixture argument"><{child}/></testcase>'))

    def test_empty_or_inconsistent_counts_refuse(self):
        for xml in [XML.replace('tests="1"', 'tests="2"'), XML.replace('skipped="0"', 'skipped="1"'), XML.replace('failures="0"', 'failures="1"')]:
            with self.assertRaises(ValueError): self.check(xml)

    def test_duplicate_suite_and_unknown_file_refuse(self):
        with self.assertRaises(ValueError): self.check(XML.replace('</testsuites>', XML.split('<testsuites tests="1" failures="0" errors="0">')[1]))
        with self.assertRaises(ValueError): self.check(files=['apps/web/src/Other.test.ts'])

    def test_repeated_theory_display_names_preserve_each_independently_discovered_row(self):
        case='<testcase classname="src/features/auth/AuthPage.landmark.test.tsx" name="Private fixture argument"/>'
        two=XML.replace(case,case+case).replace('tests="1"','tests="2"')
        discovery=[{'file':'/workspace/'+SOURCE,'name':'Private fixture argument'}]*2
        self.assertEqual(self.check(two,discovery=discovery)['passed'],2)
        with self.assertRaises(ValueError):self.check(two)
        with self.assertRaises(ValueError):self.check(XML,discovery=discovery)

    def test_xml_attribute_whitespace_matches_the_unchanged_runtime_case_name(self):
        self.assertEqual(self.check(XML.replace('Private fixture argument','Private fixture argument\n'),discovery=[{'file':'/workspace/'+SOURCE,'name':'Private fixture argument\n'}])['passed'],1)

    def test_reviewed_deferred_suite_missing_from_runtime_listing_never_counts_as_passed(self):
        source='apps/web/src/features/kanban/BoardWindow.capacity.test.tsx'
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder);file=root/source;file.parent.mkdir(parents=True);file.write_text('reviewed original fixture')
            rows=''.join(f'<testcase classname="{source[9:]}" name="row{i}"><skipped/></testcase>' for i in range(24))
            suite=f'<testsuite name="{source[9:]}" tests="24" failures="0" errors="0" skipped="24">{rows}</testsuite>'
            xml=XML.replace('testsuites tests="1"','testsuites tests="25"').replace('</testsuites>',suite+'</testsuites>')
            report=root/'web.xml';report.write_text(xml)
            deferred={'sourceSha256':hashlib.sha256(file.read_bytes()).hexdigest(),'cases':[{'source':source,'caseId':hashlib.sha256((source+'\0'+f'row{i}').encode()).hexdigest()} for i in range(24)]}
            selection={'root':folder,'files':[SOURCE,source]};discovery=[{'file':str(root/SOURCE),'name':'Private fixture argument'}]
            result=validate(selection,report,deferred,discovery)
            self.assertEqual(result['passed'],1);self.assertEqual(result['existingDeferred'],24)
            report.write_text(xml.replace('<skipped/>','',1).replace('skipped="24"','skipped="23"'))
            with self.assertRaises(ValueError):validate(selection,report,deferred,discovery)

    def test_existing_functional_count_cannot_be_widened(self):
        path = 'apps/web/src/features/kanban/BoardWindow.test.tsx'
        with self.assertRaises(ValueError): self.check(XML.replace('features/auth/AuthPage.landmark', 'features/kanban/BoardWindow'), [path])

    def test_unsafe_paths_and_entity_declarations_refuse(self):
        with self.assertRaises(ValueError): self.check(files=['apps/web/src/../private.test.ts'])
        with self.assertRaises(ValueError): self.check('<!DOCTYPE testsuites []>' + XML)


if __name__ == '__main__': unittest.main()
