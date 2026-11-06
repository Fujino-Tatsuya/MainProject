# 간파 연출 — 시도 강·약 · 오너 암전 · 빛 모임 · 적중 섬광 · 슬로우·히트스톱·암순응·성공 이펙트 칸·오너 줌

> 🔹 할 일 132(2026-10-28, 브랜치 `feature/InterruptPresentation2`): 시도를 **예측 3상태(대상 없음/약/강)** 로 나누고, 강 시도에 **오너 암전·빛 모임**,
> 성공에 **적중 섬광 ②~⑥ 5단계**(§15 재구성 — 스타버스트·화이트아웃·스미어·색수차 블러·가라앉음) 를 얹었다. 흐름 [§0](#0-132-체계--예측-3상태강약오너-암전빛-모임적중-섬광-할-일-132) 이 이 문서의 맨 앞 사실이다.
> 🔹 §16 퀄리티 수정(사용자 Play 녹화 분석): 섬광이 딱딱한 도형처럼 보이던 것 — 부드러운 코어·제각각 빛살·얇은 고리, 화이트아웃 0.75·실루엣 비침, 스미어 = 공격 방향,
> 무지개 원 제거, 월드 섬광 = 빛 덩어리·실시간 0.3s 상한, 섬광 순간 블룸 최대, **실제 Point Light 2개**(적중 지점·빛 모임 소켓) — [§0.9](#09-16-퀄리티-수정--라이트스미어-방향렌더-판단).
> 🔹 §17 튜닝 도구(2026-10-28): 디렉터에 **효과별 켜기/끄기 칸 9개**(기본 = **오너 암전만 켬**, 팀원 암순응 = A2 칸 → 기본 끔) — 끈 효과는 그리기만 안 하고 상태 기계·시간표·섬광 순간은 그대로.
> ②③④ 길이 = **렌더 프레임 수**(기본 2·2·2, 섬광 순간 = 6 프레임째), ⑤⑥ = 초. DebugInterruptLab 씬 OnGUI **튜닝 패널** — [§0.6](#06-연출-켜기끄기-칸--튜닝-패널-17).
> 🔹 할 일 143(2026-10-29, 브랜치 `feature/InterruptFirstComeOnly`): **선착순 한 명** — 한 창을 서버에서 먼저 성공시킨 1명만 정상 처리, 두 번째 이후는 피해만(반응·연출 없음) — [§0.10](#010-선착순-한-명-할-일-143).
> 🔹 할 일 179(2026-11-02, 브랜치 `feature/SuppressInterruptFlash`): **제압 간파**(제압 칸 마지막 1칸 간파) 성공은 ②~⑥ 대신 **애니메이션 섬광 시퀀스**(모든 피어, 컷 SO·후처리 뒤 셰이더 1패스, 암전은 오너만, 섬광 줄이기 옵션) — [§7](#7-제압-간파-애니메이션-섬광-할-일-179).
> 🔸 **할 일 211(2026-11-04, 브랜치 `feature/SuppressInterruptV3`)**: **제압 간파 = V3, 시퀀스는 Legacy.** 제압 간파 성공도 일반·대쉬와 같은 적중 라이트 V3(암순응 나)·**오너 암전 없음**. 섬광 시퀀스 코드·셰이더·SO·재질은 `Legacy/` 폴더로 옮기고(guid 유지) 런타임 연결(A6·A7·제압 예측·섬광 줄이기 PlayerPrefs)을 끊었다. 슬로우 Start 이벤트의 제압 비트·`ServerMarkSuppressInterrupt` 는 그대로 — 단계 판정(`Suppress`)과 랩 '받은 성공' 표시용. §7 은 기록용.
> 🔹 **할 일 191(2026-11-02)**: 예측 입력이 **`InterruptManager.Predict`**(누른 시각 표본)로 바뀌고, 성공은 **오너 클라 판정 → 서버 검증**이다. 오너 연출은 매니저의 **오너 판정 cue**(시도·판정 성공/실패·서버 거부·CC 끊김·승인 목록)로 움직인다.
> 옛 예측기·약 → 강 올리기(`LocalOwnerUpgrade`)는 삭제. 단일 원본 [interrupt-client-judge.md](interrupt-client-judge.md) — 이 문서는 [§0.2](#02-흐름)·[§6.2](#62-사건-흐름) 에 요약.
> 🔹 **할 일 192(2026-11-02, 브랜치 `feature/InterruptTierPresentation`)**: 간파 연출을 **4단계(실패 / 간파 / 대쉬 간파 / 제압 간파)** 로 나눴다 — **오너 암전·빛 모임은 제압 간파만**, 일반·대쉬 간파는 암전 없이 강한 줌 + 기존 섬광, 제압 = 암전 + 섬광 시퀀스 + **제압 암순응 컷**(→ 11-04 할 일 211: 제압도 암전 없이 V3). 단계표·누가 보나·스위치 [§8](#8-간파-연출-4단계-할-일-192).
> 🔹 할 일 118: 간파 연출은 **간파 연출 디렉터 `InterruptProductionDirector`** 가 단일 진입점이다 — 사건 흐름·개인/공용 채널·스위치/프로필 위치는 [§6](#6-간파-연출-디렉터--interruptproductiondirector-할-일-118).
> 🔹 **할 일 196(2026-11-03, 브랜치 `feature/InterruptCameraCenter`)**: 내 간파 클릭 즉시 **카메라 중심을 간파 대상 쪽으로**(오너만, 줌과 독립) — 실패 = 즉시 복귀, 성공 = 성공 슬로우 복귀 구간에 복귀 — [§10](#10-카메라-대상-중심-할-일-196).
> 🔹 스위치 12칸(디렉터): **A1 히트스톱·A3 오너 줌·A4 성공 이펙트 칸·A5 전투 표시 숨김(154·179 후속 2)·카메라 대상 중심(196) = 켬**, **A2 암순응 = 꺼짐**(§17 기본값 — A6 섬광 시퀀스·A7 섬광 범위는 11-04 할 일 211 로 삭제), B·C·E(캐릭터별·맞은 쪽 섬광) = 꺼짐 — [§6.4](#64-스위치프로필-위치).
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
| 성공(오너 판정 → 서버 승인, 191) | 오너 화면 = 판정 성공 즉시(서버 안 기다림), 전역 슬로우·팀원 화면 = 첫 승인 | 히트스톱(A1) 시작 = ② 스타버스트 → ③ 화이트아웃 → ④ 스미어 → **섬광 순간**(오너 암전 즉시 끔·월드 섬광·팀원 암순응) = ⑤ 색수차 블러 → ⑥ 가라앉음 · 성공 슬로우 |
| 실패 | 오너 판정 실패·CC 끊김·판정 버림·시전 거부 | 슬로우 실패 복귀 + 오너 암전 0.2초에 걷음·빛 모임 정지. 섬광 없음 |
| 서버 거부(191) | 판정 성공했지만 서버 검증 실패 | 이미 튼 성공 섬광까지 즉시 끔 |
| 선착순 패배(191) | 첫 승인 뒤 0.05초 넘어 도착 | 서버 거부와 같음. 0.05초 안이면 **동시 성공** — 각자 섬광·성공 기록, 보스 효과 1회 · [interrupt-client-judge.md §5](interrupt-client-judge.md#5-서버-검증-interruptclaimserver--interruptclaimvalidator) |

### 0.2 흐름

```
간파 입력(오너 — 근접 Cast / 대쉬 TryUseDashInterrupt)
  ├─ [오너 클라] InterruptManager.BeginOwnerJudge (할 일 191)
  │     다음 LateUpdate: 누른 시각 표본으로 Predict → 시도 cue → Director.LocalOwnerAttempt(forecast) → 오너 상태 기계 InterruptOwnerPresentation
  │        강 = 오너 암전 0→1(ownerDimInDuration 0.1s, 실시간) + 빛 모임 시작 / 약·대상 없음 = 아무것도 안 켬
  │     판정 시각 표본이 쌓인 LateUpdate: Judge
  │        성공 → 판정 성공 cue(서버 안 기다림 — 성공 단계·섬광) → 신고(Claim) 1회
  │        실패 → 종료 cue(걷기 0.2s) / 판정 전 CC 끊김·판정 버림 → 종료 cue
  │     서버 결과: 거부 → 종료 cue(섬광까지 즉시 끔) / 승인 목록 → D10 함께 성공자 암전 제외
  │
  └─ 요청 RPC ─▶ [서버] 승인 → OnServerStart → InterruptManager.TryPredictServerAttempt(같은 Predict, 서버 이력·창 시간표)
        → Director.ServerAttempt → 대상 없음 = 슬로우 없음(발동 번호 0) / 약 = AttemptWeak / 강 = Attempt
        → 판정(ResolveHit = 피해만) → 그때까지 승인 없으면 ServerFail
        → 신고 승인(ServerOnClaimVerdict) — 첫 승인이면 ServerSuccess(성공 슬로우 + A1)
        │
        ▼ 각 피어 GlobalTimeScale.Started/Failed/ForcedReset — 유일한 구독자 = 디렉터
  [오너 화면]   cue 경로가 시도를 몰면 내 서버 시도 슬로우 목적으로 걷지 않는다(D4 — 결론은 오너 판정)
                 · 내 서버 Success Started → 판정 성공 cue 로 이미 튼 섬광이라 한 번 삼킨다(두 번 안 터짐)
                 · 성공 단계: 오너 암전 유지 → 섬광 순간에 즉시 끔(램프 없음)
               Failed(발동 번호) / 판정 전 종료(LocalOwnerEnded) → 걷기 · 오너 애니 Hit 이벤트 → 빛 모임 정지
  [팀원 화면]   남의 강 시도(Attempt) → 발동자 소켓에서 빛 모임 → 발동자 Hit(PeerCasterHit)·실패·성공·리셋에 정지
               시도 단계에 그 외 변화 없음
  [모든 화면]   Success → 적중 섬광 진행기(InterruptHitFlashSequencer, ②③④ = 렌더 프레임 수 · ⑤⑥ = 진행기 시계 초 §0.4)
                 프레임 0 ②(오너 화면 효과, 아래 조건) · 2 ③ · 4 ④ · 6 섬광 순간 = ⑤ 시작 = 오너 암전 끔 + 월드 섬광(FX_InterruptHitFlash)·라이트 + 암순응
                 · 섬광 순간 + 0.1s ⑥ · + 0.3s 끝(기본 실시간 — 칸을 스케일로 바꾸면 ⑤⑥ 만 성공 슬로우를 따라 늘어난다)
                 효과 칸(§0.6)이 꺼진 효과는 그리지 않을 뿐 시간표·섬광 순간 사건은 그대로
                 A4 성공 이펙트 칸은 Started 즉시(§2.4)
```

- **예측·판정 함수 하나**(할 일 191 — `InterruptManager.Predict`/`Judge` = `InterruptJudgeCalculator`) — 근접·대쉬 공용, 예측과 판정이 같은 계산.
  판정 시각 = 근접 `누른 시각 + Hit 지연`, 대쉬 `max(실제 대시 끝, 누른 시각 + Hit 지연)`. Hit 지연 = 클립 Hit 이벤트 시각 ÷ 지금 애니 속도. 대시 끝 = 공용 스윕 `PlayerDashPath`.
  창 = 서버 창 시간표(닫힘 예정·조기·늦은 닫힘). 옛 창 길이 추정·0.4초 규칙은 삭제.
- **대상 판정**(새 수치 칸 없음): ① 판정 앵커를 공격 방향(근접 = 시전 방향, 대쉬 = 대시 방향)으로 돌려 판정 위치(근접 = 그 자리, 대쉬 = 대시 끝)에 놓은 형상에 닿음
  ② 대상 정면 허용각 안(`InterruptTargetGeometry.FacesAttacker` — 23호 `counterFrontAngle` 등 기존 판정값, 정면 판정 없는 대상 = 180).
  HUD 강조 사거리(전방위 = 돌면 닿는 거리)보다 좁다. 원인·앵커 수치·재현은 [dash-interrupt.md §5](dash-interrupt.md).
  HUD 강조의 대상까지 거리 = 대상 콜라이더 **실제 표면** 수평 거리(`InterruptOmniRangeModel.HorizontalDistanceToCollider`, 할 일 139-4). 예전 AABB 거리는 대각선에서 최대 0.70m(23호) 짧아 강조가 실제보다 멀리서 켜졌다.
- **예측 시점 = 우클릭을 누른 순간**(매니저 이력의 누른 시각 표본). 대쉬 → 우클릭 순서만([dash-interrupt.md §2.1](dash-interrupt.md)).
- **대상 캐시** `InterruptTargetRegistry`(서버·클라 같은 규칙): 등록 = 대상 스폰(모든 피어 — 23호 = 보스전 시작 Spawn, 중간보스·스파링 봇 = `MonsterBase`, 허수아비 = `TrainingDummyInterrupt`),
  해제 = 디스폰·사망(MonsterBase Dead)·씬 언로드. 매니저는 이 캐시의 대상을 샘플한다. 서버 피해(`ResolveHit` 오버랩)는 캐시를 쓰지 않는다.
- **서버가 슬로우 강·약을 정한다**(`Time.timeScale` 은 전 피어 공용). 오너 연출(암전·빛 모임·섬광)의 결론은 **오너 판정**이다 — 서버 시도 슬로우 목적이 달라도 걷지 않는다(D4).
  오너가 약·대상 없음으로 봤는데 서버가 강이면 오너 화면은 암전·빛 모임 없이 강 슬로우·줌만 받는다(뒤늦게 켜지 않는다 — 약 → 강 올리기 `LocalOwnerUpgrade` 는 191 에서 삭제).
- **D10 `keepCoSucceededBright`**(디렉터, 기본 켬): 승인 목록을 받으면 함께 성공한 캐릭터도 내 암전에서 밝게 남긴다. 원격 클라는 승인이 섬광 순간 뒤에 오는 일이 많아 주로 호스트·저지연에서 보인다.

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

### 0.5 새 수치 칸 — `NetworkManager.prefab › InterruptProductionDirector` (슬로우·줌 4칸은 194 후속 2 부터 SO `InterruptSlowMotionSettings`, [§9.8](#98-슬로우-3단계-so--interruptslowmotionsettings-194-후속-2-2026-11-03))

| 칸 | 기본 | 누구 값 | xlsx |
|---|---|---|---|
| `interruptProfile.*`(강) — SO | scale 0.15 · hold 0.5 · enter 0.05 · exit 0.25 · failExit 0.1 · hitStop 0/0.02 | 서버 | 열 있음(옛 대상 `NetworkManager` → 병합 Export 로 `InterruptSlowMotionSettings`, §9.8) |
| `interruptWeakProfile.*`(약) — SO | scale 0.5 · hold 0.2 · enter 0.05 · exit 0.25 · failExit 0.1 · hitStop 0/0.02 | 서버 | 〃 |
| `interruptZoomFactor`(강 줌) — SO | 0.7 | 각 피어 | 〃 |
| `interruptWeakZoomFactor`(약 줌) — SO | 0.93 | 각 피어 | 〃 |
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
- Play 중 화면 오른쪽 위 OnGUI 박스(제목 버튼으로 접기). **11-06(할 일 238) 정리**: '화면 효과' 섹션은 `빛 모임`(팀원 강 시도 — 남의 화면) 토글 하나만 남겼다.
  오너 암전(세 단계 `ownerDim` 끔이라 안 켜짐)·②~⑥·월드 섬광·적중 지점 라이트·팀원 암순응 A2(V0 섬광 순간 경로에서만 쓰임 — V3 는 안 지남)·성공 이펙트 A4(세 Variant 칸이 비어 있음) 토글과 `전부 켬`·`암전만` 버튼은 삭제(위 표의 디렉터 칸 자체는 그대로 — 인스펙터). 전제 테스트 `지운_효과_줄의_전제_세_단계가_V3_이고_오너_암전_끔`.
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

- ~~오너 예측은 근사(창 복제값·관측 중앙값)~~ · ~~창 길이를 모르면 강~~ · ~~대쉬 예측은 직선~~ — **할 일 191 에서 해소.** 예측은 서버 창 시간표·공용 대시 스윕을 쓰고, 결론은 같은 함수의 오너 판정이다.
  남은 어긋남(보스 이동·창 시간표 변경·CC)은 QA 기록 `mismatch_reason` 으로 본다. 한계 목록은 [interrupt-client-judge.md §12](interrupt-client-judge.md#12-알려진-한계).
- **클라는 23호 제압 복제값이 없다**(취약만 안다) — 제압 중엔 창이 안 열려 판정 영향 없음.
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

> 🔹 **할 일 191 로 세션 중 선착순은 `InterruptManager` 장부(`InterruptClaimLedger`)가 정한다** — 서버 도착 기준 첫 승인 + **0.05초 안 = 동시 성공**(각자 연출·성공 기록, 보스 효과 1회), 그 뒤 = 거부.
> 아래 143 규칙 중 바뀐 것: ① 143 서버 장부(`InterruptWindowClaims`)는 피격으로 성공이 날 때만 기록한다 — 세션 중엔 피격으로 성공이 안 나므로 사실상 오프라인 경로 전용이다.
> ② 오너 예측은 매니저 `Predict` 라 로컬 장부(143-2)를 읽지 않는다 — 로컬 장부는 디렉터 피어 대상 재탐색·랩 미리보기에만 남았다. ③ '두 번째는 피해만'은 `ResolveHit` 의 '이미 승인된 대상엔 간파 표식 빼기'가 맡는다.
> 상세 [interrupt-client-judge.md §5](interrupt-client-judge.md#5-서버-검증-interruptclaimserver--interruptclaimvalidator).

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

### 2.1 슬로우 프로필 — SO `InterruptSlowMotionSettings` (시트 `SlowMotion`, Order 0)

> 할 일 118 C2 에서 `GlobalTimeScale` 에서 디렉터로 옮겼다. 194 후속 2(2026-11-03)에서 디렉터 → SO `InterruptSlowMotionSettings` 로 다시 옮겼다(필드 이름 그대로, 대상 열만 바뀜 — 병합 Export 절차 [§9.8](#98-슬로우-3단계-so--interruptslowmotionsettings-194-후속-2-2026-11-03)). 대쉬 간파 시도 전용 `interruptDashProfile`·`interruptDashWeakProfile`(처음 값 = 아래 강·약)이 같은 SO 에 있다.
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
xlsx: 강·약·성공 모두 열 있음(2026-11-03 확인, 옛 대상 `NetworkManager`).

### 2.2 오너 줌 — SO `InterruptSlowMotionSettings` (시트 `SlowMotion`, 194 후속 2)

| 필드 | 위치 | 기본 | 의미 |
|---|---|---|---|
| `slowMotionZoomFactor` | SO(옛 `CameraTargetSwitcher`, 카메라는 SO 참조) | 0.85 | 내가 일으킨 **성공** 슬로우 유지 단계 카메라 거리 배율 [0.1, 1] |
| `interruptZoomFactor` | SO(옛 디렉터) | 0.7 | 내가 일으킨 **강** 시도 슬로우 배율(근접·대쉬 공통) |
| `interruptWeakZoomFactor` | SO(옛 디렉터) | 0.93 | 내가 일으킨 **약** 시도 슬로우 배율(근접·대쉬 공통) |

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

**화면 전체에 고르게.** 대상 주변을 살리는 원(옛 `revealCenter`·`revealRadius` 0.18·`revealSoftness` 0.1)은 **2026-11-03 삭제**(옵션 아님) —
사용자가 '비네팅'으로 본 것이 이 원 밖 어둡힘이었다. 이제 휘도 임계만으로 밝은 픽셀을 남긴다. A2 팀원 암순응·제압 암순응 컷(§8) 둘 다 같은 패스라 둘 다 원이 없다.
xlsx `SlowMotion` 시트의 `revealRadius`·`revealSoftness` 행(A43·A44)은 병합 Export 때 "코드에 없어 버려졌다" 2개로 빠진다 — 그 전 테이블 Play 는 "필드가 없다" 오류.

**언제.** 할 일 132 단계 5 부터 성공 `Started` 즉시가 아니라 **적중 섬광 순간**(②③④ 뒤 = ⑤ 시작, 기본 성공 Started 6 프레임째)에 튼다. §17 부터 A2 기본 꺼짐.

**본인/타인.** `Started` 의 발동자 clientId 가 이 피어의 `LocalClientId` 면 `selfIntensity`(디렉터 개인 채널), 아니면 `othersIntensity`(공용 채널).
단 본인 화면에서 섬광 오버레이가 돌았으면(강 시도 성공) self 암순응은 생략한다 — 마스크 암전·섬광이 대신한다(`InterruptHitFlashRules.SelectDarkAdaptation`).
`InterruptDarkAdaptation` 은 구독하지 않는 수동 실행자다 — 디렉터가 `Play(viewerIsCaster)`·`Stop()` 을 부른다(원이 없어 시전자·대상 위치는 받지 않는다).

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

캐릭터별 기존 성공 연출(가붕이 `PaladinSkillView` 충격파 B3, 어쌔신 `AssassinSkillView` 성공 소켓 C2)은 코드가 그대로지만 132 단계 6 부터 **스위치가 꺼져** 나오지 않는다(새 적중 섬광이 대신).
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
- **대상 위치는 판정 순간 값.** 클라 화면의 대상은 보간 지연만큼 뒤에 있을 수 있어 `targetFx` 가 살짝 어긋나 보일 수 있다.
- 사람이 MPPM 으로 확인할 것: 본인/타인 강도 차이, 암순응이 화면 전체에 고르게(대상 주변 밝은 원 없이) 걸리는지,
  `casterFx`·`targetFx` 를 넣은 뒤 전 화면에서 각각 시전자·대상 위치에 재생되는지. 132 체계 제약은 [§0.8](#08-알려진-제약-132).

## 6. 간파 연출 디렉터 — `InterruptProductionDirector` (할 일 118)

> 설계안: `_lane-artifacts/MainProject-Worktree2/118/118-설계안.md`(레포 밖). 104 스위치(`InterruptPresentationToggles`)는 이 디렉터로 흡수·삭제됐다.
> 다른 담당 영역 변경 공유: [handoff-interrupt-director-vfx.md](handoff-interrupt-director-vfx.md)(민경) · [handoff-interrupt-director-target-flash.md](handoff-interrupt-director-target-flash.md)(경석).

### 6.1 구조

- **디렉터 = `NetworkManager.prefab` 루트의 MonoBehaviour 1개**(`GlobalTimeScale`·`InterruptDarkAdaptation` 옆). 코드 `Assets/1.Scripts/Player/InterruptProduction/`.
  스위치 12칸(cue 10 + A5 전투 표시 숨김 + 카메라 대상 중심(196) — A6·A7 은 11-04 할 일 211 로 삭제) + 강·약 시도/성공 슬로우 프로필 3개 + 132 오너 암전·적중 섬광 칸(§0.5)을 들고, 재생 지점은 **정적 API** 로 묻는다(인스턴스가 없으면 전부 꺼짐·no-op).
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
| 시도 | `TryStartSkillServer` → `ServerAttempt(예측 3상태)`(191: 예측 = `InterruptManager.TryPredictServerAttempt`) → 대상 없음 = 없음 / 약 = `interruptWeakProfile`(`AttemptWeak`) / 강 = `interruptProfile`(`Attempt`), 발동 번호 반환 | [오너] 매니저 시도 cue → `LocalOwnerAttempt`(오너 암전·빛 모임, §0.2) · `OnClientPlay` → `LocalAttempt` → B1(꺼짐) · 클립 이벤트 B2·C1 은 `AllowsAnimEffect(id)` · 남의 강 시도 = 팀원 빛 모임 |
| 판정 | `ResolveHit` 끝(피해만) → `ServerResolve` → 그때까지 승인이 없거나 **선점됨**이면 시도 슬로우 `ServerFail` | [오너] 매니저 판정 cue(성공 = 섬광 즉시 / 실패 = 걷기) · 애니 Hit → `LocalOwnerHit`(빛 모임 끝) · 팀원 `PeerCasterHit` |
| 성공 | 신고 승인 순간(`ServerOnClaimVerdict`) — 그 창의 **첫 승인만** `ServerSuccess` → 성공 슬로우(`interruptSuccessProfile`, A1 칸 = `hitStopOn`) → 시전자 아래 `IInterruptSuccessRelay` 마다 1회. 동시 승인자는 슬로우 없음 | `Started(Success)` → A4 즉시 · 적중 섬광 진행기 → 섬광 순간 월드 섬광·A2 · RPC 수신부(B3·C2·E1, 꺼짐)가 `PlayCue`. 내 오너 판정 성공 뒤 오는 내 Success 는 한 번 삼킨다 |
| 서버 결과(191) | 승인 목록 전 피어 / 거부 그 오너(named message) | [오너] 거부 = 종료 cue(섬광 즉시 끔) · 승인 목록 = D10 함께 성공자 암전 제외 |
| 실패 | 판정 전 종료(`OnEnd`)·앵커 누락 → `ServerFail` → 시도 슬로우 실패 복귀 | `Failed` → 오너 암전 걷기·빛 모임 정지(실패 단계 자체 연출 없음) · [오너] `OnEnd`·거부 → `LocalOwnerEnded` |

- 대쉬 간파(할 일 115)도 같은 사건 4개를 쓴다. 판정 위치는 실제 대시 끝(공용 스윕), 예측 시점은 우클릭 누른 순간 — [dash-interrupt.md §5](dash-interrupt.md#5-118-디렉터-사건-연결--새-rpc-없음192-부터-사건-종류-dash). 192 부터 성공 사건 종류가 Dash 로 실린다(§8).
- **오너 판정 cue(할 일 191)** — 매니저 `InterruptOwnerCueRouter` → 디렉터 `IInterruptOwnerCueSink`(`InterruptProductionDirector.OwnerJudgeCues.cs`, 재생 `InterruptOwnerJudgeCuePlayer`). cue = 시도 · 판정 성공 · 판정 실패 · 서버 거부 · CC 끊김 · 판정 버림 · 승인 목록. 표는 [interrupt-client-judge.md §6](interrupt-client-judge.md#6-연출-cue-4단계).
  D10 스위치 `keepCoSucceededBright`(기본 켬, `[DataTableIgnore]`, 각 피어 값)는 `[Header("동시 성공 — 개인")]` 아래.
- 호출 순서는 옛 코드와 한 줄씩 같다: 빗나감 Fail → 성공 Start → `ReportLag`. 캐릭터 relay 는 성공 Start 바로 뒤라 옛 정적 이벤트 구독보다 `ReportLag` 한 줄 앞이다(같은 프레임·같은 RPC 순서, 화면 차이 없음).
- 정적 `PlayerInterruptSkillBase.ServerInterruptSucceeded/Resolved` 는 **로직용으로 남아 있다**(튜토리얼·통계). 연출 구독자(팔라딘·어쌔신)만 relay 로 옮겼다.
- 슬로우가 거부돼도(컷신 잠금·씬 전환) relay·섬광 RPC 는 그대로 나간다 — 전송로를 바꾸지 않았다(설계 Q1-a). 그래서 그때 A2·A4 만 빠지고 B3·C2·E1 은 나온다(지금과 같음).

### 6.3 개인 / 공용 (보는 사람 기준)

| 연출 | 칸 | 채널 | 판단 |
|---|---|---|---|
| 시도(강·약)·성공 슬로우 · 히트스톱 | — · A1 | 서버 타임라인(전 피어 공용) | 채널을 거치지 않는다. `Time.timeScale` 은 피어 전역이라 개인 슬로우는 불가 |
| 오너 줌 | A3 | 개인 | 카메라가 매 프레임 `Director.OwnerZoomFactor(목적, 성공 배율)`·`Director.OwnerAwaitsStrongResult` 질의 → `SlowMotionOwnerZoom`(시도~성공 줌 한 번). 내 슬로우인지는 카메라가 본다 |
| 카메라 대상 중심(196 → 11-05) | `cameraCenter` | **공용(각 화면 자기 카메라)** | 카메라가 매 프레임 `Director.OwnerCameraCenterShift(추적 대상, cameraBusy)` 질의 — **제압 간파 성공 순간에만, 모든 화면**(오너 = 판정 cue, 팀원·보정 = 서버 성공 사건 제압 표식 bit0, [§10](#10-카메라-대상-중심-할-일-196)) |
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
| — | `slowMotionSettings` → SO `InterruptSlowMotionSettings` | 연결 | 슬로우 3단계 수치(194 후속 2, [§9.8](#98-슬로우-3단계-so--interruptslowmotionsettings-194-후속-2-2026-11-03)) — 대쉬 간파 시도·근접 간파 시도 강/약 프로필·시도 줌·성공 프로필·성공 줌. 옛 디렉터 칸 `interruptProfile`·`interruptWeakProfile`·`interruptZoomFactor`·`interruptWeakZoomFactor`·`interruptSuccessProfile` 는 이 SO 로 옮겼다 | 시도·성공 단계 `ServerStart`, 카메라 줌 (xlsx `SlowMotion` — 대상 열 `InterruptSlowMotionSettings`) |
| A1 | `hitStop` | **켬** | 성공 슬로우 맨 앞 정지(섬광 ② 가 같은 순간 시작) | 성공 단계가 `GlobalTimeScale.ServerStart(…, hitStopOn)` 로 넘긴다 — 꺼지면 이벤트의 `hitStopDuration` 0 |
| A2 | `darkAdaptation` | 꺼짐(§17) | 성공 섬광 순간 화면 휘도 암순응(팀원 others · 오버레이 없던 본인 self) — 튜닝 패널 '팀원 암순응' | 섬광 순간 개인/공용 채널 → `InterruptDarkAdaptation.Play` |
| A3 | `ownerZoom` | **켬** | 내가 일으킨 슬로우(강·약·성공) 동안 카메라 줌 | `CameraTargetSwitcher` → `Director.OwnerZoomFactor` — 꺼지면 배율 1 |
| A4 | `successFxSlot` | **켬** | 공용 성공 이펙트 칸(§2.4, base·Variant 칸은 지금 비어 있음) | 성공 단계 공용 채널 → `InterruptSuccessFx`(sink) |
| B1 | `paladinShieldGlow` | 꺼짐 | 팔라딘 간파 시도 방패 발광·루프 | `FirstMeleeInterruptSkill.OnClientPlay` → `Director.LocalAttempt` → 스킬(sink) → `PaladinSkillView.BeginInterruptGlow`. `OnEnd` 끄기·회수(`EndInterruptVfx`)는 늘 돈다 |
| B2 | `paladinShieldCharge` | 꺼짐 | 팔라딘 방패 기 모으기(클립 `Garen_Parry` 의 `ShieldCharge`) | `EffectAnimEvents.PlayEffect`·`StartEffect` → `Director.AllowsAnimEffect(id)` |
| B3 | `paladinWave` | 꺼짐 | 팔라딘 성공 충격파 | `PaladinSkillView.PlayInterruptWave`(RPC·오프라인, 2026-11-02 PlayerShieldVfx → PaladinSkillView) → `Director.PlayCue(PaladinWave, …, this)` |
| C1 | `assassinInsightAttempt` | 꺼짐 | 어쌔신 기 모으기·튀기기·올려치기(클립 `Attack_Up_01`) | `EffectAnimEvents.PlayEffect`·`StartEffect` → `Director.AllowsAnimEffect(id)` |
| C2 | `assassinInsightSuccess` | 꺼짐 | 어쌔신 간파 성공 이펙트 | `AssassinSkillView.TryPlayInsightSuccess`(대기·올려치기 프레임 맞추기는 View) → `Director.PlayCue(AssassinInsightSuccess, …, this)` |
| E1 | `targetFlash` | 꺼짐 | 맞은 쪽 섬광 `Interrupt_Flash` | 23호·건틀릿봇·스피너봇·월봇·허수아비·스파링 봇 `PlayInterruptFlashRpc` → `Director.PlayCue(TargetFlash, …, this)` |
| A5 | `hideCombatUi`(옛 `hideDamageNumbers` — `FormerlySerializedAs`, 프리팹 줄 그대로) | **켬** | 전투 표시 숨김(154 → 179 후속 2·4) — 내 강 시도 암전 시작 ~ 성공 섬광 ⑥ 가라앉음 끝(실패·판정 전 종료·예측 빗나감이면 오너 암전 걷기 끝) 이 화면의 **피해 숫자 캔버스(보스 포함)·머리 위 체력바(플레이어·몬스터·보스 모두 — 179 후속 4. 화면 고정 보스 HP 바 `BossHealthHUD` 는 그대로)·오너 조준 표시 쿼드(렌더러만 끔)** 를 숨긴다. 생성·수명·게이지 값은 그대로. 숨긴 조준 쿼드는 그려지지 않아 암전 마스크(비트 4)에도 안 남는다(전에는 플레이어 루트 태깅으로 비트를 받아 흰 사각형 실루엣). 일반 간파의 팀원 화면은 그대로(암전·②~⑥ 없음) | 상태에서 바로 읽는 값(pull) — `FloatingDamageSpawner`·`UnitOverheadHealthBar`·`PlayerAimIndicator` 의 `LateUpdate` → `Director.CombatUiHidden` = `InterruptProductionRules.ShouldHideCombatUi(칸, State == AttemptStrong ∨ DimIntensity > 0 ∨ 섬광 진행기 OverlayActive)` (제압 섬광 시퀀스 항은 11-04 할 일 211 로 삭제). 따로 복원할 상태가 없어 끝·실패·강제 리셋·디렉터 꺼짐이면 다음 프레임에 보인다(조준 쿼드는 컴포넌트 OnDisable 에도 켬). cue(`InterruptProductionCue`) 아님 — 런타임 값은 `director.HideCombatUi` |
| A6·A7 | (삭제 — 옛 `suppressFlash`·`suppressFlashTargetOnly`) | — | **11-04 할 일 211 로 삭제** — 제압 간파 = 일반·대쉬와 같은 적중 라이트 V3(`suppressTier`, §8.3). 섬광 시퀀스·섬광 범위는 Legacy([§7](#7-제압-간파-애니메이션-섬광-할-일-179) 기록용), 프리팹 줄도 지웠다 | — |
| — | `cameraCenter`("성공 — 개인") | **켬**(코드 기본값) | 카메라 대상 중심(196 → 11-05, [§10](#10-카메라-대상-중심-할-일-196)) — **제압 간파 성공 순간** 모든 플레이어 카메라 중심을 간파 대상 쪽으로(일반·대쉬 간파·클릭 순간은 안 움직임). 줌(A3)과 독립. 끄면 그 화면은 안 움직임 |

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
- **나머지 11칸(A5·카메라 대상 중심 포함) = 각 피어 자기 인스턴스 값**(동기화 없음, 설계 Q8-a) → MPPM 가상 플레이어 화면은 호스트에서 바꿔도 안 바뀐다.
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
할 일 154·179 후속 2·4: `InterruptDamageNumberHideTests`(A5 — 강 시도 시작·성공 ⑥ 끝·실패 걷기 끝·약/대상 없음·오너 아님·칸 끔·기본값 켬 · 일반 강제 리셋 복원·머리 위 체력바 플레이어·몬스터 모두 숨김·끝 복원·조준 쿼드 렌더러 끔/켬).
격리는 메뉴 밖 `DebugInterruptLabIsolationTests`. 대상 정면 판정은 Dash 어셈블리(`BeaverLobby.Player.Dash.EditModeTests` — `InterruptTargetGeometryTests`), 앵커 형상 대상 검사는 `HitboxAnchorShapeTests`(Assembly-CSharp-Editor).
할 일 191: 예측·판정·검증·오너 판정 cue 는 `Tools/Tests/간파 판정·검증 EditMode 테스트 실행`(`InterruptOwnerJudgeCueTests` 등, [interrupt-client-judge.md §11](interrupt-client-judge.md#11-테스트)). 옛 `DashInterruptPredictionTests` 는 삭제.

## 7. 제압 간파 애니메이션 섬광 (할 일 179)

> 🔸 **Legacy — 11-04(할 일 211) 제압 간파 = V3, 시퀀스는 Legacy.** 이 절은 기록용이다. 제압 간파 성공은 이제 일반·대쉬와 같은 적중 라이트(`suppressTier` 기본 V3·암순응 나, 오너 암전 없음 — [§8](#8-간파-연출-4단계-할-일-192)·[§9.7](#97-제압-간파--v3-할-일-211-2026-11-04)).
> 옮긴 곳(git mv, `.meta` guid 그대로): 코드 `Player/InterruptProduction/Legacy/SuppressFlash/`(`SuppressFlashPlayer`·`SuppressFlashScreenOutput`·`SuppressFlashSequence`) · 셰이더·상태·렌더 패스 `Rendering/DarkAdaptation/Legacy/`(`SuppressFlash.shader`·`SuppressFlashState`·`SuppressFlashPass` — 렌더 기능에서 빼 단독 클래스) · SO·재질 `Assets/5.VFX/Interrupt/Legacy/SuppressFlash/` · 테스트 `Player/InterruptProduction/Editor/Legacy/SuppressFlashTests`(슬로우 모션 메뉴에서 뺌, 테스트 러너 전체 실행엔 남음).
> 끊은 연결: 디렉터 A6 `suppressFlash`·A7 `suppressFlashTargetOnly`·`suppressFlashSequence`·`suppressFlashMaterial`·`reduceFlash` 칸(프리팹 줄도 삭제)·PlayerPrefs `Interrupt.ReduceFlash`·단계 칸 `sequenceHitLight`·오너 제압 예측 `IInterruptSuppressForecast`(23호 구현 포함)·`DarkAdaptationFeature` 의 섬광 패스·A5 의 시퀀스 항·랩 A6 토글/A7 버튼. 남긴 것: 표식(`SuppressInterruptMark` — 파일만 밖으로 분리)·슬로우 Start 이벤트 제압 비트·랩 '허수아비 간파 = 제압 간파' 토글.
> 다시 켜려면 위 연결을 되살려야 한다(179 원본 = 211 이전 커밋). 아래 본문 경로·칸 이름은 179 당시 그대로다.

> 사양: `_lane-artifacts/common/179-suppress-interrupt-flash/SPEC.md`(2026-11-02 grill 확정, 레포 밖). 레퍼런스 = `_lane-artifacts/references/interrupt/user-ref-2026-11-02-flash/`(참고만, 텍스처로 쓰지 않음).

**제압 간파** = 23호 제압 칸이 1칸 남았을 때 성공한 간파(그 간파로 제압 진입). 이때만 일반 성공 섬광 ②~⑥ 대신 **애니메이션 섬광 시퀀스**(보라 3단 단색 · 네거/포지 교대 · 십자 빛기둥 + 스파이크 별 · 집중선 · 색수차, 약 0.6초 — 레터박스·비네팅 없음, 11-03 삭제)를 튼다. 일반 간파는 §0 그대로.

### 7.1 누가 보나

| | 오너(제압 간파 성공자) | 다른 플레이어 |
|---|---|---|
| 132 마스크 암전(강 시도) | O — 시퀀스 첫 프레임에 걷힌다 | X |
| 섬광 시퀀스 | O | O(각자 화면, 기준점 = 자기 카메라의 보스 적중점 화면 좌표) |
| 월드 섬광(`showWorldFlash`·`showHitLight` 칸) | 시퀀스 첫 프레임 | 시퀀스 첫 프레임 |
| 암순응(A2) | X | X — 시퀀스가 화면을 통째로 3단 색으로 바꾼다 |
| A5 전투 표시 숨김(피해 숫자·머리 위 체력바(플레이어·몬스터·보스)·조준 표시) | 시퀀스 끝까지 | 시퀀스 끝까지(179 후속 2·4) |

### 7.2 흐름 — 새 RPC 없음

1. [서버] 23호 `EnterCounterSuccess` 의 칸 0 분기가 `InterruptProductionDirector.ServerMarkSuppressInterrupt()` (표식 1줄 — 판정 무수정).
2. [서버] 같은 판정 호출의 `ServerSuccess` 가 표식을 소비(`SuppressInterruptMark`, **같은 프레임만** 유효) → 사건 `SuppressInterrupt` → 성공 슬로우 `ServerStart(…, suppressInterrupt)`.
3. 슬로우 Start 이벤트(`SlowMotionEvent.SuppressInterrupt`, +1바이트, 기존 named message)로 전 피어에 간다 → `GlobalTimeScale.CurrentSuppressInterrupt`.
4. [각 피어] 디렉터 `HandleStarted(Success)` 가 `PlaysSuppressFlash` = `SuppressFlashRules.ShouldPlay`(성공 ∧ 표식 ∧ A6 `suppressFlash` 칸 ∧ 컷 데이터 있음)면 `BeginSuppressFlash`, 아니면 일반 ②~⑥.
5. `SuppressFlashPlayer`(순수 로직)가 매 프레임 `Time.unscaledTime` 으로 진행 → `SuppressFlashScreenOutput` → `SuppressFlashState` → `DarkAdaptationFeature` 의 `SuppressFlashPass`.

슬로우가 거부되면(컷신 잠금·씬 전환) 일반 섬광처럼 시퀀스도 없다. 성공 relay(B3·C2)는 표식이 실린 사건을 받지만 지금은 쓰지 않는다.

### 7.3 컷 데이터 — `SuppressFlashSequence`(`Assets/5.VFX/Interrupt/SuppressFlash/SuppressFlashSequence.asset`)

- 재생: `frameRate` 24 × `holdFrames` 2 = **초당 12장**, 실제 시간(슬로우·히트스톱 무관). 컷 안 시작→끝 값도 홀드 단위 계단.
- 컷 칸: `frames`·`tone`(Negative/Positive/Return)·`cross`(None/Single/Grid)·`starSize`·`speedLines`·`aberration`→`aberrationEnd`·`hatching`·3단 색 `dark`/`mid`/`light`(#1C1930·#7D77A0·#ECE9F6).
- 기본값 = 사양 표 7컷(2·2·2·2·2·2·3 = 15프레임 = 0.625초). 레터박스 칸(`letterbox`·`letterboxEnd`·`letterboxHeight`)은 11-03 삭제(사용자 요청 — 옵션 아님). 인스펙터 메뉴 Reset = 사양 표.
- 모양 칸(화면 높이 단위): 빛기둥 굵기·가늘어짐·울퉁불퉁·최소 굵기, 격자 칸·격자 별, 별 반지름·스파이크 수, 집중선 수, 방사 블러, 3단 경계, 바탕 밝기, 빗금 간격(px), 색수차 배율, 섬광 줄이기 대비.
- **Play 중 SO 값을 바꾸면 다음 렌더 프레임부터** 반영(진행기가 프레임마다 읽는다).

### 7.4 디렉터 칸 — `NetworkManager.prefab › InterruptProductionDirector` "성공 — 제압 간파 섬광"

켜기/끄기 스위치는 다른 스위치와 같은 자리("성공 — 공용")의 **A6 `suppressFlash`**(§6.4). 끄면 제압 간파 성공도 일반 간파 흐름 그대로 —
②~⑥ 오버레이(내 성공 ∧ 오너 암전 켜짐), 섬광 순간 암전 걷힘·월드 섬광·암순응(A2), A5 는 ⑥ 끝까지. Play 중 바꾸면 **다음 성공부터**(성공 시작 순간 읽음).

| 칸 | 기본 | 뜻 |
|---|---|---|
| `suppressFlash`(A6, "성공 — 공용") | 켬 | 제압 간파 섬광 시퀀스. 끄면 제압 간파도 일반 섬광. 섬광 줄이기와 독립 |
| `suppressFlashTargetOnly`(A7, "성공 — 공용") | 끔 | 섬광 = 대상 내부만 — 켜면 시퀀스를 간파 대상 실루엣 안에만(§7.5). 재생 시작 순간 읽음. A6·섬광 줄이기와 독립 |
| `suppressFlashSequence` | 위 에셋 | 비면 일반 섬광 |
| `suppressFlashMaterial` | `M_SuppressFlash.mat` | `Hidden/Rendering/SuppressFlash`. 비면 그리지 않음(시간표·암전 걷힘은 돈다) |
| `reduceFlash` | 끔 | **섬광 줄이기** 기본값 — 켜면 반전 끔(포지티브도 네거 바탕) + 대비 `reducedContrast` |

섬광 줄이기 실제 값 = PlayerPrefs `Interrupt.ReduceFlash`(0/1)가 있으면 그것, 없으면 칸(A6 와 독립 — A6 꺼짐이면 시퀀스가 안 도니 쓰이지 않을 뿐). `Director.ReduceFlash` setter 가 둘 다 쓴다. **설정 UI 는 없다** — 나중에 설정 화면이 이 프로퍼티를 쓰면 된다.

### 7.5 렌더 — 셰이더 절차 생성만

- `DarkAdaptationFeature` 에 패스 추가(렌더러 에셋 수정 없음 — 재질은 디렉터 칸에서 `SuppressFlashState` 로 온다). **후처리 뒤**(`AfterRenderingPostProcessing`)라 톤매핑이 3단 색을 바꾸지 않는다. HUD(오버레이 캔버스)는 그 위에 그대로 보인다.
- 실루엣 마스크: 대상 렌더러에 **렌더링 레이어 비트 7**(`SuppressFlashMaskLayers`)을 132 태거(`InterruptOwnerMaskTagger(mask)`)로 붙이고 덮어쓰기 재질(패스 1)로 R8 RT 에 흰색 칠 — 깊이 판정 없음(벽 뒤도 납작 실루엣). 오너 암전 비트 4 와 나눠 서로 원복이 지우지 않는다.
- 합성(패스 0): 방사 블러 흑백 → 네거 바탕 + 쐐기 집중선 + 울퉁불퉁 십자(최소 굵기 아래는 지움) + 칸마다 길이·핵이 다른 스파이크 별(원·고리 아님) + 흰 실루엣 → 포지티브는 통째 뒤집기 → 대비 → 3단 색 → 빗금(실루엣 안 사선) → 복귀 섞기 → 가장자리 색수차(셰이더 안 RGB 분리). 레터박스·비네팅(가장자리 어둡게)은 없다(11-03 삭제 — `_SFFx.w` 는 미사용 0).
- 대상 목록: 발동자 플레이어 오브젝트 + 적중점 6m 안 가장 가까운 간파 대상(`InterruptTargetRegistry`). 191 동시 성공자는 `CollectSuppressSubjects` 목록에 더하면 된다.
  같은 함수가 간파 대상만 따로(`targets`) 모은다 — 동시 성공자는 거기에 넣지 않는다.
- **A7 섬광 = 대상 내부만(179/192 후속, 기본 끔)** — 새 렌더링 레이어 비트 없이 **비트 7 을 그대로** 쓴다: 켬이면 마스크 루트를 간파 대상만으로 골라(`SuppressFlashRules.SelectMaskRoots`) 비트 7 마스크 RT 가 곧 '그릴 범위'가 된다.
  합성 패스는 같고 `_SFMask.x = 1`(`SuppressFlashParams.Mask`)만 다르다 — 마스크 밖 픽셀은 원래 화면(카메라 컬러 그대로 — 오너 암전 덮개는 그 위 UI 캔버스라 그대로 덮인다), 안은 위 합성 순서 그대로(색 반전·3단 색·십자·별·집중선·색수차). 마스크 자체가 실루엣이라 **흰 실루엣 칠·빗금은 끈다**(켜 두면 안이 통째로 한 색이 된다).
  대상을 못 찾으면(`SelectMaskMode` — 6m 안 대상 없음) 전체 화면으로 돌아간다(마스크가 비면 아무것도 안 보이므로). 바뀐 칸은 재생 시작 순간만 읽는다.
  비트 사용처 확인(2026-11-03): 0 조명 · 1 데칼 · 2·3 벽 뒤 실루엣 · 4 오너 암전(132) · 4·5·6 보스 처치 연출(그 연출 동안만) · 7 제압 섬광 · 8 Ground — A7 은 비트를 늘리지 않는다. 조준 표시 쿼드는 A5 가 렌더러를 꺼서 원래 비트 7 에 안 남고, A7 켬이면 플레이어 루트를 아예 태깅하지 않는다.

### 7.6 알려진 한계

- 색수차는 132 ⑤ Volume 을 재사용하지 않고 셰이더 안에서 만든다 — ⑤ 는 후처리(톤매핑 전)라 3단 색 양자화가 무지개를 지우기 때문.
- HUD·피해 숫자(팀원 화면)는 시퀀스 위에 보인다.
- 시퀀스 뒤 제압 암순응 컷은 일반 암순응 렌더(`InterruptDarkAdaptation`)를 쓴다 — 화면 전체에 고르게 어두워진다. 예전엔 대상 주변 원(`revealRadius` 0.18) 밖만 어두워 비네팅처럼 보였는데 2026-11-03 원을 삭제했다(§2.3).
- 렌더링 레이어 Everything 인 렌더러(일부 VFX)는 비트 7 을 이미 갖고 있어 실루엣으로 칠해진다(A7 켬이면 그 VFX 자리에도 섬광이 그려진다). 태깅 뒤 생긴 렌더러는 빠진다.
- A7 대상 내부 마스크도 깊이 판정이 없다 — 벽·기둥 뒤에 가려진 대상 부분에도 섬광이 그려진다. 가장자리는 마스크 픽셀 경계 그대로(부드럽게 하지 않음).
- 표식은 23호 칸 판정만 낸다 — 훈련 스파링 봇(`TrainingSparringBot`)·기믹 보상 제압·구출 제압은 일반 섬광.
- 시퀀스 중 새 일반 성공 섬광이 오면 둘 다 그려질 수 있다(시퀀스는 카메라 컬러, ②③④ 는 오버레이 캔버스).

### 7.7 테스트

`Tools/Tests/슬로우 모션 EditMode 테스트 실행` 의 `SuppressFlashTests` — 사양 표 기본값·24fps 프레임 내림·2프레임 홀드·컷 전환/끝·포지티브 뒤집기·섬광 줄이기·복귀 계단·진행기(실제 시간·멈춤·재시작·SO/옵션 Play 중 반영)·프로필 선택·A6 켬(시퀀스)/끔(②~⑥)·Play 중 다시 켜기·섬광 줄이기와 독립·컷 데이터 없음·서버 표식 같은 프레임만·사건 사본·Start 이벤트 플래그 직렬화/세션·셰이더 파라미터·비트 7 태깅/원복·에셋·프리팹 연결.
진행기 시작 순간 고른 시퀀스 유지(재생 중 칸을 바꿔도 다음 성공부터). 랩 A6 토글은 `DebugInterruptLabTests`.
A7(179/192 후속): `SelectMaskMode` 4경우(칸 × 대상 찾음)·`SelectMaskRoots`(전체 = 실루엣 대상 전부, 내부 = 간파 대상만)·진행기가 시작 순간 범위를 출력에 넘김·화면 출력이 대상만 비트 7 태깅/원복·셰이더 파라미터 `Mask.x`·칸 기본 끔(코드·프리팹)·A6 와 독립. 랩 A7 버튼 글자는 `DebugInterruptLabTests`.

### 7.8 DebugInterruptLab 토글 (179 후속 3)

- 2026-11-03 사용자 결정: 레퍼런스 B 시퀀스(포트폴리오 60fps 5컷, `a294d1a7`)는 **폐기** — 이 절의 보라·흰색 7컷(A)이 제압 간파 섬광이다. B 전용 SO 칸·셰이더 분기·디렉터 A/B 선택은 걷어 냈다(셰이더·렌더 파라미터는 179 원본 `fd33cd3a` 와 같다).
- 튜닝 패널 **"제압 간파 섬광 (A6)"** 토글 — 이 피어 디렉터의 A6 런타임 값만(끔 = 제압 간파도 일반 ②~⑥). 켜면 아래 재현 토글도 켜진다.
- 버튼 **"섬광 범위 (A7): 전체 화면 / 대상 내부만"**(179/192 후속) — 지금 상태를 글자로 보여 주고 누르면 반대로. 이 피어 디렉터의 A7 런타임 값만, 다음 재생부터.
- 허수아비엔 제압 칸이 없어 토글 **"허수아비 간파 = 제압 간파(제압 직전)"** 가 허수아비 서버 간파 성공 이벤트에서 23호와 같은 `ServerMarkSuppressInterrupt` 를 부른다(허수아비·판정 코드 무수정, 호스트에서만 효과).

## 8. 간파 연출 4단계 (할 일 192)

> 사용자 사양: 실패 = 약한 줌인, 끝 / 간파 = 강한 줌인 + 섬광(포인트라이트·환경광 끄기) / 대쉬 간파 = 강한 줌인 + 섬광(암순응) + 블룸 / 제압 간파 = 암전 + 애니메이션 섬광(암순응도 애니메이션) + 대쉬 간파.
> 확정(grill Q1~Q8): 암전 = 제압만(Q1) · 슬로우 강/약 그대로(Q2) · 실패 규칙(Q3) · **Q4(일반 포인트라이트 섬광)·Q5(대쉬 암순응·블룸)는 보류 — 지금은 기존 ②~⑥** · 제압 = 대쉬 세트 포함(Q6) · 제압 암순응 컷(Q7) · 누가 보나(Q8).
> 코드: `InterruptPresentationTier.cs`(단계·종류·단계 설정·규칙 순수 함수) · 디렉터 · 테스트 `InterruptPresentationTierTests`.
> 🔸 **11-04(할 일 211) 제압 간파 = V3, 시퀀스는 Legacy.** 제압 간파도 일반·대쉬와 같은 적중 라이트 V3·**암전 없음**(Q1 '암전 = 제압만' 은 폐기 — 지금은 어느 단계도 기본 암전이 없다). 아래 표·절은 211 기준으로 고쳤다. 제압 단계(`Suppress`)·표식 비트는 남아 단계 칸 `suppressTier` 로 따로 고를 수 있다.
> 🔸 **11-05(할 일 219) 단계별 강화 — 1단계 일반 < 2단계 대쉬 < 3단계 제압.** 세 단계 모두 V3 이되 대쉬·제압은 **V3 를 복제해 세게 한 SO·프리팹**을 쓴다(§9.9). 3단계만 **제압 전용 성공 슬로우**(더 긴 히트스톱·슬로우, §9.8)·**화이트아웃**(스위치). 오너는 **판정 순간** 대상 제압 칸이 마지막 1칸인지 복제값으로 보고 그 자리에서 3단계를 튼다 — 서버 표식은 보험(§8.2).

### 8.1 단계표

| 단계 | 언제 | 오너 화면 | 팀원 화면 | 줌(오너만) |
|---|---|---|---|---|
| **실패** | 약 예측 / 강 예측의 판정 실패·끊김·서버 거부 | 약 예측 = 아무것도 안 켬(약한 줌만). 강 예측 판정 실패 = **그 순간 연출 종료**(191 D4) | 없음 | 약 = 약 시도 줌, 강 = 결론까지 강 줌 |
| **간파**(근접) | 근접 간파 성공 | 암전·빛 모임 **없음** · 섬광 = 버전(§9, 기본 **V3 레퍼런스 맞춤**·암순응 나 — 11-03). V0 이면 강 예측 ②~⑥ · 약 예측 = 섬광 순간만 | V1~V3 = 월드 연출 전부(라이트·불꽃·파편·빛줄기·블룸·암순응). V0 = 월드 섬광·라이트·암순응(A2) | 강 |
| **대쉬 간파** | 대시에 실린 간파 성공 | 간파와 같은 규칙, 섬광 기본 **V3**(11-03 — 전에는 V2) | 간파와 같음 | 강 |
| **제압 간파** | 제압 칸 마지막 1칸을 깎은 성공(근접·대쉬 무관) · **북진 압박 3번째 간파**(할 일 232 §8.7) | 간파와 같은 규칙 — 암전·빛 모임 **없음**, 섬광 기본 **V3**·암순응 나(11-04 할 일 211 — 전에는 암전 + 섬광 시퀀스 + V3 + 제압 암순응 컷, §7 Legacy) | 간파와 같음 | 강 |

- 슬로우·줌은 그대로(Q2) — 줌 세기는 슬로우 목적(강·약 시도 / 성공)이 고른다. 단계 설정에 줌 칸은 없다.
- A5 전투 표시 숨김: 암전 없는 강 시도는 시도 중 숨기지 않는다. 섬광 ②~⑥(V0) 동안은 그대로 숨긴다.

### 8.2 단계를 고르는 곳 — 새 RPC 없음

- **사건 종류 `InterruptVariant`(Normal/Dash)**: 서버 성공 사건에 싣는다 — 세션 = 신고(`InterruptClaim.ViaDash`, 191 경로) → `ServerOnClaimVerdict(…, viaDash)`, 오프라인 = 시전 시작의 대쉬 여부(`castViaDash`).
- **피어 전달**: 슬로우 Start 이벤트의 179 **표식 바이트 하나**에 비트로(비트 0 = 제압, 비트 1 = 대쉬 — `SlowMotionEvent.PackInterruptFlags`, 직렬화 크기 그대로). 디렉터 `HandleStarted` 가 `GlobalTimeScale.CurrentSuppressInterrupt`·`CurrentDashInterrupt` 를 읽어 `InterruptPresentationTierRules.Select`(실패 / 제압 &gt; 대쉬 &gt; 일반).
- **오너 시도 예측 = 일반·대쉬만**(11-04 할 일 211): 시도(입력) 순간에는 제압을 예측하지 않는다(암전 판단용 — 기본 암전 없음). 제압 예측은 219 부터 **판정 순간**에만 한다(아래). 입력 순간 `Select(true, VariantOf(viaDash), false)` 의 단계 설정이 암전 켬이고 강 예측일 때만 `InterruptOwnerPresentation.Predict(…, dims: true)` — 기본 단계 설정은 모두 암전 끔이라 강 상태(줌 붙잡기·②~⑥ 조건)만 남는다.
- **오너 판정 성공(191 cue)**: 예측 단계의 섬광(`SuccessLook`)으로 — V1~V3 면 바로 적중 라이트 연출(§9), V0 면 ②~⑥. 뒤이은 내 서버 성공은 한 번 삼킨다.
  사건에 근접/대쉬 종류(`variant`)를 싣는다 — 194 전에는 cue 경로 사건이 늘 Normal 이라 대쉬가 일반 단계로 계산됐다.
  서버 거부면 적중 라이트를 즉시 끈다.
- **오너 판정 순간 제압 예측(할 일 219, 11-05 사용자 결정 — "간파 게이지가 한 칸인 건 이미 알고 있는 거 아냐? 서버는 보험용")**: 판정 성공 cue 에서 `OwnerJudgeTier(예측 단계, PredictsSuppress(cue.Target))` —
  대상이 `IInterruptSuppressForecast` 를 구현하고(23호 `WouldSuppressOnInterrupt` = 기믹 중 아님 ∧ 복제된 남은 칸을 간파 칸 분기 `BossCounterProgress.ResolveSegments` 에 넣으면 제압) 참이면 **그 자리에서 3단계**(제압 V3 에셋·화이트아웃·카메라 중심 복귀 = 제압 성공 프로필)를 튼다.
  **서버 표식은 보험**: 내 서버 성공 슬로우의 단계(표식 bit0)가 판정 순간 튼 단계와 **다르면** 서버 단계로 다시 튼다, 같으면 그대로(`ServerCorrectsOwner` — 새 RPC·바이트 없음). 클라 오너는 복제 지연·구출 성공·기믹 판정을 서버만 정확히 알아 틀릴 수 있다 — 그때 보정된다.
  랩: '허수아비 간파 = 제압 간파' 토글이 켜진 동안 `DebugSuppressForecastOverride` 로 허수아비도 제압으로 예측한다.

### 8.3 디렉터 칸 — "간파 연출 단계(할 일 192)"

| 칸 | 기본 | 뜻 |
|---|---|---|
| `normalTier` | 암전 끔 · 섬광 `HitLightReference`(V3) · 암순응 나 | 간파(근접) 성공(194 §9, 11-03 V1 → V3) |
| `dashTier` | 암전 끔 · 섬광 `HitLightReference`(V3) · 암순응 나 | 대쉬 간파 성공(194 §9, 11-03 V2 → V3) |
| `suppressTier` | 암전 끔 · 섬광 `HitLightReference`(V3) · 암순응 나 | 제압 간파 성공(11-04 할 일 211 — 일반·대쉬와 같은 버전 선택, §9.7). 옛 `SuppressSequence`·`sequenceHitLight` 는 삭제(enum 값 1 은 비워 둠 — 들어 있으면 V0) |
| `hitLightLookDash`·`hitLightVfxDash` | `V3/Dash/` SO·프리팹 | 2단계 대쉬 V3 에셋(할 일 219, §9.9) — 둘 중 하나라도 비면 1단계(`hitLightLookReference`·`hitLightVfxReference`) |
| `hitLightLookSuppress`·`hitLightVfxSuppress` | `V3/Suppress/` SO·프리팹 | 3단계 제압 V3 에셋(할 일 219) — 둘 중 하나라도 비면 1단계 |
| `suppressWhiteout` | 켬 | 3단계 화이트아웃 스위치(본인 화면) — 끄면 제압 SO `whiteoutFrames` 와 무관하게 안 나온다 |

### 8.4 제압 암순응 컷 (Q7) — Legacy (11-04 할 일 211)

> 11-04 제압 간파 = V3, 시퀀스는 Legacy — 아래는 179·192 당시 기록이다. 컷 데이터·진행기는 `Legacy/SuppressFlash/` 에 남고 런타임에서 돌지 않는다. 암순응 출력 인터페이스 `ISuppressDarkAdaptOutput` 은 V1~V3 진행기가 계속 써서 밖(`InterruptProduction/`)으로 분리했다.

- 섬광 시퀀스 **복귀 컷 뒤**에 이어서 컷마다 `duration`(초, 실제 시간 — 시퀀스 프레임 속도 무관)·`darkness`(암순응 렌더 강도 [0, 1])를 그대로 붙잡는다. 기본 = **어두움 1.0 · 0.15초 → 중간 0.5 · 0.15초 → 원래 색**. 비우면 컷 암순응 없음.
- 렌더 = 기존 암순응(`InterruptDarkAdaptation` — 밝은 빛·이펙트는 남고 어두운 픽셀만 화면 전체에서 고르게 낮춤 — 대상 주변 원 없음(11-03 삭제), 임계 칸은 그 컴포넌트). 곡선(A2 팀원 암순응)은 멈추고 값만 붙잡는다(`ShowDarkAdaptCut`) — A2 칸·곡선 수치와는 무관.
- 모든 피어 같은 값(Q8 공용). 강제 리셋·새 제압 간파면 즉시 걷는다.

### 8.5 DebugInterruptLab 버튼 (튜닝 패널 "간파 단계")

- **지금 단계 표시**: `내 마지막`(오너 — 시도 예측 단계 → 판정·서버 결과로 갱신, 실패면 실패) · `받은 성공`(이 화면이 마지막으로 받은 성공 단계, 남의 것 포함).
- **재현 버튼 4개**(`실패`·`간파(근접)`·`대쉬 간파`·`제압 간파`): 이 화면에서만, 서버·슬로우·판정 없이 오너 연출을 판정 cue 경로 그대로 돌린다(시도 → 0.4초 → 결론, 대상 = 가장 가까운 간파 대상). **슬로우가 없어 줌은 안 보인다** — 줌·팀원 화면은 실제 간파로 본다.
- "허수아비 간파 = 제압 간파" 토글(§7.8) 유지 — 호스트에서 허수아비 성공을 제압 간파로 표식(받은 성공 = 제압 · `suppressTier` 연출 확인용). 섬광 A6 토글·A7 버튼은 11-04(할 일 211) 삭제. 219 부터 이 토글이 켜진 동안 오너 판정 순간 제압 예측도 허수아비를 제압으로 본다(`DebugSuppressForecastOverride` 재도입) — 제압 성공 슬로우(서버)는 호스트 토글일 때만.
- **"제압 화이트아웃 (219)" 토글** — 디렉터 `suppressWhiteout` 런타임 값(다음 성공부터). 밑 줄에 `V3 에셋 — 대쉬 전용/1단계 대체 · 제압 전용/1단계 대체`(단계 칸이 비어 1단계로 돌아갔는지).
- **캐릭터 변경**(할 일 238): `팔라딘`·`거너`·`어쌔신` — F1 순환과 같은 `DebugCharacterCycle.TrySwapToServer`, 호스트 화면만. 캐릭터 VFX 개별 토글은 [interrupt-vfx-list.md](interrupt-vfx-list.md).
- **1 < 2 < 3 비교**: 재현 버튼 `간파(근접)`·`대쉬 간파`·`제압 간파`(또는 단계별 줄의 `재생`)를 차례로 — 재현은 슬로우가 없으므로 3단계 슬로우는 '허수아비 간파 = 제압 간파' 토글(호스트) + 실제 간파로 본다.

### 8.6 알려진 한계

- ~~**기믹 중 23호**: 클라 오너가 기믹 중 마지막 칸을 제압으로 잘못 예측해 암전을 켤 수 있다~~ → 11-04(할 일 211) 오너 제압 예측을 지워 없어졌다.
- ~~오너 화면의 성공 연출은 예측 단계(일반·대쉬) 설정을 쓴다~~ → 219: 판정 순간 제압 예측 + 서버 보정. 클라 오너 예측이 틀리면(복제 지연·기믹·구출) 서버 성공 슬로우가 올 때 한 번 다시 튼다 — 그 사이(왕복 지연) 본인 화면은 틀린 단계를 잠깐 본다.
- 레이드 구출 성공(③ 새 가족)의 제압은 179 와 같이 제압 간파 표식을 내지 않는다(제압 칸 경로 + 북진 압박 3번째 간파(232) 만).
- ~~Q4·Q5 섬광(포인트라이트·환경광 끄기 / 암순응·블룸)은 미구현~~ → 194 적중 라이트 연출 V1·V2(§9).

### 8.7 북진 압박 3번째 간파 = 제압 간파 연출 (할 일 232, 2026-11-05)

> 사용자 요청: "북진압박 3번째 간파 때도 제압 간파 연출" — 확정: **3번째 간파만**, 카메라 대상 중심 포함, 게임플레이는 그대로(연출만). 코드: `TwentyThreeBoss.RaidRelay.cs`(`_pnCounterCount`·`PushNorthCounterIsSuppressFx`·`OnPushNorthCounter`) · 순수 규칙 `BossRaidRules.IsPushNorthSuppressFxCounter` · 데이터 칸 `BossDataSO.pushNorthSuppressFxCounterNumber`(기본 3, 0 = 끔, `[DataTableIgnore]`) · 테스트 `BossRaidRulesTests.PushNorthSuppressFx_*`.

- **어느 간파**: 북진 압박(23호 B 30% 기믹 `RaidPushNorth`)은 무력화 창마다 간파 1회 = 위로 1칸, 위 꼭짓점에 닿으면 파훼(제압 8초 ×1.3). 이번 기믹의 **N번째 성공 간파**(기본 3 — 기본 마름모 방 중앙 → 꼭짓점 3칸이라 파훼하는 간파와 같다)가 제압 간파 연출. 무력화 창을 놓친 회차(간파 없음)는 횟수에 안 든다. 기믹 시작(`StartPushNorth`)마다 0 부터.
- **연출만**: 제압 칸과 무관하게 3단계 전부(제압 V3 에셋·제압 성공 슬로우 `interruptSuppressSuccessProfile`·화이트아웃·**카메라 대상 중심** — 모든 피어). 게임플레이(밀림·파훼 제압·게이지)는 그대로 — 실제 제압은 밀림이 끝난 뒤 `EndPushNorthPush` 가 기존대로 낸다.
- **서버 = 정답**: `OnPushNorthCounter`(간파 성공 수신 안)에서 23호 칸 0 분기와 같은 `ServerMarkSuppressInterrupt()` → 같은 프레임의 성공 사건이 소비해 슬로우 Start 이벤트 **bit0**(새 RPC 없음).
- **오너 예측**: 무력화 창을 열 때 그 창의 간파가 N번째면 23호 복제값 `_counterWindowSuppressFx`(NetworkVariable bool, 서버 쓰기 — 새 RPC 아님)를 켠다 — 창이 닫히면(`SetCounterWindow(false)`, 간파 성공·무력화 끝·기믹 종료 전부) 같이 꺼진다. `WouldSuppressOnInterrupt` 가 이 값이 켜졌으면 참 → 오너 판정 순간 3단계(§8.2, 카메라 중심 포함). 서버 표식은 보험.
- **끄기**: 보스 SO `pushNorthSuppressFxCounterNumber` = 0 → 예전처럼 일반·대쉬 단계(서버 표식·복제값 둘 다 안 냄).
- **알려진 한계**: `pushNorthStepTiles`·방 크기를 바꿔 3번째가 파훼 간파가 아니게 되면 연출과 파훼가 어긋난다 — 그때는 이 칸을 같이 맞춘다. 지형 끝 막힘(`stuck`)으로 3번째 전에 파훼되면 제압 연출 없이 끝난다.

## 9. 적중 라이트 연출 V1·V2·V3 (할 일 194)

> 계획: `_lane-artifacts/Work2/194/PLAN.md`(승인 2026-11-03). 레퍼런스 = ZZZ 컷신 0234(참고만 — 프레임을 텍스처로 쓰지 않는다).
> 코드: `InterruptPresentationTier.cs`(버전 칸·선택 함수) · `HitLight/`(`InterruptHitLightLook` SO · `InterruptHitLightFx` 월드 이펙트 · `InterruptHitLightPlayer` 진행기 · `InterruptHitLightWhiteoutSettings`) ·
> `Rendering/SceneLightDim.cs`(태양광·환경광 배율 공유) · 저작 메뉴 `Tools/Interrupt/적중 라이트 연출 에셋 만들기 (194)` · 테스트 `InterruptHitLightTests`·`SceneLightDimTests`·`InterruptPresentationTierTests`.

### 9.1 버전표 — 단계(일반·대쉬·제압)마다 고른다

| 버전 | `InterruptTierFlash` | 무엇 | 누가 보나 |
|---|---|---|---|
| **V0** | `HitFlash` | 기존 적중 섬광 ②~⑥ + 섬광 순간 월드 섬광·라이트·A2 암순응(§0) — 효과 칸(§17)을 따른다 | 오너 화면 ②~⑥, 모든 피어 월드 섬광 |
| **V1** | `HitLight` | 성공 순간 바로: 적중 포인트 라이트 + 암순응(가/나) + 블룸 + 불꽃 + 메시 파편 + 빛줄기 | **모든 피어**(암순응 가는 본인 1·팀원 0.35) |
| **V2** | `HitLightWhiteout` | V1 + 적중 화이트아웃 3프레임(기존 ③ 그림 재사용, ③ 칸과 무관) | V1 + 화이트아웃은 **본인 화면만** |
| **V3** | `HitLightReference` | 레퍼런스 맞춤(194 후속) — 전용 SO·프리팹(`InterruptHitLightLook_V3`·`FX_InterruptHitLight_V3`): 암순응 **나** 기본(태양광 −95%·환경광 −90%) + 채도 낮춤 색 보정 Volume + 약한 라이트(2·반경 3) + 중심만 번지는 블룸 + 작은 중심 점·가로 빛줄 + 가늘고 많은 불꽃 + 작은 빛줄기, 화이트아웃 없음 (§9.6) | **모든 피어** |
| ~~(제압)~~ | ~~`SuppressSequence`(값 1)~~ | 11-04(할 일 211) 삭제 — 제압도 위 V0~V3 에서 고른다(§9.7). 값 1 은 비워 둠(들어 있으면 V0) | — |

- 기본 = **일반 V3 · 대쉬 V3 · 제압 V3 · 암순응 나**(제압은 11-04 할 일 211 — 전에는 시퀀스 + V3)(2026-11-03 사용자 결정 "V3 모든 인터럽트에 적용", 코드 기본값 = 프리팹 값). V0·V1·V2 는 지우지 않았다 — 랩 버튼(§9.4)으로 계속 고를 수 있다.
  디렉터 칸 `hitLightLook`(SO)·`hitLightVfx`(프리팹) 중 하나라도 비면 V1·V2 는, `hitLightLookReference`·`hitLightVfxReference` 중 하나라도 비면 V3 는 **V0 로 돌아간다**(`SelectSuccessLook`).
- **틈 수직(기본, 194 후속 11-03)** — `hitLightOrigin = Gap`(enum 값 2를 뒤에 추가, 무기 0·대상 1 은 그대로·NetworkManager.prefab 단계 칸 값 2). 위치 = 오른손 무기(없으면 시전자 중심 = 발밑 + 섬광 높이)와 대상 몸 표면(등록 대상 콜라이더 `ClosestPoint`, 몸 → 트리거 순, 못 구하면 대상 적중점)의 중간점. 방향 = 시전자→대상 축(바닥 평면)의 수직선 — `Rays` 는 날개 2개 메시(`gapRaysMesh` = `Mesh_InterruptHitLight_Streak`)를 SO `gapRaySpread`(±20°)·`gapRayCount`(5) 부채로 양쪽, `Sparks` 는 수직선 양쪽 원뿔(`gapSparkSpread` 30°), V3 `Streak` 은 `gapStreakAxis`(수직선 기본 / 축 — 레퍼런스는 축 쪽) — 납작한 것은 렌더 정렬 Local + 카메라 쪽 기울임(`InterruptHitLightRules.ViewTilted`)이라 화면에서 늘 그 선 각도. `Core`·`Debris`·라이트는 위치만. 성공 순간 한 번 계산해 고정, 시전자를 못 찾거나 축이 없으면 방향 없음(처음 모습). 순수 함수 `InterruptHitLightRules.Place`·`ViewTilted`·`SparkDirection`, 프리팹 값은 재생 때만 바꾸고 무기·대상 모드는 처음 읽은 값으로 되돌린다.
- **발생 위치 무기·대상**(194 후속, 단계 칸 `hitLightOrigin`) — 월드 이펙트(라이트·불꽃·파편·빛줄기)는 **시전자 오른손 무기**(Variant 루트 `PlayerWeaponAnchor.rightWeapon` = 팔라딘 `R_WeaponSocket` · 거너 `rig/artillery/LaserGun` · 암살자 `Dagger_Weapon_R`, 무기 로컬 `localOffset` 으로 칼끝 쪽 조정)의 성공 순간 위치에 남는다(따라가지 않음, 모든 피어가 자기 화면의 그 캐릭터 위치). 사건의 시전자(`CasterClientId`)로 찾고, 못 찾으면(앵커 없음·랩 가상 시전자) **대상 적중점**. `대상` 선택 = 194 처음 방식. 화이트아웃·V0 섬광 기준점은 늘 대상.
- 전부 **실시간**(unscaled, Q8) — 슬로우·히트스톱 중에도 같은 빠르기. 줌은 지금처럼 오너만(슬로우 목적).
- 섬광 순간 예약이 없다 — 성공 순간이 곧 섬광 순간(오너 성공 단계도 그때 끝낸다). 서버 거부·강제 리셋이면 즉시 끈다(`StopHitFlash` 한 곳 — 제압 시퀀스 전환은 11-04 삭제).

### 9.2 구성 — `FX_InterruptHitLight.prefab`·`InterruptHitLightLook.asset` (`Assets/5.VFX/Interrupt/HitLight/`)

| 요소 | 기본 | 칸 위치 |
|---|---|---|
| 포인트 라이트 | 흰주황 (1, 0.75, 0.45) · 세기 20 · 반경 8 · 0.9초 감쇠 곡선 · 그림자 끔 | SO `light*` |
| 블룸 | `VP_InterruptHitFlash_Bloom` 가중치 1 → 0.9초 곡선(이 피어 전용 전역 Volume, 우선순위 1000) | SO `bloom*` |
| 암순응 **가** | 기존 `InterruptDarkAdaptation` 화면 패스에 프레임마다 어둡힘(본인 1 · 팀원 0.35 · 0.9초 곡선). **A2 칸과 무관**(V1·V2 세트) | SO `darkAdapt*` |
| 암순응 **나** | 태양광 −85% · 환경광(구면 조화) −70% → 0.9초 곡선으로 복귀. 모든 화면 같은 세기 | SO `sunDimDepth`·`ambientDimDepth`·`sceneDim*` |
| 불꽃 `Sparks` | 스트레치 빌보드 주황 28~36개, 중력 — 간파 섬광 가우시안 가산 셰이더 재사용 | 프리팹 파티클 |
| 파편 `Debris` | 쇳조각 메시(에셋) 5~8개, 3D 회전·중력 2.5·월드 바닥 충돌, 식는 색 | 프리팹 파티클 · `Mesh_InterruptHitLight_Debris` |
| 빛줄기 `Rays` | 날개 10개 메시(에셋) + 셰이더 `BeaverLobby/VFX/InterruptHitLightRays`(흰 중심·주황 가장자리·노이즈), 메시 모드·카메라 정렬·회전 무작위, 크기·알파 수명 곡선 | 프리팹 파티클 · 재질 칸 |
| V2 화이트아웃 | 3 렌더 프레임 | SO `whiteoutFrames` |

- 메시·재질·프리팹은 **저작 메뉴가 에셋으로 저장**한다(런타임 텍스처·메시 생성 없음). 메뉴를 다시 돌리면 프리팹을 같은 guid 로 다시 짓고 디렉터 칸을 다시 잇는다(SO 값은 그대로). 파티클을 손으로 고쳤으면 메뉴를 다시 돌리지 말 것.
- 플레이어 재질 4개(`FK_Assassin_Body`·`FK_Gunner_Body`·`FK_Paladin_Shield_Toon`·`FK_Paladin_Sword_Toon`)는 `FK_Paladin_Toon` 의 **재질 Variant** 라 `_LightContribution`·`_LightFalloffSize` 1 을 이미 상속한다 — 수정 없음.

### 9.3 암순응 나 × 23호 차징 — `SceneLightDim`

- 23호 차징의 태양광 끄기(`BossChargeTetherView`)와 암순응 나가 같은 Directional Light 를 만진다. 둘 다 `SceneLightDim.Shared` 에 **배율**만 넣고, 창구가 원래 값을 한 번 저장해 `원래 × 배율 곱` 으로 적용·모두 1 이면 복구한다.
  → 차징 중 간파, 간파 중 차징 시작, 누가 먼저 끝나도 원래 밝기로 돌아온다(어두운 값을 원래 값으로 잘못 저장하지 않음). 씬이 바뀌면 저장값을 버린다.

### 9.4 DebugInterruptLab 버튼 (튜닝 패널 "간파 단계" 아래)

- 섹션 '단계별 적중 라이트 (V3)': `간파(근접) — 실제 V?` / `대쉬 간파 — 실제 V?` / `제압 간파 — 실제 V?` 줄마다 `가 화면`·`나 씬 조명`·`위치 틈`·`위치 무기`·`위치 대상`·`재생`. 지금 값은 `[ ]` 로 표시, **실제** = 데이터 없음으로 V0 로 돌아간 것까지 반영.
  **버전 `V0`·`V1`·`V2`·`V3` 버튼은 11-06(할 일 238) 삭제** — 런타임은 세 단계 모두 V3(NetworkManager 디렉터 `flash: 4`). V0~V2 로 비교하려면 디렉터 인스펙터 단계 칸 `flash` 를 바꾼다. 암순응 가/나·위치는 V3 경로(`InterruptHitLightPlayer.ApplyDarkAdapt`·`BeginHitLight` Origin)가 실제로 읽어 남겼다.
- 이 피어 런타임 값만(저장 안 함), 다음 성공부터. `재생` = §8.5 단계 재현과 같은 경로(이 화면만, 슬로우·줌 없음).
- 랩 블룸: 씬에 카메라·Volume 은 없고 `CameraSwitcher` 가 런타임에 `MainCamera.prefab`(후처리 켬 · Volume 마스크 Default)을 만든다 — 진행기의 블룸 Volume(Default 레이어)이 걸린다. 정적 구성 확인만 했고 화면 확인은 Play 필요.

### 9.5 알려진 한계

- 실시간 재생이라 히트스톱 중에도 파티클·라이트가 움직인다(Q8 결정대로).
- 암순응 나는 화면마다 세기가 같다(본인/팀원 구분 없음). 환경광은 구면 조화 배율이라 반사 프로브·라이트맵은 그대로다.
- 파편 바닥 충돌은 월드 콜라이더 전체와 부딪힌다(플레이어 캡슐 포함) — 개수가 적어 비용은 작다.
- V2 화이트아웃은 A5 전투 표시 숨김 구간에 들지 않는다(3프레임).
- 데이터 테이블(xlsx)에는 내지 않았다 — SO 가 원본.

### 9.6 V3 레퍼런스 맞춤 (194 후속, 2026-11-03)

- 왜: V1·V2 는 블룸(threshold 0.8·intensity 6)·라이트(20·반경 8)가 세서 바닥까지 하얗게 날아가고 화면이 뿌옇다. 레퍼런스 2:34 적중 프레임(f30·f34)은 주변이 어둡고 보랏빛으로 채도가 낮고, 작고 강한 중심 점 + 가로 얇은 빛줄 + 맞닿은 가장자리만 주황 + 가늘고 긴 주황 불꽃.
- **V1·V2 값은 그대로**(버전 보존) — V3 는 같은 형식의 별도 SO·프리팹·Volume 프로필. 디렉터 칸 `hitLightLookReference`·`hitLightVfxReference`, 둘 중 하나라도 비면 V3 → V0. 저작 메뉴 `Tools/Interrupt/적중 라이트 V3 레퍼런스 에셋 만들기 (194)`(SO·프로필·재질은 있으면 값 유지, 메시·프리팹은 같은 guid 로 다시 짓는다).
- 기본값: 처음엔 안 바꿨다가(일반 V1·대쉬 V2) 사용자가 랩 비교 뒤 **11-03 일반·대쉬·제압 모두 V3** 로 결정(§9.1·§9.7 — 제압은 11-04 할 일 211 부터 시퀀스 없이 V3 만). 랩 `V3` 버튼은 그 단계 암순응을 **나** 로 함께 맞춘다(`DefaultDarkAdapt`, 누른 뒤 가로 바꿀 수 있음).
- **에셋 위치(11-03 정리)**: V3 전용은 `Assets/5.VFX/Interrupt/HitLight/V3/` — `InterruptHitLightLook_V3`·`VP_InterruptHitLight_V3_Bloom`·`VP_InterruptHitLight_V3_Grade`·`FX_InterruptHitLight_V3`·`M_InterruptHitLight_Core`·`M_InterruptHitLight_Streak`·`M_InterruptHitLight_Spark_V3`·`Mesh_InterruptHitLight_Core`.
  V1 과 함께 쓰는(또는 V1·V2 만 쓰는) 것은 상위 `HitLight/` 에 남는다 — `Mesh_InterruptHitLight_Streak`(V1 틈 수직 `gapRaysMesh`)·`Mesh/M_InterruptHitLight_Rays`·`Mesh/M_InterruptHitLight_Debris`·셰이더 `InterruptHitLightRays`. 경로 상수 = `InterruptHitLightReferenceAuthoring`.
- 아래 표는 **시작값**이다. 현재 값은 사용자가 에디터에서 직접 튜닝한 값(11-03 `3a95c486` — 라이트 10·반경 100·그림자, 블룸 threshold 0.19·intensity 5, 노출 −5·대비 55·채도 40·색 필터 흰색)이고, 테스트는 V3 절대값을 단언하지 않는다.

| 요소 | V3 시작값 | 칸 |
|---|---|---|
| 암순응 나 | 태양광 −95% · 환경광 −90% · 0.9초 곡선 | `InterruptHitLightLook_V3` `sunDimDepth`·`ambientDimDepth` |
| 색 보정(신규 칸) | `VP_InterruptHitLight_V3_Grade` = ColorAdjustments 채도 −35 · 색 필터 (0.84, 0.78, 1) 보랏빛 · 노출 −0.7(11-03 −0.3→−0.7), 가중치 1 → 0.9초 곡선(블룸과 같은 방식의 이 피어 전용 전역 Volume) | SO `gradeProfile`·`gradeDuration`·`gradeCurve` (V1·V2 는 비어 있음 = 없음) |
| 포인트 라이트 | 주황 (1, 0.6, 0.3) · 세기 2(11-03 4→2) · 반경 3 | SO `light*` |
| 블룸 | `VP_InterruptHitLight_V3_Bloom` threshold 2 · intensity 0.6(11-03 1.5/1.5→2/0.6, "블룸이 너무 쎄") · scatter 0.6 · 따뜻한 틴트 | SO `bloomProfile` |
| 중심 점 `Core` | 넓은 날개 16개 메시(에셋 `Mesh_InterruptHitLight_Core`) 크기 0.45 · HDR 중심 16 · 0.35초 | 재질 `M_InterruptHitLight_Core`(셰이더 `InterruptHitLightRays` 재사용) |
| 가로 빛줄 `Streak` | 좌우 날개 2개 얇은 메시(에셋 `Mesh_InterruptHitLight_Streak`) 크기 2.6 · 회전 0(늘 화면 가로) · 0.22초 | 재질 `M_InterruptHitLight_Streak` |
| 불꽃 `Sparks` | 55~70개 · 크기 0.025~0.045 · 속도 9~18 · lengthScale 4 · 중력 0.4 · 재질 HDR 7 | 재질 `M_InterruptHitLight_Spark_V3` |
| 빛줄기 `Rays` | V1 메시·재질 그대로, 크기 0.8~1.2 · 알파 0.4 | 프리팹 파티클 |
| 빛줄기 공간(11-03 렌즈 연출) | **V3 = 화면**: 틈 원점을 게임 카메라로 투영한 화면 점에 카메라 정면·깊이 무시(ZTest Always·Overlay 큐)·화면 높이 비율 크기로 그린다(카메라 거리·줌 무관, 원점이 화면 밖·카메라 뒤면 안 그림). 월드 원점·방향은 성공 순간 고정, 화면 투영은 카메라 렌더마다(`beginCameraRendering`, `Camera.main` 만). 크기 = 프리팹 Start Size × `rayScreenSize`(화면 높이 비율 — 0.05 면 Start Size 8 = 중심~날개 끝 화면 높이 40%) · 길이 = `rayScreenLength`(줄 방향만) · 밝기 = `rayScreenBrightness`(재질 `_Intensity`). V1·V2 = 월드 그대로. 불꽃·파편·라이트·중심 점·가로 빛줄은 늘 월드 | SO `raySpace`·`rayScreenSize`·`rayScreenLength`·`rayScreenBrightness`·`rayScreenMaterial`(V3 `M_InterruptHitLight_Rays_Screen`, 셰이더 V3 `InterruptHitLightRaysScreen` = V1 셰이더 복제 + ZTest Always·`_Intensity`) |
| ~~파편 `Debris`~~ | **11-05 삭제**(사용자 "새 파티클 어색" — 섬광 주변 주황 알갱이). V3·대쉬·제압 프리팹에서 게임오브젝트째 제거, 저작 코드(`InterruptHitLightReferenceAuthoring`·`InterruptHitLightTierAuthoring` 배율)도 뺐다. V1·V2(`FX_InterruptHitLight`)는 그대로라 재질·메시 에셋은 남긴다 | — |
| 화이트아웃 | 없음(0) | SO `whiteoutFrames` |

- 4.MapScene(보스방 포함, 같은 씬) 기준: 태양광 0.75(실시간)·환경광 Trilight·전역 Volume `VP_CombatLevel_Toon`(노출 +0.25·채도 +8·대비 12·블룸 threshold 1.1/intensity 0.35·Neutral 톤매핑·비네트 0.22). V3 색 보정은 우선순위가 높아 가중치 1 에서 노출·채도·색 필터를 덮고(대비는 맵 값 유지), V3 블룸 threshold 2 는 맵(1.1)보다 높아 HDR 이펙트만 번진다. 랩(흰 체크 바닥·태양광 1·Volume 없음)보다 맵이 원래 어두워 V3 는 더 어둡게 보일 수 있다 — Play 로 확인.

### 9.7 제압 간파 = V3 (할 일 211, 2026-11-04)

- **11-04 제압 간파 = V3, 시퀀스는 Legacy.** 제압 단계도 일반·대쉬와 **같은 버전 선택**(`suppressTier.flash`, 기본 `HitLightReference` = V3 · `darkAdapt` 나 · `ownerDim` 끔)이다 — `InterruptPresentationTierRules.SelectSuccessLook` 에 제압 분기가 없다.
- 서버 성공(`HandleStarted`)은 사건 표식으로 고른 단계(`Suppress`)의 연출을 모든 피어가 튼다. 오너는 판정 순간 예측 단계(일반·대쉬)로 이미 틀었으므로 내 서버 성공은 다시 틀지 않는다(기본값 = 셋 다 V3 라 같은 화면).
- 오너 암전·빛 모임·섬광 시퀀스·제압 암순응 컷 **없음**. 랩 버전 버튼 줄에 `제압 간파` 가 생겼다(§9.4) — 재현은 '제압 간파' 재현 버튼, 실제 표식은 '허수아비 간파 = 제압 간파' 토글(호스트).
- 옛 194 후속(11-03) '시퀀스 + V3 더하기'(`sequenceHitLight`·`SelectSequenceHitLight`·`SequenceHitLightDarkAdapt`)는 삭제 — 시퀀스 자체가 Legacy(§7).

### 9.8 슬로우 3단계 SO — `InterruptSlowMotionSettings` (194 후속 2, 2026-11-03)

에셋 `Assets/5.VFX/Interrupt/HitLight/V3/InterruptSlowMotionSettings.asset` 하나에서 간파 슬로우를 단계별로 고친다. 디렉터(`NetworkManager.prefab`)·카메라(`CameraSwitcher.prefab › CameraTargetSwitcher`)는 같은 에셋을 참조(`slowMotionSettings`)만 하고 사건마다·프레임마다 읽는다(캐싱 없음 — Play 중 SO 를 바꾸면 다음 사건부터. SO 에셋 변경은 Play 끝나도 남는다). 참조가 비면 코드 기본값(`InterruptSlowMotionSettings.OrDefault`).

| 단계(인스펙터 머리글) | 칸 | 처음 값 | 쓰는 쪽 |
|---|---|---|---|
| ① 대쉬 간파 시도 | `interruptDashProfile.*` 7(강) · `interruptDashWeakProfile.*` 7(약) | ② 강·약 복사(0.15/0/0.02/0.05/0.5/0.25/0.1 · 0.5/0/0.02/0.05/0.2/0.25/0.1) | 서버(Start 이벤트에 실림) |
| ② 간파 준비(근접 간파 시도) | `interruptProfile.*` 7(강) · `interruptWeakProfile.*` 7(약) · `interruptZoomFactor` 0.7 · `interruptWeakZoomFactor` 0.93 | 이관 전 프리팹 값 그대로 | 프로필 = 서버 / 줌 = 각 피어(A3), **줌은 ① 대쉬 시도도 같이 쓴다** |
| ③ 간파 적중(성공) | `interruptSuccessProfile.*` 7(히트스톱 `hitStopDuration` 0.05·`hitStopScale` 0.02 포함) · `slowMotionZoomFactor` 0.85 | 이관 전 프리팹 값 그대로 | 프로필 = 서버 / 줌 = 각 피어(A3) |
| ③-2 제압 간파 적중(할 일 219) | `interruptSuppressSuccessProfile.*` 7 — 제압 표식(bit0) 성공만, 일반·대쉬는 ③ | 히트스톱 0.08(0.02) · 슬로우 0.3 · 진입 0.02 · 유지 0.35 · 복귀 0.25 · 실패 복귀 0.1(코드 기본값 = 에셋 값) | 서버(`InterruptProductionRules.SelectSuccessProfile` — 새 RPC·바이트 없음). 줌은 ③ `slowMotionZoomFactor` 공유(에셋 1 = 줌 없음) |

- **219 제압 성공 슬로우**: 다른 프로필은 에셋 중립값(아래)이라 사실상 꺼져 있고 3단계만 켜져 있다. 히트스톱은 디렉터 A1 `hitStop`(NetworkManager.prefab 켬)이 꺼지면 0. 서버 권한이라 팀원 화면도 같이 느려진다.

- 스위치 A1 `hitStop`·A3 `ownerZoom` 은 디렉터에 남았다(bool, 테이블 밖).
- **대쉬 시도 고르기**: 서버 시도 사건 `InterruptProductionEvent.Attempt(…, variant)` 에 대시에 실렸는가(`attachedToDash`)를 싣고 `InterruptProductionRules.SelectAttemptProfile(purpose, variant, settings)` 가 고른다. 목적(`Attempt`/`AttemptWeak`)은 근접과 같아 창 장부·오너 강약·빛 모임·줌이 그대로다. 새 RPC·바이트 없음 — 시도 Start 이벤트의 대쉬 비트는 여전히 성공에만 실린다(시도 슬로우는 프로필 7칸이 이미 실려 간다).
- 동작 변화 0(이관 당시): 처음 값 = 이관 전 값(대쉬 = 복사). EditMode `InterruptProductionPrefabTests`(코드 기본값 = 이관 당시 값·에셋 값은 정리 범위 안·프리팹 디렉터가 SO 의 맞는 칸을 고름 — 에셋 숫자는 튜닝값이라 단언 안 함)·`InterruptProductionStageTests`(대쉬 시도 → 대쉬 프로필, 근접 → 기존)로 고정.
- 대쉬 간파는 대시 남은 시간이 유지(hold) 구간을 먹는다(조사 `194-followup2-slowmo-dash-attempt.md` §2) — ① hold 를 따로 튜닝하는 자리. 체감은 🟧 Play 로 확인.

#### ✅ 데이터 테이블 — 병합 Export 완료(SVN r580, 2026-11-03 — 버려진 값 26·새 42, 아래 목록과 정확히 같음)

칸을 SO 로 옮겨 xlsx `SlowMotion` 시트의 **대상 열이 바뀐다**(`NetworkManager`·`CameraSwitcher` → `InterruptSlowMotionSettings`). 필드 이름은 그대로라 값 의미는 같다. **xlsx 는 이 브랜치에서 고치지 않았다.**

- **절차**: 이 브랜치를 development 에 병합한 **당일**, Main 이 **최신 dev 워크트리**에서 SVN update(`GameData.xlsx`) → `Tools/Data/Export Template` **병합** → 보고의 "버려진 값" 이 아래 목록과 정확히 같은지 확인 → SVN 커밋([data-table.md §4](data-table.md)). 다른 레인은 그 사이 xlsx Export 금지.
- **버려질 행(의도) — 2026-11-03 xlsx(이 워크트리) 를 읽어 센 것, 합계 26행**
  - `NetworkManager` 열(구역 `#■ InterruptProductionDirector`, A4~A27): `interruptProfile.*` 7(A4~A10) · `interruptWeakProfile.*` 7(A11~A17) · `interruptZoomFactor`(A18) · `interruptWeakZoomFactor`(A19) · `interruptSuccessProfile.*` 7(A21~A27) = **23**
  - `CameraSwitcher` 열(구역 `#■ CameraTargetSwitcher`): `slowMotionZoomFactor`(A31) = **1**
  - `NetworkManager` 열(구역 `#■ InterruptDarkAdaptation`): `revealRadius`(A43) · `revealSoftness`(A44) = **2** (2026-11-03 코드에서 삭제된 칸, 같은 Export 에서 함께 정리)
  - 값은 버려져도 SO 처음 값이 같은 숫자라 손실 없음(Export 직후 새 열 값 = 위 값과 같아야 한다 — 다르면 멈추고 확인).
- **새로 생길 행 — 합계 42행**
  - `InterruptSlowMotionSettings` 열(새 구역 `#■ InterruptSlowMotionSettings`, Order 0, 머리글 ①②③ 메모 행 별도): `interruptDashProfile.*` 7 · `interruptDashWeakProfile.*` 7(대쉬 14 — 새 칸) · `interruptProfile.*` 7 · `interruptWeakProfile.*` 7 · `interruptZoomFactor` · `interruptWeakZoomFactor` · `interruptSuccessProfile.*` 7 · `slowMotionZoomFactor` = **38**(옮긴 24 + 대쉬 14 — 에셋 직렬화 float 칸 38개로 확인)
  - `NetworkManager` 열(구역 `#■ InterruptManager`, Order 3): 191 서버 검증 허용 오차·동시 성공 `verifyTimeSlack` 0.033 · `verifyDistanceSlack` 0.2 · `verifyAngleSlack` 5 · `simultaneousSeconds` 0.05 = **4** (지금 xlsx 에 이 구역이 아직 없다 — 행이 없어 프리팹 값으로 돌던 칸이 이번 Export 로 생긴다)
  - 구역 `#■ InterruptProductionDirector`·`#■ CameraTargetSwitcher` 는 사라진다(두 타입 모두 테이블 칸이 없어 `[DataTableSheet]` 를 뺐다).
- **🔴 병합 ~ Export 사이 경고**: xlsx 에 옛 행이 남아 있는 동안 **테이블 모드 Play·빌드는 시작 전에 오류로 멈춘다**(`NetworkManager` 의 `interruptProfile.*` 등 = "어디에도 필드가 없다", `CameraSwitcher` 열 = "대상 없음", `revealRadius`·`revealSoftness` = "필드가 없다"). 인스펙터 모드 Play 는 영향 없음. 그래서 병합과 Export 를 같은 날 붙여서 한다.

#### 에셋 중립값 — 슬로우·히트스톱·줌 사실상 끔 (2026-11-03 사용자 지시, 브랜치 `tune/InterruptSlowMoNeutral`)

- 에셋의 프로필 5개 모두 `scale`·`hitStopScale` = 1, `hitStopDuration`·`enterDuration`·`holdDuration`·`exitDuration`·`failExitDuration` = 0, 줌 3칸(`interruptZoomFactor`·`interruptWeakZoomFactor`·`slowMotionZoomFactor`) = 1. 코드 기본값(새 SO·참조 빔)은 위 표 그대로다.
- 동작: 서버는 시도·성공 슬로우를 그대로 발동하고 `Started` 이벤트도 전 피어에 그대로 난다(창 장부 닫힘·오너 성공 단계·빛 모임·성공 섬광은 그대로 돈다). 다만 타임라인은 길이 0 단계를 건너뛰어 **시작 즉시 유휴**라 `Time.timeScale`·줌은 안 바뀐다.
- 알려진 차이: 빗나감 `ServerFail` 이 올 때 슬로우가 이미 유휴라 **`Failed` 이벤트가 나지 않는다**. 오너 암전은 오너 판정 cue(191 `JudgeFailed`)·스킬 끝(`LocalOwnerEnded`)이 걷어 영향 없지만, **팀원 화면 로컬 선착순 장부(`InterruptLocalWindowClaims.AttemptEnded`)가 그 강 시도로 닫은 창을 다시 열지 않는다** — 새 창이 열릴 때까지 팀원의 같은 창 입력 예측이 약(암전·섬광 없음)으로 내려간다. 서버 판정은 영향 없다(연출만).
- ✅ **xlsx `SlowMotion` 시트 = 중립값(SVN r583, 2026-11-04 196·197 병합 Export)** — 병합 Export 는 기존 xlsx 값을 유지하므로, Export 직전에 `InterruptSlowMotionSettings` 열 38칸을 SO 값으로 덮어써 옮겼다(34칸 변경·4칸 이미 같음, 다른 칸 변경 0·버려진 값 0). 이후 이 값을 바꿀 땐 xlsx 를 직접 고친다(병합 Export 로는 기존 칸이 안 바뀐다).

#### 🟪 219 병합 때 — 병합 Export 할 새 행(xlsx 는 브랜치 `feature/InterruptTierScaling` 에서 고치지 않았다)

- **새로 생길 행 7개** — `SlowMotion` 시트 구역 `#■ InterruptSlowMotionSettings`, 대상 열 `InterruptSlowMotionSettings`: `interruptSuppressSuccessProfile.scale`(0.3) · `.hitStopDuration`(0.08) · `.hitStopScale`(0.02) · `.enterDuration`(0.02) · `.holdDuration`(0.35) · `.exitDuration`(0.25) · `.failExitDuration`(0.1). 버려질 행 0.
- **절차**: development 병합 당일 최신 dev 워크트리에서 SVN update(`GameData.xlsx`) → `Tools/Data/Export Template` 병합 → 보고가 "새 7 · 버려진 값 0" 인지 확인 → SVN 커밋. 병합 Export 는 기존 칸을 안 바꾸므로 새 7칸은 SO 값(위)으로 들어간다.
- **그 사이 테이블 모드**: 행이 없을 뿐 **오류 아님** — `DataTableApplier.Bind` 는 테이블에 있는 칸만 쓰므로 새 칸은 SO 에셋 값으로 돈다([data-table.md](data-table.md) "테이블에 없으면 = SO·프리팹 값"). 빌드도 같다.
- **🟪 사용자 튜닝(커밋 `4dfe01a0`, 11-05) — 기존 행 값도 바뀜 → 병합 Export 때 덮어쓰기 필요**: 병합 Export 는 기존 xlsx 값을 유지하므로(지금 xlsx = 중립값 r583), Export 직전에 `InterruptSlowMotionSettings` 열을 SO 값으로 덮어쓴다(r583 때와 같은 절차). 바뀐 기존 행 **21개**: `interruptDashProfile.` scale 0.7·hitStopDuration 0.5·hitStopScale 0.7·enterDuration 0.7·holdDuration 0.5·exitDuration 0.5 · `interruptDashWeakProfile.` 0.9·0.5·0.9·0.5·0.5·0.5 · `interruptProfile.` 0.7·0.5·0.7·0.7·0.5·0.5 · `interruptSuccessProfile.scale` 0.1·`.hitStopDuration` 1.25 · `slowMotionZoomFactor` 0.9. 새 7행 중 `interruptSuppressSuccessProfile.enterDuration` = **0.2**(위 0.02 아님). 테이블 모드 Play·빌드는 그전까지 xlsx 중립값으로 돈다(인스펙터 모드에서만 이 튜닝이 보인다).

### 9.9 단계별 강화 — 1단계 일반 · 2단계 대쉬 · 3단계 제압 (할 일 219, 2026-11-05)

> 계획: `_lane-artifacts/Work1/219/PLAN.md`(grill 확정 11-05). 코드: `InterruptPresentationTierRules.SelectReferenceAssetTier`·`WhiteoutFrames(look, tier, …)`·`OwnerJudgeTier`·`ServerCorrectsOwner` · 디렉터 `HitLightLookFor(look, tier)`·`ReferenceAssetTier` · 진행기 요청 `Tier`·`SuppressWhiteout`(에셋 창구 `IInterruptHitLightAssets`) · 저작 메뉴 `Tools/Interrupt/간파 단계별 V3 강화 에셋 만들기 (219)`(없을 때만 생성·빈 칸만 연결) · 테스트 `InterruptTierScalingTests`.

- **1단계(일반) = 지금 V3 그대로**(`InterruptHitLightLook_V3`·`FX_InterruptHitLight_V3` — 값 변화 0). 대쉬·제압은 V3 를 **복제**한 SO·블룸 프로필·프리팹(`V3/Dash/`·`V3/Suppress/`)을 단계 칸으로 고른다. 재질·메시·셰이더는 공유. **색 보정 프로필도 단계별**(11-05 후속): `V3/Dash/VP_InterruptHitLight_V3_Dash_Grade`·`V3/Suppress/VP_InterruptHitLight_V3_Suppress_Grade` = 일반 `VP_InterruptHitLight_V3_Grade` 복사(시작값 같음 — 동작 변화 0, 219 배율은 블룸 프로필에만 있어 옮길 값 없음). 단계 SO `gradeProfile` 이 각자 가리키고, **칸이 비면 일반 V3 프로필**로 대체(`InterruptHitLightRules.SelectGrade` · 디렉터 `HitLightGradeFor` · 에셋 창구 `IInterruptHitLightAssets.GradeFor`). 저작 메뉴는 프로필이 없을 때만 복사하고, 단계 SO 칸이 일반 프로필을 가리킬 때만 단계 프로필로 바꾼다.
- **빈 칸 = 1단계**: 단계 SO·프리팹 둘 다 있을 때만 그 단계 것, 하나라도 비면 1단계 묶음(섞어 쓰지 않음). V3 일 때만 — V0~V2 는 단계 무관.
- 모든 피어가 같은 단계 에셋을 튼다(성공 사건의 단계 = 표식 비트). 오너는 판정 순간 단계(§8.2 제압 예측), 서버 단계가 다르면 보정.

| 요소 | 칸 | 1단계(현재 V3) | 2단계 대쉬 | 3단계 제압 |
|---|---|---|---|---|
| 불꽃 개수 | 프리팹 `Sparks` 버스트(최대 입자) | 55~70 (96) | ×1.6 = 88~112 (128) | ×2.5 = 138~175 (200) |
| 포인트 라이트 세기 | SO `lightIntensity` | 10 | ×1.5 = 15 | ×2.5 = 25 |
| 블룸 세기 | 단계별 프로필 `VP_InterruptHitLight_V3_{Dash,Suppress}_Bloom` intensity | 5 | ×1.4 = 7 | ×2.5 = 12.5 |
| 색 보정 | 단계별 프로필 `VP_InterruptHitLight_V3_{Dash,Suppress}_Grade`(11-05) | 일반 프로필 값(노출·대비·채도·색 필터) | 일반과 같음(복사) | 일반과 같음(복사) |
| 암순응 나 깊이 | SO `sunDimDepth`·`ambientDimDepth` | 0.95·0.9 | 1·1(최대) | 1·1 |
| 암순응 지속 | SO `sceneDimDuration`·`darkAdaptDuration` | 0.9 | ×1.3 = 1.17 | ×2.5 = 2.25 |
| 가로 빛줄 크기 | 프리팹 `Streak` Start Size | 2.6 | ×1.5 = 3.9 | ×2.5 = 6.5 |
| 빛줄기 양 | SO `gapRayCount`·`rayScreenBrightness`(+ 프리팹 `Rays` 버스트·최대 입자) | 5 · 1 (버스트 2 · 최대 4) | 8 · 1.5 (3 · 16) | 13 · 2.5 (5 · 16) |
| ~~파편 개수~~ | 11-05 삭제(V3 에 `Debris` 없음) | — | — | — |
| 중심 점 크기 | 프리팹 `Core` Start Size | 0.45 | 그대로 | ×2.5 = 1.125 |
| 화이트아웃 | SO `whiteoutFrames` + 디렉터 `suppressWhiteout` | 0 | 0 | **3프레임**(본인 화면, 기존 V2 화이트아웃 경로 재사용) |
| 성공 슬로우 | 슬로우 SO | ③(중립) | ③(중립) | **③-2** (§9.8) |

- 값은 **시작값**(사용자 Play 튜닝 전제) — 3단계 ×2.5 는 화면을 날릴 수 있다. 블룸·라이트가 과하면 그 칸만 낮춘다. 인스펙터에서 단계 에셋을 직접 고치면 되고, 저작 메뉴는 있는 에셋을 덮지 않는다.
- **화이트아웃 끄기(쉽게 빼기)**: 디렉터 `suppressWhiteout` 끔(또는 랩 토글) **또는** 제압 SO `whiteoutFrames` 0 — 둘 중 하나만 꺼도 안 나온다. 코드 삭제 불필요.
- **알려진 한계**: 1단계 `Rays` 최대 입자 4 가 `gapRayCount` 5 보다 작아 1단계 화면 빛줄기는 4줄로 잘린다(194 기존 — 1단계 값 변경 금지라 그대로, 2·3단계는 16). '가로 빛줄 길이' 는 크기(Start Size)로만 늘렸다(수명 0.22초 그대로).

#### V3 이펙트 = 게임 시간 (11-05 사용자 요청 "HitStop 때 이펙트도 Time Scale 영향")

- **게임 시간으로 바뀐 것(V3 일반·대쉬·제압 프리팹만)** — 프리팹 루트 `InterruptHitLightFx.gameTime` = 켬(`FX_InterruptHitLight_V3`·`_Dash`·`_Suppress`, 저작 메뉴도 켬으로 만든다). 재생 때 모든 파티클(`Rays`·`Sparks`·`Streak`·`Core`) `useUnscaledTime` 끔 → `Time.timeScale`(GlobalTimeScale 성공 슬로우·히트스톱)을 따라 **히트스톱 동안 거의 멈추고 슬로우 동안 느려진다**. 적중 **포인트 라이트 세기 곡선**도 `Time.deltaTime` 으로 진행. 빛줄기 셰이더는 시간 값을 안 쓴다(입자 수명으로만 움직임) — 바꿀 것 없음.
- **안전장치**: 히트스톱 배율 하한 0.01(`SlowMotionProfile.MinScale`)이고 슬로우 타임라인은 실시간으로 끝나 timeScale 은 늘 1 로 돌아온다. 그래도 일시정지 등으로 0 근처에 갇히면 라이트는 **곡선 길이 + 5초(실시간, `InterruptHitLightFx.GameTimeSafetyRealSeconds`)** 에 끈다(`InterruptHitLightRules.LightTimedOut`). 파티클은 다음 재생 때 `Clear` 로 지워지고, 월드 이펙트 인스턴스는 단계별로 하나씩 재사용(풀 반납·정리 타이머 없음 — 진행기 `Finish` 는 이펙트를 끄지 않고 강제 리셋 `Stop` 만 지운다).
- **실시간(unscaled)으로 남은 것 — 사용자 결정 대기**: 진행기(`InterruptHitLightPlayer.Tick(Time.unscaledTime)`)가 도는 화면 효과 — **블룸 Volume 곡선**·**색 보정(Grade) Volume 곡선**·**암순응 가(화면 패스)·나(태양광·환경광 `SceneLightDim`)**·**화이트아웃(렌더 프레임 수)**, 그리고 진행기 끝 판정(`Finished` — 라이트·블룸·암순응·색 보정 길이, 실시간). V1·V2 랩 버전 월드 이펙트(`FX_InterruptHitLight`, `gameTime` 꺼짐)도 실시간 그대로.
- 게임 시간 라이트는 슬로우만큼 실시간으로 더 오래 켜져 있어 진행기 끝(실시간)보다 늦게 꺼질 수 있다 — 서로 독립이라 문제 없음.
- 테스트: `InterruptTierScalingTests` — V3 프리팹만 게임 시간 · 재생 시 파티클 시간 기준 · 히트스톱 중 라이트 곡선 정지 + 안전장치 · 규칙.

### 9.10 거너 간파 적중 라이트 — 빛줄기·불꽃 숨김 + 라이트 색 (할 일 238, 2026-11-06)

- **사용자 결정(11-06)**: 거너가 간파 성공했을 때만 V3 적중 라이트의 빛줄기와 쇳조각(파편) 파티클을 빼고, 포인트 라이트 색을 거너 간파 이펙트 색에 맞춘다. 팔라딘·어쌔신은 그대로.
- **칸 = 디렉터 `gunnerHitLight`**(`InterruptHitLightCasterOverride`, `NetworkManager.prefab` 의 `InterruptProductionDirector`, 헤더 "성공 — 시전자 캐릭터별 적중 라이트")
  - `enabled` 켬 — 끄면 거너도 다른 캐릭터와 같다.
  - `hiddenParticles` = `Rays`(빛줄기)·`Sparks`(불꽃) — 월드 이펙트 자식 파티클 이름 목록. 이 이름의 파티클은 재생하지 않는다(그 재생만 `Stop`+`Clear`). `Streak`(가로 빛줄)·`Core` 는 그대로.
    - 🟨 "쇳조각(파편)" = `Sparks` 로 해석: V3 에는 `Debris` 가 없다(11-05 삭제, §9.6). 다르면 이 목록만 고친다.
  - `overrideLightColor` 켬 · `lightColor` = (0.42, 0.588, 1) — 거너 간파 이펙트 `FX_Arrow_hail` 의 `LightFlash` 시작 색. 세기·반경·곡선은 단계 SO 그대로, 색만 바꾼다.
- **세 단계 모두**: 일반·대쉬·제압 단계 SO·프리팹(`V3`·`V3/Dash`·`V3/Suppress`)을 복제하지 않고, 디렉터가 재생 요청(`InterruptHitLightRequest.FxOverride`)에 같은 값을 얹는다 → `InterruptHitLightFx.Play(…, fxOverride)`. 다음 재생(다른 캐릭터)은 덮어쓰기 없이 기본대로.
- **캐릭터 구분 = 시전자**: `BeginHitLight` 가 성공 사건의 `CasterClientId` 로 그 플레이어의 간파 스킬을 찾아 `GunnerInterruptSkill` 이면 얹는다(`CasterHitLightOverride`). 모든 피어가 자기 스폰 목록으로 같은 답 — 새 복제 값·RPC 없음.
- 테스트: `InterruptHitLightCasterOverrideTests`(메뉴 슬로우 EditMode 묶음) — 기본값 · 숨김/라이트 색 재생 · 다음 재생 원복 · 프리팹 칸.

## 10. 카메라 대상 중심 (할 일 196)

> 계획서: `_lane-artifacts/MainProject-WorkTree/196/PLAN.md`(레포 밖). 코드: `InterruptCameraCenterRules`(순수 규칙)·`InterruptCameraCenterTracker`(상태 기계, 실제 시간)·`InterruptProductionDirector.CameraCenter.cs`·`CameraTargetSwitcher`.

**11-05 사용자 확정 — 제압 간파 성공 순간에만, 모든 플레이어 카메라**(호스트·클라이언트, 간파한 사람·팀원 전부)를 **간파 대상 쪽으로** 짧게 옮긴다. 일반·대쉬 간파는 안 움직이고, 예전 **클릭 즉시 이동은 없앴다**. 줌(A3)은 그대로 — 중심만 옮긴다. 각 화면은 자기 카메라만, 자기 플레이어를 따르는 지점 기준으로 같은 칸 값으로 옮긴다. 새 RPC 없음.

| 칸 | 위치 | 기본값 | 뜻 |
|---|---|---|---|
| `cameraCenter` | 디렉터 "성공 — 개인" | 켬 | 켜기/끄기(bool, 테이블 밖, 각 화면 자기 값). 성공 순간 읽는다 |
| `cameraCenterMoveDuration` | 슬로우 SO ④ | 0.08 | 대상 쪽으로 옮기는 시간(초, 실제 시간) |
| `cameraCenterRatio` | 슬로우 SO ④ | 1 | 옮기는 비율(0~1, 1 = 대상이 화면 중심) |
| `cameraCenterReturnDuration` | 슬로우 SO ④ | 0.25 | 평소 중심으로 돌아오는 시간(초, 실제 시간) |

- **시작(11-05)**: ① **간파한 오너** = 오너 판정 성공 cue 순간, 판정 단계가 제압(219 오너 제압 예측 = 대상 제압 칸 마지막 칸)이면 바로(`CameraCenterOwnerJudged`, 대상 = cue 대상, 없으면 적중 지점). ② **팀원·호스트 구경 화면** = 서버 성공 슬로우 Started 의 제압 표식(bit0)으로(`CameraCenterServerSucceeded`, 대상 = 발동자 간파 스킬이 이 피어 복제 위치로 다시 찾은 대상 `FindPeerSuccessTargetId`, 못 찾으면 적중 지점 고정). ③ **오너 화면 보정** = 판정 cue 가 이 성공을 처리했고 판정 단계가 제압이면 서버 사건은 **그대로(한 번만)**, 서버 단계가 제압이 아니면 **복귀**, 판정은 일반·대쉬였는데 서버가 제압이면 **그때 옮긴다** — 순수 규칙 `InterruptCameraCenterRules.AppliesOnSuccess`·`OnServerSuccess`. 대상이 움직이면 따라가고 사라지면(파괴·비활성) 복귀. 새 제압 간파는 지금 이동량에서 이어 대체.
- **복귀**: 시작하자마자 **발동자의 성공 슬로우(제압 전용 ③-2 프로필)가 복귀(Exit) 구간에 들어가는 순간**까지 유지 — 이 화면에 그 슬로우가 끝내 안 보이면 ③-2 시간표(히트스톱+진입+유지)로. 서버 거부(내 제압으로 옮긴 것만)·서버 단계 보정·강제 리셋 = **그 순간**. 판정 실패·CC·판정 버림은 성공 전이라 옮긴 것이 없어 무관(팀원 제압으로 옮긴 중이면 그대로).
- **오프셋 방식**: 오너 줌과 같은 경로 — `FollowOffset`(World Space) = 기본 오프셋 × 줌 배율 + 이동량. 이동량은 수평(XZ)만, 복귀가 끝나면 정확히 0 → 원래 오프셋.
- **기준점(196 후속)**: 이동량 = (대상 − **카메라가 실제로 따라간 지점**) × 비율. 따라간 지점 = 플레이어 vcam `State.RawPosition` − 직전에 넣은 오프셋(기본 × 줌 배율 + 이동량) — `InterruptCameraCenterRules.FollowedPoint`. 플레이어 실제 위치를 쓰면 팔로우 감쇠(PositionDamping 1, 슬로우 때 감쇠도 느려짐)로 뒤처진 만큼 이동·대쉬 간파 때 대상에서 빗나가고 슬로우가 풀릴 때 중심이 쓸려 갔다.
- **다른 카메라 연출과 충돌**: `cameraBusy` = 연출 vcam(`IsInCinematic`) ∨ 낙하 뷰 ∨ 관전 ∨ 임시 추적(`HasTemporaryFollow`)이면 진행 중인 이동을 **즉시 버리고 0**(그 연출이 끝나도 되살리지 않는다). 추적 대상이 없을 때도 같다.
- **데이터 테이블**: SO ④ 칸 3개는 `SlowMotion` 시트(대상 열 `InterruptSlowMotionSettings`) 행 — ✅ SVN r583 병합 Export 로 생김(0.08·0.75·0.25, 버려진 값 0).
- 랩: 튜닝 패널 효과 줄 밑 **"카메라 대상 중심 (196)"** 토글 — 이 피어 런타임 값만, 다음 간파부터. '전부 켬'·'암전만' 은 건드리지 않는다.
- 테스트: `InterruptCameraCenterTests`(순수 시점표·비율·기준점·SO 기본값) · `InterruptCameraCenterTrackerTests`(이동·유지·복귀·대체·소실 · 11-05 제압만·서버 사건 처리 규칙 · 디렉터: 시도 cue·일반·대쉬 안 움직임 / 제압 오너·팀원 / 판정+서버 사건 한 번 / 예측 보정 / 서버 거부는 내 것만) · 랩 토글 `DebugInterruptLabTests`. 랩 '제압 간파' 재현 버튼도 판정 cue 경로라 이 화면 카메라가 적중 지점 쪽으로 움직인다.
- **알려진 차이(11-05)**: 원격 클라이언트 오너는 판정 순간 바로 움직이지만, 팀원 화면은 서버 사건이 닿는 만큼(편도 지연) 늦게 시작한다. 복귀는 각 화면이 받은 그 슬로우 구간을 따르므로 대략 맞는다.

**알려진 한계**
- 시네마틱 블렌드로 들어가는 첫 프레임에 이동량이 0 으로 꺼지면서 화면이 한 번 튈 수 있다.
- Cinemachine 위치 감쇠 때문에 빠르게 움직이는 대상은 화면 중심보다 조금 뒤처진다.

## 11. 이후 확장

- ~~암순응 셰이더에 흑백 실루엣·반전 모드~~ → 132 마스크 암전 모드·섬광 오버레이 흰 반전으로 구현(§0).
- ~~주변 입자가 한 점으로 모이는 효과 + 피격 대상 충격파~~ → 132 빛 모임·월드 섬광으로 구현(§0.7). 손그림으로 바꿀 땐 덮어쓸 텍스처 칸.
- ~~115 대쉬 간파: 사건 `Variant` 칸~~ → 192 에서 추가(§8.2).
  117 성공 예측 연출은 132 의 오너 즉시 예측(§0.2)으로 대신했다 — 191 부터는 매니저 시도 cue.
