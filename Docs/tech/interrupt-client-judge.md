# 간파 클라 1차 판정 · 서버 2차 검증 (할 일 191)

> 상태: **1단계 초안** — `InterruptManager` 뼈대 + 세계 스냅샷 + 창 시간표 복제까지. 예측·판정·검증은 2~3단계, 문서 전체는 6단계에서 채운다.
> 계획(승인본): `_lane-artifacts/common/191-interrupt-client-judge/PLAN.md` (D1~D13).

## 1. 지금 들어간 것 (1단계)

- `Assets/1.Scripts/Player/InterruptJudge/InterruptManager.cs` — `NetworkManager.prefab` 에 붙는 MonoBehaviour(모든 피어 하나).
  프리팹에 NetworkObject 가 없어서 NetworkBehaviour·RPC 대신 `GlobalTimeScale` 처럼 named message(`InterruptManager.Window`)를 쓴다.
- **세계 스냅샷**: LateUpdate 에서 매 프레임 샘플 → 대상별 약 1초 링 버퍼(`InterruptSampleHistory<T>`), 시각으로 보간 조회(범위 밖 = false).
  - 플레이어(`Player.OnNetworkSpawn` 에서 등록): 위치·바라보는 방향·Alive·CC(Knockback/Restrained)·진행 중 대시(시작·방향·속도·남은 시간).
  - 대상(`InterruptTargetRegistry` 그대로 — 23호·중간보스·스파링 봇·허수아비): 위치·정면·간파 불가 상태(`IInterruptJudgeTarget`)·지금 창의 지목자.
- **창 시간표**(`InterruptWindowSchedule`): 서버가 `InterruptManager.ServerReportWindow` 알림으로 기록(창 id·공격 시작·열림·닫힘 예정·실제 닫힘·닫힘 종류·지목자)을 만들고 모든 클라에 복제.
  닫힘 종류 = 제때 / 조기(즉시 배포) / 늦음(23호 게이트에서 애니 Hit 이 늦으면 실제 닫힘 때 갱신 배포) / 길이 모름. 늦은 합류 클라에는 열린 창만 한 번 보낸다.
- **시각**: 전부 서버 시각(`NetworkClock.GameNow` — 슬로우·일시정지 제외). 로컬 게임초(`Time.timeAsDouble`) ↔ 서버 시각은 `InterruptServerClock`(오프셋 부드럽게 추종, 0.25초 넘게 튀면 바로 맞춤).
- 🔴 기존 예측·판정(`DashInterruptPredictor`·`InterruptWindowReplicaTracker`·서버 `ResolveHit`)은 이 데이터를 **읽지 않는다** — 동작 무변경. 삭제는 5단계.
- 확인용 로그: 프리팹 `InterruptManager.logWindows` 를 켜면 열림·닫힘·수신이 콘솔에 남는다(에디터 전용, 기본 끔).

## 2. 창 여닫는 곳 목록

### 2.1 23호 (`TwentyThreeBoss`) — 전부 `SetCounterWindow` 한 곳을 지난다 → 여기서 `ReportCounterWindowToManager` 로 연결

`SetCounterWindow` 는 복제값이 바뀔 때만 알린다. 이미 열린 채 길이를 넘겨 다시 열면(착지 창 도중 다음 콤보 타 등) 새 창으로 알린다.

| # | 위치 | 열림/닫힘 | 닫힘 예정 | 연결 |
|---|---|---|---|---|
| 1 | `StartAttack` — 창 여는 공격(돌진 등, 잡기 제외) | 열림 | `CounterWindowDuration`(= 선딜 게이트 타이머) · 공격 시작 = StartAttack 시각 | ✅ |
| 1' | 같은 곳 — 창 안 여는 공격·취약/제압 중 | 닫힘 | — | ✅ |
| 2 | `TryReleaseCounterAttack` — 게이트(타이머 끝 ∧ 애니 Hit) 발사 | 닫힘 | 애니가 늦으면 **늦은 닫힘** 갱신 배포 | ✅ |
| 3 | `ResetCounterWindup`(= `AbortAttackChain` — 상태 이탈·사망·체인 타임아웃·디스폰·간파 성공) | 닫힘 | 조기 닫힘 | ✅ |
| 4 | `PerformAttackHit` — 잡기 외 공격의 히트 | 닫힘 | — | ✅ |
| 5 | `PlayStateAnimation` — 서버에서 Attack 을 벗어남 | 닫힘 | — | ✅ |
| 6 | 잡기 `AttachGrabbed`(실제로 붙잡은 뒤) | 열림 | **길이 모름**(내려치기 진행에 따라 닫힘) | ✅ |
| 7 | 잡기 `TickGrabHold` — 잡힌 대상 소멸 | 닫힘 | — | ✅ |
| 8 | 잡기 `AdvanceGrabSlam` — 내려치기 사이 쓰러짐 / 마지막 타 직전 | 닫힘 | — | ✅ |
| 9 | 잡기 `ReleaseGrabbedPlayer`(놓기) | 닫힘 | — | ✅ |
| 10 | `BeginCharge` — 차징 시작 | 닫힘 | — | ✅ |
| 11 | `BeginRage` — 레이지 시작 | 닫힘 | — | ✅ |
| 12 | `OpenLandingCounterWindow` — 점프 착지 간파 창 | 열림 | `landingCounterWindow` | ✅ |
| 13 | `TickLandingCounterWindow` — 착지 창 만료 | 닫힘 | 제때 | ✅ |
| 14 | 레이드 `OnRaidGimmickCounter` — 기믹 간파 성공 | 닫힘 | 조기 | ✅ |
| 15 | 레이드 `EndRaidGimmick` — 기믹 종료 | 닫힘 | — | ✅ |
| 16 | 릴레이 `Prepare` 마지막 `relayCounterWindow` 초 | 열림 | 남은 준비 시간(`_gStepTimer`) · 지목자 없음(명단은 서버만) | ✅ |
| 17 | 릴레이 `Prepare` 끝 | 닫힘 | 제때 | ✅ |
| 18 | 연쇄 간파 `ChainMark` 끝 → `ChainWindow` | 열림 | `chainCounterWindow` · **지목자 = 지목 대상 clientId** | ✅ |
| 19 | 연쇄 간파 창 도중 지목 대상 쓰러짐 / 창 끝 | 닫힘 | 조기 / 제때 | ✅ |
| 20 | 밀어내기 `PnCharge` 송전탑 전부 파괴 → 무력화 | 열림 | `pushNorthStunTime` | ✅ |
| 21 | 밀어내기 `PnStun` 끝 | 닫힘 | 제때 | ✅ |

- 공격 시작 시각: 공격 상태(레이드 기믹 밖)면 `StartAttack` 시각, 기믹 창은 열림 시각과 같다.
- 간파 불가 상태: 서버 = `CounterBlockedByState`(취약·제압), 클라 = 취약 복제값만(제압은 복제값이 없다 — 제압 중엔 창이 안 열려 판정에는 영향 없다).

### 2.2 중간보스 (`MonsterCounterWindow` — SpinnerBot·GauntletBot·WallBot)

`MonsterCounterWindow.RecordTransition` 한 곳에서 알린다(열림 = 저작 길이 ÷ 공격속도, 만료 = 제때, `TryConsumeInterrupt`·`Close` = 조기). 공격 시작 시각은 모른다(= 열림 시각). ✅

### 2.3 로비 스파링 봇 (`TrainingSparringBot`)

`MonsterCounterWindow` 경로 그대로. 훈련 창 연장(T31)은 타이머를 느리게 흘리므로 `Counter.Open(windup)` 으로 닫힘 예정 = 예고 길이를 알린다(판정 타이머 무변경). 간파 불가 = 서버 취약·제압, 클라 복제값. ✅

### 2.4 허수아비 (`TrainingDummyInterrupt`)

스폰 시작 상태 + `OnStateChanged`(서버)에서 알린다. 무기한 창이라 **길이 모름**, 닫힘은 간파 성공(조기 아님 — 길이 모름 닫힘)·디버그 랩 게이트. ✅

## 3. 알려진 한계 (1단계)

- 닫힘 예정은 게임초 길이 기준 — 서버 슬로우 중에 열린 창도 `GameNow` 도메인이라 맞는다. 다만 슬로우가 끼면 실제 닫힘이 조금 어긋날 수 있어 닫힘은 항상 배포한다.
- 원격 플레이어의 대시·CC 칸은 그 피어에서 보이는 상태 기계 값이다(오너가 아니면 대시 상태가 없을 수 있다) — 2단계에서 오너 시전자만 쓰므로 문제 없음.
- 간파 상자 형상은 아직 샘플하지 않는다(2단계에서 판정 순간 `HitboxAnchorShape` 로 읽는다).
