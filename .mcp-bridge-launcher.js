#!/usr/bin/env node
// Unity MCP 브릿지 런처.
//
// 왜 필요한가:
//   설정이 브릿지 경로를 직접 가리키면, 패키지 커밋 핀을 바꿀 때마다 PackageCache 폴더명
//   (com.community.unity-mcp@<해시 12자>)이 함께 바뀌어 경로가 조용히 깨진다.
//   Unity는 새 폴더를 만들며 구 폴더를 지우므로 증상은 "MCP 서버를 못 찾음"으로만 보이고,
//   원인이 패키지 업데이트라는 게 드러나지 않는다.
//   홈에 브릿지 사본을 두는 우회는 그 사본이 낡아도 아무도 모른다는 더 나쁜 문제를 만든다
//   (실제로 2026-08-05에 7/19자 홈 사본이 도는 걸 발견).
//   이 런처는 실행 시점에 실제 브릿지를 찾으므로 두 문제를 모두 없앤다.
//
// 등록 예 (~/.claude.json 전역 또는 프로젝트 .mcp.json):
//   { "mcpServers": { "unity": {
//       "command": "node",
//       "args": ["C:\\Users\\user\\MainProject\\.mcp-bridge-launcher.js"],
//       "timeout": 120000 } } }

const fs = require('fs');
const path = require('path');

const PACKAGE_ID = 'com.community.unity-mcp';
const BRIDGE_REL = path.join('Bridge', 'mcp-bridge.js');

function log(msg) {
    // stdout은 JSON-RPC 전용이므로 로그는 반드시 stderr로.
    console.error(`[Unity MCP Launcher] ${msg}`);
}

// 탐색 기준 디렉터리.
// 1순위 cwd — 이 런처를 전역 등록하면 다른 Unity 프로젝트에서도 실행되는데,
//            그때는 그 프로젝트가 쓰는 패키지 버전의 브릿지를 쓰는 게 맞다.
// 2순위 __dirname — 런처가 놓인 프로젝트(폴백). cwd에 패키지가 없는 경우를 덮는다.
//            브릿지는 localhost 포트로 Unity에 붙는 클라이언트라 프로젝트가 달라도 동작한다.
function searchRoots() {
    const roots = [process.cwd()];
    if (path.resolve(__dirname) !== path.resolve(process.cwd())) {
        roots.push(__dirname);
    }
    return roots;
}

function resolveBridgeIn(projectRoot) {
    // 1) 임베드 패키지(Packages/)가 있으면 최우선 — 패키지를 직접 고쳐가며 개발할 때 이쪽이 정본이다.
    const embedded = path.join(projectRoot, 'Packages', PACKAGE_ID, BRIDGE_REL);
    if (fs.existsSync(embedded)) {
        return { bridge: embedded, source: `${projectRoot} > Packages/ (embedded)` };
    }

    // 2) PackageCache의 해시 폴더. 구/신 버전이 함께 남는 경우가 있어 가장 최근 것을 쓴다.
    const cacheDir = path.join(projectRoot, 'Library', 'PackageCache');
    let entries;
    try {
        entries = fs.readdirSync(cacheDir);
    } catch (e) {
        return { error: `${cacheDir} — 읽을 수 없음 (${e.code || e.message})` };
    }

    const candidates = entries
        .filter(name => name === PACKAGE_ID || name.startsWith(PACKAGE_ID + '@'))
        .map(name => path.join(cacheDir, name, BRIDGE_REL))
        .filter(p => fs.existsSync(p))
        .map(p => ({ p, mtime: fs.statSync(p).mtimeMs }))
        .sort((a, b) => b.mtime - a.mtime);

    if (candidates.length === 0) {
        return { error: `${cacheDir}\\${PACKAGE_ID}@* — 브릿지 없음` };
    }

    if (candidates.length > 1) {
        log(`브릿지 후보 ${candidates.length}개 발견 — 가장 최근 것을 사용합니다.`);
    }

    return { bridge: candidates[0].p, source: `${projectRoot} > Library/PackageCache` };
}

let resolved = null;
const tried = [];

for (const root of searchRoots()) {
    const result = resolveBridgeIn(root);
    if (result.bridge) {
        resolved = result;
        break;
    }
    tried.push(result.error);
}

if (!resolved) {
    log(`${PACKAGE_ID}의 브릿지를 찾지 못했습니다. 확인한 곳:`);
    tried.forEach(e => log(`  - ${e}`));
    log('Unity 에디터를 한 번 열어 패키지를 받게 한 뒤 다시 시도하세요.');
    log('MCP 서버를 시작할 수 없습니다.');
    process.exit(1);
}

log(`브릿지 실행: ${resolved.bridge}`);
log(`  (탐색: ${resolved.source})`);

// 브릿지는 top-level 코드로 stdin/stdout을 직접 다룬다. 같은 프로세스에서 require하면
// 별도 프로세스를 띄우지 않고도 stdio가 그대로 이어진다.
require(resolved.bridge);
