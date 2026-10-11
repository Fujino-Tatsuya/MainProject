# 튜토리얼 진행 상태 — TutorialDirector

> 할 일 15 / S1 (2026-10-11). 원본 시나리오: [../design/tutorial-scenario.md](../design/tutorial-scenario.md) §2.1·§4 · 계획: [../../PLAN-tutorial.md](../../PLAN-tutorial.md).
> 코드: `Assets/1.Scripts/Map/Tutorial/` — `TutorialFlowModel`(순수 규칙) · `TutorialDirector`(서버 소유·복제) · `TutorialStageMarker`(판별).
> 테스트: 메뉴 `Tools/Tests/튜토리얼 EditMode 테스트 실행`(`TutorialFlowModelTests`). 튜토리얼 단계의 새 픽스처는 이 러너에 넣는다.

## 1. 판별 — 한 곳
- `StageTutorial.prefab` 루트에 `TutorialStageMarker`(비네트워크). 저작 메뉴 `2. Build StageTutorial Prefab` 도 붙인다.
- **튜토리얼 여부는 `TutorialStageMarker.IsActive` 하나만 본다.** 켜진 스테이지에 마커가 있으면 true, 절차 맵(`Stage1_Procedural`)·Debug 씬은 false.
  타이머 지연(S5)·패드 잠금·몬스터 휴면 등 튜토리얼 전용 규칙은 모두 이 값으로 거른다(절차 맵에 퍼지지 않게).

## 2. 배치 · 스폰 순서
- `4.MapScene` 루트 `TutorialDirector`(NetworkObject + `TutorialDirector`) — `ZoneBridgeGateManager`·`BossTeleportManager` 와 같은 씬 상주 NetworkObject.
- 서버가 `MapGenerator.OnGenerated` 를 받았을 때 마커가 있으면 활성화한다. **스폰 순서 가정 없음**: 생성 이벤트가 스폰보다 먼저 오면
  보류 플래그만 세우고 `OnNetworkSpawn`(IsServer) 에서 한 번만 적용한다(스폰 전 NetworkVariable/NetworkList 쓰기는 버려진다 — [PLAN-boss-timer.md](../history/PLANS/PLAN-boss-timer.md) 리스크 4).
  `OnGenerated` 구독은 `OnEnable`(스폰 전), 판정은 `NetworkManager.Singleton`(스폰 전 프로퍼티 null 함정).
- 네트워크 없이 단독 실행하면 스폰되지 않아 돌지 않는다(BossTimerManager 와 같음 — 호스트로 확인).

## 3. 상태(서버 쓰기 · 모두 읽기)
| 복제 값 | 내용 |
|---|---|
| `_active` | 튜토리얼 활성 |
| `_step` (`TutorialStep`) | 현재 단계 — 앞으로만 |
| `_openedGates` / `_timeoutGates` | 게이트 G1~G10 열림 비트 / 그중 교착 방지로 열린 것 (G5 = 스피너봇 본 모드, G8 = 타이머 시작) |
| `_players` (NetworkList) | 개인 진행: 체크 마스크(이동·대시·평타 적중 n/5·패시브 툴팁·Q·E·특성 복도1~4) · 접속 · 완전 사망 |
| `_counters` (NetworkList) | 파티 카운터: 존2 처치 n/9 · 존3 간파 n/3 · 존4 패널 n/4 |
| `_wait` | 대기 표시: 지금 열 차례인 개인 게이트 · 완료 n · 전원 수 · 자동 진행 시각(ServerTime, 첫 완료자 전 -1) |

늦은 클라는 이 값만으로 다시 그린다(이벤트 재생 없음).

## 4. 규칙 (`TutorialFlowModel`)
- 게이트는 **G1 → G10 순서로만** 열린다(앞이 닫혀 있으면 뒤는 거부).
- 개인 게이트 G1(이동·대시·적중 5·패시브 툴팁)·G2/G4/G7/G10(복도1~4 특성) = 참가자 **전원 완료**, 또는 **첫 완료자 후 60초**(서버 실제 시간 = `ServerTime` — 슬로우모션·솔로 일시정지 무관, 인스펙터 `deadlockSeconds`).
  시계는 그 게이트가 열 차례가 된 뒤부터 잰다. 첫 완료자가 나가도 시계는 계속 간다. Q·E 는 안내용(게이트 아님, D13).
- 카운터 게이트 G5 = 존3 간파 합계가 목표(3)에 닿으면 자동. 나머지(G3·G6·G8·G9)는 서버가 `TryOpenGateServer` 로 연다.
- '전원' = 접속 중 + PermanentDead 아님(Soul·사망 연출은 포함). 이탈·완전 사망 시 즉시 재판정. **참가자 0명이면 자동으로 아무것도 열지 않는다**(세션 종료·결과 화면이 처리).
- 같은 보고 중복·모르는 clientId 는 무시. 적중은 5에서, 카운터는 목표에서 멈춘다. 이탈한 id 는 다시 등록되지 않는다(재접속 없음).

## 5. API
| 쪽 | API |
|---|---|
| 서버 | `ReportCheckServer(clientId, TutorialCheck)` · `AddCounterServer(counter, n)` · `TryOpenGateServer(gate)` · `SetStepServer(step)` |
| 오너 클라 | `ReportLocalCheck(Move / PassiveTooltip)` → `ReportOwnerCheckServerRpc`(서버 재검증: 항목·G1 전·PlayerObject·이동은 Alive) — **초안, S7 에서 사용** |
| 모두 읽기 | `IsTutorialActive` · `Step` · `IsGateOpen` · `WasGateAutoOpened` · `GetCounter`/`GetCounterTarget` · `TryGetPlayerProgress` · `TryGetWait` |
| 이벤트(정적) | `StepChanged` · `GateOpened(gate, reason)` — 서버는 실제 이유, 클라는 Timeout/Manual 만 · `StateChanged`(아무 복제 변화 + 스폰 직후 1회 — 다시 그리기 신호) |

다음 단계가 붙일 자리는 `TutorialDirector` 클래스 주석(S2 구간 → `SetStepServer`, S3 배리어 → `GateOpened`/`IsGateOpen`, S4 → G3·Zone2Kills, S9 → Zone3Interrupts, S12 → Timeout 열림 시 특성 자동 획득).
