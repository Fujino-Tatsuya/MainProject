# 튜토리얼 존3 흐름 — 스피너봇 연계 연습 (할 일 15 · S9 → v8 V4)

> 원본: [../design/tutorial-scenario.md](../design/tutorial-scenario.md) §3.5(Z3-1~Z3-10)·§4 G4~G6·D2·D2-1·D25·D26·D18 — **v8**(2026-10-12) · 계획: [../../PLAN-tutorial.md](../../PLAN-tutorial.md) S9·§3-1 V4.
> 디렉터: [tutorial-director.md](tutorial-director.md) · 스피너봇 쪽(몬스터 훅·연습/연계 본체): [handoff-tutorial-spinner.md](handoff-tutorial-spinner.md) §4-2 · 안내 UI: [tutorial-guide-ui.md](tutorial-guide-ui.md).
> 테스트: `TutorialZone3RulesTests` · `SpinnerComboModelTests` · `SpinnerPracticeModelTests`(튜토리얼 러너 `Tools/Tests/튜토리얼 EditMode 테스트 실행`).
> v8 변경: '간파 3회' → **[간파 성공 → 취약 유지 → 우클릭 밀치기 → 유효 벽 충돌 → 회복] 연계 파티 합계 3회**, 3회째 회복 끝 = G5 → 본 전투. 거너 전용 간파 설명 제거.
> **사용자 변경(2026-10-13): 원위치 복귀 없음** — 회복이 끝나면 스피너봇은 **그 자리(벽 앞)**에서 다음 준비·간파. 복귀 단계(`Returning`)·복귀 무적/관통·5초 Warp·`TUT_Z3_RETURN` 호출을 지웠다(SO 행은 남김).

## 1. 파일
| 무엇 | 경로 | 피어 |
|---|---|---|
| 순수 규칙(흐름) | `Map/Tutorial/Flow/TutorialZone3Rules.cs` — `TutorialZone3Model`(시작·첫 성공·피드백·힌트·밀치기 힌트 간격) · `TutorialZone3Display`(목표·강조 — 연계 단계별) · `TutorialZone3Geometry`(연습 반경 · **유효 벽·연습 위치**) | — |
| 순수 규칙(연계) | `Tutorial/SpinnerPractice/SpinnerComboModel.cs` — `SpinnerComboModel`(단계·밀치기·벽 충돌·회복) · `SpinnerComboGeometry`(벽 면 거리·방향) | — |
| 흐름 | `Map/Tutorial/Flow/TutorialZone3Flow.cs` | 서버 |
| 표시 | `Map/Tutorial/Flow/TutorialZone3Guide.cs` | 모든 피어 |
| 쿨 초기화 실행 | `Map/Tutorial/Flow/TutorialCooldownReset.cs`(복도1 과 공용) | 서버 |
| 튜토리얼 스피너봇 | `Assets/2.Prefabs/Monster/Tutorial/SpinnerBot_Tutorial.prefab`(SpinnerBot.prefab Variant) · `Tutorial/SpinnerPractice/TutorialSpinnerPractice.cs`(연습 + 연계) · `TutorialSpinnerSpawnOverride.cs` · `TutorialSpinnerHealthBarRule.cs` | 서버 스폰 → 복제 |

부착: `TutorialZone3Flow`(서버)·`TutorialZone3Guide`(각 피어)는 `TutorialDirector.StateChanged` 에서 `IsTutorialActive` 일 때 디렉터 GameObject 에 런타임 `AddComponent`(씬 무수정).

## 2. 튜토리얼 스피너봇 = 전용 Variant + 스폰 치환 (S9 그대로)
- `TutorialSpinnerPractice` 는 NetworkBehaviour 라 런타임 `AddComponent` 를 못 한다 → 튜토리얼 전용 Variant `SpinnerBot_Tutorial` 을 서버 스폰 직전 훅 `MapContentSpawner.ServerMonsterPrefabOverride` 에서
  **`TutorialStageMarker.IsActive` 일 때만** 치환(`TutorialSpinnerSpawnOverride`). Variant hash 1723163300(원본 2973007222). 절차 맵·Debug 씬은 치환 없음.
- 체력바: Variant 만 — 연습·전환 대기(`IsHealthLocked`) = 회색 "연습 중 · 체력 잠금", 본 모드 = 빨강.

## 3. 흐름 (서버 판단 → 복제/RPC 표시)
| 순서 | 트리거 | 서버 | 표시(모든 피어) |
|---|---|---|---|
| (활성) | 디렉터 활성 | 유효 벽·연습 위치 계산 → `SetPracticeWall` · `RelocateAnchor`(휴면 중 이동) · 연습 반경 | — |
| Z3-1 | G4 열림 ∧ `AreaFirstEntered(Zone3)`(순서 무관) | 휴면 해제 → `EnterPractice()` · `SetStepServer(Zone3Practice)` | 자막 INTRO → PRACTICE_NOTE(공통만) |
| Z3-2 | 준비·간파(`ComboPhase.Ready`) | 원래 연습 루프(창 9초 간격·스핀만·돌진 피해 0) | 목표 `TUT_Z3_COUNTER_GOAL` + 진행도 n/3 · 강조 스피너봇 + 우클릭 칸 |
| Z3-3 | 준비 단계 간파 판정 | — | 빗나감 `TUT_Z3_MISS` · 이른 간파 `TUT_Z3_EARLY`(시전자만) · 연속 놓침 4·8… `TUT_Z3_HINT` |
| Z3-4 | 간파 성공(준비 단계) = `PlayerInterruptSkillBase.ServerInterruptSucceeded`(연습 중 · 시전자 존3 안 · 스피너봇 연습 컴포넌트 있음) | **파티 전원 우클릭 쿨 1회 초기화**(등록된 기본·대체 우클릭 스킬, `TutorialCooldownReset`) · 스피너봇 = 원래 그로기(기절) → **취약 유지** | 첫 성공만 `TUT_Z3_FIRST_SUCCESS` · `TUT_Z3_PUSH_INTRO` · 목표 `TUT_Z3_PUSH_GOAL` · 강조 **유효 벽(화살표)** + 스피너봇 + 우클릭 칸 |
| Z3-5 | 밀치기가 벽에 안 닿고 끝남(`PushEndedNoWall`) | 취약 유지 | `TUT_Z3_PUSH_HINT`(5초에 1번까지) |
| Z3-6 | 취약 중 우클릭 밀치기로 유효 벽 충돌(`WallHit`) | `AddCounterServer(Zone3Interrupts)` · 취약 종료 → 회복(0.6초) | `TUT_Z3_WALL_SUCCESS` ({n}/{total}) |
| Z3-7 | 회복 중(`Recovering`) | 정지 · 밀치기·간파 무시 · **원위치 복귀 없음**(사용자 변경) | 목표 `TUT_Z3_GOAL`(연계 n/3) · 강조 없음 · 자막 없음(`TUT_Z3_RETURN` 호출 제거) |
| Z3-8 | 회복 끝, 성공 1·2회(`ReadyAgain`) | **그 자리에서** 준비·간파로 · 다음 창 ≥ 4초 뒤 | `TUT_Z3_COUNTER_GOAL` |
| Z3-9 | 회복 끝, 성공 3회(`Completed`) | **`RequestGateServer(G5)`** → `EnterRealMode()`(교육 제한 해제 + 2초 정지) · `SetStepServer(Zone3Fight)` | `TUT_Z3_REAL` · 목표 `TUT_Z3_BOSS` · 강조 끔 · 체력바 빨강 |
| Z3-10 | `ZoneCleared(Zone3)`(장부 — 스피너봇 처치) | G6 은 장부가 이미 연다 | `TUT_ZONE_CLEAR` · G6 열림 → 존3 목표 지움 |

- **G5 = 수동 게이트**(v8): `TutorialFlowModel.TryGetGateCounter` 는 이제 아무 게이트도 돌려주지 않는다 — 연계 3회(카운터 3)만으로는 안 열리고 3회째 충돌 뒤 회복이 끝날 때 흐름이 연다.
  디버그로 G5 를 먼저 열면(연습 전) 휴면을 풀고 바로 본 모드(이전과 같음).
- 자막 = 디렉터 `ShowSubtitleServer(id, args, targets)` 하나. 지나가는 신호라 복제하지 않는다.
- 목표·강조 = 각 피어가 복제 값(`Step`·`IsGateOpen(G6)`·`GetCounter(Zone3Interrupts)`·**스피너봇 `ComboPhase`·유효 벽 지점**)으로 다시 그린다(0.2초 주기 — 스피너봇 복제 변수는 디렉터 `StateChanged` 로 오지 않음).
  유효 벽 강조 = 표시 지점(벽 면 위 + 1.5m)에 로컬 빈 GameObject 를 두고 `Highlight(Transform, Arrow)`.

## 4. 연계 규칙 (`SpinnerComboModel` · `TutorialSpinnerPractice`)
| 단계 | FSM | 체력 | 외부 넉백 | 공격·간파 창 | 이동 |
|---|---|---|---|---|---|
| Ready | 동작(연습 루프) | 잠금 | 받음 | 스핀만·창 9초 | 연습 반경 클램프 |
| Stunned | 원래 그로기 | 잠금 | 무시(슈퍼 아머) | 없음 | 우클릭 밀치기만 |
| Vulnerable | **정지**(`SetServerLogicSuspended`) — 시간 종료 없음 | 잠금 | 무시 | 없음 | 우클릭 밀치기만 |
| Recovering | 정지 | 잠금 | 무시 | 없음 | 없음(0.6초) → 끝나면 그 자리에서 Ready(3회째면 Done) |
| Done | (본 모드 전환 — 원래 스피너봇) | 감소 | 받음 | 원래 패턴 | 원래 리쉬 |

- **밀치기** = 취약(기절 포함) 중 **우클릭 간파 적중**(`AttackInfo.isInterruptAttack`, 창 밖) → SpinnerBot 이 `ISpinnerBotOverride.NotifyInterruptHit(공격자)` → 공격자 → 스피너봇 방향으로 3m / 0.35초(23호 `vulnerableKnockbackDistance` 3m 를 출발점).
  새 밀치기가 오면 방향·거리를 새로 잡는다. Q 넉백·일반 피해는 이 경로를 안 타고, 슈퍼 아머라 밀리지도 않는다.
- **유효 벽 충돌**(한 연계 1회) = 밀치기 이동 뒤 몸 가장자리 ↔ 유효 벽 면 ≤ `comboContactSlack`(0.4m), 또는 이동이 막혔고(NavMesh 끝) ≤ `comboBlockedContactSlack`(1.6m — NavMesh 가 벽에서 에이전트 반경만큼 안쪽에서 끝나서) ·
  **벽 쪽으로 민 것만**(밀치기 방향 · 방 안쪽 법선 ≤ −0.3). 상자·몬스터·플레이어·배리어에 벽에서 멀리 막히면 '빗나감'.
- **유효 벽 · 연습 위치**(`TutorialZone3Geometry.ChoosePracticeLayout`): 존3 슬롯 사각형의 **긴 변** 중 스폰에 가까운 쪽(같으면 로컬 +). 연습 위치 = 스폰의 긴 축 좌표(존 끝에서 `practiceEndClearance` 8m 안쪽으로 자름)에서
  벽 면 ↔ 몸 가장자리 `practiceWallGap` 4m(밀치기 1~2회). NavMesh 2m 안 가장 가까운 점으로 맞춘다(없으면 스폰 위치 + 경고). **첫 연계만** 이 위치에서 시작한다 —
  원위치 복귀가 없어 2·3회째 연계와 본 전투는 직전 벽 충돌 자리(벽 앞)에서 시작한다.
  2026-10-11 계산값(StageTutorial, 존3 중심 (0.18, −160.55) 20×40): 유효 벽 x ≈ 10.18(또는 −9.82 — 스폰이 가운데라 로컬 + 쪽), 연습 위치 z ≈ −148.55. **실제 벽 면·장애물 위치는 Play 확인(D26)**.
- **연습 반경**(준비 단계 클램프) = min(인스펙터 `practiceRadius` 6m, 연습 위치 → G6 배리어 박스 거리 − 몸 반경 − 여유 0.5). 기절·취약·회복 중에는 클램프하지 않는다(NavMesh·배리어 카브가 막는다).
  다시 Ready 가 되면 클램프가 다시 걸린다 — 벽 앞(연습 위치에서 약 4m)은 보통 반경 안이라 그 자리에 남지만, 취약 중 옆으로 많이 밀려 반경 밖에서 충돌하면 Ready 순간 반경 가장자리로 옮겨진다.
- 다음 간파 창 = 원래 간격(창 열림 + 9초)과 회복 끝 + `comboReadyDelay`(4초) 중 늦은 쪽 — 밀치기에 쓴 우클릭이 준비될 틈.
- ⚠️ 알려진 문제(대응 안 함 — 벽 판정 방식 사용자 논의 중): 원위치 복귀가 없어 2·3회째 연계는 스피너봇이 **벽에 붙은 채** 시작한다 → 간파 뒤 벽 쪽으로 한 번만 밀면(또는 거의 안 밀려도) 충돌로 세져 너무 쉬울 수 있다.
- 본 전투 전환(`EnterRealMode`) 때 `SpinnerComboModel.Finish()` — 정지·슈퍼 아머·취약 유지를 모두 끈다. 본 전투의 간파 성공은 원래 그로기(1초)뿐(우클릭 쿨 초기화 없음).

## 5. 우클릭 쿨 초기화 — 다른 레인과의 관계
- 쿨 장부(`PlayerSkillController`)는 **고치지 않았다**. 할 일 26 의 공개 API `ResetCooldownServer(skill)` 로 0 을 만든다(이미 준비된 스킬은 건너뜀, 오너 HUD 미러는 장부가 보냄).
- 대상 = 그 플레이어에 등록된 스킬 중 Slot == Interrupt 전부(`GetSkillIndex` ≥ 0 — 기본·대체).
- **순서**(할 일 26 의존): 같은 판정(`ResolveHit`) 안에서 스피너봇 `ServerCounterSucceeded`(→ 연계 시작 `ServerComboCounterAccepted`) → 26 의 시전자 쿨 감소 → `ServerInterruptSucceeded`(→ 여기서 파티 초기화) → `ServerInterruptResolved`(→ 빗나감·이른 간파 피드백).
  초기화를 감소 **뒤** 이벤트에서 하므로 시전자도 결과는 0. 이른 간파 판정의 '이번 판정 성공' = 같은 호출 안에서 `ServerInterruptSucceeded` 로 받은 시전자(`ServerInterruptResolved` 에서 소비 — 프레임 비교 없음).
  `ServerComboCounterAccepted` 는 존3 흐름이 더 구독하지 않는다(감소 전에 나서 초기화 시점으로 맞지 않음).
- 존3 의 카운터 대상은 스피너봇뿐이고 간파 창은 연계 준비 단계에서만 열리므로, 연습 중 존3 안 시전자의 간파 성공 = 연계 시작으로 본다.
- 파티 전원 = 접속 중 PlayerObject 가 있는 모두(영혼 포함 — 쿨만 0 이 된다).

## 6. 추가한 API
| 쪽 | 시그니처 |
|---|---|
| 디렉터(S9) | `ShowSubtitleServer(id, args, targets)` · `GetClientsOfCharacterServer(TutorialCharacter)` |
| 스피너봇(v8, 서버) | `SetPracticeWall(Vector3 pointOnWall, Vector3 inwardNormal, float bodyRadius)` · `bool RelocateAnchor(Vector3)` · 이벤트 `ServerComboCounterAccepted(practice, clientId)`(현재 구독자 없음) · `ServerComboEvent(practice, SpinnerComboEvent, successes)` · `ComboSuccesses` · `ComboTarget` |
| 스피너봇(v8, 모든 피어) | `SpinnerComboPhase ComboPhase` · `bool TryGetWallPoint(out Vector3)` |
| SpinnerBot(경석 — handoff) | `ISpinnerBotOverride.NotifyInterruptHit(ulong attackerClientId)` |
| 그 밖 | `SpinnerPracticeWindowSchedule.DelayUntil(float)` · `TutorialSpinnerPractice.SetPracticeRadius`·`PracticeAnchor`·`PracticeRadius` |

## 7. 판단(보수적으로 고른 것 — 결정)
- 23호 간파 게이지·제압·취약·벽 충돌은 **재사용하지 않음**(전부 `TwentyThreeBoss` private, 이벤트 없음). 방식만 따랐다 — handoff-tutorial-spinner §4-2.
- 기절 = 원래 간파 그로기(`MonsterCounterWindow.groggyDuration` − 0.1초) — 그로기가 끝나 FSM 이 추격을 고르기 전에 세운다. 세운 뒤는 Idle 자세(취약 전용 연출 없음 — 아트 결정).
- 기절 중에도 밀칠 수 있다(취약 = 기절 포함).
- 밀치기 빗나감 안내는 5초에 1번(동시 밀치기 도배 방지). 준비 단계 밖에서는 빗나감·이른 간파 안내를 하지 않는다.
- 이른 간파 = "적중 &gt; 0 ∧ 이번 판정 성공 없음 ∧ 창 닫힘 ∧ 시전자가 스피너봇 4m 안" **추정**(존3 수신자는 스피너봇뿐 전제 — S9 그대로).
- 거너 전용 `TUT_Z3_GUNNER` 호출 제거(행은 SO 에 남김).
- (사용자 변경 2026-10-13) 원위치 복귀 제거 — `SpinnerComboPhase.Returning`·`SpinnerComboEvent.ReturnStarted`·`NotifyReturnArrived`·인스펙터 `comboReturnSpeed`/`comboReturnTimeout` 삭제, 회복 끝(`Tick`)이 바로 `ReadyAgain`/`Completed` 를 낸다.
  `Done` 값은 5 → 4(복제 enum — 호스트·클라 같은 빌드라 무해). `TUT_Z3_RETURN` 상수·SO 행은 남김(호출 없음). 연습 반경 기준점(`_anchor`)은 옮기지 않는다(G6 배리어 기준 반경 계산과 짝).
- 목표 문구 "(n/3)" 의 3 = 디렉터 목표 복제값 `{total}`. 스피너봇 `comboTarget`(3)과 디렉터 `zone3InterruptTarget`(3)이 다르면 활성 때 경고.

## 8. Play 확인 대기
→ [tutorial-qa.md](tutorial-qa.md) 1부 "존3 — v8 V4 연계 연습" · 2부 "파티 공통 진행".
