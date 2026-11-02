# 대쉬 간파 — 대시 시작 직후 우클릭으로 대시하며 간파 준비, 대시 끝 지점에서 판정

> 할 일 115(2026-10-27, 브랜치 `feature/DashInterrupt`). 구현 커밋 C1 `07a85d46` · C2a `f0f56ee2` · C2b `a5af6d8b`. C4(우클릭 → 대시 전환)는 할 일 132 정정2(2026-10-28, 사용자 지시)로 폐기 — §2.1.
> 설계안: `_lane-artifacts/MainProject-Worktree2/115/115-설계안.md`(레포 밖, §10·§11·§12 확정 절이 우선). 문서와 코드가 다르면 코드가 기준이다.
> 연출 디렉터는 [interrupt-presentation.md §6](interrupt-presentation.md#6-간파-연출-디렉터--interruptproductiondirector-할-일-118), 입력 거부 피드백은 [skill-reject-feedback.md](skill-reject-feedback.md).
> 🔹 할 일 191(2026-11-02): 예측·판정·성공 권한이 **`InterruptManager`(오너 클라 1차 판정 · 서버 2차 검증)** 로 옮겨졌다 — 단일 원본 [interrupt-client-judge.md](interrupt-client-judge.md). 입력 창·상태 흐름·애니 레이어(§1~§4·§6·§7)는 그대로다.

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

- **위치·방향**: 판정 위치 = 공용 스윕(`PlayerDashPath`)으로 구한 실제 대시 끝, 방향 = 대시 방향(할 일 191 — [interrupt-client-judge.md §3](interrupt-client-judge.md#3-예측판정--같은-순수-함수-interruptjudgecalculator)). 서버 피해는 여전히 플레이어 자식 `HitboxAnchor` 오버랩. 승인 방향 = 서버 대시 상태의 방향(`PlayerDashState.Direction`), 조준 방향 회전 스냅·고정 거리 조준 재투영 없음.
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

> 🔹 **할 일 191(2026-11-02)부터 예측·판정·성공 권한은 `InterruptManager` 다** — 단일 원본 [interrupt-client-judge.md](interrupt-client-judge.md).
> 아래는 대쉬 간파에 해당하는 요약이다. 옛 예측기(`DashInterruptPredictor`)·창 길이 추정·0.4초 규칙·대시 중 재예측은 **삭제됐다**(그 문서 §10).

- **판정 = 오너 클라 1차, 서버 2차 검증**: 오너가 누른 시각 표본으로 `InterruptManager.Predict` → 판정 시각 표본이 쌓이면 `Judge` → 성공이면 신고 1회 → 서버가 같은 `Judge` 를 자기 이력으로 다시 돌려 승인·거부([interrupt-client-judge.md §4·§5](interrupt-client-judge.md#4-오너-판정-흐름)).
- **판정 시각 = max(실제 대시 끝, 누른 시각 + Hit 지연)**. Hit 지연 = 클립 Hit 이벤트 시각 ÷ 지금 애니 속도.
- **대시 끝 = 공용 스윕 `PlayerDashPath`** — `PlayerMotor` 의 실제 대시 이동과 같은 충돌 검사라, 벽·유닛에 막히면 막힌 자리에서 판정한다. 예전 '남은 대시 시간 × 속도 직선' 예측의 오발동(막힌 대시에서 강)이 없어졌다.
- **시도 슬로우**: 승인 순간 `OnServerStart` → `InterruptManager.TryPredictServerAttempt`(같은 `Predict`, 서버 이력·서버 창 시간표) → `ServerAttempt(예측 3상태)`. 한 번 정한 강·약은 대시 중 다시 보지 않는다.
- **오너 연출**: 매니저 시도 cue(누른 순간 예측) → 판정 cue(성공/실패) — [interrupt-presentation.md §0.2](interrupt-presentation.md#02-흐름).
- 대상 = 판정 앵커를 공격 방향(대쉬 = 대시 방향)으로 돌려 판정 위치에 놓은 형상에 닿는 등록 대상 + 대상 정면 허용각(`InterruptTargetGeometry.FacesAttacker` — 기존 판정값, 정면 판정 없으면 180).
  - 오너 HUD 강조 사거리(`HasTargetInOmniRange`)는 '돌면 닿는 거리'(앵커 먼 모서리) 표시라 판정보다 넓다 — 강조가 켜져도 그 방향이면 대상 없음일 수 있다.
    대상까지 거리는 실제 콜라이더 표면 수평 거리(`InterruptOmniRangeModel.HorizontalDistanceToCollider`, 할 일 139-4).
  - 판정 앵커 = 각 `<캐릭터>_Armature.prefab` 의 `InterruptAttack` BoxCollider(프리팹이 원본, xlsx 칸 없음). 예측·판정·HUD 강조·서버 피해가 모두 이 상자를 읽는다.
    팔라딘 1.6×**2.4**×1.6 / 거너·어쌔신 1.4×**2.4**×1.4, 중심 (0, **1.3**, 0.9) → 높이 띠 발 +0.1 ~ +2.5m. 정면 도달 팔라딘 1.7m · 거너·어쌔신 1.6m(플레이어 위치 → 대상 표면).
    웰즈 & 23호 HurtBox(캡슐 r 1.7 · 높이 5.78)의 가장 넓은 단면(y 1.7~4.08)에 띠가 걸려 정면 도달이 표면 거리 그대로 나온다(할 일 163).
- 서버 `ResolveHit` 의 앵커 오버랩은 **피해만** 준다. 세션 중 성공은 신고 승인으로만 정해진다.
- **AABB 거리 착시(할 일 139-4)** — HUD 강조와 랩 CSV 거리 열은 대상 콜라이더 **실제 표면**(`HorizontalDistanceToCollider` — `Collider.ClosestPoint`, 질의점 높이 = 대상 AABB 중심 높이, 안이면 0)까지 잰다. 예전 AABB 거리는 대각선에서 최대 r(√2−1) 짧았다(23호 0.70m). 테스트 `Assets/1.Scripts/Player/Editor/InterruptOmniRangeModelTests.cs`.
- **창 길이 = 서버 창 시간표**(할 일 191) — 옛 139-6 창 길이 해석기(`InterruptWindowLengthResolver`·`TwentyThreeInterruptWindowLength`·관측 중앙값)는 삭제됐다. 보스·허수아비가 창 열림 때 닫힘 예정을 알리고 서버가 배포한다. 여닫는 곳 목록은 [interrupt-client-judge.md §9](interrupt-client-judge.md#9-창-여닫는-곳-목록-1단계).
- **성공**: 서버 승인 순간(`ServerOnClaimVerdict`) 기록, 첫 승인이면 성공 슬로우 사건. 선착순/동시(0.05초)는 매니저 장부가 서버 도착 기준으로 정한다([interrupt-client-judge.md §5](interrupt-client-judge.md#5-서버-검증-interruptclaimserver--interruptclaimvalidator)).
- **실패**: 대시 중 끊김 → `OnEnd` → `ServerFail(EndedBeforeResolve)` 그대로. 오너 쪽은 판정 전 끊김(`SkillCut`)으로 대기 판정을 버리고 연출을 바로 끝낸다(D5).
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
- ~~시도 예측 오프셋은 남은 대시 시간 기준 직선 이동~~ — 할 일 191 에서 공용 스윕(`PlayerDashPath`)으로 바뀌어 막힌 대시도 실제 끝을 쓴다.
- 서버 검증의 시전자 위치는 오너 복제 이력이라 보간 지연만큼 늦다 — 대쉬 간파 끝에서 거부가 늘 수 있다(시각 여유 안에서만 따라잡음, [interrupt-client-judge.md §12](interrupt-client-judge.md#12-알려진-한계)).
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
이 씬의 허수아비 창은 무기한(`DebugDummyInterruptRecovery`)이라 창 시간표에 **길이 모름**으로 올라간다 — 열려 있는 동안은 오래 기다렸다 눌러도 강이다(할 일 191 전의 '관측 중앙값 → 약' 증상은 없어졌다).

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

> 🔹 **할 일 191 5단계에서 용도가 바뀌었다 — "예측 vs 판정 vs 서버" 기록.** 옛 139 계측 훅(오너·서버 예측·재예측·서버 판정)은 예측기와 함께 삭제됐다.
> 표본 = 간파 QA 기록 한 줄(예측·오너 판정·서버 결과·어긋난 이유, [interrupt-client-judge.md §7·§8](interrupt-client-judge.md#8-보정-랩-용도-변경-interruptcalibrationlab-5단계)) + 판정 순간 허수아비 창 상태 + 사람의 판단. 씬·질문 창·Y/N/Esc 흐름은 그대로다.

간파 한 번마다 **예측(강/약/대상 없음)·오너 판정·서버 결과·판정 시점 실제 창 상태·사람의 판단(성공이어야 했나)** 을 표본 하나로 모아 CSV 로 남기는 씬이다.
기록만 한다 — 판정식·허용치 칸은 고치지 않는다(데이터를 본 뒤 새 할 일). 계획 원본: 할 일 139 계획서(grill 2026-10-29) → 할 일 191 PLAN §3.4.

**씬**: `Assets/0.Scenes/Debug/InterruptCalibrationLab.unity` — `DebugInterruptLab` 을 `AssetDatabase.CopyAsset` 으로 복제(새 guid)한 뒤 아래만 바꿨다. **빌드 목록에 없다**(§10.1 과 같이 Dev Boot 가 Play 동안만 넣었다 뺀다).

| 오브젝트 | §10.1 대비 |
|---|---|
| `Plane`·`Directional Light`·`CameraSwitcher`·`PlayerDashValidationManager`·`FloatingDamageSpawner` | 그대로 |
| `TrainingDummy`(`TrainingDummy.prefab`, (0, 0, 5)) + `DebugDummyInterruptRecovery`·**`DebugDummyInterruptWindowMode`** | 허수아비 **하나만** 남겼다(`_2` 삭제, `_1` 을 가운데로). 창 모드 기본 = 무기한, 주기 0.6s/1.5s |
| `InterruptCalibrationLab` + `DebugTimeScale`·`DebugZeroCooldown`·`DebugCharacterCycle`·`DebugInterruptTuningPanel`·**`InterruptCalibrationCapture`·`InterruptCalibrationQuestionPanel`·`InterruptJudgeShapeGizmos`** | 랩 오브젝트 이름 변경. 캡처·질문 패널의 `windowMode`(+ 패널 `capture`)는 씬에서 명시 연결. 판정 범위 기즈모(할 일 156)는 아래 '간파 판정 범위 기즈모' |

보스는 원래 없다. 강·약 연출은 부트 `NetworkManager` 의 `InterruptProductionDirector` 경로 그대로 켜져 있다(튜닝 패널 기본 = 오너 암전만, §10.1).
격리: `DebugInterruptLabIsolationTests` 가 두 랩 씬을 디버그 씬으로 보고(빌드 목록·다른 씬·프리팹에 디버그 컴포넌트 없음), 이 씬의 허수아비 하나·창 모드 연결도 YAML 로 확인한다.

**진입**: 메인 툴바 Dev Boot 씬 드롭다운 → `InterruptCalibrationLab` → Dev Boot ▶ (솔로 호스트. `DevBootSceneCatalog` 가 `Assets/0.Scenes` 아래를 자동으로 찾아 등록 코드 없음). 씬을 직접 열고 Play 하면 동작하지 않는다(§10.1 과 같음).

**화면** — 왼쪽 아래 `간파 예측·판정·서버 보정(191)` 패널(제목 버튼으로 접기, 너비 320), 오른쪽 위는 §10.1 튜닝 패널, 왼쪽 위는 판정 범위 기즈모 패널(할 일 156, 아래)(겹치지 않는다). 질문 창은 화면 가운데.
- 첫 줄: 표본 수 · 건너뜀(Esc) · 짝 실패 버림 / 대기열 · 넘쳐 버림 · 지금 단계.
- 일치율 4줄(실시간): 예측 ↔ 답 · 오너 판정 ↔ 답 · 서버 결과 ↔ 답 · 예측 ↔ 판정. 강 = true·약 = false, 판정·서버 성공 = true. 대상 없음·값 없음은 분모에서 뺀다.
- `기록:` 이번 세션 폴더 · `지금 summary 쓰기` 버튼(표본 0 개면 안 씀).
- `허수아비 창:` `무기한`/`주기` 토글 · `열림`/`닫힘` 길이(슬라이더 0~3s, 0.05 단위, ± 버튼 — 키보드 입력 칸 없음) — Play 중 바로 적용.
- 패널 위에 마우스가 있는 동안은 플레이어 입력(이동·대시·간파·스킬)을 막는다(`UiInputGateManager`).

**질문 흐름** (`InterruptCalibrationQuestionPanel` + 순수 `InterruptCalibrationQuestionFlow`)
1. QA 기록 줄이 닫힘(서버 결과까지 옴 · 신고 안 함 · 응답 없음) = 표본 완성 → 질문 대기열.
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
| QA 열 전부(`qa_seq` … `resolve_z`) | 간파 QA 기록 한 줄 그대로 — 예측·오너 판정·서버 결과·어긋난 이유·판정 시각 등. 열 뜻은 [interrupt-client-judge.md §7](interrupt-client-judge.md#7-qa-기록-d12-5단계) |
| `target_distance`·`target_angle` | 판정 위치 → 대상 판정 콜라이더 표면까지 수평 거리 · 공격 방향 ↔ 대상 방향 각도(도). 대상 없음이면 가장 가까운 등록 대상으로 잰다 |
| `actual_window_open`·`actual_open_elapsed`·`actual_remaining` | 판정 시점 실제 허수아비 창(열림 여부 · 열린 뒤 경과 · 실제 남은 시간 — 닫혀 있으면 경과·남은 시간 빈 칸, 무기한이면 남은 시간 빈 칸) |
| `user_answer` | 사람 답(true = 성공이어야 했음) |

- `summary.txt` — 표본 수, 일치율 4개(+제외 수), 불일치 유형별 개수(예: 예측 강·답 false / 예측 약·답 true), 어긋난 이유(QA `mismatch_reason`)별 개수.
  패널이 꺼질 때(씬 나감·Play 정지 포함)·앱 종료·`지금 summary 쓰기` 버튼에서 (다시) 쓴다.

**짝짓기 규칙** (`InterruptCalibrationPairing`)
- **시도 식별 = QA 기록 한 줄**(할 일 191). 예측·판정·서버 결과의 짝짓기는 QA 추적기(`InterruptQaTracker`)가 시전자 id·창 id 로 한다 — 옛 '대기 표본 하나' 규칙은 없앴다.
- 판정 순간 캡처(`InterruptManager.OwnerJudged`)는 시전자별로 들고 있다가 그 시전자의 기록 줄이 닫히면 붙여 표본을 만든다. 실제 창 상태는 판정 순간 값이다.
- **판정까지 간 줄만 표본**. 판정 전 종료(끊김·버림·시전 거부)는 버리고 패널 `짝 실패 버림` 으로 센다.
- 대쉬 간파만 기록한다. 근접 간파는 `InterruptCalibrationCapture.includeMelee`(기본 끔)를 켜면 같은 구조로 들어온다.

**주기 창 규칙** (`DummyInterruptWindowSchedule`, 서버 `deltaTime` = 디버그 배속·슬로우를 따른다)
- 열림 → 닫힘 반복, 경계 시각은 바뀐 뒤 단계. 열림 0 = 열리지 않음, 닫힘 0 = 계속 열림. 주기로 바꾸면 열림 처음부터, 도중 길이 변경은 지금 단계 경과 유지.
- **회복 중엔 주기 시계가 멈춘다** — 간파 성공 → 회복(`DebugDummyInterruptRecovery`, 기본 2초, 취약·보라)이 끝나면 **닫힘 단계 처음부터** 다시 시작한다(성공 직후 잘린 열림이 다시 나오지 않게, 관측 창 길이가 열림 길이로 모인다).

**간파 판정 범위 기즈모(할 일 156)** — `InterruptJudgeShapeGizmos`(랩 오브젝트) + 순수 규칙 `InterruptJudgeShapeGizmoRules`
- **무엇과 같은 값인가**: 근사 도형이 아니라 판정 앵커(`HitboxAnchorShape.TryRead`)를 공격 방향으로 돌려 판정 위치에 놓은 것(`HitboxAnchorShape.PlacedAt` — 매니저 예측·판정의 `InterruptJudgeCalculator.PlaceHitbox` 와 같은 회전·이동). 기즈모는 읽기 전용 진입점 `PlayerInterruptSkillBase.TryGetPlacedJudgeShape(판정 위치, 공격 방향)` 를 지난다. 판정 위치 = 근접은 시전 위치, 대쉬는 예측이 낸 대시 끝(공용 스윕).
- **화면**: 왼쪽 위 `간파 판정 범위(156)` 패널(제목 버튼으로 접기). 토글 `상시 미리보기`·`시도 잔상`·`Game 뷰에도 그리기`, `잔상` 길이 슬라이더(0.5~10s, 기본 3s), `잔상 지우기`, 색 범례, `마지막 시도:` 한 줄(출처·근접/대쉬·예측 결과·대시 거리). 패널 위에 마우스가 있으면 플레이어 입력을 막는다(질문 패널과 같다).
- **상시 미리보기** — 이 피어의 로컬 플레이어마다 지금 위치·조준 방향(`PlayerSkillController.CurrentAimDirection` = 근접 간파 요청 방향)에 놓은 형상. 색 = 미리보기 전용 대상 탐색(`FindOwnerViewTarget` — 같은 형상·대상 캐시·복제 창·로컬 선착순 장부, 이벤트 없음. 매니저 예측과는 창 출처가 달라 근사다): **흰색(옅음) = 대상 없음 · 주황 = 대상 있음·창 닫힘 · 하늘색 = 대상 있음·창 열림**. 창 열림 ≠ 강 예측(예측은 준비 시간 뒤 창 남은 시간까지 본다).
- **시도 잔상** — 예측 이벤트(할 일 191 — 오너 `InterruptManager.OwnerPredicted`, 시전자가 로컬이 아니면 서버 `InterruptManager.ServerAttemptPredicted`)가 오는 그 프레임에 그 예측의 판정 위치·공격 방향으로 형상을 잡아 남긴다. 대쉬면 시전 위치(누른 순간) → 예측 대시 끝 선도. 색 = 예측 결과: **회색 = 대상 없음 · 노랑 = 약 · 빨강 = 강**. 재예측은 없어졌다. 실시간으로 옅어지다 사라지고, 보정 질문 창 정지(timeScale 0) 중에는 줄지 않는다. 최대 12개.
- **그리기**: Scene 뷰 = `OnDrawGizmos`(+ 라벨), Game 뷰 = 메인 카메라 렌더 뒤 GL 선(깊이 무시 — 바닥·몸에 가려지지 않는다, Game 뷰 Gizmos 버튼 무관). 선분 = `AttackShapeDrawing.CollectAnchorSegments`(상자 모서리 12 · 구 원 3 · 캡슐 양끝 구 + 옆선 4, 특성 랩 앵커 기즈모와 같은 모양). 메시·텍스처 생성 없음.
- **그리지 않는 것**: 전방위 HUD 강조 사거리(`HasTargetInOmniRange`)는 판정이 아니라 넣지 않았다. 판정 순간 실제 앵커(애니메이션으로 움직인 자리)도 그리지 않는다 — 예측이 보는 '지금 앵커를 돌려 옮긴 것'이 기준이다.
- **한계**: Game 뷰 GL 선은 에디터 Play 전용(선분 함수가 에디터 전용 `AttackShapeDrawing`). 원격 클라 창에서는 자기 시도(오너 훅)만 잔상으로 남고, 호스트 창에서는 남의 시도도 서버 훅으로 남는다. 형상은 훅이 온 프레임의 앵커·시전자 위치로 잡는다(예측과 같은 프레임이라 같은 값). 테스트 `Editor/InterruptJudgeShapeGizmoTests.cs`.

**알려진 한계**
- **솔로 호스트만**. 캡처·창 모드·정지가 서버와 오너가 같은 피어라는 전제다(클라 창에서는 서버 판정·실제 창 상태가 안 들어온다).
- **허수아비 하나** 전제 — 창 모드·실제 창 상태는 연결된(없으면 씬에서 처음 찾은) 허수아비 하나만 본다. 허수아비를 늘리면 다른 허수아비 표본의 창 칸이 틀린다.
- 무기한 모드 창은 창 시간표에 '길이 모름'으로 올라간다 — 열려 있으면 강이다. 주기 모드 표본과는 `window_mode` 열로 나눠 본다.
- Esc 는 질문 중에만 이 패널이 쓴다. 부트의 게임 메뉴가 같은 프레임에 Esc 를 받는지는 Play 확인 전이다.

## 11. 범위 밖

- 116(간파 예측 로그)·117(성공 예측 연출)은 이 작업 범위 밖이다. 116 은 §5 의 예측 오프셋 값을 그대로 기록하면 된다.

## 12. 관련 파일

| 파일 | 내용 |
|---|---|
| `Assets/1.Scripts/Player/Dash/DashInterruptRules.cs` | 순수 규칙 — 창 판정·입력 결정·서버 승인 창·판정 시점·max·예상 대시 끝 오프셋 |
| `Assets/1.Scripts/Player/InterruptJudge/` | 할 일 191 — `InterruptManager`·`InterruptJudgeCalculator`(예측·판정)·`InterruptClaimServer`(검증)·QA 기록. [interrupt-client-judge.md](interrupt-client-judge.md) |
| `Assets/1.Scripts/Player/Motor/PlayerDashPath.cs` | 대시 경로 공용 스윕 — 실제 이동(`PlayerMotor`)과 판정이 같은 충돌 검사 |
| `Assets/1.Scripts/Player/Dash/DashInterruptPrediction.cs` · `Assets/1.Scripts/Player/Skill/InterruptTargetRegistry.cs` | 예측 3상태 enum(`InterruptAttemptForecast`)·대상 정면 판정(`InterruptTargetGeometry`)·대상 캐시. 옛 예측기는 191 에서 삭제 |
| `Assets/1.Scripts/Player/Dash/DashInterruptLayerFade.cs` | 상체 레이어 가중치 전환·인수인계 정규화 시각 |
| `Assets/1.Scripts/Player/Dash/PlayerDashData.cs` · `Assets/9.ScriptableObject/Player/PlayerDashData.asset` | SO 칸 3개 |
| `Assets/1.Scripts/Player/PlayerDashController.cs` | 상체 레이어 시작·인수인계·취소, `PlayDodgeEnd(keepBasePose)`, 창·허용오차 조회 |
| `Assets/1.Scripts/Player/PlayerStateController.cs` | `PlayerDashState` 창 입력·부착/분리·대시 끝 Skill 전이·강제 종료, `BeginSkillFromDash`·`AttachDashInterrupt`·`ActiveDash` |
| `Assets/1.Scripts/Player/Skill/PlayerSkillController.cs` | `TryUseDashInterrupt`, RPC 2개 플래그, 승인 분기, `HandleDashInterruptAborted`·`CompleteDashInterruptHandOff`, `PlayerSkillApprovalRules.EvaluateDashInterruptPreconditions`·`PlayerSkillApproval.DashWindow` |
| `Assets/1.Scripts/Player/Skill/PlayerInterruptSkillBase.cs` | Hit 보류·End 연기·`OnDashHandOff`·판정 Hit 지연(`TryGetJudgeHitDelay`)·서버 시도 슬로우 |
| `Assets/1.Scripts/Player/Skill/PlayerSkillBase.cs` · `HitboxAnchorShape.cs` | `OverlapHitboxAnchor(results, offset)` · 판정 앵커 형상(읽기·돌리기·옮기기·오버랩) — 판정·시도 예측 공용(할 일 139-3), 테스트 `Assets/1.Scripts/Player/Editor/HitboxAnchorShapeTests.cs` |
| `Assets/1.Scripts/Player/PlayerInputBuffer.cs` · `PlayerInputReader.cs` | `TryGetRecordedTime` · `TryGetPressTime` |
| `Assets/4.Animations/Player/{Garen,Assassin,Gunner}/*AnimatorController.controller` · `Garen/GarenUpperBody.mask` | `DashInterruptUpper` 레이어·팔라딘 상체 마스크 |
| `Assets/0.Scenes/Debug/DebugInterruptLab.unity` · `Assets/1.Scripts/Dev/InterruptLab/` | 디버그 씬(§10.1) — `DebugTimeScale`(+ `Editor/DebugTimeScaleEditor`)·`DebugDummyInterruptRecovery`·`DebugInterruptTuningPanel`(132 §17)·테스트 `Editor/DebugInterruptLabTests.cs`·`Editor/DebugInterruptLabIsolationTests.cs`. 기본 배속은 `Network/SlowMotion/GlobalTimeScale.cs` 의 `BaseScale` |
| `Assets/0.Scenes/Debug/InterruptCalibrationLab.unity` · `Assets/1.Scripts/Dev/InterruptLab/Calibration/` · `DebugDummyInterruptWindowMode.cs`·`DummyInterruptWindowSchedule.cs` | 예측 보정 랩(§10.2, 할 일 139) — 캡처·짝짓기·질문 창·기록·주기 창. 테스트 `Editor/InterruptCalibration*Tests.cs`·`DummyInterruptWindowScheduleTests.cs`, 메뉴 `Tools/Tests/간파 랩 EditMode 테스트 실행` |
| `Assets/1.Scripts/Dev/InterruptLab/InterruptJudgeShapeGizmos.cs`·`InterruptJudgeShapeGizmoRules.cs` | 보정 랩 간파 판정 범위 기즈모(§10.2, 할 일 156) — 형상 = `PlayerInterruptSkillBase.TryGetPlacedJudgeShape`, 선분 = `AttackShapeDrawing.CollectAnchorSegments`. 테스트 `Editor/InterruptJudgeShapeGizmoTests.cs` |
| 테스트 `Assets/1.Scripts/Player/Dash/Tests/EditMode/DashInterruptRulesTests.cs` · `DashInterruptLayerFadeTests.cs` · `Assets/1.Scripts/Player/Editor/PlayerSkillApprovalRulesTests.cs` · `PlayerInputBufferTests.cs` | 창 경계·0=끔·허용오차·판정 시점·입력 결정·승인 사전조건·가중치 전환·버퍼 기록 시각 |
