import { execFileSync } from 'node:child_process';
import { randomUUID } from 'node:crypto';
import { rmSync } from 'node:fs';
import { materialize } from '../../scripts/local/repository.mjs';

// One owned SDK container for this source lane; no production runtime ports,
// shared build directory, global Docker cleanup or mutable host source mount.
export function linuxBackend(tree, output) {
  const label=randomUUID(), name=`strataai-fast-${label}`, source=materialize(tree);
  let created=false;
  const docker=args=>execFileSync('docker',args,{encoding:'utf8',timeout:30_000}).trim();
  const close=()=>{
    try {
      if (created) {
        if(docker(['inspect','--format','{{index .Config.Labels "strataai.fast.source"}}',name])!==label) throw new Error('Refuse cleanup of unowned SDK container');
        docker(['rm','-f',name]);created=false;
      }
    } finally {if(!created)rmSync(source,{recursive:true,force:true});}
  };
  try {
    docker(['run','-d','--name',name,'--label',`strataai.fast.source=${label}`,
      '--mount',`type=bind,source=${source},target=/candidate,readonly`,
      '--mount',`type=bind,source=${output},target=/results`,
      '-e','DOTNET_CLI_TELEMETRY_OPTOUT=1','-e','STRATAAI_POST_MVP_LOAD_TESTS=0','mcr.microsoft.com/dotnet/sdk:10.0','sleep','infinity']);created=true;
    docker(['exec',name,'bash','-ec','mkdir /tmp/source; cp -a /candidate/. /tmp/source/']);
    const image=docker(['inspect','--format','{{.Image}}',name]);
    const runtime={image,platform:docker(['image','inspect','--format','{{.Os}}/{{.Architecture}}',image]),sdk:docker(['exec','--workdir','/tmp/source',name,'dotnet','--version'])};
    return {close,runtime,wrap:(command,args)=>({command:'docker',args:['exec','--workdir','/tmp/source',name,command,...args.map(a=>a===output?'/results':a)]})};
  } catch(error){close();throw error;}
}
