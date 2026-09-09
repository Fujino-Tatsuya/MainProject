// 레포지토리 맵 생성기 — Assets/**/*.cs 를 tree-sitter로 파싱해
// Docs/tech/repo_map.md (사람/AI용 요약) + repo_map.json (원본 데이터) 를 만든다.
// 설계 근거: PLAN.md 「CURRENT PLAN — 레포지토리 맵(Repo Map) 생성 도구」
//
// 사용법: node Tools/RepoMap/generate.js

import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import pkg from 'web-tree-sitter';
const Parser = pkg;

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const REPO_ROOT = path.resolve(__dirname, '..', '..');
const ASSETS_DIR = path.join(REPO_ROOT, 'Assets');
const OUT_MD = path.join(REPO_ROOT, 'Docs', 'tech', 'repo_map.md');
const OUT_JSON = path.join(REPO_ROOT, 'Docs', 'tech', 'repo_map.json');
const WASM_PATH = path.join(__dirname, 'node_modules', 'tree-sitter-wasms', 'out', 'tree-sitter-c_sharp.wasm');

// 서드파티 에셋 폴더 제외 (우리 코드가 아님). 다른 게 추가되면 여기 늘린다.
const EXCLUDE_PATH_SUBSTRINGS = ['BroAudio'];

const TYPE_DECL_KINDS = new Set([
  'class_declaration',
  'interface_declaration',
  'struct_declaration',
  'record_declaration',
  'enum_declaration',
]);

const CONTROL_FLOW_KINDS = new Set([
  'if_statement',
  'for_statement',
  'foreach_statement',
  'while_statement',
  'do_statement',
  'switch_statement',
  'try_statement',
]);

const WRAPPER_COMMENT_RE = /래퍼|wrapper|호환/i;

function collectCsFiles(dir) {
  const results = [];
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (EXCLUDE_PATH_SUBSTRINGS.some((s) => full.includes(s))) continue;
    if (entry.isDirectory()) {
      results.push(...collectCsFiles(full));
    } else if (entry.name.endsWith('.cs')) {
      results.push(full);
    }
  }
  return results;
}

function modifiersOf(node) {
  const mods = [];
  for (const child of node.children) {
    if (child.type === 'modifier') mods.push(child.text);
  }
  return mods;
}

function basesOf(node) {
  const basesField = node.childForFieldName('bases');
  if (!basesField) return [];
  return basesField.namedChildren.map((c) => c.text);
}

function paramsOf(paramList) {
  if (!paramList) return '';
  return paramList.namedChildren
    .filter((p) => p.type === 'parameter')
    .map((p) => {
      const t = p.childForFieldName('type');
      const n = p.childForFieldName('name');
      return `${t ? t.text : '?'} ${n ? n.text : '?'}`;
    })
    .join(', ');
}

// 직전 형제(comment 들)를 doc 주석으로 모아온다. tree-sitter는 `///` 한 줄마다 별도 comment 노드를 만든다.
function leadingCommentOf(node) {
  const parent = node.parent;
  if (!parent) return '';
  const siblings = parent.children;
  // web-tree-sitter는 매 접근마다 새 Node 객체를 만들어 반환하므로 참조(===) 비교로는
  // 같은 노드를 못 찾는다 — 바이트 오프셋(startIndex)으로 위치를 식별해야 한다.
  const idx = siblings.findIndex((s) => s.startIndex === node.startIndex);
  const lines = [];
  for (let i = idx - 1; i >= 0; i--) {
    if (siblings[i].type === 'comment') lines.unshift(siblings[i].text);
    else break;
  }
  return lines.join('\n');
}

// wrapper(위임) 함수 판정: 본문이 얕고(분기/반복 없음) 다른 멤버를 호출하는 게 전부인가.
function detectWrapper(methodNode) {
  const body = methodNode.childForFieldName('body');
  if (!body) return null;

  const findInvocationTarget = (expr) => {
    if (!expr) return null;
    if (expr.type === 'invocation_expression') {
      const fn = expr.childForFieldName('function');
      return fn ? fn.text : expr.text;
    }
    // 괄호/캐스트 등으로 감싸인 경우 하위에서 invocation_expression 탐색
    for (const child of expr.namedChildren) {
      const found = findInvocationTarget(child);
      if (found) return found;
    }
    return null;
  };

  if (body.type === 'arrow_expression_clause') {
    // `Foo() => Bar();` 형태 — 문법상 항상 단일 식이라 분기 자체가 불가능함(신뢰도 높음).
    const expr = body.namedChildren[0];
    const target = findInvocationTarget(expr);
    return target ? { target, confidence: 'high' } : null;
  }

  if (body.type === 'block') {
    const statements = body.namedChildren;
    if (statements.length === 0 || statements.length > 2) return null;
    const hasControlFlow = statements.some((s) => CONTROL_FLOW_KINDS.has(s.type));
    if (hasControlFlow) return null;

    let target = null;
    for (const s of statements) {
      const found = findInvocationTarget(s);
      if (found) target = found;
    }
    return target ? { target, confidence: 'medium' } : null;
  }

  return null;
}

function extractMethods(declList) {
  const methods = [];
  for (const child of declList.namedChildren) {
    if (child.type !== 'method_declaration') continue;
    const mods = modifiersOf(child);
    const isPublic = mods.includes('public');
    const returnType = child.childForFieldName('type');
    const name = child.childForFieldName('name');
    const params = child.childForFieldName('parameters');
    const wrapper = detectWrapper(child);
    const doc = leadingCommentOf(child);
    const docConfirmsWrapper = WRAPPER_COMMENT_RE.test(doc);
    methods.push({
      name: name ? name.text : '?',
      returnType: returnType ? returnType.text : 'void',
      params: paramsOf(params),
      modifiers: mods,
      isPublic,
      wrapper: wrapper ? { ...wrapper, confidence: docConfirmsWrapper ? 'confirmed' : wrapper.confidence } : (docConfirmsWrapper ? { target: '(주석상 래퍼, 위임 대상 미탐지)', confidence: 'confirmed' } : null),
    });
  }
  return methods;
}

function extractTypeDecl(node, namespaceName) {
  const nameNode = node.childForFieldName('name');
  const bodyNode = node.childForFieldName('body');
  const decl = {
    kind: node.type.replace('_declaration', ''), // class/interface/struct/record/enum
    name: nameNode ? nameNode.text : '?',
    namespace: namespaceName || '',
    bases: basesOf(node),
    modifiers: modifiersOf(node),
    methods: bodyNode ? extractMethods(bodyNode) : [],
    nested: [],
  };
  // 중첩 타입(클래스 안 클래스/enum 등)도 재귀적으로 수집한다.
  if (bodyNode) {
    for (const child of bodyNode.namedChildren) {
      if (TYPE_DECL_KINDS.has(child.type)) {
        decl.nested.push(extractTypeDecl(child, namespaceName));
      }
    }
  }
  return decl;
}

// compilation_unit 전체를 재귀 순회하며 네임스페이스를 추적하고 타입 선언을 수집한다.
function walkFile(rootNode) {
  const decls = [];
  const visit = (node, ns) => {
    if (node.type === 'namespace_declaration' || node.type === 'file_scoped_namespace_declaration') {
      const nameNode = node.childForFieldName('name');
      const childNs = nameNode ? nameNode.text : ns;
      const body = node.childForFieldName('body') || node;
      for (const child of body.namedChildren) visit(child, childNs);
      return;
    }
    if (TYPE_DECL_KINDS.has(node.type)) {
      decls.push(extractTypeDecl(node, ns));
      return; // 중첩 타입은 extractTypeDecl 안에서 이미 처리됨
    }
    for (const child of node.namedChildren) visit(child, ns);
  };
  visit(rootNode, '');
  return decls;
}

function flattenDecls(decls, out = []) {
  for (const d of decls) {
    out.push(d);
    if (d.nested.length) flattenDecls(d.nested, out);
  }
  return out;
}

function renderMethodLine(m) {
  const sig = `${m.returnType} ${m.name}(${m.params})`;
  if (m.wrapper) {
    const tag = m.wrapper.confidence === 'confirmed' ? '[wrapper·주석확인]' : m.wrapper.confidence === 'high' ? '[wrapper]' : '[wrapper?]';
    return `  - ${sig} → \`${m.wrapper.target}\` 위임 ${tag}`;
  }
  return `  - ${sig}`;
}

function renderDecl(d) {
  const lines = [];
  const basesStr = d.bases.length ? ` : ${d.bases.join(', ')}` : '';
  lines.push(`### ${d.kind} ${d.name}${basesStr}`);

  if (d.kind === 'interface') {
    // 얕은 계층(공개 계약) — 전체 노출
    if (d.methods.length === 0) lines.push('  (멤버 없음)');
    for (const m of d.methods) lines.push(renderMethodLine(m));
  } else {
    const publicMethods = d.methods.filter((m) => m.isPublic || m.wrapper);
    const shown = publicMethods.filter((m) => m.wrapper || m.isPublic);
    const hiddenCount = d.methods.length - shown.length;
    for (const m of shown) lines.push(renderMethodLine(m));
    if (hiddenCount > 0) lines.push(`  - (내부 메서드 ${hiddenCount}개 숨김)`);
    if (d.methods.length === 0) lines.push('  (메서드 없음)');
  }
  return lines.join('\n');
}

async function main() {
  await Parser.init();
  const parser = new Parser();
  const lang = await Parser.Language.load(WASM_PATH);
  parser.setLanguage(lang);

  const files = collectCsFiles(ASSETS_DIR);
  const byFolder = new Map();
  const parseWarnings = [];
  const allDecls = []; // { file, decl }

  for (const file of files) {
    const rel = path.relative(REPO_ROOT, file).replace(/\\/g, '/');
    // 팀 컨벤션상 .cs는 UTF-8 BOM으로 저장된다(Docs/tech/conventions.md) — BOM을 벗기지 않으면
    // 파일 첫 글자부터 파싱이 깨져 모든 파일이 문법 오류로 잡힌다.
    let src = fs.readFileSync(file, 'utf8');
    if (src.charCodeAt(0) === 0xfeff) src = src.slice(1);
    let tree;
    try {
      tree = parser.parse(src);
    } catch (e) {
      parseWarnings.push(`${rel}: 파싱 실패 — ${e.message}`);
      continue;
    }
    if (tree.rootNode.hasError()) {
      parseWarnings.push(`${rel}: 문법 오류 노드 포함(부분 결과만 신뢰)`);
    }
    const decls = flattenDecls(walkFile(tree.rootNode));
    if (decls.length === 0) continue;

    const folder = path.dirname(rel);
    if (!byFolder.has(folder)) byFolder.set(folder, []);
    byFolder.get(folder).push({ file: rel, decls });

    for (const d of decls) allDecls.push({ file: rel, decl: d });
  }

  // ---- Markdown ----
  const md = [];
  md.push('# Repository Map');
  md.push('');
  md.push('> 자동 생성 파일 — 직접 수정하지 말 것. `node Tools/RepoMap/generate.js`로 재생성.');
  md.push('> 생성 근거: [PLAN.md](../../PLAN.md) 「레포지토리 맵(Repo Map) 생성 도구」.');
  md.push('> 표시 기준: interface는 전체 시그니처 노출(공개 계약). class는 `[wrapper]`로 태깅된');
  md.push('> 위임 메서드만 노출하고 나머지 내부 구현은 개수만 남긴다(Deep Module 원칙).');
  md.push('');

  const folders = [...byFolder.keys()].sort();
  for (const folder of folders) {
    md.push(`## ${folder}`);
    md.push('');
    for (const { file, decls } of byFolder.get(folder)) {
      for (const d of decls) {
        md.push(renderDecl(d));
        md.push('');
      }
    }
  }

  md.push('## 위임(Facade) 관계 요약');
  md.push('');
  const wrapperEdges = [];
  for (const { file, decl } of allDecls) {
    for (const m of decl.methods) {
      if (m.wrapper) wrapperEdges.push(`- \`${decl.name}.${m.name}()\` → \`${m.wrapper.target}\` (${file})`);
    }
  }
  if (wrapperEdges.length === 0) md.push('(탐지된 위임 관계 없음)');
  else md.push(...wrapperEdges);
  md.push('');

  if (parseWarnings.length) {
    md.push('## 파싱 경고');
    md.push('');
    md.push(...parseWarnings.map((w) => `- ${w}`));
    md.push('');
  }

  fs.mkdirSync(path.dirname(OUT_MD), { recursive: true });
  fs.writeFileSync(OUT_MD, md.join('\n'), 'utf8');

  // ---- JSON (원본 데이터) ----
  fs.writeFileSync(
    OUT_JSON,
    JSON.stringify({ generatedAt: new Date().toISOString(), files: [...byFolder.values()].flat(), parseWarnings }, null, 2),
    'utf8',
  );

  console.log(`파일 ${files.length}개 스캔, 타입 선언 ${allDecls.length}개 추출.`);
  console.log(`경고 ${parseWarnings.length}건.`);
  console.log(`출력: ${path.relative(REPO_ROOT, OUT_MD)}, ${path.relative(REPO_ROOT, OUT_JSON)}`);
}

main();
