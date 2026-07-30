# /diff-check 구현 계획

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
> **설계 근거:** [diff-check-design.md](diff-check-design.md)

**Goal:** 오늘 작업 diff의 안개구역을 오픈북 체크리스트(Obsidian md)로 만들어주는 `/diff-check` 커맨드 + `/daily-todo` 연동.

**Architecture:** 코드가 아니라 **커맨드 프롬프트 파일 2개**(신규 1, 수정 1). 검증은 단위테스트가 아니라 실제 diff에 대한 E2E 실행으로 한다.

**Tech Stack:** Claude Code custom slash commands (markdown), git CLI, Obsidian 체크박스.

## Global Constraints

- evidence-only: 이 diff의 구체 지점을 가리키는 항목만, 일반론 금지. 원본 수정 금지(읽기 전용, 출력 파일 제외).
- 회당 최대 12개 항목. 우선순위: 네트워크 권한 > 코어 설계 원칙 > 검증 > 컨벤션.
- 출력: `<Vault>/Worklog/<이름>/안개체크 YYYY-MM-DD.md` (이름 하위폴더 필수 — Vault는 팀 공유).
- 규칙 문서: `Docs/tech/architecture.md` · `networking.md` · `conventions.md` + `AGENTS.md` §4.
- 기획·아트 대상 아님. `/daily-todo`의 안개 섹션은 프로그래머(경석/은희/민경)에게만.

---

### Task 1: `.claude/commands/diff-check.md` 작성 + 실제 diff E2E

**Files:**
- Create: `C:\Users\user\MainProject\.claude\commands\diff-check.md`
- 출력(E2E 검증물): `C:\Users\user\MainProjectVault\Worklog\경석\안개체크 2026-07-07.md`

**Interfaces:**
- Produces: 안개체크 파일 형식 — 상단 `## 요약`(범위·파일 수·어제 미해소 N건), `## ✅ 검증 체크`, `## 🧠 이해도 체크 (오픈북)`, `## ⚠️ 규칙 대조 의심 지점`, 각 항목 `- [ ] **파일명:라인** — 내용`. Task 2가 이 형식(`- [ ]` 미체크 집계)에 의존.

- [ ] **Step 1: 커맨드 파일 작성** — 아래 내용 그대로 (프론트매터 포함):

````markdown
---
description: 오늘 작업 diff에서 안개구역(미이해·미검증 지점) 오픈북 체크리스트 생성
---

내(또는 AI가 짜준) 코드 diff에서 이해·검증하지 못하고 넘어간 부분을 체크리스트로 만든다.
설계: Docs/tech/diff-check-design.md. 원본 코드는 절대 수정하지 않는다(읽기 전용).

## 1. 대상자와 Vault 경로

- 대상자 = `git config user.name` → AGENTS.md §5 분담표(경석/은희/민경)와 매칭. 매칭 실패 시 사용자에게 묻는다.
- Vault 경로 = `C:\Users\<현재 사용자>\MainProjectVault`. 없으면 사용자에게 경로를 묻는다.

## 2. diff 범위 (`$ARGUMENTS`)

- 인자 없음(기본): 오늘 내 커밋(`git log --since=midnight --author=<user.name> --oneline`) + 미커밋 변경(`git status --short`, staged/unstaged 모두).
- 인자 있음: 해당 ref 대비 전체 — `git diff <인자>...HEAD` + 미커밋 변경 (예: `/diff-check development` = PR 전 점검).
- 변경이 하나도 없으면 "오늘 분석할 변경 없음"만 출력하고 파일을 만들지 않는다.

## 3. 분석

1. 변경 파일 각각에 대해 diff 헝크만 보지 말고 **파일을 열어 맥락까지 읽는다** (.cs/.shader 등 코드 중심, .meta·에셋 바이너리는 건너뜀. .unity/.prefab/.asset은 코드가 아니라 "에디터에서 뭘 바꿨는지 아는가" 관점으로만 1항목 이내).
2. 규칙 문서와 대조: `Docs/tech/architecture.md`, `Docs/tech/networking.md`, `Docs/tech/conventions.md`, `AGENTS.md` §4
   (UnitBase=공통만·컴포넌트 조립 / 권한: 플레이어=클라(오너), 보스·데미지·상태이상·장판=서버(호스트) / 스킬·보스패턴=SO 데이터 주도).

## 4. 체크리스트 생성 규칙

- **최대 12개.** 넘치면 우선순위(네트워크 권한 > 코어 설계 원칙 > 검증 > 컨벤션)로 자르고 "N건 생략"을 요약에 적는다.
- 모든 항목은 `- [ ] **파일명:라인** — 내용` 형식. 이 diff의 구체 지점 없이는 항목을 만들지 않는다 ("null 체크 했나요?" 같은 일반론 금지).
- 3섹션:
  - `## ✅ 검증 체크` — 실제로 돌려서 확인했는가. 변경 특성 기반 (네트워크 코드면 "MPPM 2인, 호스트/클라 양쪽 확인" 식으로 구체적으로).
  - `## 🧠 이해도 체크 (오픈북)` — "이 로직이 왜 이렇게 동작하는지 설명할 수 있는가" 질문형. 코드를 열어 읽고 이해되면 체크하는 용도.
  - `## ⚠️ 규칙 대조 의심 지점` — 규칙 문서와 어긋나 보이는 부분. **확신 없으면 단정하지 말고 질문형으로** ("~인 것으로 보이는데 의도인가?"). 어긋남의 근거 문서·조항을 함께 적는다. 없으면 "없음".

## 5. 출력

- 경로: `<Vault>/Worklog/<이름>/안개체크 YYYY-MM-DD.md` (이름 폴더 없으면 생성).
- 파일 상단 `## 요약`: 분석 범위(커밋 SHA들·미커밋 파일 수), 생성 항목 수, **어제까지 미해소 N건** — 같은 폴더의 이전 `안개체크 *.md`들에서 `- [ ]` 남은 개수 합계 + 가장 최근 파일 링크 한 줄 (항목을 복사해오지는 않는다).
- **오늘 파일이 이미 있으면**: 기존 항목(체크 여부 무관)과 같은 지점을 가리키는 항목은 다시 만들지 않고, 새 변경분만 기존 섹션 뒤에 추가한다. 기존 체크 상태는 절대 건드리지 않는다.
````

- [ ] **Step 2: E2E — 오늘의 실제 diff로 실행** — 이 레포에는 오늘 실제 변경(미커밋: MapScene.unity, ZoneWiring.cs, WaterDark.shader 등 + 오늘 Docs 커밋들)이 있다. 커맨드 파일의 절차를 그대로 따라 `Worklog/경석/안개체크 2026-07-07.md`를 생성한다.
- [ ] **Step 3: 품질 검증** — 생성물이 다음을 모두 만족하는지 확인: ①3섹션+요약 존재 ②항목 ≤12 ③모든 항목에 파일:라인 ④일반론 항목 0개 ⑤원본 코드 무수정(`git status`로 확인, Docs/문서 외 변경 없음).
- [ ] **Step 4: 커밋** — `git add .claude/commands/diff-check.md; git commit -m "feat: /diff-check fog-clearing checklist command (Phase D)"`

---

### Task 2: `/daily-todo`에 안개구역 섹션 연동

**Files:**
- Modify: `C:\Users\user\MainProjectVault\.claude\commands\daily-todo.md` (Vault — git 아님, Syncthing 동기화)

**Interfaces:**
- Consumes: Task 1의 안개체크 파일 형식 (`Worklog/<이름>/안개체크 *.md`의 `- [ ]` 항목)

- [ ] **Step 1: "## 1. 근거 수집"에 5번 항목 추가** — 4번(git 상태) 뒤에:

```markdown
5. **(프로그래머만) 안개구역** — 대상자가 프로그래머(경석/은희/민경)이면 `Worklog/<이름>/안개체크 *.md`
   전체에서 미체크(`- [ ]`) 항목을 집계한다. 기획·아트가 대상이면 이 단계는 건너뛴다.
```

- [ ] **Step 2: "## 3. TodoList 작성 규칙" 구조 6번(확인 필요) 뒤에 섹션 추가** — 구조 목록의 6과 7 사이에 삽입하고 이후 번호를 하나씩 민다:

```markdown
7. 🌫️ 안개구역 미해소 (프로그래머만): 총 N건 + 가장 오래된/중요한 항목 최대 3개 미리보기(파일:라인 그대로) +
   해당 안개체크 파일 링크. 0건이거나 대상자가 프로그래머가 아니면 이 섹션 자체를 넣지 않는다.
```

- [ ] **Step 3: 검증** — 수정된 daily-todo.md를 통독해 번호 순서·기존 규칙과 모순 없는지 확인 (실행 E2E는 다음 daily-todo 실행 때 자연 검증됨을 결과 보고에 명시).
- [ ] **Step 4:** Vault는 git이 아니므로 커밋 없음 — Syncthing이 자동 배포. `Vault/사용법.md`의 커맨드 목록에 `/diff-check` 한 줄 추가.

---

## 검증 완료 기준

- [ ] `안개체크 2026-07-07.md`가 실제 diff 근거로 생성되고 품질 기준(≤12개·파일:라인·일반론 0) 통과
- [ ] 원본 코드/에셋 무변경
- [ ] daily-todo.md 수정이 기존 규칙과 모순 없음
- [ ] `.claude/commands/diff-check.md` 커밋됨
