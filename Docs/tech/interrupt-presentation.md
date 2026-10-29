# 간파 연출 — 시도 강·약 · 오너 암전 · 빛 모임 · 적중 섬광 · 슬로우·히트스톱·암순응·성공 이펙트 칸·오너 줌

> 🔹 할 일 132(2026-10-28, 브랜치 `feature/InterruptPresentation2`): 시도를 **예측 3상태(대상 없음/약/강)** 로 나누고, 강 시도에 **오너 암전·빛 모임**,
> 성공에 **적중 섬광 ②~⑥ 5단계**(§15 재구성 — 스타버스트·화이트아웃·스미어·색수차 블러·가라앉음) 를 얹었다. 흐름 [§0](#0-132-체계--예측-3상태강약오너-암전빛-모임적중-섬광-할-일-132) 이 이 문서의 맨 앞 사실이다.
> 🔹 §16 퀄리티 수정(사용자 Play 녹화 분석): 섬광이 딱딱한 도형처럼 보이던 것 — 부드러운 코어·제각각 빛살·얇은 고리, 화이트아웃 0.75·실루엣 비침, 스미어 = 공격 방향,
> 무지개 원 제거, 월드 섬광 = 빛 덩어리·실시간 0.3s 상한, 섬광 순간 블룸 최대, **실제 Point Light 2개**(적중 지점·빛 모임 소켓) — [§0.9](#09-16-퀄리티-수정--라이트스미어-방향렌더-판단).
> 🔹 §17 튜닝 도구(2026-10-28): 디렉터에 **효과별 켜기/끄기 칸 9개**(기본 = **오너 암전만 켬**, 팀원 암순응 = A2 칸 → 기본 끔) — 끈 효과는 그리기만 안 하고 상태 기계·시간표·섬광 순간은 그대로.
> ②③④ 길이 = **렌더 프레임 수**(기본 2·2·2, 섬광 순간 = 6 프레임째), ⑤⑥ = 초. DebugInterruptLab 씬 OnGUI **튜닝 패널** — [§0.6](#06-연출-켜기끄기-칸--튜닝-패널-17).
> 🔹 할 일 143(2026-10-29, 브랜치 `feature/InterruptFirstComeOnly`): **선착순 한 명** — 한 창을 서버에서 먼저 성공시킨 1명만 정상 처리, 두 번째 이후는 피해만(반응·연출 없음) — [§0.10](#010-선착순-한-명-할-일-143).
> 🔹 할 일 118: 간파 연출은 **간파 연출 디렉터 `InterruptProductionDirector`** 가 단일 진입점이다 — 사건 흐름·개인/공용 채널·스위치/프로필 위치는 [§6](#6-간파-연출-디렉터--interruptproductiondirector-할-일-118).
> 🔹 스위치 11칸(디렉터): **A1 히트스톱·A3 오너 줌·A4 성공 이펙트 칸·A5 피해 숫자 숨김(154) = 켬**, **A2 암순응 = 꺼짐**(§17 기본값), B·C·E(캐릭터별·맞은 쪽 섬광) = 꺼짐 — [§6.4](#64-스위치프로필-위치).
>
> 할 일 87(C1~C5)의 단일 사실 원본. 슬로우 모션 레이어 자체(시간 도메인·서버 권한·last-wins·컷신 리셋)는
> [PLAN-interrupt-slowmo.md](../history/PLANS/PLAN-interrupt-slowmo.md) 가 원본이고, 여기서는 **간파에 얹은 연출**만 다룬다.
> 수치 원본은 `GameData.xlsx` `SlowMotion` 시트([data-table.md](data-table.md)). 계획서: `_lane-artifacts/MainProject-Worktree4/132/132-간파연출-계획.md`(레포 밖).
> 다른 담당 영역 공유: [handoff-interrupt-132-monster.md](handoff-interrupt-132-monster.md)(경석) · [handoff-interrupt-132-vfx.md](handoff-interrupt-132-vfx.md)(민경) · [handoff-interrupt-first-come.md](handoff-interrupt-first-come.md)(경석, 143).

## 0. 132 체계 — 예측 3상태·강/약·오너 암전·빛 모임·적중 섬광 (할 일 132)

### 0.1 상태와 연출

| 상태 | 판단 | 연출 |
|---|---|---|
| 시도 · **대상 없음** | 사거리·각도 안 간파 대상 없음 | **아무것도 없음**(슬로우·줌·암전·빛 모임 없음 — 132 이전 '예측 빗나감'과 같음) |
| 시도 · **약**(실패 예측) | 대상은 있지만 창이 닫혀 있음 / 판정 시각 전에 창이 닫힐 것 | 약 슬로우(`interruptWeakProfile`)·약 줌만 |
| 시도 · **강**(성공 예측) | 판정 시각까지 창이 버틸 것 | 강 슬로우(`interruptProfile`)·강 줌 + **오너 암전** + **빛 모임** |
| 성공(서버 확정) | 카운터 창 소비 | 히트스톱(A1) 시작 = ② 스타버스트 → ③ 화이트아웃 → ④ 스미어 → **섬광 순간**(오너 암전 즉시 끔·월드 섬광·팀원 암순응) = ⑤ 색수차 블러 → ⑥ 가라앉음 · 성공 슬로우 |
| 실패(서버 확정) | 판정 빗나감·판정 전 종료·거부 | 슬로우 실패 복귀 + 오너 암전 0.2초에 걷음·빛 모임 정지. 섬광 없음 |
| 선점(서버 확정, 할 일 143) | 다른 플레이어가 먼저 성공시킨 창 | 피해만 — 실패와 같은 정리(섬광·성공 연출 없음) · [§0.10](#010-선착순-한-명-할-일-143) |

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
  [모든 화면]   Success → 적중 섬광 진행기(InterruptHitFlashSequencer, ②③④ = 렌더 프레임 수 · ⑤⑥ = 진행기 시계 초 §0.4)
                 프레임 0 ②(오너 화면 효과, 아래 조건) · 2 ③ · 4 ④ · 6 섬광 순간 = ⑤ 시작 = 오너 암전 끔 + 월드 섬광(FX_InterruptHitFlash)·라이트 + 암순응
                 · 섬광 순간 + 0.1s ⑥ · + 0.3s 끝(기본 실시간 — 칸을 스케일로 바꾸면 ⑤⑥ 만 성공 슬로우를 따라 늘어난다)
                 효과 칸(§0.6)이 꺼진 효과는 그리지 않을 뿐 시간표·섬광 순간 사건은 그대로
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
| 오너 암전(마스크) | ✓ 강 예측 때만 | — | `showOwnerDim`(**켬**, §0.6) |
| 빛 모임(월드) + 소켓 Point Light(차지 동안 상승, Hit 에서 끔) | ✓ 입력 순간 강 예측 | ✓ 서버 강 시도 슬로우 받을 때 | `showLightGather`(끔) |
| 섬광 화면 효과 ②~⑥(오버레이·렌즈 플레어·방사형 블러·색수차·블룸 Volume) | ✓ 성공 순간 오너 암전 **상태**가 켜져 있었을 때(= 강 시도 성공 — 암전 칸을 꺼도 같다) | — | `showStarburst`·`showWhiteout`·`showSmear`·`showAberration`·`showSettle`(끔) |
| 월드 섬광(적중 지점 파랑·흰 빛 덩어리 + 블룸) | ✓ 섬광 순간(⑤ 시작) | ✓ 섬광 순간 | `showWorldFlash`(끔) |
| 적중 지점 Point Light | ✓ 섬광 순간 | ✓ 섬광 순간 | `showHitLight`(끔) |
| 휘도 암순응 | 화면 효과가 돌았으면 없음(두 번 어두워짐 방지) · 약 시도 성공이면 self 강도 | others 강도(0.35), 섬광 순간 | A2 `darkAdaptation`(= 팀원 암순응, **끔**) |
| 성공 이펙트 칸 casterFx/targetFx | ✓ | ✓ | A4 |

### 0.4 시간표 (기본값) · 시간 기준

이펙트(빛 모임·월드 섬광 파티클)는 `GlobalTimeScale`(`Time.timeScale`)을 따라 느려진다(**스케일 시간**, 할 일 132 §14). 각 이펙트에 시간 기준 칸(`InterruptEffectTimeBase` — 스케일/실시간)이 있어 실시간으로 되돌릴 수 있다.
섬광 진행기는 ②③④·섬광 순간을 **렌더 프레임 수**로 재고(§17 — 시계와 무관), ⑤⑥ 은 진행기 시계(기본 **실시간**, 칸으로 스케일 전환 가능) 초로 잰다.
오너 암전 램프와 카메라 줌 블렌드는 늘 실시간이다.

| 구간 | 길이 | 시계 | 칸 |
|---|---|---|---|
| 오너 암전 들어옴(강 입력 순간 0 → 1) | 0.1s | 실시간(고정) | 디렉터 `ownerDimInDuration` |
| 오너 암전 걷힘(예측 빗나감·실패·판정 전 종료) | 0.2s | 실시간(고정) | 디렉터 `ownerDimOutDuration` |
| 카메라 줌 블렌드 | — | 실시간(고정) | `SlowMotionOwnerZoom` |
| 빛 모임 파티클 | 파티클 수명 | **스케일**(기본) | `FX_InterruptLightGather` › `InterruptLightGatherVfx.timeBase` |
| 빛 모임 소켓 라이트 상승 | 0.5s 에 최대, Hit 까지 유지 | 빛 모임과 같은 시계(스케일) | `FX_InterruptLightGather` › `lightRiseDuration`·`lightRiseCurve` |
| ② 스타버스트 — 오너 암전 위 베이지 렌즈 플레어(부드러운 코어 + 길이 제각각 빛살, 아나모픽 가로 빛살 최장·끝 흐림) + 얇고 흐린 푸른 고리 · 프레임 0 → 2 | **2 프레임** | **렌더 프레임**(시계 무관) | 디렉터 `hitFlashStarburstFrames` |
| ③ 화이트아웃 — 화면 전체 밝은 회청색(0.75) + 강한 방사형 블러, 오너·대상 블러 실루엣이 비침 · 프레임 2 → 4 | 2 프레임 | 렌더 프레임 | `hitFlashWhiteoutFrames` |
| ④ 스미어 — 검정 위 오너·대상·빔(암전 남김 영역)을 키워 **공격 방향**으로 늘인 흰 덩어리 · 프레임 4 → 6 · 끝 = **섬광 순간** | 2 프레임 | 렌더 프레임 | `hitFlashSmearFrames` |
| 섬광 순간 = ②+③+④ 끝 프레임 = ⑤ 시작 — 오너 암전 끔 · 월드 섬광·적중 지점 라이트 시작 · 팀원 암순응 시작 | **6 프레임째** | 렌더 프레임 | 계산값(따로 칸 없음) |
| ⑤ 색수차 블러 — 실제 화면 + 방사형 블러 + Volume 색수차(가장자리) + 블룸 최대 · 섬광 순간 → +0.1 | 0.1s | **진행기 시계**(기본 실시간) | `hitFlashAberrationDuration` |
| ⑥ 가라앉음 — 블룸 덩어리가 가라앉음, 블러·색수차 → 0 · +0.1 → **+0.3 끝** | 0.2s | 진행기 시계 | `hitFlashSettleDuration` |
| 월드 섬광 파티클 | 파티클 수명(최장 0.45) — **실시간 0.3s 상한** | **스케일**(기본) + 상한 넘으면 배속 | `FX_InterruptHitFlash` › `InterruptHitFlashVfx.timeBase`·`realTimeCap` |
| 적중 지점 라이트 | 0.25s(곡선) | **실시간** | `FX_InterruptHitFlash` › `lightDuration`·`lightCurve` |

②③④ 는 §17 에서 초 칸(0.067·0.033·0.033)을 렌더 프레임 수 칸으로 바꿨다 — 60fps 에서 2 프레임 = 0.033s, 프레임레이트가 달라도 '몇 장 보이나'가 같다. 단계별 화면 효과 세기(`InterruptHitFlashRules.Look`, 0~1 × 칸 최대값):

| 단계 | 렌즈 플레어 | 방사형 블러 | 색수차 Volume | 블룸 Volume | 오버레이 그림 |
|---|---|---|---|---|---|
| ② | 1 → 0.6 | 0 | 0 | 0 | 플레어 층 + 푸른 고리(가산) |
| ③ | 0 | **1** | 0 | 0 | 회청색 화이트아웃(덮음) |
| ④ | 0 | 0 | 0 | 0 | 스미어 한 장(덮음) |
| ⑤ | 0 | 0.8 → 0.6 | 1 | **1**(섬광 순간부터 최대 — 코어가 번짐, §16) | 없음(캔버스 끔) |
| ⑥ | 0 | 0.6 → 0 | 1 → 0 | 1 → 0(끝까지 남다가 가라앉음) | 없음(캔버스 끔 — §16 무지개 고리 제거) |

- **②③④ = 렌더 프레임**: 진행기가 `Time.frameCount` 로 성공 `Started` 프레임부터 센다(히트스톱으로 시간이 거의 멈춰도 프레임은 넘어간다).
  섬광 순간 = 그 프레임 + ②+③+④(기본 6) — 진행기가 한 번 알리고(시간표 하나) 디렉터가 그 프레임에 오너 암전 끔·월드 섬광·라이트·암순응을 튼다
  (오너 상태 기계는 따로 재지 않는다 — `InterruptOwnerPresentation.SuccessFlashMoment`). ⑤ 의 0초 = 섬광 순간 프레임의 진행기 시각.
- **⑤⑥ = 진행기 시계** = 디렉터 `hitFlashTimeBase`(기본 **실시간**). 진행 중에 칸을 바꾸면 다음 성공부터.
- ② 시작 = 성공 `Started` 순간 = 히트스톱 시작 순간(히트스톱 길이 0 이어도 같다). 각 길이 0 = 그 단계 건너뜀(②③④ 모두 0 이면 섬광 순간 = Started 프레임).
- 순수 로직: `InterruptHitFlashTimeline.StageAt(지난 프레임, 섬광 순간 뒤 초)` · `InterruptHitFlashSequencer.Tick(시각, 프레임)` — EditMode 가 프레임·시각을 직접 넘겨 확인한다.
- 빛 모임은 입력 순간 → 오너 애니 Hit 이벤트까지(기존 Hit 이벤트 재사용, SO 타이머 칸 없음).
- 셰이더는 `_Time` 을 쓰지 않는다 — 빛 모임·월드 섬광은 파티클 나이(AgePercent·수명 곡선)라 파티클 시간 기준을 그대로 따르고, 오버레이는 진행기가 넘기는 `_Progress` 로 그린다.
- 팀원 암순응 곡선 자체(`InterruptDarkAdaptation` 0.08/0.6/0.5)는 실시간 그대로다(§3) — 시작 시각만 섬광 순간을 따른다.

기본 = 2+2+2 프레임 뒤 섬광 순간 · 그 뒤 실시간 0.3s 끝 — 히트스톱·성공 슬로우와 무관
(EditMode `InterruptHitFlashTests.기본_실시간_시계면_2_2_2_프레임_뒤_섬광_순간_그_뒤_실시간_0_3초` · `디렉터_기본_시간표_2_2_2_프레임_뒤_섬광_순간_그_뒤_0_3초`).

**칸을 스케일로 바꾸면** ⑤⑥ 만 성공 슬로우(유지 0.25)를 따라 길이 × 약 4 로 늘어난다 — ②③④·섬광 순간 프레임은 그대로
(EditMode `칸을_스케일로_바꾸면_색수차_가라앉음만_성공_슬로우를_따라_늘어나고_섬광_순간_프레임은_그대로`).

### 0.5 새 수치 칸 — 전부 `NetworkManager.prefab › InterruptProductionDirector`

| 칸 | 기본 | 누구 값 | xlsx |
|---|---|---|---|
| `interruptProfile.*`(강) | scale 0.15 · hold 0.5 · enter 0.05 · exit 0.25 · failExit 0.1 · hitStop 0/0.02 | 서버 | 기존 열(옛 시도 자리) — xlsx 값이 132 이전(0.3·0.35)이면 🟨 **메인이 xlsx 갱신 필요** |
| `interruptWeakProfile.*`(약) | scale 0.5 · hold 0.2 · enter 0.05 · exit 0.25 · failExit 0.1 · hitStop 0/0.02 | 서버 | 🟨 **반영 예정**(메인 — 열이 없는 동안 프리팹 값) |
| `interruptZoomFactor`(강 줌) | 0.7 | 각 피어 | 🟨 **반영 예정** |
| `interruptWeakZoomFactor`(약 줌) | 0.93 | 각 피어 | 🟨 **반영 예정** |
| `ownerDimInDuration` · `ownerDimOutDuration` | 0.1 · 0.2 | 각 피어 | `[DataTableIgnore]` — 프리팹이 원본 |
| `ownerDimColor` · `ownerDimOpacity` | 검정 · 1(100%) — 예: 남색 0.9 | 각 피어 | `[DataTableIgnore]`(색은 테이블 열 아님) |
| `show*` 연출 켜기/끄기 9칸(§0.6) | `showOwnerDim` 켬, 나머지 8칸 끔 | 각 피어 | `[DataTableIgnore]` — 프리팹이 원본 |
| `hitFlashTimeBase`(⑤⑥ 시계) | 실시간(Unscaled) | 각 피어 | `[DataTableIgnore]` |
| `hitFlashStarburstFrames` · `WhiteoutFrames` · `SmearFrames` | 2 · 2 · 2(렌더 프레임 수) | 각 피어 | `[DataTableIgnore]` |
| `hitFlashAberrationDuration` · `SettleDuration` | 0.1 · 0.2(진행기 시계 초) | 각 피어 | `[DataTableIgnore]` |
| `hitFlashOverlayMaterial` | `M_InterruptHitFlashOverlay` | 각 피어 | 참조 — 비우면 오버레이 그림 없음(플레어·블러·Volume 은 돈다) |
| `hitFlashLensFlare` | `LF_InterruptHitFlashStarburst` | 각 피어 | 참조 — 비우면 ② 플레어 없음 |
| `hitFlashFlareIntensity` · `hitFlashFlareScale` | 1 · 1 | 각 피어 | `[DataTableIgnore]` |
| `hitFlashBlurStrength` | 0.6(③ 최대 — 픽셀에서 적중 지점까지 거리 중 번지는 비율, §16 강하게 0.35 → 0.6·샘플 12 → 16) | 각 피어 | `[DataTableIgnore]` |
| `hitFlashAberrationProfile` · `hitFlashBloomProfile` | `VP_InterruptHitFlash_Aberration` · `VP_InterruptHitFlash_Bloom` | 각 피어 | 참조 — 비우면 그 Volume 없음 |
| `hitFlashWorldVfx` | `FX_InterruptHitFlash` | 각 피어 | 참조 — 비우면 월드 섬광 없음 |
| `hitFlashWorldScale` · `hitFlashHeightOffset` | 1 · 1m | 각 피어 | `[DataTableIgnore]` |

- 옛 1~4단계 칸(`hitFlashBurst/Glare/Invert/ReturnDuration`·`hitFlashSpeedLines`)은 §15 재구성으로, ②③④ 초 칸(`hitFlashStarburst/Whiteout/SmearDuration`)은 §17 프레임 칸으로 없앴다(프리팹 YAML 에도 없음 — 테스트가 확인).
- `show*` 9칸은 프리팹 YAML 에 명시해 뒀다(기본값 = 오너 암전만).
- 오너 암전·섬광 수치 칸들은 `NetworkManager.prefab` YAML 에 아직 써 있지 않아 **코드 기본값**이 쓰인다(인스펙터에서 바꾸고 저장하면 그때 써진다).
  예외 `hitFlashTimeBase: 0`(실시간)은 명시해 뒀다 — 스케일 기본 시절 메모리에 올라간 프리팹이 리임포트 없이 스케일로 남지 않게. 에셋 참조 5칸도 명시.
- 이펙트 시간 기준(§0.4): `FX_InterruptLightGather` › `InterruptLightGatherVfx.timeBase` · `FX_InterruptHitFlash` › `InterruptHitFlashVfx.timeBase`(둘 다 기본 **스케일** —
  재생할 때마다 모든 파티클 `useUnscaledTime` 에 넣는다, 프리팹 파티클 값도 스케일로 맞춤). 소켓은 §0.7.

### 0.6 연출 켜기/끄기 칸 · 튜닝 패널 (§17)

132 단계 6 에서는 칸 하나로 일부만 끄면 상태 기계 조건(강 예측 → 암전 → 성공 때 암전이 켜져 있었으면 화면 섬광)이 꼬일까 봐 칸을 안 뒀다.
§17 에서 **'그리기만 안 함'** 칸으로 더했다 — 칸은 출력 쪽에서만 읽고, 상태 기계·진행기 시간표·섬광 순간 사건은 칸과 무관하게 그대로 돈다
(예: ②③④ 를 꺼도 6 프레임째 섬광 순간에 오너 암전 상태가 걷힌다, 오너 암전 칸을 꺼도 `SuccessDimmed` 는 서서 ②~⑥ 오버레이 조건이 그대로).

| 효과 칸(`InterruptPresentationEffect`) | 디렉터 필드 | 기본 | 끄면 막히는 곳 |
|---|---|---|---|
| 오너 암전 | `showOwnerDim` | **켬** | `InterruptOwnerOutputGate` 가 덮개에 0 을 넘김(상태 값은 그대로). Play 중 바꾸면 다음 프레임(`Sync`) |
| 빛 모임(VFX + 소켓 라이트) | `showLightGather` | 끔 | 오너 = 문이 시작을 안 넘김, 팀원 = 디렉터가 `InterruptLightGatherPeers.Begin` 을 안 부름. 시작 순간 읽음(돌던 빛 모임은 끝까지) |
| ② 스타버스트 · ③ 화이트아웃 · ④ 스미어 · ⑤ 색수차 블러 · ⑥ 가라앉음(블룸) | `showStarburst`·`showWhiteout`·`showSmear`·`showAberration`·`showSettle` | 끔 | `InterruptHitFlashOverlay.Show` 가 그 단계 동안 세기 0·캔버스 끔(플레어·블러·Volume 가중치 0) |
| 월드 섬광 파티클 | `showWorldFlash` | 끔 | `InterruptHitFlashVfx.Play(…, particles: false)` — 둘 다 끄면 월드 섬광 오브젝트를 만들지도 않음 |
| 적중 지점 라이트 | `showHitLight` | 끔 | `InterruptHitFlashVfx.Play(…, light: false)` — 끈 채 첫 재생이면 라이트를 만들지 않음 |
| 팀원 암순응 | 기존 **A2** `darkAdaptation` | **끔** | 새 칸을 더하지 않고 A2 하나로 정리(본인 self 강도 = 약 예측 성공도 같은 칸) |

- 슬로우·줌·히트스톱은 연출 그림이 아니라 그대로다(A1 `hitStop`·A3 `ownerZoom` 켬, 프로필 그대로). A4·B·C·E 칸도 그대로.
- 런타임 API: `director.IsEffectOn/SetEffect(InterruptPresentationEffect, bool)`, `director.Get/Set(InterruptProductionCue, bool)` — 인스턴스 값만 바꾸고 저장하지 않는다.
- 에셋 칸을 비우는 옛 방법(`hitFlashOverlayMaterial` 비움 등)도 그대로 먹지만, 튜닝은 위 칸으로 한다.

**튜닝 패널** `DebugInterruptTuningPanel`(`Assets/1.Scripts/Dev/InterruptLab/`) — `DebugInterruptLab.unity` 의 `InterruptLab` 오브젝트(`DebugTimeScale` 옆)에만 있다.
- Play 중 화면 오른쪽 위 OnGUI 박스(제목 버튼으로 접기). 줄마다 위 효과 10개(효과 칸 9 + 팀원 암순응 A2) 토글, `전부 켬`·`암전만`(기본값) 버튼.
- 배속 = 같은 오브젝트 `DebugTimeScale` 슬라이더(0.05~2) + 버튼(0.05·0.1·0.25·0.5·1·2). 성공 후 1배속 복귀(기본 3초)는 `DebugTimeScale` 규칙 그대로 — 그 동안 남은 시간을 표시.
- 토글은 이 피어 디렉터 런타임 값만 바꾼다 — 프리팹·씬 저장 없음, **Play 종료 시 원래 값**(MPPM 이면 피어마다 따로).
- 격리: 빌드 목록·다른 씬·프리팹에 없음 — `DebugInterruptLabIsolationTests` 가 디버그 컴포넌트 목록에 넣어 검사, 같은 오브젝트 배치도 확인.

### 0.7 렌더러 · 에셋 위치

**오너 마스크 암전**(`DarkAdaptationFeature` 한 기능 두 모드 — 모드별 강도 0 이면 패스 없음):
- 휘도 암순응(팀원·기존 A2) = 패스 0, 마스크 암전(오너) = 패스 1 `MaskCover`(같은 `_shader` 칸).
- 남길 렌더러 = **렌더링 레이어 비트 4**(`InterruptOwnerMaskLayers` — 비트 0 = 조명, 1 = 데칼, 2·3 = 실루엣, 8 = Ground Layer).
  암전 0 → 양수 순간 오너 캐릭터 하위 전부·이번 시도 간파 대상·`KeepInOwnerDim` 루트(빛 모임)·대쉬 잔상에 비트 4 를 **더하고**(대입 아님), 0 이 되면 원래 없던 비트만 지운다.
- 마스크 패스: 비트 4 렌더러만 원래 머티리얼로 투명 검정 위에 다시 그려(카메라 깊이로 가림) 덮개 알파(1 − 남김)를 외부 RT 에 쓴다.
- **HUD 덮기**: 최상위 Screen Space Overlay 캔버스 RawImage = 덮개 RT × 칸 색 × 불투명도 × 강도 — 월드·HUD 전부 덮고 남길 픽셀만 뚫린다.
  `sortingOrder` = 오너 암전 덮개 **short.MaxValue − 2** / 적중 섬광 오버레이 **short.MaxValue − 1**(그 위). 캔버스·RT·재질 사본은 첫 사용 때 한 번 만든다.
- 대쉬 잔상: SVN `DashAfterimage.cs` 가 Ghost 생성 때 리터럴 `1u << 4` 를 더한다(SVN 커밋 대기 — §5).

**적중 섬광 오너 화면 효과**(할 일 132 §15 — `InterruptHitFlashOverlay` 하나가 출력, 오너 화면에서만):
- **오버레이 캔버스**(②③④ 그림, ⑤⑥ 은 캔버스 끔 — `InterruptHitFlashRules.OverlayDraws`) = 암전 덮개 바로 위 캔버스(short.MaxValue − 1). 미리 곱한 알파 섞기 — ② 는 가산(배경 암전 그대로), ③④ 는 덮음.
  ② 플레어 층은 `1 − e^(−x·_FlareGain)` 로 부드럽게 포화(HDR 세기가 1 에서 잘려 딱딱한 원판이 되지 않게), 고리는 얇게(`_RingWidth` 0.01 → 퍼지며 3배로 흐려짐)·`_RingIntensity` 0.6·(1−t)² 로 사라짐.
  ③ 화이트아웃 불투명도 `_WhiteoutOpacity` **0.75** — 덮개 RT 남김 영역을 적중 지점 쪽으로 8번 번지게 읽어(`_WhiteoutSilhouetteBlur` 0.35) 그 자리를 `_WhiteoutSilhouette` 0.6 만큼 덜 덮는다 → 아래 블러 걸린 오너·대상이 흐린 실루엣으로 비친다.
  ④ 스미어는 **암전 덮개 RT**(`DarkAdaptationState.MaskCoverTarget`, 알파 = 1 − 남김)를 공격 방향 반대로 12번(뒤 25%·앞 100%) 읽되 매번 상하좌우 팽창(`_SmearGrow` 0.018)해 최댓값을 모은다 → 남김 영역(오너·대상·빛 모임)이 크고 하얀 덩어리로 공격 방향으로 늘어난다(`_SmearLength` 0.22).
  방향 = 런타임 `_SmearAxis`(§0.9), 모르면 재질 `_SmearDirection`.
- **렌즈 플레어**(②) = URP Lens Flare (SRP) — 적중 지점에 `LensFlareComponentSRP` 하나(첫 사용 때 생성, 가림 판정·거리 감쇠 끔) + 에셋 `LF_InterruptHitFlashStarburst`(원형 요소 13개, 베이지 `(1, 0.9, 0.74)`, 텍스처 없음 —
  §16: 가로 빛살 16(감쇠 0.75 = 끝이 서서히 흐림)·세로 4.2·대각 5/3.4/4.4/2.9·잔 빛살 2.1/3.3/1.7/2.6/1.4 + **무작위 빛살 16개**(요소 하나 Random 분포, 회전·길이 ±55%·세기 ±50%, seed 7) · 코어 2.2 원판(감쇠 0.05) → **0.9·감쇠 0.55**(작고 부드러운 코어)).
  카메라 컬러는 암전 덮개(HUD 위 캔버스)에 가려지므로 `DarkAdaptationFeature` 의 **플레어 층 패스**(`FlareLayerPassName`, AfterRenderingPostProcessing, 언세이프 패스)가
  켜진 플레어만 외부 RT(ARGBHalf 화면 크기, 첫 사용 1회·크기 바뀔 때만 다시)에 `LensFlareCommonSRP.DoLensFlareDataDrivenCommon` 으로 그리고, 오버레이가 덮개 위에 더한다.
  셰이더 = 렌더러 기능 칸 `_lensFlareShader`(URP `Hidden/Universal Render Pipeline/LensFlareDataDriven`, PC·Mobile 렌더러 둘 다 연결).
  → URP 에셋의 `Data Driven Lens Flare`(PC_RPAsset **꺼짐** · Mobile_RPAsset 켜짐)와 **무관하게** 돈다 — 켜는 변경 불필요. Mobile 은 URP 자체 플레어도 카메라 컬러에 그리지만 ② 동안 암전 덮개 아래라 안 보이고 ③ 부터 컴포넌트를 끈다.
- **방사형 블러**(③⑤⑥) = `DarkAdaptationFeature` 패스 2 `HitFlashRadialBlur`(같은 `_shader`) — BeforeRenderingPostProcessing, 휘도·마스크 패스 뒤.
  카메라 컬러(HDR)를 적중 지점 쪽으로 16번 겹쳐 읽는다 → 뒤의 Bloom·색수차(후처리)가 블러 위에 걸린다. **HUD 는 블러 밖**(오버레이 캔버스라 그 위에 그려진다).
- **색수차·블룸**(⑤⑥) = 이 출력이 만든 전역 `Volume` 둘(우선순위 **1000** — 씬 전역 Volume 위, 기본 레이어 = 카메라 Volume 마스크 Default) 의 **가중치만** 움직인다.
  가중치 w 면 씬 값 → 프로필 값으로 w 만큼 섞인다(블룸 = 씬 블룸에서 intensity **6**·threshold 0.8 쪽으로 — 섬광 순간 가중치 1). 프로필 에셋·씬 Volume 은 건드리지 않는다.
  색수차는 URP 후처리라 원래 화면 가장자리에서 번진다 — §16 에서 오버레이 무지개 원을 없애고 이것만 남겼다(세기 1 → **0.6**, 가장자리 살짝).
  재생 첫 프레임 가중치 스냅샷(`InterruptHitFlashVolumeSnapshot`) → 끝·강제 리셋에 그 값으로 복구(가중치 0 이면 Volume 꺼 둠).
- 평소 비용 0 — 블러 세기 0·플레어 대상 없음이면 패스를 넣지 않는다(`DarkAdaptationModes.Select` 5칸판).

**새 VFX(git, `Assets/5.VFX/Interrupt/`)** — 셰이더/파티클 절차 생성, 외부 이미지 없음:

| 연출 | 에셋 | 셰이더 | 덮어쓸 텍스처 칸 |
|---|---|---|---|
| 빛 모임(흰 빛 기둥 + 모여드는 입자 + 중심 빛) | `LightGather/FX_InterruptLightGather.prefab` · `M_InterruptLightGather_Pillar`·`_Mote` | `BeaverLobby/VFX/InterruptLightGather`(가산 언릿, HDR `_Color` → 블룸) | 재질마다 `_OverrideTex` |
| 월드 섬광(섬광 순간 — 흰 중심·파란 번짐·불티, 전부 가우시안 빛 덩어리 + 적중 지점 Point Light) | `HitFlash/FX_InterruptHitFlash.prefab` · `M_InterruptHitFlash_Core`(§16 광선 `_Rays`·고리 `_Ring` 자식·재질 삭제) | `BeaverLobby/VFX/InterruptHitFlash`(`_Falloff` 4 — 가우시안 날카로움) | 재질마다 `_OverrideTex` |
| 섬광 오버레이(②③④, 오너 화면) | `HitFlash/M_InterruptHitFlashOverlay.mat` | `BeaverLobby/UI/InterruptHitFlashOverlay` | `_StarburstTex`(② 가산) · `_WhiteoutTex`(③) · `_SmearTex`(④) + `_FlareGain` · `_RingColor`·`_RingIntensity`·`_RingWidth`·`_RingRadius` · `_WhiteoutColor`·`_WhiteoutOpacity`·`_WhiteoutSilhouette`·`_WhiteoutSilhouetteBlur`·`_StreakCount` · `_SmearColor`·`_SmearBackColor`·`_SmearDirection`·`_SmearLength`·`_SmearGrow` |
| ② 렌즈 플레어 | `HitFlash/LF_InterruptHitFlashStarburst.asset`(LensFlareDataSRP) | URP LensFlareDataDriven | 요소에 텍스처 칸(지금 없음 = 절차 원형) |
| ⑤⑥ 색수차 · 블룸 | `HitFlash/VP_InterruptHitFlash_Aberration.asset`(ChromaticAberration 0.6) · `VP_InterruptHitFlash_Bloom.asset`(Bloom 6·threshold 0.8·scatter 0.75·옅은 파랑 틴트) | URP 후처리 | — |

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
- **HUD 도 덮는다.** 오너 암전 동안 내 HP·쿨 HUD 가 안 보인다(계획서 확정). ③④ 섬광 중도 같다. ⑤⑥ 방사형 블러·색수차·블룸은 HUD 에 걸리지 않는다.
- **진행기 칸을 스케일로 바꾸면 ⑤⑥ 이 길어진다**(기본은 섬광 순간 뒤 실시간 0.3s). ②③④·섬광 순간은 렌더 프레임 수라 칸과 무관하다(§17).
- **②③④ 는 프레임레이트를 따른다**(§17) — 2 프레임 = 60fps 0.033s · 30fps 0.067s · 144fps 0.014s. 에디터·MPPM 처럼 프레임이 들쭉날쭉하면 화면에 머무는 실시간도 들쭉날쭉하다.
- **오너 암전 칸을 끄고 ④ 스미어만 켜면 남김 실루엣이 안 나올 수 있다**(Play 미확인) — 스미어는 암전 덮개 RT(`MaskCoverTarget`)를 읽는데, 암전 강도 0 이면 그 RT 가 비어 흰 텍스처(= 남김 없음)로 대신한다.
- **플레어 층은 Play 미확인.** URP 내부 함수(`DoLensFlareDataDrivenCommon`)를 외부 RT 에 직접 부른다 — RT 상하 방향·플레어 크기(요소 sizeXY × 0.1 화면 단위)·세기는 Play 로 확인·튜닝한다.
  URP 버전이 바뀌면 그 함수 인자가 바뀔 수 있다(지금 17.3).
- **⑥ 이후 블룸은 씬 Volume 값으로 돌아간다** — 섬광 Volume 은 가중치만이라 씬에 블룸이 없으면(활성 끔) ⑤⑥ 동안만 블룸이 생긴다.
- 강도·색·단계 길이·이펙트 시간 기준·블룸 세기는 Play 튜닝 전 처음 값이다. §16 값(플레어 요소·고리·화이트아웃·스미어·라이트 세기/반경)도 **Play 미확인** — §0.9.

### 0.9 §16 퀄리티 수정 — 라이트·스미어 방향·렌더 판단

**적중 지점 Point Light**(`InterruptHitFlashVfx`, 월드 섬광과 같은 순간·모든 피어): 첫 재생 때 자식 `HitLight` 하나를 만들어 재사용(재생마다 생성 없음, 쉬는 동안 꺼짐 — 추가 라이트 슬롯을 차지하지 않음).
섬광 순간 `lightIntensity` **12**(색 `(0.62, 0.8, 1)` 파랑·흰, 반경 `lightRange` **7m × 루트 스케일**, 그림자 `lightShadows` **끔**)로 번쩍 켜져 `lightDuration` **0.25s**(실시간) 동안 `lightCurve`(1 → 0.35@0.35 → 0)로 꺼진다. 강제 리셋 = 즉시 끔.
**빛 모임 소켓 Point Light**(`InterruptLightGatherVfx`): 첫 재생 때 자식 `GatherLight` 하나(재사용). 차지 시작 0 → `lightRiseDuration` **0.5s**(빛 모임 시계, 기본 스케일) 동안 `lightRiseCurve`(EaseInOut)로 `lightIntensity` **3**(반경 2.5m, 색 `(0.85, 0.92, 1)`, 그림자 끔)까지 올라 유지, 정지(= 발동자 Hit·실패·리셋)에서 바로 끔.
- 라이트 칸은 전부 **프리팹(이펙트 컴포넌트)** 칸 — 플레이어 Variant 를 건드리지 않는다(빛 모임 VFX 는 소켓이 첫 재생 때 플레이어 아래 한 번 만든다).
- 렌더링 레이어 = 기본(비트 0 = 조명). 오너 암전 남김 비트 4 와 무관하게 모든 렌더러를 비춘다.

**월드 섬광 실시간 상한**(`InterruptHitFlashVfxClock`): 파티클은 스케일 시간 그대로 두되, 프레임마다 '남은 효과 길이 ÷ 남은 실시간' 비율로 `simulationSpeed`(≥ 1)를 올려 `realTimeCap` **0.3s** 안에 끝낸다.
성공 슬로우 0.25 에서 0.45s 짜리 빛 덩어리가 1.8s 남던 것 → 0.3s. 슬로우가 없거나 약해 원래 상한 안이면 배속 1(빨라지지 않음). 시간이 멈추면(스케일 0) 상한에서 남은 입자를 지운다. 0 = 상한 없음.

**④ 스미어 방향 출처** = **오너 상태**(`InterruptOwnerPresentation.AttackDirection`) — 입력 순간 예측(`PlayerSkillController.PredictOwnerInterrupt`)이 받은 공격 방향을
`Director.LocalOwnerAttempt(…, attackDirection)` → `Predict(…, attackDirection)` 로 그 시도에 저장한다(근접 = 요청 RPC 에 싣는 시전 방향, 대쉬 = 대시 방향 — 대상 판정 ③과 같은 값).
성공 `Started` 에 디렉터가 `HitFlash.BeginOwnerOverlay(적중 지점, AttackDirection, now)` 로 넘기고, 출력이 적중 지점과 '적중 지점 + 방향 1m' 를 `Camera.main` 뷰포트로 투영해
화면 방향(화면 높이 단위, `InterruptHitFlashRules.TryScreenDirection`)을 `_SmearAxis` 에 넣는다. 성공 사건(서버 `Started`)에는 방향이 실려 오지 않는다(새 RPC·필드 없음) —
오버레이는 오너 화면에서만 돌고 오너는 자기 입력 방향을 이미 알기 때문. 카메라 뒤·카메라 축과 나란한 방향·모름(0) = 재질 기본 방향.

**URP 추가 라이트 한도**(조사 2026-10-28):
| | PC (`PC_RPAsset` + `PC_Renderer`) | Mobile (`Mobile_RPAsset` + `Mobile_Renderer`) |
|---|---|---|
| 렌더링 경로 | **Forward+** (`m_RenderingMode: 2`) | **Forward** (`m_RenderingMode: 0`) |
| 추가 라이트 | 픽셀(`m_AdditionalLightsRenderingMode: 1`) · 물체당 한도 4 는 Forward+ 에서 **무시**(클러스터, 화면 전체 가시 라이트 한도 내) | 픽셀 · **물체당 4개** — 이미 라이트 4개 받는 물체는 섬광 라이트 중 하나가 빠질 수 있다(가까운·밝은 순) |
| 추가 라이트 그림자 | 지원(켬) — 섬광 라이트는 기본 끔 | 미지원 — 칸을 켜도 그림자 없음 |
| 라이트 레이어 | 켬(`m_SupportsLightLayers: 1`) — 라이트·렌더러 비트 0 이 겹쳐야 비춤 | 켬 |
영향: 섬광·빛 모임 라이트는 짧게(0.25s·차지 동안)만 켜지고 쉬는 동안 `enabled = false` 라 평소 비용 0. 다인원이 동시에 간파하면 피어 수만큼 동시 라이트(최대 플레이어 수 × 2).

**오너 암전 중 남긴 오브젝트가 라이트를 받나** — **받는다.** 마스크 남김 패스는 비트 4 렌더러를 원래 머티리얼(`UniversalForward`)로 `CreateDrawingSettings(… lightData …)`·전역 텍스처/상태 그대로 다시 그리므로
라이팅 경로가 같다(Forward+ 클러스터 버퍼·Forward 물체당 라이트 인덱스 모두 같은 프레임 값). 단 그 재그리기는 **덮개 알파(남김 모양)만** 만들고, 화면에 보이는 남김 픽셀은 원래 카메라 컬러다 —
즉 오너·대상·빛 모임은 실제 라이트를 받은 모습 그대로 뚫린 구멍으로 보인다. 덮인 월드도 빛을 받지만 덮개에 가려 안 보인다.
시간상: 빛 모임 라이트는 암전 동안 켜져 있어 오너·무기 주변만 밝히고(덮인 바닥은 안 보임), 적중 지점 라이트는 섬광 순간(= 암전 걷힘)에 켜져 걷힌 월드 전체를 비춘다.

### 0.10 선착순 한 명 (할 일 143)

보스 간파 창 하나에 여러 플레이어가 간파를 넣으면 **서버에서 먼저 성공 판정된 1명만** 지금처럼 처리한다(넉백·보스 반응·132 연출 전부).
두 번째 이후 = **피해만** — 간파로 생기는 대상 반응(카운터 그로기·칸 감소·취약 넉백·기믹 처리)도, 연출(A1 히트스톱·성공 슬로우·A3 줌·A4·오너 암전·적중 섬광·A2 암순응·B3·C2·E1)도 없다.

**왜 생기나(실제 경로).** 23호·중간보스·스파링 봇·허수아비는 첫 성공을 받는 **그 수신 호출 안에서 창을 닫고 취약/그로기로 넘어간다**. 그래서 두 번째 사람의 판정은
'창 닫힘 = 실패'가 되지만, 표식(`isInterruptAttack`)이 그대로라 23호·스파링 봇에선 **취약 중 간파 = 방향 무관 넉백**이 한 번 더 났다(첫 성공자의 반응을 덮음).
또 둘 다 창이 열린 동안 눌렀으면 두 오너 모두 강 시도(암전·빛 모임)가 돌았다.

| 항목 | 규칙 |
|---|---|
| 장부 | `InterruptWindowClaims`(순수 클래스, `Assets/1.Scripts/Player/Skill/`) — 서버 런타임 인스턴스 `InterruptWindowClaims.Server`. 키 = 대상 GameObject 인스턴스 id · 시전자 `Player` 인스턴스 id |
| 창 식별 | 대상의 카운터 창이 **한 번 열린 것** = 한 창. 기존 창 전이 훅 `InterruptLagMonitor.RecordWindow`(23호 `SetCounterWindow`·`MonsterCounterWindow`·허수아비)가 장부에도 넘긴다 — **보스 코드 무수정**, 새 복제값 없음 |
| 기록 | `ResolveHit` 에서 카운터 성공(성공 수 전후 비교)이 난 대상에 `Claim(대상, 시전자, 서버 Time.time)` — 첫 성공자만 남는다 |
| 새 창 = 초기화 | 창이 열리면 그 대상 기록을 새로 시작한다(앞 창 첫 성공자 버림) |
| 닫힘 = 늦게 시작한 시전만 초기화 | 보스가 첫 성공 수신 안에서 창을 닫으므로 닫힘은 기록을 지우지 않는다. **다른 사람이 성공시킨 창이 열려 있거나, 닫혔어도 이 시전이 첫 성공 시각 이전(같은 시각 포함)에 서버에서 시작했으면 선점**. 닫힌 뒤 새로 시작한 시전은 자유 — 취약 중 간파 넉백 같은 기존 규칙 그대로 |
| 같은 사람 | 첫 성공자 자신의 다른 시전 = 자기 창(정상 처리) |
| 대상별 | 대상마다 따로 — 23호 창을 선점당해도 다른 대상(중간보스 등)은 독립 |
| 두 번째 처리(한 곳) | `PlayerInterruptSkillBase.ResolveHit`(근접·대쉬·팔라딘·거너·어쌔신 공통) — 선점된 대상에는 **`isInterruptAttack: false`** 로 수신시킨다. 대상은 표식이 없으면 간파 반응을 아예 보지 않는다(피해·제압 배율·자폭 실드 흡수 등 피해 규칙은 그대로) |
| 연출(디렉터) | 판정 사건 `Resolve` 에 **`Preempted`**(선점된 대상이 있고 이 판정에 성공이 하나도 없음)를 싣는다. 성공 사건은 안 나므로 성공 연출 전부 없음. 디렉터 판정 단계가 `InterruptProductionRules.ResolveFailsAttempt(landed, preempted)` 로 시도 슬로우를 **실패 복귀** — 각 피어 `Failed` → 오너 암전 걷기·빛 모임 정지(§0.2) |
| 선점 시도 억제 | 서버 승인 예측(`FindPredictTarget`, 서버 쪽)이 장부를 본다 — 남이 성공시킨 창은 닫힌 것처럼 예측 → 강 시도 슬로우 없음. 오너 즉시 예측은 아래 **로컬 장부**를 본다(할 일 143-2) |
| 로컬 장부(1차 창 닫기, 할 일 143-2) | `InterruptLocalWindowClaims`(순수 클래스, `Assets/1.Scripts/Player/Skill/`) — 각 피어(호스트 포함) 런타임 인스턴스 `InterruptLocalWindowClaims.Local`. 키 = 대상 NetworkObjectId. **닫힘** = 이 창에서 **남의 강 시도**(`Started(Attempt)`) 또는 **남의 성공**(`Started(Success)`)을 받음 — 디렉터 `HandleStarted` 한 곳이 넘긴다. **남의 약 시도(`AttemptWeak`)·내 것은 안 닫음**(그 사람이 실패하면 내가 첫 성공일 수 있다). **새 창 = 초기화** — 서버 장부와 같은 복제값 열림 전이(`IInterruptWindowReplica` 대상이 부르는 `InterruptWindowReplicaTracker.Record`, 호스트도 지난다). 디스폰(`Forget`) 때 지움 |
| 로컬 장부 읽기(한 곳) | 오너 즉시 예측 `PlayerInterruptSkillBase.FindPredictTarget`(ownerView — 근접·대쉬, 팔라딘·거너·어쌔신 공통)이 닫힌 창을 **닫힘**으로 본다 → 예측 약 → 오너 암전·빛 모임·섬광 없음. **피해 요청은 그대로 보내고 최종 판정은 서버 장부**다 |
| 대상 찾기(전송 없음, 할 일 143-3) | **슬로우 메시지·`GlobalTimeScale.Started`·`ITimeScalePort` 모양은 그대로**(대상 칸 없음 — 143-2 의 `TargetObjectId` 칸은 되돌림). 각 피어가 이미 아는 것으로 대상을 다시 찾는다 — 디렉터가 `TriggerClientId` 로 시전자 플레이어 오브젝트(스폰 목록)의 간파 슬롯 스킬을 찾아: **강 시도** = `FindPeerAttemptTargetId` — 시전자 복제 위치·바라보는 방향으로 오너 즉시 예측과 같은 사거리·정면각 탐색(창 = 복제값, 로컬 장부 무시), 거기 열린 창이 없으면 시전자에게 가장 가까운 열린 창(강 = 서버가 열린 창을 봤다). **성공** = `FindPeerSuccessTargetId` — 성공 슬로우의 연출 기준 위치(서버가 실은 첫 성공 대상 위치)가 판정 콜라이더 범위 안인 대상 + 그 시전자의 강 시도가 닫아 둔 창. 새 RPC 없음, **추가 빌드 조건 없음** |
| 다시 열림(할 일 143-3) | 창을 닫은 **남의 강 시도가 실패**하면(그 발동 번호의 `Failed`·`ForcedReset` 수신 — 디렉터 `HandleFailed`/`HandleForcedReset` → `AttemptEnded`) 다시 연다 → 그 뒤 내 간파는 정상 강 예측·연출. **성공으로 닫힌 창은 계속 닫힘**(실패 신호가 와도). 한 창을 여러 사람 강 시도가 닫았으면 **전부 실패해야** 열림(성공이 하나라도 있으면 닫힘 유지) |
| 거의 동시 | 상대 신호가 닿기 **전**에 둘 다 열린 창에서 눌러 강 시도가 돌던 중 서버 판정이 '두 번째'로 오면 → 섬광 없음 · 시도 슬로우 실패 복귀 · 오너 암전 0.2초 걷기(`Failed`). 새 RPC 없음 |
| 정적 이벤트 | `ServerInterruptSucceeded`·`CombatStatsEvents.ServerCounterSucceeded` = **두 번째에겐 안 남**(창을 소비하지 않았다 — 결과 화면 간파 수·튜토리얼 '간파 성공'에 중복으로 안 잡힌다). `ServerInterruptResolved` = **그대로 남**(판정 1회·수신 대상 수 — 피해는 들어갔다. 구독자는 레거시 존3 튜토리얼 피드백뿐이고 반응 트리거가 아니다). 피해 통계는 일반 피해 경로라 그대로 |

**알려진 한계**
- 같은 창 판정은 **서버 시작 시각** 기준이다. 오너 화면에선 창이 열려 보였지만(복제 지연) 서버에선 첫 성공 뒤에 승인된 시전은 '새 시전'이라 취약 넉백이 난다 — 서버 기준 규칙(사양).
- **지연 한계(143-2)**: 상대 신호(강 시도·성공 슬로우)가 내 피어에 닿기 **전**의 동시 입력은 로컬 장부로 못 막는다 — 기존 서버 판정 뒤 암전 걷힘(`Failed`, 0.2초)으로 처리된다.
- **실패 신호가 없는 시도(143-3)**: 슬로우는 마지막 것이 이긴다 — 남의 강 시도 A 가 다른 슬로우(B)에 덮인 뒤 빗나가면 서버 `ServerFail(A)` 이 무시돼 A 의 실패 신호가 오지 않는다. 그 창은 새 창이 열릴 때까지 닫힘으로 남는다(보수적 — 내 다음 입력은 약 예측, 판정은 서버). 시도 슬로우가 이미 끝난 뒤의 실패(`NoEffect`)도 같다.
- **피어 대상 찾기 근사(143-3)**: 시전자 위치·방향은 이 피어의 복제값이고 대쉬 간파의 예상 대시 끝은 모른다 — 사거리·정면각 탐색이 빗나가면 가장 가까운 열린 창으로 고른다. 열린 창이 여럿 가까이 있으면 다른 대상을 닫을 수 있다(연출만 영향, 판정은 서버).
- 창 열림 복제값과 상대 시도 신호가 같은 서버 프레임에 나가면 클라에서 시도 신호가 먼저 처리되고 뒤이은 열림이 장부를 지울 수 있다(드묾) — 그때는 위 지연 한계와 같이 서버 판정으로 정리된다.
- 한 시전이 선점된 대상과 다른 대상의 정상 성공을 같이 맞히면 정상 성공 쪽 연출은 그대로 난다(`Preempted` = 성공이 하나도 없을 때만).
- 간파 지연 측정 로그(`[InterruptLag]`)는 두 번째 사람의 판정을 실패로 적는다(창 닫힘) — 측정 전용이라 손대지 않았다.
- 장부는 디스폰 때 지우지 않는다(인스턴스 id 는 세션 안에서 재사용되지 않음). 도메인 리로드 끈 Play 진입은 `SubsystemRegistration` 에서 비운다.
- 테스트: `InterruptWindowClaimsTests`(기록·두 번째 거절·같은 사람·닫힘/새 창 초기화·대상별), `InterruptProductionStageTests.판정_선점이면_시도_슬로우_실패_복귀`,
  `InterruptLocalWindowClaimsTests`(남의 강 시도·성공 닫힘·남의 약 시도·내 것 안 닫음·새 창 초기화·대상별·디스폰), `InterruptProductionStageTests.시도와_성공_슬로우는_대상_id_를_싣는다`, `SlowMotionSessionTests.Start_이벤트_대상_id_직렬화_왕복`. 판정 지점·창 전이 연결·연출 정리는 MPPM 2인 Play 확인.

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
- 컷신·씬 전환·세션 종료로 `ForcedReset` 이 오면 암순응·오너 암전·빛 모임·섬광 화면 효과(오버레이·플레어·블러·Volume 가중치 스냅샷 복구)는 즉시 0, 남은 섬광 순간 취소, 월드 섬광 지움. 원샷 성공 이펙트(A4)는 끊지 않는다.

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
- **줌 한 번(10-28 수정)** — `SlowMotionOwnerZoom`(순수 모델, 카메라가 매 프레임 질의해 넣는다): 내 간파 한 건(시도 → 이어진 내 성공 = 발동 번호 사슬)을 한 줌 세션으로 본다.
  - 세션 동안 목표 깊이는 복귀 단계(Exit·FailExit) 전까지 줄지 않는다. 강 시도가 결론 전(`Director.OwnerAwaitsStrongResult` = 오너 상태 `AttemptStrong`)이면 **시도 Exit 중에도 붙잡는다** → 시도 유지가 끝난 뒤 성공이 와도 다시 줌인하지 않는다.
  - 배율은 세션에서 본 가장 깊은 쪽(min) — 강(0.7) → 성공 = 0.7 유지, 약(0.93) → 성공 = 0.85 로 더 깊게만. 성공 칸 `slowMotionZoomFactor` 는 약 → 성공·시도 없는 성공에서 쓰인다(남김).
  - 세션 끝 = 성공 슬로우 복귀·실패 복귀·시도 슬로우 끝(결론 없음)·남의 슬로우 덮음·강제 리셋·낙하/관전 → 한 번 복귀.
  - 알려진 한계: 시도 슬로우가 **완전히 끝난 뒤**(강 기준 입력 후 0.8초) 성공이 오면 새 세션이라 다시 줌인한다. 약 시도는 결론 전 붙잡지 않는다 — 약 Exit 뒤 성공이면 다시 깊어진다(약 = 실패 예측이라 늦게 빠지는 쪽을 피함).

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

**언제.** 할 일 132 단계 5 부터 성공 `Started` 즉시가 아니라 **적중 섬광 순간**(②③④ 뒤 = ⑤ 시작, 기본 성공 Started 6 프레임째)에 튼다. §17 부터 A2 기본 꺼짐.

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
A4 칸은 켜져 있다 — 성공 순간(Started 즉시) 원샷이라 섬광(6 프레임 + 0.3초)과 겹쳐 보이면 칸을 비운다.
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
  스위치 11칸(cue 10 + A5 피해 숫자 숨김) + 강·약 시도/성공 슬로우 프로필 3개 + 132 오너 암전·적중 섬광 칸(§0.5)을 들고, 재생 지점은 **정적 API** 로 묻는다(인스턴스가 없으면 전부 꺼짐·no-op).
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
| 판정 | `ResolveHit` 끝 → `ServerResolve` → 빗나가거나 **선점됨**(`Preempted`, 할 일 143 — 남이 먼저 성공시킨 창, 피해만)이면 시도 슬로우 `ServerFail` | [오너] 애니 Hit → `LocalOwnerHit`(빛 모임 끝) · 팀원 `PeerCasterHit` · 선점 = `Failed` 로 오너 암전 걷기([§0.10](#010-선착순-한-명-할-일-143)) |
| 성공 | 판정 바로 뒤 `ServerSuccess` — 그 창의 **첫 성공자만**(선점된 대상은 표식 없이 수신돼 성공이 안 난다, §0.10) → 성공 슬로우(`interruptSuccessProfile`, A1 칸 = `hitStopOn`) → 시전자 아래 `IInterruptSuccessRelay` 마다 1회 | `Started(Success)` → A4 즉시 · 적중 섬광 진행기 → 섬광 순간 월드 섬광·A2 · RPC 수신부(B3·C2·E1, 꺼짐)가 `PlayCue` |
| 실패 | 판정 전 종료(`OnEnd`)·앵커 누락 → `ServerFail` → 시도 슬로우 실패 복귀 | `Failed` → 오너 암전 걷기·빛 모임 정지(실패 단계 자체 연출 없음) · [오너] `OnEnd`·거부 → `LocalOwnerEnded` |

- 대쉬 간파(할 일 115)도 같은 사건 4개를 쓴다. 예측 판정 위치는 예상 대시 끝, 예측 시점은 우클릭 누른 순간 — [dash-interrupt.md §5](dash-interrupt.md#5-118-디렉터-사건-연결--새-경로-없음).
- 호출 순서는 옛 코드와 한 줄씩 같다: 빗나감 Fail → 성공 Start → `ReportLag`. 캐릭터 relay 는 성공 Start 바로 뒤라 옛 정적 이벤트 구독보다 `ReportLag` 한 줄 앞이다(같은 프레임·같은 RPC 순서, 화면 차이 없음).
- 정적 `PlayerInterruptSkillBase.ServerInterruptSucceeded/Resolved` 는 **로직용으로 남아 있다**(튜토리얼·통계). 연출 구독자(팔라딘·어쌔신)만 relay 로 옮겼다.
- 슬로우가 거부돼도(컷신 잠금·씬 전환) relay·섬광 RPC 는 그대로 나간다 — 전송로를 바꾸지 않았다(설계 Q1-a). 그래서 그때 A2·A4 만 빠지고 B3·C2·E1 은 나온다(지금과 같음).

### 6.3 개인 / 공용 (보는 사람 기준)

| 연출 | 칸 | 채널 | 판단 |
|---|---|---|---|
| 시도(강·약)·성공 슬로우 · 히트스톱 | — · A1 | 서버 타임라인(전 피어 공용) | 채널을 거치지 않는다. `Time.timeScale` 은 피어 전역이라 개인 슬로우는 불가 |
| 오너 줌 | A3 | 개인 | 카메라가 매 프레임 `Director.OwnerZoomFactor(목적, 성공 배율)`·`Director.OwnerAwaitsStrongResult` 질의 → `SlowMotionOwnerZoom`(시도~성공 줌 한 번). 내 슬로우인지는 카메라가 본다 |
| 오너 암전 · 섬광 오버레이 | 효과 칸 `show*`(§0.6) | 개인(오너 상태 기계) | §0.3 |
| 빛 모임 · 월드 섬광 · 적중 지점 라이트 | 효과 칸 `show*`(§0.6) | 오너 = 입력 순간 예측 / 팀원·모두 = 슬로우 이벤트 | §0.3 |
| 암순응 self 강도 | A2 | 개인 | 발동자 == `LocalClientId` ∧ 섬광 오버레이 안 돎(약 시도 성공), 섬광 순간 |
| 암순응 others 강도 | A2 | 공용 | 그 외 화면, 섬광 순간. 칸은 하나(설계 Q3-a) |
| 성공 이펙트 칸 casterFx/targetFx | A4 | 공용 | 발동자 소유 켜진 `InterruptSuccessFx` |
| 방패 발광·루프 · 기 모으기 · 어쌔신 기 모으기/튀기기/올려치기 | B1 · B2 · C1 | 공용 | 전 피어 `OnClientPlay`·클립 이벤트 |
| 충격파 · 어쌔신 성공 · 맞은 쪽 섬광 | B3 · C2 · E1 | 공용 | 서버 → 기존 RPC → 수신부 `PlayCue` |

### 6.4 스위치·프로필 위치

`NetworkManager.prefab › InterruptProductionDirector`. 인스펙터는 `[Header]` 로 "시도 — 공용 / 성공 — 공용 / 성공 — 개인 / 시도·결론 — 개인(오너 암전·빛 모임) / 성공 — 적중 섬광" 으로 묶여 있다.
**스위치 기본값(할 일 132 단계 6 → §17)**: 공용 칸 **A1·A3·A4 = 켬**, **A2 = 꺼짐**(§17 기본값 — 팀원 암순응 끔), 캐릭터별·맞은 쪽 연출 **B1·B2·B3·C1·C2·E1 = 꺼짐**(새 적중 섬광이 대신).
새 컴포넌트(코드 기본값)는 여전히 전부 꺼짐이다 — 켬은 프리팹 값이다. 효과 칸 `show*` 는 [§0.6](#06-연출-켜기끄기-칸--튜닝-패널-17).

| 칸 | 필드 | 프리팹 | 무엇이 언제 | 재생 지점 → 디렉터 |
|---|---|---|---|---|
| — | `interruptProfile` · `interruptWeakProfile` | — | 강·약 시도 슬로우 모양 | 시도 단계 `ServerStart` (xlsx `SlowMotion`, §2.1) |
| — | `interruptSuccessProfile` | — | 성공 슬로우 모양 | 성공 단계 `ServerStart` (xlsx `SlowMotion`, §2.1) |
| A1 | `hitStop` | **켬** | 성공 슬로우 맨 앞 정지(섬광 ② 가 같은 순간 시작) | 성공 단계가 `GlobalTimeScale.ServerStart(…, hitStopOn)` 로 넘긴다 — 꺼지면 이벤트의 `hitStopDuration` 0 |
| A2 | `darkAdaptation` | 꺼짐(§17) | 성공 섬광 순간 화면 휘도 암순응(팀원 others · 오버레이 없던 본인 self) — 튜닝 패널 '팀원 암순응' | 섬광 순간 개인/공용 채널 → `InterruptDarkAdaptation.Play` |
| A3 | `ownerZoom` | **켬** | 내가 일으킨 슬로우(강·약·성공) 동안 카메라 줌 | `CameraTargetSwitcher` → `Director.OwnerZoomFactor` — 꺼지면 배율 1 |
| A4 | `successFxSlot` | **켬** | 공용 성공 이펙트 칸(§2.4, base·Variant 칸은 지금 비어 있음) | 성공 단계 공용 채널 → `InterruptSuccessFx`(sink) |
| B1 | `paladinShieldGlow` | 꺼짐 | 팔라딘 간파 시도 방패 발광·루프 | `FirstMeleeInterruptSkill.OnClientPlay` → `Director.LocalAttempt` → 스킬(sink). `OnEnd` 끄기·회수는 늘 돈다 |
| B2 | `paladinShieldCharge` | 꺼짐 | 팔라딘 방패 기 모으기(클립 `Garen_Parry` 의 `ShieldCharge`) | `EffectAnimEvents.PlayEffect`·`StartEffect` → `Director.AllowsAnimEffect(id)` |
| B3 | `paladinWave` | 꺼짐 | 팔라딘 성공 충격파 | `PlayerShieldVfx.PlayInterruptWave`(RPC·오프라인) → `Director.PlayCue(PaladinWave, …, this)` |
| C1 | `assassinInsightAttempt` | 꺼짐 | 어쌔신 기 모으기·튀기기·올려치기(클립 `Attack_Up_01`) | `EffectAnimEvents.PlayEffect`·`StartEffect` → `Director.AllowsAnimEffect(id)` |
| C2 | `assassinInsightSuccess` | 꺼짐 | 어쌔신 간파 성공 이펙트 | `AssassinSkillView.TryPlayInsightSuccess`(대기·올려치기 프레임 맞추기는 View) → `Director.PlayCue(AssassinInsightSuccess, …, this)` |
| E1 | `targetFlash` | 꺼짐 | 맞은 쪽 섬광 `Interrupt_Flash` | 23호·건틀릿봇·스피너봇·월봇·허수아비·스파링 봇 `PlayInterruptFlashRpc` → `Director.PlayCue(TargetFlash, …, this)` |
| A5 | `hideDamageNumbers` | **켬**(코드 기본값 — 프리팹에 저장 안 됨) | 피해 숫자 숨김(154) — 내 강 시도 암전 시작 ~ 성공 섬광 ⑥ 가라앉음 끝(실패·판정 전 종료·예측 빗나감이면 오너 암전 걷기 끝) 동안 **본인 화면만** 피해 숫자 캔버스 끔. 숫자 생성·수명은 그대로 | `FloatingDamageSpawner.LateUpdate` → `Director.DamageNumbersHidden` = `InterruptProductionRules.ShouldHideDamageNumbers(칸, 오너 상태 기계 State == AttemptStrong ∨ DimIntensity > 0 ∨ 섬광 진행기 OverlayActive)`. cue(`InterruptProductionCue`) 아님 — 런타임 값은 `director.HideDamageNumbers` |

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
- **나머지 10칸(A5 포함) = 각 피어 자기 인스턴스 값**(동기화 없음, 설계 Q8-a) → MPPM 가상 플레이어 화면은 호스트에서 바꿔도 안 바뀐다.
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
`InterruptLightGatherTests` · `InterruptHitFlashTests` · `InterruptProductionDirectorTests` 의 프리팹 스위치 기본값(A1·A3·A4 켬) ·
`InterruptPresentationEffectTests`(§17 효과 칸 기본값·프리팹·그리기만 끔·섬광 순간 정상) · `DebugInterruptLabTests`(튜닝 패널 줄·배속 버튼).
할 일 154: `InterruptDamageNumberHideTests`(A5 — 강 시도 시작·성공 ⑥ 끝·실패 걷기 끝·약/대상 없음·오너 아님·칸 끔·기본값 켬).
격리는 메뉴 밖 `DebugInterruptLabIsolationTests`. 예측기·대상 판정은 Dash 어셈블리(`BeaverLobby.Player.Dash.EditModeTests` — `DashInterruptPredictionTests`·`InterruptTargetGeometryTests`).

## 7. 이후 확장

- ~~암순응 셰이더에 흑백 실루엣·반전 모드~~ → 132 마스크 암전 모드·섬광 오버레이 흰 반전으로 구현(§0).
- ~~주변 입자가 한 점으로 모이는 효과 + 피격 대상 충격파~~ → 132 빛 모임·월드 섬광으로 구현(§0.7). 손그림으로 바꿀 땐 덮어쓸 텍스처 칸.
- 115 대쉬 간파: 사건 `Variant`(Normal/Dash) 칸은 만들지 않았다(일반 간파와 같은 프로필·cue, [dash-interrupt.md](dash-interrupt.md)) — 다른 프로필·cue 가 필요해지면 그때 이 칸을 더한다.
  117 성공 예측 연출은 132 의 오너 즉시 예측(§0.2)으로 대신했다.
