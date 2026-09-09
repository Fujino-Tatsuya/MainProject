'use strict';
const fs=require('fs'),path=require('path'),{spawn}=require('child_process'),readline=require('readline');
const root=process.cwd(),out=process.env.AUDIT_OUTPUT||path.join(root,'output/unity-mcp-audit-2026-09-08');
const bridge=process.env.AUDIT_PACKAGE?path.join(process.env.AUDIT_PACKAGE,'Bridge/mcp-bridge.js'):path.join(root,'Library/PackageCache/com.community.unity-mcp@85f6c175c082/Bridge/mcp-bridge.js');
const child=spawn(process.execPath,['--require',path.join(out,'live-preload.cjs'),bridge],{cwd:root,env:{...process.env,USERPROFILE:path.join(out,'live-profile'),UNITY_MCP_PROJECT:root,UNITY_MCP_HOST:'::1'},stdio:['pipe','pipe','pipe']});
const waiters=new Map(),records=[];let serial=0,stderr='';child.stderr.on('data',c=>stderr+=c);
readline.createInterface({input:child.stdout}).on('line',line=>{try{const r=JSON.parse(line);waiters.get(r.id)?.(r);}catch{}});
const sleep=ms=>new Promise(r=>setTimeout(r,ms));
async function rpc(method,params){const id='play-audit-'+(++serial),start=performance.now();let timer;const response=await new Promise(resolve=>{waiters.set(id,resolve);timer=setTimeout(()=>resolve({auditTimeout:true}),55000);child.stdin.write(JSON.stringify({jsonrpc:'2.0',id,method,params})+'\n');});clearTimeout(timer);waiters.delete(id);const record={id,method,name:params?.name,ms:Math.round(performance.now()-start),response};records.push(record);return record;}
async function call(name,args={}){const r=await rpc('tools/call',{name,arguments:args});let value;try{value=JSON.parse(r.response.result.content[0].text);}catch{}return {...r,value};}
async function transition(name,expected){const start=performance.now(),ack=await call(name);console.log(JSON.stringify({phase:name,ack}));if(!ack.value?.jobId)throw Error(name+' did not return jobId');
 let last;
 while(performance.now()-start<90000){await sleep(1500);last=await call('unity_get_job_status',{jobId:ack.value.jobId});if(last.value?.done||['done','failed'].includes(last.value?.status))break;}
 const state=await call('unity_get_editor_state');const ok=last?.value?.status==='done' && (expected===undefined||state.value?.isPlaying===expected);
 const result={name,jobId:ack.value.jobId,ackMs:ack.ms,totalMs:Math.round(performance.now()-start),job:last?.value,state:state.value,ok};console.log(JSON.stringify(result));return result;
}
(async()=>{const result={at:new Date().toISOString(),bridge,cycles:[]};let fixture=null;try{
 await rpc('initialize',{protocolVersion:'2024-11-05',capabilities:{},clientInfo:{name:'authorized-play-audit',version:'1'}});child.stdin.write(JSON.stringify({jsonrpc:'2.0',method:'notifications/initialized'})+'\n');
 await rpc('tools/list');
 const before=await call('unity_get_editor_state');result.before=before.value;if(before.value?.isPlaying!==false||before.value?.isCompiling)throw Error('Editor must start idle in edit mode');
 result.consoleBefore=(await call('unity_get_console_logs',{type:'Error',count:100})).value;
 for(let i=0;i<2;i++){
  const entered=await transition('unity_enter_play_mode',true);result.cycles.push(entered);if(!entered.ok)throw Error('Play entry failed');
  await sleep(2500);const exited=await transition('unity_exit_play_mode',false);result.cycles.push(exited);if(!exited.ok)throw Error('Play exit failed');
 }
 if(process.env.AUDIT_FORCE_COMPILE_FIXTURE==='1'){
  const name='McpAudit_'+require('crypto').randomBytes(6).toString('hex');
  fixture={name,dir:path.join(root,'Assets',name),dll:path.join(root,'Library/ScriptAssemblies',name+'.dll')};
  if(fs.existsSync(fixture.dir)||fs.existsSync(fixture.dll))throw Error('Fixture already exists');
  fs.mkdirSync(fixture.dir);
  fs.writeFileSync(path.join(fixture.dir,name+'.asmdef'),JSON.stringify({name,includePlatforms:['Editor'],autoReferenced:false}));
  fs.writeFileSync(path.join(fixture.dir,'VerificationMarker.cs'),'namespace '+name+' { internal static class VerificationMarker { public const int Value = 1; } }\n');
  result.fixture={name,createdAt:new Date().toISOString()};
 }
 result.compile=await transition('unity_recompile_scripts');result.compilation=(await call('unity_get_compilation_status')).value;
 if(!result.compile.ok)throw Error('Recompile job failed');
 if(fixture){
  result.fixture.assemblyCreated=fs.existsSync(fixture.dll);
  if(!result.fixture.assemblyCreated)throw Error('No actual verification assembly generated');
  result.fixture.assemblyBytes=fs.statSync(fixture.dll).size;
 }
 }catch(e){result.error=e.message;console.log(JSON.stringify({error:e.message}));}finally{
  const state=await call('unity_get_editor_state');if(state.value?.isPlaying)result.restore=await transition('unity_exit_play_mode',false);
  if(fixture&&fs.existsSync(fixture.dir)){
   const real=fs.realpathSync(fixture.dir),assets=fs.realpathSync(path.join(root,'Assets'));
   if(path.dirname(real)!==assets||path.basename(real)!==fixture.name)throw Error('Unsafe fixture cleanup path');
   const owned=new Set([fixture.name+'.asmdef',fixture.name+'.asmdef.meta','VerificationMarker.cs','VerificationMarker.cs.meta']);
   if(fs.readdirSync(real).some(n=>!owned.has(n)))throw Error('Unexpected file in fixture; preserving it');
   fs.rmSync(real,{recursive:true});if(fs.existsSync(real+'.meta'))fs.unlinkSync(real+'.meta');
   result.cleanupCompile=await transition('unity_recompile_scripts');
   result.fixture.assemblyRemoved=!fs.existsSync(fixture.dll);
   if(!result.cleanupCompile.ok||!result.fixture.assemblyRemoved)result.error=result.error||'Fixture cleanup compilation incomplete';
  }
  result.consoleAfter=(await call('unity_get_console_logs',{type:'Error',count:100})).value;
  result.after=(await call('unity_get_editor_state')).value;child.stdin.end();
  fs.writeFileSync(path.join(out,'play-results.json'),JSON.stringify({...result,records,stderr},null,2));console.log(JSON.stringify({after:result.after,error:result.error,compile:result.compile,fixture:result.fixture}));
  process.exitCode=result.error||result.after?.isPlaying!==false?1:0;
 }})();
