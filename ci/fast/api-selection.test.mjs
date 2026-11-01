import test from 'node:test';import assert from 'node:assert/strict';import{readFileSync,readdirSync}from'node:fs';
import{methodsInTestFile,selectApiMethods,apiTestArguments}from'./api-selection.mjs';
const folder='tests/StrataAI.Api.Tests/',inventory=readdirSync(folder).filter(f=>f.endsWith('.cs')).map(f=>folder+f),read=file=>readFileSync(file,'utf8');
test('actual Board actor tests select all existing methods without selecting the shared 1450-test class',()=>{
 const selection=selectApiMethods([folder+'BoardCreationActorTests.cs'],inventory,read);
 assert.equal(selection.methods.length,3);assert.ok(selection.methods.every(m=>m.startsWith('StrataAI.Api.Tests.ApiHostTests.')));
 const args=apiTestArguments(selection);assert.ok(args.includes('--filter-method'));assert.ok(!args.includes('--filter-class'));assert.ok(!args.includes('--filter-trait'));
 assert.equal(selection.theoryRows,'all');assert.ok(selection.methods.some(m=>m.endsWith('PRD_04_creation_expected_actor_refusal_precedes_graph_mutation')));
});
test('actual permission matrix retains its complete theory method and all rows',()=>{
 const selection=selectApiMethods([folder+'BoardPermissionMatrixTests.cs'],inventory,read);
 assert.deepEqual(selection.methods,['StrataAI.Api.Tests.ApiHostTests.PRD_05_HTTP_permission_matrix_preserves_visibility_and_explicit_edit_boundaries']);
 assert.equal(selection.theoryRows,'all');
});
test('baseline host file selects all its original methods, including production configuration refusal',()=>{
 const selection=selectApiMethods([folder+'ApiHostTests.cs'],inventory,read);assert.equal(selection.methods.length,11);
 assert.ok(selection.methods.some(m=>m.endsWith('Production_without_database_configuration_fails_closed')));
 assert.ok(selection.methods.some(m=>m.endsWith('Work_retry_keys_deduplicate_concurrent_creation_and_versioned_updates_without_disclosing_revoked_results')));
});
test('test files are deduplicated without dropping distinct methods',()=>{
 const a=folder+'ApiHostTests.cs',b=folder+'BoardCreationActorTests.cs';const selection=selectApiMethods([a,b,a],inventory,read);assert.equal(selection.methods.length,14);assert.equal(selection.files.length,2);
});
test('empty, missing, traversal and command-bearing selections fail before execution',()=>{
 for(const files of [[],['src/StrataAI.Api/Program.cs'],[folder+'Missing.cs'],[folder+'../ApiHostTests.cs'],[folder+'$(touch private).cs']])assert.throws(()=>selectApiMethods(files,inventory,read));
 for(const methods of [[],['*'],['ApiHostTests'],['StrataAI.Api.Tests.Test; exit 0'],['StrataAI.Api.Tests.Test\nextra']])assert.throws(()=>apiTestArguments({methods,theoryRows:'all'}));
});
test('unsupported namespaces and ambiguous class ownership refuse discovery',()=>{
 for(const source of ['', 'namespace Example; public class A {}', 'namespace Example;\npublic class A {}\npublic class B {}'])assert.throws(()=>methodsInTestFile(source));
 const source='namespace Example.Tests;\npublic partial class A {\n[Fact]\npublic Task<int> Unsupported() {}\n}';assert.throws(()=>methodsInTestFile(source),/Unsupported/);
});
