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
  - 예외: **간파 성공 = 클라(오너) 1차 판정, 서버 2차 검증** — [Docs/tech/interrupt-client-judge.md](Docs/tech/interrupt-client-judge.md).
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
- **맵씬 진행(옛 튜토리얼)**: [Docs/tech/tutorial-director.md](Docs/tech/tutorial-director.md) 맨 위 — 할 일 107(2026-10-27)로 `TutorialDirector` → `MapSceneDirector` 등 `Map/MapScene/`·`MapScene…` 이름, 존1~3 교육·스피너 연습·존4 교육은 `Map/Tutorial/Legacy/`·`Tutorial/Legacy/`(런타임 연결 끊음), 존1~3 진행은 `MapSceneZoneFlow`. 로비 공용(`Tutorial/Guide`·`Tutorial/Traits`·`TutorialCooldownReset`)은 이름 그대로.
- **특성 선택(36개 구조, 할 일 125)**: [Docs/tech/tutorial-traits.md](Docs/tech/tutorial-traits.md) — ID 해시·캐릭터×차수 고정 후보·목록 버전 · 수치 출처 `PlayerTraitModifiers`(`HasTrait` = 모드 변경 스위치) · 효과 10종·장부·CastId · 밀치기 `AttackInfo` 경로 한 곳·뇌진탕 ·
  카드(이름/분류/설명 `{필드}`·`{dmg}`·도움말·2차 상충 확인창) · Strings `Trait.<ID>.*` · 시트 `Traits_Paladin/Gunner/Assassin`(없으면 무시). 캐릭터 레인(126~128)은 `Traits/<캐릭터>/` 파일만 고친다(§11.6).
- **특성 강화 스킬 연출 이벤트(145)**: [Docs/tech/trait-effect-events.md](Docs/tech/trait-effect-events.md) — 이펙트는 `TraitSkillFx.Raised`(static event, 인자 `TraitFxEvent` = 특성·사건 Activated/Hit/Attached/Exploded·시전자·위치·대상·153 도형/포즈)만 구독. 전 피어 경로가 없는 서버 판정은 특성 공용 RPC 1개(`Player.TraitSkillFxRpc`, ClientsAndHost — 호스트 1번). 이벤트 표·한계·잔류 포격 구체 칸.
- **튜토리얼 QA**: [Docs/tech/tutorial-qa.md](Docs/tech/tutorial-qa.md) — 솔로·MPPM·회귀 Play 체크리스트·알려진 한계·병합 전 확인. 단계별 문서 목록은 [tutorial-director.md](Docs/tech/tutorial-director.md) §10.1.
- **로비 튜토리얼(157)**: [Docs/tech/lobby-training.md](Docs/tech/lobby-training.md)(흐름 L00~L41·서버 상태·Play 확인) + [handoff-lobby-tutorial-contract.md](Docs/tech/handoff-lobby-tutorial-contract.md)(158 표시 계약) — 단계표 SO `LobbyTutorialSteps.asset` = 진행 원본, 판정은 서버·표시는 전 피어(스냅샷 1개), 캐릭터 잠금 없음, 매 회차 L00 부터.
- **로비 데모 부스 운영**: [Docs/tech/lobby-demo-flow.md](Docs/tech/lobby-demo-flow.md) — 흐름·모든 PC 공용 `demo-connection.json`(role auto — 이 PC IP = hostAddress 면 호스트, host/client 명시 우선)·같은 빌드 필수·대기 화면 문구·재접속·MPPM `Client` 태그·알려진 한계.
- **로비 튜토리얼 UI(158)**: [Docs/tech/lobby-tutorial-ui.md](Docs/tech/lobby-tutorial-ui.md) — 입력 API `LobbyTutorialUI.Instance`(목표·대사·경로·벽 표식) · 157 상태는 바인더 `LobbyTutorialUiBinder` 한 곳만 읽음(과도기 어댑터 삭제) · 로비 씬 연결·경로 점 임시 배치 · 프리팹 2개와 저작 메뉴 · 별도 Overlay 캔버스(HUD 안 건드림) · 집결 민트 테두리 = `PartyGatherZone` 복제값 · 디버그 구동(화면 01–27) · 아트 `.meta` 원본은 Work4 · 문구 키(시트 행 r570).
- **포드 발사·착륙 애니 이벤트(198)**: [Docs/tech/pod-drop-anim-events.md](Docs/tech/pod-drop-anim-events.md) — `PodClipEvent` 종류 0~3(단계·가속 1·2·하강 시작) · FBX `.meta`(SVN) 이벤트 튜닝·저작 메뉴 · 로비 끝 페이드 = 하강 시작 + `launchEndWaitSeconds` · 이벤트 위치를 각 피어가 읽어 복제 시각으로 계산(새 복제 값 없음) · 맵 착륙 앞 구간 건너뛰기·페이드 인 · 레터박스·검은 화면 = 프리팹 `PodDropOverlay.prefab`(`PodDropSettings.overlayPrefab`, 코드는 띠 높이 anchor·페이드 알파·정렬 500만, 230 §5).
- **피해 숫자(플로팅 데미지)**: [Docs/tech/floating-damage-design.md](Docs/tech/floating-damage-design.md) — Overlay·이미지 숫자·몸 중심 앵커, 단발/누적·구간·필터·보호막 숫자, 줌과 무관(154 — 넣었다가 뺐음), 대상별 위치 = 프리팹 루트 `FloatingDamageAnchor`. 보면서 고치는 디버그 씬 `Assets/0.Scenes/Debug/DebugFloatingDamage.unity`(242, §12 — 네트워크 없이 ▶, 크기 = 설정 SO·위치 = 앵커 '프리팹에 저장').
- **공격 판정 도형(153)**: [Docs/tech/attack-shapes.md](Docs/tech/attack-shapes.md) — 프리팹 콜라이더 대신 코드 도형(부채꼴·원·직사각형·삼각형, 합집합), SO `AttackShapeSet`(`[SerializeReference]`, 행 `hitArea.shapes[i].칸`)·기준점 종류·판정 점(ClosestPoint 바닥 평면+높이)·대상 1회·특성 배율·이관 단계 1~6. 간파 상자(`InterruptAttack`)는 예외.
- **패널 배경 블러(201)**: [Docs/tech/panel-backdrop-blur.md](Docs/tech/panel-backdrop-blur.md) — 패널 콘텐츠 루트에 `PanelBackdropBlurTarget` 하나(참조 카운트 `PanelBackdropBlur.Acquire/Release`, 0.15초 보간) · 열린 동안 패널보다 아래 Overlay 캔버스(HUD 등)를 Screen Space Camera 로 잠깐 바꿔 PC_Renderer `PanelBackdropBlurFeature` 가 월드와 함께 실시간 블러, 패널만 선명 · 설정 SO `PanelBackdropBlurSettings` · MaskBlur(203)와 분리 · 캐릭터 선택 제외.
- **보스 처치 연출 = ZZZ 글리치(11-09)**: [PLAN-boss-victory-glitch.md](PLAN-boss-victory-glitch.md) 가 원본(맨 위 "지금 흐름 한눈에") — 처치 판정 즉시 글리치(흑백·호박색·보스 노이즈 UV)·처치자 하던 동작 ×0.5('처치자 평타' 클립 끝에서 끊음) → '백대쉬 시작' 마커 백대쉬(등장 연출 밀림 재사용) → 처치 연출 스킬(표시 전용, End 에서 ×0.1) → CRT 꺼짐 → 로비 전환 마커에서 로비 결과. 수치 = `Resources/BossVictory/BossVictoryGlitchSettings.asset` — 시각·길이는 트랙별 클립·마커 타임라인(`BossVictoryTimeline` — 카메라 컷도 트랙: 클립 시작 = 컷, 다음 컷까지 시작 → 끝 화면 이동, 기준 좌표계 저장, 창 'Scene 뷰로' 버튼, 컷별 벽 숨김, 255 · 노드 선택 → 창 아래 칸: 백대쉬 값·주목 블렌드·숨김 대상·트랙 모양 값·시간 배율, 255 후속 3 · 처치자 순서 = 평타 클립·백대쉬 시작·스킬 연출 시작·느려짐 시작 노드, 255 후속 4 · 보스 = 피격 애니(23호 ParryHit_InPlace)·정지·슬로우 노드, 255 후속 5 · 효과 세기·카메라 컷 클립 = 시작·중간·끝 키(클립 비율 위치·구간 곡선·컷 부드러운 경로, 255 후속 6) · 처치 연출 스킬 = 클립(캐릭터 클립 길이·프레임, 끝 끌어 자르면 그 프레임에서 멈춤, 255 후속 7) · 컷 사이 블렌드(컷 시작 전 쐐기, 앞 컷 → 들어오는 컷 월드 보간, 255 후속 8)), 편집 창 `Tools/Boss Victory/처치 연출 타임라인`(끌기·키 마름모(더블클릭 추가)·스냅·Undo, Play 중 디버그 씬 재생 헤드 스크럽 미리보기). 옛 카메라 무빙·주황·회색 원·프리셋은 `Legacy/`(연결 끊음, 기록은 [PLAN-boss-victory-cinematic.md](PLAN-boss-victory-cinematic.md)). 반복 디버그 씬(244) `0.Scenes/Debug/DebugBossVictory`(Dev Boot, 빌드 목록 제외) · 보스방·디렉터·23호 B 는 랩이 런타임 생성, 처치·결과 대기는 실게임 경로 · 결과 전환 차단 → 원상복구 → 반복(F7 다시·F8 정지·배속·막타 배치·팀원 0~2).
- **디버그 단축키**: [Docs/tech/debug-hotkeys.md](Docs/tech/debug-hotkeys.md) — 키 목록·겹침, 릴리스 빌드 포함 심볼 `DEV_HOTKEYS`(Standalone). 정식 릴리스 때 심볼만 지운다.
- **플레이어 스탯**: [Docs/tech/player-stats.md](Docs/tech/player-stats.md) — 공격력·이동속도·공격속도·방어력·최대 체력 5종 기본%·전체%(+공격력 가하는 피해%) 공식, 서버·클라 같은 종합(`PlayerStats`), 판정은 서버 값, 공격 요청마다 서버 2차 검증(경고만). 새 스탯 추가 절차 §3.
- **행동 입력 거부 피드백**: [Docs/tech/skill-reject-feedback.md](Docs/tech/skill-reject-feedback.md) — 거부 사유별 선입력 버퍼 처리 표(보관/버림), 오너 전용 이벤트 `CooldownRejected`·`ActionBlockedByEffect`(입력 1회=1회)·사운드 키 지정법(비우면 무음).
- **간파 연출 디렉터**: [Docs/tech/interrupt-presentation.md §6](Docs/tech/interrupt-presentation.md) — `NetworkManager.prefab` 의 `InterruptProductionDirector` 가 단일 진입점. 간파 스킬은 사건 4개(시도·판정·성공·실패)만 알리고, 스위치 12칸(A5 = 간파 암전·섬광 중 피해 숫자·머리 위 체력바(플레이어·몬스터·보스)·조준 표시 숨김, 154·179 후속 2·4 · A6·A7(제압 섬광 시퀀스·범위)은 11-04 할 일 211 로 삭제 · 카메라 대상 중심 = 제압 간파 성공 순간 모든 플레이어 카메라 중심을 대상 쪽으로(11-05 — 일반·대쉬·클릭 순간은 안 움직임), 196 §10)·시도/성공 슬로우 프로필·개인/공용 채널은 디렉터가 든다. 새 간파 연출은 `GlobalTimeScale.Started` 직접 구독 말고 디렉터 cue(`IInterruptCueSink`)로.
- **간파 클라 판정(191)**: [Docs/tech/interrupt-client-judge.md](Docs/tech/interrupt-client-judge.md) — `InterruptManager`(`NetworkManager.prefab`)가 세계 스냅샷·창 시간표·예측/판정 순수 함수(근접 = 누름 + Hit 지연, 대쉬 = max(대시 끝, Hit))를 들고, 오너가 Hit 타이밍에 판정 → 신고 → 서버 검증(허용 오차 `SlowMotion` 시트)·선착순/동시 0.05초·보스 효과 1회. 연출 cue·D10 암전 제외·QA 기록(`QA/191interrupt-…`)·23호 창 여닫는 곳·알려진 한계. QA 봇 간파는 할 일 193.
- **간파 스킬 캐릭터 VFX 목록·랩 개별 토글(238)**: [Docs/tech/interrupt-vfx-list.md](Docs/tech/interrupt-vfx-list.md) — 팔라딘 4·거너 1·어쌔신 4 VFX(프리팹·에셋·사건·스폰 코드·디렉터 칸 표, 전부 전 피어, 공용 간파 연출 제외) · 공용 게이트 `InterruptVfxToggles`(정적·이 피어만·저장 안 함·릴리스 늘 켬)를 스폰 지점·`EffectAnimEvents` 가 조회 · DebugInterruptLab 섹션 '캐릭터 VFX 개별 켜고 끄기'.
- **간파 연출 4단계(192)**: [Docs/tech/interrupt-presentation.md §8](Docs/tech/interrupt-presentation.md) — 실패/간파/대쉬 간파/제압 간파. 순수 선택 함수 `InterruptPresentationTierRules`·사건 `Variant`(Normal/Dash)·슬로우 사건 1바이트 플래그(bit0 제압·bit1 대쉬, 새 RPC 없음). **11-04(할 일 211) 제압 간파 = V3, 시퀀스는 Legacy** — 제압도 일반·대쉬와 같은 적중 라이트(`suppressTier` 기본 V3·나), 오너 암전·제압 예측·암순응 컷 없음(단계 판정·표식 비트는 그대로). 랩 단계 버튼 §8.5.
- **간파 연출 단계별 강화(219)**: [Docs/tech/interrupt-presentation.md §9.9](Docs/tech/interrupt-presentation.md) — 1단계 일반(V3 그대로) < 2단계 대쉬 < 3단계 제압(×2.5·화이트아웃 스위치 `suppressWhiteout`·제압 전용 성공 슬로우 `interruptSuppressSuccessProfile`). 대쉬·제압 = V3 복제 SO·프리팹(`V3/Dash`·`V3/Suppress`, 디렉터 칸 비면 1단계). 오너는 판정 순간 대상 제압 칸 복제값으로 3단계를 틀고 서버 표식은 보험(다르면 보정).
- **간파 적중 라이트 연출 V1·V2(194)**: [Docs/tech/interrupt-presentation.md §9](Docs/tech/interrupt-presentation.md) — 일반·대쉬·제압 단계마다 버전(V0 기존 ②~⑥ / V1 포인트 라이트·암순응·블룸·불꽃·파편·빛줄기 / V2 V1 + 화이트아웃 3프레임 오너만)·암순응 가(화면 패스)/나(태양광·환경광), 기본 일반·대쉬·제압 모두 V3·나(제압은 11-04 할 일 211 — 시퀀스는 Legacy, §9.7 · V0~V2 는 랩 버튼으로), 전부 실시간·모든 피어. 수치 SO `InterruptHitLightLook`·프리팹 `FX_InterruptHitLight`(저작 메뉴 `Tools/Interrupt/적중 라이트 연출 에셋 만들기 (194)`)·23호 차징 태양광과 배율 공유 `SceneLightDim`·랩 버전 버튼 §9.4.
- **간파 연출(132)**: [Docs/tech/interrupt-presentation.md](Docs/tech/interrupt-presentation.md) §0 — 예측 3상태(대상 없음/약/강 — 191부터 `InterruptManager.Predict`, 결론은 오너 판정 cue) → 강 = 오너 마스크 암전(비트 4·HUD 덮개)+빛 모임, 성공 = 히트스톱·적중 섬광 ②~⑥(스타버스트 렌즈 플레어·화이트아웃·스미어 → 섬광 순간 0.133s → 색수차 블러·가라앉음, 끝 0.433s)·팀원 암순응. 스위치 A1~A4 켬·B/C/E 꺼짐, 수치·누가 보나·덮어쓸 텍스처 칸·알려진 제약.
  **제압 간파(179, §7) — 11-04(할 일 211) 제압 간파 = V3, 시퀀스는 Legacy.** 옛 애니메이션 섬광 시퀀스(컷 SO `SuppressFlashSequence`·셰이더·재질·렌더 패스·테스트)는 각 `Legacy/` 폴더로 옮겨(guid 유지) 런타임 연결을 끊었다(A6·A7·섬광 줄이기 PlayerPrefs 삭제). 표식(슬로우 Start 이벤트 1바이트, 새 RPC 없음)은 그대로.
- **대쉬 간파**: [Docs/tech/dash-interrupt.md](Docs/tech/dash-interrupt.md) — 대시 시작 후 창(기본 0.1초) 안 우클릭만 소비(창 밖은 기존 선입력 버퍼), 대시에 간파가 실려 대시 끝에 Skill 전이·판정(판정 시각 = max(대시 끝, 평소 Hit) — 191부터 판정은 `InterruptManager`, 대시 끝 = 공용 스윕), RPC 플래그 `dashInterrupt`(새 RPC 없음)·서버 창+허용오차, 상체 레이어 `DashInterruptUpper`, `PlayerDashData` 칸 3개·알려진 한계.
- **MCP RunCommand 씬 dirty(190)**: [Docs/tech/mcp-runcommand-scene-dirty.md](Docs/tech/mcp-runcommand-scene-dirty.md) — RunCommand 끝 `Undo.CollapseUndoOperations` 가 레이아웃 구동 RectTransform 값으로 씬을 dirty 로 만들던 것을 에디터 훅 `McpRunCommandDrivenUndoFilter` 가 그 구동 수정만 버려 막음. 실제 변경·기존 dirty 는 그대로.
- **사운드 명세**: [Docs/design/sound-spec/README.md](Docs/design/sound-spec/README.md) — 소리가 필요한 지점 353행(플레이어 3종·공용 전투·몬스터·중간보스·23호·UI·흐름/BGM/환경음). 행마다 키·트리거·듣는 사람·2D/3D 감쇠·길이·영문 생성 프롬프트·녹화 지시·현재 상태. 등록 경로는 xlsx `Sound` 시트 → `Tools/Data/사운드 동기화`.
- 레포지토리 맵(자동생성): [Docs/tech/repo_map.md](Docs/tech/repo_map.md) — 클래스/인터페이스
  구조·상속·Facade 위임 관계 요약. 재생성은 `DevTools/RepoMap/`(`npm run generate`).
- 일정: `Project_MT 일정.xlsx` (담당: 이지원) — 팀 지식베이스 Vault `Core/project-schedule-2026.md` 스냅샷 참조

## 7. AI 협업 규칙 / 공유 이해도 문서
작업 전 기본적으로 이 문서를 먼저 읽고, 작업 성격에 따라 아래 문서도 함께 읽는다.

- [AIRULE.md](AIRULE.md): AI와 협업할 때의 진행 방식. 큰 작업은 바로 구현하지 말고 질문 → 계획 → 승인 → 구현 순서로 진행.
- [CONTEXT.md](CONTEXT.md): 프로젝트 용어와 도메인 언어. AI와 팀원이 같은 단어를 쓰기 위한 공유 사전.
- [PLAN.md](PLAN.md): 현재 큰 작업의 잠긴 계획. 중요한 작업은 구현 전 이 파일에 목표/접근/리스크/검증 방법을 정리하고 승인받는다.

비자명한 기능 추가, 네트워크/권한 변경, 보스/플레이어/상태이상/빌드/데이터 구조 변경은 `AIRULE.md`의 grill workflow를 따른다.
