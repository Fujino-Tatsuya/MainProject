# 튜토리얼 존1 흐름 (할 일 15 · S7 → v8 V1)

> 원본 시나리오: [../design/tutorial-scenario.md](../design/tutorial-scenario.md) §3.0·§3.1·§5(공통·존1) — **v8**(2026-10-12) · 계획: [../../PLAN-tutorial.md](../../PLAN-tutorial.md) S7·§3-1 V1.
> 디렉터 API 전체는 [tutorial-director.md](tutorial-director.md) §10, 훅은 [tutorial-player-hooks.md](tutorial-player-hooks.md), 안내 UI 는 [tutorial-guide-ui.md](tutorial-guide-ui.md).
> v8 변경: 평타 '허수아비 적중 5회' 제거 → **기본 공격·Q·E·R·우클릭 각각 실제 사용 1회**(적중 불필요) · **G1 은 60초 교착 방지 제외** · 체크리스트 8줄 · 캐릭터별 힌트.

## 파일
| 무엇 | 경로 |
|---|---|
| 흐름(런타임, 모든 피어) | `Assets/1.Scripts/Map/Tutorial/Flow/TutorialZone1Flow.cs` |
| 순수 규칙 | `Flow/TutorialZone1Model.cs` — `TutorialZone1Rules`(이동 보고 상한) · `TutorialZone1SkillRules`(공격·스킬 사용 판정) · `TutorialMoveAccumulator` · `TutorialTooltipHold` · `TutorialLandingTimer` · `TutorialZone1Progress`(목표 순서·체크리스트·대기 줄) |
| 허수아비 자리 | `Flow/TutorialZone1Dummies.cs`(StageTutorial 자식, 비네트워크) |
| 저작 | `Flow/Editor/TutorialZone1Authoring.cs` — 메뉴 `Tools/Map/Authoring/Tutorial/8. Place Zone1 Dummies (StageTutorial Prefab)`. `2. Build StageTutorial Prefab` 도 같이 만든다 |
| 허수아비 변형 | `Assets/2.Prefabs/TrainingDummy_Tutorial.prefab` — `TrainingDummy.prefab` 의 Prefab Variant, `TrainingDummyInterrupt` 제거(간파 노랑 사이클 없음). `DefaultNetworkPrefabs.asset` 등록 |
| 테스트 | `Map/Tutorial/Editor/TutorialZone1ModelTests.cs`(튜토리얼 러너) · G1 마스크·교착 제외는 `TutorialFlowModelTests` |

## 붙는 방식
`TutorialDirector.StateChanged` 에서 `IsTutorialActive` 가 보이면 디렉터 GameObject 에 `AddComponent`(모든 피어 — 사망 규칙 S11 과 같은 부트스트랩, 씬·프리팹 저장 없음). 절차 맵은 디렉터가 안 켜져 안 붙는다.

## 개인 항목 8개 = G1 (v8 §3.1·§4)
`TutorialFlowModel.G1Mask` = 이동 · 대시 · 패시브 툴팁 + **기본 공격 · Q · E · R · 우클릭**(`TutorialCheck.BasicAttack/SkillQ/SkillE/SkillR/SkillInterrupt`).
- 체크는 한 번 켜지면 끄지 않는다 — **존1 들어오기 전(G1 전 어디서든)에 쓴 것도 인정**, 사망·부활로 되돌리지 않는다.
- **G1 = 참가자 전원 완료일 때만**. 첫 완료자 뒤 60초 자동 진행이 없다(`TutorialFlowModel.HasDeadlockTimeout(G1) == false`). 특성 게이트(G2·G4·G7·G10)는 그대로 60초.
- 대기 줄(나는 끝냈고 동료가 남음) = `TUT_WAIT_PARTY_NOAUTO` "동료를 기다리는 중 (n/total)" — 카운트다운 없음(디렉터 `TryGetWait` 남은 초 -1 → 안내 UI 가 음수면 이 행).

## 판정
| 항목 | 피어 | 방법 |
|---|---|---|
| S0 환영 | 오너 | `PlayerGroundingSensor.IsGrounded` 가 **연속 1초**(낙하·튕김·사망 중 0) → 자막 `TUT_S0_WELCOME` 1번 |
| Z1-1 이동 5m | 오너 → 서버 | XZ 누적(한 프레임에 30m/s 넘는 이동 = 순간이동으로 버림, 사망 중 끊음) → `TutorialDirector.ReportLocalMoveDistance(m)` 1초 간격 재시도. 서버 `ReportOwnerMoveServerRpc`: G1 전·PlayerObject·Alive + **상한** = 5m ≤ 보고 ≤ 30 m/s × (튜토리얼 활성 뒤 경과) + 3m |
| Z1-2 대시 | 서버 | `PlayerDashController.ServerDashApproved` → `ReportCheckServer(Dash)` |
| Z1-3 기본 공격 | 서버 | **`Player.ServerBasicAttackStarted`**(신규 정적 이벤트) — 기본 공격 1타가 실제로 시작될 때(적중 무관): 가붕이 `DefaultAttackController.StartAttackServer` · 거너 `GunnerBasicAttack.ServerFire`(한 발) · 어쌔신 `AssassinBasicAttack.StartServer`(강타 포함). 거부된 요청·거너 준비 자세만으로는 안 난다. **R 대상 확정 좌클릭은 조준 경로라 기본 공격으로 안 센다** |
| Z1-4 패시브 1초 | 오너 → 서버 | `SkillTooltipView.TooltipShown("Passive")` 뒤 1초 안에 `TooltipHidden`/다른 키가 안 오면 `ReportLocalCheck(PassiveTooltip)`. **이동·대시·기본 공격을 마친 뒤에만** 센다(§3.1 순서). 호버가 끝나면 캐릭터별 자막 `TUT_Z1_PASSIVE_INFO` |
| Z1-5~8 Q·E·우클릭·R | 서버 | `PlayerSkillController.ServerSkillUsed`(서버 **승인** = 실제 발동 — 쿨타임 중 거부·`CanUse` 실패(어쌔신 분노 부족)·조준 취소는 승인이 없어 안 온다) → `TutorialZone1SkillRules.TryGetSkillCheck`(Main=Q · Sub=E · Ultimate=R · Interrupt=우클릭). 대체 슬롯(어쌔신 변신 Q/E)도 같은 슬롯 = 같은 항목 |
| — 거너 Q | 서버 | 승인 = 충전 시작이라 **승인으로 세지 않고**(`GetSkill(Main) is GunnerChargeLaserSkill`) **`GunnerChargeLaserSkill.ServerFired`**(신규 정적 이벤트 — 실제 발사, 대시 취소·피격 종료는 안 남)로 센다 |
| — R | 서버 | 팔라딘·거너 R 은 대상 지정 스킬이라 **대상 확정 뒤** 승인된다 = 발동. 어쌔신 R 승인 = 변신 진입 |
| 거너 과열 | 오너 | 로컬 `GunnerHeat.StageChanged` ≥ 1 → `TUT_Z1_ATTACK_HEAT` 1번(존1 동안) |
| R 취소 팁 | 오너 | 로컬 `PlayerSkillTargeting.TargetingStarted(Ultimate)` → `TUT_TIP_R_CANCEL` 1번(존1 동안) |
| G1 | 서버 | 디렉터 모델(전원 완료만). 열리면 다음 프레임 `SetStepServer(Corridor1)` |

### 허수아비 — 거너 R 대상 · 어쌔신 분노 (확인, 코드 변경 없음)
- 거너 R(`GunnerTrackingLaserData.targetableLayers` = Enemy(8)·EnemyHurtBox(14)) — `TrainingDummy_Tutorial` 루트 레이어 8 + Hurtbox 14 → 대상 지정 가능. `CanUse` = 플레이어 아님 · 체력 > 0 · 사거리.
- 어쌔신 분노 — `AssassinHitTargets.IsRewardTarget` 이 `TrainingDummy` 를 포함 → 일반 1타·강타 적중으로 분노 충전.

## 안내 UI
- 목표 = 이동 → 대시 → 기본 공격 → 패시브(P 칸 펄스) → Q → E → 우클릭 → R → 복도로(`TutorialZone1Progress.Resolve`). **먼저 쓴 항목은 ■ 인 채 건너뛴다**(다시 시키지 않음).
  목표 줄 "({n}/1)" 의 n = 0(지금 할 일은 아직 안 한 항목).
- 체크리스트 8줄(`TutorialZone1Progress.ChecklistOrder` · 행 `TUT_Z1_CHECK_MOVE/DASH/ATTACK/PASSIVE/Q/E/RMB/R`).
- **캐릭터별 힌트 = 목표 줄 변형 행**(v8 §5): `TUT_Z1_Q_GUNNER` · `TUT_Z1_E_PALADIN/_GUNNER/_ASSASSIN` · `TUT_Z1_R_ASSASSIN` 을 **일부러 채웠다** — 문구 변형 규칙(id + `_캐릭터`)으로 그 캐릭터의 Q/E/R 목표 줄이 힌트 문구로 바뀐다.
  나머지 캐릭터·항목은 기본 `TUT_Z1_Q/E/RMB/R`({Q}{E} = 툴팁 스킬 이름).
- 동료 대기 줄 = 디렉터 `TryGetWait` 가 G1 이고 내가 여덟 개 다 했을 때만(`TUT_WAIT_PARTY_NOAUTO`, 카운트다운 없음).
- 매번 디렉터 복제 상태로 다시 계산한다. **처음 그릴 때는 `RebuildFromState`**(늦은 클라 — 지나간 자막 생략, 효과음 없음).
- G1 이 열리면 존1 을 지켜본 클라만 목표 `TUT_Z1_GO` 를 남기고 대기 줄·P 강조를 지운 뒤 **손을 뗀다** — 복도1·존2 안내는 S8.
  G1 뒤에 처음 그리는 클라(늦은 클라)에게는 아무것도 쓰지 않는다.
- `TutorialGuideUI.prefab` 을 `4.MapScene` 루트에 놓았다(S7 확인용, 추가만 — 디렉터 hash 114710176 유지).

## ⚠️ 문구 id 변형 충돌
문구 변형 규칙은 `id + _PALADIN/_GUNNER/_ASSASSIN` 행이 **비어 있지 않으면** 그 행을 쓴다(목표 줄 포함). 시나리오 §5 의
`TUT_Z1_ATTACK_GUNNER`(과열 자막)·`TUT_Z1_PASSIVE_{캐릭터}`(패시브 자막)는 목표 줄 `TUT_Z1_ATTACK`·`TUT_Z1_PASSIVE` 의 변형 이름과 같아,
채우면 그 캐릭터의 **목표 줄이 자막 문구로 바뀐다**. 그래서:
- 자막은 `TUT_Z1_ATTACK_HEAT` · `TUT_Z1_PASSIVE_INFO_{캐릭터}` 행에 넣었다.
- `TUT_Z1_ATTACK_GUNNER` · `TUT_Z1_PASSIVE_PALADIN/_GUNNER/_ASSASSIN` 행은 **비워 둔다**(시드·v8 메뉴 모두 빈 행 유지). 채우지 말 것.
- 반대로 v8 의 `TUT_Z1_Q_GUNNER`·`TUT_Z1_E_*`·`TUT_Z1_R_ASSASSIN` 은 **목표 줄을 바꾸는 것이 의도**라 채운다(위 '캐릭터별 힌트').
- 기획 문서(시나리오 §5) id 를 바꿀지는 기획 결정 — 바꾸면 `TutorialZone1Flow.AttackHeatId`·`PassiveInfoId` 상수만 고친다.

## 문구 반영
메뉴 **`Tools/UI/튜토리얼 안내 문구 v8 적용`**(`TutorialGuideUIAuthoring.V8Texts`) — v8 §5 행을 덮어쓴다(없으면 추가, 효과음 유지, `TutorialGuideTextTable.SetRowText`).
`Tools/UI/튜토리얼 안내 UI 구성` 의 시드(`TutorialGuideIds.All`)는 빠진 행만 빈 행으로 만든다. 🔴 GameData.xlsx 는 건드리지 않았다(Export 후보는 tutorial-qa §5).

## 디렉터에 더한 것 (S7 · v8)
- `void ReportLocalMoveDistance(float meters)`(오너) → `ReportOwnerMoveServerRpc`(서버 상한 검증, `TutorialZone1Rules.IsMoveReportPlausible`).
- 활성 시각 `_activatedServerTime`(ServerTime) — 상한 검증의 기준.
- v8: 복제 `PlayerState` 에서 `AttackHits` 삭제(적중 횟수 없음). `TutorialCheck` 값 10~12 추가(SkillR·SkillInterrupt·Corridor1CooldownReset). 모든 피어가 같은 빌드여야 한다.

## 플레이어 쪽 훅 (v8 — 은희 영역, 동작 무변경)
| 훅 | 위치 | 비고 |
|---|---|---|
| `Player.ServerBasicAttackStarted`(static) + `RaiseServerBasicAttackStarted()` | `Player.cs` | 서버만 발행(IsServer 가드). SubsystemRegistration 에서 비움 |
| 호출 3곳 | `DefaultAttackController.StartAttackServer` · `GunnerBasicAttack.ServerFire` · `AssassinBasicAttack.StartServer` | 한 줄씩 |
| `GunnerChargeLaserSkill.ServerFired`(static) | 발사 처리(`CommitCooldownServer` 직후) | 대시 취소 커밋 경로에서는 안 남 |

## Play 확인 (사람 — MPPM 호스트+클라)
→ [tutorial-qa.md](tutorial-qa.md) 1부 "존1"·2부 "개인 체크리스트 · 대기 줄".

## 남은 것
- (S8 해결) 복도1 이후 목표 패널·대기 줄(G2)은 복도 흐름 — [tutorial-corridors-zone2.md](tutorial-corridors-zone2.md). G1 뒤 단계 Corridor1 에서 복도 흐름이 목표를 덮어쓴다.
- (S12-2 B 해결) 허수아비 디스폰: G1 이 열리고 **살아 있는 플레이어가 존1 에 아무도 없거나 단계가 Zone2 이상**이면 서버가 디스폰한다(`TutorialZone1Rules.ShouldDespawnDummies`).
- 스킬 칸 강조(Q/E/R/우클릭 차례에 그 칸 펄스)는 v8 표에 없어 넣지 않았다 — 필요하면 `TutorialHudTarget.SkillSlotQ` 등으로 P 칸과 같은 방식.
