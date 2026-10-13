# 파티 공용 목숨 · 전멸 리스폰 · 특성 재선택 (할 일 30)

> 사양 확정: 2026-10-13(사용자). 담당: 은희(코어·네트워크·Player). 보스 리셋 훅은 경석 영역 — 최소 훅 + `Docs/tech/handoff-<주제>.md`.
> 진행: **1단계(이 문서 + 공용 목숨 풀) ✅ 구현** · 2단계(전멸 → 복도4 리스폰 · 보스 리셋) · 3단계(특성 초기화 팝업·재선택).
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
  `Temp_MultiGameRule` 이 없거나 스폰 전이면 예전 규칙(전원 PermanentDead).
- 튜토리얼 G11 뒤 자동 부활이 풀 0 으로 거부되면 경고 로그 후 Soul 로 남는다(`TutorialDeathRules.Revive`).
- 보스 강제 개시 부활(`BossTeleportManager.ReviveSoulsForForcedStart`)은 Soul 마다 공용 풀에서 1개씩 쓴다 — 풀이 모자라면 `ConnectedClientsList` 순회 앞쪽만 산다. 기존 코드 그대로.
- **데이터 테이블(xlsx Export 안 함 — 기획·병합 Export 때)**: Player 시트 · `#■ Temp_MultiGameRule` 구역 · 대상 열 `GameRule` 에 **`partyLifeCount` 행(5)** 추가.
  행이 없는 동안은 프리팹 값 5 를 쓴다(테이블 규칙 — 행 없음 = 인스펙터 값). 옛 `defaultLifeCount` 행은 코드 필드를 남겨 두었다(지우면 `필드 'X' 가 없다` 오류로 테이블 적용 전체가 멈춘다) —
  병합 Export 로 행이 정리된 뒤 필드를 지운다.

## 3. 전멸 → 복도4 리스폰 (2단계 — 설계)

### 3.1 흐름(서버)

```
전멸 확정(PartyWipeWatcher, 유예 2초)
 ├─ 결과 화면 경로(기존): 리스폰 대상이 아니면 GoToResult
 └─ 리스폰 경로(신규, 튜토리얼 G10 열림 또는 절차 맵 보스 진입 존 있음):
     1) 보스 인카운터 리셋(경석 훅) — 보스 디스폰·페이즈 Idle·결과 전환 예약 해제
     2) 보스방 이동 상태 정리 — 카운트다운 취소·도착 대기 해제(BossTeleportManager)
     3) 튜토리얼 상태 되감기 — _bossArrived·G11·Step Finished → BossEnter(또는 Corridor4) / 사망 규칙 다시 켜기
     4) 공용 목숨 재충전(TryRefillPartyLivesOnServer)
     5) 전원 Alive·체력 가득 — PermanentDead/Soul/DeadPresentation 모두
     6) 복도4 지점으로 순간이동(PlayerFallRecovery.RelocateServer) + 짧은 부활 무적
     7) 개인 뒤 배리어 재계산·복제
     8) (3단계) 각자 특성 재선택 팝업
     9) PartyWipeWatcher._fired 해제 — 다음 전멸도 다시 판정
```

### 3.2 리스폰 판정 — 누가 결과 화면을 막는가

- `PartyWipeWatcher` 에 서버 훅 하나: `static Func<bool> ServerTryRespawnInsteadOfResult`(null = 기존 결과 화면). 리스폰 디렉터가 걸고 성공하면 `_fired` 를 다시 내린다.
  `ServerDeathKeepsSoul`·`ReturnPointFallback` 와 같은 "기본값 = 기존 동작" 전역 훅 패턴.
- 보스 클리어(`BossEncounterDirector.TickResultTransition`)·나가기(`GameManager.GoToResultButton`)는 `GoToResult` 를 직접 부르므로 영향 없다.
- 리스폰 조건: 튜토리얼이면 G10 열림(보스 진입 단계 이후 전멸만 해당 — G10 전에는 사망이 차감 없음이라 풀 0 전멸이 거의 없다. 생기면 같은 리스폰). 절차 맵이면 보스 진입 존(패드)이 씬에 있음.

### 3.3 PermanentDead 에서 되살리기

- `PlayerLifeCycleController.IsValidTransition` 은 PermanentDead 에서 나가는 전이가 없다. **새 서버 전용 진입점 `TryRespawnAfterWipeOnServer()`** — 어떤 상태에서도
  `Unit.Revive()`(체력 가득·사망 래치 해제) → Alive. 일반 전이표는 그대로 두고 이 메서드만 예외(호출자 = 리스폰 디렉터 하나).
  연출 잠금 중이면 거부(호출자가 잠금 해제 뒤 재시도). 보류된 사망(`deferredDeathWhileLocked`)·사망 연출 예약(`deathResolutionPending`)을 지운다.
- Alive 였던 사람도 체력 가득(`Unit.Revive()` 는 서버 전용 풀힐 — 상태가 Alive 면 상태 전이 없이 힐만).
- 부활 보호: `PlayerLandingProtection.BeginProtection(InvulnerabilityCause.Revive, 2초)` — 튜토리얼 자동 부활과 같은 값.

### 3.4 리스폰 지점

- 튜토리얼: `TutorialArea.Corridor4` 구간 격자(`TutorialRevivePointRules.SampleGrid`) → 바닥·점유 검사(`TutorialDeathRules.TryResolveStandPoint` 와 같은 규칙) → 패드 범위 + 1m 제외(`IsOnPad`) → 플레이어 수만큼 서로 떨어진 점.
  복도4 는 G10 배리어 **뒤**라 패드와 겹치지 않는 게 정상이지만, 같은 패드 제외 규칙을 그대로 건다(C3 일관).
- 절차 맵: 보스 진입 존(패드가 있는 방)의 패드 범위 + 1m 밖 바닥 — `BossEnterTrigger` 콜라이더 bounds 기준 바깥 링에서 고른다. 못 찾으면 스폰 지점.
- 이동 = `PlayerFallRecovery.RelocateServer`(대기 중인 추락 복구 취소 포함).

### 3.5 개인 뒤 배리어(C2)

- 서버 `_backBarrierMasks`(TutorialDirector)는 비트를 **더하기만** 하고 Finished 뒤엔 갱신도 멈춘다. G10 을 지난 사람은 복도4 가 자기 뒤 배리어 너머라 복도4 에 리스폰하면 갇힌다.
- 리스폰 때 각자 마스크를 **복도4 가 최전방인 값**(= G10 비트 제거, G9 까지 유지)으로 다시 쓰고 `PlayerState.BackBarrierMask` 로 복제 — 신규 `TutorialDirector.ResetBackBarrierMaskServer(clientId, TutorialArea front)`.
  `TutorialBackBarrierRules.FrontmostPassedArea(mask) == Corridor4` 가 되게 순수 함수로 계산(테스트 대상). 이후 G10 을 다시 지나면 기존 갱신 그대로.
- `TutorialRevivePointRules`·패드 제외 규칙은 이 마스크를 그대로 쓰므로 리스폰 뒤 사망 부활 지점도 일관된다.

### 3.6 튜토리얼 되감기

- `TutorialDirector`: `_bossArrived` 해제, G11 닫기, Step `Finished → BossEnter`. 현재 게이트 닫기 API·단계 되돌리기가 없다(순서 규칙상 앞으로만) — 신규 서버 전용 `RewindForBossRetryServer()` 하나에 몰아 둔다(`TutorialFlowModel` 에 순수 되감기 규칙 + 테스트).
- `TutorialDeathRules`: 도착·Finished 로 `_finished` 가 켜져 있다 — 되감기 신호(`StateChanged`)에서 다시 켠다(G11 전 사망 = 차감 없음 규칙이 다시 산다. 사양 "G11 전 사망 차감 없음" 과 일치).
- 존 장부·몬스터·배리어 G1~G10 은 건드리지 않는다(이미 클리어).

### 3.7 보스 리셋(경석 영역 — 최소 훅)

조사 결과(2026-10-13) 리셋 API 가 없다. 기존 기본 동작은 그대로 두고 아래만 추가 — handoff 문서 `Docs/tech/handoff-boss-retry-reset.md`(2단계에서 작성).

| 클래스 | 현재 | 필요한 훅 |
|---|---|---|
| `BossEncounterDirector` | `AbortEncounterServer` 가 보스 디스폰·FailedSafe 까지 하지만 `_defeatHandled`·`_resultTransitionAt`·`_bossMonster` 는 안 지운다. 새 도착은 Idle/FailedSafe 에서만 | `ResetForRetryServer()` — 잠금 해제·`DespawnBoss`·모든 서버 필드 초기화·페이즈 Idle. 보스가 죽은 뒤(결과 전환 예약)는 리스폰 대상이 아니므로 거부 |
| `TwentyThreeBoss` | 디스폰 시 장판·드론·우물·잡기 정리(`:416-440`) | 없음 — 다음 도착 때 새로 스폰되므로 체력·패턴은 처음부터 |
| `BossTeleportManager` | `CancelCountdown` private, 시퀀스 카운터는 재진입 가능 | `ResetForRetryServer()` — 카운트다운·도착 대기 취소, 점유 재보고 |
| `BossTimerManager` | `StartTimerServer` 는 Idle 에서만, Stopped/Expired 에서 되돌릴 API 없음 | `ResetToIdleServer()` — 2단계에서 아래 결정에 따라 호출 |

- **등장 연출**: `Dialogue` 페이즈는 선언만 있고 쓰지 않는다. "등장 연출" = Preparing → Descending → Impact. 새로 스폰되므로 다시 재생된다.

### 3.8 타이머 재가동 — 추천안

- 기존 규칙: 튜토리얼 타이머는 G8 에서 시작, 도착 시 Stopped, 만료 시 3초 경고 뒤 강제 이동(Soul 강제 부활 포함).
- **추천: 재가동하지 않는다**(Stopped 유지 → HUD 0). 이유 — ① 리스폰 지점(복도4)에서 패드까지 거리가 짧아 시간 압박이 의미가 없다,
  ② 재가동하면 강제 이동이 리스폰 직후 특성 재선택(최대 4라운드) 중에 터질 수 있다, ③ 타이머 Idle 복귀 API 를 새로 만들어야 한다(경석 영역).
  사용자가 원하면 대안 = `ResetToIdleServer()` + `StartTimerServer()` 로 남은 시간 없이 처음부터.

## 4. 특성 초기화 · 재선택 (3단계 — 설계)

- 팝업: 리스폰 완료 신호(서버 → 각 클라 `ClientRpc`/NetworkVariable 세대값)로 각자 뷰가 연다. 문구는 `TutorialTraitTexts` 에 ID 추가
  (`ResetPromptTitle`·`ResetPromptYes`·`ResetPromptNo` — 기본값은 코드, 테이블 `TutorialTraitTexts.asset`). 루트에 `UiModalBlocker`.
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
| R1 | 갇힌 Soul(공용 풀 0) — 예전엔 없던 상태 | 전멸 판정에서 탈락으로 센다(1단계 구현·테스트). 관전 전환은 안 함 |
| R2 | PermanentDead 탈출 전이 신설 — 다른 소비자(관전 카메라·Corpse·Hurtbox)가 PermanentDead → Alive 를 처음 본다 | 2단계에서 `LifeStateChanged` 구독자 전수 점검, 상태 소비자는 대부분 현재 상태만 봐서 안전할 것으로 보임 — Play 확인 필수 |
| R3 | 보스 리셋 훅이 경석 코드에 들어감 | 기존 경로 무변경 + handoff 문서. 결과 전환 예약 뒤(보스 사망)는 리셋 거부 |
| R4 | 튜토리얼 단계 되감기 — 단계·게이트는 앞으로만이라는 가정이 여러 곳에 있다(Corridor flow 의 자동 완료, 사망 규칙 `_finished`) | 되감기는 Finished → BossEnter 한 칸, G11 하나만. 영향 구독자를 2단계 시작 때 목록화 |
| R5 | 리스폰과 사망 연출 타이밍 경합(DeadPresentation 예약이 리스폰 뒤 PermanentDead 로 덮음) | `TryRespawnAfterWipeOnServer` 가 예약을 지운다 |
| R6 | MPPM 늦은 클라·NetworkList 복제 순서(특성 행) | 팝업 열림은 리스폰 세대값으로 — 늦게 받아도 한 번만 |
| R7 | 데이터 테이블 행 없음 | 프리팹 값 5 로 동작. 병합 Export 때 `partyLifeCount` 행 추가 |

## 6. 열린 결정

- **D1 — 갇힌 Soul 처리**: 지금 = Soul 유지 + 전멸 판정에서 탈락. 대안 = 풀이 0 이 되는 순간 남은 Soul 을 PermanentDead(관전)로. 사용자 확인 권장.
- **D2 — 타이머 재가동**: 추천 = 재가동 안 함(§3.8).
- **D3 — 재선택 중 패드**: 한 명이 재선택 중일 때 다른 사람이 패드에 서면 카운트다운 시작 — 막을지(모두 응답할 때까지 패드 잠금) 둘지.
- **D4 — 절차 맵 리스폰**: 사양 = 보스 진입 존 패드 밖. 절차 맵엔 튜토리얼 특성이 없으므로 팝업도 없음(추천).
- **D5 — 강제 개시 부활 순서**: 공용 풀이 모자랄 때 `ConnectedClientsList` 순회 순서(기존 코드) — 그대로 둠.
- **D6 — 옛 `defaultLifeCount` 필드**: 병합 Export 로 xlsx 행 정리 후 삭제.

## 7. 테스트 범위

- **EditMode(자동)**: 1단계 `PartyLivesRulesTests`(11건 — 목적지·차감·재충전·전멸·갇힌 Soul·차감 없는 Soul).
  2단계 예정: 뒤 배리어 마스크 재계산(복도4 최전방), 튜토리얼 되감기 규칙, 리스폰 지점 선택(패드 제외·간격). 3단계: 특성 원복 모델·재선택 4라운드·응답 검증.
- **Play(사람)**:
  - 1단계: 솔로 — F10 부활로 공용 목숨이 5 → 4 … 0, 0 에서 사망 → PermanentDead → 결과 화면. MPPM 2인 — A 부활이 B 의 남은 목숨도 줄인다,
    풀 1 에서 둘 다 Soul → A 부활 → A 사망 → B(갇힌 Soul) 남은 채로 결과 화면. 튜토리얼 G11 전 사망은 풀이 줄지 않는다.
  - 2단계: 보스전 전멸 → 복도4 리스폰·보스 처음부터·목숨 5·체력 가득·뒤 배리어에 안 갇힘 / 보스 클리어·나가기 = 결과 화면.
  - 3단계: 예/아니오 각각, 2인 중 한 명만 예.
