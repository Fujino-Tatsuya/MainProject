# 팀 지식베이스 Vault 설계 (Obsidian + Syncthing + Tailscale)

> 목적: 기획/아트/프로그래밍/회의록을 한곳에 모아 팀 전원(비개발자 포함)과 AI가 같은 컨텍스트를 보게 하고,
> 이후(Phase B~D) 오늘의 할일 자동화·AI 질의응답·구현 후 체크리스트로 확장하기 위한 기반 인프라.
> 이 문서는 Phase A(지식베이스 구조)의 설계다. AIRULE.md의 grill 단계를 거쳐 합의된 내용을 정리했다.

## 배경

- MainProject 리포에는 이미 `Docs/design`, `Docs/tech`, `Docs/tasks` 마크다운 문서가 있다(git 관리).
- 아트/기획 원본 자산(PSD·이미지 등)은 SVN에 있다.
- 회의록은 지금까지 별도 정리 체계가 없었다.
- 서버 PC(Windows, 상시 켜짐)는 이미 준비되어 있고 접근 가능하다.
- 본인 PC·서버 PC 모두 Tailscale/Syncthing 미설치 상태에서 시작한다.
- 본인 PC에는 NVIDIA RTX GPU가 있어 Whisper 로컬 전사를 GPU로 돌릴 수 있다.

## 아키텍처

```
[서버 PC (Windows, 상시 켜짐)]
   └── Vault (Syncthing 마스터 사본)
[본인 PC (프로그래머)]
   ├── Tailscale + Syncthing + Obsidian 설치
   ├── MainProject 리포 (git, 그대로 유지 — Syncthing이 건드리지 않음)
   └── Vault (Syncthing으로 서버와 동기화)
[향후: 아트/기획자 PC] — Vault만 추가 연결 (git 불필요, Syncthing만 설치)
```

- **Tailscale**: 서버 PC·본인 PC(향후 팀원 PC)를 하나의 프라이빗 네트워크로 연결하는 역할만 한다.
- **Syncthing**: 실제 파일 동기화를 담당한다. Vault 폴더와 `MainProject/Docs` 폴더를 각각 별도의 Syncthing 공유 폴더로 등록한다.

### Docs 연동 방식 (심볼릭 링크 미사용)

`MainProject/Docs`를 Vault 안에 심볼릭 링크로 걸지 않는다. Syncthing은 심볼릭 링크를 경로 문자열 그대로 동기화하기 때문에, 그 경로가 존재하지 않는 서버·다른 팀원 PC에서는 깨진 링크가 된다.

대신 `MainProject/Docs`를 **별도의 Syncthing 공유 폴더**로 등록한다.

- 본인 PC: Syncthing이 `C:\Users\user\MainProject\Docs`를 그 자리 그대로(복사 없이) 감시. git 작업트리 원본이 곧 동기화 소스.
- 서버 PC / 향후 팀원 PC: 같은 Syncthing 폴더를 받는 위치를 각자 Vault 안의 `MainProject-Docs/` 하위 폴더로 지정.

**알려진 리스크**: 팀원이 Vault 안에서 Docs 파일을 직접 고치면 본인 PC의 git 작업트리에 미커밋 변경으로 반영된다. 동시에 본인이 로컬에서 같은 파일을 편집 중이면 Syncthing 충돌 파일(`*.sync-conflict-*`)이 생길 수 있다. 우선은 "Docs 수정 전에는 담당자에게 알리고 편집" 정도의 느슨한 룰로 대응하고, 충돌이 잦아지면 재검토한다.

## Vault 폴더 구조

```
Vault/
├── MainProject-Docs/     ← Syncthing으로 git repo Docs/(design·tech·tasks) 실시간 반영
├── Art-Planning/
│   ├── characters/       ← 캐릭터별 상태 노트(의도·진행 단계) + SVN 경로 링크 + 저해상도 미리보기
│   ├── concept-refs/     ← 참고자료 요약 + 저해상도 이미지
│   └── planning/         ← 기획 관련 별도 문서
├── Meetings/             ← Whisper 플러그인 전사 결과 (YYYY-MM-DD 회의명.md)
├── Worklog/              ← 팀원별 "오늘 한 일" 기록 (Phase B: Daily Todo 자동화의 근거자료로 재사용)
└── Core/                 ← AI가 항상 참고할 핵심 개념 노드 — 용어집·설계원칙·주요 결정사항 로그
```

### Art-Planning 폴더 원칙

아트/기획 원본(PSD·고해상도 이미지)은 계속 SVN에만 둔다. Vault에는 원본을 통째로 가져오지 않고:

- 이 에셋이 지금 뭘 의도하는지, 어느 단계인지를 담은 **마크다운 요약 노트**
- 필요하면 **저해상도 미리보기 이미지**(JPG/PNG)만

만 저장한다. 원본은 SVN 경로만 링크로 남긴다. AI는 PSD 원본을 직접 읽지 못하므로, 요약 노트가 AI에게는 사실상 유일한 접점이다.

## 회의록 파이프라인

- Obsidian 커뮤니티 **Whisper 플러그인**을 설치한다.
- 본인 PC에 NVIDIA RTX GPU가 있으므로, 로컬 GPU 모델(large-v3급)로 설정해 사내 회의 내용이 외부로 나가지 않게 한다.
- 녹음/전사 결과는 자동으로 `Meetings/` 폴더에 날짜별 파일로 저장한다.
- 후처리(LLM 요약/액션아이템 추출)는 이번 Phase A 범위에는 포함하지 않고, 이후 필요 시 추가한다.

## 셋업 순서 (구현 계획에서 상세화)

1. 본인 PC + 서버 PC에 Tailscale 설치·연결
2. 양쪽에 Syncthing 설치, Vault 폴더 + `MainProject/Docs` 폴더 각각 공유 폴더로 등록
3. Obsidian 설치, Vault 열기, 위 폴더 구조 생성
4. Whisper 커뮤니티 플러그인 설치 + GPU 로컬 모델 설정
5. `Core/` 폴더에 초기 용어집·설계원칙 문서 시드

## 범위 밖 (다음 Phase로 미룸)

- **Phase B** — 오늘의 할일 자동화(Daily Todo Reminder를 MainProject에 맞게 세팅)
- **Phase C** — AI 질의응답(구현 중 궁금한 점을 Vault 근거로 AI에게 물어보는 채팅)
- **Phase D** — 구현 후 체크리스트(AI가 만든 diff에서 개발자가 놓쳤을 만한 부분을 짚어주는 체크리스트)
- 팀원(아트/기획자) 온보딩 — Phase A 인프라가 안정화된 뒤 진행
- 회의록 후처리(LLM 요약/액션아이템 자동 추출)
