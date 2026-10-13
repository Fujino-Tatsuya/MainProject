# 파티 공용 목숨 · 전멸 리스폰 · 특성 재선택 (할 일 30)

> 사양 확정: 2026-10-13(사용자). 담당: 은희(코어·네트워크·Player). 보스 리셋 훅은 경석 영역 — 최소 훅 + [handoff-boss-encounter-reset.md](handoff-boss-encounter-reset.md).
> 진행: **1단계(이 문서 + 공용 목숨 풀) ✅ 구현** · **2단계(전멸 → 복도4 리스폰 · 보스 리셋) ✅ 구현(Play 확인 전)** · 3단계(특성 초기화 팝업·재선택).
> 관련: [tutorial-director.md §11](tutorial-director.md#11-사망-규칙-s11)(사망 규칙) · §7(배리어) · §9(타이머·보스 길) · [tutorial-traits.md](tutorial-traits.md) · [data-table.md](data-table.md).

## 1. 사양 요약

1. **공용 목숨 5개** — 클라별 LifeCount(기본 3) 대신 파티 공용 풀 5개. 값은 데이터 필드(`Temp_MultiGameRule.partyLifeCount`, Player 시트).
   부활(Soul → Alive) **성공 때** 풀에서 1 차감(기존 '부활 시 차감' 시점 유지). 튜토리얼 G11(보스방 도착) 전 사망은 차감 없음(`TutorialDeathRules` 그대로).
   목숨 표시 UI 는 현재 없다(2026-10-13 검색 — 디버그 UI 언급만). 생기면 `Temp_MultiGameRule.PartyLives`/`PartyLivesChanged` 를 읽는다.
2. **전멸 리스폰** — 공용 목숨 0 에서 전멸하면 결과 화면 대신 **복도4**(튜토리얼 존4 ~ 보스 진입 사이)에 전원 리스폰.
   보스 클리어·사용자 나가기는 기존대로 결과 화면. 리스폰 = 보스 23호 처음부터(체력·패턴·등장 연출·보스방 상태) + 공용 목숨 5 재충전 + 전원 Alive·체력 가득.
   보스방은 다시 보스 진입 패드로(G10 이미 열림). 절차 맵(비튜토리얼)은 복도4 가 없어 **보스 진입 존 패드 밖** 폴백.
3. **특성 재선택 팝업** — 리스폰 직후 각자 "특성을 초기화하고 다시 선택할까요?" 예/아니오.
   예 = 고른 특성 전부 원복 후 4번 연속 선택(후보 3장, 고른 건 빠짐, 기존 선택 창·라운드 재사용, 선택 중 무적 토큰). 아니오 = 유지. 개인별 서버 검증 RPC, 입력 차단 = `UiModalBlocker`.

## 2. 공용 목숨 풀 (1단계 — 구현됨)

| 무엇 | 어디 |
|---|---|
| 순수 규칙 | `PartyLivesRules`(`Assets/1.Scripts/Player/Life/PartyLivesRules.cs`) — 사망 목적지·부활 가능·차감·재충전·전멸 판정 |
| 상태(서버 쓰기·모두 읽기) | `Temp_MultiGameRule.partyLives`(`NetworkVariable<int>`) — 스폰 때 `partyLifeCount` 로 채움 |
| 데이터 | `Temp_MultiGameRule.partyLifeCount`(기본 5, `[Min(0)]`) — Player 시트 `Temp_MultiGameRule` 구역, 대상 Id `GameRule` |
| 테스트 | `PartyLivesRulesTests`(플레이어 러너) |

- **API(이름 유지, clientId 무시)**: `TryGetLifeCount`·`HasReviveAvailable`·`TryResolveDeathState`·`TryConsumeLifeAfterAliveRevive` 가 공용 풀을 본다.
  `TryRegisterClient`·`TryUnregisterClient` 는 호환용(장부 없음 — 서버 스폰이면 true). 이탈해도 풀은 그대로.
- **새 API**: `PartyLives`(복제값)·`PartyLifeCapacity`·`PartyLivesChanged(int)`(모든 피어)·**`TryRefillPartyLivesOnServer(out int)`**(2단계 리스폰이 쓴다).
- **상태 전이**:
  - 사망 연출 끝: `ServerDeathKeepsSoul`(튜토리얼 G11 전) 또는 풀 > 0 → Soul, 아니면 PermanentDead. 차감 없음.
  - Soul → Alive(차감 부활 — F10 디버그·튜토리얼 G11 뒤 자동 부활·강제 개시 부활): 풀 > 0 일 때만 승인, 성공 직후 1 차감.
  - Soul → Alive(차감 없는 부활 — 튜토리얼 G11 전): 풀과 무관.
- **공용 풀의 새 경우 — 갇힌 Soul**: 풀 1 에서 둘이 Soul 이 된 뒤 한 명이 부활하면 0 — 남은 Soul 은 차감 부활을 못 한다.
  예전 클라별 풀에서는 "Soul = 목숨 있음" 이라 없던 경우다. 상태는 그대로 Soul 로 두고(관전 전환 없음), **전멸 판정이 '탈락'으로 센다**:
  `PartyWipeWatcher` 가 `PartyLivesRules.IsPartyWiped(풀, 구성원)` 으로 판정 — 전원 PermanentDead 이거나 풀 0 의 Soul(차감 없는 부활 대기 제외).
  `Temp_MultiGameRule` 이 없거나 스폰 전이면 예전 규칙(전원 PermanentDead). 2단계 리스폰이 Soul 도 전원 살리므로 갇힌 Soul 은 전멸 리스폰으로 풀린다(D1 유지).
- 튜토리얼 G11 뒤 자동 부활이 풀 0 으로 거부되면 경고 로그 후 Soul 로 남는다(`TutorialDeathRules.Revive`).
- 보스 강제 개시 부활(`BossTeleportManager.ReviveSoulsForForcedStart`)은 Soul 마다 공용 풀에서 1개씩 쓴다 — 풀이 모자라면 `ConnectedClientsList` 순회 앞쪽만 산다. 기존 코드 그대로.
- **데이터 테이블(xlsx Export 안 함 — 기획·병합 Export 때)**: Player 시트 · `#■ Temp_MultiGameRule` 구역 · 대상 열 `GameRule` 에 **`partyLifeCount` 행(5)** 추가.
  행이 없는 동안은 프리팹 값 5 를 쓴다(테이블 규칙 — 행 없음 = 인스펙터 값). 옛 `defaultLifeCount` 행은 코드 필드를 남겨 두었다(지우면 `필드 'X' 가 없다` 오류로 테이블 적용 전체가 멈춘다) —
  병합 Export 로 행이 정리된 뒤 필드를 지운다.

## 3. 전멸 → 복도4 리스폰 (2단계 — 구현됨)

| 무엇 | 어디 |
|---|---|
| 순수 규칙 | `PartyWipeRespawnRules`(`Player/Life/`) — 결과/리스폰 분기(`Decide`)·리스폰 가능(`CanRespawn`)·지점 선택(`TrySelectSpread`)·패드 둘레 후보(`RingCandidates`) |
| 서버 실행 | `PartyWipeRespawner`(`Managers/`, static) — `TryRespawnServer()` · 이벤트 **`PartyWipeRespawned(IReadOnlyList<ulong>)`**(3단계용) |
| 진입 | `PartyWipeWatcher` — 전멸 확정 시 `TryRespawnServer()` 먼저, false 면 기존 `GoToResult` |
| 생명주기 | `PlayerLifeCycleController.TryRespawnAfterWipeOnServer()` · 시체 회수 `PlayerCorpseController`(PermanentDead → 다른 상태) |
| 튜토리얼 | `TutorialDirector.RewindForBossRetryServer`·`ResetBackBarrierMaskServer` · `TutorialFlowModel.RewindForBossRetry` · `TutorialBackBarrierRules.MaskWithFrontmost` · `TutorialDeathRules.ResumeForBossRetry`·`TryFindWipeRespawnPoints` |
| 보스(경석 영역) | `BossEncounterDirector.ResetForRetryServer`·`IsBossDefeatedServer` · `BossTeleportManager.ResetForRetryServer` · `BossTimerManager.StopForRetryServer` — [handoff-boss-encounter-reset.md](handoff-boss-encounter-reset.md) |
| 테스트 | `PartyWipeRespawnRulesTests`(플레이어 러너) · `TutorialBossRetryTests`(튜토리얼 러너) |

### 3.1 흐름(서버 — `PartyWipeRespawner.TryRespawnServer`)

```
전멸 확정(PartyWipeWatcher, 유예 2초) → TryRespawnServer()
 0) 판정·지점 탐색 — 실패면 아무것도 안 바꾸고 false → 기존 결과 화면(GoToResult)
    · CanRespawn: 보스 미격파 ∧ (튜토리얼 ? G10 열림 : 보스 진입 패드 있음)
    · 지점: 튜토리얼 = 복도4(TryFindWipeRespawnPoints) → 못 찾으면 패드 둘레 / 절차 맵 = 패드 둘레. 둘 다 없으면 결과 화면(LogError)
 1) 보스 — BossEncounterDirector.ResetForRetryServer(잠금 해제·디스폰·Idle·송전탑 내림) → BossTeleportManager.ResetForRetryServer → BossTimerManager.StopForRetryServer
 2) 튜토리얼 — ResetBackBarrierMaskServer(각자, Corridor4) → RewindForBossRetryServer → TutorialDeathRules.ResumeForBossRetry   (🔴 마스크가 먼저)
 3) 공용 목숨 재충전(TryRefillPartyLivesOnServer → 5)
 4) 플레이어(clientId 오름차순 = 지점 순서) — TryRespawnAfterWipeOnServer → 부활 무적 2초(InvulnerabilityCause.Revive) → PlayerFallRecovery.RelocateServer(지점)
 5) PartyWipeRespawned(리스폰한 clientId 목록) — 3단계 특성 재선택 팝업이 구독
 → PartyWipeWatcher 가 _fired·유예 누적을 내려 다음 전멸도 다시 판정
```

### 3.2 리스폰 판정 — 누가 결과 화면을 막는가

- 설계 초안의 전역 훅(`Func<bool>`) 대신 **`PartyWipeWatcher` 가 `PartyWipeRespawner.TryRespawnServer()` 를 직접 부른다** — 호출자·구현이 하나뿐이라 훅을 두면 간접층만 는다.
  false 의 의미는 "아무것도 안 바꿨다" 로 고정(판정·지점 탐색을 상태 변경보다 먼저 한다) — 그래서 실패해도 결과 화면 경로가 기존 그대로다.
- 보스 클리어(`BossEncounterDirector.TickResultTransition`)·나가기(`GameManager.GoToResultButton`)는 `GoToResult` 를 직접 부르므로 영향 없다.
  보스가 쓰러져 결과 전환을 기다리는 중(`IsBossDefeatedServer`)에 전멸하면 리스폰하지 않는다(클리어).
- 튜토리얼에서 G10 전에 공용 목숨 0 전멸은 사실상 없다(G11 전 사망은 차감 없음 = Soul 로 자동 부활). 생기면 결과 화면(리스폰 조건 = G10 열림).
- 절차 맵은 **어디서 전멸하든** 패드가 씬에 있으면 패드 둘레로 리스폰한다(사양 "보스 진입 존 패드 밖").

### 3.3 PermanentDead 에서 되살리기

- `PlayerLifeCycleController.IsValidTransition` 은 그대로(PermanentDead 에서 나가는 전이 없음). **서버 전용 `TryRespawnAfterWipeOnServer()`** 만 예외 — 어떤 상태에서도
  `Unit.Revive()`(체력 가득·사망 래치 해제) → Alive(Alive 면 힐만). 호출자 = `PartyWipeRespawner` 하나.
  연출 잠금 중이면 거부(호출자가 보스 리셋에서 잠금을 먼저 푼다). 보류된 사망(`deferredDeathWhileLocked`)·사망 연출 예약(`deathResolutionPending`)을 지운다(R5).
- 부활 보호: `PlayerLandingProtection.BeginProtection(InvulnerabilityCause.Revive, 2초, applyStun: false)` — 튜토리얼 자동 부활과 같은 값.
- **R2 점검(2026-10-13, `LifeStateChanged`·`GameplayAccessChanged` 구독자 전수)**: 대부분 현재 상태만 본다 — `PlayerSoulController`(외형·장비·레이어·허트박스),
  `PlayerLifeInputPolicy`, `CameraTargetSwitcher`(관전 = 현재 상태 PermanentDead 여부), `PlayerCombatUiLifecyclePolicy`, `AssassinState`(Alive 를 떠날 때만), `SessionStatsTracker`(사망 수 = Alive→DeadPresentation 만),
  `TutorialDirector`(완전 사망 플래그 해제 — 참가자 수 복구), `TutorialTraitDirector`. **예외 = `PlayerCorpseController`** — PermanentDead 진입 때 놓은 시체를 아무도 거두지 않아
  PermanentDead → 다른 상태에서 시체를 숨기고 제거 래치를 푸는 분기를 넣었다. Play 확인 필수.

### 3.4 리스폰 지점

- 튜토리얼(`TutorialDeathRules.TryFindWipeRespawnPoints`): `TutorialArea.Corridor4` 격자(`SampleGrid`, 1.5m·안쪽 1m) → 최전방 = 복도4 마스크 기준 G9 앞면 여유(0.75m) 안 제외 →
  바닥·점유 검사(`TryResolveStandPoint` — 부활 지점과 같은 규칙) → `PartyWipeRespawnRules.TrySelectSpread`: 패드 범위 + 1m(`IsOnPad`) 제외, 복도4 중심에 가까운 순으로 **2m 간격**.
  모자라면 간격 무시 → 그래도 모자라면 고른 점 재사용(겹침은 최후 수단). 복도4 는 G10 배리어 **뒤**라 패드와 겹치지 않는 게 정상이지만 같은 패드 제외를 건다(C3 일관).
- 절차 맵(또는 복도4 를 못 찾은 튜토리얼): 각 `BossEnterTrigger` 의 `BoxCollider` bounds 를 패드로, 패드 + 1m + 1.5m 둘레 링(1m 간격, `RingCandidates`) → 바닥(트리거 바닥 높이 ±1m)·점유 →
  같은 `TrySelectSpread`(기준 = 패드 중심). 못 찾으면 **결과 화면**(설계 초안의 '스폰 지점' 폴백은 안 넣었다 — 절차 맵 스폰 지점 조회 API 가 없고, 보스방에 남겨 두면 갇힌다).
- 이동 = `PlayerFallRecovery.RelocateServer`(대기 중인 추락 복구 취소 포함 — 오너 권위면 오너 RPC).

### 3.5 개인 뒤 배리어(C2)

- 서버 `_backBarrierMasks`(TutorialDirector)는 비트를 **더하기만** 하고 Finished 뒤엔 갱신도 멈춘다. G10 을 지난 사람은 복도4 가 자기 뒤 배리어 너머라 그대로 두면 갇힌다.
- 리스폰 때 각자 마스크를 `TutorialBackBarrierRules.MaskWithFrontmost(Corridor4)`(= G1~G9, G10 제외)로 다시 쓴다 — `TutorialDirector.ResetBackBarrierMaskServer(clientId, Corridor4)`, 복제는 `PlayerState.BackBarrierMask`.
  `FrontmostPassedArea(mask) == Corridor4`, 로컬 콜라이더(`LocalColliderMask`)에 G10 없음 → 보스 진입 존(패드 방향) 앞길이 열려 있고 G9 뒤로는 못 돌아간다. 이후 G10 을 다시 지나면 기존 갱신 그대로.
- 🔴 **마스크를 되감기보다 먼저** 쓴다 — Finished 동안은 로컬 뒤 배리어가 꺼져 있다가 되감기로 '진행 중'이 되는 순간 켜지는데, 그때 G10 비트가 남아 있으면 복도4 앞을 막는다.
- `TutorialRevivePointRules`·패드 제외 규칙은 이 마스크를 그대로 쓰므로 리스폰 뒤 사망 부활 지점도 복도4 다(단계 BossEnter → 복도4, 최전방 복도4).

### 3.6 튜토리얼 되감기 · 목숨 차감

- `TutorialFlowModel.RewindForBossRetry()`(순수) — G10 열림일 때만: 단계 → BossEnter, G11 닫기(열림·교착·보류 비트), G1~G10·카운터·체크 그대로. **'앞으로만' 규칙의 유일한 예외**.
  `TutorialDirector.RewindForBossRetryServer()` 가 `_bossArrived` 해제 + Commit. 다음 보스방 도착 때 G11·Finished·`BossRoomArrived` 가 다시 난다.
- `TutorialDeathRules.ResumeForBossRetry()` — `_finished` 해제·대기 기록 비움·이동 표시 끔.
- **목숨 차감 규칙(사양 그대로)**: 차감 기준은 사망 시점 G11. 리스폰 뒤 **보스방 재도착 전 사망 = 차감 없음**(복도4 자동 부활), **재도착(G11 다시 열림) 뒤 사망 = 차감 대상**.
- 존 장부·몬스터·배리어 G1~G10 은 건드리지 않는다(이미 클리어). 안내 UI 흐름(복도·존4)은 `IsFinished` 가 다시 false 가 되어 다시 그릴 수 있다 — 복도 라운드는 전부 처리돼 새 창은 안 열린다(Play 확인 항목).

### 3.7 보스 리셋(경석 영역 — 최소 훅)

- 기존 기본 동작은 그대로, **새 메서드 호출 때만** 동작. 목록·호출 순서·건드린 줄: [handoff-boss-encounter-reset.md](handoff-boss-encounter-reset.md).
- **보스는 리셋하지 않고 디스폰 → 다음 도착 때 기존 경로가 새로 스폰**(체력·패턴·FSM·등장 연출 처음부터). 기존 오브젝트를 제자리 리셋하는 쪽보다 안전한 이유:
  `TwentyThreeBoss` 는 5천 줄 FSM 에 서버 상태(쿨·차징·웰즈·장판·드론·잡기·카운터)가 흩어져 있어 '처음 상태'를 빠짐없이 되돌리는 메서드를 새로 만들어야 하고,
  디스폰 정리(`OnNetworkDespawn` — 장판·드론·웰즈 정지·잡기 해제)와 스폰 초기화(`OnNetworkSpawn`)는 이미 매 판 검증된 경로다. 그래서 `TwentyThreeBoss.cs` 는 수정하지 않았다.
- 등장 연출 = Preparing → Descending → Impact(`Dialogue` 페이즈는 선언만). 새로 스폰되므로 다시 재생된다.

### 3.8 타이머 — 결정: 재가동 안 함

- 기존 규칙: 튜토리얼 타이머는 G8 에서 시작, 도착 시 Stopped, 만료 시 3초 경고 뒤 강제 이동(Soul 강제 부활 포함).
- **결정(2026-10-13, 구현)**: 재가동하지 않는다 — `BossTimerManager.StopForRetryServer()` 가 Stopped(HUD 0)로 두고 만료 재시도 플래그도 지운다.
  이유 — ① 리스폰 지점(복도4)에서 패드까지 거리가 짧아 시간 압박이 의미가 없다, ② 재가동하면 강제 이동이 리스폰 직후 특성 재선택(최대 4라운드, 3단계) 중에 터질 수 있다,
  ③ 재가동하려면 Idle 복귀 API 가 필요하다(경석 영역 확대). 보스방에 도착했던 판이면 이미 Stopped 라 바뀌는 게 없고, 만료(Expired) 상태였으면 강제 이동 재시도가 리스폰 직후 끌고 가지 않게 멈춘다.
  절차 맵에서 보스 도착 전 전멸이면 Running 이던 타이머도 멈춘다(리스폰 지점이 패드 옆).

## 4. 특성 초기화 · 재선택 (3단계 — 설계)

- 팝업: 리스폰 완료 신호로 각자 뷰가 연다. **서버 훅 = `PartyWipeRespawner.PartyWipeRespawned(IReadOnlyList<ulong>)`(2단계에서 남김)** — 받아서 리스폰 세대값(NetworkVariable)을 올리거나 대상 `ClientRpc`.
  문구는 `TutorialTraitTexts` 에 ID 추가(`ResetPromptTitle`·`ResetPromptYes`·`ResetPromptNo` — 기본값은 코드, 테이블 `TutorialTraitTexts.asset`). 루트에 `UiModalBlocker`.
- 응답: `[Rpc(SendTo.Server)] RequestTraitResetRpc(bool reset)` — 서버가 발신자 = 그 플레이어 소유자·리스폰 세대 일치·아직 응답 안 함을 검증(중복·지연 응답 무시).
- 예:
  1. 원복 — 스탯 특성은 `SourceIdOf(index)` 로 붙인 영구 상태이상 제거, 쿨다운 특성은 `cooldownHooks` 구독 해제. **현재 훅이 특성별 키가 아니다** → 특성 index 별로 보관하게 바꾼다.
  2. 모델 — `TutorialTraitSelectionModel` 에 `ResetPlayer(clientId)`(획득·제시 행 비우기, `RemovePlayer` 와 달리 등록 유지).
  3. 4라운드 연속 — 복도 흐름(`TutorialCorridorFlow`)과 별개로 그 플레이어만 라운드 4개를 차례로 연다(후보 3장·획득 제외 기존 규칙). 다른 사람은 기다리지 않는다.
  4. 선택 중 무적 `InvulnerabilityCause.TraitSelection` 토큰 — 기존 `SyncSelectionShields` 그대로.
- 아니오: 아무 것도 안 한다(팝업만 닫힘).
- 보스 진입 패드는 그동안 열려 있다 — 다른 사람이 먼저 패드에 서면 카운트다운이 시작될 수 있다(§6 열린 결정 D3).

## 5. 리스크

| # | 리스크 | 대응 |
|---|---|---|
| R1 | 갇힌 Soul(공용 풀 0) — 예전엔 없던 상태 | 전멸 판정에서 탈락으로 센다(1단계 구현·테스트). 관전 전환은 안 함. 전멸 리스폰이 전원 살린다 |
| R2 | PermanentDead 탈출 전이 신설 — 다른 소비자(관전 카메라·Corpse·Hurtbox)가 PermanentDead → Alive 를 처음 본다 | 구독자 전수 점검(§3.3) — 시체만 회수 분기 추가. Play 확인 필수 |
| R3 | 보스 리셋 훅이 경석 코드에 들어감 | 기존 경로 무변경 + handoff 문서. 결과 전환 예약 뒤(보스 사망)는 리셋 거부 |
| R4 | 튜토리얼 단계 되감기 — 단계·게이트는 앞으로만이라는 가정이 여러 곳에 있다(Corridor flow 의 자동 완료, 사망 규칙 `_finished`) | 되감기는 Finished → BossEnter 한 칸, G11 하나만. `StepChanged` 외부 구독자 0(2026-10-13 grep), `IsFinished` 소비자는 안내 UI·뒤 배리어·사망 규칙 — Play 확인 |
| R5 | 리스폰과 사망 연출 타이밍 경합(DeadPresentation 예약이 리스폰 뒤 PermanentDead 로 덮음) | `TryRespawnAfterWipeOnServer` 가 예약을 지운다 |
| R6 | MPPM 늦은 클라·NetworkList 복제 순서(특성 행) | 팝업 열림은 리스폰 세대값으로 — 늦게 받아도 한 번만 |
| R7 | 데이터 테이블 행 없음 | 프리팹 값 5 로 동작. 병합 Export 때 `partyLifeCount` 행 추가 |
| R8 | 보스방에 남은 폭탄(`BossBomb`, 웰즈가 던진 것)·드론 잔여물 | 보스 디스폰 정리가 장판·드론·웰즈를 멈춘다. 이미 던진 폭탄은 5초 퓨즈로 터지고 사라진다 — 리스폰 지점(복도4)은 보스방 밖이라 무해. Play 확인 |

## 6. 열린 결정

- **D1 — 갇힌 Soul 처리**: 지금 = Soul 유지 + 전멸 판정에서 탈락(2단계 리스폰이 전원 살린다). 대안 = 풀이 0 이 되는 순간 남은 Soul 을 PermanentDead(관전)로. 사용자 확인 권장.
- **D2 — 타이머 재가동**: ✅ 결정 = 재가동 안 함(§3.8).
- **D3 — 재선택 중 패드**: 한 명이 재선택 중일 때 다른 사람이 패드에 서면 카운트다운 시작 — 막을지(모두 응답할 때까지 패드 잠금) 둘지.
- **D4 — 절차 맵 리스폰**: 사양 = 보스 진입 존 패드 밖(구현). 절차 맵엔 튜토리얼 특성이 없으므로 팝업도 없음(추천).
- **D5 — 강제 개시 부활 순서**: 공용 풀이 모자랄 때 `ConnectedClientsList` 순회 순서(기존 코드) — 그대로 둠.
- **D6 — 옛 `defaultLifeCount` 필드**: 병합 Export 로 xlsx 행 정리 후 삭제.

## 7. 테스트 범위

- **EditMode(자동)**: 1단계 `PartyLivesRulesTests`(11건 — 목적지·차감·재충전·전멸·갇힌 Soul·차감 없는 Soul).
  2단계 `PartyWipeRespawnRulesTests`(11건, 플레이어 러너 — 결과/리스폰 분기·조건·보스 격파 거부·재충전 5·지점 선택 패드/여유/간격/폴백/실패·패드 둘레) ·
  `TutorialBossRetryTests`(8건, 튜토리얼 러너 — 되감기·재도착 뒤 차감 재개·G10 전 거부·멱등·마스크 복도4 최전방·로컬 콜라이더 G10 없음·부활 구간 복도4·존1).
  3단계: 특성 원복 모델·재선택 4라운드·응답 검증.
- **Play(사람)**:
  - 1단계: 솔로 — F10 부활로 공용 목숨이 5 → 4 … 0, 0 에서 사망 → PermanentDead → 결과 화면. MPPM 2인 — A 부활이 B 의 남은 목숨도 줄인다,
    풀 1 에서 둘 다 Soul → A 부활 → A 사망 → B(갇힌 Soul) 남은 채로 결과 화면. 튜토리얼 G11 전 사망은 풀이 줄지 않는다.
    ⚠️ 2단계 뒤로는 보스전 전멸이 결과 화면 대신 리스폰이다 — 결과 화면 확인은 보스 클리어·나가기로.
  - 2단계: 보스전 전멸 → 복도4 리스폰(겹치지 않음)·보스 처음부터(등장 연출 다시)·목숨 5·체력 가득·시체 없음·관전 카메라 해제·뒤 배리어에 안 갇힘(패드 쪽 진행 가능, 복도4 뒤로 못 감) /
    리스폰 뒤 보스방 재도착 전 사망 = 차감 없이 복도4 부활, 재도착 뒤 사망 = 차감 / 패드 다시 사용 가능 / 타이머 HUD 0 유지 / 보스 클리어·나가기 = 결과 화면 / MPPM 클라 화면도 같은지.
  - 3단계: 예/아니오 각각, 2인 중 한 명만 예.
