# 튜토리얼 존4 흐름 + 보스 진입 안내 — 설정 → 타이머 → 패널·다리·중간보스 (할 일 15 · S10)

> 원본: [../design/tutorial-scenario.md](../design/tutorial-scenario.md) §3.7(Z4-0~Z4-6)·§3.9(B-1·B-2)·§4 G7~G10·D4·D6·D17·D19 · 계획: [../../PLAN-tutorial.md](../../PLAN-tutorial.md) S10.
> 디렉터: [tutorial-director.md](tutorial-director.md)(§8 장부·§9 보스 타이머/보스 길) · 안내 UI: [tutorial-guide-ui.md](tutorial-guide-ui.md) · 존3(같은 구조): [tutorial-zone3.md](tutorial-zone3.md).
> 테스트: `TutorialZone4RulesTests`(튜토리얼 러너 `Tools/Tests/튜토리얼 EditMode 테스트 실행`).

## 1. 파일
| 무엇 | 경로 | 피어 |
|---|---|---|
| 순수 규칙 | `Map/Tutorial/Flow/TutorialZone4Rules.cs` — `TutorialZone4Model`(설정 자막→G8·기상 지연·패널/다리/간파 리마인드·만료·패드·도착 팁) · `TutorialZone4Display`(목표·강조) | — |
| 흐름 | `Map/Tutorial/Flow/TutorialZone4Flow.cs`(보스 진입 안내 포함) | 서버 |
| 표시 | `Map/Tutorial/Flow/TutorialZone4Guide.cs`(목표·강조·도착 정리) | 모든 피어 |

부착: 존3 과 같다 — `TutorialDirector.StateChanged` 에서 `IsTutorialActive` 일 때 디렉터 GameObject 에 런타임 `AddComponent`(씬 무수정).
보스 진입 안내는 존4 파일에 둔다(별도 `TutorialBossEnterFlow` 없음). 디렉터의 `HandleBossRoomArrived` 는 단계 Finished 만 정하고(남은 `TODO(S12)` 주석은 낡은 표시) — 안내 정리·23호 팁은 이 흐름이 `BossRoomArrived`·`IsFinished` 를 구독해 한다.
S12-2 B: 보스방 도착(Finished) 뒤에는 존4 설정·기상·만료·패드 시계가 멈춘다(23호 팁만). 복도4 고르기·대기·발판 목표를 존4 "보스방으로" 목표가 덮어쓰지 않는다.

## 2. 흐름 (서버 판단 → 복제/RPC 표시)
| 순서 | 트리거 | 서버 | 표시 |
|---|---|---|---|
| Z4-0 | G7 열림 ∧ `AreaFirstEntered(Zone4)`(순서 무관) | `SetStepServer(Zone4Lore)` · `ShowTimerHudServer()` · 존4 휴면 유지 | 자막 `TUT_Z4_LORE` → `TUT_Z4_TIMER_HINT`(전원) · 타이머 칸 펄스 |
| Z4-1 | 설정 자막 시작 + **10초** | `StartBossTimerServer()`(= G8 → 420초) | — |
| Z4-1/2 | G8 열림(어느 경로든 — `GateOpened`) | `SetStepServer(Zone4Combat)` · 3초 뒤 `WakeZoneServer(Zone4, NormalOnly)` | `TUT_Z4_START` → `TUT_Z4_PANEL_INTRO` · 목표 `TUT_Z4_PANEL_GOAL` ({n}/{total}) · 펄스 끔 |
| Z4-3 | `ZoneBridgeGateManager.ServerPanelActivated` | `AddCounterServer(Zone4Panels)` | 첫 1번 `TUT_Z4_PANEL_FIRST` · 목표 n 갱신 |
| Z4-4 | `ZoneBridgeGateManager.ServerBridgeOpened`(다리 진행도 1) | `WakeZoneServer(Zone4, KeyOnly)`(중간보스) | `TUT_Z4_BRIDGE` · 목표 `TUT_Z4_MIDBOSS`(패널 다 켠 순간부터) · 강조 가장 가까운 계단 + 중간보스 |
| Z4-5 | 중간보스 `MonsterCounterWindow.IsOpen` 첫 상승(다리 뒤·처치 전) | — | `TUT_Z4_COUNTER_REMIND` 1번 |
| Z4-6 | `ZoneCleared(Zone4)`(장부 — 중간보스 처치, D4) | G9 는 장부가 연다(여기서 안 엶) | `TUT_Z4_CLEAR` → `TUT_Z4_GO_BOSS` · 목표 `TUT_Z4_GO_BOSS` · 미니맵 보스 아이콘 펄스 |
| 만료 | `BossTimerManager.State == Expired`(서버 폴링) | — | `TUT_TIMER_EXPIRED` 1번(도착 전) |
| B-1 | `BossTeleportManager.IsCountdownActive` 상승(만료 강제 이동 제외) | — | `TUT_B_PAD_COUNT`(재시작은 8초 간격) |
| B-2 | `TutorialDirector.BossRoomArrived`(→ 단계 Finished) | 1초 뒤 팁 | 각 클라 `TutorialGuideUI.Clear()`(복제 `IsFinished`) → `TUT_B_BOSS_FRONT_TIP`(D19) |

- 자막 = 디렉터 `ShowSubtitleServer(id, args, targets)` 하나(전부 전원). 목표·강조는 각 피어가 복제 값(`Step`·`GetCounter(Zone4Panels)`·`IsGateOpen(G9)`·`IsFinished`)으로 다시 그린다 — 목표/강조용 디렉터 RPC 없음.
- 목표 `TUT_Z4_GO_BOSS` 는 G9 부터 도착까지 유지(복도4 특성 대기 줄은 별도 `SetWaitParty`). 내가 띄운 목표(`TUT_Z4_*`)만 지운다.
- 디버그로 G8 을 먼저 열면 설정 자막 없이 바로 전투(자막 START·PANEL_INTRO 는 나간다).

## 3. G8 시점 근거
- 시나리오 §3.7 Z4-1 "설정 자막이 끝나면(약 8~10초)". 문구 행 `TUT_Z4_LORE` 5초 + `TUT_Z4_TIMER_HINT` 5초 = **10초**(`TutorialZone4Model.DefaultLoreSeconds`, 흐름 인스펙터 `loreSeconds`).
- 서버는 자막 큐를 모른다 — 고정값이다. 문구 표시 시간을 바꾸면 이 값도 맞출 것. 존4 진입 때 다른 자막이 큐에 남아 있으면(예: 복도3 특성 안내) 화면에서는 설정 자막이 그만큼 늦게 끝난다.
- `TUT_Z4_START` 를 3초로 둬서 `TUT_Z4_PANEL_INTRO` 가 몬스터 기상(G8+3초)과 같이 뜬다.

## 4. 경석 영역 훅 (기본 = 기존 동작)
`ZoneBridgeGateManager` 에 읽기 전용 정적 이벤트 2개(서버) — 구독자가 없으면 아무 일도 없다.
- `ServerPanelActivated(int slotId, int panelIndex, int activated, int panelCount)` — 서버 RPC 가 패널을 새로 켠 직후(중복 요청은 안 냄).
- `ServerBridgeOpened(int slotId)` — `TickOpening` 에서 진행도 1 도달, 슬롯마다 1번(NavMesh 재베이크 호출 직전).
- 타이머 만료·패드 카운트다운은 이벤트 없이 복제 상태(`State`·`IsCountdownActive`)를 서버에서 폴링 — `BossTimerManager`·`BossTeleportManager` 무수정.
- 슬롯 필터 없음: 튜토리얼에서 다리 게이트는 존4(`ZoneL_typeB`) 하나뿐이라 모든 패널을 존4 패널로 센다.

## 5. 강조 대상 찾기 (클라)
- 계단 = `ZoneBridgeGate`(존 루트) 아래 이름 `floor_staire*` 4개 중 로컬 플레이어에게 가장 가까운 것(0.5초마다 다시 고름). 없으면 경고 + 중간보스만.
- 중간보스 = 존 루트 25m 안 비일반(`Rank != Normal`) 몬스터. 다리 조각은 `ZoneBridgeGate` 가 Transform 을 공개하지 않아 강조하지 않는다.
- 타이머 칸 `TutorialHudTarget.BossTimer`, 미니맵 보스 아이콘 `TutorialHudTarget.MinimapBossIcon`(`Role_Boss`).

## 6. 조사 — 다리 개통 NavMesh 재베이크 · 고정 조각 에러 (고치지 않음)
- **재베이크 끊김 — 난다(호스트).** `4.MapScene.unity` 의 `ZoneBridgeGateManager.rebakeNavMeshOnOpen: 1`(코드 기본값은 false, 툴팁이 "기본은 끔"이라 적어 둠).
  개통 완료 프레임에 서버가 `MapNavMeshBaker.RebakeNow` → `NavMeshSurface.BuildNavMesh()` **동기**(맵 전체 서피스, 원점~x≈500 — 주석상 수백 ms) + `BakeOpenScope`·`UnreadableMeshColliderBakeScope` + 전 에이전트 재부착.
  그런데 맵 생성 베이크가 이미 `BakeOpenScope` 로 다리를 연결 상태로 굽고 평상시엔 `NavMeshObstacle` 카브로 막는다(`ZoneBridgeGate.ApplyOpenProgress(1)` 에서 카브 해제) → 재베이크는 **중복**이다.
  끊김은 `TUT_Z4_BRIDGE` 자막·중간보스 기상과 같은 프레임(서버 → 클라는 복제·RPC 지연으로 체감). 권장: 씬 값을 0 으로(경석 판단 — 이번에 씬 무수정).
- **고정 조각 에러 — 매 실행·모든 피어 1줄씩, 거짓 경보.** `ZoneL_typeB.prefab` `ZoneBridgeGate.segments` 4개 중 0·1번이 `HasOpenPosition: 0`·열림=닫힘 위치(로컬 z +11.95 / −11.55 — 움직이지 않는 끝 조각).
  `RegisterGate` 가 `CountUnauthoredSegments()` > 0 이면 `Edit.LogError("[BridgeGate] Slot …: 다리 2조각의 열림 위치가 미저작…")`. 2·3번(실제로 움직이는 조각)은 저작돼 있다.
  처리안(경석): 고정 조각을 segments 에서 빼거나, 열림=닫힘으로 `HasOpenPosition: 1` 저장(`Record Bridge OPEN Positions`) — 둘 다 공용 존 프리팹 수정이라 이번엔 손대지 않았다.

## 7. 판단(보수적으로 고른 것)
- `TUT_Z4_ULT`·`TUT_Z4_ULT_ASSASSIN` 은 문구만 채우고 띄우지 않는다(조건 — 궁극기 준비·분노 게이지 — 이 명확하지 않음).
- `TUT_Z4_MIDBOSS` 문구는 `{이름}` 자리표시자 없이 "중앙 섬의 중간보스를 처치하세요"(클라가 이름을 채울 경로가 없음).
- `TUT_B_PAD_WARN`(G10, D17)은 이 흐름이 아니라 복도 흐름(`TutorialCorridorFlow`, S8)이 G10 열림 때 전원에게 띄운다 — [tutorial-corridors-zone2.md](tutorial-corridors-zone2.md). 디렉터 `HandleModelGateOpened` 의 `TODO(S6)` 주석은 낡은 표시다.
- 도착 정리는 클라가 복제 `IsFinished` 로 `Clear()`(자막 포함). 23호 팁은 RPC 라 정리보다 먼저 도착하면 지워진다 → 서버가 1초 늦게 보낸다.
- 단계는 Zone4Lore·Zone4Combat 만 올린다(Corridor4·BossEnter 는 다음 단계 몫).

## 8. Play 확인 대기 (사람이 MPPM 에서)
- 존4 첫 진입 → 타이머 빈 게이지 등장 + 펄스 · 자막 2줄 전원 · 몬스터 안 움직임 · 로그 `존4 설정 자막 시작 — 10초 뒤 G8`.
- 10초 뒤 게이지 차오름(420초) · START → PANEL_INTRO · 목표 0/4 · 3초 뒤 일반 몹 기상(중간보스는 그대로).
- 패널 F → 목표 1/4…(누가 켜도) · 첫 패널만 PANEL_FIRST · 4/4 → 목표 MIDBOSS · 1.5초 뒤 BRIDGE + 중간보스 기상 · 계단(가까운 것)·중간보스 강조 · 호스트 끊김 체감.
- 중간보스 첫 노란빛 → COUNTER_REMIND 1번 · 처치 → CLEAR → GO_BOSS · 목표 GO_BOSS · 미니맵 보스 아이콘 펄스 · G9 개방.
- 패드 카운트다운 → PAD_COUNT(들락거려도 8초 안엔 1번) · 도착 → 목표·강조·자막 사라짐 → 1초 뒤 23호 정면 팁.
- 타이머 만료(디렉터 `bossTimerLimitSeconds` 짧게) → TIMER_EXPIRED 1번 · 강제 이동 카운트다운에 PAD_COUNT 안 뜸.
- 늦게 붙은 클라도 목표·강조가 같다.
