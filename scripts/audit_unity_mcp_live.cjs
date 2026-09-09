'use strict';
const fs=require('fs'),path=require('path'),http=require('http'),os=require('os');
const {spawn}=require('child_process');const readline=require('readline');
const root=process.cwd(),out=process.env.AUDIT_OUTPUT||path.join(root,'output/unity-mcp-audit-2026-09-08');
fs.mkdirSync(out,{recursive:true});
const authFile=path.join(os.homedir(),'.unity-mcp','auth-token-3000.json');
const auth=JSON.parse(fs.readFileSync(authFile,'utf8'));
if(path.resolve(auth.projectRoot)!==root)throw Error('Wrong Unity project');
function rpc(host,id,method,params){return new Promise(resolve=>{
 const start=performance.now(),data=JSON.stringify({jsonrpc:'2.0',id,method,params});
 const req=http.request({hostname:host,port:3000,path:'/message',method:'POST',headers:{Host:'localhost:3000','Content-Type':'application/json','Content-Length':Buffer.byteLength(data),'X-Unity-MCP-Token':auth.token}},res=>{
  let text='';res.on('data',c=>text+=c);res.on('end',()=>{let json;try{json=JSON.parse(text);}catch{}resolve({host,ms:Math.round(performance.now()-start),httpStatus:res.statusCode,bytes:Buffer.byteLength(text),json});});res.on('error',e=>resolve({host,error:e.message}));
 });req.on('error',e=>resolve({host,error:e.code}));req.setTimeout(6000,()=>req.destroy(Error('audit timeout')));req.end(data);
});}
(async()=>{
 const result={at:new Date().toISOString(),projectRoot:root};
 result.discovery=await Promise.all(['127.0.0.1','::1'].map(h=>rpc(h,'audit-ping','ping')));
 const host=result.discovery.find(x=>x.httpStatus===200)?.host;
 if(host){
  result.tools=await rpc(host,'audit-tools','tools/list');
  fs.writeFileSync(path.join(out,'live-tools.json'),JSON.stringify(result.tools.json,null,2));
  const available=result.tools.json?.result?.tools||[];
  result.checks=[];
  for(const name of ['unity_get_editor_state','unity_get_compilation_status','unity_get_job_status']){
   const def=available.find(t=>t.name===name);if(!def)continue;
   result.checks.push({name,...await rpc(host,name,'tools/call',{name,arguments:{}})});
  }
 }
 const profile=path.join(out,'live-profile');fs.mkdirSync(path.join(profile,'.unity-mcp'),{recursive:true});
 // Preload credentials only in memory, keeping authentication material out of audit artifacts.
 const preload=path.join(out,'live-preload.cjs');
 fs.writeFileSync(preload,"const fs=require('fs'),os=require('os'),path=require('path');const original=fs.readFileSync;fs.readFileSync=function(p,...a){if(String(p)===path.join(os.homedir(),'.unity-mcp','auth-token-3000.json'))return original.call(this,"+JSON.stringify(authFile)+",...a);return original.call(this,p,...a);};");
 const launcher=process.env.AUDIT_PACKAGE?path.join(process.env.AUDIT_PACKAGE,'Bridge/mcp-bridge.js'):JSON.parse(fs.readFileSync(path.join(root,'.mcp.json'),'utf8')).mcpServers.unity.args[0];
 const child=spawn(process.execPath,['--require',preload,launcher],{cwd:root,env:{...process.env,USERPROFILE:profile,UNITY_MCP_PROJECT:root},stdio:['pipe','pipe','pipe']});
 let stderr='',messages=[];child.stderr.on('data',c=>stderr+=c);const start=performance.now();
 const waiters=new Map();readline.createInterface({input:child.stdout}).on('line',line=>{try{const r=JSON.parse(line);messages.push({id:r.id,ms:Math.round(performance.now()-start),bytes:Buffer.byteLength(line)});waiters.get(r.id)?.(r);}catch{}});
 async function call(id,method,params){const t=performance.now();let timer;const r=await new Promise(resolve=>{waiters.set(id,resolve);timer=setTimeout(()=>resolve({auditTimeout:true}),18000);child.stdin.write(JSON.stringify({jsonrpc:'2.0',id,method,params})+'\n');});clearTimeout(timer);waiters.delete(id);return {ms:Math.round(performance.now()-t),response:r};}
 result.stdio=[];
 result.stdio.push(await call('init','initialize',{protocolVersion:'2024-11-05',capabilities:{},clientInfo:{name:'read-only-audit',version:'1'}}));
 child.stdin.write(JSON.stringify({jsonrpc:'2.0',method:'notifications/initialized'})+'\n');
 const list=await call('tools','tools/list');
 fs.writeFileSync(path.join(out,'stdio-tools.json'),JSON.stringify(list.response,null,2));
 result.stdio.push({name:'tools/list',ms:list.ms,count:list.response.result?.tools?.length,wireBytes:Buffer.byteLength(JSON.stringify(list.response)),timeout:!!list.response.auditTimeout});
 result.stdio.push({name:'editor',...await call('editor','tools/call',{name:'unity_get_editor_state',arguments:{}})});
 result.local=[];
 for(const [name,args] of [['unity_project_map',{budgetTokens:6000}],['unity_impact_analysis',{target:'Hurtbox'}],['unity_index_status',{}],['unity_explain_compile_errors',{errors:[{file:'Assets/__McpAuditSyntheticDiagnostic.cs',line:1,message:'Synthetic diagnostic for freshness contract; not a real Unity error'}],hadErrors:false,compilationGeneration:0}]]){
  const r=await call(name,'tools/call',{name,arguments:args});
  fs.writeFileSync(path.join(out,'local-'+name+'.json'),JSON.stringify(r.response,null,2));
  let value;try{value=JSON.parse(r.response.result.content[0].text);}catch{}
  const contractOk=name!=='unity_explain_compile_errors'||(value?.freshness?.state==='unknown'&&value?.freshness?.reportedHasErrors===false);
  result.local.push({name,ms:r.ms,bytes:Buffer.byteLength(JSON.stringify(r.response)),ok:!!value&&!value.error&&!r.response.error&&!r.response.result?.isError&&contractOk,summary:value?.summary||value?.freshness||value?.stats||null});
 }
 child.stdin.end();result.launcherLog=stderr;result.messages=messages;
 result.ok=!!host && !list.response.error && !list.response.auditTimeout && Array.isArray(list.response.result?.tools) && result.stdio.every(x=>!x.response?.error&&!x.response?.auditTimeout&&!x.response?.result?.isError) && result.local.every(x=>x.ok);
 process.exitCode=result.ok?0:1;
 fs.writeFileSync(path.join(out,'live-results.json'),JSON.stringify(result,null,2));
 console.log(JSON.stringify({...result,tools:result.tools?{ms:result.tools.ms,count:result.tools.json?.result?.tools?.length,bytes:result.tools.bytes}:null},null,2));
})().catch(e=>{console.error(e);process.exitCode=1;});
