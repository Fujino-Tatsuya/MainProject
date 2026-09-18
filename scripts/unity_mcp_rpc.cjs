'use strict';
// Authenticated loopback diagnostic client. Credentials never enter output files.
const fs=require('node:fs'),os=require('node:os'),path=require('node:path'),http=require('node:http');
async function rpc(method,params){
 const auth=JSON.parse(fs.readFileSync(path.join(os.homedir(),'.unity-mcp/auth-token-3000.json'),'utf8'));
 if(path.resolve(auth.projectRoot)!==process.cwd())throw Error('Wrong Unity project');
 const id='audit-'+Date.now(),body=JSON.stringify({jsonrpc:'2.0',id,method,params}),start=performance.now();
 return new Promise((resolve,reject)=>{
  const req=http.request({hostname:'::1',port:3000,path:'/message',method:'POST',headers:{Host:'localhost:3000','Content-Type':'application/json','Content-Length':Buffer.byteLength(body),'X-Unity-MCP-Token':auth.token}},res=>{
   let text='';res.on('data',c=>{text+=c;if(text.length>8*1024*1024)req.destroy(Error('Response limit'));});res.on('error',reject);
   res.on('end',()=>{try{const response=JSON.parse(text);if(response.id!==id)throw Error('Uncorrelated response');let value;try{value=JSON.parse(response.result.content[0].text);}catch{}resolve({ms:Math.round(performance.now()-start),httpStatus:res.statusCode,response,value});}catch(e){reject(e);}});
  });const deadline=setTimeout(()=>req.destroy(Error('RPC deadline')),45000);req.on('close',()=>clearTimeout(deadline));req.on('error',reject);req.end(body);
 });
}
module.exports={rpc};
if(require.main===module)rpc(process.argv[2]||'ping',process.argv[3]?JSON.parse(process.argv[3]):undefined).then(r=>{console.log(JSON.stringify(r,null,2));if(r.response.error||r.response.result?.isError)process.exitCode=1;},e=>{console.error(e.message);process.exitCode=1;});
