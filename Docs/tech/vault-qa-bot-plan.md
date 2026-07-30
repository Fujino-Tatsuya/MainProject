# Vault Q&A Discord 봇 구현 계획

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
> **설계 근거:** [vault-qa-bot-design.md](vault-qa-bot-design.md)

**Goal:** 팀 Discord `#ask-vault` 채널의 질문에 Vault 근거 + 출처로 답하는 봇을 만들어 서버 PC에 배포한다.

**Architecture:** Node.js 단일 프로세스. md 파일 인덱스(메모리) → Gemini 1차 호출로 관련 파일 선별 → 선별 파일 본문으로 2차 호출 답변 생성 → Discord 회신. 임베딩/DB 없음.

**Tech Stack:** Node.js 20+, discord.js v14, Gemini REST API (SDK 없이 내장 fetch), 테스트는 내장 `node --test`.

## Global Constraints

- 봇 코드 위치: `C:\Users\user\vault-qa-bot` (게임 리포·Vault 밖 — node_modules가 Syncthing에 퍼지면 안 됨). 자체 git repo(`git init`).
- 비밀값은 **환경변수만**: `DISCORD_BOT_TOKEN`, `GEMINI_API_KEY`. 코드·Vault·리포에 절대 하드코딩 금지.
- 경로 이원화: 개발 PC에선 Docs가 Vault 밖(`C:\Users\user\MainProject\Docs`)에 있고, 서버에선 Vault 안(`MainProject-Docs/`)에 있다 → `VAULT_PATH`(필수) + `DOCS_PATH`(선택, 비면 무시) 두 개의 지식 루트를 스캔한다.
- 제외 규칙: `.obsidian`, `.stversions`, `.stfolder`, `.claude`, `_processed`, `_inbox`, `DailyTodo`, `Worklog`, `sync-conflict` 포함 경로.
- evidence-only: 근거 없으면 "Vault에 근거 없음 — 담당자에게 확인 필요" 명시. 모든 답변에 출처 파일명. 답변은 한국어.
- Gemini 모델은 `GEMINI_MODEL` 환경변수 (기본 `gemini-3.5-flash`).
- 한글이 들어가는 `.ps1`은 반드시 **UTF-8 BOM**으로 저장 (PowerShell 5.1 CP949 파싱 함정).
- Discord 메시지는 2000자 제한 → 답변 분할 전송.

## File Structure

```
C:\Users\user\vault-qa-bot\
├── package.json
├── .gitignore              (node_modules, .env)
├── src\
│   ├── config.js           환경변수·지식 루트·제외 규칙 (한 곳에만)
│   ├── indexer.js          md 스캔 → [{root, relPath, title, preview}] 인덱스
│   ├── gemini.js           Gemini REST 호출 + 파일선별/답변 프롬프트 + 응답 파싱
│   ├── pipeline.js         질문 → 선별 → 본문 로드 → 답변 → 출처 붙이기 → 2000자 분할
│   └── bot.js              discord.js 진입점 (채널 필터, 오케스트레이션)
├── test\
│   ├── indexer.test.js
│   ├── gemini.test.js      (파싱·프롬프트 등 순수 함수만 — 실제 API 호출은 E2E)
│   └── pipeline.test.js    (분할·출처 포맷)
└── deploy\
    ├── install-server.ps1  서버 설치 스크립트
    └── SERVER-SETUP.md     서버 배포 + Discord 포털 수동 단계 가이드
```

---

### Task 1: 프로젝트 스캐폴드 + config

**Files:**
- Create: `C:\Users\user\vault-qa-bot\package.json`
- Create: `C:\Users\user\vault-qa-bot\.gitignore`
- Create: `C:\Users\user\vault-qa-bot\src\config.js`

**Interfaces:**
- Produces: `config = { vaultPath, docsPath, geminiKey, geminiModel, discordToken, askChannel, isExcluded(relPath) }` — 이후 모든 태스크가 이 모듈에서 설정을 읽는다.

- [ ] **Step 1: 폴더 생성 + git init + package.json**

```powershell
New-Item -ItemType Directory -Force C:\Users\user\vault-qa-bot\src, C:\Users\user\vault-qa-bot\test, C:\Users\user\vault-qa-bot\deploy | Out-Null
Set-Location C:\Users\user\vault-qa-bot; git init
npm init -y; npm pkg set type=module scripts.test="node --test" scripts.start="node src/bot.js"
npm install discord.js
```

- [ ] **Step 2: .gitignore 작성**

```
node_modules/
.env
```

- [ ] **Step 3: config.js 작성**

```js
// src/config.js
const EXCLUDE_PATTERNS = [
  '.obsidian', '.stversions', '.stfolder', '.claude',
  '_processed', '_inbox', 'DailyTodo', 'Worklog', 'sync-conflict',
];

export const config = {
  vaultPath: process.env.VAULT_PATH ?? 'C:\\Users\\user\\MainProjectVault',
  docsPath: process.env.DOCS_PATH ?? '',   // 서버에선 빈 값(Docs가 Vault 안에 있음)
  geminiKey: process.env.GEMINI_API_KEY ?? '',
  geminiModel: process.env.GEMINI_MODEL ?? 'gemini-3.5-flash',
  discordToken: process.env.DISCORD_BOT_TOKEN ?? '',
  askChannel: process.env.ASK_CHANNEL ?? 'ask-vault',
};

export function isExcluded(relPath) {
  const norm = relPath.replaceAll('\\', '/');
  return EXCLUDE_PATTERNS.some((p) => norm.split('/').some((seg) => seg.includes(p)));
}
```

- [ ] **Step 4: 커밋**

```powershell
git add -A; git commit -m "chore: scaffold vault-qa-bot (config, gitignore)"
```

---

### Task 2: 인덱서 (TDD)

**Files:**
- Create: `src\indexer.js`
- Test: `test\indexer.test.js`

**Interfaces:**
- Consumes: `isExcluded` (Task 1)
- Produces: `buildIndex() -> Promise<Entry[]>`, `Entry = { root, relPath, display, title, preview }` — `display`는 답변 출처 표기용(예: `MainProject-Docs/tech/networking.md`는 docsPath 루트일 때 `Docs/tech/networking.md`), `readEntry(entry) -> Promise<string>`(본문, 30KB 초과 시 잘라냄).

- [ ] **Step 1: 실패하는 테스트 작성** — 임시 폴더에 fixture md를 만들고 스캔 결과 검증

```js
// test/indexer.test.js
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { scanRoot, entryTitle } from '../src/indexer.js';

test('scanRoot finds md files, extracts title, skips excluded dirs', async () => {
  const dir = mkdtempSync(join(tmpdir(), 'vqa-'));
  mkdirSync(join(dir, 'Core'));
  mkdirSync(join(dir, 'DailyTodo'));
  writeFileSync(join(dir, 'Core', 'glossary.md'), '# 용어집\n내용 첫 줄\n둘째 줄');
  writeFileSync(join(dir, 'DailyTodo', 'x.md'), '# 제외되어야 함');
  const entries = await scanRoot(dir, 'Vault');
  assert.equal(entries.length, 1);
  assert.equal(entries[0].relPath.replaceAll('\\', '/'), 'Core/glossary.md');
  assert.equal(entries[0].title, '용어집');
  assert.ok(entries[0].preview.includes('내용 첫 줄'));
  assert.equal(entries[0].display, 'Vault/Core/glossary.md');
});

test('entryTitle falls back to filename when no heading', () => {
  assert.equal(entryTitle('그냥 본문', 'notes\\foo.md'), 'foo');
});
```

- [ ] **Step 2: 실행해서 실패 확인**

Run: `npm test` — Expected: FAIL (`scanRoot` is not defined)

- [ ] **Step 3: 구현**

```js
// src/indexer.js
import { readdir, readFile, stat } from 'node:fs/promises';
import { join, relative, basename } from 'node:path';
import { config, isExcluded } from './config.js';

const MAX_BODY = 30 * 1024;

export function entryTitle(text, relPath) {
  const m = text.match(/^#\s+(.+)$/m);
  return m ? m[1].trim() : basename(relPath, '.md');
}

export async function scanRoot(rootPath, label) {
  const entries = [];
  async function walk(dir) {
    for (const name of await readdir(dir)) {
      const full = join(dir, name);
      const rel = relative(rootPath, full);
      if (isExcluded(rel)) continue;
      const st = await stat(full);
      if (st.isDirectory()) await walk(full);
      else if (name.endsWith('.md')) {
        const text = await readFile(full, 'utf8');
        entries.push({
          root: rootPath,
          relPath: rel,
          display: `${label}/${rel.replaceAll('\\', '/')}`,
          title: entryTitle(text, rel),
          preview: text.replace(/^#.*$/m, '').trim().slice(0, 200),
        });
      }
    }
  }
  await walk(rootPath);
  return entries;
}

export async function buildIndex() {
  const out = await scanRoot(config.vaultPath, 'Vault');
  if (config.docsPath) out.push(...await scanRoot(config.docsPath, 'Docs'));
  return out;
}

export async function readEntry(entry) {
  const text = await readFile(join(entry.root, entry.relPath), 'utf8');
  return text.length > MAX_BODY ? text.slice(0, MAX_BODY) + '\n…(이하 생략)' : text;
}
```

- [ ] **Step 4: 테스트 통과 확인** — Run: `npm test` — Expected: PASS
- [ ] **Step 5: 커밋** — `git add -A; git commit -m "feat: vault markdown indexer"`

---

### Task 3: Gemini 클라이언트 + 파일 선별 (TDD는 파싱만)

**Files:**
- Create: `src\gemini.js`
- Test: `test\gemini.test.js`

**Interfaces:**
- Consumes: `config` (Task 1), `Entry[]` (Task 2)
- Produces: `callGemini(prompt) -> Promise<string>` (429/오류 시 `GeminiRateLimitError`/`Error` throw), `buildSelectPrompt(index, question) -> string`, `parseSelection(text, index) -> Entry[]` (최대 6개), `buildAnswerPrompt(files, question) -> string` where `files = [{display, body}]`.

- [ ] **Step 1: 실패하는 테스트 작성**

```js
// test/gemini.test.js
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { parseSelection, buildSelectPrompt, buildAnswerPrompt } from '../src/gemini.js';

const index = [
  { display: 'Docs/tech/networking.md', title: '네트워크', preview: '' },
  { display: 'Vault/Core/glossary.md', title: '용어집', preview: '' },
];

test('parseSelection matches display paths in model output, caps at 6', () => {
  const out = parseSelection('관련 파일: ["Docs/tech/networking.md"] 그리고 Vault/Core/glossary.md', index);
  assert.deepEqual(out.map((e) => e.display), ['Docs/tech/networking.md', 'Vault/Core/glossary.md']);
});

test('parseSelection returns [] when nothing matches', () => {
  assert.deepEqual(parseSelection('없음', index), []);
});

test('prompts contain question and evidence-only rule', () => {
  assert.ok(buildSelectPrompt(index, '보스 권한?').includes('보스 권한?'));
  const p = buildAnswerPrompt([{ display: 'Vault/Core/glossary.md', body: '# 용어집' }], '질문');
  assert.ok(p.includes('근거 없음'));
  assert.ok(p.includes('# 용어집'));
});
```

- [ ] **Step 2: 실행해서 실패 확인** — Run: `npm test` — Expected: FAIL
- [ ] **Step 3: 구현**

```js
// src/gemini.js
import { config } from './config.js';

export class GeminiRateLimitError extends Error {}

export async function callGemini(prompt) {
  const url = `https://generativelanguage.googleapis.com/v1beta/models/${config.geminiModel}:generateContent?key=${config.geminiKey}`;
  const res = await fetch(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ contents: [{ parts: [{ text: prompt }] }] }),
  });
  if (res.status === 429) throw new GeminiRateLimitError('rate limited');
  if (!res.ok) throw new Error(`Gemini ${res.status}: ${await res.text()}`);
  const data = await res.json();
  const text = data.candidates?.[0]?.content?.parts?.map((p) => p.text).join('') ?? '';
  if (!text) throw new Error('Gemini returned empty response');
  return text;
}

export function buildSelectPrompt(index, question) {
  const list = index.map((e) => `- ${e.display} | ${e.title} | ${e.preview.slice(0, 100)}`).join('\n');
  return `아래는 게임 프로젝트 지식베이스의 파일 목록이다. 질문에 답하는 데 필요한 파일 경로만 JSON 배열로 골라라(최대 6개, 관련 파일이 없으면 []).\n\n질문: ${question}\n\n파일 목록:\n${list}\n\n출력 형식: ["경로", ...] 만 출력.`;
}

export function parseSelection(text, index) {
  return index.filter((e) => text.includes(e.display)).slice(0, 6);
}

export function buildAnswerPrompt(files, question) {
  const docs = files.map((f) => `=== 파일: ${f.display} ===\n${f.body}`).join('\n\n');
  return `너는 게임 개발팀의 지식베이스 안내 봇이다. 아래 문서만 근거로 한국어로 답하라.
규칙:
- 문서에 없는 내용은 절대 지어내지 말 것. 근거가 없으면 "Vault에 근거 없음 — 담당자에게 확인 필요"라고 답할 것.
- 답변 마지막에 실제로 근거로 쓴 파일명을 "출처: 파일명" 형식으로 나열할 것.
- 간결하게, 질문에 직접 답할 것.

질문: ${question}

${docs}`;
}
```

- [ ] **Step 4: 테스트 통과 확인** — Run: `npm test` — Expected: PASS
- [ ] **Step 5: 커밋** — `git add -A; git commit -m "feat: gemini client, file selection and answer prompts"`

---

### Task 4: 파이프라인 (질문→답변, 2000자 분할)

**Files:**
- Create: `src\pipeline.js`
- Test: `test\pipeline.test.js`

**Interfaces:**
- Consumes: Task 2 `buildIndex`/`readEntry`, Task 3 전부
- Produces: `answerQuestion(question) -> Promise<string[]>` (Discord로 보낼 메시지 조각들), `chunkMessage(text, limit=2000) -> string[]`, `refreshIndex()` (5분 주기 갱신용, bot.js가 호출)

- [ ] **Step 1: 실패하는 테스트 작성**

```js
// test/pipeline.test.js
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { chunkMessage } from '../src/pipeline.js';

test('chunkMessage splits long text under limit, prefers line breaks', () => {
  const text = ('가나다라마바사아자차'.repeat(30) + '\n').repeat(20); // 줄당 300자 × 20줄
  const chunks = chunkMessage(text, 2000);
  assert.ok(chunks.length >= 3);
  for (const c of chunks) assert.ok(c.length <= 2000);
  assert.equal(chunks.join('\n'), text.trim());
});

test('chunkMessage returns short text as single chunk', () => {
  assert.deepEqual(chunkMessage('짧음', 2000), ['짧음']);
});
```

- [ ] **Step 2: 실행해서 실패 확인** — Run: `npm test` — Expected: FAIL
- [ ] **Step 3: 구현**

```js
// src/pipeline.js
import { buildIndex, readEntry } from './indexer.js';
import { callGemini, buildSelectPrompt, parseSelection, buildAnswerPrompt, GeminiRateLimitError } from './gemini.js';

let index = [];
export async function refreshIndex() {
  index = await buildIndex();
  console.log(`[index] ${index.length} files`);
}

export function chunkMessage(text, limit = 2000) {
  const chunks = [];
  let cur = '';
  for (const line of text.trim().split('\n')) {
    const add = cur ? cur + '\n' + line : line;
    if (add.length > limit) {
      if (cur) chunks.push(cur);
      // 한 줄 자체가 limit보다 길면 강제 분할
      let rest = line;
      while (rest.length > limit) { chunks.push(rest.slice(0, limit)); rest = rest.slice(limit); }
      cur = rest;
    } else cur = add;
  }
  if (cur) chunks.push(cur);
  return chunks;
}

export async function answerQuestion(question) {
  try {
    if (index.length === 0) await refreshIndex();
    const picked = parseSelection(await callGemini(buildSelectPrompt(index, question)), index);
    if (picked.length === 0) {
      return ['Vault에 근거 없음 — 담당자에게 확인 필요. (관련 문서를 찾지 못했습니다)'];
    }
    const files = await Promise.all(picked.map(async (e) => ({ display: e.display, body: await readEntry(e) })));
    return chunkMessage(await callGemini(buildAnswerPrompt(files, question)));
  } catch (err) {
    if (err instanceof GeminiRateLimitError) return ['지금 질문이 몰려서 잠시 쉬는 중이에요. 1분 뒤에 다시 물어봐 주세요.'];
    console.error('[pipeline]', err);
    return ['답변 생성 중 오류가 났어요. 잠시 후 다시 시도해 주세요.'];
  }
}
```

- [ ] **Step 4: 테스트 통과 확인** — Run: `npm test` — Expected: PASS
- [ ] **Step 5: 커밋** — `git add -A; git commit -m "feat: question answering pipeline with message chunking"`

---

### Task 5: Discord 봇 진입점 + 개발 PC E2E

**Files:**
- Create: `src\bot.js`

**Interfaces:**
- Consumes: `config` (Task 1), `answerQuestion`/`refreshIndex` (Task 4)

- [ ] **Step 1: 구현** (단위 테스트 없음 — discord.js 연결부는 E2E로 검증)

```js
// src/bot.js
import { Client, GatewayIntentBits, Events } from 'discord.js';
import { config } from './config.js';
import { answerQuestion, refreshIndex } from './pipeline.js';

for (const [k, v] of Object.entries({ DISCORD_BOT_TOKEN: config.discordToken, GEMINI_API_KEY: config.geminiKey })) {
  if (!v) { console.error(`환경변수 ${k}가 없습니다.`); process.exit(1); }
}

const client = new Client({
  intents: [GatewayIntentBits.Guilds, GatewayIntentBits.GuildMessages, GatewayIntentBits.MessageContent],
});

client.once(Events.ClientReady, async (c) => {
  console.log(`[bot] logged in as ${c.user.tag}, watching #${config.askChannel}`);
  await refreshIndex();
  setInterval(() => refreshIndex().catch((e) => console.error('[index]', e)), 5 * 60 * 1000);
});

client.on(Events.MessageCreate, async (msg) => {
  if (msg.author.bot) return;
  if (msg.channel.name !== config.askChannel) return;
  try {
    await msg.channel.sendTyping();
    for (const chunk of await answerQuestion(msg.content)) await msg.reply(chunk);
  } catch (err) {
    console.error('[bot]', err);
  }
});

client.login(config.discordToken);
```

- [ ] **Step 2: 🙋 사용자 수동 단계 — Discord 포털** (이 단계는 사용자에게 요청)
  1. https://discord.com/developers/applications → New Application (`VaultQA`)
  2. Bot 탭 → Reset Token으로 토큰 발급 → **MESSAGE CONTENT INTENT 켜기**
  3. OAuth2 → URL Generator → scope `bot`, 권한 `View Channels`, `Send Messages`, `Read Message History` → 생성된 URL로 팀 서버에 초대
  4. 팀 서버에 `#ask-vault` 채널 생성

- [ ] **Step 3: 개발 PC에서 E2E 실행** (Gemini 키·봇 토큰은 사용자에게 받아 세션 환경변수로만 설정)

```powershell
$env:DISCORD_BOT_TOKEN='...'; $env:GEMINI_API_KEY='...'
$env:DOCS_PATH='C:\Users\user\MainProject\Docs'
node src/bot.js
```

검증 질문(#ask-vault에 직접 입력, 3개 이상):
- "보스 데미지 판정은 서버 권한이야 클라 권한이야?" → networking/architecture 문서 근거 + 출처 표기 확인
- "레퍼런스 게임이 뭐야?" → `Vault/Core/reference-games.md` 근거 확인
- "우리 팀 회식 규정 알려줘" → "Vault에 근거 없음" 폴백 확인

Expected: 세 경우 모두 한국어 답변 + 출처(또는 근거 없음 명시), 2000자 초과 답변은 분할 전송.

- [ ] **Step 4: 커밋** — `git add -A; git commit -m "feat: discord bot entrypoint"`

---

### Task 6: 서버 배포 패키지

**Files:**
- Create: `deploy\install-server.ps1` (**UTF-8 BOM으로 저장** — 한글 포함 시 PS 5.1 파싱 함정)
- Create: `deploy\SERVER-SETUP.md`

**Interfaces:**
- Consumes: 완성된 봇 (Task 1~5)

- [ ] **Step 1: install-server.ps1 작성** — 서버 PC에서 사용자가 실행. 하는 일:
  1. `winget install OpenJS.NodeJS.LTS` (없을 때만)
  2. 봇 폴더를 `C:\vault-qa-bot`에 복사 후 `npm install`
  3. 사용자 환경변수 등록: `[Environment]::SetEnvironmentVariable('DISCORD_BOT_TOKEN', $token, 'User')` 방식으로 `DISCORD_BOT_TOKEN`/`GEMINI_API_KEY`/`VAULT_PATH`(서버의 Vault 경로) — 값은 `Read-Host`가 아니라 **스크립트 파라미터**로 받음 (`-Confirm:$false` 비대화형 안전)
  4. 작업 스케줄러 등록: 로그온 시 시작 + 실패 시 1분 후 재시작 (`schtasks /create ... /sc onlogon`)
- [ ] **Step 2: SERVER-SETUP.md 작성** — Discord 포털 단계(Task 5 Step 2 재수록) + 스크립트 사용법 + "봇이 안 뜰 때" 트러블슈팅(환경변수 확인, `node src/bot.js` 직접 실행해 로그 보기)
- [ ] **Step 3: 커밋** — `git add -A; git commit -m "feat: server deployment script and setup guide"`
- [ ] **Step 4: 🙋 사용자가 서버 PC에서 실행 + 팀 Discord에서 실사용 검증** (Task 5 Step 3과 같은 질문 3개)

---

## 검증 완료 기준

- [ ] `npm test` 전체 통과 (개발 PC)
- [ ] 개발 PC E2E: 근거 있는 질문 2개 + 근거 없는 질문 1개 모두 규칙대로 응답
- [ ] 서버 PC에서 봇 상주 + 재부팅 후 자동 시작 확인
- [ ] 키·토큰이 git/Vault 어디에도 없음 (`git grep -i api_key` 결과 없음)
