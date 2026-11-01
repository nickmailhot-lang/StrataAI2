import {createHash} from 'node:crypto';
import {readFileSync,writeFileSync,readdirSync} from 'node:fs';
import {resolve,join} from 'node:path';
import {fileURLToPath} from 'node:url';
import {execFileSync} from 'node:child_process';
import assert from 'node:assert/strict';
const hash=bytes=>createHash('sha256').update(bytes).digest('hex');
export function partitionWebTests(files){
  if(!Array.isArray(files)||files.length<3||new Set(files).size!==files.length||files.some(p=>typeof p!=='string'||!/^apps\/web\/src\/(?:[A-Za-z0-9_.-]+\/)*[A-Za-z0-9_.-]+\.test\.tsx?$/.test(p)||p.split('/').includes('..')))throw Error('Complete unique web files required for three partitions');
  const sorted=[...files].sort();
  return [1,2,3].map(id=>({id,total:3,files:sorted.filter((_,i)=>i%3===id-1)}));
}
export function validatePartitionProofs(plan,proofs){
  const {scopeFingerprint,...scope}=plan;
  if(hash(JSON.stringify(scope))!==scopeFingerprint||!plan.web||plan.full)throw Error('Invalid partition source scope');
  const partitions=partitionWebTests(plan.webTests);
  assert.deepEqual(plan.webPartitions,partitions,'Changed complete partition inventory');
  if(!Array.isArray(proofs)||proofs.length!==partitions.length)throw Error('Missing or extra web partition evidence');
  const seen=new Set();
  for(const proof of proofs){
    if(!proof||seen.has(proof.partition?.id))throw Error('Duplicate web partition evidence');
    const expected=partitions.find(x=>x.id===proof.partition?.id);
    assert.deepEqual(proof.partition,expected,'Unexpected partition files or index');seen.add(expected.id);
    if(proof.result!=='Passed'||proof.lane!=='web'||proof.revision!==plan.revision||proof.sourceTree!==plan.sourceTree||proof.scopeFingerprint!==scopeFingerprint||proof.productionQualified!==false||!Number.isFinite(proof.elapsedMs)||proof.elapsedMs<0||proof.elapsedMs>=600_000)throw Error('Invalid web partition provenance/result/timing');
    if(proof.summary?.result!=='Passed'||proof.summary.files!==expected.files.length||!Number.isInteger(proof.summary.passed)||proof.summary.passed<=0||!Number.isInteger(proof.summary.existingDeferred)||proof.summary.existingDeferred<0)throw Error('Missing complete partition report');
    assert.deepEqual(proof.checks,expected.id===1?['types','browser-types','lint','units','build','compiled-ui']:['units'],'Required web checks omitted');
    for(const key of ['selection.json','discovery.json','web.xml','summary.json'])if(!/^[0-9a-f]{64}$/.test(proof.reportHashes?.[key]??''))throw Error('Missing partition report digest');
  }
  return {result:'Passed',partitions:3,files:plan.webTests.length,passed:proofs.reduce((n,p)=>n+p.summary.passed,0),existingDeferred:proofs.reduce((n,p)=>n+p.summary.existingDeferred,0),productionQualified:false};
}
if(process.argv[1]&&resolve(process.argv[1])===fileURLToPath(import.meta.url)){
  if(process.argv[2]!=='verify')throw Error('Use verify');
  const directory=resolve(process.env.FAST_CI_PLAN_DIR??'artifacts/fast'),plan=JSON.parse(readFileSync(join(directory,'feature-plan.json')));
  const git=(...args)=>execFileSync('git',args,{encoding:'utf8'}).trim();
  if(git('rev-parse','HEAD')!==plan.revision||git('rev-parse','HEAD^{tree}')!==plan.sourceTree)throw Error('Partition aggregate source changed');
  const root=join(directory,'web-partitions'),folders=readdirSync(root,{withFileTypes:true});
  if(folders.length!==3||folders.some(x=>!x.isDirectory()||x.isSymbolicLink()))throw Error('Missing or unsafe partition artifacts');
  const proofs=folders.map(folder=>{
    const path=join(root,folder.name),proof=JSON.parse(readFileSync(join(path,'lane.json')));
    for(const key of ['selection.json','discovery.json','web.xml','summary.json'])if(hash(readFileSync(join(path,key)))!==proof.reportHashes?.[key])throw Error('Partition report digest changed');
    const selection=JSON.parse(readFileSync(join(path,'selection.json')));assert.deepEqual(selection.files,proof.partition?.files);if(resolve(selection.root)!==process.cwd())throw Error('Partition report root changed');
    execFileSync('python3',['ci/fast/web_results.py',join(path,'selection.json'),join(path,'web.xml'),'ci/fast/web-deferred.json',join(path,'discovery.json'),join(path,'revalidated-summary.json')],{stdio:'inherit',env:{...process.env,PYTHONDONTWRITEBYTECODE:'1'}});
    assert.deepEqual(JSON.parse(readFileSync(join(path,'revalidated-summary.json'))),proof.summary);
    return proof;
  });
  const summary=validatePartitionProofs(plan,proofs);writeFileSync(join(directory,'web-partition-summary.json'),JSON.stringify(summary,null,2)+'\n');console.log(JSON.stringify(summary));
}
