# 팀 지식베이스 Vault 구축 실행 계획

> **참고 문서:** [Docs/tech/knowledge-base-vault-design.md](knowledge-base-vault-design.md) (설계 스펙)
> 이 계획은 소프트웨어 코드가 아니라 두 대의 Windows PC(본인 PC + 서버 PC)에 걸친 인프라 셋업이라
> TDD 대신 "설치 → 확인" 사이클로 각 태스크를 구성했다. 체크박스(`- [ ]`)로 진행 추적.

**목표:** Tailscale + Syncthing + Obsidian + Whisper 플러그인을 설치·연결해서, 설계 문서의 Vault 폴더 구조가 본인 PC와 서버 PC 사이에 실시간 동기화되게 만든다.

**아키텍처:** 서버 PC가 Syncthing 마스터 사본을 들고 있고, 본인 PC는 Vault 폴더와 `MainProject/Docs` 폴더를 각각 별도 Syncthing 공유로 등록해 동기화한다. 심볼릭 링크는 쓰지 않는다.

**실행 주체 표기:**
- 🤖 = AI가 이 PC(본인 PC)에서 직접 실행 (PowerShell/winget)
- 🙋 = 사람이 GUI로 직접 해야 함 (설치 마법사, 기기 승인 등 자동화 불가 구간)
- 🖥️서버 = 서버 PC 앞에서 사람이 직접 해야 함 (AI는 서버 PC에 원격 접근 권한이 없음)

## Global Constraints

- Vault 로컬 경로: `C:\Users\user\MainProjectVault` (본인 PC)
- `MainProject/Docs`는 심볼릭 링크로 vault에 넣지 않는다 — 별도 Syncthing 폴더로 등록 (설계 문서 근거).
- 서버 PC OS: Windows.
- 회의록 전사는 로컬 GPU(RTX)로 처리, 클라우드로 보내지 않는다.
- git 작업트리(`C:\Users\user\MainProject`)는 Syncthing이 통째로 감시하지 않는다 — `Docs` 폴더만 별도 등록.

---

### Task 1: 본인 PC — Tailscale 설치 및 로그인

**대상:** 본인 PC 🤖(설치)+🙋(로그인)

- [ ] **Step 1: winget으로 Tailscale 설치**

```powershell
winget install --id Tailscale.Tailscale -e --accept-source-agreements --accept-package-agreements
```
예상 출력: `Successfully installed`

- [ ] **Step 2: 설치 확인**

```powershell
Get-Command tailscale -ErrorAction SilentlyContinue
& "C:\Program Files\Tailscale\tailscale.exe" version
```
예상 출력: 버전 문자열이 출력됨 (명령이 없다고 나오면 재부팅 또는 새 PowerShell 창 필요 — PATH 갱신 지연).

- [ ] **Step 3: 🙋 로그인 (사람이 직접)**

```powershell
& "C:\Program Files\Tailscale\tailscale.exe" up
```
브라우저가 열리면 회사/개인 계정으로 로그인해서 이 기기를 tailnet에 등록한다.

- [ ] **Step 4: 연결 확인**

```powershell
& "C:\Program Files\Tailscale\tailscale.exe" status
```
예상 출력: 본인 PC가 `100.x.x.x` 형태의 Tailscale IP를 받은 상태로 표시됨.

---

### Task 2: 서버 PC — Tailscale 설치 및 로그인

**대상:** 서버 PC 🖥️서버 (AI가 원격 접근할 수 없으므로 사람이 직접 진행)

- [ ] **Step 1:** 서버 PC에서 브라우저로 https://tailscale.com/download/windows 접속, 설치 파일 다운로드·실행
- [ ] **Step 2:** 설치 완료 후 트레이 아이콘에서 로그인 — 본인 PC와 **같은 Tailscale 계정/조직**으로 로그인해야 같은 tailnet에 묶인다
- [ ] **Step 3:** 로그인 후 트레이 아이콘 클릭 → 자신의 Tailscale IP(`100.x.x.x`) 확인, 기록해두기

**검증 (본인 PC에서 🤖):**
```powershell
& "C:\Program Files\Tailscale\tailscale.exe" status
```
예상 출력: 서버 PC 호스트명이 목록에 나타남 (온라인 상태).

---

### Task 3: 본인 PC — Syncthing 설치

**대상:** 본인 PC 🤖

- [ ] **Step 1: winget으로 Syncthing 설치**

```powershell
winget install --id Syncthing.Syncthing -e --accept-source-agreements --accept-package-agreements
```

- [ ] **Step 2: Syncthing 실행 확인**

Syncthing은 설치 후 트레이에서 실행되며 웹 UI가 자동으로 열린다 (`http://127.0.0.1:8384`). 안 열리면:

```powershell
Start-Process "http://127.0.0.1:8384"
```

- [ ] **Step 3: 본인 PC의 Device ID 확인**

웹 UI 우측 상단 **작업(Actions) → 이 기기 표시(Show ID)** 에서 Device ID 복사해서 기록해둔다.

---

### Task 4: 서버 PC — Syncthing 설치

**대상:** 서버 PC 🖥️서버

- [ ] **Step 1:** 서버 PC에서 https://syncthing.net/downloads/ 에서 Windows용 설치, 또는 서버 PC에서도 `winget install --id Syncthing.Syncthing -e` 실행
- [ ] **Step 2:** 웹 UI(`http://127.0.0.1:8384`)에서 Device ID 확인, 기록
- [ ] **Step 3:** 서버 PC가 상시 켜져 있어야 하므로, Syncthing이 로그아웃 후에도 백그라운드로 계속 돌게 **시작 프로그램 등록** 확인 (설치 시 기본 등록됨 — 트레이 아이콘 우클릭 → 시작 시 실행 여부 확인)

---

### Task 5: 본인 PC ↔ 서버 PC Syncthing 기기 페어링

**대상:** 본인 PC 🙋 + 서버 PC 🖥️서버 (기기 승인은 반드시 양쪽에서 수동 확인해야 함 — 보안상 자동화 불가)

- [ ] **Step 1 (본인 PC):** 웹 UI → **기기 추가(Add Remote Device)** → 서버 PC의 Device ID 붙여넣기 → 기기 이름 `MainProject-Server`로 저장
- [ ] **Step 2 (서버 PC):** 잠시 후 서버 PC 웹 UI에 "새 기기가 연결을 시도합니다" 팝업이 뜸 → 본인 PC의 Device ID인지 확인 후 **승인**, 이름 `본인-PC`로 저장
- [ ] **Step 3 (검증, 본인 PC):** 웹 UI 좌측 하단에 `MainProject-Server`가 "연결됨(Connected, up to date)" 초록색으로 뜨면 성공. Tailscale IP를 통해 연결되므로 두 PC가 물리적으로 다른 네트워크에 있어도 잡혀야 정상.

---

### Task 6: Vault 폴더 구조 생성 (본인 PC)

**대상:** 본인 PC 🤖

- [ ] **Step 1: 폴더 생성**

```powershell
$vault = "C:\Users\user\MainProjectVault"
New-Item -ItemType Directory -Force -Path $vault | Out-Null
New-Item -ItemType Directory -Force -Path "$vault\Art-Planning\characters" | Out-Null
New-Item -ItemType Directory -Force -Path "$vault\Art-Planning\concept-refs" | Out-Null
New-Item -ItemType Directory -Force -Path "$vault\Art-Planning\planning" | Out-Null
New-Item -ItemType Directory -Force -Path "$vault\Meetings" | Out-Null
New-Item -ItemType Directory -Force -Path "$vault\Worklog" | Out-Null
New-Item -ItemType Directory -Force -Path "$vault\Core" | Out-Null
```

- [ ] **Step 2: 확인**

```powershell
Get-ChildItem -Recurse "C:\Users\user\MainProjectVault" | Select-Object FullName
```
예상 출력: `Art-Planning`, `Meetings`, `Worklog`, `Core` 및 하위 3개 폴더가 나열됨. (`MainProject-Docs`는 다음 Task에서 Syncthing이 자동으로 만든다 — 지금 미리 만들지 않는다, 폴더가 이미 있으면 Syncthing이 "폴더 마커 없음" 에러를 낼 수 있음)

---

### Task 7: Vault를 Syncthing 공유 폴더로 등록

**대상:** 본인 PC 🙋 + 서버 PC 🙋 (폴더 공유 승인은 수동)

- [ ] **Step 1 (본인 PC 웹 UI):** **폴더 추가(Add Folder)**
  - Folder Label: `MainProjectVault`
  - Folder Path: `C:\Users\user\MainProjectVault`
  - **공유(Sharing)** 탭에서 `MainProject-Server` 체크
  - 저장

- [ ] **Step 2 (서버 PC 웹 UI):** 폴더 공유 요청 팝업이 뜨면 **수락**
  - Folder Path를 서버 PC의 Vault 저장 위치로 지정 (예: `D:\TeamVault\MainProjectVault`)
  - 저장

- [ ] **Step 3 (검증, 본인 PC):** 웹 UI에서 `MainProjectVault` 폴더 상태가 "최신(Up to Date)"으로 뜨는지 확인. 본인 PC에서 테스트 파일 하나 만들어서(`$vault\test.md`에 아무 텍스트) 서버 PC의 대응 폴더에 몇 초 안에 나타나는지 확인 후 테스트 파일 삭제.

---

### Task 8: MainProject/Docs를 별도 Syncthing 공유 폴더로 등록

**대상:** 본인 PC 🙋 + 서버 PC 🙋

- [ ] **Step 1 (본인 PC 웹 UI):** **폴더 추가**
  - Folder Label: `MainProject-Docs`
  - Folder Path: `C:\Users\user\MainProject\Docs`
  - 공유 탭에서 `MainProject-Server` 체크
  - **무시 패턴(Ignore Patterns)** 에 아래 추가 (html 빌드 산출물 동기화 안 함):
    ```
    (?d)*.html
    ```
  - 저장

- [ ] **Step 2 (서버 PC 웹 UI):** 폴더 공유 요청 수락
  - Folder Path를 **Vault 폴더의 하위 경로**로 지정: 서버 Vault 경로가 `D:\TeamVault\MainProjectVault`라면 → `D:\TeamVault\MainProjectVault\MainProject-Docs`
  - 저장

- [ ] **Step 3 (검증, 본인 PC):**
```powershell
Get-ChildItem "C:\Users\user\MainProject\Docs"
```
와 서버 PC의 `MainProjectVault\MainProject-Docs` 폴더 내용이 몇 초 안에 일치하는지 확인 (design/tech/tasks 하위 폴더 존재 여부).

**주의:** 본인 PC 쪽에서는 이 폴더가 **Vault 밖의 git 작업트리 안**(`MainProject\Docs`)에 그대로 있고, 서버 PC 쪽에서만 Vault 안으로 들어간다 — 설계 문서의 "심볼릭 링크 미사용" 결정을 그대로 구현한 것.

---

### Task 9: Obsidian 설치 및 Vault 열기 (본인 PC)

**대상:** 본인 PC 🤖(설치) + 🙋(Vault 열기, GUI 필요)

- [ ] **Step 1: winget으로 Obsidian 설치**

```powershell
winget install --id Obsidian.Obsidian -e --accept-source-agreements --accept-package-agreements
```

- [ ] **Step 2: 🙋 Obsidian 실행 후 Vault 열기**
  - Obsidian 실행 → **Open folder as vault** → `C:\Users\user\MainProjectVault` 선택
  - 좌측 파일 트리에 `Art-Planning`, `Meetings`, `Worklog`, `Core`, `MainProject-Docs`(Task 8에서 서버 통해 동기화됨 — 본인 PC 쪽엔 아직 없을 수 있음, 아래 참고) 폴더가 보이는지 확인

**참고:** 본인 PC의 Vault 폴더 자체에는 `MainProject-Docs`가 없다(그건 `MainProject\Docs`에 원본이 있음). 본인 PC의 Obsidian에서 Docs 문서를 보고 싶으면, Vault 루트가 아니라 **`C:\Users\user\MainProject`를 별도 Vault로 열거나**, Obsidian의 "다른 vault 즐겨찾기"로 두 개를 등록해 전환하며 쓴다. 서버 PC와 향후 팀원 PC의 Obsidian에서는 Vault 하나 안에 `MainProject-Docs`가 자동으로 포함되어 보인다.

- [ ] **Step 3: 서버 PC에는 Obsidian 설치 불필요** — 서버는 Syncthing 저장소 역할만 하면 되므로 Obsidian GUI가 없어도 무방. (원한다면 나중에 설치해서 서버에서도 직접 열람 가능)

---

### Task 10: Whisper 플러그인 설치 및 GPU 로컬 모델 설정

**대상:** 본인 PC 🙋 (Obsidian 커뮤니티 플러그인은 GUI 설치만 지원, CLI 설치 경로 없음)

- [ ] **Step 1:** Obsidian → 설정(Settings) → **커뮤니티 플러그인(Community plugins)** → 찾아보기(Browse) → `Whisper` 검색 → 설치·활성화
- [ ] **Step 2:** 플러그인 설정에서 **로컬 모델(Local)** 백엔드 선택 (OpenAI API 등 클라우드 옵션 선택하지 않기 — 사내 회의록 외부 전송 방지)
- [ ] **Step 3:** GPU(RTX)를 활용하도록 모델 크기를 `large-v3` 계열로 설정, 저장 위치를 `Meetings/` 폴더로 지정
- [ ] **Step 4: 검증** — 짧은 테스트 음성(10초 내외)을 녹음하거나 업로드해서 전사 실행 → `Meetings/` 폴더에 날짜 파일이 생성되고 내용이 맞는지 확인

---

### Task 11: Core 폴더 초기 문서 시드

**대상:** 본인 PC 🤖

- [ ] **Step 1: 용어집/설계원칙 초기 노트 생성**

```powershell
$core = "C:\Users\user\MainProjectVault\Core"
@"
# 용어집

이 문서는 AI와 팀원이 같은 단어를 쓰기 위한 공유 사전이다.
MainProject 리포의 [CONTEXT.md](../MainProject-Docs/../../MainProject/CONTEXT.md) 내용과 중복되지 않도록,
Vault 전용으로 새로 생기는 용어만 여기에 추가한다.

## 용어 목록
(작성 예정)
"@ | Set-Content -Path "$core\glossary.md" -Encoding utf8

@"
# 설계 원칙 요약

MainProject의 핵심 설계 원칙 요약. 상세는 MainProject-Docs/tech/architecture.md 참고.

- UnitBase = 공통 상태+스탯만, 나머지는 컴포넌트 조립
- 네트워크 권한: 플레이어=클라(오너) / 보스·데미지·상태이상·장판=서버(호스트)
- 스킬/보스패턴 = ScriptableObject 데이터 주도
"@ | Set-Content -Path "$core\design-principles.md" -Encoding utf8

@"
# 결정사항 로그

날짜별로 팀이 합의한 주요 결정을 한 줄씩 追加한다. 형식: `YYYY-MM-DD: 결정 내용 (근거)`
"@ | Set-Content -Path "$core\decisions-log.md" -Encoding utf8
```

- [ ] **Step 2: 확인**

```powershell
Get-ChildItem "C:\Users\user\MainProjectVault\Core"
```
예상 출력: `glossary.md`, `design-principles.md`, `decisions-log.md` 3개 파일.

---

## 범위 밖 (다음 단계)

- 팀원(아트/기획자) PC 온보딩 — Task 1~5와 동일한 절차를 팀원 PC에서 반복 (Syncthing만 필요, git/Tailscale 로그인은 조직 계정 공유 방식 재검토 필요)
- Phase B(오늘의 할일 자동화) / C(AI 질의응답) / D(구현 후 체크리스트) — 설계 문서의 "범위 밖" 항목 그대로
