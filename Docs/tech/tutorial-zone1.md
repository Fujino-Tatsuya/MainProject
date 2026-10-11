# 튜토리얼 존1 흐름 (할 일 15 · S7)

> 원본 시나리오: [../design/tutorial-scenario.md](../design/tutorial-scenario.md) §3.0·§3.1·§5(공통·존1) · 계획: [../../PLAN-tutorial.md](../../PLAN-tutorial.md) S7.
> 디렉터 API 전체는 [tutorial-director.md](tutorial-director.md) §10, 훅은 [tutorial-player-hooks.md](tutorial-player-hooks.md), 안내 UI 는 [tutorial-guide-ui.md](tutorial-guide-ui.md).

## 파일
| 무엇 | 경로 |
|---|---|
| 흐름(런타임, 모든 피어) | `Assets/1.Scripts/Map/Tutorial/Flow/TutorialZone1Flow.cs` |
| 순수 규칙 | `Flow/TutorialZone1Model.cs` — `TutorialZone1Rules`(이동 보고 상한) · `TutorialMoveAccumulator` · `TutorialTooltipHold` · `TutorialLandingTimer` · `TutorialZone1Progress`(목표 순서·대기 줄) |
| 허수아비 자리 | `Flow/TutorialZone1Dummies.cs`(StageTutorial 자식, 비네트워크) |
| 저작 | `Flow/Editor/TutorialZone1Authoring.cs` — 메뉴 `Tools/Map/Authoring/Tutorial/8. Place Zone1 Dummies (StageTutorial Prefab)`. `2. Build StageTutorial Prefab` 도 같이 만든다 |
| 허수아비 변형 | `Assets/2.Prefabs/TrainingDummy_Tutorial.prefab` — `TrainingDummy.prefab` 의 Prefab Variant, `TrainingDummyInterrupt` 제거(간파 노랑 사이클 없음). `DefaultNetworkPrefabs.asset` 등록 |
| 테스트 | `Map/Tutorial/Editor/TutorialZone1ModelTests.cs`(튜토리얼 러너) |

## 붙는 방식
`TutorialDirector.StateChanged` 에서 `IsTutorialActive` 가 보이면 디렉터 GameObject 에 `AddComponent`(모든 피어 — 사망 규칙 S11 과 같은 부트스트랩, 씬·프리팹 저장 없음). 절차 맵은 디렉터가 안 켜져 안 붙는다.

## 판정
| 항목 | 피어 | 방법 |
|---|---|---|
| S0 환영 | 오너 | `PlayerGroundingSensor.IsGrounded` 가 **연속 1초**(낙하·튕김·사망 중 0) → 자막 `TUT_S0_WELCOME` 1번 |
| Z1-1 이동 5m | 오너 → 서버 | XZ 누적(한 프레임에 30m/s 넘는 이동 = 순간이동으로 버림, 사망 중 끊음) → `TutorialDirector.ReportLocalMoveDistance(m)` 1초 간격 재시도. 서버 `ReportOwnerMoveServerRpc`: G1 전·PlayerObject·Alive + **상한** = 5m ≤ 보고 ≤ 30 m/s × (튜토리얼 활성 뒤 경과) + 3m |
| Z1-2 대시 | 서버 | `PlayerDashController.ServerDashApproved` → `ReportCheckServer(Dash)` |
| Z1-3 적중 5회 | 서버 | 각 `Player.ServerAttackLanded`(0.5초마다 접속 플레이어 훑어 구독) — `AttackType.Default` 이고 대상에 **서버가 스폰한 존1 허수아비**가 있으면 판정 1번 = 1회(관통 다중 적중도 1) |
| Z1-4 패시브 1초 | 오너 → 서버 | `SkillTooltipView.TooltipShown("Passive")` 뒤 1초 안에 `TooltipHidden`/다른 키가 안 오면 `ReportLocalCheck(PassiveTooltip)`. **이동·대시·평타를 마친 뒤에만** 센다(§3.1 순서) |
| Z1-5 | 오너 | 위 완료 뒤 호버가 끝나면 자막 `TUT_Z1_PASSIVE_INFO`(캐릭터 변형) → 목표 `TUT_Z1_GO` |
| 거너 과열 | 오너 | 로컬 `GunnerHeat.StageChanged` ≥ 1, 평타 미완료 → `TUT_Z1_ATTACK_HEAT` 1번 |
| R 취소 팁 | 오너 | 로컬 `PlayerSkillTargeting.TargetingStarted(Ultimate)` → `TUT_TIP_R_CANCEL` 1번(존1 동안) |
| G1 | 서버 | 디렉터 모델 그대로(전원 완료 / 첫 완료자 뒤 60초). 열리면 다음 프레임 `SetStepServer(Corridor1)` |

## 안내 UI
- 목표 = 이동 → 대시 → 평타(`{n}`/5) → 패시브(P 칸 펄스) → 복도로. 체크리스트 4줄(`TUT_Z1_CHECK_*`). 이동·대시·평타는 순서 무관하게 체크된다.
- 동료 대기 줄 = 디렉터 `TryGetWait` 가 G1 이고 내가 넷 다 했을 때만(`TUT_WAIT_PARTY`, 남은 초 = 서버 값).
- 매번 디렉터 복제 상태로 다시 계산한다. **처음 그릴 때는 `RebuildFromState`**(늦은 클라 — 지나간 자막 생략, 효과음 없음).
- G1 이 열리면 존1 을 지켜본 클라만 목표 `TUT_Z1_GO` 를 남기고 대기 줄·P 강조를 지운 뒤 **손을 뗀다** — 복도1·존2 안내는 S8.
  G1 뒤에 처음 그리는 클라(늦은 클라)에게는 아무것도 쓰지 않는다.
- `TutorialGuideUI.prefab` 을 `4.MapScene` 루트에 놓았다(S7 확인용, 추가만 — 디렉터 hash 114710176 유지).

## ⚠️ 문구 id 변형 충돌
문구 변형 규칙은 `id + _PALADIN/_GUNNER/_ASSASSIN` 행이 **비어 있지 않으면** 그 행을 쓴다(목표 줄 포함). 시나리오 §5 의
`TUT_Z1_ATTACK_GUNNER`(과열 자막)·`TUT_Z1_PASSIVE_{캐릭터}`(패시브 자막)는 목표 줄 `TUT_Z1_ATTACK`·`TUT_Z1_PASSIVE` 의 변형 이름과 같아,
채우면 그 캐릭터의 **목표 줄이 자막 문구로 바뀐다**. 그래서:
- 자막은 `TUT_Z1_ATTACK_HEAT` · `TUT_Z1_PASSIVE_INFO_{캐릭터}` 행에 넣었다.
- `TUT_Z1_ATTACK_GUNNER` · `TUT_Z1_PASSIVE_PALADIN/_GUNNER/_ASSASSIN` 행은 **비워 둔다**(S6 시드라 저작 메뉴가 다시 만들어도 빈 행). 채우지 말 것.
- 기획 문서(시나리오 §5) id 를 바꿀지는 기획 결정 — 바꾸면 이 두 상수(`TutorialZone1Flow.AttackHeatId`·`PassiveInfoId`)만 고친다.

## 디렉터에 더한 것 (S7)
- `void ReportLocalMoveDistance(float meters)`(오너) → `ReportOwnerMoveServerRpc`(서버 상한 검증, `TutorialZone1Rules.IsMoveReportPlausible`).
- 활성 시각 `_activatedServerTime`(ServerTime) — 상한 검증의 기준.
- S1 `TODO(S7)` 자리: 거리 없는 `ReportLocalCheck(Move)` 는 서버가 거부(로그) — 이동은 거리 보고로만.

## Play 확인 (사람 — MPPM 호스트+클라)
- 존1 에 허수아비 3개(노랗게 안 빛남)가 모든 피어에 보인다 · `[Tutorial] 존1 흐름 켜짐 (서버:True) — 허수아비 3개` 로그.
- 착지 1초 뒤 환영 자막(낙하 중엔 안 뜸) · 목표 패널이 이동 → 대시(+보조 자막) → 평타 n/5 → 패시브(P 칸 펄스) 순서.
- 5m 이동(클라 포함 — 서버 거부 경고가 없어야 함) · 대시 1회 · 허수아비 평타 5회(상자·다른 공격은 안 셈) · P 칸 툴팁 1초(스치기는 안 셈) → 호버를 떼면 캐릭터별 패시브 자막 → "복도로".
- 거너: 과열 1단계에 과열 자막 1번 · 팔라딘/거너 R 조준 시작에 R 취소 팁 1번.
- 한 명만 끝내면 대기 줄(1/2 · 카운트다운) → 전원 완료 또는 60초 뒤 G1 배리어 열림 · 단계 Corridor1 로그.
- 사망·관전 중에도 목표 패널이 보인다(안내 UI 캔버스).

## 남은 것
- 복도1 이후 목표 패널·대기 줄(G2)은 S8. G1 뒤 단계 Corridor1 에서 S8 이 목표를 덮어쓴다.
- 허수아비는 G1 뒤에도 남아 있다(존1 에서만 보임 — 치워야 하면 S12).
