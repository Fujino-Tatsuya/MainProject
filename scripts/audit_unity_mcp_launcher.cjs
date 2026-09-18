'use strict';
const fs=require('fs'),path=require('path'),{spawnSync}=require('child_process');
const out=path.join(process.cwd(),'output/unity-mcp-audit-2026-09-08');
const fixture=fs.mkdtempSync(path.join(out,'launcher-fixture-'));
fs.mkdirSync(path.join(fixture,'Packages'),{recursive:true});
const id='com.community.unity-mcp', pinned='a'.repeat(40),extra='b'.repeat(40);
fs.writeFileSync(path.join(fixture,'Packages/manifest.json'),JSON.stringify({dependencies:{[id]:'https://github.com/example/repo.git#'+pinned}}));
fs.writeFileSync(path.join(fixture,'Packages/packages-lock.json'),JSON.stringify({dependencies:{[id]:{version:'https://github.com/example/repo.git#'+pinned,source:'git',hash:pinned,depth:0}}}));
for(const [hash,epoch] of [[pinned,1700000000],[extra,1710000000]]){
 const dir=path.join(fixture,'Library/PackageCache',id+'@'+hash.slice(0,12),'Bridge');fs.mkdirSync(dir,{recursive:true});
 const file=path.join(dir,'mcp-bridge.js');fs.writeFileSync(file,'console.log('+JSON.stringify(hash)+');');fs.utimesSync(file,epoch,epoch);
}
const launcher='C:/Users/user/Projects/unity-mcp-launcher/mcp-bridge-launcher.js';
const r=spawnSync(process.execPath,[launcher],{cwd:fixture,encoding:'utf8',timeout:5000});
const result={pinned,selected:r.stdout.trim(),correct:r.stdout.trim()===pinned,exitCode:r.status,stderr:r.stderr,fixture};
fs.writeFileSync(path.join(out,'launcher-results.json'),JSON.stringify(result,null,2));console.log(JSON.stringify(result,null,2));
