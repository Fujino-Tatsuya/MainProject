'use strict';
const fs=require('fs'),path=require('path'),http=require('http'),readline=require('readline');
const {spawn}=require('child_process');
const root=process.cwd(),out=path.join(root,'output/unity-mcp-audit-2026-09-08');
const bridge=path.join(root,'Library/PackageCache/com.community.unity-mcp@85f6c175c082/Bridge/mcp-bridge.js');
fs.mkdirSync(out,{recursive:true});
const sleep=ms=>new Promise(r=>setTimeout(r,ms));
async function probe(name,body,deadline=2000,eof=false){
 const sockets=new Set(); let requests=0;const timers=[];
 const server=http.createServer((req,res)=>{
  if(req.url==='/sse'){res.writeHead(200,{'Content-Type':'text/event-stream'});res.write(': connected\n\n');return;}
  let data='';req.on('data',c=>data+=c);req.on('end',()=>{
   const r=JSON.parse(data);if(r.id==='__hb'){res.end(JSON.stringify({jsonrpc:'2.0',id:r.id,result:{}}));return;}
   requests++;body(r,res,timers);
  });
 });server.on('connection',s=>{sockets.add(s);s.on('close',()=>sockets.delete(s));});
 await new Promise(r=>server.listen(0,'127.0.0.1',r));
 const child=spawn(process.execPath,[bridge],{cwd:root,env:{...process.env,USERPROFILE:path.join(out,'transport-profile'),UNITY_MCP_PROJECT:root,UNITY_MCP_HOST:'127.0.0.1',UNITY_MCP_PORT:String(server.address().port)},stdio:['pipe','pipe','pipe']});
 let stderr='',exited=false;child.stderr.on('data',c=>stderr+=c);child.on('exit',()=>exited=true);
 const messages=[];const start=performance.now();readline.createInterface({input:child.stdout}).on('line',line=>{let json;try{json=JSON.parse(line);}catch{}messages.push({ms:Math.round(performance.now()-start),raw:line,json});});
 child.stdin.write(JSON.stringify({jsonrpc:'2.0',id:37,method:'tools/call',params:{name:'audit_mutation',arguments:{}}})+'\n');
 if(eof)child.stdin.end();
 for(let t=0;t<deadline;t+=50){await sleep(50);if(!eof&&messages.length)break;}
 const result={name,requests,elapsedMs:Math.round(performance.now()-start),exited,messages,correlated:messages.some(m=>m.json?.id===37),stderr};
 child.kill();for(const t of timers)clearInterval(t);for(const s of sockets)s.destroy();await new Promise(r=>server.close(r));return result;
}
(async()=>{
 const cases=[
 ['valid',(r,s)=>s.end(JSON.stringify({jsonrpc:'2.0',id:r.id,result:{ok:true}}))],
 ['idless-error',(r,s)=>s.end(JSON.stringify({jsonrpc:'2.0',id:null,error:{code:-32000,message:'test'}}))],
 ['wrong-id',(r,s)=>s.end(JSON.stringify({jsonrpc:'2.0',id:999,result:{ok:true}}))],
 ['stringified-id',(r,s)=>s.end(JSON.stringify({jsonrpc:'2.0',id:String(r.id),result:{ok:true}}))],
 ['malformed-json',(r,s)=>s.end('{broken')],
 ['idless-success',(r,s)=>s.end(JSON.stringify({jsonrpc:'2.0',id:null,result:{ok:true}}))],
 ['empty',(r,s)=>s.end('')],
 ['reset-mutation',(r,s)=>s.destroy()],
 ['stdin-eof',(r,s)=>s.end(JSON.stringify({jsonrpc:'2.0',id:r.id,result:{ok:true}})),1500,true],
 ['slow-trickle',(r,s,t)=>{s.writeHead(200,{'Content-Type':'application/json'});s.write(' ');t.push(setInterval(()=>s.write(' '),1000));},48000],
 ];const results=[];
 for(const c of cases){const r=await probe(...c);results.push(r);console.log(JSON.stringify({...r,stderr:undefined}));}
 fs.writeFileSync(path.join(out,'transport-results.json'),JSON.stringify({at:new Date().toISOString(),bridge,results},null,2));
})().catch(e=>{console.error(e);process.exitCode=1;});
