# AGENT.md — 프로젝트 온보딩 / 에이전트·팀원 공용 가이드

> 이 파일은 **필수**다. 사람 팀원과, 다른 모델/에이전트를 쓰는 팀원 모두 작업 전 이 문서를 먼저 읽는다.
> 상세는 `Docs/` 하위 문서를 참조.

## 1. 프로젝트 한 줄 요약
레이븐스워치류 **탑다운 협동 액션**. 7월 말 목표 = **"게임 시작 → 보스 연출 → 보스 전투"** 까지의
**리슨 서버 네트워크 수직 슬라이스**. 8월부터 맵/성장/일반 몹 확장.

- 인원: 프로그래머 3명. **PR 없음** — 각자 `development` 에 직접 병합(Docs/tech/workflow.md).
- 7월 스코프: 플레이어 **근거리(가붕이)/원거리(징크스) 1종씩** + **적은 보스(웰즈 & 23호)만**.

## 2. 엔진 / 실행
- **Unity 6000.3.16f1 고정** (팀 전원 동일 버전. Unity Hub에서 정확히 이 버전으로 열 것).
- 멀티플레이 테스트: **Multiplayer Play Mode(MPPM)** 로 에디터 1개에서 2~3인 검증.
- 네트워크: **NGO** 기반.
  - **공모전 제출·디버깅 = IPv4 직접통신**으로 멀티 플레이 구현.
  - **출시 = Steamworks API**(SteamDatagramRelay 등) 기능을 활용해 멀티 플레이 구현.

## 3. 버전 관리 (하이브리드)
- **대용량 아트 + 오디오 = SVN** / **코드·씬·프리팹·소형(캐시성) 에셋 = GitHub**.
- `.meta` 는 **에셋과 같은 VCS** 로 함께 관리 (아트 .meta는 SVN). GUID 깨지면 프리팹 참조 전부 깨짐.
- 브랜치: `main`(보호) ← `development` ← `feature/<area>`. 작업 단위는 작게.
- **PR 없음**: 본인이 `feature` → `development` 직접 병합·푸시. 다른 담당 영역을 건드리면 리뷰 대신
  `Docs/tech/handoff-<주제>.md` + 관련 문서 갱신으로 공유. 코어 인터페이스 변경은 사전 합의. (상세: Docs/tech/workflow.md)

## 4. 코어 설계 원칙 (요약)
- **UnitBase = 공통(기본 상태 + 스탯)만.** 이동/어빌리티/상태이상/스탯은 **컴포넌트로 조립**(상속 최소).
- **네트워크 권한**: 플레이어 이동/스킬 = **클라(오너) 권한** / 보스·데미지·상태이상·장판 = **서버(호스트) 권한**.
- **데이터 주도**: 스킬/빌드/보스 패턴은 ScriptableObject로 (머지 충돌·튜닝 편의).
- 상세: [Docs/tech/architecture.md](Docs/tech/architecture.md), [Docs/tech/networking.md](Docs/tech/networking.md)

## 5. 작업 분담
> 아래는 **2026-08-07 팀장 확정** 기준이다. 이전 판(민경=보스 기믹/일반몹, 은희=성장시스템 등)은 폐기.

- **은희**: 코어(UnitBase·상태이상·스탯/모디파이어) + 네트워크(NGO / IPv4 / SteamDatagramRelay / SceneManagement) + Player 제작.
- **경석(팀장)**: **몬스터 전체 단독** — 일반 몬스터·중간보스·보스의 FSM·기믹·프리팹·데이터.
  - 진행: **일반 몬스터 ✅ / 중간보스 ✅ / 보스 = 남은 것**(재작성 중 — [PLAN-boss-fsm.md](Docs/history/PLANS/PLAN-boss-fsm.md)).
- **민경**: **이펙트(VFX)**.

## 6. 문서 인덱스
- 설계(GDD): [Docs/design/](Docs/design/) — 보스 / 플레이어 / 상태이상 / 빌드
- 기술: [Docs/tech/](Docs/tech/) — 아키텍처 / 네트워크 / 컨벤션 / 워크플로우
- **플레이어 프리팹**: [Docs/tech/player-prefabs.md](Docs/tech/player-prefabs.md) — 단일 사실 원본.
  **`Player.prefab` = 역할 base, 캐릭터마다 그 Prefab Variant(`Player_Paladin.prefab`)가
  `Armature`·스킬·VFX 를 얹는다. 캐릭터는 스폰 전에 고른다.** 역할 동작은 base, 캐릭터 고유는 Variant 를 고친다.
  구 통짜 복제본은 스폰되지 않는다 — `Paladin` 은 `Player/Legacy/`, `Paladin_VFX` 는 `Player/Paladin/` 에 보관만 한다(2026-09-29).
  플레이어 프리팹·스폰·캐릭터 교체를 건드리기 전에 읽을 것.
- **데이터 테이블**: [Docs/tech/data-table.md](Docs/tech/data-table.md) — 기획 수치는 `GameData.xlsx`(SVN)가 원본.
  툴바 `데이터: 테이블/인스펙터` 로 Play 출처를 고르고, **빌드는 항상 xlsx 값**. 새 수치를 노출할 땐 `[DataTableSheet]`·`[DataTableIgnore]`.
- **튜토리얼 특성 선택**: [Docs/tech/tutorial-traits.md](Docs/tech/tutorial-traits.md) — 디렉터 API·S12 배치 순서·placeholder 특성·알려진 제약.
- **튜토리얼 QA**: [Docs/tech/tutorial-qa.md](Docs/tech/tutorial-qa.md) — 솔로·MPPM·회귀 Play 체크리스트·알려진 한계·병합 전 확인. 단계별 문서 목록은 [tutorial-director.md](Docs/tech/tutorial-director.md) §10.1.
- **로비 데모 부스 운영**: [Docs/tech/lobby-demo-flow.md](Docs/tech/lobby-demo-flow.md) — 흐름·모든 PC 공용 `demo-connection.json`(role auto — 이 PC IP = hostAddress 면 호스트, host/client 명시 우선)·같은 빌드 필수·대기 화면 문구·재접속·MPPM `Client` 태그·알려진 한계.
- **디버그 단축키**: [Docs/tech/debug-hotkeys.md](Docs/tech/debug-hotkeys.md) — 키 목록·겹침, 릴리스 빌드 포함 심볼 `DEV_HOTKEYS`(Standalone). 정식 릴리스 때 심볼만 지운다.
- **플레이어 스탯**: [Docs/tech/player-stats.md](Docs/tech/player-stats.md) — 공격력·이동속도·공격속도·방어력·최대 체력 5종 기본%·전체%(+공격력 가하는 피해%) 공식, 서버·클라 같은 종합(`PlayerStats`), 판정은 서버 값, 공격 요청마다 서버 2차 검증(경고만). 새 스탯 추가 절차 §3.
- **행동 입력 거부 피드백**: [Docs/tech/skill-reject-feedback.md](Docs/tech/skill-reject-feedback.md) — 거부 사유별 선입력 버퍼 처리 표(보관/버림), 오너 전용 이벤트 `CooldownRejected`·`ActionBlockedByEffect`(입력 1회=1회)·사운드 키 지정법(비우면 무음).
- **간파 연출 디렉터**: [Docs/tech/interrupt-presentation.md §6](Docs/tech/interrupt-presentation.md) — `NetworkManager.prefab` 의 `InterruptProductionDirector` 가 단일 진입점. 간파 스킬은 사건 4개(시도·판정·성공·실패)만 알리고, 스위치 10칸·시도/성공 슬로우 프로필·개인/공용 채널은 디렉터가 든다. 새 간파 연출은 `GlobalTimeScale.Started` 직접 구독 말고 디렉터 cue(`IInterruptCueSink`)로.
- **간파 성공 연출**: [Docs/tech/interrupt-presentation.md](Docs/tech/interrupt-presentation.md) — 시도 슬로우 → 성공 슬로우 재발동·히트스톱 → 슬로우 이벤트 목적 칸(공용 성공 신호) → 암순응 패스·`InterruptSuccessFx` 성공 이펙트 칸(Variant 가 소켓 연결)·오너 줌, 수치·렌더러 순서·알려진 제약.
- **대쉬 간파**: [Docs/tech/dash-interrupt.md](Docs/tech/dash-interrupt.md) — 대시 시작 후 창(기본 0.1초) 안 우클릭만 소비(창 밖은 기존 선입력 버퍼), 대시에 간파가 실려 대시 끝에 Skill 전이·판정(판정 시각 = max(대시 끝, 평소 Hit)), RPC 플래그 `dashInterrupt`(새 RPC 없음)·서버 창+허용오차, 상체 레이어 `DashInterruptUpper`, `PlayerDashData` 칸 3개·알려진 한계.
- 레포지토리 맵(자동생성): [Docs/tech/repo_map.md](Docs/tech/repo_map.md) — 클래스/인터페이스
  구조·상속·Facade 위임 관계 요약. 재생성은 `DevTools/RepoMap/`(`npm run generate`).
- 일정: `Project_MT 일정.xlsx` (담당: 이지원) — 팀 지식베이스 Vault `Core/project-schedule-2026.md` 스냅샷 참조

## 7. AI 협업 규칙 / 공유 이해도 문서
작업 전 기본적으로 이 문서를 먼저 읽고, 작업 성격에 따라 아래 문서도 함께 읽는다.

- [AIRULE.md](AIRULE.md): AI와 협업할 때의 진행 방식. 큰 작업은 바로 구현하지 말고 질문 → 계획 → 승인 → 구현 순서로 진행.
- [CONTEXT.md](CONTEXT.md): 프로젝트 용어와 도메인 언어. AI와 팀원이 같은 단어를 쓰기 위한 공유 사전.
- [PLAN.md](PLAN.md): 현재 큰 작업의 잠긴 계획. 중요한 작업은 구현 전 이 파일에 목표/접근/리스크/검증 방법을 정리하고 승인받는다.

비자명한 기능 추가, 네트워크/권한 변경, 보스/플레이어/상태이상/빌드/데이터 구조 변경은 `AIRULE.md`의 grill workflow를 따른다.