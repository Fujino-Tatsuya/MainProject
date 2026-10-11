# 튜토리얼 존3 흐름 — 스피너봇 간파 연습 (할 일 15 · S9)

> 원본: [../design/tutorial-scenario.md](../design/tutorial-scenario.md) §3.5(Z3-1~Z3-8)·§4 G4~G6·D2·D2-1·D2-2·D18 · 계획: [../../PLAN-tutorial.md](../../PLAN-tutorial.md) S9.
> 디렉터: [tutorial-director.md](tutorial-director.md) · 스피너봇 연습 모드(몬스터 쪽): [handoff-tutorial-spinner.md](handoff-tutorial-spinner.md) · 안내 UI: [tutorial-guide-ui.md](tutorial-guide-ui.md).
> 테스트: `TutorialZone3RulesTests`(튜토리얼 러너 `Tools/Tests/튜토리얼 EditMode 테스트 실행`).

## 1. 파일
| 무엇 | 경로 | 피어 |
|---|---|---|
| 순수 규칙 | `Map/Tutorial/Flow/TutorialZone3Rules.cs` — `TutorialZone3Model`(시작·집계·피드백·힌트) · `TutorialZone3Display`(목표·강조) · `TutorialZone3Geometry`(연습 반경) | — |
| 흐름 | `Map/Tutorial/Flow/TutorialZone3Flow.cs` | 서버 |
| 표시 | `Map/Tutorial/Flow/TutorialZone3Guide.cs` | 모든 피어 |
| 튜토리얼 스피너봇 | `Assets/2.Prefabs/Monster/Tutorial/SpinnerBot_Tutorial.prefab`(SpinnerBot.prefab Variant) · `Tutorial/SpinnerPractice/TutorialSpinnerSpawnOverride.cs` · `TutorialSpinnerHealthBarRule.cs` | 서버 스폰 → 복제 |

부착: `TutorialZone3Flow`(서버)·`TutorialZone3Guide`(각 피어)는 `TutorialDirector.StateChanged` 에서 `IsTutorialActive` 일 때 디렉터 GameObject 에 런타임 `AddComponent`(일반 MonoBehaviour — `TutorialDeathRules` 와 같은 방식, 씬 무수정).

## 2. 튜토리얼 스피너봇 = 전용 Variant + 스폰 치환
- `TutorialSpinnerPractice` 는 NetworkBehaviour 라 런타임 `AddComponent` 를 못 한다. 존3 스피너봇은 공용 `ZoneM_typeC` 스폰 지점 Override(`SpinnerBot.prefab`)에서 나온다 →
  공용 존·원본 SpinnerBot 은 고치지 않고 **튜토리얼 전용 Variant** `SpinnerBot_Tutorial`(+ `TutorialSpinnerPractice` + 머리 위 체력바 `OverheadHealthBar.prefab` + `TutorialSpinnerHealthBarRule`)을 만들고,
  서버 스폰 직전 훅 `MapContentSpawner.ServerMonsterPrefabOverride` 에서 **`TutorialStageMarker.IsActive` 일 때만** SpinnerBot → Variant 로 바꾼다(`TutorialSpinnerSpawnOverride`).
- Variant 는 `DefaultNetworkPrefabs.asset` 에 등록(클라도 같은 프리팹으로 복제). 참조는 그 목록에서 `TutorialSpinnerPractice` 가 붙은 SpinnerBot 을 찾는다(StageTutorial·씬 무수정).
  Variant NetworkObject `GlobalObjectIdHash` = 1723163300(원본 2973007222) — 파일에 오버라이드로 저장돼 있다(빌드 인스턴스도 같은 값).
- 못 찾으면 에러 로그 + 원래 SpinnerBot(연습 모드 없음). 절차 맵·Debug 씬은 마커가 없어 치환하지 않는다.
- 체력바: 원래 스피너봇엔 머리 위 바가 없다. Variant 만 — 연습·전환 대기(`IsHealthLocked`) = 회색 + "연습 중 · 체력 잠금", 본 모드 = 빨강.

## 3. 흐름 (서버 판단 → 복제/RPC 표시)
| 순서 | 트리거 | 서버 | 표시 |
|---|---|---|---|
| Z3-1 | G4 열림 ∧ `AreaFirstEntered(Zone3)`(순서 무관) | `SetMonsterDormantServer(spinner,false)` → `EnterPractice()` · `SetStepServer(Zone3Practice)` | 자막 INTRO → PRACTICE_NOTE → (거너만) GUNNER · 강조 스피너봇(월드) + 우클릭 칸 |
| Z3-2 | 연습 중 | — | 목표 `TUT_Z3_GOAL` ({n}/{total}) · 체력바 회색 |
| Z3-3 | `ServerInterruptResolved` hit 0, 시전자 존3 | — | 그 시전자만 `TUT_Z3_MISS` |
| Z3-4 | hit &gt; 0, 같은 판정에 스피너봇 성공 없음, 창 닫힘, 시전자가 스피너봇 4m 안 | — | 그 시전자만 `TUT_Z3_EARLY` |
| Z3-5 | `ServerCounterSucceeded`(대상 = 이 스피너봇만, 연습 중) | `AddCounterServer(Zone3Interrupts)` | 첫 성공만 `TUT_Z3_FIRST_SUCCESS`(전원) · 목표 n 갱신 |
| Z3-6 | `ServerWindowMissed` 연속 4·8·… | — | `TUT_Z3_HINT`(전원) |
| Z3-7 | G5(카운터 3 → 디렉터 모델 자동) | `EnterRealMode()`(2초 정지) · `SetStepServer(Zone3Fight)` | `TUT_Z3_REAL` · 목표 `TUT_Z3_BOSS` · 강조 끔 · 체력바 빨강 |
| Z3-8 | `ZoneCleared(Zone3)`(장부 — 스피너봇 처치) | G6 은 장부가 이미 연다(여기서 안 엶) | `TUT_ZONE_CLEAR` · G6 열림 → 존3 목표 지움 |

- 자막 = 디렉터 `ShowSubtitleServer(id, args, targets)` 하나(S7~S9 공용). 개인 피드백은 `targets: { clientId }`, 거너 전용은 `targets: GetClientsOfCharacterServer(Gunner)`(서버가 접속 플레이어를 `TutorialLocalPlayerInfo` 로 판별). 지나가는 신호라 복제하지 않는다.
- 강조는 디렉터 API 없이 흐름 파일(`TutorialZone3Guide`)이 클라에서 직접, 목표 패널도 클라가 복제 상태로 직접 그린다(목표용 디렉터 RPC 없음).
- 목표·강조 = 각 피어가 복제 값(`Step`·`IsGateOpen(G6)`·`GetCounter(Zone3Interrupts)`)으로 다시 그린다 → 늦게 붙은 클라도 `StateChanged` 한 번으로 같다. 내가 띄운 목표(`TUT_Z3_*`)만 지운다.
- 디버그로 G5 를 먼저 열면(연습 전) 휴면을 풀고 바로 본 모드. 본 모드 간파·놓침은 세지 않는다.

## 4. 연습 반경 — 출구 배리어(G6) 기준
- 반경 = min(인스펙터 `practiceRadius` 6m, 기준점(스폰) → G6 배리어 박스 거리 − 몸 반경 − 여유 0.5m). 0 미만이면 0(고정). 활성 순간 서버가 `TutorialAreaLayout`·`TutorialBarrierRules.BuildSpecs` 로 계산해 `SetPracticeRadius` 로 넣는다(로그 `[Tutorial] 존3 연습 반경 …`).
- 2026-10-11 StageTutorial 실측: 스폰 (0.18, −145.95)(슬롯2 저장 위치 (0.18, −160.95) · yaw 270° · 지점 로컬 (15,0,0)) · G6 배리어 (0.18, −140.35) 4.6×0.4 → 존 쪽 면 z −140.55 → 거리 **5.4m** −
  몸 0.5(NavMeshAgent·Capsule 반경) − 여유 0.5 = **4.4m**. 기존 6m 면 중심이 z −139.95 까지 가 배리어를 넘는다. 존 옆벽(±10m)·입구(35m)는 반경 밖.
- 본 모드는 클램프 없음(원래 리쉬) — 배리어(Wall 콜라이더 + NavMeshObstacle 카브)가 막는다. 돌진·넉백으로 넘지 않는지는 Play 확인 항목.

## 5. 추가한 디렉터 API (S9)
| 쪽 | 시그니처 |
|---|---|
| 서버 | `void ShowSubtitleServer(string id, string[] args = null, ulong[] targets = null)` — args = 키·값 번갈아 둔 평탄 배열(`{ "N","2" }`), targets null = 전원·빈 배열 = 안 보냄 · `ulong[] GetClientsOfCharacterServer(TutorialCharacter)` |
| 내부 | `[ClientRpc] ShowSubtitleClientRpc(string id, SubtitleArgs args, ClientRpcParams)` → `TutorialGuideUI.Instance?.ShowSubtitle(id, args)`(씬에 UI 없으면 로그만 — S12 배치 전) |

그 밖: `TutorialSpinnerPractice.SetPracticeRadius(float)`·`PracticeAnchor`·`PracticeRadius` · `MapContentSpawner.ServerMonsterPrefabOverride`(정적 훅).

## 6. 판단(보수적으로 고른 것)
- 이른 간파 = 판정 이벤트에 적중 대상이 없어 "적중 &gt; 0 ∧ 이번 판정 성공 없음 ∧ 창 닫힘 ∧ 시전자가 스피너봇 4m 안"으로 **추정**(존3 수신자는 스피너봇뿐 전제 — 상자가 생기면 오판). 전투 코드는 안 고쳤다.
- 빗나감·이른 간파는 시전자 개인 자막, 첫 성공·힌트·연습 끝·처치는 전원. 힌트는 연속 놓침 4의 배수마다(매 놓침마다가 아님 — 9초마다 도배 방지).
- 강조는 연습 중에만(G5 에서 끈다). 단계는 Zone3Practice·Zone3Fight 만 올린다(Corridor3 이후는 다음 단계 몫).
- 목표 문구 `TUT_Z3_GOAL` = "({n}/{total})" — 시나리오 "(n/3)" 의 3 을 목표 복제값으로.

## 7. Play 확인 대기 (사람이 MPPM 에서)
- 호스트 로그: `스피너봇 스폰 치환 SpinnerBot → SpinnerBot_Tutorial` · 장부 `Zone3 1/1 (핵심 SpinnerBot_Tutorial…)` · `존3 연습 반경 4.4m …` · 클라에도 같은 Variant(체력바 회색)가 보인다.
- 존3 첫 진입 → 기상·연습 · 자막 3줄(거너만 3번째) · 스피너봇·우클릭 칸 강조 · 목표 0/3.
- 9초 간격 창 · 성공 n/3(멀티: 누가 성공해도 1칸, 한 창 1번) · 첫 성공 자막 1번 · 빗나감/이른 간파 문구가 시전자에게만 · 4번 연속 놓침 힌트.
- 3번 → 2초 정지 → 본 모드(빨강 바·체력 감소·원래 패턴) · REAL 자막 · 목표 BOSS · 처치 → ZONE_CLEAR + G6 개방.
- 연습 중 스피너봇이 반경 4.4m 밖·G6 배리어 너머로 안 나간다 · 본 모드 돌진·넉백으로도 복도3 에 안 넘어간다.
- 늦게 붙은 클라(또는 UI 재로드)도 목표·강조가 같다 · 절차 맵 스피너봇은 원래 그대로(치환 없음·체력바 없음).
