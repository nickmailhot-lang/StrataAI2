import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, writeFileSync, mkdirSync, readFileSync, rmSync, chmodSync, symlinkSync } from 'node:fs';
import { execFileSync, spawnSync } from 'node:child_process';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { pathToFileURL } from 'node:url';
const source = resolve('scripts/local');
function fixture() {
  const root = mkdtempSync(join(tmpdir(), 'strataai local workflow '));
  const git = (...args) => execFileSync('git', args, { cwd: root, encoding: 'utf8', stdio: ['ignore','pipe','pipe'] }).trim();
  git('init', '-b', 'main'); git('config','user.name','Local fixture'); git('config','user.email','fixture@example.test');
  mkdirSync(join(root,'tests/browser'), { recursive: true });
  writeFileSync(join(root,'tests/browser/board.spec.ts'), '// baseline\n');
  writeFileSync(join(root,'README.md'), 'baseline\n'); git('add','.'); git('commit','-m','fixture baseline');
  git('branch','origin/main'); git('switch','-c','feature/fixture');
  const run = (file, args = [], input) => spawnSync(process.execPath, [join(source,file), ...args], { cwd: root, encoding: 'utf8', input });
  const script = code => spawnSync(process.execPath,['--input-type=module','-e',code], {cwd:root,encoding:'utf8'});
  const invoke = (file, expression) => {
    const r=script(`import * as m from ${JSON.stringify(pathToFileURL(join(source,file)).href)}; console.log(JSON.stringify(${expression}));`);
    assert.equal(r.status,0,r.stderr); return JSON.parse(r.stdout);
  };
  return {root,git,run,script,invoke,cleanup:()=>rmSync(root,{recursive:true,force:true})};
}
test('local impact covers committed, staged, unstaged, untracked, removed and both rename paths',()=>{
  const f=fixture(); try {
    writeFileSync(join(f.root,'committed.txt'),'1');f.git('add','.');f.git('commit','-m','branch change');
    writeFileSync(join(f.root,'staged.txt'),'2'); f.git('add','.');
    writeFileSync(join(f.root,'README.md'),'unstaged');writeFileSync(join(f.root,'untracked.ts'),'3');
    f.git('mv','tests/browser/board.spec.ts','tests/browser/renamed.spec.ts');
    const p=f.invoke('repository.mjs','m.localPlan("origin/main")');
    // Missing immutable baseline must refuse, rather than silently filtering it.
    assert.fail(`Unexpected plan ${p}`);
  } catch(e) { assert.match(e.message,/Missing baseline browser file/); } finally {f.cleanup();}
  const g=fixture();try {
    writeFileSync(join(g.root,'committed.txt'),'1');g.git('add','.');g.git('commit','-m','branch change');
    writeFileSync(join(g.root,'staged.txt'),'2');g.git('add','.');
    writeFileSync(join(g.root,'README.md'),'unstaged');writeFileSync(join(g.root,'untracked.ts'),'3');
    const p=g.invoke('repository.mjs','m.localPlan("origin/main")');
    assert.deepEqual(p.changedPaths,['README.md','committed.txt','staged.txt','untracked.ts']); assert.equal(p.full,true);
    assert.notEqual(p.tree,g.git('write-tree')); assert.equal(g.git('status','--porcelain').includes('untracked.ts'),true);
  }finally{g.cleanup();}
});
test('snapshot preserves the real index and detects content and executable-mode changes',()=>{
  const f=fixture();try{
    const before=f.git('write-tree'); writeFileSync(join(f.root,'new.txt'),'new');
    const first=f.invoke('repository.mjs','m.snapshot(process.cwd())'); assert.equal(f.git('write-tree'),before);
    writeFileSync(join(f.root,'new.txt'),'edited');assert.notEqual(f.invoke('repository.mjs','m.snapshot(process.cwd())'),first);
    assert.notEqual(first,before);
    writeFileSync(join(f.root,'new.txt'),'new');chmodSync(join(f.root,'new.txt'),0o755);assert.notEqual(f.invoke('repository.mjs','m.snapshot(process.cwd())'),first);
    writeFileSync(join(f.root,'.gitattributes'),'new.txt export-ignore\n');
    const copied=f.invoke('repository.mjs','m.materialize(m.snapshot(process.cwd()))');
    try { assert.equal(readFileSync(join(copied,'new.txt'),'utf8'),'new'); } finally { rmSync(copied,{recursive:true,force:true}); }
  }finally{f.cleanup();}
});
test('deletion and rename selection retain both paths without collapsing',()=>{
  const f=fixture();try{writeFileSync(join(f.root,'old.ts'),'same');f.git('add','.');f.git('commit','-m','old');f.git('branch','-f','origin/main','HEAD');f.git('mv','old.ts','new.ts');const p=f.invoke('repository.mjs','m.localPlan("origin/main")');assert.ok(p.changedPaths.includes('old.ts'));assert.ok(p.changedPaths.includes('new.ts'));assert.equal(p.full,true);}finally{f.cleanup();}
});
test('invalid base refuses selection instead of a successful empty plan',()=>{
  const f=fixture();try{ const r=f.script(`import {localPlan} from ${JSON.stringify(pathToFileURL(join(source,'repository.mjs')).href)}; localPlan('missing-base');`);assert.notEqual(r.status,0); }finally{f.cleanup();}
});
test('atomic locks reject another claimant and never steal an existing lock',()=>{
  const f=fixture();try{
    const r=f.script(`import {withLock,context} from ${JSON.stringify(pathToFileURL(join(source,'repository.mjs')).href)}; import {join} from 'node:path'; const p=join(context().state,'lock'); withLock(p,()=>withLock(p,()=>{}));`);
    assert.notEqual(r.status,0);assert.match(r.stderr,/EEXIST/);
  }finally{f.cleanup();}
});
test('two real worktrees claim independent files, refuse overlap/shared ownership, and retain handoff history',()=>{
  const f=fixture();let other;try{
    assert.equal(f.run('agents.mjs',['init','--owner','integrator']).status,0);
    assert.equal(f.run('agents.mjs',['claim','--owner','agent-a','--id','task-a','--paths','apps/web/src/features/alpha']).status,0);
    other=mkdtempSync(join(tmpdir(),'strataai worker b '));rmSync(other,{recursive:true});f.git('worktree','add','-b','feature/b',other);
    const run=(...args)=>spawnSync(process.execPath,[join(source,'agents.mjs'),...args],{cwd:other,encoding:'utf8'});
    assert.notEqual(run('claim','--owner','agent-b','--id','overlap','--paths','apps/web/src/features/alpha/Child.tsx').status,0);
    assert.notEqual(run('claim','--owner','agent-b','--id','case-overlap','--paths','apps/web/src/features/ALPHA').status,0);
    assert.notEqual(run('claim','--owner','agent-b','--id','shared','--paths','package.json').status,0);
    assert.equal(run('claim','--owner','agent-b','--id','task-b','--paths','apps/web/src/features/beta').status,0);
    assert.notEqual(run('handoff','--owner','agent-a','--id','task-b','--handoff','missing.json').status,0);
    writeFileSync(join(other,'handoff.json'),JSON.stringify({acceptance:['observable outcome'],evidence:['passing result'],limitations:[],dependencies:[]}));
    assert.equal(run('handoff','--owner','agent-b','--id','task-b','--handoff',join(other,'handoff.json')).status,0);
    assert.notEqual(run('release','--owner','agent-b','--id','task-b','--reason','done').status,0);
    assert.equal(run('release','--owner','integrator','--id','task-b','--reason','Reviewed explicit cancellation').status,0);
    const registry=JSON.parse(run('status').stdout);assert.equal(registry.tasks.length,2);assert.equal(registry.tasks[1].status,'released');
    assert.notEqual(registry.tasks[0].runtimeProject,registry.tasks[1].runtimeProject);
    assert.notEqual(registry.tasks[0].worktree,registry.tasks[1].worktree);
  }finally{if(other)rmSync(other,{recursive:true,force:true});f.cleanup();}
});
test('hooks reject absent/stale evidence, partial staging, untracked additions and base movement',()=>{
  const f=fixture();try{
    assert.notEqual(f.run('hooks.mjs',['pre-commit']).status,0);
    writeFileSync(join(f.root,'README.md'),'verified candidate');f.git('add','.');
    assert.equal(f.run('agents.mjs',['init','--owner','integrator']).status,0);
    assert.equal(f.run('agents.mjs',['claim','--owner','integrator','--id','hook-task','--paths','README.md']).status,0);
    const setup=`import {context,localPlan,atomicJson,hash} from ${JSON.stringify(pathToFileURL(join(source,'repository.mjs')).href)}; import {writeFileSync} from 'node:fs'; import {join} from 'node:path'; const c=context(),p=localPlan('origin/main'),log=join(c.state,'pass.log');writeFileSync(log,'pass');atomicJson(join(c.state,'verified-'+c.worktree+'.json'),{passed:true,mode:'final',tree:p.tree,baseRef:'origin/main',baseSha:p.baseSha,results:[{passed:true,log,sha256:hash('pass')}]});`;
    assert.equal(f.script(setup).status,0);assert.equal(f.run('hooks.mjs',['pre-commit']).status,0);
    writeFileSync(join(f.root,'README.md'),'after test');assert.notEqual(f.run('hooks.mjs',['pre-commit']).status,0);
    writeFileSync(join(f.root,'README.md'),'verified candidate');writeFileSync(join(f.root,'new.ts'),'untracked');assert.notEqual(f.run('hooks.mjs',['pre-commit']).status,0);
    rmSync(join(f.root,'new.ts')); f.git('commit','-m','verified');
    const head=f.git('rev-parse','HEAD');
    assert.equal(f.run('hooks.mjs',['pre-push'],`refs/heads/feature/fixture ${head} refs/heads/feature/fixture ${'0'.repeat(40)}\n`).status,0);
    assert.notEqual(f.run('hooks.mjs',['pre-push'],`refs/heads/main ${head} refs/heads/main ${'0'.repeat(40)}\n`).status,0);
    f.git('branch','-f','origin/main',head);assert.notEqual(f.run('hooks.mjs',['pre-commit']).status,0);
  }finally{f.cleanup();}
});
test('hooks installation preserves an existing custom hook configuration',()=>{
  const f=fixture();try{f.git('config','core.hooksPath','existing-hooks');assert.notEqual(f.run('hooks.mjs',['install']).status,0);assert.equal(f.git('config','--get','core.hooksPath'),'existing-hooks');}finally{f.cleanup();}
});

test('integration owner can claim GitHub/hooks files while .git internals remain forbidden',()=>{
 const f=fixture();try{assert.equal(f.run('agents.mjs',['init','--owner','integrator']).status,0);assert.notEqual(f.run('agents.mjs',['claim','--owner','integrator','--id','unsafe','--paths','.git/config']).status,0);assert.notEqual(f.run('agents.mjs',['claim','--owner','integrator','--id','unsafe-case','--paths','.GIT/config']).status,0);assert.equal(f.run('agents.mjs',['claim','--owner','integrator','--id','safe','--paths','.github,.githooks']).status,0);}finally{f.cleanup();}
});

test('verification ownership rejects unclaimed work and changes beyond the claim',()=>{
 const f=fixture();try{
   const call=()=>f.script(`import {assertOwnership} from ${JSON.stringify(pathToFileURL(join(source,'agents.mjs')).href)}; assertOwnership(['outside.ts']);`);
   assert.notEqual(call().status,0);
   assert.equal(f.run('agents.mjs',['init','--owner','integrator']).status,0);
   assert.equal(f.run('agents.mjs',['claim','--owner','integrator','--id','owned','--paths','README.md']).status,0);
   assert.notEqual(call().status,0);
 }finally{f.cleanup();}
});

test('local UUID adapter supplies fresh CSPRNG IDs and forwards ordinary cat reads unchanged',()=>{
 const f=fixture();try{
   const adapter=join(source,'bin/cat');const env={...process.env,STRATAAI_LOCAL_NODE:process.execPath};
   const first=spawnSync('bash',[adapter,'/proc/sys/kernel/random/uuid'],{encoding:'utf8',env});
   const second=spawnSync('bash',[adapter,'/proc/sys/kernel/random/uuid'],{encoding:'utf8',env});
   assert.equal(first.status,0);assert.match(first.stdout,/^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}\n$/);assert.notEqual(first.stdout,second.stdout);
   const ordinary=spawnSync('bash',[adapter,join(f.root,'README.md')],{encoding:'utf8',env});assert.equal(ordinary.status,0);assert.equal(ordinary.stdout,'baseline\n');
   assert.notEqual(spawnSync('bash',[adapter,'/proc/unsupported-local-fixture'],{env}).status,0);
 }finally{f.cleanup();}
});

test('evidence inventory hashes nested artifacts and refuses symbolic-link substitutions',()=>{
 const f=fixture();try{const folder=join(f.root,'evidence');mkdirSync(join(folder,'nested'),{recursive:true});writeFileSync(join(folder,'result.json'),'self');writeFileSync(join(folder,'runtime.env'),'synthetic-only');writeFileSync(join(folder,'log.txt'),'assertion log');writeFileSync(join(folder,'nested','screen.png'),Buffer.from([1,2,3]));
 const expression='m.evidenceInventory('+JSON.stringify(folder)+')';const entries=f.invoke('repository.mjs',expression);assert.equal(entries.length,2);assert.ok(entries.every(x=>/^[a-f0-9]{64}$/.test(x.sha256)));
 symlinkSync(join(f.root,'README.md'),join(folder,'external'));const call=f.script('import * as m from '+JSON.stringify(pathToFileURL(join(source,'repository.mjs')).href)+'; '+expression);assert.notEqual(call.status,0);
 }finally{f.cleanup();}
});

test('untracked mapped leaf source expands coverage before Git-based changed-test selection',()=>{
 const f=fixture();try{
  writeFileSync(join(f.root,'tests/browser/card-labels.spec.ts'),'// label contract\n');f.git('add','.');f.git('commit','-m','mapped browser baseline');f.git('branch','-f','origin/main','HEAD');
  mkdirSync(join(f.root,'apps/web/src/features/kanban'),{recursive:true});writeFileSync(join(f.root,'apps/web/src/features/kanban/CardLabelsControl.tsx'),'// new source\n');
  const p=f.invoke('repository.mjs','m.localPlan("origin/main")');assert.equal(p.full,true);assert.ok(p.reasons.some(x=>x.includes('Untracked source')));
 }finally{f.cleanup();}
});

test('runtime excludes ambient production provider/authority and Compose settings',()=>{
 const f=fixture();try{
  const clean=f.invoke('repository.mjs',`m.localRuntimeEnvironment({PATH:'/tool/bin',DOCKER_CONTEXT:'desktop-linux',STRATAAI_IDENTITY_EMAIL_ENABLED:'true',STRATAAI_IDENTITY_EMAIL_API_KEY:'synthetic-provider-key',STRATAAI_WORKER_ORGANIZATION_IDS:'foreign-scope',COMPOSE_FILE:'production.yml',COMPOSE_PROJECT_NAME:'production'})`);
  assert.deepEqual(clean,{PATH:'/tool/bin',DOCKER_CONTEXT:'desktop-linux'});
 }finally{f.cleanup();}
});
