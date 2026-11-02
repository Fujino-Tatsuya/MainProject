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

## 2.5 클라 판정 → 서버 검증 (3단계)

- **오너**: 누를 때 `PlayerSkillController` → `InterruptManager.BeginOwnerJudge`. 다음 샘플 뒤 누른 시각 표본으로 `Predict` 해 판정 시각(근접 = 누름 + Hit 지연, 대쉬 = max(대시 끝, Hit))을 잡고,
  그 시각 표본이 쌓이면 `Judge`. 성공이면 신고(`InterruptClaim` — 판정 시각·대상·창 id·공격 방향·내 위치) 1회, 실패면 안 보낸다(`OwnerJudged` 이벤트만). 시전 거부·강제 종료면 대기 판정을 버린다.
- **전송 경로**: 매니저 named message(`InterruptManager.Claim` / `.Approval` / `.Reject`) — 프리팹에 NetworkObject 가 없어 1단계 창 시간표와 같은 방식. 호스트 오너는 서버 처리를 직접 부른다.
- **서버 검증**(`InterruptClaimServer.Process`): 소유 확인 → `InterruptClaimValidator`(같은 `Judge` 를 서버 이력으로, 허용 오차 = 시각 RTT/2 + 여유 · 거리 · 각도. 창은 판정 시각 ± 시각 여유 — 조기 닫힘도 닫힘 + 여유까지 인정,
  위치·대상 표본은 판정 시각 ± 시각 여유 안 한 칸이라도 성공이면 통과) → 첫 승인 후보만 대상 규칙(`IInterruptClaimTarget.ServerAcceptsInterruptClaim` — 사망·23호 기믹) →
  창마다 장부(`InterruptClaimLedger`, 서버 도착 시각 기준 첫 승인 + 동시 인정 시간 안 = 동시, 경계 포함, 그 뒤 = 선착순 패배).
- **효과 나눔(D9)**: 첫 승인만 `ServerApplyInterruptSuccessEffect`(창 소비·그로기·제압 칸·섬광 — 서버 창이 이미 닫혔어도 준다), 승인마다 `ServerRecordInterruptSuccess`(결과 화면 통계 `CombatStatsEvents.ServerCounterSucceeded`)와
  시전자 간파 스킬의 `ServerInterruptSucceeded`(특성·튜토리얼 훅). 대상: 23호·SpinnerBot·GauntletBot·WallBot·스파링 봇·허수아비(허수아비는 통계 없음).
- **세션 중 대상은 피격으로 성공을 정하지 않는다**(`InterruptManager.ServerVerifiesClaims`). 오프라인(세션·매니저 없음)은 기존 피격 판정 그대로.
- **서버 간파 스킬 판정(ResolveHit)**: 앵커 오버랩은 피해만. 신고 결과를 **기다리지 않는다**(4단계에서 3단계 RTT+0.1초 대기 삭제) — 그때까지 승인이 없으면 시도 슬로우를 빗나감으로 정리한다.
  승인은 오는 순간(`ServerOnClaimVerdict`) 성공 기록을 내고, **첫 승인이면 성공 슬로우 사건**(전역 슬로우는 한 번 — 동시 성공자는 각자 오너 판정 cue 로 섬광)을 낸다.
  판정 전에 이미 승인된 대상에게는 이 피해에 간파 표식을 싣지 않는다(방금 든 취약에 간파 넉백이 또 들어가지 않게).
- **결과 배포**: 승인 = 같은 창 승인 목록 전체를 모든 피어에(`ApprovalReceived`), 거부 = 그 오너에게 이유와 함께(`RejectionReceived` — 범위·각도·창 닫힘·선착순·서버 거부). 연출 cue 연결은 4단계.
- **데이터 테이블 칸**(`SlowMotion` 시트, 대상 `NetworkManager`, 타입 `InterruptManager` Order 3): `verifyTimeSlack`(0.033)·`verifyDistanceSlack`(0.2)·`verifyAngleSlack`(5)·`simultaneousSeconds`(0.05). 병합 Export 전까지 프리팹 값.
- 테스트: `InterruptClaimServerTests`, 메뉴 `Tools/Tests/간파 판정·검증 EditMode 테스트 실행`.
- 한계: 오너 권한 이동이라 서버 이력의 시전자 위치가 보간 지연만큼 늦다 — 시각 여유 안에서만 따라잡는다(대쉬 간파 끝에서 거부가 늘 수 있음, D12 QA 비율로 판단). QA 봇 간파(164)는 이 브랜치에 없어 5단계.

## 2.6 연출 cue (4단계)

- 오너 연출은 매니저 cue 로만 움직인다: `InterruptOwnerCueRouter`(매니저 안 순수 판단) → 디렉터 `IInterruptOwnerCueSink`(`InterruptProductionDirector.OwnerJudgeCues.cs`, 재생 = `InterruptOwnerJudgeCuePlayer`).
  옛 오너 즉시 예측(`PlayerSkillController.PredictOwnerInterrupt` — 예측기·복제 창·대시 중 재예측)은 호출을 끊었다(삭제 5단계). 서버 시도 슬로우 강·약은 아직 옛 예측기.

| cue | 보내는 때 | 디렉터가 하는 일 |
|---|---|---|
| 시도(대상 없음/약/강) | 누른 프레임 LateUpdate — 누른 시각 표본으로 `Predict` | 기존 `LocalOwnerAttempt`(강 = 오너 암전·빛 모임). 이 시도는 내 서버 시도 슬로우 목적으로 걷지 않는다(D4) |
| 판정 성공 | 판정 시각 표본이 쌓인 LateUpdate — `Judge` 성공, **신고보다 먼저** | 서버를 기다리지 않고 성공 단계(강이었으면 섬광 오버레이 ②~⑥, 약·대상 없음이면 섬광 순간만). 뒤이어 오는 내 서버 성공 슬로우 Started 는 한 번 삼킨다(두 번 안 터짐) |
| 판정 실패 → 종료 | 같은 때 `Judge` 실패 | 결론 전 시도 걷기(0.2초). 약이면 걷을 것이 없다 |
| 서버 거부 → 종료 | 거부 수신(그 시전자·대상·창 id 가 판정 성공 상태일 때만) | 이미 튼 성공 섬광까지 즉시 끔(상태 기계 리셋·섬광 진행기 리셋·월드 섬광 정지) |
| CC 끊김 → 종료(D5) | 판정 전 간파 스킬이 정상 완료 아닌 이유(넉백·끌기·잡기 = `Cancelled`·사망·대시·안전망)로 끝남 | 대기 판정 버림 + 걷기. 일반 피해는 스킬을 끊지 않아 해당 없음. 판정 전 정상 완료(End)는 판정을 기다린다 |
| 판정 버림 → 종료 | 판정 시각이 이력(1초) 밖 | 걷기 |
| 승인 목록 | 승인 배포 수신(내가 목록에 있고 같은 창일 때) | D10 `keepCoSucceededBright`(기본 켬)면 나 말고 함께 성공한 캐릭터를 이번 암전에 남김(오너·보스는 시도 때부터). 내가 첫 승인자가 아니면 내 서버 성공 슬로우는 안 온다 |

- 구경하는 팀원 화면은 기존 규칙(첫 승인의 서버 성공 슬로우 → 월드 섬광). 동시 성공자의 두 번째 섬광은 그 본인 화면에만 나온다.
- 한계: 원격 클라는 암전이 섬광 순간(6프레임)에 꺼진 뒤 승인이 도착하는 일이 많아 D10 이 눈에 띄는 건 주로 호스트·저지연. 슬로우(전역)는 서버 승인 시점이라 원격 오너 화면에선 섬광보다 RTT/2 늦게 들어온다.
- 테스트: `InterruptOwnerJudgeCueTests`(같은 메뉴).

## 3. 알려진 한계 (1단계)

- 닫힘 예정은 게임초 길이 기준 — 서버 슬로우 중에 열린 창도 `GameNow` 도메인이라 맞는다. 다만 슬로우가 끼면 실제 닫힘이 조금 어긋날 수 있어 닫힘은 항상 배포한다.
- 원격 플레이어의 대시·CC 칸은 그 피어에서 보이는 상태 기계 값이다(오너가 아니면 대시 상태가 없을 수 있다) — 2단계에서 오너 시전자만 쓰므로 문제 없음.
- 간파 상자 형상은 아직 샘플하지 않는다(2단계에서 판정 순간 `HitboxAnchorShape` 로 읽는다).
