'use strict';
// Reproduce the candidate checks without changing Unity Assets or the installed package.
const fs = require('fs');
const path = require('path');
const { spawnSync } = require('child_process');
const pkg = path.resolve(process.argv[2] || process.env.AUDIT_PACKAGE || '');
if (!process.argv[2] && !process.env.AUDIT_PACKAGE) throw Error('Pass candidate package root');
const out = path.resolve(process.env.AUDIT_OUTPUT || 'output/unity-mcp-audit-2026-09-09-fixed');
fs.mkdirSync(out, { recursive: true });
const env = { ...process.env, AUDIT_PACKAGE: pkg, AUDIT_OUTPUT: out, UNITY_MCP_TEST_OUTPUT: out };
const results = [];
for (const name of ['transport-regression.cjs', 'index-freshness-regression.cjs', 'launcher-regression.cjs', 'probe-support-regression.cjs', 'diagnostic-freshness-regression.cjs', 'legacy-suites']) {
  const script = name === 'legacy-suites' ? path.resolve('scripts/audit_unity_mcp.cjs') : path.join(pkg, 'Tools/verify', name);
  const started = performance.now();
  const r = spawnSync(process.execPath, [script], { cwd: process.cwd(), env, encoding: 'utf8', timeout: 240000, maxBuffer: 8 * 1024 * 1024 });
  fs.writeFileSync(path.join(out, name + '.log'), (r.stdout || '') + (r.stderr || ''));
  const result = { name, exitCode: r.status, error: r.error?.message, ms: Math.round(performance.now() - started) };
  results.push(result);
  console.log(JSON.stringify(result));
}
fs.writeFileSync(path.join(out, 'regression-results.json'), JSON.stringify({ at: new Date().toISOString(), node: process.version, pkg, results }, null, 2));
process.exitCode = results.some(r => r.exitCode !== 0) ? 1 : 0;
