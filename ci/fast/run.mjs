import { readFileSync, writeFileSync, mkdirSync, mkdtempSync, rmSync } from 'node:fs';
import { resolve, join } from 'node:path';
import { tmpdir } from 'node:os';
import { spawnSync, execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { snapshot } from '../../scripts/local/repository.mjs';
import { apiTestArguments } from './api-selection.mjs';
import { linuxBackend } from './linux-backend.mjs';
import { partitionWebTests } from './web-partitions.mjs';
import assert from 'node:assert/strict';

const lane = process.argv[2], root = process.cwd();
const plan = JSON.parse(readFileSync(resolve(process.env.FAST_CI_PLAN_DIR ?? 'artifacts/fast', 'feature-plan.json'), 'utf8'));
const {scopeFingerprint, ...scope} = plan;
if (!['web','backend'].includes(lane) || plan.full || !(lane === 'web' ? plan.web : plan.dotnet && plan.api)) throw new Error('Unselected fast lane');
if (createHash('sha256').update(JSON.stringify(scope)).digest('hex') !== scopeFingerprint) throw new Error('Changed fast plan');
function source() {
  if (execFileSync('git',['rev-parse','HEAD'],{encoding:'utf8'}).trim() !== plan.revision || snapshot(root) !== plan.sourceTree) throw new Error('Source tree changed since selection');
}
source();
const output = resolve(process.env.FAST_CI_RESULTS ?? `artifacts/fast/${lane}`);
mkdirSync(output,{recursive:true});
const start = Date.now(), commands = [];
let linux;
let coldPackages;
let partition=null, summary=null;
const checks=[];
if(lane==='web'){
  assert.deepEqual(plan.webPartitions,partitionWebTests(plan.webTests));
  const requested=process.env.FAST_CI_WEB_PARTITION;
  if(requested!==undefined){if(!/^[123]$/.test(requested))throw Error('Invalid web partition');partition=plan.webPartitions[Number(requested)-1];}
  else if(process.env.GITHUB_ACTIONS==='true')throw Error('Hosted web partition required');
}
const run = (command, args, extra = {}) => {
  const remaining = 600_000 - (Date.now() - start);
  if (remaining <= 0) throw new Error('Fast lane execution budget exhausted');
  commands.push([command,...args]);
  const actual=command==='dotnet' && linux ? linux.wrap(command,args) : {command,args};
  const result = spawnSync(actual.command,actual.args,{stdio:'inherit',shell:false,timeout:remaining,
    env:{...process.env,DOTNET_CLI_TELEMETRY_OPTOUT:'1',STRATAAI_POST_MVP_LOAD_TESTS:'0',...(coldPackages && command==='dotnet'?{NUGET_PACKAGES:coldPackages}:{}),...extra}});
  if (result.status !== 0 || result.error) throw new Error(`Fast ${lane} command failed: ${command}`);
};
try { if (lane === 'web') {
  if (!plan.webTests?.length) throw new Error('Empty web selection');
  if(!partition||partition.id===1){run('npm',['run','typecheck']); run('npm',['run','typecheck:browser']); run('npm',['run','lint']);checks.push('types','browser-types','lint');}
  const selected=partition?.files??plan.webTests;
  writeFileSync(resolve(output,'selection.json'),JSON.stringify({root,files:selected}));
  const files=selected.map(p=>p.replace(/^apps\/web\//,''));
  run('npm',['exec','--workspace','@strataai/web','--','vitest','list',...files,'--no-staticParse','--maxWorkers=2',`--json=${resolve(output,'discovery.json')}`]);
  run('npm',['test','--',...files,'--maxWorkers=2','--reporter=junit',`--outputFile=${resolve(output,'web.xml')}`]);
  run('python3',['ci/fast/web_results.py',resolve(output,'selection.json'),resolve(output,'web.xml'),'ci/fast/web-deferred.json',resolve(output,'discovery.json'),resolve(output,'summary.json')],{PYTHONDONTWRITEBYTECODE:'1'});
  summary=JSON.parse(readFileSync(resolve(output,'summary.json')));checks.push('units');
  if(!partition||partition.id===1){run('npm',['run','build']);
    run('npx',['playwright','test','--config','ci/fast/ui.config.ts'],{FAST_CI_UI_RESULTS:resolve(output,'ui')});checks.push('build','compiled-ui');}
} else {
  if (!plan.apiSelection?.methods?.length) throw new Error('Empty API selection');
  if (process.platform !== 'linux') linux=linuxBackend(plan.sourceTree,output);
  else if(plan.measurement?.coldCache)coldPackages=mkdtempSync(join(tmpdir(),'strataai-fast-cold-nuget-'));
  run('dotnet',['restore','StrataAI2.slnx','--locked-mode']);
  run('dotnet',['build','StrataAI2.slnx','-c','Release','--no-restore','-warnaserror']);
  run('dotnet',['test','--project','tests/StrataAI.Domain.Tests/StrataAI.Domain.Tests.csproj','-c','Release','--no-build','--report-trx','--report-trx-filename','feature-domain.trx','--results-directory',output]);
  run('python3',['ci/fast/domain_results.py',resolve(output,'feature-domain.trx'),root,'ci/fast/domain-deferred.json',resolve(output,'domain-summary.json')],{PYTHONDONTWRITEBYTECODE:'1'});
  writeFileSync(resolve(output,'selection.json'),JSON.stringify(plan.apiSelection));
  run('dotnet',[...apiTestArguments(plan.apiSelection),'--results-directory',output]);
  run('python3',['ci/fast/api_results.py',resolve(output,'selection.json'),resolve(output,'feature-api.trx'),resolve(output,'summary.json')],{PYTHONDONTWRITEBYTECODE:'1'});
}
source();
const elapsedMs=Date.now()-start;
if (elapsedMs>=600_000) throw new Error('Fast lane exceeded execution budget');
writeFileSync(resolve(output,'lane.json'),JSON.stringify({lane,revision:plan.revision,sourceTree:plan.sourceTree,scopeFingerprint,commands,elapsedMs,...(lane==='web'?{partition,summary,checks,reportHashes:Object.fromEntries(['selection.json','discovery.json','web.xml','summary.json'].map(name=>[name,createHash('sha256').update(readFileSync(resolve(output,name))).digest('hex')]))}:{}),runtimePlatform:linux?linux.runtime:{platform:process.platform,architecture:process.arch},coldCacheRequested:!!plan.measurement?.coldCache,result:'Passed',productionQualified:false},null,2)+'\n');
} finally {try{linux?.close();}finally{if(coldPackages)rmSync(coldPackages,{recursive:true,force:true});}}
