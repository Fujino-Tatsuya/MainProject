'use strict';
// Read-only audit of the pinned package. All generated data stays under output/.
const fs = require('fs');
const path = require('path');
const {spawnSync} = require('child_process');
const root = process.cwd();
const pkg = process.env.AUDIT_PACKAGE || path.join(root, 'Library/PackageCache/com.community.unity-mcp@85f6c175c082');
const out = process.env.AUDIT_OUTPUT || path.join(root, 'output/unity-mcp-audit-2026-09-08');
const profile = path.join(out, 'profile');
fs.mkdirSync(profile, {recursive:true});
const env = {...process.env, USERPROFILE:profile, UNITY_MCP_PROJECT:root, PROBE_PROFILE:profile};
const results = [];
for (const name of fs.readdirSync(path.join(pkg,'Tools')).filter(n=>/^probe-.*\.js$/.test(n))) {
  const start = performance.now();
  const r = spawnSync(process.execPath,[path.join(pkg,'Tools',name)],{env,encoding:'utf8',timeout:120000,maxBuffer:8*1024*1024});
  fs.writeFileSync(path.join(out,name+'.log'),(r.stdout||'')+(r.stderr||''));
  const result={name,exitCode:r.status,ms:Math.round(performance.now()-start),error:r.error?.message,
    summary:(r.stdout||'').split(/\r?\n/).filter(x=>/FAIL|passed|failed|pass.*fail|PASS|budget|\/7/.test(x)).slice(-12)};
  results.push(result); console.log(JSON.stringify(result));
}
fs.writeFileSync(path.join(out,'suite-results.json'),JSON.stringify({at:new Date().toISOString(),root,pkg,node:process.version,results},null,2));
process.exitCode = results.some(r => r.exitCode !== 0) ? 1 : 0;
