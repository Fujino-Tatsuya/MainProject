# 간파 연출 — 시도 강·약 · 오너 암전 · 빛 모임 · 적중 섬광 · 슬로우·히트스톱·암순응·성공 이펙트 칸·오너 줌

> 🔹 할 일 132(2026-10-28, 브랜치 `feature/InterruptPresentation2`): 시도를 **예측 3상태(대상 없음/약/강)** 로 나누고, 강 시도에 **오너 암전·빛 모임**,
> 성공에 **적중 섬광 1~4단계** 를 얹었다. 흐름 [§0](#0-132-체계--예측-3상태강약오너-암전빛-모임적중-섬광-할-일-132) 이 이 문서의 맨 앞 사실이다.
> 🔹 할 일 118: 간파 연출은 **간파 연출 디렉터 `InterruptProductionDirector`** 가 단일 진입점이다 — 사건 흐름·개인/공용 채널·스위치/프로필 위치는 [§6](#6-간파-연출-디렉터--interruptproductiondirector-할-일-118).
> 🔹 스위치 10칸(디렉터): 132 단계 6 부터 **A1 히트스톱·A2 암순응·A3 오너 줌·A4 성공 이펙트 칸 = 켬**, B·C·E(캐릭터별·맞은 쪽 섬광) = 꺼짐 — [§6.4](#64-스위치프로필-위치).
>
> 할 일 87(C1~C5)의 단일 사실 원본. 슬로우 모션 레이어 자체(시간 도메인·서버 권한·last-wins·컷신 리셋)는
> [PLAN-interrupt-slowmo.md](../history/PLANS/PLAN-interrupt-slowmo.md) 가 원본이고, 여기서는 **간파에 얹은 연출**만 다룬다.
> 수치 원본은 `GameData.xlsx` `SlowMotion` 시트([data-table.md](data-table.md)). 계획서: `_lane-artifacts/MainProject-Worktree4/132/132-간파연출-계획.md`(레포 밖).
> 다른 담당 영역 공유: [handoff-interrupt-132-monster.md](handoff-interrupt-132-monster.md)(경석) · [handoff-interrupt-132-vfx.md](handoff-interrupt-132-vfx.md)(민경).

## 0. 132 체계 — 예측 3상태·강/약·오너 암전·빛 모임·적중 섬광 (할 일 132)

### 0.1 상태와 연출

| 상태 | 판단 | 연출 |
|---|---|---|
| 시도 · **대상 없음** | 사거리·각도 안 간파 대상 없음 | **아무것도 없음**(슬로우·줌·암전·빛 모임 없음 — 132 이전 '예측 빗나감'과 같음) |
| 시도 · **약**(실패 예측) | 대상은 있지만 창이 닫혀 있음 / 판정 시각 전에 창이 닫힐 것 | 약 슬로우(`interruptWeakProfile`)·약 줌만 |
| 시도 · **강**(성공 예측) | 판정 시각까지 창이 버틸 것 | 강 슬로우(`interruptProfile`)·강 줌 + **오너 암전** + **빛 모임** |
| 성공(서버 확정) | 카운터 창 소비 | 히트스톱(A1) 시작 = 섬광 1단계 → 2 → 3 → **섬광 순간**(오너 암전 즉시 끔·월드 섬광·팀원 암순응) → 4단계 복귀 · 성공 슬로우 |
| 실패(서버 확정) | 판정 빗나감·판정 전 종료·거부 | 슬로우 실패 복귀 + 오너 암전 0.2초에 걷음·빛 모임 정지. 섬광 없음 |

### 0.2 흐름

```
간파 입력(오너 — 근접 Cast / 대쉬 TryUseDashInterrupt)
  ├─ [오너 클라] 즉시 예측(PlayerInterruptSkillBase.TryPredictOwnerAttempt — 요청 RPC 보다 먼저)
  │     대상 = InterruptTargetRegistry 캐시(오버랩 없음) · 창 = IInterruptWindowReplica 복제값 + InterruptWindowReplicaTracker
  │     → Director.LocalOwnerAttempt(forecast) → 오너 연출 상태 기계 InterruptOwnerPresentation
  │        강 = 오너 암전 0→1(ownerDimInDuration 0.1s, 실시간) + 빛 모임 시작 / 약·대상 없음 = 아무것도 안 켬
  │
  └─ 요청 RPC ─▶ [서버] 승인 → OnServerStart → 같은 예측(InterruptAttemptPrediction.BuildInput — 창 값 출처만 서버)
        → Director.ServerAttempt → 대상 없음 = 슬로우 없음(발동 번호 0) / 약 = AttemptWeak / 강 = Attempt
        → 판정(ResolveHit) → ServerResolve(빗나감 = ServerFail) → 성공이면 ServerSuccess(성공 슬로우 + A1)
        │
        ▼ 각 피어 GlobalTimeScale.Started/Failed/ForcedReset — 유일한 구독자 = 디렉터
  [오너 화면]   ServerStarted(purpose)
                 · 서버 목적 ≠ 예측(예: 오너 강 ↔ 서버 약) → '예측 빗나감' = 실패처럼 걷기(ownerDimOutDuration 0.2s) · 빛 모임 정지
                 · Success → 성공 단계: 오너 암전 유지 → 섬광 순간에 즉시 끔(램프 없음)
               Failed(발동 번호) / 판정 전 종료(LocalOwnerEnded) → 걷기 · 오너 애니 Hit 이벤트 → 빛 모임 정지
  [팀원 화면]   남의 강 시도(Attempt) → 발동자 소켓에서 빛 모임 → 발동자 Hit(PeerCasterHit)·실패·성공·리셋에 정지
               시도 단계에 그 외 변화 없음
  [모든 화면]   Success → 적중 섬광 진행기(InterruptHitFlashSequencer, 실시간)
                 0.0s 1단계(오너 오버레이, 아래 조건) … 0.3s 섬광 순간 = 월드 섬광(FX_InterruptHitFlash) + 암순응 … 0.4s 끝
                 A4 성공 이펙트 칸은 Started 즉시(§2.4)
```

- **예측 함수 하나**(`DashInterruptPredictor`, `Assets/1.Scripts/Player/Dash/DashInterruptPrediction.cs`) — 근접·대쉬 공용.
  판정 시각 = 근접 `지금 + 준비 시간`, 대쉬 `max(예상 대시 끝, 평소 Hit)`. 준비 시간 = 이 피어 관측값 > 데이터 HitDelay > 모름(0).
  남은 창 = 창 추정 길이(과거 관측) − 열린 뒤 경과. **모르면 '열려 있음'만으로 강**.
- **대상 판정**(`InterruptTargetGeometry`, 새 수치 칸 없음): ① 판정 위치(근접 = 지금, 대쉬 = 예상 대시 끝) → 대상 콜라이더 표면 수평 거리 ≤ 간파 사거리(HUD 강조 사거리와 같은 값)
  ② 대상 정면 허용각 안(23호 `counterFrontAngle` 등 기존 판정값, 정면 판정 없는 대상 = 180) ③ 공격 방향(근접 = 시전 방향, 대쉬 = 대시 방향)이 대상을 향함(반각 = min(정면 허용각, 90)).
- **대쉬 간파 예측 시점 = 우클릭을 누른 순간**(`InterruptAttemptPrediction.AtPress` 가 처리 프레임 값을 누른 뒤 경과만큼 되돌림). 대쉬 → 우클릭 순서만([dash-interrupt.md §2.1](dash-interrupt.md)).
- **대상 캐시** `InterruptTargetRegistry`(서버·클라 같은 규칙): 등록 = 대상 스폰(모든 피어 — 23호 = 보스전 시작 Spawn, 중간보스·스파링 봇 = `MonsterBase`, 허수아비 = `TrainingDummyInterrupt`),
  해제 = 디스폰·사망(MonsterBase Dead)·씬 언로드. 실제 판정(`ResolveHit` 오버랩)은 캐시를 쓰지 않는다.
- **서버가 슬로우 강·약을 정한다**(`Time.timeScale` 은 전 피어 공용). 오너 클라 예측은 암전·빛 모임만 켠다 — 서버 결과가 다르면 실패 규칙으로 걷는다.
  오너가 약·대상 없음으로 봤는데 서버가 강이면 오너 화면은 암전·빛 모임 없이 강 슬로우·줌만 받는다(뒤늦게 켜지 않는다).

### 0.3 누가 보나

| 연출 | 오너(간파한 사람) | 팀원 | 칸 |
|---|---|---|---|
| 시도 슬로우 강·약 · 성공 슬로우 · 히트스톱 | ✓ | ✓ | 서버 타임라인(A1 = 히트스톱) |
| 오너 줌(강 0.7 / 약 0.93 / 성공 0.85) | ✓ | — | A3 |
| 오너 암전(마스크) | ✓ 강 예측 때만 | — | 칸 없음(§0.6) |
| 빛 모임(월드) | ✓ 입력 순간 강 예측 | ✓ 서버 강 시도 슬로우 받을 때 | 칸 없음 |
| 섬광 1~3단계 + 4단계 걷힘(화면 오버레이) | ✓ 성공 순간 오너 암전이 켜져 있었을 때(= 강 시도 성공) | — | 칸 없음 |
| 월드 섬광(적중 지점 파랑·흰 + 블룸) | ✓ 섬광 순간 | ✓ 섬광 순간 | 칸 없음 |
| 휘도 암순응 | 오버레이가 돌았으면 없음(두 번 어두워짐 방지) · 약 시도 성공이면 self 강도 | others 강도(0.35), 섬광 순간 | A2 |
| 성공 이펙트 칸 casterFx/targetFx | ✓ | ✓ | A4 |

### 0.4 시간표 (기본값, 전부 실시간)

| 구간 | 길이 | 칸(디렉터) |
|---|---|---|
| 오너 암전 들어옴(강 입력 순간 0 → 1) | 0.1s | `ownerDimInDuration` |
| 오너 암전 걷힘(예측 빗나감·실패·판정 전 종료) | 0.2s | `ownerDimOutDuration` |
| 섬광 1단계 — 검은 배경 위 흰 폭발 | 0.1s | `hitFlashBurstDuration` |
| 섬광 2단계 — 빛 갈라짐(스타버스트) + 원형 충격파 | 0.1s | `hitFlashGlareDuration` |
| 섬광 3단계 — 화면 전체 흰 반전(+검은 집중선) · 끝 = **섬광 순간** | 0.1s | `hitFlashInvertDuration` |
| 섬광 4단계 — 실제 화면 복귀(3단계 그림이 걷힘) | 0.1s | `hitFlashReturnDuration` |
| 오너 암전 끄는 때(성공) = 1~3단계 합 | 0.3s | 계산값 `OwnerSuccessDimReleaseDelay` — 따로 칸 없음 |

- 1단계 시작 = 성공 `Started` 순간 = 히트스톱 시작 순간(히트스톱 길이 0 이어도 같다). 각 길이 0 = 그 단계 건너뜀.
- 빛 모임은 입력 순간 → 오너 애니 Hit 이벤트까지(기존 Hit 이벤트 재사용, SO 타이머 칸 없음).

### 0.5 새 수치 칸 — 전부 `NetworkManager.prefab › InterruptProductionDirector`

| 칸 | 기본 | 누구 값 | xlsx |
|---|---|---|---|
| `interruptProfile.*`(강) | scale 0.15 · hold 0.5 · enter 0.05 · exit 0.25 · failExit 0.1 · hitStop 0/0.02 | 서버 | 기존 열(옛 시도 자리) — xlsx 값이 132 이전(0.3·0.35)이면 🟨 **메인이 xlsx 갱신 필요** |
| `interruptWeakProfile.*`(약) | scale 0.5 · hold 0.2 · enter 0.05 · exit 0.25 · failExit 0.1 · hitStop 0/0.02 | 서버 | 🟨 **반영 예정**(메인 — 열이 없는 동안 프리팹 값) |
| `interruptZoomFactor`(강 줌) | 0.7 | 각 피어 | 🟨 **반영 예정** |
| `interruptWeakZoomFactor`(약 줌) | 0.93 | 각 피어 | 🟨 **반영 예정** |
| `ownerDimInDuration` · `ownerDimOutDuration` | 0.1 · 0.2 | 각 피어 | `[DataTableIgnore]` — 프리팹이 원본 |
| `ownerDimColor` · `ownerDimOpacity` | 검정 · 1(100%) — 예: 남색 0.9 | 각 피어 | `[DataTableIgnore]`(색은 테이블 열 아님) |
| `hitFlashBurstDuration` · `GlareDuration` · `InvertDuration` · `ReturnDuration` | 0.1 · 0.1 · 0.1 · 0.1 | 각 피어 | `[DataTableIgnore]` |
| `hitFlashSpeedLines` | 켬 | 각 피어 | `[DataTableIgnore]` |
| `hitFlashOverlayMaterial` | `M_InterruptHitFlashOverlay` | 각 피어 | 참조 — 비우면 오버레이 없음 |
| `hitFlashWorldVfx` | `FX_InterruptHitFlash` | 각 피어 | 참조 — 비우면 월드 섬광 없음 |
| `hitFlashWorldScale` · `hitFlashHeightOffset` | 1 · 1m | 각 피어 | `[DataTableIgnore]` |

- 오너 암전·섬광 칸들은 `NetworkManager.prefab` YAML 에 아직 써 있지 않아 **코드 기본값**이 쓰인다(인스펙터에서 바꾸고 저장하면 그때 써진다).
- 빛 모임 시간 기준: `FX_InterruptLightGather` 의 `InterruptLightGatherVfx.timeBase`(기본 **실시간** — 강 슬로우 0.15 중에도 같은 속도). 소켓은 §0.7.

### 0.6 스위치 칸이 없는 새 연출 (132 단계 6 판단)

오너 암전·빛 모임·적중 섬광에는 디렉터 cue 칸을 **더하지 않았다**(10칸 유지). 셋 다 132 체계의 본체라 끌 일이 튜닝·디버그뿐이고,
상태 기계(강 예측 → 암전 → 성공 때 암전이 켜져 있었으면 오버레이)에 서로 물려 있어 칸 하나로 일부만 끄면 조건이 꼬인다. 끄고 싶을 때:
- 섬광 오버레이 = `hitFlashOverlayMaterial` 비움 · 월드 섬광 = `hitFlashWorldVfx` 비움(섬광 순간·암순응은 그대로 돈다).
- 오너 암전 = `ownerDimOpacity` 0(상태 기계·오버레이 조건은 그대로 — 화면만 안 덮인다).
- 빛 모임 = `Player.prefab` 루트 `InterruptLightGatherSocket.vfxPrefab` 비움(로컬, 저장 금지).
필요해지면 §6.6 순서로 cue 를 더한다(기본 켬).

### 0.7 렌더러 · 에셋 위치

**오너 마스크 암전**(`DarkAdaptationFeature` 한 기능 두 모드 — 모드별 강도 0 이면 패스 없음):
- 휘도 암순응(팀원·기존 A2) = 패스 0, 마스크 암전(오너) = 패스 1 `MaskCover`(같은 `_shader` 칸).
- 남길 렌더러 = **렌더링 레이어 비트 4**(`InterruptOwnerMaskLayers` — 비트 0 = 조명, 1 = 데칼, 2·3 = 실루엣, 8 = Ground Layer).
  암전 0 → 양수 순간 오너 캐릭터 하위 전부·이번 시도 간파 대상·`KeepInOwnerDim` 루트(빛 모임)·대쉬 잔상에 비트 4 를 **더하고**(대입 아님), 0 이 되면 원래 없던 비트만 지운다.
- 마스크 패스: 비트 4 렌더러만 원래 머티리얼로 투명 검정 위에 다시 그려(카메라 깊이로 가림) 덮개 알파(1 − 남김)를 외부 RT 에 쓴다.
- **HUD 덮기**: 최상위 Screen Space Overlay 캔버스 RawImage = 덮개 RT × 칸 색 × 불투명도 × 강도 — 월드·HUD 전부 덮고 남길 픽셀만 뚫린다.
  `sortingOrder` = 오너 암전 덮개 **short.MaxValue − 2** / 적중 섬광 오버레이 **short.MaxValue − 1**(그 위). 캔버스·RT·재질 사본은 첫 사용 때 한 번 만든다.
- 대쉬 잔상: SVN `DashAfterimage.cs` 가 Ghost 생성 때 리터럴 `1u << 4` 를 더한다(SVN 커밋 대기 — §5).

**새 VFX(git, `Assets/5.VFX/Interrupt/`)** — 셰이더/파티클 절차 생성, 외부 이미지 없음:

| 연출 | 에셋 | 셰이더 | 덮어쓸 텍스처 칸 |
|---|---|---|---|
| 빛 모임(흰 빛 기둥 + 모여드는 입자 + 중심 빛) | `LightGather/FX_InterruptLightGather.prefab` · `M_InterruptLightGather_Pillar`·`_Mote` | `BeaverLobby/VFX/InterruptLightGather`(가산 언릿, HDR `_Color` → 블룸) | 재질마다 `_OverrideTex` |
| 월드 섬광(4단계 — 흰 중심·파란 번짐·광선·링·불티) | `HitFlash/FX_InterruptHitFlash.prefab` · `M_InterruptHitFlash_Core`·`_Rays`·`_Ring` | `BeaverLobby/VFX/InterruptHitFlash` | 재질마다 `_OverrideTex` |
| 섬광 오버레이(1~3단계 + 4 걷힘, 오너 화면) | `HitFlash/M_InterruptHitFlashOverlay.mat` | `BeaverLobby/UI/InterruptHitFlashOverlay` | `_BurstTex`(1) · `_GlareTex`(2) · `_InvertTex`(3·4) + 색 `_FlashColor`·`_BackColor`·`_LineColor` · `_RayCount` · `_LineDensity` |

- **덮어쓸 텍스처 사용법**: 재질 인스펙터에서 칸에 텍스처를 넣으면 셰이더 GUI 가 키워드를 켜 그 텍스처로 그린다(헬프 박스에 '덮어쓸 텍스처 사용' 표시). 비우면 절차 생성으로 돌아간다.
  파티클 셰이더는 텍스처 RGBA × `_Color` × 파티클 색, 오버레이는 화면 전체 UV 로 RGBA 그대로다. 상세: [handoff-interrupt-132-vfx.md](handoff-interrupt-132-vfx.md).
- **빛 모임 소켓** `InterruptLightGatherSocket`(Player.prefab base 루트 — 소켓 비움 = 캐릭터 중심, `vfxPrefab` = `FX_InterruptLightGather`) — Variant 가 소켓만 연결:
  팔라딘 `SM_Wep_Shield_01` · 어쌔신 `Dagger_Weapon_R` · 거너 `LaserGun_Mount/Muzzle`. 위치만 따라가고 회전은 월드 기준. VFX 는 첫 재생 때 한 번 만들어 재사용.

### 0.8 알려진 제약 (132)

- **오너 예측은 근사.** 클라는 창 복제값(`IInterruptWindowReplica`)과 도착 시각 기록만 본다 — 23호 취약·제압 차단, 스파링 봇 준비 남은 시간 등은 서버만 안다. 그래서 오너 강 ↔ 서버 약이 갈릴 수 있고, 그때 오너 화면은 0.2초 걷기로 정리된다.
  같은 틱 '다시 열기'(닫힘 → 열림)는 복제로 '열림 유지'로만 보인다.
- **오너 강 ↔ 서버 대상 없음이면 스킬 끝까지 남는다.** 서버가 대상 없음으로 보면 시도 슬로우가 없어(발동 번호 0) 예측 빗나감·실패 이벤트가 오지 않는다 —
  오너 암전은 간파 스킬 `OnEnd`(`LocalOwnerEnded`)에서 0.2초 걷기로 정리된다(빛 모임은 Hit 에서 먼저 멈춘다).
- **창 길이를 모르면 강.** 첫 창(과거 관측 없음)은 '열려 있음'만으로 성공 예측한다 — 첫 시도는 강이 잘 나온다.
  창 추정 길이 = 그 대상의 지난 '열림 → 닫힘' 길이 중앙값이라, 무기한 창(DebugInterruptLab 허수아비)은 '지난번 기다린 시간'이 길이가 된다 — 더 오래 기다리면 약([dash-interrupt.md §10.1](dash-interrupt.md)).
- **대쉬 예측은 직선.** 예상 대시 끝 = 방향 × 속도 × 남은 시간. 유닛·벽에 막히면 실제 판정 위치보다 멀다.
- **태깅은 그 순간 한 번.** 암전이 켜진 뒤 오너·대상 아래 새로 생긴 렌더러(새 이펙트)는 비트 4 가 없어 덮인다.
- **렌더링 레이어 Everything 인 렌더러는 늘 남는다.** SVN `Assets/50.Art` VFX 26파일·75곳을 Everything → 1 로 바꿨다(SVN 커밋 대기). 커밋 전 다른 워크트리·팀원 화면에선 보스 이펙트 등이 암전 위로 비친다.
- **잔상 비트는 SVN 커밋 뒤부터.** `DashAfterimage.cs` 한 줄이 SVN 에 들어가기 전에는 잔상이 암전에 덮인다. 🔴 SVN 을 git 보다 먼저 커밋한다(메인이 병합 직전).
- **팀원 빛 모임은 발동자 Hit 릴레이로 끈다**(새 RPC 없음) — 관찰자 애니메이터가 Hit 을 못 받으면 실패·성공·리셋 때 꺼진다.
- **HUD 도 덮는다.** 오너 암전 동안 내 HP·쿨 HUD 가 안 보인다(계획서 확정). 오버레이 섬광 중도 같다.
- 강도·색·단계 길이·빛 모임 시간 기준·블룸 세기는 Play 튜닝 전 처음 값이다.

## 1. 흐름 (132 이전 공통 경로 — 성공·실패·슬로우 배포)

```
간파 입력(오너) ─▶ 서버 PlayerInterruptSkillBase  — 사건만 디렉터에 알린다(할 일 118)
  ① 시도  Director.ServerAttempt(예측 3상태) → 대상 없음 = 슬로우 없음 / 약 = ServerStart(interruptWeakProfile, AttemptWeak) / 강 = ServerStart(interruptProfile, Attempt)
  ② 판정 — 대상 수신 전후 성공 수 비교 / CombatStatsEvents.ServerCounterSucceeded 수신(스킬 소관, 불변)
       카운터 창을 소비한 첫 대상의 transform.position 을 기록만 한다(C5 — 판정 불변)
     Director.ServerResolve → 빗나가면 ServerFail(시도 번호)
  ③ 성공(카운터 창 소비)이면 Director.ServerSuccess
       → ServerStart(interruptSuccessProfile, 시전자, Success, 대상 위치 — 없으면 시전자 위치, hitStopOn = A1 칸)
         last-wins: 시도 슬로우의 지금 배율을 덮고 맨 앞 히트스톱부터 다시 시작 · 시도 슬로우가 없었어도 건다
       → 시전자 아래 IInterruptSuccessRelay 마다 ServerRelaySuccess → 캐릭터 기존 RPC(팔라딘 충격파·어쌔신 성공 — B3·C2 칸이 꺼져 있어 재생 안 함)
  ④ 실패  판정 전 종료·앵커 누락 → Director.ServerFail → 시도 슬로우 실패 복귀
  ⑤ named message 로 전 피어 배포(SlowMotionEvent — 목적 칸 Purpose·대상 위치 TargetPosition 포함)
        │
        ▼ 각 피어 GlobalTimeScale.Started(id, 발동자 clientId, purpose, 대상 위치) — 유일한 구독자 = 디렉터
  ├─ 시간 배율: 히트스톱 → 진입 → 유지 → 복귀(전 피어 같은 타임라인)
  ├─ 오너 연출 상태 기계(내 슬로우만) · 팀원 빛 모임(남의 강 시도) — §0.2
  └─ 디렉터 성공 단계(purpose==Success 만)
       ├─ Started 즉시   공용 채널 A4 — 발동자 소유의 켜진 InterruptSuccessFx → casterFx(시전자 소켓) + targetFx(대상 위치) 원샷
       │                 적중 섬광 진행기 시작(오너 오버레이 조건 = 내 성공 ∧ 오너 암전 켜짐)
       └─ 섬광 순간      월드 섬광(모든 화면) + 암순응 A2 — 다른 화면 = 공용 others / 본인 = 오버레이 없었을 때만 개인 self
```

- **공용 성공 신호 = 슬로우 이벤트의 목적 칸**(`SlowMotionPurpose.Success`). 그 이벤트를 듣는 건 디렉터 하나뿐이다 —
  성공 연출을 더할 땐 `GlobalTimeScale.Started` 를 직접 구독하지 말고 디렉터 성공 단계에 cue 를 더한다([§6.6](#66-새-연출-붙이기)).
- **캐릭터·대상 RPC 연출**(팔라딘 충격파 B3·어쌔신 성공 C2·맞은 쪽 섬광 E1)은 전송로를 그대로 두고, 받는 쪽이 `Director.PlayCue(cue, 사건, this)` 로 묻는다(지금 칸 꺼짐).
- **대상 위치** = 카운터 창을 소비한 첫 대상(수신 전후 성공 수가 처음 바뀐 Unit, 또는 `CombatStatsEvents` 로만 온 성공의 target).
  서버 판정 시점 위치를 고정 월드 좌표로 싣는다 — 대상을 따라가지 않는다. 시도 슬로우·Fail·Reset 은 0. 섬광 중심 = 대상 위치 + `hitFlashHeightOffset`.
- 연출 훅일 뿐 판정·피해·카운터 창에는 관여하지 않는다.
- 컷신·씬 전환·세션 종료로 `ForcedReset` 이 오면 암순응·오너 암전·빛 모임·섬광 오버레이는 즉시 0, 남은 섬광 순간 취소, 월드 섬광 지움. 원샷 성공 이펙트(A4)는 끊지 않는다.

## 2. 수치 (기본값 = `NetworkManager.prefab`)

### 2.1 슬로우 프로필 — `InterruptProductionDirector` (시트 `SlowMotion`, Order 0)

> 할 일 118 C2 에서 `GlobalTimeScale` 에서 디렉터로 옮겼다. 필드 이름(`interruptProfile`·`interruptSuccessProfile`)과 대상(`NetworkManager`)이 같아 xlsx 행은 그대로 묶인다.
> 할 일 132: `interruptProfile` = **강** 시도(옛 시도 자리 그대로), 새 `interruptWeakProfile` = **약** 시도.
> **서버(호스트) 인스턴스 값만 쓰인다** — 서버가 사건 순간 값을 이벤트에 실어 보낸다.

| 필드 | 강 시도 `interruptProfile` | 약 시도 `interruptWeakProfile` | 성공 `interruptSuccessProfile` | 의미 |
|---|---|---|---|---|
| `scale` | 0.15 | 0.5 | 0.25 | 유지 단계 시간 배율 [0.01, 1] |
| `hitStopDuration` | 0 | 0 | 0.05 | 맨 앞 히트스톱 길이(실시간 초). 0 = 없음 |
| `hitStopScale` | 0.02 | 0.02 | 0.02 | 히트스톱 배율 [0.01, 1]. 진입은 이 배율에서 `scale` 로 간다 |
| `enterDuration` | 0.05 | 0.05 | 0.05 | 진입(linear) 실시간 초 |
| `holdDuration` | 0.5 | 0.2 | 1.2 | 유지 실시간 초 |
| `exitDuration` | 0.25 | 0.25 | 0.4 | 복귀(ease-out) 실시간 초 |
| `failExitDuration` | 0.1 | 0.1 | 0.1 | 실패 복귀 실시간 초(성공 슬로우는 Fail 을 받지 않는다) |

곡선은 고정(닫힌 식) — 피어마다 같은 `LostTimeUntil` 을 내야 해서 AnimationCurve 를 쓰지 않는다(`SlowMotionProfile` 주석).
xlsx: 강·성공은 기존 열, **약 `interruptWeakProfile.*` 는 반영 예정**(§0.5).

### 2.2 오너 줌 — `CameraTargetSwitcher`·디렉터 (시트 `SlowMotion`)

| 필드 | 위치 | 기본 | 의미 |
|---|---|---|---|
| `slowMotionZoomFactor` | `CameraTargetSwitcher`(Order 1) | 0.85 | 내가 일으킨 **성공** 슬로우 유지 단계 카메라 거리 배율 [0.1, 1] |
| `interruptZoomFactor` | 디렉터(Order 0) | 0.7 | 내가 일으킨 **강** 시도 슬로우 배율 — 🟨 xlsx 반영 예정 |
| `interruptWeakZoomFactor` | 디렉터(Order 0) | 0.93 | 내가 일으킨 **약** 시도 슬로우 배율 — 🟨 xlsx 반영 예정 |

- 슬로우 목적(`Attempt`/`AttemptWeak`/`Success`)으로 배율을 고른다(`InterruptProductionRules.SelectOwnerZoomFactor`). 줌 깊이는 지금 재생 중인 프로필 배율 기준. A3 꺼짐 = 배율 1.

### 2.3 암순응 — `InterruptDarkAdaptation` (시트 `SlowMotion`, Order 2)

곡선은 **실시간(unscaled)** — 슬로우 배율과 무관하게 같은 길이로 보인다.

| 필드 | 기본 | 의미 |
|---|---|---|
| `selfIntensity` | 1 | 발동자 본인 화면 최대 강도 [0, 1] |
| `othersIntensity` | 0.35 | 다른 사람 화면 최대 강도 [0, 1] |
| `fadeInDuration` | 0.08 | 최대 강도까지 실시간 초 |
| `holdDuration` | 0.6 | 최대 강도 유지 실시간 초 |
| `fadeOutDuration` | 0.5 | 0 으로 돌아오는 실시간 초 |
| `luminanceThreshold` | 0.8 | 이 HDR 휘도보다 어두운 픽셀만 낮춘다(빛 이펙트·발광은 남는다) |
| `thresholdSoftness` | 0.3 | 임계 아래로 부드럽게 섞는 휘도 폭 |
| `maxDarken` | 0.85 | 강도 1 에서 어두운 픽셀을 낮추는 최대 비율 [0, 1] |
| `revealCenter` | Target | 원 중심 기준(열거 — **데이터 테이블 열 아님**, 인스펙터·`NetworkManager.prefab` 에서 고른다). Target = 대상 위치 · Caster = 발동자 추적 · Midpoint = 둘의 중간 |
| `revealRadius` | 0.18 | 중심 주변을 살리는 원 반경(화면 높이 비율). 0 = 원형 끔 |
| `revealSoftness` | 0.1 | 원 가장자리 부드러움(화면 높이 비율) |

**언제.** 할 일 132 단계 5 부터 성공 `Started` 즉시가 아니라 **적중 섬광 순간**(1~3단계 뒤, 기본 0.3초)에 튼다.

**본인/타인.** `Started` 의 발동자 clientId 가 이 피어의 `LocalClientId` 면 `selfIntensity`(디렉터 개인 채널), 아니면 `othersIntensity`(공용 채널).
단 본인 화면에서 섬광 오버레이가 돌았으면(강 시도 성공) self 암순응은 생략한다 — 마스크 암전·섬광이 대신한다(`InterruptHitFlashRules.SelectDarkAdaptation`).
`InterruptDarkAdaptation` 은 구독하지 않는 수동 실행자다 — 디렉터가 `Play(viewerIsCaster, …)`·`Stop()` 을 부른다.

**원형 중심**(`InterruptDarkAdaptationModel.TrySelectRevealCenter`) — 매 프레임 메인 카메라 뷰포트로 바꾼다(카메라 뒤면 원형 끔).

| `revealCenter` | 중심 | 시전자를 못 찾으면 |
|---|---|---|
| `Target`(기본) | 신호의 대상 위치(고정 월드 좌표) | 상관없음 |
| `Caster` | 발동자 플레이어 오브젝트(매 프레임 추적, C3 동작) | 원형 끔 |
| `Midpoint` | 대상과 시전자의 중간 | 대상 |

> 데이터 테이블은 enum 을 템플릿에 넣지 않는다([data-table.md §8](data-table.md)) — `revealCenter` 는 xlsx 열이 없다.

### 2.4 성공 이펙트 칸 — `InterruptSuccessFx` (Player.prefab base 루트)

| 필드 | base 기본 | 의미 |
|---|---|---|
| `casterFx` | 비어 있음 | 간파 성공 시 **시전자 소켓 위치**에서 원샷(`EffectSocketPlayer.PlayOnce`). C4 의 `successEffect` 를 이름만 바꾼 칸(`FormerlySerializedAs`) |
| `targetFx` | 비어 있음 | 간파 성공 시 **신호의 대상 위치**에서 원샷 — 충격파는 여기. 소켓을 그 위치로 잠깐 옮겨 `PlayOnce` 하고 같은 호출 안에서 되돌린다(회전은 소켓 회전, `offset` 은 더해진다) |

- 칸마다 따로 — 비어 있는 칸은 아무것도 안 한다. 둘 다 비면 아무것도 안 함(경고 없음).
- 전 피어가 각자 재생한다 — 디렉터 공용 채널이 A4 칸을 보고 발동자 소유 스폰 오브젝트의 켜진 `InterruptSuccessFx`(`IInterruptCueSink`)를 부른다.
  판단 규칙은 `InterruptProductionRules.ShouldPlaySuccessFx(purpose, 발동자, 스폰 여부, OwnerClientId)` → `SelectSuccessFxSlots(재생 여부, casterFx 있음, targetFx 있음)`.
- 구독·`Update` 재구독은 없다(118 C4 에서 제거 — 디렉터가 유일한 구독자).
- 데이터 테이블 대상이 아니다(오브젝트 참조만 있다).

## 3. 렌더러 기능 — 순서와 주입 시점

`DarkAdaptationFeature`(`Assets/1.Scripts/Rendering/DarkAdaptation/`)는 `PC_Renderer`·`Mobile_Renderer` 기능 목록 **맨 끝**에 있다.

- 주입 시점 = `RenderPassEvent.BeforeRenderingPostProcessing`(550). Fog(450) 뒤, 같은 550 의 PlayerSilhouette·EnemyOutline·MaskBlur 뒤 —
  같은 이벤트끼리는 기능 목록 순서라 맨 끝에 둔다. 후처리(Bloom·톤매핑) 전 HDR 색에서 돌아 남긴 밝은 픽셀에서 Bloom 이 그대로 나온다.
- 한 기능 두 모드(할 일 132): 휘도 암순응(`DarkAdaptationState.Intensity`, 패스 0) / 오너 마스크 암전(`DarkAdaptationState.MaskIntensity` + 덮개 RT, 패스 1 `MaskCover`) — §0.7.
- 모드별 강도가 0 이면 그 패스를 넣지 않는다(`DarkAdaptationModes.Select`) — 둘 다 0 이면 `EnqueuePass` 없음, 평소 비용 0.
- 셰이더는 기능의 `_shader` 칸에 직접 연결한다(`Shader.Find` 는 빌드에서 빠진다). 두 패스가 같은 셰이더다.
- 렌더러 밖 UI: 오너 암전 덮개 캔버스(sortingOrder short.MaxValue − 2) · 섬광 오버레이 캔버스(short.MaxValue − 1) — 런타임에 만든다.
- `PC_Renderer` 목록 순서(현재): SSAO(꺼짐) · Decal · Fog · MaskBlur(꺼짐) · RetroCRT · PlayerSilhouette · Flat Kit Outline · EnemyOutline · **DarkAdaptation**.

## 4. 성공 이펙트 넣는 법 (이펙트 담당 — 민경)

1. 캐릭터 **Variant**(`Player_Paladin.prefab`·`Player_Gunner.prefab` 등)를 연다. **base `Player.prefab` 은 고치지 않는다**
   ([player-prefabs.md](player-prefabs.md) — 캐릭터 고유 = Variant).
2. Variant 안에 소켓 오브젝트를 만들고 `EffectSocketPlayer` 를 붙인다 — `effect` 에 EffectEntry, 필요하면 `socket`·`offset`·`scale`.
   `id` 는 애니메이션 이벤트로 부르지 않으니 비워도 된다.
3. Variant 루트의 `InterruptSuccessFx` 칸에 그 소켓을 끌어 넣는다(base 값에 대한 Variant 오버라이드로 저장된다).
   - **충격파처럼 맞은 대상에서 터져야 하는 것은 `targetFx`**, 시전자 몸에서 나는 것은 `casterFx`.
   - `targetFx` 소켓은 루트가 아닌 **빈 자식 오브젝트**에 둔다 — 재생 순간 그 트랜스폼을 대상 위치로 잠깐 옮겼다 되돌린다.
4. Play — 간파 성공 시 모든 화면에서 한 번 재생된다. 원샷이라 소켓·대상을 따라가지 않고 재생 순간 위치에 찍힌다.

캐릭터별 기존 성공 연출(가붕이 `PlayerShieldVfx` 충격파 B3, 어쌔신 `AssassinSkillView` 성공 소켓 C2)은 코드가 그대로지만 132 단계 6 부터 **스위치가 꺼져** 나오지 않는다(새 적중 섬광이 대신).
A4 칸은 켜져 있다 — 성공 순간(Started 즉시) 원샷이라 섬광(0~0.4초)과 겹쳐 보이면 칸을 비운다.
빛 모임·적중 섬광 텍스처 바꾸기는 §0.7·[handoff-interrupt-132-vfx.md](handoff-interrupt-132-vfx.md).

## 5. 알려진 제약

- **`Mobile_Renderer` 재직렬화.** C3 에서 기능을 추가하며 URP 가 `Mobile_Renderer.asset` 을 업그레이드(`m_AssetVersion` 2→3, 줄 바꿈 정리)했다.
  다음 사람이 열어도 같은 diff 가 다시 나지 않지만, 그 커밋 diff 에 노이즈가 섞여 있다.
- **성공이 `CombatStatsEvents` 로만 온 경우.** 대상이 `IInterruptSlowMotionTarget` 의 성공 수를 올리지 않고
  `CombatStatsEvents.ServerCounterSucceeded` 로만 성공을 알리면(`FindSlowMotionTarget` 이 못 찾는 대상 등) `interruptSucceeded` 는 서지만
  `interruptLanded` 는 서지 않는다. 그래서 시도 슬로우가 먼저 Fail 로 정리되고, 같은 판정 안에서 성공 Start 가 바로 덮는다(last-wins).
  화면상 차이는 없지만 `Failed` 이벤트가 성공 직전에 한 번 난다 — 디렉터 실패 단계(`InterruptFailStage.Local`, 지금 연출 없음)에 연출을 붙일 땐 같은 판정의 성공과 묶어 걸러야 한다.
- **임계값은 톤매핑 전 HDR 기준.** `luminanceThreshold` 는 화면에 보이는 최종 밝기가 아니라 후처리 전 HDR 휘도다.
  노출·톤매핑 설정을 바꾸면 같은 값이 다르게 보인다.
- **대상 위치는 판정 순간 값.** 클라 화면의 대상은 보간 지연만큼 뒤에 있을 수 있어 `targetFx`·Target 원형이 살짝 어긋나 보일 수 있다.
- 사람이 MPPM 으로 확인할 것: 본인/타인 강도 차이, 원형 중심이 `revealCenter` 대로(기본 = 맞은 대상) 잡히는지,
  `casterFx`·`targetFx` 를 넣은 뒤 전 화면에서 각각 시전자·대상 위치에 재생되는지. 132 체계 제약은 [§0.8](#08-알려진-제약-132).

## 6. 간파 연출 디렉터 — `InterruptProductionDirector` (할 일 118)

> 설계안: `_lane-artifacts/MainProject-Worktree2/118/118-설계안.md`(레포 밖). 104 스위치(`InterruptPresentationToggles`)는 이 디렉터로 흡수·삭제됐다.
> 다른 담당 영역 변경 공유: [handoff-interrupt-director-vfx.md](handoff-interrupt-director-vfx.md)(민경) · [handoff-interrupt-director-target-flash.md](handoff-interrupt-director-target-flash.md)(경석).

### 6.1 구조

- **디렉터 = `NetworkManager.prefab` 루트의 MonoBehaviour 1개**(`GlobalTimeScale`·`InterruptDarkAdaptation` 옆). 코드 `Assets/1.Scripts/Player/InterruptProduction/`.
  스위치 10칸 + 강·약 시도/성공 슬로우 프로필 3개 + 132 오너 암전·적중 섬광 칸(§0.5)을 들고, 재생 지점은 **정적 API** 로 묻는다(인스턴스가 없으면 전부 꺼짐·no-op).
- 간파 스킬(`PlayerInterruptSkillBase`)은 **사건 4개(시도·판정·성공·실패)만 알린다** — 슬로우 시작·실패·성공 재시작 코드는 스킬에 없다.
  판정·피해·카운터 창·그로기·통계는 그대로 스킬·대상(서버) 소관이고, 디렉터는 재생 여부와 슬로우 호출만 한다.
- 단계마다 클래스가 따로 — `InterruptAttemptStage`·`InterruptResolveStage`·`InterruptSuccessStage`·`InterruptFailStage`.
  각 단계는 **개인 채널**(`InterruptPersonalChannel`, 간파한 본인 화면만)과 **공용 채널**(`InterruptSharedChannel`, 모든 화면)로 연출을 나눠 튼다.
- 판단 규칙(칸 on/off·히트스톱 길이·클립 id→cue·시도 cue·보는 사람·성공 이펙트 칸)은 상태 없는 `InterruptProductionRules` 한 곳.

| 타입 | 역할 |
|---|---|
| `InterruptProductionDirector` | 단일 진입점, 직렬화 설정, `GlobalTimeScale.Started/Failed/ForcedReset` 의 **유일한 구독자** |
| `InterruptProductionEvent` · `InterruptProductionPhase` · `InterruptFailReason` | 사건 한 건 데이터(서버 사건·피어 사건이 같은 모양) |
| `InterruptProductionCue` | 연출 하나의 이름 = 스위치 칸 하나(A1~E1, 옛 `InterruptPresentationKind`) |
| `IInterruptCueSink` | 디렉터가 "틀어라" 고 부르는 실제 재생 창구 — 에셋은 구현 쪽에 그대로 |
| `IInterruptSuccessRelay` | [서버] 성공을 캐릭터 기존 RPC 로 퍼뜨리는 창구(전송로 불변 어댑터) |
| `ITimeScalePort`(`GlobalTimeScalePort`) | 단계 → `GlobalTimeScale.ServerStart/ServerFail` 이음새(EditMode 가짜 검증용) |
| `IInterruptProductionSettings` | 단계가 설정을 읽는 창구(= 디렉터). 설계안에 없던 **보조 인터페이스** — 단계를 디렉터 없이 테스트하려고 |
| `IInterruptDarkAdaptationOutput` | 암순응 수동 실행자 창구(= `InterruptDarkAdaptation`). 설계안에 없던 **보조 인터페이스** |

### 6.2 사건 흐름

| 사건 | [서버] 누가 알리나 → 디렉터가 하는 일 | [각 피어] 디렉터가 하는 일 |
|---|---|---|
| 시도 | `TryStartSkillServer` → `ServerAttempt(예측 3상태)` → 대상 없음 = 없음 / 약 = `interruptWeakProfile`(`AttemptWeak`) / 강 = `interruptProfile`(`Attempt`), 발동 번호 반환 | [오너] 입력 순간 `LocalOwnerAttempt`(오너 암전·빛 모임, §0.2) · `OnClientPlay` → `LocalAttempt` → B1(꺼짐) · 클립 이벤트 B2·C1 은 `AllowsAnimEffect(id)` · 남의 강 시도 = 팀원 빛 모임 |
| 판정 | `ResolveHit` 끝 → `ServerResolve` → 빗나가면 시도 슬로우 `ServerFail` | [오너] 애니 Hit → `LocalOwnerHit`(빛 모임 끝) · 팀원 `PeerCasterHit` |
| 성공 | 판정 바로 뒤 `ServerSuccess` → 성공 슬로우(`interruptSuccessProfile`, A1 칸 = `hitStopOn`) → 시전자 아래 `IInterruptSuccessRelay` 마다 1회 | `Started(Success)` → A4 즉시 · 적중 섬광 진행기 → 섬광 순간 월드 섬광·A2 · RPC 수신부(B3·C2·E1, 꺼짐)가 `PlayCue` |
| 실패 | 판정 전 종료(`OnEnd`)·앵커 누락 → `ServerFail` → 시도 슬로우 실패 복귀 | `Failed` → 오너 암전 걷기·빛 모임 정지(실패 단계 자체 연출 없음) · [오너] `OnEnd`·거부 → `LocalOwnerEnded` |

- 대쉬 간파(할 일 115)도 같은 사건 4개를 쓴다. 예측 판정 위치는 예상 대시 끝, 예측 시점은 우클릭 누른 순간 — [dash-interrupt.md §5](dash-interrupt.md#5-118-디렉터-사건-연결--새-경로-없음).
- 호출 순서는 옛 코드와 한 줄씩 같다: 빗나감 Fail → 성공 Start → `ReportLag`. 캐릭터 relay 는 성공 Start 바로 뒤라 옛 정적 이벤트 구독보다 `ReportLag` 한 줄 앞이다(같은 프레임·같은 RPC 순서, 화면 차이 없음).
- 정적 `PlayerInterruptSkillBase.ServerInterruptSucceeded/Resolved` 는 **로직용으로 남아 있다**(튜토리얼·통계). 연출 구독자(팔라딘·어쌔신)만 relay 로 옮겼다.
- 슬로우가 거부돼도(컷신 잠금·씬 전환) relay·섬광 RPC 는 그대로 나간다 — 전송로를 바꾸지 않았다(설계 Q1-a). 그래서 그때 A2·A4 만 빠지고 B3·C2·E1 은 나온다(지금과 같음).

### 6.3 개인 / 공용 (보는 사람 기준)

| 연출 | 칸 | 채널 | 판단 |
|---|---|---|---|
| 시도(강·약)·성공 슬로우 · 히트스톱 | — · A1 | 서버 타임라인(전 피어 공용) | 채널을 거치지 않는다. `Time.timeScale` 은 피어 전역이라 개인 슬로우는 불가 |
| 오너 줌 | A3 | 개인 | 카메라가 매 프레임 `Director.OwnerZoomFactor(목적, 성공 배율)` 질의. 내 슬로우인지는 카메라가 본다 |
| 오너 암전 · 섬광 오버레이 | (칸 없음) | 개인(오너 상태 기계) | §0.3 |
| 빛 모임 · 월드 섬광 | (칸 없음) | 오너 = 입력 순간 예측 / 팀원·모두 = 슬로우 이벤트 | §0.3 |
| 암순응 self 강도 | A2 | 개인 | 발동자 == `LocalClientId` ∧ 섬광 오버레이 안 돎(약 시도 성공), 섬광 순간 |
| 암순응 others 강도 | A2 | 공용 | 그 외 화면, 섬광 순간. 칸은 하나(설계 Q3-a) |
| 성공 이펙트 칸 casterFx/targetFx | A4 | 공용 | 발동자 소유 켜진 `InterruptSuccessFx` |
| 방패 발광·루프 · 기 모으기 · 어쌔신 기 모으기/튀기기/올려치기 | B1 · B2 · C1 | 공용 | 전 피어 `OnClientPlay`·클립 이벤트 |
| 충격파 · 어쌔신 성공 · 맞은 쪽 섬광 | B3 · C2 · E1 | 공용 | 서버 → 기존 RPC → 수신부 `PlayCue` |

### 6.4 스위치·프로필 위치

`NetworkManager.prefab › InterruptProductionDirector`. 인스펙터는 `[Header]` 로 "시도 — 공용 / 성공 — 공용 / 성공 — 개인 / 시도·결론 — 개인(오너 암전·빛 모임) / 성공 — 적중 섬광" 으로 묶여 있다.
**스위치 기본값(할 일 132 단계 6)**: 새 체계에 들어오는 공용 칸 **A1·A2·A3·A4 = 켬**, 캐릭터별·맞은 쪽 연출 **B1·B2·B3·C1·C2·E1 = 꺼짐**(새 적중 섬광이 대신).
새 컴포넌트(코드 기본값)는 여전히 전부 꺼짐이다 — 켬은 프리팹 값이다.

| 칸 | 필드 | 프리팹 | 무엇이 언제 | 재생 지점 → 디렉터 |
|---|---|---|---|---|
| — | `interruptProfile` · `interruptWeakProfile` | — | 강·약 시도 슬로우 모양 | 시도 단계 `ServerStart` (xlsx `SlowMotion`, §2.1) |
| — | `interruptSuccessProfile` | — | 성공 슬로우 모양 | 성공 단계 `ServerStart` (xlsx `SlowMotion`, §2.1) |
| A1 | `hitStop` | **켬** | 성공 슬로우 맨 앞 정지(섬광 1단계가 같은 순간 시작) | 성공 단계가 `GlobalTimeScale.ServerStart(…, hitStopOn)` 로 넘긴다 — 꺼지면 이벤트의 `hitStopDuration` 0 |
| A2 | `darkAdaptation` | **켬** | 성공 섬광 순간 화면 휘도 암순응(팀원 others · 오버레이 없던 본인 self) | 섬광 순간 개인/공용 채널 → `InterruptDarkAdaptation.Play` |
| A3 | `ownerZoom` | **켬** | 내가 일으킨 슬로우(강·약·성공) 동안 카메라 줌 | `CameraTargetSwitcher` → `Director.OwnerZoomFactor` — 꺼지면 배율 1 |
| A4 | `successFxSlot` | **켬** | 공용 성공 이펙트 칸(§2.4, base·Variant 칸은 지금 비어 있음) | 성공 단계 공용 채널 → `InterruptSuccessFx`(sink) |
| B1 | `paladinShieldGlow` | 꺼짐 | 팔라딘 간파 시도 방패 발광·루프 | `FirstMeleeInterruptSkill.OnClientPlay` → `Director.LocalAttempt` → 스킬(sink). `OnEnd` 끄기·회수는 늘 돈다 |
| B2 | `paladinShieldCharge` | 꺼짐 | 팔라딘 방패 기 모으기(클립 `Garen_Parry` 의 `ShieldCharge`) | `EffectAnimEvents.PlayEffect`·`StartEffect` → `Director.AllowsAnimEffect(id)` |
| B3 | `paladinWave` | 꺼짐 | 팔라딘 성공 충격파 | `PlayerShieldVfx.PlayInterruptWave`(RPC·오프라인) → `Director.PlayCue(PaladinWave, …, this)` |
| C1 | `assassinInsightAttempt` | 꺼짐 | 어쌔신 기 모으기·튀기기·올려치기(클립 `Attack_Up_01`) | `EffectAnimEvents.PlayEffect`·`StartEffect` → `Director.AllowsAnimEffect(id)` |
| C2 | `assassinInsightSuccess` | 꺼짐 | 어쌔신 간파 성공 이펙트 | `AssassinSkillView.TryPlayInsightSuccess`(대기·올려치기 프레임 맞추기는 View) → `Director.PlayCue(AssassinInsightSuccess, …, this)` |
| E1 | `targetFlash` | 꺼짐 | 맞은 쪽 섬광 `Interrupt_Flash` | 23호·건틀릿봇·스피너봇·월봇·허수아비·스파링 봇 `PlayInterruptFlashRpc` → `Director.PlayCue(TargetFlash, …, this)` |

- **B1·B2·C1 과 빛 모임의 겹침**: 셋 다 '시도 순간 무기에서 기를 모으는' 연출이라 강 시도의 빛 모임(같은 소켓 — 방패·단검)과 뜻이 겹친다. 132 이전부터 꺼져 있던 값을 그대로 두었다 — 켜면 강 시도에서 둘이 같은 자리에 겹쳐 나온다.

- **클립 이벤트 이펙트(B2·C1)** 는 id 로 가린다 — 간파 id(`ShieldCharge`, `Assassin_InsightWindup`/`Pop`/`Slash`)는 `InterruptProductionRules` 상수 한 곳에만 있다.
  다른 id 는 영향 없음. `StopEffect` 는 막지 않는다. 재생을 막아도 `EffectAnimEvents.Played` 알림은 난다 —
  `AssassinSkillView` 가 올려치기 알림을 C2 기준 프레임으로 쓰므로 **C1 을 꺼도 C2 를 켜면 성공 이펙트가 같은 프레임에 나온다.**
- 섬광(E1)은 연출만 막는다 — 판정·그로기·통계, 보스의 '섬광 칸 비어 있음' 경고(`WarnNoInterruptFlashOnce`, `PlayCue` 앞)는 그대로.
- 대상 아님: 슬로우 자체, 거너 폭발선(`GunnerBeamView.InterruptBlastRpc` — 판정마다 나는 임시 연출, 설계 Q4-a 로 디렉터 밖), `InterruptHighlightHUD`, `CameraFeedback` 공통 흔들림.
- 스위치는 bool 이라 데이터 테이블 열이 아니다(`[DataTableIgnore]`, [data-table.md §8](data-table.md)) — 프리팹이 원본. 프로필은 xlsx 가 원본.

### 6.5 Play 중 바꾸기 · 누구 값이 쓰이나

- **사건이 올 때마다 디렉터 필드를 읽는다(캐싱 없음).** Play 중 Hierarchy `DontDestroyOnLoad › NetworkManager › InterruptProductionDirector` 에서 바꾸면 다음 간파부터 바로 쓰인다.
  프리팹 에셋을 고치면 Play 중 인스턴스엔 안 닿는다(Unity 기본).
- **슬로우 프로필·히트스톱(A1) = 호스트(서버) 인스턴스 값만.** 서버가 사건 순간 값을 이벤트에 실어 보내고 전 피어가 같은 닫힌 식으로 재생한다 — 클라 값은 무시. MPPM 에서는 호스트 에디터에서 바꾼다.
- **나머지 9칸 = 각 피어 자기 인스턴스 값**(동기화 없음, 설계 Q8-a) → MPPM 가상 플레이어 화면은 호스트에서 바꿔도 안 바뀐다.
  줌 배율·오너 암전·섬광 칸(§0.5)도 각 피어 값이다.
- 테이블 모드 Play 는 시작 때 프로필이 xlsx 값으로 덮인 뒤, 그 이후 인스펙터 변경은 Play 끝날 때까지 산다([data-table.md](data-table.md) §6).

### 6.6 새 연출 붙이기

- 성공 이펙트 하나 더 → 먼저 [§4](#4-성공-이펙트-넣는-법-이펙트-담당--민경) 의 성공 이펙트 칸(A4)으로 되는지 본다. 코드가 필요 없다.
- 새 cue 가 필요하면 → `InterruptProductionCue` 값 + 디렉터 bool 칸(`[DataTableIgnore]`) + `Get` 분기 + 어느 단계·채널에서 트는지 정하고, 재생 쪽은 `IInterruptCueSink` 를 구현해 `Director.PlayCue(cue, 사건, this)` 로 묻는다.
  `GlobalTimeScale.Started` 를 직접 구독하지 않는다(유일 구독자 규칙).
- 새 간파 대상(보스·엘리트) 섬광 → 카운터 성공 지점(서버)에서 기존처럼 RPC, 수신부에서 `Director.PlayCue(TargetFlash, InterruptProductionEvent.LocalCue(Success), this)` + `IInterruptCueSink`.
- 끄기·회수(`FadeOut`·루프 `Stop`·`StopEffect`)는 디렉터를 거치지 않는다 — 칸을 꺼도 켜진 루프가 남지 않게.

### 6.7 설계안과 달라진 점(구현 C1~C6)

- `[RequireComponent(typeof(GlobalTimeScale))]` 를 쓰지 않았다 — 없으면 피어 구독만 건너뛴다(`TryGetComponent`).
- 시도 슬로우 `ServerStart` 에도 A1 칸을 넘긴다(시도 프로필 `hitStopDuration` 기본 0 — 동작 동일).
- 빗나감 실패는 판정 단계의 `ServerFail` 한 경로뿐이다(성공·판정과 별개로 실패 사건을 따로 내지 않는다).
- 보조 인터페이스 2개 추가: `IInterruptProductionSettings`·`IInterruptDarkAdaptationOutput`(§6.1).
- A4 대상 찾기 = 스폰 목록에서 발동자 소유 오브젝트의 **켜진** `InterruptSuccessFx`(옛 구독 조건과 같다. `GetPlayerNetworkObject` 는 클라에서 남의 것을 찾으면 에러).
- relay 구현(팔라딘 `FirstMeleeInterruptSkill` · 어쌔신 `AssassinSkillView`)은 **켜진 컴포넌트만** 반응 — 옛 `OnEnable` 구독과 같은 조건. 팔라딘 relay 는 스킬에 있다(설계 Q10-b).
- 피어 시도 사건용 `Director.LocalAttempt` 와 RPC 수신부용 `Director.PlayCue(cue, 사건, 한 sink)` 를 정적 API 로 추가. 시도 cue 고르기 = `InterruptProductionRules.TryGetAttemptCue`(팔라딘만 B1).
- 옛 `InterruptPresentationToggles.cs` 는 삭제(새 컴포넌트 추가 → 옛 컴포넌트 제거 → 스크립트 삭제).

### 6.8 테스트

`Tools/Tests/슬로우 모션 EditMode 테스트 실행` — `InterruptProductionDirectorTests`(옛 토글 테스트 이관) · `InterruptProductionPrefabTests`(프로필 값·`GlobalTimeScale` 에 프로필 없음·Missing Script 0) ·
`InterruptProductionStageTests`(가짜 포트로 서버 사건 순서) · `InterruptProductionChannelTests`(개인/공용·시도 B1·한 sink `PlayCue`·섬광 대상 6종) · `InterruptProductionLiveReadTests`(캐싱 금지 회귀).
`InterruptSuccessFxTests` 는 플레이어 러너. 데이터 테이블 러너로 `SlowMotion` 바인딩 확인.
할 일 132 추가(같은 슬로우 모션 메뉴): `InterruptOwnerPresentationTests`(상태 기계) · `InterruptTargetRegistryTests`(캐시) · `InterruptOwnerDimTests`(마스크 암전·비트 4) ·
`InterruptLightGatherTests` · `InterruptHitFlashTests` · `InterruptProductionDirectorTests` 의 프리팹 스위치 기본값(A1~A4 켬). 예측기·대상 판정은 Dash 어셈블리(`BeaverLobby.Player.Dash.EditModeTests` — `DashInterruptPredictionTests`·`InterruptTargetGeometryTests`).

## 7. 이후 확장

- ~~암순응 셰이더에 흑백 실루엣·반전 모드~~ → 132 마스크 암전 모드·섬광 오버레이 흰 반전으로 구현(§0).
- ~~주변 입자가 한 점으로 모이는 효과 + 피격 대상 충격파~~ → 132 빛 모임·월드 섬광으로 구현(§0.7). 손그림으로 바꿀 땐 덮어쓸 텍스처 칸.
- 115 대쉬 간파: 사건 `Variant`(Normal/Dash) 칸은 만들지 않았다(일반 간파와 같은 프로필·cue, [dash-interrupt.md](dash-interrupt.md)) — 다른 프로필·cue 가 필요해지면 그때 이 칸을 더한다.
  117 성공 예측 연출은 132 의 오너 즉시 예측(§0.2)으로 대신했다.
