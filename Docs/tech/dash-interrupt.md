# 대쉬 간파 — 대시 시작 직후 우클릭으로 대시하며 간파 준비, 대시 끝 지점에서 판정

> 할 일 115(2026-10-27, 브랜치 `feature/DashInterrupt`). 구현 커밋 C1 `07a85d46` · C2a `f0f56ee2` · C2b `a5af6d8b`.
> 설계안: `_lane-artifacts/MainProject-Worktree2/115/115-설계안.md`(레포 밖, §10·§11 확정 절이 우선). 문서와 코드가 다르면 코드가 기준이다.
> 연출 디렉터는 [interrupt-presentation.md §6](interrupt-presentation.md#6-간파-연출-디렉터--interruptproductiondirector-할-일-118), 입력 거부 피드백은 [skill-reject-feedback.md](skill-reject-feedback.md).

**한 줄 정의**: 대시를 시작하고 짧은 창(기본 0.1초) 안에 우클릭하면, 대시는 그대로 진행하면서(이동·무적·충돌 그대로) 상체만 간파 준비 자세를 취하고, **대시가 끝나는 지점·대시 방향**에서 간파를 판정한다.

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

- **시도**: 승인 순간 `OnServerStart` → `ServerAttempt(예측)` 그대로. 단 대쉬 간파는 예측 오버랩을 **예상 대시 끝**으로 옮긴다 — 오프셋 = 대시 방향 × 대시 속도 × 서버 남은 대시 시간(`DashInterruptRules.GetPredictedEndOffset` → `PredictsInterruptHit` → `OverlapHitboxAnchor(results, offset)`). 판정 오버랩은 옮기지 않는다(실제 위치).
- **판정·성공**: 대시 끝(또는 그 뒤 클립 Hit)의 `ResolveHit` 안 `ServerResolve`/`ServerSuccess` 그대로.
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
- 시도 예측 오프셋은 서버 남은 대시 시간 기준 직선 이동이다 — 대시가 유닛·벽에 막히면 예측이 실제 판정 위치보다 멀다(시도 슬로우 오발동 → 실패 복귀 가능).
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

## 11. 범위 밖

- 116(간파 예측 로그)·117(성공 예측 연출)은 이 작업 범위 밖이다. 116 은 §5 의 예측 오프셋 값을 그대로 기록하면 된다.

## 12. 관련 파일

| 파일 | 내용 |
|---|---|
| `Assets/1.Scripts/Player/Dash/DashInterruptRules.cs` | 순수 규칙 — 창 판정·입력 결정·서버 승인 창·판정 시점·max·예상 대시 끝 오프셋 |
| `Assets/1.Scripts/Player/Dash/DashInterruptLayerFade.cs` | 상체 레이어 가중치 전환·인수인계 정규화 시각 |
| `Assets/1.Scripts/Player/Dash/PlayerDashData.cs` · `Assets/9.ScriptableObject/Player/PlayerDashData.asset` | SO 칸 3개 |
| `Assets/1.Scripts/Player/PlayerDashController.cs` | 상체 레이어 시작·인수인계·취소, `PlayDodgeEnd(keepBasePose)`, 창·허용오차 조회 |
| `Assets/1.Scripts/Player/PlayerStateController.cs` | `PlayerDashState` 창 입력·부착/분리·대시 끝 Skill 전이·강제 종료, `BeginSkillFromDash`·`AttachDashInterrupt`·`ActiveDash` |
| `Assets/1.Scripts/Player/Skill/PlayerSkillController.cs` | `TryUseDashInterrupt`, RPC 2개 플래그, 승인 분기, `HandleDashInterruptAborted`·`CompleteDashInterruptHandOff`, `PlayerSkillApprovalRules.EvaluateDashInterruptPreconditions`·`PlayerSkillApproval.DashWindow` |
| `Assets/1.Scripts/Player/Skill/PlayerInterruptSkillBase.cs` | Hit 보류·End 연기·`OnDashHandOff`·시도 예측 오프셋 |
| `Assets/1.Scripts/Player/Skill/PlayerSkillBase.cs` | `OverlapHitboxAnchor(results, offset)` |
| `Assets/1.Scripts/Player/PlayerInputBuffer.cs` · `PlayerInputReader.cs` | `TryGetRecordedTime` · `TryGetPressTime` |
| `Assets/4.Animations/Player/{Garen,Assassin,Gunner}/*AnimatorController.controller` · `Garen/GarenUpperBody.mask` | `DashInterruptUpper` 레이어·팔라딘 상체 마스크 |
| 테스트 `Assets/1.Scripts/Player/Dash/Tests/EditMode/DashInterruptRulesTests.cs` · `DashInterruptLayerFadeTests.cs` · `Assets/1.Scripts/Player/Editor/PlayerSkillApprovalRulesTests.cs` · `PlayerInputBufferTests.cs` | 창 경계·0=끔·허용오차·판정 시점·입력 결정·승인 사전조건·가중치 전환·버퍼 기록 시각 |
