import test from 'node:test';
import assert from 'node:assert/strict';
import {createHash} from 'node:crypto';
import {partitionWebTests,validatePartitionProofs} from './web-partitions.mjs';
const seal=scope=>({...scope,scopeFingerprint:createHash('sha256').update(JSON.stringify(scope)).digest('hex')});
const files=Array.from({length:225},(_,i)=>`apps/web/src/feature/Case${i}.test.tsx`);
const plan=seal({web:true,full:false,webTests:files,webPartitions:partitionWebTests(files),revision:'a'.repeat(40),sourceTree:'b'.repeat(40)});
const proofs=()=>plan.webPartitions.map(partition=>({lane:'web',partition,revision:plan.revision,sourceTree:plan.sourceTree,scopeFingerprint:plan.scopeFingerprint,elapsedMs:200_000,result:'Passed',productionQualified:false,checks:partition.id===1?['types','browser-types','lint','units','build','compiled-ui']:['units'],summary:{result:'Passed',files:partition.files.length,passed:100,existingDeferred:0},reportHashes:Object.fromEntries(['selection.json','discovery.json','web.xml','summary.json'].map(k=>[k,'c'.repeat(64)]))}));
test('all225completefiles assigned exactlyonce without narrowing actual selected source',()=>{
 const parts=partitionWebTests(files);assert.equal(parts.length,3);assert.deepEqual(parts.map(p=>p.files.length),[75,75,75]);assert.deepEqual(parts.flatMap(p=>p.files).sort(),[...files].sort());assert.deepEqual(partitionWebTests([...files].reverse()),parts);
 assert.equal(validatePartitionProofs(plan,proofs()).files,225);
 assert.equal(validatePartitionProofs(plan,proofs()).productionQualified,false);
});
for(const bad of [[],files.slice(0,2),[...files,files[0]],[...files,'../secret'],[...files,'apps/web/src/../secret.test.ts']])test('unsafe,missing,duplicateorinsufficient inventory refuses '+bad.length,()=>assert.throws(()=>partitionWebTests(bad)));
for(const [name,mutate] of [
 ['missing partition',p=>p.pop()],['extra partition',p=>p.push(p[0])],['duplicate partition',p=>p[1]=p[0]],
 ['missing source file',p=>p[1].partition.files.pop()],['extra source file',p=>p[1].partition.files.push('apps/web/src/Extra.test.ts')],
 ['wrong source',p=>p[0].sourceTree='d'.repeat(40)],['wrong revision',p=>p[0].revision='d'.repeat(40)],['wrong scope',p=>p[0].scopeFingerprint='d'.repeat(64)],
 ['cancelled sibling',p=>p[2].result='Cancelled'],['missing result',p=>delete p[2].result],['empty cases',p=>p[2].summary.passed=0],['missing report',p=>delete p[0].summary],
 ['invalid skip counter',p=>p[0].summary.existingDeferred=-1],['wrong file count',p=>p[1].summary.files--],['missing hash',p=>delete p[0].reportHashes['web.xml']],
 ['UI omitted',p=>p[0].checks.pop()],['unit omitted',p=>p[1].checks=[]],['budget exceeded',p=>p[1].elapsedMs=600_000],['production claim',p=>p[0].productionQualified=true]]){
 test('aggregate refuses '+name,()=>{const p=structuredClone(proofs());mutate(p);assert.throws(()=>validatePartitionProofs(plan,p));});
}
test('even a resealed omitted or overlapping partition plan refuses',()=>{
 for(const change of [s=>s.webPartitions[0].files.pop(),s=>s.webPartitions[1].files.push(s.webPartitions[0].files[0])]){const {scopeFingerprint,...scope}=structuredClone(plan);change(scope);assert.throws(()=>validatePartitionProofs(seal(scope),proofs()));}
});
