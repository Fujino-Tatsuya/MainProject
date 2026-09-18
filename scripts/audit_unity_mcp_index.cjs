'use strict';
const fs=require('fs'),path=require('path');const {spawnSync}=require('child_process');
const root=process.cwd(),out=process.env.AUDIT_OUTPUT||path.join(root,'output/unity-mcp-audit-2026-09-08');
const pkg=process.env.AUDIT_PACKAGE||path.join(root,'Library/PackageCache/com.community.unity-mcp@85f6c175c082');
process.env.USERPROFILE=path.join(out,'benchmark-profile');process.env.UNITY_MCP_PROJECT=root;
const tools=require(path.join(pkg,'Bridge/index/tools')),scan=require(path.join(pkg,'Bridge/index/scan'));
const logs=[];tools.setLogger(s=>logs.push(s));
const result={at:new Date().toISOString(),samples:[]};
function measure(label,fn){const start=performance.now(),value=fn();const row={label,ms:Math.round(performance.now()-start),bytes:Buffer.byteLength(JSON.stringify(value))};result.samples.push(row);return value;}
let idx=measure('cold-full-build',()=>tools.ensureIndex(3000,true,false));result.stats=idx.stats;
function call(name,args){return tools.callLocalTool(name,args,3000);}
for(const budgetTokens of [2000,4000,6000,8000,10000]){
 const r=measure('map-'+budgetTokens,()=>call('unity_project_map',{budgetTokens}));fs.writeFileSync(path.join(out,'map-'+budgetTokens+'.json'),JSON.stringify(r));
}
for(let i=0;i<10;i++)measure('warm-map-6000',()=>call('unity_project_map',{budgetTokens:6000}));
for(const [name,args] of [
 ['unity_impact_analysis',{target:'Hurtbox'}],['unity_impact_analysis',{target:'Assets/50.Art/TestAssets/TestPlayerAsset/SkillUI/SkillRange.png'}],
 ['unity_find_missing_scripts',{maxResults:200}],['unity_index_status',{}]]){
 const r=measure(name,()=>call(name,args));fs.writeFileSync(path.join(out,name+(args.target==='Hurtbox'?'-hurtbox':'')+'.json'),JSON.stringify(r));
}
// Independent on-disk rename fixture: same files, timestamps and sizes; only path changes.
const fixture=path.join(out,'rename-fixture');fs.mkdirSync(path.join(fixture,'Assets'),{recursive:true});fs.mkdirSync(path.join(fixture,'Packages'),{recursive:true});fs.mkdirSync(path.join(fixture,'ProjectSettings'),{recursive:true});
const original=path.join(fixture,'Assets/Before.asset'),renamed=path.join(fixture,'Assets/After_.asset');
fs.writeFileSync(original,'%YAML 1.1\n--- !u!114 &1\nMonoBehaviour:\n  m_Name: Example\n');
fs.writeFileSync(original+'.meta','fileFormatVersion: 2\nguid: 0123456789abcdef0123456789abcdef\n');
const before=scan.fingerprint(fixture);fs.renameSync(original,renamed);fs.renameSync(original+'.meta',renamed+'.meta');const after=scan.fingerprint(fixture);
result.rename={before,after,detected:before.hash!==after.hash||before.totalBytes!==after.totalBytes||before.metaFiles!==after.metaFiles||before.yamlFiles!==after.yamlFiles};
// Restore fixture filenames to allow repeat runs.
fs.renameSync(renamed,original);fs.renameSync(renamed+'.meta',original+'.meta');
// A new bridge process must invalidate a persisted graph if only a DLL changed.
const coldFixture=path.join(out,'assembly-fixture');fs.mkdirSync(path.join(coldFixture,'Assets'),{recursive:true});fs.mkdirSync(path.join(coldFixture,'Library/ScriptAssemblies'),{recursive:true});
const dll=path.join(coldFixture,'Library/ScriptAssemblies/Assembly-CSharp.dll');fs.writeFileSync(dll,'not a managed DLL: fixture version A');
const fixtureEnv={...process.env,UNITY_MCP_PROJECT:coldFixture};
const snippet="const t=require(process.argv[1]);t.setLogger(console.error);const i=t.ensureIndex(3000,false,false);console.log(JSON.stringify(t._freshness()));";
function freshProcess(){const r=spawnSync(process.execPath,['-e',snippet,path.join(pkg,'Bridge/index/tools')],{env:fixtureEnv,encoding:'utf8',timeout:30000});return {exitCode:r.status,stdout:r.stdout,stderr:r.stderr};}
result.assemblyCache={first:freshProcess()};fs.appendFileSync(dll,' changed size and timestamp');result.assemblyCache.second=freshProcess();
result.logs=logs;fs.writeFileSync(path.join(out,'index-results.json'),JSON.stringify(result,null,2));console.log(JSON.stringify({...result,logs:undefined},null,2));
