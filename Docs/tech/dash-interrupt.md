# 대쉬 간파 — 대시 시작 직후 우클릭으로 대시하며 간파 준비, 대시 끝 지점에서 판정

> 할 일 115(2026-10-27, 브랜치 `feature/DashInterrupt`). 구현 커밋 C1 `07a85d46` · C2a `f0f56ee2` · C2b `a5af6d8b`. C4(우클릭 → 대시 전환)는 할 일 132 정정2(2026-10-28, 사용자 지시)로 폐기 — §2.1.
> 설계안: `_lane-artifacts/MainProject-Worktree2/115/115-설계안.md`(레포 밖, §10·§11·§12 확정 절이 우선). 문서와 코드가 다르면 코드가 기준이다.
> 연출 디렉터는 [interrupt-presentation.md §6](interrupt-presentation.md#6-간파-연출-디렉터--interruptproductiondirector-할-일-118), 입력 거부 피드백은 [skill-reject-feedback.md](skill-reject-feedback.md).

**한 줄 정의**: **대시 → 우클릭 순서로만**, 대시 시작 후 짧은 창(기본 0.1초) 안에 우클릭을 누르면(§1·§2) 대시는 그대로 진행하면서(이동·무적·충돌 그대로) 상체만 간파 준비 자세를 취하고, **대시가 끝나는 지점·대시 방향**에서 간파를 판정한다.

- 대상: 우클릭 슬롯이 간파 스킬(`PlayerInterruptSkillBase`)인 캐릭터 — 팔라딘·어쌔신·거너. 팔라딘 E 처럼 우클릭이 다른 스킬로 바뀐 동안은 대상이 아니다.
- `dashInterruptWindow = 0` 이면 기능이 꺼지고 지금과 완전히 같다.

## 1. 입력 창 규칙 (오너 입력 주체 — `PlayerDashState.TickDashInterruptInput`)

- 창 판정 = **누른 시각**(선입력 버퍼 기록 시각, 버퍼가 꺼져 있으면 이번 프레임 눌림)이 대시 시작 후 `[0, 창]` 안인가(경계 포함). 대시 시작 전에 누른 입력은 창 밖.
  - 누른 시각 읽기: `PlayerInputReader.TryGetPressTime` → `PlayerInputBuffer.TryGetRecordedTime`(버퍼를 건드리지 않는 읽기 전용).
- **창 안 입력만 대쉬 간파로 소비한다.** 창 밖 입력·우클릭이 간파 스킬이 아닌 경우는 Dash 상태가 손대지 않는다 — 평타·스킬 중 입력과 같이 기존 선입력 버퍼(0.2초)·우선순위 규칙대로 대시가 끝난 뒤 처리된다([skill-reject-feedback.md §0.2](skill-reject-feedback.md)).
- 판정 순서(`DashInterruptRules.EvaluateInput`): 입력 없음 → 창 밖 → 간파 스킬 아님 → 이미 요청 → 차단 효과 → 쿨타임 → 요청.

| 결정 | 처리 |
|---|---|
| `OutOfWindow` · `NotInterruptSkill` | 버퍼 그대로(대시 후 처리) |
| `ConsumeOnly` — 이 대시에서 이미 요청했거나 스킬 요청 응답 대기 중(`IsBusy`) | 버퍼에서 지우기만, 재요청 없음 — **대시 1회당 대쉬 간파 1회** |
| `RejectByEffect` — `BlocksSkill` 또는 `BlocksInterrupt` | 기존 차단 효과 처리(`RejectByEffect` → `ActionBlockedByEffect` 1회 + 버림) |
| `RejectByCooldown` | `NotifyCooldownRejected` → `CooldownRejected` 1회 + 버림 |
| `Request` | `PlayerSkillController.TryUseDashInterrupt(대시 방향)` — 성공이면 소비, 사전 검증 거부면 버림(이벤트 없음) |

## 2. 상태 흐름

```
Idle/Move ──Space──▶ Dash(대시 시작)
                      │ 창 안 우클릭 → 서버 승인
                      ▼
          Dash + 실린 간파(PlayerDashState.Attach)     ← FSM 은 계속 Dash
            · 쿨 시작·피해 스냅샷·OnServerStart(시도 사건) — 일반 간파와 같음
            · 0층 = 대시 포즈 그대로, 상체 레이어 DashInterruptUpper 에서 간파 준비 클립
            · 대시 중 Hit 이벤트 → 판정 보류(pendingHit), 판정 전 End → 판정 뒤로(pendingEnd)
                      │ 대시 끝(시간 만료 · 유닛에 막혀 조기 종료)
                      ▼
          Skill(간파) 상태로 바로 전이(BeginSkillFromDash — Idle/Move 안 거침, CanUseSkill 안 봄)
            · 0층이 상체 레이어의 간파 상태를 이어받음(HandOffDashInterruptToBase)
            · 보류된 Hit 이 있으면 → 즉시 ResolveHit(= 대시 끝 지점 판정)
            · 아직 Hit 전이면 → 클립 Hit 이벤트(남은 준비 시간)에서 판정
            · 이후 End 이벤트·안전망으로 종료 — 일반 간파와 같음, 무적 없음
          대시가 끊김(넉백·그랩·사망·CC·서버 취소 EndDash) → 간파 강제 종료(아래 §7)
```

- **판정 시각 = max(대시 끝, 평소 Hit)** (`DashInterruptRules.GetResolveTime`) — 평소 간파보다 빠르지 않고, 대시가 준비 시간보다 짧으면 남은 준비 시간만큼 기다린다.
- 준비 시간은 **클립 Hit 이벤트 위치**가 정한다(SO 타이머 칸 없음).
- 대시 끝에 실린 간파가 이미 끝났으면(`EndsIntoSkill` 거짓) 기존대로 Idle/Move.
- 안전망 시간은 승인 때 시작했으므로 Skill 전이 때 실려 있던 시간만큼 늦춘다(`CompleteDashInterruptHandOff`, `activeDashAttachTime`).

## 2.1 우클릭 → 대시 순서 — 대쉬 간파 아님 (C4 전환 폐기, 할 일 132 정정2)

2026-10-28 사용자 지시(게임플레이 변경)로 우클릭 → 대시 전환(C4 — 진행 중인 일반 간파를 대시에 옮겨 싣던 경로)을 없앴다. 대쉬 간파는 **대시 → 우클릭 순서만**이다.

우클릭(일반 간파) 중 대시를 누르면 **기존 일반 규칙**대로다:
- 간파 스킬(팔라딘·어쌔신·거너 우클릭)은 `CanBeCanceledByDash` 를 덮지 않아(기본 false) 대시로 끊을 수 없다 → `PlayerStateController.Tick` 이 그 대시를 실행하지 않는다.
- 대시 입력은 선입력 버퍼(0.2초)에 남아, 간파가 끝나 Idle/Move 로 돌아올 때 아직 살아 있으면 **일반 대시**로 나가고 지나면 버려진다([skill-reject-feedback.md §0.2](skill-reject-feedback.md)). 그 대시 뒤 창 안 우클릭은 다시 §1 의 대쉬 간파 대상이다(쿨이면 쿨 거부).
- 간파는 끊기지 않고 끝까지 진행한다(판정·연출·쿨 그대로).

## 3. 위치·방향·비용·무적

- **위치·방향**: 판정은 기존처럼 플레이어 자식 `HitboxAnchor` 오버랩 — 대시 끝 위치·대시 방향이 자연히 기준이 된다. 승인 방향 = 서버 대시 상태의 방향(`PlayerDashState.Direction`), 조준 방향 회전 스냅·고정 거리 조준 재투영 없음.
- **비용**: 대시 1충전(대시 자체) + 간파 쿨타임(승인 순간 시작). 대쉬 간파용 추가 자원은 없다.
- **무적**: 대시 구간만. 서버 = 대시 승인 때 남은 대시 시간만큼 토큰, 오너 예측 = 상태가 Dash 인 동안. Skill 로 넘어간 뒤 간파 판정 구간에는 무적이 없다.

## 4. 네트워크 (권한 유지: 이동·입력 = 오너, 승인·판정·피해 = 서버)

| 지점 | 내용 |
|---|---|
| `RequestUseSkillRpc` | 인자 **`bool dashInterrupt`** 추가. 이 플래그일 때만 서버가 대쉬 간파 승인 경로를 탄다. `CreateStatReport` → 2차 검증 경고·간파 지연 측정(`ServerSetLagPress`)은 그대로 |
| 오너 선검증 | 차단 효과·쿨타임은 `PlayerDashState` 가 먼저 거른다(피드백 1회). 원격 오너는 RPC 전에 `CanApproveSkill(dashInterrupt: true)` 를 자기 상태로 돌린다 — 창은 이미 누른 시각으로 봤으므로 여기서는 기능 켜짐만 본다 |
| 서버 최종 검증 | `PlayerSkillApprovalRules.EvaluateDashInterruptPreconditions` — 빈 슬롯 → 슬롯 경합 → 실행 중(실린 간파 포함) → 쿨 → 사망 → (Interrupt 슬롯·간파 스킬·서버 FSM Dash·차단 효과 아님) → **서버 대시 경과 ≤ 창 + `dashInterruptServerTolerance`**(`IsServerElapsedAccepted`, 경계 포함). 창 밖이면 `PlayerSkillApproval.DashWindow`. 그 뒤 스킬 `CanUse` 는 기존과 같음 |
| 승인 처리 | `StartSkillServer(dashInterrupt)` — `BeginSkill` 대신 `AttachDashInterrupt`, `ArmDashAttach(예상 대시 끝 오프셋)` → `OnServerStart` → `PlaySkillPresentation(dashInterrupt)`(0층 CrossFade 대신 상체 레이어) |
| `PlaySkillClientRpc` | 인자 **`bool dashInterrupt`** 추가. 원격 오너: 아직 Dash 면 부착 + 상체 레이어, 대시가 이미 끝났으면 일반 Skill 진입·0층 재생. 관찰자: 0층을 건드리지 않고 오너 애니메이터 복제(NetworkAnimator)를 따른다 |
| 거부 | 기존 `RejectSkillClientRpc`(오너 요청 대기 해제). 대시는 그대로 진행 |
| 강제 종료 | 서버 `HandleDashInterruptAborted` → 기존 `EndSkillClientRpc` |
| 새 RPC | **없음** |

- 원격 오너의 서버 대시는 대시 요청 도착 때 시작하므로 서버 기준 경과가 오너 경과와 비슷하다 — 허용오차(기본 0.1초)는 그 차이를 받는다.
- 호스트 오너·오프라인은 같은 프레임에 `StartSkillServer` 로 바로 승인된다.

## 5. 118 디렉터 사건 연결 — 새 경로 없음

- **시도**: 승인 순간 `OnServerStart` → `ServerAttempt(예측 3상태)` 그대로. 예측은 근접·대쉬 공용 `DashInterruptPredictor`(할 일 132 — [interrupt-presentation.md §0.2](interrupt-presentation.md#02-흐름)):
  - 판정 위치 = **예상 대시 끝** = 시전 위치 + 대시 방향 × 대시 속도 × 남은 대시 시간(`DashInterruptRules.GetPredictedEndOffset`), 판정 시각 = max(예상 대시 끝, 평소 Hit).
  - 대상 = **판정 앵커를 공격 방향(근접 = 시전 방향, 대쉬 = 대시 방향)으로 돌려 판정 위치에 놓은 오버랩**(`HitboxAnchorShape` — 서버 `ResolveHit` 의 `OverlapHitboxAnchor` 와 같은 형상 계산·같은 질의, 할 일 139-3)에 걸린 Unit 중 `InterruptTargetRegistry` 캐시에 있는 것(창 소유자) + `InterruptTargetGeometry.FacesAttacker`(대상 정면 허용각 — 기존 판정값 그대로, 정면 판정 없으면 180). 밖이면 '대상 없음' = 슬로우 없음.
  - 오너 HUD 강조 사거리(`HasTargetInOmniRange`)는 '돌면 닿는 거리'(앵커 먼 모서리) 표시라 예측보다 넓다 — 강조가 켜져도 그 방향이면 대상 없음일 수 있다.
    대상까지 거리는 실제 콜라이더 표면 수평 거리(`InterruptOmniRangeModel.HorizontalDistanceToCollider`, 할 일 139-4 — 아래 AABB 착시).
  - 서버 = 승인 시점 값(`ArmDashAttach` 가 방향·속도·서버 남은 시간). **오너 클라 = 우클릭을 누른 순간**으로 되돌린 같은 입력(`InterruptAttemptPrediction.AtPress`) — 강이면 그 자리에서 오너 암전·빛 모임.
  - 판정 오버랩은 옮기지 않는다(실제 위치).
- **예측 규칙 보정(할 일 139-2, §10.2 표본 27개 근거)** — `DashInterruptPredictor` 공용 규칙이라 ①은 근접 간파에도 같이 적용된다, ②는 대쉬 간파만:
  - **① 틀린 창 길이 추정 버리기**: 창이 지금 열려 있는데 열린 뒤 경과 > 추정 길이(관측 중앙값, `IsWindowEstimateStale`)면 추정이 틀린 것으로 보고 버린다(남은 창 = 모름 -1). 그때 판정까지 남은 시간 ≤ **`StaleWindowMaxTimeToResolve` = 0.4초**면 강(`Success`), 길면 약(`WindowClosesBeforeResolve`). 무기한 허수아비 창(§10.1 ⚠️)의 표본 seq 5·8·15·19·26 이 약 → 강. seq 22 는 경과 0.924 < 추정 1.243 이라 해당 없음(남은 0.319 < 판정까지 0.345 — 약 그대로).
  - **② 대시 중 재예측(약 → 강만)**: 누른(서버 = 승인) 순간 대상은 범위 안인데 창이 닫혀 약(`WindowClosedNow`, `ShouldRepredictDuringDash`)이었으면, 그 대시 동안 매 프레임 같은 예측기로 다시 본다(`PlayerDashState.Tick` → `PlayerSkillController.TickDashInterruptReprediction`). 입력 = 지금 위치·남은 대시·창, 준비 기준점은 처음 시도의 t0(`InterruptAttemptPrediction.ForReprediction`). 강이 되는 순간 한 번:
    - 오너 = 복제 창으로 → `InterruptProductionDirector.LocalOwnerUpgrade` → `InterruptOwnerPresentation.UpgradeToStrong`(암전·빛 모임이 늦게 켜짐). 올린 뒤 서버의 약 목적(승인 때 예측)이 늦게 와도 내리지 않는다. 서버 강이 먼저 와 있었어도 켠다.
    - 서버 = 서버 창으로 → `PlayerInterruptSkillBase.TickServerDashReprediction` 이 **강 시도 슬로우를 다시 건다**(`GlobalTimeScale` 은 마지막 발동이 이긴다 — 기존 슬로우 배포로 전 피어가 받음, 새 RPC 없음). 레이어가 거부하면(0) 약 슬로우 번호를 그대로 둔다.
    - 강 → 약 내리기 없음. 근접 간파는 재예측하지 않는다. 표본 seq 16(누를 때 닫힘 → 0.04초 뒤 열림 → 서버 성공).
  - ~~③ 겹침이면 공격 방향 생략~~(`OverlapDistance` 0.05m) — **할 일 139-3 에서 없앴다**. 아래 앵커 형상 검사가 seq 10(거리 0·각도 133° · 서버 성공)을 그대로 대상 있음으로 본다(상자 가까운 면이 겹친 허수아비 캡슐에 닿는다).
- **예측 사거리 = 판정 앵커 실제 모양(할 일 139-3)** — 원인: 예전 대상 검사는 **전방위 사거리**(앵커를 어느 방향으로 돌려도 닿는 최대 거리 = 상자 먼 모서리) + 공격 방향 반평면(±90°)이라, 실제 판정(공격 방향에 놓인 상자)보다 정면은 모서리까지·옆은 반평면 끝까지 넓었다 → 예측 강 + 서버 실패.
  - 앵커 값(프리팹 저작값, Armature 직속·회전 0 — 팔라딘 -0.1°): 어쌔신·거너 상자 중심 z 0.9 · 반폭 0.7 · 반길이 0.7 → **정면 도달 1.6m · 옆 0.7m · 전방위 1.746m**. 팔라딘 중심 z 0.9 · 반폭 0.8 · 반길이 0.8 → **정면 1.7m · 옆 0.8m · 전방위 1.879m**. 거리는 플레이어 위치 → 대상 표면.
  - 새 규칙: 지금 앵커 형상을 플레이어 위치 수직축으로 돌려 공격 방향을 향하게 하고(`HitboxAnchorShape.Aimed`) 판정 위치로 옮겨(`Offset`) 서버와 같은 `Overlap` 질의. 걸린 Unit 중 캐시 대상만, 그리고 대상 정면 허용각(23호). 공격 방향 반평면·사거리 비교는 없어졌다. 허용 오차 칸 없음. 판정 동작은 그대로(`OverlapHitboxAnchor` 가 같은 형상 함수를 쓸 뿐).
  - 야간 QA 봇(23호) 로그의 강 예측 surf 1.71~1.74m(봇 reachM 1.75)·표본 seq 8(옆 1.31m·75°) 은 이제 대상 없음.
  - **AABB 거리 착시(할 일 139-4)** — 예측은 앵커 오버랩이라 영향 없었지만, HUD 강조와 랩 CSV 거리 열(`InterruptTargetRegistry.Entry.TryGetHorizontalDistance`)은 대상 콜라이더 **AABB** 까지 재서 대상이 월드 대각선에 있으면 최대 r(√2−1) 짧았다(23호 캡슐 r 1.7 → 0.70m, 거너 '1.73m' = 실제 2.1~2.4m). 이제 둘 다 `HorizontalDistanceToCollider` — `Collider.ClosestPoint`(질의점 높이 = 대상 AABB 중심 높이, 안이면 0, 트리거 포함). 비볼록 MeshCollider 등 ClosestPoint 미지원은 AABB 표면 근사. 테스트 `Assets/1.Scripts/Player/Editor/InterruptOmniRangeModelTests.cs`.
  - **보정 랩 재현**(§10.2 `InterruptCalibrationLab`, 어쌔신 또는 거너, `includeMelee` 켬 — 근접 간파): ⓐ 허수아비 정면에서 표면 1.6~1.75m(허수아비 중심에서 2.1~2.25m) 떨어져 허수아비 쪽으로 우클릭 · ⓑ 허수아비가 조준 방향과 75° 옆, 표면 1.3m 에서 우클릭. **수정 전 = 강(암전) + 서버 실패, 수정 후 = 대상 없음(연출 없음)**. 대조: 정면 1.5m·옆 75° 0.55m 는 강 + 서버 성공. 팔라딘은 정면 1.7~1.88m 띠가 같은 증상이다.
- **창 길이 출처(할 일 139-6)** — 예측 입력의 창 길이(`WindowExpectedDuration`)는 **로컬로 아는 이번 열림 길이 우선, 모르면 관측 중앙값**(`InterruptWindowHistory.TryGetTypicalOpenDuration`) 폴백. 예측기 공식(남은 창 = 길이 − 경과)은 그대로이고 입력만 바뀐다.
  - 해석기 = `InterruptWindowLengthResolver`(`Player/Skill`) — 대상 종류마다 어댑터(이미 공개된 값·클라에 있는 SO/프리팹 데이터·이미 오는 복제값/RPC 만 읽음). 값 = 게임초, **무기한 = +∞**(남은 창 +∞ → 창 닫힘으로 약 없음, 추정 버리기 ①도 안 탐), **모름 = -1**. 서버(`InterruptLagMonitor.EstimateWindow`)·오너(`InterruptWindowReplicaTracker.Estimate`) 같은 해석기. 보스·몬스터 코드는 고치지 않는다 — 보스 쪽 인터페이스(`IInterruptWindowReplica`)에 칸을 늘리지 않는다. 새 전송 없음.
  - 지금 출처(단계 1): **허수아비** — 게이트 없음(본게임 맵씬·튜토리얼) = 무기한(모든 피어) · 랩 '무기한' = 무기한 · 랩 '주기' = 열림 길이(`DummyInterruptWindowSchedule.OpenWindowLength`, 닫힘 0 = 무기한). 랩 게이트 값은 **서버(솔로 호스트 포함)만** 쓴다 — 원격 클라는 호스트 패널의 Play 중 변경이 복제되지 않아 모름(-1) → 관측 중앙값.
  - 단계 2: **중간보스 SpinnerBot·GauntletBot·WallBot + 로비 `TrainingSparringBot`** = `MonsterCounterWindow.WindowDuration`(저작값 ÷ 몬스터 SO `attackSpeed` — 프리팹 Spinner 1.5·Gauntlet 2.0·Wall 1.5·스파링 1.5(Spinner 변형), 전부 ÷1). 네 봇 모두 이 길이로 열고 같은 길이의 단계 타이머로 닫는다(Spinner `_phaseTimer = WindupDuration`, Wall `_phaseTimer = WindowDuration`, Gauntlet 창 타이머 만료, 스파링 `_windupLeft`). 공격속도는 `MonsterBase.AttackAnimSpeed` = SO 값 직접(버프·디버프·복제 없음) → **서버·원격 클라 같은 값(정확)**. 어댑터는 봇 타입으로 한정 — 23호 프리팹에도 `MonsterCounterWindow` 가 붙어 있지만 23호 창은 자기 계통이라 맡지 않는다. 길이 외 조기 닫힘(성공·피격·그로기·리쉬·사망 등 Attack 이탈, Gauntlet 타격 순간 `PerformAttackHit`, 스파링 넉백·제압·초기화)은 그대로 복제 bool 로만 안다.
  - 단계 3: **23호** = `TwentyThreeInterruptWindowLength`(`Player/Skill`) — 창이 열려 있을 때 **종류를 공개 상태로 가르고 길이는 보스 SO 값**(서버가 창을 여닫는 값과 같은 출처, 클라에도 있음). 텔레그래프(`BossAttackConeTelegraph`)의 growTime·경과는 private 이라 읽지 않는다. 보스 코드·메시지 무수정.
    - 구별 재료: ① 보스 0층 애니메이터 상태(전이 중이면 다음 상태) — 각 피어 로컬이지만 서버가 같은 ClientRpc(PlayPrepPose·CrossFadeState·PlayAttackAnim·PlayPushNorthDownPose)로 구동 ② 보스 SO `phases` 의 레이드 기믹 구성(이 데이터가 릴레이·연쇄·북진을 쓰는가) ③ 서버만 공개 `IsChargeGimmickActive`(레이드 기믹 = 차징 대기 안) ④ 클라는 '창이 열릴 때 준비 클립이 이미 흐른 시간' = 준비 상태 경과 − 창 열린 뒤 경과(해석기 어댑터에 `openElapsed` 를 넘긴다).
    - **A 일반 돌진** = 준비 클립(`DashPrep`) + (레이드 준비 기믹 없는 데이터 / 서버 플래그 0 / 클라 열림 시 준비 경과 < 간격의 절반) → 돌진 행 `counterWindowDuration`(clamp 0~2, 에셋 1.5) — **정확**(준비 경로는 애니 준비 즉시라 창 타이머 단독으로 닫힘).
    - **A' 후속 돌진** = 돌진 클립(`DashAttack`)에서 정지 → `comboDashWindup` 0.7 — **근사(공칭)**: 돌진 클립 Hit 가 창보다 늦으면 그만큼 늦게 닫힌다(실제 ≥ 공칭 → 약 쪽으로만 틀림).
    - **D 레이드 릴레이** = 준비 클립 + 데이터가 릴레이(5)만 씀 + (서버 플래그 1 / 클라 열림 시 준비 경과 ≥ (relayPrepare − relayCounterWindow)/2 = 0.45) → `min(relayCounterWindow, relayPrepare)` 0.9 — **정확**(한 프레임 양자화). 간격이 0.3초 미만으로 튜닝되면 클라는 일반 돌진과 못 갈라 모름. `TwentyThree.prefab` 기본 데이터 `No23_A` 가 이 경우.
    - **E 레이드 연쇄** = 같은 방식, 간격 = `chainMarkTime`(1) → `chainCounterWindow` 1.5 — **정확**. 지금 에셋엔 연쇄(6)를 쓰는 데이터가 없다. 릴레이·연쇄를 둘 다 쓰는 데이터면 레이드 쪽은 모름.
    - **F 북진 무력화** = `GroggyStart` + 데이터가 북진(7)을 씀 → `pushNorthStunTime` 3 — **정확**(일반 그로기는 Attack 이탈로 창이 닫혀 겹치지 않는다).
    - **C 점프 착지(단계 4)** = `jumpLandingState`(또는 착지 뒤 이동 자세 `locomotionState` 인데 창이 남음) → 보스 SO `combos[].steps[]` 중 **점프 타**의 `landingCounterWindow` 가운데 0 이 아닌 값이 **한 가지뿐이면 그 값**(지금 No23·No23_A·No23_Raid·No23_B 전부 0.8) — **정확**(서버는 그 값만큼 열고 `Time.time` 으로 닫음). 0 아닌 값이 서로 다르게 둘 이상이면 어느 콤보인지 공개 상태로 몰라 **모름**(관측 중앙값), 하나도 없으면 모름. 가중치 0 콤보도 센다(스크립트 선택 `FindJumpTripleCombo` 는 가중치를 안 본다). 서버도 같은 규칙 — 닫힘 시각(`_landingCounterCloseAt`)·이번 타 값은 private.
    - **B 잡기(단계 4)** = `grabCatchState`/`grabHoldState`/`grabThrowState`(셋이 비었거나 겹치면 모름). 창 = 붙잡은 순간(`Boss_23_grab` OnAttackHit ≈ 붙잡기 구간 끝) ~ **마지막 내려치기 시작**. 서버 단계 길이 = SO 값 ÷ `grabCycleSpeed`(애니도 같은 배속), 닫힘 전 타 수 = max(1, `grabSlamCount` − 1)(서버는 각 타 끝에 남은 타 ≤ 1 이면 닫음 → 1타 데이터는 그 1타 끝). 반환 = **열린 뒤 경과 + 지금 클립에서 닫힘까지 남은 시간**(= Catch 중이면 합 − 열릴 때의 Catch 경과 — 해석기 계약 '이번 열림 전체 길이', 경과 기준 = 이 피어 창 기록과 애니 상태 경과(정규화 × 길이 ÷ `animator.speed`)).
      - Catch 중: 남은 = (catch + hold + 타 수 × throw)/speed − Catch 경과 — **정확**.
      - Shock(Holding) 중: 남은 = (hold + 타 수 × throw)/speed − Shock 경과 — **정확**. No23 실제 값(catch 1.1·hold 1.13·throw 0.65·slam 3·speed 1.4) → Shock 시작 뒤 남은 **1.736**.
      - Throw 중: 타마다 같은 클립을 처음부터 다시 틀어(`ReplayGrabSlamClientRpc` → `Play(…, 0)`) 몇 번째 타인지 애니로 모른다 → 열림 ≈ Shock 시작으로 보고 round((열린 뒤 경과 − hold/speed − 이번 타 경과) ÷ (throw/speed)) 로 지난 타 수 추정 — **근사**(열림과 Shock 시작 차이 + 도착 흔들림이 throw/2 ≈ 0.23초 미만이면 맞음).
      - 애니메이터 정지(자세 홀드 → 경과 -1)·창 기록 없음(`openElapsed` -1) = 모름. 붙잡힌 플레이어 쓰러짐·성공·중단 같은 조기 닫힘은 그대로 복제 bool 로만 안다.
    - 창이 닫혀 있거나 못 가르면 모름.
    - 🔴 `TwentyThree.prefab` 의 `MonsterCounterWindow`(1.5)는 23호 전체 창 길이가 아니다 — 중간보스 어댑터는 봇 타입으로 한정해 23호를 맡지 않는다.
    - 테스트 `Dev/InterruptLab/Editor/TwentyThreeInterruptWindowLengthTests.cs`(실제 에셋 No23·No23_A·No23_Raid·No23_B, 가짜 공개 상태(잡기 Catch·Shock·Throw 경과 포함), 프리팹 애니메이터·0층 상태 배선). 표: `_lane-artifacts/MainProject-Worktree3/139-5/window-inventory.md` §6.
  - **경과 시계 = 게임초**: 예측용 창 기록·추정은 `Time.time`(창 타이머·판정까지 남은 시간과 같은 스케일 시간) — 예전엔 기록 = `ServerTime`(실시간)이라 간파 시도 슬로우 중 경과·관측 길이가 부풀었다(139-5). 서버는 지연 판정용 `ServerTime` 기록(`InterruptLagJudge` — 의미 그대로)과 예측용 게임초 기록을 따로 둔다. 오너 클라 경과는 **복제 bool 이 도착한** 로컬 시각 기준이라 한 방향 지연만큼 짧게(남은 창을 길게) 잡힌다.
  - 보정 랩 CSV `window_expected_duration`·`predicted_window_remaining` 은 무기한이면 `inf`(질문 패널 '무기한'). 표본 seq 22(랩 무기한, 경과 0.924·관측 1.243 → 약)는 이제 길이 +∞ → 강(`InterruptCalibrationSample22Tests`).
- **판정·성공**: 대시 끝(또는 그 뒤 클립 Hit)의 `ResolveHit` 안 `ServerResolve`/`ServerSuccess` 그대로.
  선착순 한 명(할 일 143)도 같은 `ResolveHit` 에서 거른다 — 같은 창 판정 기준 시각 = 승인(`OnServerStart`) 순간([interrupt-presentation.md §0.10](interrupt-presentation.md#010-선착순-한-명-할-일-143)).
- **실패**: 대시 중 끊김 → `OnEnd` → `ServerFail(EndedBeforeResolve)` 그대로.
- 사건에 `Variant`(Normal/Dash) 칸은 **만들지 않았다** — 대쉬 간파도 일반 간파와 같은 프로필·cue 를 쓴다.
- 캐릭터 이펙트(클립 이벤트)는 같은 클립을 상체 레이어에서 트므로 같이 나온다(§6 이벤트 중복 방지 참고).

## 6. 마스킹 애니메이션 — `DashInterruptUpper` 레이어

하체 = 대시 그대로, 상체 = 간파 준비 클립. 대시 끝 판정부터는 기존 전신(0층) 간파 클립.

| 캐릭터 | 컨트롤러(`Assets/4.Animations/Player/`) | 상체 마스크 | 간파 상태 이름(SO `AnimatorStateName`) | 하체(대시 중 0층) |
|---|---|---|---|---|
| 팔라딘 | `Garen/GarenAnimatorController.controller` | **신규** `Garen/GarenUpperBody.mask`(어쌔신과 같은 부위) | `Interrupt` | Dodge 없음 → 0층 포즈(Idle/Walk) 그대로 |
| 어쌔신 | `Assassin/AssassinAnimatorController.controller` | 기존 `Assassin/AssassinUpperBody.mask` 재사용 | `Interrupt` | `Dodge_Combat_F_0` |
| 거너 | `Gunner/GunnerAnimatorController.controller` | 기존 `Gunner/GunnerUpperBody.mask` 재사용 | `Gunner_RMB_Interrupt` | Dodge 없음 → 0층 포즈 그대로 |

- 레이어: 세 컨트롤러 **맨 위**, Override, 기본 가중치 0. 상태 = `Empty`(기본) + 0층 간파와 **같은 클립·같은 이름**의 준비 상태. 기존 `UpperBody` 레이어(어쌔신 E Buff·거너 이동 사격)는 재사용하지 않는다 — 가중치 충돌 회피. 기존 레이어·상태·전이·파라미터는 바꾸지 않았다.
- 코드: `PlayerDashController`(입력 주체만 — 레이어 상태·가중치는 오너 권한 NetworkAnimator 가 서버·관찰자에 복제).
  - 시작 `BeginDashInterruptUpper(state)`: 상체 레이어 준비 상태를 처음부터 재생, 가중치 0→1 을 `dashInterruptBlend` 초 동안 선형으로. 0층은 건드리지 않는다.
  - 인수인계 `HandOffDashInterruptToBase(state)`: 0층을 같은 상태로 **상체 레이어의 지금 정규화 시각**에서 `CrossFadeInFixedTime(blend)` 시작 → 같은 프레임에 상체 레이어를 `Empty` 로 즉시 전환, 가중치 1→0 을 `dashInterruptBlend` 동안. 대시 끝 `PlayDodgeEnd(keepBasePose: true)` 는 0층을 덮지 않고 구간 속도만 되돌린다.
  - 취소 `CancelDashInterruptUpper()`: `Empty` + 가중치 0 즉시(강제 종료·대시에 실린 채 종료).
  - 가중치 계산 = `DashInterruptLayerFade`(순수 struct, 진행 중 다시 시작하면 지금 가중치에서 이어 감).
  - 컨트롤러에 레이어·상태가 없으면 경고 1회(`[Dash] 대쉬 간파 상체 레이어 생략 — …`) 후 마스킹 없이 진행.
- **이벤트 중복 방지**: 대시 중엔 상체 레이어 클립 이벤트만(Hit 은 보류 래치), 인수인계 뒤엔 0층만 — 인수인계 프레임에 상체 레이어를 `Empty` 로 블렌드 없이 바꿔 상체 클립 이벤트가 더 나지 않게 한다. 🟧 이벤트가 두 번 나지 않는지는 EditMode 로 못 본다 — Play 확인 항목.

## 7. 엣지 케이스

- 유닛에 막혀 조기 종료 → 그 시점이 대시 끝 → 바로 Skill 전이, Hit 보류면 즉시 판정.
- 대시 중 넉백·그랩·사망·CC·서버 취소(`EndDash`) → `PlayerDashState.Exit` 가 인수인계가 아니면 `HandleDashInterruptAborted`: 간파 `OnEnd(ForcedExitReason(다음 상태))`, 상체 레이어 취소, 서버면 `EndSkillClientRpc`. 쿨은 이미 소모(일반 간파 강제 종료와 같음, 환불 없음).
- 대시에 실린 채 스킬이 먼저 끝남(`EndActiveSkillServer`, 데이터 누락 등) → 대시에서 내리고 상체 레이어만 취소, 0층(대시 포즈)은 되돌리지 않는다.
- 서버 대시가 먼저 끝났는데 요청이 늦게 도착 → 서버 FSM 이 Dash 가 아니라 거부(`StateOrEffect`), 오너는 요청 대기 해제·피드백 없음, 대시는 정상 종료 → 일반 흐름.
- 팔라딘 E 가 우클릭 슬롯을 대체한 동안 → 대상 아님(창 안이어도 버퍼 그대로).

## 8. 알려진 한계

- **원격 오너: 서버 대시가 더 짧다.** 서버 대시는 요청 도착 때 시작하되 길이 = 지연을 뺀 남은 시간(`RemainingServerDuration`)이라, 서버가 오너 화면의 대시 끝보다 **먼저** Skill 로 넘어가 판정할 수 있다. 판정 위치는 서버가 가진 오너 복제 위치라, 그 순간 복제 위치가 아직 대시 끝에 닿지 않았을 수 있다(일반 간파와 같은 수준의 위치 오차 + 대시 이동분).
- 원격 오너에서 `PlaySkillClientRpc` 가 오너 대시 끝 뒤에 오면 오너는 일반 Skill 로 0층 전신 클립을 처음부터 튼다 — 서버 판정 시각과 화면 클립 시각이 어긋날 수 있다.
- 시도 예측 오프셋은 남은 대시 시간 기준 직선 이동이다 — 대시가 유닛·벽에 막히면 예측이 실제 판정 위치보다 멀다(강 시도 슬로우·오너 암전 오발동 → 실패 복귀 가능).
- `PlayerDashData.dashInterruptBlend` 의 툴팁은 "Dodge → 간파 준비 클립 크로스페이드" 라고 적혀 있지만 실제 용도는 **상체 레이어 가중치 전환 시간 + 인수인계 0층 CrossFade 시간**이다(§6).
- 이벤트 중복·레이어 블렌드 모양·관찰자 화면은 Play/MPPM 확인 전이다.

## 9. 수치 — `PlayerDashData` (`9.ScriptableObject/Player/PlayerDashData.asset`, 모든 캐릭터 공용)

| 칸 | 기본값 | 뜻 | xlsx |
|---|---|---|---|
| `dashInterruptWindow` | 0.1 | 대시 시작 후 이 초 안에 누른 우클릭이 대쉬 간파. 0 = 기능 끔 | 노출(숫자 칸) — 🟨 **병합 뒤 메인이 xlsx 반영 필요** |
| `dashInterruptServerTolerance` | 0.1 | 서버 승인 때 창에 더해 주는 지연 허용(초) | `[DataTableIgnore]` — 기획 수치 아님 |
| `dashInterruptBlend` | 0.05 | 상체 레이어 가중치 전환·인수인계 CrossFade 시간(초) | 노출(숫자 칸) — 🟨 **병합 뒤 메인이 xlsx 반영 필요** |

- 이 레인에서는 xlsx Export·SVN 을 하지 않았다. 반영 전까지 테이블 모드 Play·빌드에는 두 칸이 xlsx 에 없어 SO 기본값이 쓰인다(데이터 테이블 규칙은 [data-table.md](data-table.md)). Export 는 최신 development 워크트리에서 병합 Export 로.

## 10. 로그 태그 `[DashInterrupt]`

| 위치 | 내용 |
|---|---|
| `PlayerDashState.TickDashInterruptInput` | 우클릭 처리 결과(요청·이미 요청·차단 효과·쿨타임·창 밖·간파 스킬 아님) + 대시 경과/창 — 같은 누름은 1줄 |
| `PlayerSkillController.CanApproveSkill` | `거부 — 대시 경과 …s 가 창(+서버 허용오차) 밖` |
| `PlayerSkillController.HandleDashInterruptAborted` | `대시 중 강제 종료 → 간파 종료 (사유, 다음 상태)` |
| `PlayerInterruptSkillBase` | `대시 중 Hit — 대시 끝까지 판정 보류` · `대시 끝 → Skill 전이 — 보류된 Hit 즉시 판정 / 클립 Hit 대기` |

- 승인 로그는 기존 `[Skill] Interrupt 시작(대쉬 간파 — 대시에 실림) — …`, 상체 레이어 누락 경고는 `[Dash]` 태그다.

## 10.1 간파 제작용 디버그 씬 — `DebugInterruptLab` (C5, 설계안 §13)

간파·대쉬 간파 타이밍을 느린 배속으로 보고 같은 허수아비에 반복해 넣어 보는 씬이다. **빌드 목록에 없다** — Dev Boot 가 Play 동안만 잠깐 넣었다 뺀다.

**진입**: 메인 툴바 Dev Boot 씬 드롭다운 → `DebugInterruptLab` 선택 → Dev Boot ▶. (`Dev_Boot` → 호스트 기동 → NGO 로 이 씬 로드 → 플레이어 스폰. 캐릭터는 툴바 캐릭터 드롭다운.) 씬을 직접 열고 Play 하면 NetworkManager 가 없어 동작하지 않는다.

**132 연출 확인**(할 일 132 단계 6 점검 — 씬 변경 없음): 간파 연출 디렉터·암전 렌더 기능·빛 모임 소켓은 부트의 `NetworkManager`·플레이어 프리팹·`PC_Renderer` 에서 오고,
허수아비 두 개가 스폰 때 `InterruptTargetRegistry` 에 등록된다(`TrainingDummyInterrupt`, 정면 판정 없음 = 180). 허수아비가 **노랑(인터럽트 가능)** 일 때 사거리 안에서 그쪽으로 우클릭 = 강(암전·빛 모임 → 성공 섬광),
노랑이 아닐 때 = 약(약 슬로우·줌만), 허수아비와 먼 곳·등진 방향 = 대상 없음(아무것도 없음). 23호는 이 씬에 없다 — 정면 허용각 확인은 보스 씬에서.
⚠️ 이 씬의 허수아비 창은 무기한(`DebugDummyInterruptRecovery`)이라 창 '추정 길이' = 지난번 노랑이 된 뒤 간파까지 기다린 시간(중앙값)이 된다 — 그보다 오래 기다렸다 누르면 창이 열려 있어도 **약**으로 예측될 수 있다(예측 규칙대로의 동작, 보스 창은 길이가 정해져 있어 해당 없음).

**씬에 둔 것** (부트에서 오는 NetworkManager·`GlobalTimeScale`·간파 연출 디렉터·GameManager·EffectManager·EventSystem·GameMenu 는 두지 않는다 — `TrainingDummy.unity` 와 같은 구성)

| 오브젝트 | 이유 |
|---|---|
| `Plane`·`Directional Light` | 바닥·조명(`TrainingDummy.unity` 에서 복사). 스폰 지점은 두지 않는다 — `MapGenerator` 가 없으면 원점 + `spawn_Offset` 으로 폴백한다 |
| `CameraSwitcher`(프리팹) | 카메라 리그. 카메라 매니저는 대상 씬 소속이라 씬마다 둔다 |
| `PlayerDashValidationManager`(프리팹) | 대시 서버 검증 — 맵 씬마다 두는 매니저(대시·대쉬 간파에 필요) |
| `FloatingDamageSpawner` | 씬 싱글턴 — 없으면 데미지 숫자가 안 뜬다 |
| `TrainingDummy_1`·`_2`(`TrainingDummy.prefab`, (±2, 0, 5)) + `DebugDummyInterruptRecovery` | 간파 표적. HUD 는 플레이어 프리팹에 있어 따로 두지 않는다 |
| `InterruptLab` + `DebugTimeScale`·`DebugZeroCooldown`·`DebugCharacterCycle`·`DebugInterruptTuningPanel` | 디버그 배속(+성공 후 1배속)·쿨 0·F1 캐릭터 순환(C6)·간파 연출 튜닝 패널(132 §17) |

**배속** (`DebugTimeScale` — `InterruptLab` 오브젝트 인스펙터): 슬라이더 0.05~2, 프리셋 버튼 ×0.1/×0.25/×0.5/×1. Play 중 바로 반영되고 인스펙터에 지금 `Time.timeScale`·슬로우·기본 배속이 보인다.
- `Time.timeScale` 을 직접 쓰지 않고 `GlobalTimeScale.BaseScale`(기본 배속, 기본 1)만 정한다. 최종 = **디렉터 슬로우 배율 × 디버그 배속**(`GlobalTimeScale.ComposeScale`). 슬로우가 없을 때도 기본 배속을 쓰고, 1 로 돌리면 1 로 돌아간다. 1 이면 이 기능이 없던 때와 동작이 완전히 같다.
- 히트스톱은 슬로우 타임라인 안의 구간(`SlowMotionTimeline`)이라 같은 곱을 받는다. `GlobalTimeScale` 밖에서 `Time.timeScale` 을 쓰는 곳은 `GameManager`(타이틀 복귀)·`DemoSessionDirector`(세션 끊김) 의 `= 1` 두 곳뿐이고 둘 다 세션 종료 뒤라, 그 시점엔 `GlobalTimeScale` 도 세션 종료로 1 로 돌린다 — 충돌 없음.
- 컴포넌트가 꺼지거나 씬이 내려가면 기본 배속을 1 로 되돌린다.
- **한계**: 이 피어에만 적용된다(복제 안 함) — 솔로 호스트 전제. `NetworkClock.GameNow`(쿨다운·보스 타이머 등)는 슬로우만 빼고 기본 배속은 반영하지 않는다 — 느린 배속에서 GameNow 기준 타이머는 실시간으로 흐른다.

**허수아비 회복** (`DebugDummyInterruptRecovery` — 각 허수아비 인스턴스): `회복 시간`(기본 2초) — 간파 성공 → 그 시간 뒤 인터럽트 가능(노랑)으로 돌아오고, 그 상태는 시간 제한 없이 유지된다.
- 허수아비 코드는 그대로다. 순환이 이미 [취약 → idle → 인터럽트 가능(무기한)] 이라 이 컴포넌트는 `TrainingDummyInterrupt` 의 취약 = 회복 시간, idle = 0 으로 **씬 인스턴스 오버라이드**만 맞춘다(에디터, Play 밖). 회복 시간 전부가 취약(보라, CC 가능)이다.
- 순환은 스폰 때 만들어진다 — Play 중 바꾼 값은 다음 Play 부터. 시간은 서버 `deltaTime` 이라 디버그 배속·슬로우를 따른다(×0.1 이면 실시간 20초).
- 씬 오버라이드라 데이터 테이블 `Map` 시트 값이 이 두 인스턴스엔 닿지 않는다 — Verify 가 이 씬을 경고하는 것은 의도다.

**성공 후 1배속** (C6, `DebugTimeScale` 의 `성공 후 1배속(초, 실제 시간)`, 기본 3): 간파 성공 → 그 시간(실제 시간, `Time.unscaledTimeAsDouble`) 동안 기본 배속을 1 로 둔다. 끝나면 슬라이더 값으로 돌아간다 — 슬라이더는 건드리지 않으므로 그 사이 바꾼 값이 쓰인다. 그 사이 또 성공하면 그때부터 다시 잰다. 0 = 끔.
- 디렉터 성공 슬로우·히트스톱은 그대로 곱해진다(1 × 슬로우).
- 성공 감지 = `InterruptProductionDirector.LocalSucceeded`(각 피어, 성공 슬로우가 이 피어에서 시작될 때 — 칸 스위치와 무관). 이 구독용 이벤트 하나만 디렉터에 추가했다(새 연출 경로 아님). 성공 슬로우가 거부되면(컷신 잠금·씬 전환) 이벤트도 나지 않는다.

**간파 연출 튜닝 패널** (할 일 132 §17, `DebugInterruptTuningPanel` — `InterruptLab` 오브젝트, `DebugTimeScale` 옆): Play 중 화면 오른쪽 위 OnGUI 박스(제목 버튼으로 접기).
- 연출 효과 10줄 토글 — 오너 암전 · 빛 모임(VFX+라이트) · ②스타버스트 · ③화이트아웃 · ④스미어 · ⑤색수차 블러 · ⑥가라앉음(블룸) · 월드 섬광 · 적중 지점 라이트 · 팀원 암순응(A2). `전부 켬`·`암전만`(기본값) 버튼.
- 배속 = 같은 오브젝트 `DebugTimeScale` 슬라이더(0.05~2)와 버튼 ×0.05/0.1/0.25/0.5/1/2 — 위 '배속'·'성공 후 1배속' 규칙 그대로(1배속 중이면 남은 초 표시).
- 토글은 이 피어 `InterruptProductionDirector` 런타임 값만 바꾼다 — 프리팹·씬 저장 없음, Play 를 끝내면 프리팹 값(기본 = 오너 암전만 켬). 끈 효과는 그리기만 안 하고 상태 기계·섬광 순간은 그대로 돈다.
  칸 표·의미: [interrupt-presentation.md §0.6](interrupt-presentation.md).

**쿨 0** (C6, `DebugZeroCooldown` — 칸 `interrupt`·`dash`, 기본 둘 다 켬): 쿨 계산 코드는 그대로고 기존 리셋 API 만 부른다(튜토리얼 복도1 쿨 초기화와 같은 짝).
- 간파 [서버]: 매 프레임 각 플레이어의 간파 슬롯 쿨이 남아 있으면 `PlayerSkillController.ResetCooldownServer(Interrupt)` — 성공·실패·대쉬 간파 무관. 장부 API 가 오너 HUD 장부에 미러하므로 오너 선검증(`IsBlockedByCooldown`)·서버 승인(`CanApproveSkill`) 모두 통과.
- 대시 [서버]: `PlayerDashController.ServerDashApproved` 마다 `ServerResetChargeToFull`. [오너]: 내 예측 충전이 덜 찼으면 `OwnerResetChargeToFull`(각 피어의 씬 인스턴스가 자기 플레이어만). 리셋마다 `[Dash/서버] 충전 강제 초기화` 로그가 한 줄 남는다.
- 컴포넌트를 끄면 평소 쿨로 돌아간다(이미 남은 쿨은 그대로 흐른다).

**F1 캐릭터 순환** (C6, `DebugCharacterCycle` — `순환 순서` 기본 팔라딘(0) → 어쌔신(2) → 거너(1), 로스터 = `CharacterRoster.asset` id): 로비 캐릭터 교체(할 일 48 C12)와 같은 길 — 서버가 지금 PlayerObject 를 디스폰하고 같은 위치·회전에 새 Variant 를 `PlayerSpawnService.SpawnCharacter` 로 세운다. 카메라는 새 오너 플레이어의 `OnNetworkSpawn → CameraTargetSwitcher.FocusOwnerPlayer` 로 다시 붙는다.
- **호스트만**(서버 API 를 바로 부른다, 새 RPC 없음). 클라 창에서 누르면 경고만.
- 지금 캐릭터는 PlayerObject 프리팹 해시로 로스터에서 찾는다(모르면 순서의 처음). 선택 저장소(`ServerCharacterSelectionStore`)는 건드리지 않는다.
- 새 오브젝트라 HP·쿨·상태가 새로 시작된다. 연타 방지 0.5초(실제 시간).
- 게이트 `UNITY_EDITOR || DEVELOPMENT_BUILD || DEV_HOTKEYS`. F1 은 `HitVFXDebugHUD`(피격 이펙트 순환)와 같은 키지만 그 HUD 는 이 씬·부트 구성에 없어 겹치지 않는다([debug-hotkeys.md](debug-hotkeys.md)).

## 10.2 예측 보정 랩(139) — `InterruptCalibrationLab`

"분명 맞췄는데 강 연출이 안 뜬다" 를 잡으려고, 대쉬 간파 한 번마다 **오너 예측(강/약/대상 없음)·서버 예측·서버 판정·판정 시점 실제 창 상태·사람의 판단(성공이어야 했나)** 을 표본 하나로 모아 CSV 로 남기는 씬이다.
기록만 한다 — 예측식(`DashInterruptPredictor`)·허용치 칸은 고치지 않는다(데이터를 본 뒤 새 할 일). 계획 원본: 할 일 139 계획서(grill 2026-10-29).
첫 세션 표본(27개)으로 고친 예측 규칙은 §5 '예측 규칙 보정(할 일 139-2)'.

**씬**: `Assets/0.Scenes/Debug/InterruptCalibrationLab.unity` — `DebugInterruptLab` 을 `AssetDatabase.CopyAsset` 으로 복제(새 guid)한 뒤 아래만 바꿨다. **빌드 목록에 없다**(§10.1 과 같이 Dev Boot 가 Play 동안만 넣었다 뺀다).

| 오브젝트 | §10.1 대비 |
|---|---|
| `Plane`·`Directional Light`·`CameraSwitcher`·`PlayerDashValidationManager`·`FloatingDamageSpawner` | 그대로 |
| `TrainingDummy`(`TrainingDummy.prefab`, (0, 0, 5)) + `DebugDummyInterruptRecovery`·**`DebugDummyInterruptWindowMode`** | 허수아비 **하나만** 남겼다(`_2` 삭제, `_1` 을 가운데로). 창 모드 기본 = 무기한, 주기 0.6s/1.5s |
| `InterruptCalibrationLab` + `DebugTimeScale`·`DebugZeroCooldown`·`DebugCharacterCycle`·`DebugInterruptTuningPanel`·**`InterruptCalibrationCapture`·`InterruptCalibrationQuestionPanel`·`InterruptJudgeShapeGizmos`** | 랩 오브젝트 이름 변경. 캡처·질문 패널의 `windowMode`(+ 패널 `capture`)는 씬에서 명시 연결. 판정 범위 기즈모(할 일 156)는 아래 '간파 판정 범위 기즈모' |

보스는 원래 없다. 강·약 연출은 부트 `NetworkManager` 의 `InterruptProductionDirector` 경로 그대로 켜져 있다(튜닝 패널 기본 = 오너 암전만, §10.1).
격리: `DebugInterruptLabIsolationTests` 가 두 랩 씬을 디버그 씬으로 보고(빌드 목록·다른 씬·프리팹에 디버그 컴포넌트 없음), 이 씬의 허수아비 하나·창 모드 연결도 YAML 로 확인한다.

**진입**: 메인 툴바 Dev Boot 씬 드롭다운 → `InterruptCalibrationLab` → Dev Boot ▶ (솔로 호스트. `DevBootSceneCatalog` 가 `Assets/0.Scenes` 아래를 자동으로 찾아 등록 코드 없음). 씬을 직접 열고 Play 하면 동작하지 않는다(§10.1 과 같음).

**화면** — 왼쪽 아래 `간파 예측 보정(139)` 패널(제목 버튼으로 접기, 너비 320), 오른쪽 위는 §10.1 튜닝 패널, 왼쪽 위는 판정 범위 기즈모 패널(할 일 156, 아래)(겹치지 않는다). 질문 창은 화면 가운데.
- 첫 줄: 표본 수 · 건너뜀(Esc) · 짝 실패 버림 / 대기열 · 넘쳐 버림 · 지금 단계.
- 일치율 4줄(실시간): 오너 예측 ↔ 답 · 서버 예측 ↔ 답 · 서버 결과 ↔ 답 · 오너 ↔ 서버 예측. 강 = true·약 = false, 서버 성공 = true. 대상 없음·값 없음은 분모에서 뺀다.
- `기록:` 이번 세션 폴더 · `지금 summary 쓰기` 버튼(표본 0 개면 안 씀).
- `허수아비 창:` `무기한`/`주기` 토글 · `열림`/`닫힘` 길이(슬라이더 0~3s, 0.05 단위, ± 버튼 — 키보드 입력 칸 없음) — Play 중 바로 적용.
- `예측기 관측 기록 초기화` — 예측기가 쌓은 창 길이 관측(중앙값의 재료, `InterruptWindowReplicaTracker`)을 지운다. 모드·길이를 바꾼 뒤 누르면 지난 모드의 관측이 섞이지 않는다.
- 패널 위에 마우스가 있는 동안은 플레이어 입력(이동·대시·간파·스킬)을 막는다(`UiInputGateManager`).

**질문 흐름** (`InterruptCalibrationQuestionPanel` + 순수 `InterruptCalibrationQuestionFlow`)
1. 서버 판정 = 표본 완성 → 질문 대기열.
2. 판정 + **0.5초(실시간)** 가 지나고 `GlobalTimeScale` 가 평소(슬로우·히트스톱·실패 복귀 끝, `IsActive = false`)면 시간을 멈추고 묻는다. 평소로 안 돌아와도 판정 + 3초(실시간)면 그냥 묻는다.
   `waitDebugNormalSpeedHold`(기본 끔)를 켜면 §10.1 '성공 후 1배속' 이 끝날 때까지도 기다린다.
3. 정지 = `Time.timeScale` 기억 → 0(가장 늦은 LateUpdate 에서 덮어씀) + 솔로 호스트면 `NetworkClock.Pause`. 재개 = 기억한 배율로 돌리고 시계 재개, 그 뒤엔 `GlobalTimeScale` 이 평소대로 쓴다.
4. 답: **Y** = 성공이어야 했음(true) · **N** = 실패여야 했음(false) · **Esc** = 건너뛰기(기록 안 함, seq·일치율에 안 들어감). 버튼도 같다. 묻는 동안 플레이어 입력을 막고, 답한 뒤 마우스 버튼을 다 뗄 때까지 더 막는다(클릭이 공격으로 새지 않게).
5. 질문 중 다음 표본이 오면 줄 선다(최대 8, 넘치면 새 표본 버림). 대기 표본이 바로 물을 수 있으면 멈춘 채로 다음 질문.

**기록** — `C:/UnityProject/QA/139calib-<yyyyMMdd_HHmmss>/` (씬 시작마다 새 폴더, 첫 답 때 만든다 — 표본 0 개 세션은 폴더가 없다)
- `samples.csv` — 답(Y/N) 마다 즉시 한 줄(UTF-8 BOM, 헤더 1줄, 값 없음 = 빈 칸, 시각은 같은 피어 `Time.time` 초).

| 열 | 뜻 |
|---|---|
| `seq`·`real_time` | 기록 번호(1부터) · 판정 실시간(`realtimeSinceStartup`) |
| `window_mode`·`window_open_len`·`window_closed_len` | 판정 때 허수아비 창 모드(Unbounded/Periodic)·주기 길이 |
| `press_age` | 대시 입력 → 우클릭 간격(초) |
| `cast_time`·`cast_pos_*`·`dash_dir_*`·`dash_speed`·`dash_remaining`·`skill_start_time`·`prep_delay`·`has_target_in_range`·`target_window_open`·`window_open_elapsed`·`window_expected_duration` | 오너 입력 순간 `DashInterruptPredictInput` 필드 그대로(음수 = 예측기 기준 '모름') |
| `resolve_pos_*`·`target_distance`·`target_angle` | 예상 판정 위치(대시 끝) · 대상 판정 콜라이더 표면까지 수평 거리 · 대시 방향 ↔ 대상 방향 각도(도). 대상 없음이면 가장 가까운 등록 대상으로 잰다 |
| `predicted_resolve_time`·`predicted_window_remaining` | 예측 판정 시각 · 그때 창 남은 시간 추정 |
| `owner_forecast`·`owner_reason` | 오너 예측(강/약/대상 없음) · 예측기 Reason — **누른 순간 원래 예측** |
| `owner_repredicted`·`owner_final_forecast` | 대시 중 재예측(§5 ②, 할 일 139-2)이 약 → 강으로 올렸나 · 오너 최종 예측(없으면 원래와 같다) |
| `server_forecast` | 서버 승인 시점 예측(시도 슬로우 강/약, 대상 없음 = NoTarget) |
| `server_repredicted`·`server_final_forecast` | 서버 재예측이 강 시도 슬로우를 다시 걸었나 · 서버 최종 시도 슬로우 |
| `server_succeeded` | 서버 판정 성공/실패 |
| `actual_window_open`·`actual_open_elapsed`·`actual_remaining` | 판정 시점 실제 허수아비 창(열림 여부 · 열린 뒤 경과 · 실제 남은 시간 — 닫혀 있으면 경과·남은 시간 빈 칸, 무기한이면 남은 시간 빈 칸) |
| `user_answer` | 사람 답(true = 성공이어야 했음) |

- `summary.txt` — 표본 수, 일치율 4개(+제외 수, 누른 순간 원래 예측 기준), 불일치 유형별 개수(예: 오너 강·답 false / 오너 약·답 true), `[대시 중 재예측(약 → 강)]` 재예측 수(오너·서버)·오너 최종 예측 ↔ 답, 오너 예측 ↔ 답 불일치의 오너 Reason 별 개수.
- 재예측은 강으로 올린 때만 알린다(`CalibrationOwnerRepredicted`·`CalibrationServerRepredicted`) — 대기 표본에 덧붙고 새 표본을 열지 않는다. 질문 창에는 `→ 재예측 강` 으로 보인다.
  패널이 꺼질 때(씬 나감·Play 정지 포함)·앱 종료·`지금 summary 쓰기` 버튼에서 (다시) 쓴다.

**짝짓기 규칙** (`InterruptCalibrationPairing`)
- 오너 요청에 시전 번호가 없어 **대기 표본은 하나**만 둔다. 오너 예측·서버 예측은 들어온 순서와 무관하게 그 표본에 붙고, 서버 판정이 오면 닫혀 질문으로 간다(빠진 쪽 칸은 빈 칸).
- 같은 쪽(오너 또는 서버) 예측이 또 오면 앞 표본은 판정 누락으로 버리고 새로 연다. 대기 표본 없이 온 판정도 버린다.
- 대기 표본이 열리고 **3초(게임 시간)** 안에 판정이 안 오면 버린다(판정 전 종료·넉백 등). 버린 수 = 패널 `짝 실패 버림`.
- 대쉬 간파만 기록한다. 근접 간파는 `InterruptCalibrationCapture.includeMelee`(기본 끔)를 켜면 같은 구조로 들어온다.

**주기 창 규칙** (`DummyInterruptWindowSchedule`, 서버 `deltaTime` = 디버그 배속·슬로우를 따른다)
- 열림 → 닫힘 반복, 경계 시각은 바뀐 뒤 단계. 열림 0 = 열리지 않음, 닫힘 0 = 계속 열림. 주기로 바꾸면 열림 처음부터, 도중 길이 변경은 지금 단계 경과 유지.
- **회복 중엔 주기 시계가 멈춘다** — 간파 성공 → 회복(`DebugDummyInterruptRecovery`, 기본 2초, 취약·보라)이 끝나면 **닫힘 단계 처음부터** 다시 시작한다(성공 직후 잘린 열림이 다시 나오지 않게, 관측 창 길이가 열림 길이로 모인다).

**간파 판정 범위 기즈모(할 일 156)** — `InterruptJudgeShapeGizmos`(랩 오브젝트) + 순수 규칙 `InterruptJudgeShapeGizmoRules`
- **무엇과 같은 값인가**: 근사 도형이 아니라 시도 예측 오버랩이 질의하는 **바로 그 형상** — 판정 앵커(`HitboxAnchorShape.TryRead`)를 공격 방향으로 돌려 판정 위치에 놓은 것(`HitboxAnchorShape.PlacedAt`). 예측 대상 탐색(`FindPredictTarget`)과 기즈모가 같은 읽기 전용 진입점 `PlayerInterruptSkillBase.TryGetPlacedJudgeShape(판정 위치, 공격 방향)` 하나를 지난다(판정 동작 불변 — 서버 `ResolveHit` 은 여전히 판정 순간 실제 앵커 `OverlapHitboxAnchor`). 판정 위치 = 근접은 시전 위치, 대쉬는 예상 대시 끝(`DashInterruptPrediction.PredictedEndPosition`).
- **화면**: 왼쪽 위 `간파 판정 범위(156)` 패널(제목 버튼으로 접기). 토글 `상시 미리보기`·`시도 잔상`·`Game 뷰에도 그리기`, `잔상` 길이 슬라이더(0.5~10s, 기본 3s), `잔상 지우기`, 색 범례, `마지막 시도:` 한 줄(출처·근접/대쉬·재예측·예측 결과·대시 거리). 패널 위에 마우스가 있으면 플레이어 입력을 막는다(질문 패널과 같다).
- **상시 미리보기** — 이 피어의 로컬 플레이어마다 지금 위치·조준 방향(`PlayerSkillController.CurrentAimDirection` = 근접 간파 요청 방향)에 놓은 형상. 색 = 오너 즉시 예측과 같은 대상 탐색(`FindOwnerViewTarget` — 같은 형상·대상 캐시·복제 창·로컬 선착순 장부, 이벤트 없음): **흰색(옅음) = 대상 없음 · 주황 = 대상 있음·창 닫힘 · 하늘색 = 대상 있음·창 열림**. 창 열림 ≠ 강 예측(예측은 준비 시간 뒤 창 남은 시간까지 본다).
- **시도 잔상** — 계측 훅(`CalibrationOwnerPredicted`·`CalibrationOwnerRepredicted`, 시전자가 로컬이 아니면 `CalibrationServerPredicted`·`CalibrationServerRepredicted`)이 오는 그 프레임에 그 예측의 판정 위치·공격 방향으로 형상을 잡아 남긴다. 대쉬면 시전 위치(누른 순간) → 예상 대시 끝 선도. 색 = 예측 결과: **회색 = 대상 없음 · 노랑 = 약 · 빨강 = 강**. 재예측(약 → 강)은 새 잔상(빨강)으로 하나 더. 실시간으로 옅어지다 사라지고, 보정 질문 창 정지(timeScale 0) 중에는 줄지 않는다. 최대 12개.
- **그리기**: Scene 뷰 = `OnDrawGizmos`(+ 라벨), Game 뷰 = 메인 카메라 렌더 뒤 GL 선(깊이 무시 — 바닥·몸에 가려지지 않는다, Game 뷰 Gizmos 버튼 무관). 선분 = `AttackShapeDrawing.CollectAnchorSegments`(상자 모서리 12 · 구 원 3 · 캡슐 양끝 구 + 옆선 4, 특성 랩 앵커 기즈모와 같은 모양). 메시·텍스처 생성 없음.
- **그리지 않는 것**: 전방위 HUD 강조 사거리(`HasTargetInOmniRange`)는 판정이 아니라 넣지 않았다. 판정 순간 실제 앵커(애니메이션으로 움직인 자리)도 그리지 않는다 — 예측이 보는 '지금 앵커를 돌려 옮긴 것'이 기준이다.
- **한계**: Game 뷰 GL 선은 에디터 Play 전용(선분 함수가 에디터 전용 `AttackShapeDrawing`). 원격 클라 창에서는 자기 시도(오너 훅)만 잔상으로 남고, 호스트 창에서는 남의 시도도 서버 훅으로 남는다. 형상은 훅이 온 프레임의 앵커·시전자 위치로 잡는다(예측과 같은 프레임이라 같은 값). 테스트 `Editor/InterruptJudgeShapeGizmoTests.cs`.

**알려진 한계**
- **솔로 호스트만**. 캡처·창 모드·정지가 서버와 오너가 같은 피어라는 전제다(클라 창에서는 서버 판정·실제 창 상태가 안 들어온다).
- **허수아비 하나** 전제 — 창 모드·실제 창 상태는 연결된(없으면 씬에서 처음 찾은) 허수아비 하나만 본다. 허수아비를 늘리면 다른 허수아비 표본의 창 칸이 틀린다.
- 무기한 모드에서는 창 추정 길이 = 지난번 기다린 시간이라(§10.1 ⚠️) '약' 이 나오는 것이 예측 규칙대로다 — 증상 재현용. 주기 모드 표본과 섞어 보지 말 것(`window_mode` 열로 나눈다).
- Esc 는 질문 중에만 이 패널이 쓴다. 부트의 게임 메뉴가 같은 프레임에 Esc 를 받는지는 Play 확인 전이다.

## 11. 범위 밖

- 116(간파 예측 로그)·117(성공 예측 연출)은 이 작업 범위 밖이다. 116 은 §5 의 예측 오프셋 값을 그대로 기록하면 된다.

## 12. 관련 파일

| 파일 | 내용 |
|---|---|
| `Assets/1.Scripts/Player/Dash/DashInterruptRules.cs` | 순수 규칙 — 창 판정·입력 결정·서버 승인 창·판정 시점·max·예상 대시 끝 오프셋 |
| `Assets/1.Scripts/Player/Dash/DashInterruptPrediction.cs` · `Assets/1.Scripts/Player/Skill/InterruptAttemptPrediction.cs` · `InterruptTargetRegistry.cs` | 할 일 132 — 근접·대쉬 공용 예측기·대상 정면 판정(`InterruptTargetGeometry`)·예측 입력 구성(`AtPress`)·대상 캐시 |
| `Assets/1.Scripts/Player/Dash/DashInterruptLayerFade.cs` | 상체 레이어 가중치 전환·인수인계 정규화 시각 |
| `Assets/1.Scripts/Player/Dash/PlayerDashData.cs` · `Assets/9.ScriptableObject/Player/PlayerDashData.asset` | SO 칸 3개 |
| `Assets/1.Scripts/Player/PlayerDashController.cs` | 상체 레이어 시작·인수인계·취소, `PlayDodgeEnd(keepBasePose)`, 창·허용오차 조회 |
| `Assets/1.Scripts/Player/PlayerStateController.cs` | `PlayerDashState` 창 입력·부착/분리·대시 끝 Skill 전이·강제 종료, `BeginSkillFromDash`·`AttachDashInterrupt`·`ActiveDash` |
| `Assets/1.Scripts/Player/Skill/PlayerSkillController.cs` | `TryUseDashInterrupt`, RPC 2개 플래그, 승인 분기, `HandleDashInterruptAborted`·`CompleteDashInterruptHandOff`, `PlayerSkillApprovalRules.EvaluateDashInterruptPreconditions`·`PlayerSkillApproval.DashWindow` |
| `Assets/1.Scripts/Player/Skill/PlayerInterruptSkillBase.cs` | Hit 보류·End 연기·`OnDashHandOff`·시도 예측 오프셋 |
| `Assets/1.Scripts/Player/Skill/PlayerSkillBase.cs` · `HitboxAnchorShape.cs` | `OverlapHitboxAnchor(results, offset)` · 판정 앵커 형상(읽기·돌리기·옮기기·오버랩) — 판정·시도 예측 공용(할 일 139-3), 테스트 `Assets/1.Scripts/Player/Editor/HitboxAnchorShapeTests.cs` |
| `Assets/1.Scripts/Player/PlayerInputBuffer.cs` · `PlayerInputReader.cs` | `TryGetRecordedTime` · `TryGetPressTime` |
| `Assets/4.Animations/Player/{Garen,Assassin,Gunner}/*AnimatorController.controller` · `Garen/GarenUpperBody.mask` | `DashInterruptUpper` 레이어·팔라딘 상체 마스크 |
| `Assets/0.Scenes/Debug/DebugInterruptLab.unity` · `Assets/1.Scripts/Dev/InterruptLab/` | 디버그 씬(§10.1) — `DebugTimeScale`(+ `Editor/DebugTimeScaleEditor`)·`DebugDummyInterruptRecovery`·`DebugInterruptTuningPanel`(132 §17)·테스트 `Editor/DebugInterruptLabTests.cs`·`Editor/DebugInterruptLabIsolationTests.cs`. 기본 배속은 `Network/SlowMotion/GlobalTimeScale.cs` 의 `BaseScale` |
| `Assets/0.Scenes/Debug/InterruptCalibrationLab.unity` · `Assets/1.Scripts/Dev/InterruptLab/Calibration/` · `DebugDummyInterruptWindowMode.cs`·`DummyInterruptWindowSchedule.cs` | 예측 보정 랩(§10.2, 할 일 139) — 캡처·짝짓기·질문 창·기록·주기 창. 테스트 `Editor/InterruptCalibration*Tests.cs`·`DummyInterruptWindowScheduleTests.cs`, 메뉴 `Tools/Tests/간파 랩 EditMode 테스트 실행` |
| `Assets/1.Scripts/Dev/InterruptLab/InterruptJudgeShapeGizmos.cs`·`InterruptJudgeShapeGizmoRules.cs` | 보정 랩 간파 판정 범위 기즈모(§10.2, 할 일 156) — 형상 = `PlayerInterruptSkillBase.TryGetPlacedJudgeShape`, 선분 = `AttackShapeDrawing.CollectAnchorSegments`. 테스트 `Editor/InterruptJudgeShapeGizmoTests.cs` |
| 테스트 `Assets/1.Scripts/Player/Dash/Tests/EditMode/DashInterruptRulesTests.cs` · `DashInterruptLayerFadeTests.cs` · `Assets/1.Scripts/Player/Editor/PlayerSkillApprovalRulesTests.cs` · `PlayerInputBufferTests.cs` | 창 경계·0=끔·허용오차·판정 시점·입력 결정·승인 사전조건·가중치 전환·버퍼 기록 시각 |
