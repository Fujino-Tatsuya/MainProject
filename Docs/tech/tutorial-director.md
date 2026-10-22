# 튜토리얼 진행 상태 — TutorialDirector

> 할 일 15 / S1 (2026-10-11). 원본 시나리오: [../design/tutorial-scenario.md](../design/tutorial-scenario.md) §2.1·§4(**v8**, 2026-10-12 — 반영 계획 PLAN-tutorial §3-1) · 계획: [../../PLAN-tutorial.md](../../PLAN-tutorial.md).
> v8 에서 바뀐 디렉터 규칙: G1 = 여덟 항목·교착 방지 제외 · G5 = 수동(연계 3회 + 마지막 복귀) · 체크 항목 SkillR·SkillInterrupt·Corridor1CooldownReset 추가 · `AttackHits` 삭제. 목숨 차감 기준 G11 은 그대로(사용자 확정, §11).
> 코드: `Assets/1.Scripts/Map/Tutorial/` — `TutorialFlowModel`(순수 규칙) · `TutorialDirector`(서버 소유·복제) · `TutorialStageMarker`(판별).
> 테스트: 메뉴 `Tools/Tests/튜토리얼 EditMode 테스트 실행`(`TutorialFlowModelTests` · `TutorialAreaTrackerTests`). 튜토리얼 단계의 새 픽스처는 이 러너에 넣는다.
> S2 구간 감지는 §6. S3 배리어는 §7. S4 존 장부·몬스터 휴면은 §8. S5 타이머·보스 길은 §9. **단계별 문서·handoff 목록·API 전체는 §10.1.** S11 사망 규칙은 §11. 스텝별 고정 안전 지점(낙사 복귀, 할 일 80)은 §12.
> **Play 검증 체크리스트(S0~S12 전 단계·MPPM·회귀·병합 전 확인)**: [tutorial-qa.md](tutorial-qa.md).

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
| `_openedGates` / `_timeoutGates` | 게이트 G1~G11 열림 비트 / 그중 교착 방지로 열린 것 (G5 = 스피너봇 본 모드, G8 = 타이머 시작, G11 = 보스방 도착 — 목숨 차감 기준) |
| `_players` (NetworkList) | 개인 진행: 체크 마스크(이동·대시·기본 공격·패시브 툴팁·Q·E·R·우클릭·특성 복도1~4·복도1 쿨 초기화 기록 — v8) · 접속 · 완전 사망 · 지난 배리어 마스크(개인 뒤 배리어 — §7). v8 에서 적중 횟수(`AttackHits`) 삭제 |
| `_counters` (NetworkList) | 파티 카운터: 존2 처치 n/9 · 존3 **연계**(간파 → 취약 밀치기 → 벽 충돌) n/3 · 존4 패널 n/4 |
| `_wait` | 대기 표시: 지금 열 차례인 개인 게이트 · 완료 n · 전원 수 · 자동 진행 시각(ServerTime, 첫 완료자 전 -1) |

늦은 클라는 이 값만으로 다시 그린다(이벤트 재생 없음).

## 4. 규칙 (`TutorialFlowModel`)
- 게이트는 **G1 → G11 순서로만** 열린다(앞이 닫혀 있으면 뒤는 거부).
- 개인 게이트 G1(v8: 이동·대시·패시브 툴팁 + **기본 공격·Q·E·R·우클릭 각 1회** = `TutorialFlowModel.G1Mask`)·G2/G4/G7/G10(복도1~4 특성) = 참가자 **전원 완료**.
  특성 게이트(G2/G4/G7/G10)만 **첫 완료자 후 60초** 자동 진행(서버 실제 시간 = `ServerTime` — 슬로우모션·솔로 일시정지 무관, 인스펙터 `deadlockSeconds`) —
  **G1 은 교착 방지 제외**(v8 §2.1, `HasDeadlockTimeout(G1) == false` — 대기 표시의 자동 진행 시각도 -1). 시계는 그 게이트가 열 차례가 된 뒤부터 잰다. 첫 완료자가 나가도 시계는 계속 간다.
- v8: **카운터로 자동 열리는 게이트는 없다**(`TryGetGateCounter` 항상 false). G5(스피너봇 본 모드) = 연계 3회 + 마지막 원위치 복귀 뒤 존3 흐름이 `RequestGateServer(G5)`. 나머지(G3·G6·G8·G9)는 서버가 `TryOpenGateServer`/`RequestGateServer` 로 연다.
- 개인 체크는 한 번 켜지면 끄지 않는다(사망·부활 무관). `Corridor1CooldownReset`(v8 복도1 첫 진입 쿨 초기화 기록)은 체크 마스크에 있지만 게이트 조건이 아니다.
- G11(보스방 도착) = 서버 판정 게이트(개인·카운터 아님, 배리어 없음). 디렉터 `HandleBossRoomArrived`(패드·만료 강제 이동 공통 `AlivePlayersArrived`)가
  단계 Finished **전에** 연다. 순서 규칙 그대로라 **G10 전에 도착(타이머 만료 강제 이동)하면 닫힌 채로 남는다** — 사망 규칙은 도착·Finished 로 이미 끝나므로 목숨 규칙엔 영향 없음.
- '전원' = 접속 중 + PermanentDead 아님(Soul·사망 연출은 포함). 이탈·완전 사망 시 즉시 재판정. **참가자 0명이면 자동으로 아무것도 열지 않는다**(세션 종료·결과 화면이 처리).
- 같은 보고 중복·모르는 clientId 는 무시. 카운터는 목표에서 멈춘다. 이탈한 id 는 다시 등록되지 않는다(재접속 없음).

## 5. API
| 쪽 | API |
|---|---|
| 서버 | `ReportCheckServer(clientId, TutorialCheck)` · `AddCounterServer(counter, n)` · `TryOpenGateServer(gate)` · `SetStepServer(step)` |
| 오너 클라 | `ReportLocalCheck(PassiveTooltip)` → `ReportOwnerCheckServerRpc`(서버 재검증: 항목·G1 전·PlayerObject) · 이동은 **`ReportLocalMoveDistance(m)`** → `ReportOwnerMoveServerRpc`(Alive·거리 상한 검증, S7). 거리 없는 `ReportLocalCheck(Move)` 는 서버가 거부한다 — [tutorial-zone1.md](tutorial-zone1.md) |
| 모두 읽기 | `IsTutorialActive` · `Step` · `IsGateOpen` · `WasGateAutoOpened` · `GetCounter`/`GetCounterTarget` · `TryGetPlayerProgress` · `TryGetWait` |
| 이벤트(정적) | `StepChanged` · `GateOpened(gate, reason)` — 서버는 실제 이유, 클라는 Timeout/Manual 만 · `StateChanged`(아무 복제 변화 + 스폰 직후 1회 — 다시 그리기 신호) |

S2~S5 에서 늘어난 API 전체 목록은 §10.1.

다음 단계가 붙일 자리는 `TutorialDirector` 클래스 주석(S3 배리어 → `GateOpened`/`IsGateOpen`, S4 → G3·Zone2Kills, S9 → Zone3Interrupts, S12 → Timeout 열림 시 특성 자동 획득).
S6~S12 는 모두 붙었다 — 실제로 붙은 곳은 §10.2(디렉터 안에 남은 `TODO(S6/S8/S12)` 주석은 낡은 표시다).

## 6. 구간 감지 (S2)
> 코드: `TutorialAreaTracker`(순수 규칙 · `TutorialRect` · `TutorialCorridorGeometry`) · `TutorialAreaLayout`(저작 데이터 → 모양, 기즈모) · `TutorialDirector`(서버 폴링·이벤트).
> 테스트: `TutorialAreaTrackerTests`(튜토리얼 러너).

**구간 모양 — 저작 데이터에서 계산(하드코딩 좌표 없음)**
- 존 = `StageTutorial` 슬롯 `SlotID` 0~3 → 존1~4, 4 → 보스 진입. 사각형 = 슬롯 `transform.position` · `Footprint` · 슬롯 Y 회전.
  스테이지 180° 회전은 저작 메뉴(`RotateAroundWalls`)가 슬롯 위치에 이미 구워 넣었다.
  존 프리팹 스폰 위치가 아니라 **슬롯 위치**를 쓴다 — 복도 문턱이 슬롯 사각형 경계에 맞춰 저작돼 있다(존3 M_typeC 는 피벗 때문에 스폰 위치가 z 0.4m 어긋남).
- 복도 n = 존n ↔ 존n+1(복도4 = 존4 ↔ 보스 진입) 사각형 사이 틈(길이) × 그 틈 가운데 절반에 놓인 실제 복도 바닥 콜라이더의 가로 범위(너비 4m).
  바닥 = 슬롯 밖, 비트리거, 두께 ≤ 1m, 윗면 ≤ 스테이지 높이 +0.5m(창틀 캡 제외). 못 찾으면 가운데 ±2m + 경고.
- 복도 중간 지점 = 복도 한가운데 선 → 다음 존 쪽 끝(뒤쪽 절반 전체 — 폴링 간격·대시로 얇은 띠를 건너뛰지 않게).
- 높이는 보지 않는다(XZ 평면). 벽 투명화 트리거(`Occlusion_zone_*`)는 연출용 크기라 쓰지 않는다(건드리지도 않음).

2026-10-11 `StageTutorial.prefab` 계산값(시나리오 §1.1 대조):

| 구간 | 계산(월드 XZ) | §1.1 |
|---|---|---|
| 존1 | 중심 (29.83, −200.4) 20×20 | (29.8, −200.4) |
| 복도1 | x 10.18~19.83, z −202.4~−198.4 · 중간 x 10.18~15.0 | x 10.1~19.9 |
| 존2 | (0.18, −200.4) 20×20 | (0.18, −200.4) |
| 복도2 | z −190.4~−180.55, x −1.82~2.18 · 중간 z −185.48~−180.55 | z −190.5~−180.7 |
| 존3 | (0.18, −160.55) 20×40 | (0.18, −160.95) — 스폰 위치(피벗) |
| 복도3 | z −140.55~−130.9 · 중간 z −135.73~−130.9 | z −140.9~−131.0 |
| 존4 | (10.17, −110.9) 40×40 | (10.17, −110.9) |
| 복도4 | x −19.47~−9.83, z −103.11~−99.11 · 중간 x −19.47~−14.65 | x −9.7~−19.6 |
| 보스 진입 | (−29.47, −101.1) 20×20 | (−29.5, −101.1) |

**판정 규칙**
- 서버만, 튜토리얼 활성(`TutorialStageMarker` 있음 → 디렉터 활성)일 때만. 주기 `areaPollInterval`(기본 0.15초, 실제 시간 — 슬로우모션 무관).
- 위치 = 서버가 보는 `PlayerObject.transform`. 원격 플레이어는 오너 권한 이동의 NetworkTransform 복제값이라 늦고 흔들린다 → 히스테리시스:
  새 구간은 경계에서 **안쪽 `areaEnterMargin`(기본 0.5m)** 까지 들어와야 진입, 지금 구간은 **바깥 `areaExitMargin`(기본 1.0m)** 을 넘어야 이탈. 둘 다 인스펙터 값.
- 🔴 **판정 대상 = `PlayerLifeState.Alive` 뿐.** 사망 연출·영혼(Soul)·완전 사망(PermanentDead)은 판정하지 않는다 — 현재 구간 None,
  존 첫 진입·복도 중간 지점 이벤트 모두 만들지 않는다. 되살아나면 그 자리부터 다시 판정(안 지나간 중간 지점이면 그때 발행).
  (게이트 '전원' 판정(§4)은 Soul 을 포함한다 — 둘은 다른 규칙이다.)
- 이탈한 플레이어는 현재 구간만 지운다(개인 1회 기록은 유지 — 재접속 없음).

**이벤트 · API (서버)**
| | |
|---|---|
| `TutorialDirector.AreaFirstEntered(area, clientId)` | 파티 첫 진입 — 존1~4·보스 진입 각각 판 전체 1번(복도는 없음). 존1 은 스폰 착지 직후 나온다 |
| `TutorialDirector.CorridorMidReached(clientId, corridor 1~4)` | 개인 첫 복도 중간 지점 — 플레이어마다 복도마다 1번, 왕복해도 재발행 없음 |
| `GetPlayerAreaServer(clientId)` · `HasPartyEnteredServer(area)` · `HasReachedCorridorMidServer(clientId, n)` | 조회 |

이벤트만 낸다. 단계 갱신(`SetStepServer`)·안내(S6)·존2/3/4 시작(S8~S10)·특성 창(S8/S12)은 구독해서 붙인다. 복제 상태는 아직 없다(늦은 클라는 `Step` 으로 그린다).

**디버그**: 씬에서 `Stage1`(StageTutorial 루트, `TutorialStageMarker`)을 선택하면 존(하늘색)·복도(노랑)·안쪽/바깥 여유(옅은 선)·복도 중간 지점(분홍) 기즈모.

## 7. 배리어 (S3)
> 코드: `TutorialBarrierRules`(순수 — 매핑·모양·진입 가능·추락 후보) · `TutorialBarriers`(런타임, 모든 피어) · `PlayerSafePointTracker.ReturnPointFilter`(훅).
> 테스트: `TutorialBarrierRulesTests`(튜토리얼 러너). 저작 메뉴: `Tools/Map/Authoring/Tutorial/5. Preview Barriers`(열린 씬, 저장 안 됨) · `6. Clear Barrier Preview` · `7. Check Barriers (StageTutorial Prefab)`.

**8개 = 복도 n 입구(앞 존 쪽)·출구(다음 존 쪽)**: 복도1 G1/G2 · 복도2 G3/G4 · 복도3 G6/G7 · 복도4 G9/G10. G5·G8·G11 은 배리어 없음.
- 모양 = §6 복도 사각형의 양 끝에서 **복도 안쪽으로** 두께 0.4m(존 사각형을 침범하지 않음), 가로 = 복도 너비 + 양옆 0.3m(복도 벽 안으로 파고듦), 높이 3m.
  2026-10-11 프리팹 점검: 8개 모두 양옆 벽 안쪽 면 ±1.38m, 벽 바깥 면 ±2.63m(배리어 반폭 2.3m — 벽 밖으로 안 나옴).
- 어디에: Play 중에만 `TutorialStageMarker.Start` 가 StageTutorial 루트에 `TutorialBarriers` 를 붙이고, 그것이 루트 아래 `TutorialBarriers/Barrier_G*` 를 만든다.
  🔴 공용 존 프리팹(Zones/*)·씬에는 아무것도 저장하지 않는다. 프리팹에 저장된 것은 마커의 머티리얼 참조 한 줄뿐.

**언제 · 어떻게 여나**
- 디렉터가 튜토리얼을 켠 것(`IsTutorialActive`)이 그 피어에서 처음 보일 때 만든다(`StateChanged`). 튜토리얼이 아니면 만들지 않는다.
- 이미 열린 게이트는 만들 때 바로 끈다(늦은 클라 = 스폰 직후 `StateChanged` 로 현재 상태). 이후 `IsGateOpen` 이 켜지면 콜라이더·카브를 **즉시** 끄고
  0.35초 페이드(실제 시간, 높이·알파) 뒤 비활성. 다시 닫히는 일은 없다(게이트는 앞으로만).

**막기**
- 플레이어: **Barrier 레이어**(19 — 할 일 32 로 Wall 에서 분리, [wall-concept.md](wall-concept.md)) 비트리거 BoxCollider. 플레이어 모터 스윕 마스크(`PlayerGameRuleData.obstacleMask` = Default|Ground|Wall|Enemy|Env|Barrier)가 본다.
  배리어는 벽이 아니다 — 공용 벽 충돌 이벤트(`UnitWallImpact`)가 나지 않는다.
  걷기·대시·이동 스킬(어쌔신 Q 돌진·평타 전진, 거너 E 백스텝·우클릭 반동, 팔라딘 전진)·넉백이 모두 같은 스윕(`PlayerMovementSimulation`)이라 막힌다.
  스윕을 안 타는 것 = 순간이동(`TeleportAuthoritative` — 스폰·추락 복구·보스방 이동)과 23호 잡기/밀기(`PlayerRestrainedState` — 보스방 전용).
  Barrier 는 거너 평타·차지 레이저 차단 레이어에도 있어(할 일 32 에서 추가) 배리어 너머로는 거너 빔이 막힌다. 부활·리스폰 점유 검사(`TutorialDeathRules`·`PartyWipeRespawner`)도 Wall|Barrier 를 본다.
- 몬스터: NavMeshObstacle 카브(Box, `carveOnlyStationary` 끔, 위아래 +2m — `ZoneBridgeGate` 와 같은 설정). Barrier 는 NavMesh 베이크 수집(Default·Ground·Wall)에 안 들어가 다리 재베이크에 섞이지 않는다.
- 추락 복구: 서버 `ResolveReturnPoint` 가 기준점·Ring 후보마다 `ReturnPointFilter` 를 부른다 → `TutorialBarrierRules.IsPointReachable` —
  점을 품은 구간이 모두 '아직 못 들어가는 구간'(들어가는 배리어가 닫힘)이면 거부. 튜토리얼 구간 밖(보스방)은 허용. 스폰 폴백(존1)은 원래 거르지 않았다 —
  C2 부터는 그 사람의 뒤 배리어 너머면 대체 지점을 쓴다(아래 '뒤 배리어 예외').
- 타이머 만료·패드 이동은 `BossTeleportManager` 순간이동이라 배리어와 무관(§4 "만료 강제 이동은 G9·G10 과 무관").

**개인 뒤 배리어 (15 후속 C1, 2026-10-12)** — 배리어를 지나 앞 구간으로 넘어간 플레이어 **본인만** 뒤로 못 돌아간다. 뒤에 남은 동료의 전진은 막지 않는다.
> 코드: `TutorialBackBarrierRules`(순수 — 지남 판정·마스크 갱신·로컬 콜라이더 목록) · `TutorialDirector`(서버 판정·복제) · `TutorialBarriers`(각 피어 로컬 콜라이더).
> 테스트: `TutorialBackBarrierRulesTests`(튜토리얼 러너). 디버그: `Tools/Tutorial/Debug/뒤 배리어 마스크 로그 (클라별)`.
- **복제 필드**: `_players[i].BackBarrierMask`(int, `PlayerState`) — 지난 배리어, bit = `1 << (int)TutorialGate`(게이트 열림 마스크와 같은 배치). 한 번 켜지면 끄지 않는다.
  읽기 = `GetBackBarrierMask(clientId)`(모든 피어, 복제값) · `GetBackBarrierMaskServer(clientId)`(서버 원본 — 부활·추락 복구 예외(C2, 아래)가 쓴다) · `BackBarrierClearance`.
- **지남 판정(서버, `PollAreas` 주기)**: 게이트가 **열린** 배리어만. 배리어 박스의 진행 방향 쪽 면에서 `backBarrierClearance`(기본 0.75m — 플레이어 캡슐 반경 이상, 인스펙터) 이상 나아갔고
  너머 구간(입구 G1/G3/G6/G9 = 복도 n, 출구 G2/G4/G7/G10 = 다음 존·보스 진입) 사각형 안에 있을 때. 진행 방향은 `TutorialBarrierSpec.AlongX`·`Forward`.
  `Alive` 만 판정, 단계 Finished 뒤엔 판정 안 함. 새로 지나면 서버 로그 `[TutorialBarrier] 뒤 배리어 G* 닫힘 — client n`.
- **막기(각 피어, 로컬 플레이어 기준)**: `TutorialBarriers` 가 자기 `LocalClientId` 의 복제 마스크로 `지난 ∧ 열린 게이트`(닫힌 배리어는 일반 배리어가 막으니 중복 생성 안 함)를
  같은 자리의 `TutorialBarriers/BackBarrier_G*` 로 켠다 — Barrier 레이어 BoxCollider + 같은 머티리얼, **NavMeshObstacle 없음**(몬스터는 안 막음).
  이동이 오너 권한·오너 로컬 스윕이라 이 피어의 콜라이더는 이 피어의 로컬 플레이어만 막는다(남의 플레이어는 복제 위치라 무관). 늦은 클라는 스폰 직후 `StateChanged` 로 같은 결과.
  켤 때 로컬 플레이어 콜라이더 bounds 와 겹치면 켜지 않고 다음 프레임(`Update`)에 다시 시도(끼임 방지).
- 사망(Soul) 중에도 유지(Soul 도 같은 모터 스윕 — `PlayerMotor.ResolveObstacleMask` 가 Soul 에서도 Wall 을 남긴다). 튜토리얼 비활성(비튜토리얼 스테이지)이면 안 만들고, 단계 Finished 면 끈다.

**뒤 배리어 예외 — 스폰·부활·추락 복구·순간이동 (15 후속 C2, 2026-10-12)** — 서버가 그 플레이어를 자기 뒤 배리어 너머로 옮기지 않는다. 기준은 늘 **그 clientId 의 서버 마스크**(`GetBackBarrierMaskServer`)다(호스트 로컬 값 아님).
> 순수: `TutorialBackBarrierRules.FrontmostPassedArea`(지나온 최전방 구간 = 지난 배리어 중 가장 앞 것의 너머 구간, 없으면 존1) · `IsAreaBehind` ·
> `IsNearPassedFace`(지난 배리어 너머 구간이면서 앞면에서 `BackBarrierClearance` 미만 — 로컬 콜라이더와 겹칠 자리) · `IsReturnPointAllowed`(추락 복구 후보).
> 테스트: `TutorialBackBarrierRulesTests`·`TutorialDeathRulesTests`(튜토리얼 러너).
- **자동 부활**(§11): 지금 단계 구간이 그 플레이어의 최전방 구간보다 뒤면 최전방 구간으로 올리고, 찾는 순서도 최전방 구간에서 멈춘다(`TutorialRevivePointRules.AreaOrder(step, mask, passed)`).
  '가장 먼 후보' 폴백도 같은 순서 안에서만. 지난 배리어 앞면 여유 안 격자점은 버린다. G10 을 지난 사람만 보스 진입 존에서 부활(복도4 가 그 사람의 뒤 배리어 너머 — 갇힘 방지).
  후보가 없으면 그 자리(사망 지점은 이미 그 사람이 서 있던 곳이라 뒤 배리어 앞쪽).
- **추락 복구**: `PlayerSafePointTracker.ReturnPointFilter` 가 `Func<ulong, Vector3, bool>`(OwnerClientId, 후보)로 바뀌었다(null = 모두 허용, 기존과 같음).
  `TutorialBarriers.IsReturnPointAllowed(clientId, p)` → `TutorialBackBarrierRules.IsReturnPointAllowed` — 닫힌 배리어 너머 + 그 사람의 뒤 배리어 너머 + 지난 배리어 앞면 여유 안을 거부. 마스크 0 이면 기존 `IsPointReachable` 과 같다.
  스폰 폴백(존1)이 필터에 거부되면 `PlayerSafePointTracker.ReturnPointFallback`(서버, `TutorialDeathRules` 가 건다)이 부활과 같은 규칙의 안전 지점을 준다 — 못 찾으면 기존대로 스폰.
  추락 사망(Soul 로 옮기기)도 같은 `ResolveReturnPoint` 라 함께 적용된다.
- **순간이동 점검**: `TeleportAuthoritative` 경로 = 추락 복구(위 필터)·부활 재배치(`RelocateServer` — 위 부활 규칙)·스폰 포즈(`Player.PlaceOwnerAtSpawnClientRpc` — 최초 스폰 존1, 그때 마스크 0)·
  보스방 이동(`BossTeleportManager` — 목적지가 튜토리얼 구간 밖이라 지남 판정·뒤 배리어와 무관, Finished 에서 끈다). 뒤 배리어 너머로 옮기는 경로는 없다.
  옮긴 자리는 지난 배리어 앞면에서 여유(0.75m) 이상 + 부활 격자 inset 1m 라 로컬 뒤 배리어와 겹치지 않는다. 혹시 겹쳐도 C1 의 '켤 때 겹치면 다음 프레임' 재시도가 그대로 있다
  (이미 켜진 콜라이더는 옮긴 뒤에도 끄지 않는다 — 갇힐 자리로는 애초에 안 보낸다).
- 타이머 만료·패드 강제 이동·초기 스폰은 바뀐 것 없음(위 마스크 0·튜토리얼 밖 근거).
- 보스 진입 패드 제외(C3, 2026-10-12): 부활·추락 복구 지점은 패드 범위 + 1m 밖만 — G10 을 지난 사람이 보스 진입 존에서 부활·추락 복구해도 패드 위에 내리지 않아
  카운트다운이 시작되지 않는다. 상세 §11 '보스 진입 패드 제외'.

## 8. 존 장부 · 몬스터 휴면 (S4)
> 코드: `TutorialMonsterLedger`(순수 — 존 배정·클리어 규칙·필터) · `TutorialMonsterZones`(서버 — MonsterBase 참조·휴면 적용·사망 구독) · `TutorialDirector`(배선·API) ·
> 몬스터 훅 `MonsterBase.SetDormantServer` — 무엇을 왜 바꿨는지는 [handoff-tutorial.md](handoff-tutorial.md).
> 테스트: `TutorialMonsterLedgerTests`(튜토리얼 러너 — 장부 + 게이트 보류). 디버그 메뉴: `Tools/Tutorial/Debug/*`(Play 중 호스트).

**장부 만들기 — 활성화 순간(서버)**
- `MapGenerator.OnGenerated` 는 `MapContentSpawner.SpawnPlacements`(몬스터 `Spawn()` 동기 완료) **뒤**에 온다 → 디렉터 활성(그 이벤트 또는 그 뒤 자기 스폰)에서 몬스터가 이미 다 있다.
- 순서: 구간(§6) → 장부 → 모델. 스폰된 살아 있는 `MonsterBase` 전부를 **그 순간 위치(XZ)가 든 존 사각형**(§6 슬롯 사각형, 여유 없음)으로 배정. 어느 존에도 없으면 장부 밖(경고 로그).
  핵심 몬스터 = `Rank != Normal`(중간보스급 — 존3 SpinnerBot, 존4 GauntletBot).
- 서버 로그 1줄: `[Tutorial] 존 장부 — Zone1 0/0 · Zone2 9/9 휴면 9 · Zone3 1/1 (핵심 SpinnerBot…) · Zone4 31/31 (핵심 GauntletBot…) · 장부 밖 0 · 휴면 시작 41마리`.
- 2026-10-11 프리팹 집계(스폰 지점 × count): 존2 `ZoneS_typeA` **9**(Chomp 4·Humanoid 4·Mortar 1) · 존3 `ZoneM_typeC` **1**(SpinnerBot) ·
  존4 `ZoneL_typeB` **31**(PeekABot 4·Chomp 12·Humanoid 10·Mortar 4·GauntletBot 1). 모든 지점이 존 발자국 안(최대 |x|,|z| = 7.2 / 15 / 17.8m). 바닥을 못 찾은 지점은 스폰되지 않으니 실제 수는 로그로 본다.

**휴면**
- 활성 때 존2·존3·존4 **전부** 휴면(핵심 포함). 존1 은 0마리(있으면 깨워 둔 채로 두고 경고).
- 휴면 = FSM 정지(인지·추격·공격) + 무적(피해·넉백·경직·CC·피격 이펙트·피해 숫자 없음). 복제하지 않는다(클라 연출에 필요해지면 그때 NetworkVariable).
- 기상 API(서버): `SetZoneDormantServer(zone, dormant, filter)` · `WakeZoneServer(zone, filter)` · `SetMonsterDormantServer(monster, dormant)` · `GetZoneMonstersServer` ·
  `TryGetZoneMonsterCountServer` · `DescribeMonsterZonesServer`. filter = `All` / `NormalOnly`(존4 기상 — 중간보스 제외) / `KeyOnly`(다리 개통 — 중간보스만).
- 부를 곳: S8 존2(진입 안내 4초 뒤 `WakeZoneServer(Zone2)`) · S9 존3(스피너봇 — 스피너봇 레인과 정리, handoff §2) · S10 존4(G8 3초 뒤 `NormalOnly`, 다리 개통 때 `KeyOnly`).

**클리어 판정**
| 존 | 규칙 | 게이트 |
|---|---|---|
| 존2 | 전멸(`AllDead`) | G3 |
| 존3 | 핵심 처치(`KeyDead` — 스피너봇) | G6 |
| 존4 | 핵심 처치(`KeyDead` — 중간보스, 일반 몹 남아도 됨 D4) | G9 |
- 핵심이 없는 존은 전멸로 본다. 판정은 사망 때만 — 0마리 존은 클리어되지 않는다. 존마다 1번(`TutorialDirector.ZoneCleared` 정적 이벤트, 서버).
- 사망 = `MonsterDeathEvents.ServerMonsterDied`. 존2 사망마다 `Zone2Kills` +1. **처치 목표 = 장부의 존2 수**(0 이면 인스펙터 `zone2KillTarget`).
  목표는 `_counters` 뒤쪽 절반으로 복제한다(`GetCounterTarget` — 클라도 실제 수를 본다).

**게이트 보류** — `TutorialFlowModel.RequestGate` / `TutorialDirector.RequestGateServer`
- 존 클리어 = "조건은 이미 충족, 순서만 기다림". 차례(앞 게이트 열림)면 즉시 열고, 아니면 `RequestedGateMask` 에 남겨 **앞 게이트가 열리는 그 `Evaluate` 에서** 연다(조건 재확인 없음 — 처치는 되돌릴 수 없다).
- 예: 디버그로 존2 를 G2 전에 정리 → G3 보류 → 복도1 특성 전원 선택으로 G2 가 열리는 순간 G3 도 열림. 중간보스를 G8(타이머 시작) 전에 잡음 → G8 열릴 때 G9.
- `TryOpenGateServer` 는 그대로(순서가 안 맞으면 거부) — 보류가 필요한 쪽만 `RequestGateServer` 를 쓴다.

**디버그(Play 중 호스트, `Tools/Tutorial/Debug/`)**: 장부 로그 · 존2 기상 · 존3 기상 · 존4 기상(중간보스 제외) · 존4 중간보스 기상 · 존2~4 전부 휴면 ·
존2 전멸(깨워서 처치) · 존3 핵심 처치 · 존4 핵심 처치. F9(DevMonsterKill)는 휴면 몬스터를 못 죽인다(무적) — 먼저 깨울 것.
처치·강제 개방 코드는 `TutorialDebugActions`(런타임 — 구간 스킵 Alt+1~0 과 공용, §10.5).

## 9. 보스 타이머 · 보스 길 (S5)
> 코드: `TutorialBossPathRules`(순수) · `TutorialBossPath`(판별 한 곳 — 경석 코드가 묻는 곳) · `TutorialElevatorDoor`(모든 피어) · `TutorialDirector`(G8·HUD·도착) ·
> 경석 코드 훅(`BossTimerManager`·`BossTimerHUD`·`BossEnterTrigger`·`BossEnterZoneVisual`) — 무엇을 왜는 [handoff-tutorial.md](handoff-tutorial.md) §3.
> 테스트: `TutorialBossPathRulesTests`(튜토리얼 러너). 회귀: `BossTimerClockTests`(슬로우 모션 러너). 디버그: `Tools/Tutorial/Debug/` 보스 길 6개(G11 열기 = 목숨 차감 기준만 켬, 이동·Finished 없음).

**판별**: `TutorialBossPath.IsTutorialStage` = `TutorialStageMarker.IsActive` && 씬에 `TutorialDirector`. 둘 다 씬 로드(OnEnable·Awake) 때 정해져
`MapGenerator.OnGenerated`·NetworkObject 스폰 순서와 무관하다 → 타이머는 디렉터 활성 전에도 **한 프레임도 자동 시작하지 않는다**. 디렉터가 씬에 없으면 잠금도 없다(교착 방지).

| 항목 | 튜토리얼 | 비튜토리얼(절차 맵) |
|---|---|---|
| 타이머 시작 | 맵 생성 자동 시작 없음(Idle) → **G8 열림 = `BossTimerManager.StartTimerServer(420)`** | 맵 생성 직후 자동(씬 600 · 코드 300) — 그대로 |
| 제한시간 | 디렉터 인스펙터 `bossTimerLimitSeconds` 420(D6). `_limitOverride` 로 복제 → 클라 게이지 같은 길이 | 인스펙터 값 |
| HUD | Idle + `IsTimerHudShown` 꺼짐 = 숨김(알파 0). `ShowTimerHudServer()`(Z4-0) = 빈 게이지로 등장. G8 이면 자동 표시 | Idle 에도 빈 틀(기존) |
| 패드 | G10 전 잠김 — 서 있어도 점유 false(카운트다운·색 전환 없음), 테두리 숨김. G10 순간 이미 서 있던 사람으로 카운트다운 | 처음부터 활성 |
| 엘리베이터 문 | G10 과 함께 `Animator.Play("Open")`(늦은 클라 = 끝 프레임) | 손대지 않음(기본 Closed) |
| 만료 | 3초 경고 → Soul 강제 부활 → 전원 이동 — **게이트·배리어·패드 무관**(잠긴 패드는 '진행 중'을 만들 수 없다) | 같음 |
| 도착 | 타이머 Stopped·게이지 0(기존) + 디렉터 G11(G10 뒤일 때만) → 단계 `Finished` + `BossRoomArrived` | 기존 |

- **G8 과 타이머 시작은 하나다**: 타이머를 직접 켜는 API 는 디렉터에 없다 — `StartBossTimerServer()` = `TryOpenGateServer(G8)`, 실제 시작은 G8 열림 처리 한 곳.
  '타이머 전/후'(S10 존4 기상)는 **`IsBossTimerStarted`(= G8 열림, 모든 피어)** 하나로 본다. S11 목숨 차감은 G8 이 아니라 **G11(보스방 도착)** 기준(10-12 변경, §11). `BossTimerManager.State` 는 만료·도착(Stopped)으로 바뀌어 기준이 못 된다.
- 엘리베이터 문 컨트롤러(`4.Animations/.../MV_BossroomEnter_Doors.controller`)에는 파라미터·전이가 없다(Closed 기본, Open·Close 고립 상태). 애셋을 고치지 않고 상태를 직접 재생한다.
  문을 다시 닫는 연출(보스방 이동 때 Close 등)은 없다 — 필요하면 같은 방식으로 `Close`.
- 도착 = `BossTeleportManager.AlivePlayersArrived`(서버, 패드·강제 공통). 판 전체 1번. ACK 실패(`ArrivalAborted`)는 도착이 아니다.

## 10. 인계 (S0~S5 레인 마무리 — S6~S12 가 읽을 것)

### 10.1 단계별 문서 · handoff · 공개 API 전체

**단계별 문서** (원본 시나리오 [../design/tutorial-scenario.md](../design/tutorial-scenario.md) — 이 문서는 S0~S5 코어 + S11)
| 단계 | 문서 | 담는 것 |
|---|---|---|
| S6 안내 UI | [tutorial-guide-ui.md](tutorial-guide-ui.md) | 목표 패널·자막·강조 API, 문구 변형·치환 규칙, 화면 배치 |
| 훅 | [tutorial-player-hooks.md](tutorial-player-hooks.md) | 대시 승인·툴팁 표시/숨김·간파 판정·스킬 사용·과열·조준 시작 이벤트 · v8 기본 공격 시작·거너 Q 발사 |
| S0·S7 존1(v8) | [tutorial-zone1.md](tutorial-zone1.md) | 착지 환영·이동 5m·대시·**기본 공격·Q·E·R·우클릭 각 1회**·패시브 1초·G1(교착 방지 없음)·캐릭터별 힌트·허수아비 디스폰 |
| S8 복도1~4·존2(v8) | [tutorial-corridors-zone2.md](tutorial-corridors-zone2.md) | 개인 도착 특성 라운드·출구 게이트·자동 획득·G10 패드 경고 · **복도1 첫 진입 쿨 초기화** · 존2 기상 4초·**전멸만** |
| S9 존3(v8) | [tutorial-zone3.md](tutorial-zone3.md) | 스피너봇 Variant 스폰 치환·유효 벽·연습 위치·**간파 → 취약 밀치기 → 벽 충돌 → 복귀 연계 3회** → G5 → 본 모드 → G6 |
| S10 존4·보스 진입 | [tutorial-zone4.md](tutorial-zone4.md) | 설정 자막 10초 → G8 → 패널·다리·중간보스 → G9 · 만료·패드·도착 정리 · 재베이크/고정 조각 조사 |
| 특성 | [tutorial-traits.md](tutorial-traits.md) | 특성 디렉터 API·라운드·무적 토큰·placeholder 6종·xlsx 이관 메모 |
| S11 사망 | 이 문서 §11 | 자동 부활·목숨 차감(G11 보스방 도착)·부활 지점(단계 구간) |
| QA | [tutorial-qa.md](tutorial-qa.md) | 사람이 위에서부터 따라 하는 Play/MPPM 체크리스트 · 알려진 한계 · 병합 전 확인 |

**handoff (다른 담당 코드에 넣은 훅 — 경석 공유)**
- [handoff-tutorial.md](handoff-tutorial.md) — `MonsterBase` 휴면(`SetDormantServer`), 스피너봇 휴면 통합(I0), 보스 타이머 지연 시작·HUD 숨김·패드 잠금(`TutorialBossPath` 한 곳 판별).
- [handoff-tutorial-spinner.md](handoff-tutorial-spinner.md) — `SpinnerBot` 주입 인터페이스(`ISpinnerBotOverride`)·연습/전환 대기/본 모드, `MapContentSpawner.ServerMonsterPrefabOverride` 스폰 치환 훅.
- 존3/4 레인 별도 handoff 는 없다 — 존4 의 `ZoneBridgeGateManager` 정적 이벤트 2개(`ServerPanelActivated`·`ServerBridgeOpened`)와 재베이크·고정 조각 조사는 [tutorial-zone4.md](tutorial-zone4.md) §4·§6 에 있다.

**`TutorialDirector`** (씬 상주 NetworkObject, `Instance`)

| 쪽 | 시그니처 |
|---|---|
| 읽기(모든 피어) | `bool IsTutorialActive` · `TutorialStep Step` · `bool IsGateOpen(TutorialGate)` · `bool WasGateAutoOpened(TutorialGate)` · `int GetCounter(TutorialCounter)` · `int GetCounterTarget(TutorialCounter)` · `bool TryGetPlayerProgress(ulong, out TutorialPlayerProgress)` · `bool TryGetWait(out TutorialGate, out int done, out int total, out float autoAdvanceRemaining)` · `bool IsTimerHudShown` · `bool IsBossTimerStarted`(= G8) · `bool IsFinished`(단계 Finished) · `float BossTimerLimitSeconds` · `float AreaEnterMargin/AreaExitMargin` |
| 서버 쓰기 | `bool ReportCheckServer(ulong, TutorialCheck)` · `bool AddCounterServer(TutorialCounter, int = 1)` · `bool TryOpenGateServer(TutorialGate, TutorialGateOpenReason = Manual)`(순서 어기면 거부) · `bool RequestGateServer(TutorialGate)`(차례 전이면 보류) · `bool SetStepServer(TutorialStep)`(앞으로만) · `void ShowTimerHudServer()` · `bool StartBossTimerServer()`(= G8) · `void ShowSubtitleServer(string id, string[] args = null, ulong[] targets = null)`(S9 — 자막 RPC 하나, targets null = 전원) · `ulong[] GetClientsOfCharacterServer(TutorialCharacter)` |
| 서버 몬스터 | `int SetZoneDormantServer(TutorialArea, bool, TutorialMonsterFilter = All)` · `int WakeZoneServer(TutorialArea, filter = All)` · `bool SetMonsterDormantServer(MonsterBase, bool)` · `void GetZoneMonstersServer(TutorialArea, filter, List<MonsterBase>)` · `bool TryGetZoneMonsterCountServer(zone, filter, out total, out alive)` · `bool IsZoneClearedServer(TutorialArea)` · `string DescribeMonsterZonesServer()` |
| 서버 구간 | `TutorialArea GetPlayerAreaServer(ulong)` · `bool HasPartyEnteredServer(TutorialArea)` · `bool HasReachedCorridorMidServer(ulong, int)` |
| 오너 클라 | `void ReportLocalCheck(TutorialCheck)`(PassiveTooltip — Move 는 거리 없이 보내면 서버 거부) · `void ReportLocalMoveDistance(float meters)`(S7, 서버 상한 검증) · `static bool IsOwnerReportable(TutorialCheck)` |
| 정적 이벤트 — 양쪽 | `StepChanged(TutorialStep from, to)` · `GateOpened(TutorialGate, TutorialGateOpenReason)`(클라 이유 = Timeout/Manual 만) · `StateChanged()`(아무 복제 변화 + 스폰 직후 1회) |
| 정적 이벤트 — 서버만 | `AreaFirstEntered(TutorialArea, ulong)` · `CorridorMidReached(ulong, int corridor)` · `ZoneCleared(TutorialArea)` · `BossRoomArrived(IReadOnlyList<ulong>)` |

**그 밖의 컴포넌트**
- `TutorialStageMarker` — `static bool IsActive` · `static TutorialStageMarker Active` · `static bool OwnsGeneratedMap(MapGenerator)` · `Material BarrierMaterial`. Play 중 `Start` 에서 `TutorialBarriers`·`TutorialElevatorDoor` 를 붙인다.
- `TutorialBossPath`(모든 피어) — `IsTutorialStage` · `ShouldAutoStartTimer` · `ShouldHideTimerHud(TimerState)` · `IsBossPadEnabled`.
- `TutorialBarriers`(모든 피어) — `static Active` · `bool IsBuilt` · `bool IsBarrierClosed(TutorialGate)` · `bool IsReturnPointAllowed(Vector3)`(서버 추락 복구 필터).
- `TutorialElevatorDoor`(모든 피어) — `bool IsOpened`.
- `BossTimerManager`(경석) — `bool StartTimerServer(float limitSecondsOverride = 0f)` · `TimerState State` · `float LimitSeconds` · `float RemainingSeconds` · `float Fill01`.
- 순수: `TutorialFlowModel` · `TutorialAreaTracker`/`TutorialAreaLayout` · `TutorialBarrierRules` · `TutorialMonsterLedger` · `TutorialBossPathRules`.
- 열거형: `TutorialStep`(None·Zone1·Corridor1·Zone2·Corridor2·Zone3Practice·Zone3Fight·Corridor3·Zone4Lore·Zone4Combat·Corridor4·BossEnter·Finished) ·
  `TutorialGate`(G1~G11) · `TutorialCheck`(Move·Dash·BasicAttack·PassiveTooltip·SkillQ·SkillE·TraitCorridor1~4·SkillR·SkillInterrupt·Corridor1CooldownReset — v8) · `TutorialCounter`(Zone2Kills·Zone3Interrupts(= v8 연계)·Zone4Panels) ·
  `TutorialArea`(None·Zone1~4·Corridor1~4·BossEnter) · `TutorialMonsterFilter`(All·NormalOnly·KeyOnly) · `TutorialGateOpenReason`(Manual·AllDone·Timeout·CounterReached).

### 10.2 S6~S12 가 붙일 자리
> ✅ **모두 붙었다(S12 기준).** 실제 구현은 디렉터 본문이 아니라 디렉터 GameObject 에 런타임 `AddComponent` 되는 흐름 컴포넌트다 —
> `TutorialZone1Flow`(S7) · `TutorialCorridorFlow`·`TutorialZone2Flow`(S8 — G10 `TUT_B_PAD_WARN` 포함) · `TutorialZone3Flow`/`Guide`(S9) · `TutorialZone4Flow`/`Guide`(S10 — 보스방 도착 정리·23호 팁 포함) · `TutorialDeathRules`(S11).
> 단계 갱신(`SetStepServer`)도 각 흐름이 부른다. 디렉터 코드에 남은 `TODO(S6)`·`TODO(S8/S12)`·`TODO(S12)` 주석 4곳은 **낡은 표시**다(동작은 이벤트 구독 쪽에 있음 — 코드 정리 때 지울 것).
> 아래 표는 처음 계획(이력)이다.

| 단계 | 자리 | 할 일 |
|---|---|---|
| S6 안내 UI | `TutorialDirector.HandleAreaFirstEntered` `TODO(S6)` · `HandleModelGateOpened` `TODO(S6)`(G10 → `TUT_B_PAD_WARN` 전원) | 단계 갱신 규칙(`SetStepServer` — 아직 아무도 안 부른다: 활성 때 Zone1, 도착 때 Finished 뿐) · 목표 패널·자막(늦은 클라 = `StateChanged` 로 재구성) |
| S7 존1 | `ReportOwnerCheckServerRpc` `TODO(S7)`(이동 거리 검증) | Dash·AttackHit = `ReportCheckServer` · Move·PassiveTooltip = `ReportLocalCheck` |
| S8 존2·복도 | `HandleCorridorMidReached` `TODO(S8/S12)` · `AreaFirstEntered(Zone2)` | 존2 안내 4초 뒤 `WakeZoneServer(Zone2)` · 특성 창 → `ReportCheckServer(TraitCorridorN)` |
| S9 존3 | `AreaFirstEntered(Zone3)` · handoff-tutorial.md §2 | 스피너봇 휴면 해제 → 연습 모드 → `AddCounterServer(Zone3Interrupts)` → G5 자동 → 본 모드 |
| S10 존4 | `AreaFirstEntered(Zone4)` | Z4-0 `ShowTimerHudServer()` + 자막 → 끝나면 `StartBossTimerServer()`(G8) → 3초 뒤 `WakeZoneServer(Zone4, NormalOnly)` · 다리 개통 `KeyOnly` · 패널 `AddCounterServer(Zone4Panels)` · 만료 문구 = `BossTimerManager.State == Expired` |
| S11 사망 | — | 목숨 차감 기준 `IsBossTimerStarted`(→ 10-12 G11 로 변경) · 패드/강제 이동 시작(`BossTeleportManager.IsCountdownActive`) 때 대기 부활 취소·즉시 부활 |
| S12 통합 | `HandleBossRoomArrived` `TODO(S12)` | 안내 종료(`BossRoomArrived`/`IsFinished`) · 개인 게이트 `Timeout` 열림 → 미선택자 자동 획득 · 만료 시 열린 특성 창 닫기 |

### 10.3 함정
- **스폰 순서**: `MapGenerator.OnGenerated` 가 디렉터·타이머 스폰보다 먼저 올 수 있다 — 서버는 보류 후 `OnNetworkSpawn` 에서 적용. 스폰 전 NetworkVariable/NetworkList 쓰기는 버려진다.
  스폰 전에는 `NetworkManager`(NetworkBehaviour 프로퍼티)가 null → `NetworkManager.Singleton` 으로 판정. 클라의 존 스폰(맵 생성)과 디렉터 스폰도 순서가 없다 — 존 오브젝트를 찾는 클라 코드는 재시도(엘리베이터 문처럼).
- **튜토리얼 판별은 `TutorialStageMarker` 하나**(경석 코드는 `TutorialBossPath` 경유). 디렉터 `_active` 복제를 기다리는 판정은 스폰 전에 false 라 늦다.
- **게이트 순서·보류**: G1→G11 순서만. `TryOpenGateServer` 는 차례가 아니면 거부, 존 클리어처럼 조건이 이미 충족된 것은 `RequestGateServer`(보류 → 앞 게이트 열리는 `Evaluate` 에서 연다).
  G8 이 열리면 보류된 G9 가 같이 열린다(중간보스를 타이머 전에 잡은 경우). 게이트 이벤트로 일을 붙일 때 **어느 경로로 열려도** 오는 `HandleModelGateOpened`(서버)·`GateOpened` 에 붙일 것.
- **휴면은 `MonsterBase.SetDormantServer` 하나**(I0 에서 통합 완료) — 스피너봇 연습 컴포넌트에는 휴면이 없고 연습/전환 대기/본 모드만 있다(handoff-tutorial.md §2).
- **씬 hash**: `4.MapScene` 의 `TutorialDirector` NetworkObject `GlobalObjectIdHash 114710176`. development 재반영·씬 머지 때 이 값이 0/중복이 되면 스폰이 깨진다 — 머지 뒤 확인.
  씬 상주 NetworkBehaviour 에 NetworkVariable 을 더하는 것(이번 `_timerHudShown`·`_limitOverride`)은 hash 와 무관하지만 모든 피어가 같은 빌드여야 한다.
- **Stage1 = 모든 판**(Start → 항상 튜토리얼). 튜토리얼 규칙(타이머 지연·패드 잠금·휴면)이 사실상 본게임 규칙이 된다 → D1 팀장 합의 전 development 병합 금지.
  절차 맵(`Stage1_Procedural`)에서 기존 동작이 그대로인지는 마커 유무 하나로 갈린다.
- 서버 전용 이벤트(`AreaFirstEntered`·`CorridorMidReached`·`ZoneCleared`·`BossRoomArrived`)는 클라에서 안 온다 — 클라 표시는 복제 값(`Step`·`IsGateOpen`·카운터·`StateChanged`)으로.
- 네트워크 없이 단독 실행하면 디렉터·타이머가 스폰되지 않아 아무것도 돌지 않는다 — 확인은 호스트(MPPM).

### 10.4 Play 확인 대기 (S0~S5 누적 — 사람이 MPPM 에서)
> 전 단계 통합 순서표는 [tutorial-qa.md](tutorial-qa.md) — 아래 항목도 거기에 흡수했다.
- **S0**: 존3 `ZoneM_typeC` 배치·벽 투명화 페이드가 StageTutorial 에서 보인다.
- **S1**: 호스트 로그 `[Tutorial] 튜토리얼 시작 — 참가자 n명` · 클라 로그 `디렉터 스폰 (서버:False) — 활성:True` · 늦게 붙은 클라도 같은 상태.
- **S2**: 존 첫 진입·복도 중간 지점 로그(존1 은 착지 직후) · 영혼·사망 중에는 판정 없음.
- **S3**: 배리어 8개가 모든 피어에서 걷기·대시·이동 스킬·넉백을 막는다 · 몬스터가 못 지나간다 · 열릴 때 페이드 · 추락 복구가 배리어 너머로 안 간다 · 거너 빔이 배리어에 막힌다.
- **S4**: 존 장부 로그 1줄(존2 9 · 존3 1 · 존4 31) · 휴면 몬스터가 안 움직이고 피해·넉백·피해 숫자가 없다 · 깨우면 정상 · 존 클리어 → G3/G6/G9(보류 포함).
- **S5**: 존1~4 동안 타이머 HUD 가 안 보인다(빈 틀도 없음) · `[BossTimer] 튜토리얼 스테이지 — 맵 생성 자동 시작을 건너뛴다` 로그 ·
  디버그 `타이머 HUD 표시` → 빈 게이지 등장(클라 포함) · `타이머 시작 (G8)` → 420초로 차오름(클라 게이지 같은 속도) ·
  G10 전 패드 위에 서도 카운트다운·테두리 없음 → `G10 열기` 순간 테두리 표시·엘리베이터 문 열림(클라 포함, 늦은 클라 = 열린 채) · 패드 3초 → 도착 → `[Tutorial] 보스방 도착` + 타이머 정지·게이지 0 ·
  G10 전 타이머 만료(인스펙터 `bossTimerLimitSeconds` 를 짧게) → 3초 경고 → 전원 강제 이동(배리어·패드 잠금 무관) → 도착 로그 ·
  🔴 절차 맵(`Stage1_Procedural`)에서 타이머가 맵 생성 직후 자동 시작·HUD 빈 틀·패드 즉시 활성·문 Closed 로 **기존과 같다**.

### 10.5 구간 스킵 Alt+1~0 (할 일 34 — QA·테스트용)
> 코드: `TutorialSkipPlan`(순수 계획) · `TutorialSkipController`(런타임 — 입력·패널·서버 실행) · `TutorialDirector.Skip.cs`(호스트 전용 ServerRpc) ·
> `TutorialDebugActions`(강제 개방·존 처치 — `Tools/Tutorial/Debug/*` 메뉴와 공용) · `TutorialCorridorFlow.SkipRoundServer` · `TutorialDeathRules.TryFindAreaSpreadPoints`.
> 테스트: `TutorialSkipPlanTests`(튜토리얼 러너). 단축키 표·QA 메모: [tutorial-qa.md 0.4](tutorial-qa.md#04-디버그-메뉴-play-중-호스트-toolstutorialdebug).
- **빌드**: 모두 `#if UNITY_EDITOR || DEVELOPMENT_BUILD`(계획 모델만 예외 — 순수 코드). 릴리스 빌드엔 컨트롤러·RPC 가 없어 입력이 무시된다. 로그는 `Debug.Log`(`Edit.*` 는 빌드에서 사라짐).
- **권한**: 각 피어의 컨트롤러가 Alt+숫자를 읽는다. 호스트가 아니면 "호스트만" 토스트로 끝. 호스트는 `RequestSkip` → `RequestSkipServerRpc` — 서버가 보낸 사람 = 서버 자신(호스트)인지 다시 검증.
- **판정**(`TutorialSkipPlan.Check(step, target, restartInProgress)`): 판 재시작 진행 중(`NetworkLoadingFlowController.IsFlowInProgress`)이면 무엇이든 거부(`RestartInProgress`).
  Alt+0(`Restart`) = 뒤로 가기 예외 — 어느 구간·Finished 뒤에도 허용. 그 밖엔 지금 구간 = 디렉터 `Step` 의 구간(`CurrentOf`), 목적 < 지금 = 뒤로 → 거부(토스트·로그), Finished = 거부,
  같은 구간 = 게이트 없이 다시 옮기기만. 이전 스킵이 진행 중이면 거부(Alt+0 은 진행 중 스킵을 끊고 실행). 존1 은 단축키가 없다(지금 구간 기준값으로만 남음).
- **처음부터(Alt+0 — 판 재시작, 할 일 34 후속 3)**: 확인 없이 `NetworkLoadingFlowController.TryRestartTargetScene` — 정식 진입과 같은 로딩 흐름을 탄다:
  로딩 씬 Additive → (로비 언로드 대신) **동적 스폰 NetworkObject 전부 디스폰**(플레이어 포함 — `Spawn()` 기본 `DestroyWithScene=false` 라 안 지우면 NGO 가 활성 씬으로 옮겨 살린다) →
  `4.MapScene` 네트워크 언로드 → 다시 Additive 로드 → `SpawnAllPlayers`(10m 낙하) → MainGameReady → 로딩 씬 언로드. 씬 배치 객체(디렉터·게이트·특성·몬스터·공용 목숨·보스 타이머·`SessionStatsTracker`)는
  씬과 함께 새로 생긴다. 유지: `ServerCharacterSelectionStore`(세션 정적) · NetworkManager·GameManager 등 DDOL 매니저(`GameState` 가 Loading → MainGame 으로 돌아 MainGameReady 가 다시 발행되고, `NetworkClock.MarkMainGameStart` 재스탬프).
  Dev_Boot(Single 로드) 경로에서도 같은 흐름. 이 컴포넌트는 맵과 함께 사라지므로 재시작은 NetworkManager 쪽이 끝까지 진행한다. 거부 = 로딩 흐름 진행 중·타깃 씬 미로드·로딩 씬 로드 실패(판 그대로).
  - **수정(후속 4)**: Dev_Boot 재시작에서 로딩 씬이 안 내려가고 몬스터가 로딩 씬에 남던 버그 — 표시 시간 0(`SetEditorDefaults(...,0,0)`)이라 완료 루틴이 진행률 0.9(활성화 전)에서 바로 로딩 씬 언로드를 요청 → NGO `SceneEventInProgress` 거절(재시도 없음) → 로딩 씬이 활성 씬으로 남고 그 안에 Instantiate 된 몬스터도 남음. 완료 루틴이 타깃 `LoadEventCompleted` 를 먼저 기다린다([scene-flow-audit.md §8](scene-flow-audit.md)).
- **계획**(`TutorialSkipPlan.Build`): 목적 구간 입구 게이트(복도 n = G1/G3/G6/G9, 다음 존 = G2/G4/G7, 보스 진입·보스방 = G10)까지 **닫힌 게이트만** G1 부터 순서대로, 게이트마다 정식 경로 하나:
  G1 = 접속자 전원 `ReportCheckServer`(G1Mask 전 항목) · G2/G4/G7/G10 = `TutorialCorridorFlow.SkipRoundServer`(**제시 없이** 라운드를 열고(`StartRoundWithoutOfferServer`) 아직 참가하지 않은 접속자마다 `GrantParticipantServer` = 바로 무작위 1장 획득 → `ServerTraitChosen`(Auto) → 특성 체크 → 전원 완료.
  제시 행·플레이어별 제시 번호(OfferSeq)가 생기지 않아 **클라 특성 창이 열리지 않는다**. 이미 중간 지점에 도착해 창을 보던 사람만 `AutoChooseAllPendingServer` 로 창이 닫힌다) ·
  G3/G6/G9 = 그 존 살아 있는 몬스터 전부 깨워 처치(`TutorialDebugActions.KillZoneMonsters` — 장부 클리어 → `RequestGateServer`, 0마리면 직접 요청) ·
  G5 = 연계 카운터를 목표까지 + `RequestGateServer(G5)`(존3 흐름이 '연습 없이' 본 모드로) · G8 = `StartBossTimerServer`(존4 흐름이 '설정 자막 없이' 전투 시작 + 420초 타이머).
  존3 규칙 파일은 고치지 않는다 — 디렉터·흐름의 기존 공개 API 만 부른다.
- **실행**(서버 코루틴): 단계마다 게이트가 열리기를 최대 5초(실제 시간) 기다린다. 안 열리면 경고 후 `TutorialDebugActions.OpenGatesThrough`(디버그 메뉴 강제 개방과 같은 코드). 그래도 안 열리면 중단·이동 없음.
  끝나면 `SetStepServer(StepOf(목적))`(앞으로만 — 존은 첫 진입 단계) → 접속자마다(완전 사망 제외) `ResetBackBarrierMaskServer(id, 목적 구간)`(전멸 리스폰과 같은 예외 — 최전방 = 목적 구간) →
  `TryFindAreaSpreadPoints`(전멸 리스폰과 같은 규칙: 격자·바닥·점유·지난 배리어 앞면 여유·패드 + 1m 밖·2m 간격 — 기준점 = 그 구간 입구 배리어, 존1 = 가운데) → `PlayerFallRecovery.RelocateServer`(= `TeleportAuthoritative`).
  존이면 구간 감지가 새 위치로 `AreaFirstEntered` 를 내서 그 존 흐름(존2 4초 기상 · 존3 연습 · 존4 설정 자막)이 평소대로 시작한다. 복도면 그 복도 특성은 도착 뒤 중간 지점에서 평소대로.
- **보스방(Alt+9)**: 보스 진입과 같은 계획·이동 뒤 `BossTeleportManager.ForceStartEncounter(3)` — 패드 이동 경로 그대로(카운트다운 → 산개 → 도착 ACK → `AlivePlayersArrived` → G11·Finished).
- **화면**: Alt 를 **누르고 있는 동안만** 호스트 화면 오른쪽 위에 버튼 10개(지금 구간 ▶ · 뒤 구간·진행 중 비활성 · 마지막 'Alt+0 처음부터' 는 재시작 중이 아니면 늘 활성). 토글이 아닌 이유 — 단축키가 이미 Alt 조합이라 쓰려는 순간에만 보이고, 켜 둔 채 잊어 캡처·녹화에 남지 않는다.
- **로그**: `[TutorialSkip] 시작 → X (단계 …) — 할 일 G1(…) → …` · `[TutorialSkip] → 구간 X (열린 게이트 G1·G2… · 특성 자동 획득 복도 1·2 · 이동 n명 · 단계 …)`.
- **알려진 한계**: 건너뛴 복도1 첫 진입 쿨 초기화(체크리스트 아님)는 없다. 존4 패널·다리는 건너뛴다(카운터 0/4 그대로 — 중간보스는 다리 없이 처치).
  건너뛴 복도의 획득 토스트는 그 플레이어가 전에 제시받은 적이 있을 때만 뜬다(창 모델 규칙 — 제시 번호 0 이면 토스트 없음). 획득 아이콘은 항상 갱신된다.
  이동 지점은 몬스터 거리를 보지 않는다(전멸 리스폰과 같음 — 목적 존 몬스터는 아직 휴면).
  처음부터: DDOL 풀(`EffectManager`)에 떠 있던 이펙트는 정리하지 않는다(풀 수명대로 사라짐). 타깃 씬 언로드가 실패하면 같은 씬을 겹쳐 올리지 않고 남은 판에 플레이어만 다시 스폰한다(에러 로그).

## 11. 사망 규칙 (S11)
> 코드: `TutorialDeathRulesModel`·`TutorialRevivePointRules`(순수) · `TutorialDeathRules`(서버, 디렉터에 런타임 부착). 테스트: `TutorialDeathRulesTests`(튜토리얼 러너).
> 훅(기본값 = 기존 동작): `PlayerLifeCycleController.ServerDeathKeepsSoul` · `PlayerReviveController.TryCompleteReviveOnServer(bool consumeLife)` · `PlayerFallRecovery.RelocateServer`.
> **목숨 = 파티 공용 풀 5개**(할 일 30, 2026-10-13 — 아래 '목숨'은 전부 공용 풀) · 전멸 → 복도4 리스폰 · 특성 재선택: [party-lives-respawn.md](party-lives-respawn.md).

- 부착: 서버에서 `StateChanged` 때 `IsTutorialActive` 면 디렉터 GameObject 에 `AddComponent`(씬·프리팹 저장 없음). 비튜토리얼은 안 붙는다.
- 사망(Alive → DeadPresentation) → 3초 뒤 자동 부활 + 2초 무적(`InvulnerabilityCause.Revive`). 차감 = **사망 시점** `IsGateOpen(G11)`(보스방 도착 — `TutorialDeathRulesModel.IsLifeChargedAt(mask)`, 단계 기준, 위치 무관).
  G11 전(존1~복도4·패드 대기·타이머 만료 강제 이동 중 — **G8 타이머 시작 뒤 포함**)은 목숨 수와 무관하게 Soul(훅), 부활 시 차감 없음.
  G11 뒤는 기존 규칙(공용 목숨 0 → PermanentDead → 전멸이면 PartyWipeWatcher — 공용 0 의 갇힌 Soul 도 탈락으로 센다, party-lives-respawn.md §2). 10-12 이전 기준은 G8 이었다 — G8 은 타이머 시작·존4 기상 용도로만 남는다.
  G11 은 도착과 같은 순간 열리고 사망 규칙도 도착·Finished 로 끝나므로, 실제로 튜토리얼 규칙 아래 차감이 일어나는 경로는 없다(G11 뒤 사망 = 일반 규칙).
- 부활 지점 = **지금 단계 구간**(디렉터 `Step` 의 구간 — 열린 배리어로 들어갈 수 있는 가장 앞 구간을 넘지 않음, 아래 S12 항목)(존1~복도4, **보스 진입 제외 — 패드**) → 앞 구간 순. 구간 안쪽 1m·1.5m 격자 중
  1층 바닥(스테이지 높이 ±1m)·점유 없음(Wall 포함)·살아 있는 `MonsterBase` 에서 10m(고정 — 몬스터 인지 반경 공개 API 없음) 밖, 사망 지점에 가장 가까운 점.
  안전한 곳이 없으면 몬스터에서 가장 먼 후보, 후보가 없으면 그 자리.
- 보스방 이동: `BossTeleportManager.IsCountdownActive`(패드·만료 강제 공통)가 켜지면 대기 부활 전부 '즉시·그 자리'(Soul 이 되는 대로). 이동 중 사망도 같다.
  튜토리얼 구간 밖(보스방)에 있는 Soul 은 늘 그 자리. 도착(`AlivePlayersArrived`/`ArrivalAborted`) 또는 단계 Finished 뒤 사망은 일반 규칙.
- 연출 잠금(`PlayerEncounterLock`) 중이면 보류 + 경고 1회, 풀리면 다음 프레임 부활(사망 연출 해소도 잠금 해제 뒤 진행).
- 추락 생존 복구는 그대로. 추락 사망은 기존대로 안전지점에 Soul 로 옮겨진 뒤 같은 규칙으로 부활.
- '지금 단계 구간'(부활 지점의 기준 구간) = **디렉터 `Step` 의 구간**(S12 — `TutorialRevivePointRules.StepArea`·`ResolveStageArea(step, mask)`).
  배리어 기준(열린 배리어로 들어갈 수 있는 가장 앞 구간)은 **상한**으로만 쓴다(둘 중 앞쪽). 두 기준이 어긋나던 구간 = 복도 출구(G2·G4·G7)가 열렸지만
  아직 아무도 다음 존에 안 들어간 동안(단계는 복도) — 배리어 기준이면 다음 존에서 되살아나 그 존 첫 진입·기상을 앞당겼다. 보스 진입·종료 단계는 복도4.
  테스트: `TutorialDeathRulesTests` 의 단계 기준 2건.
- **개인 뒤 배리어(C2, §7)**: 부활 지점은 그 플레이어(clientId)가 지나온 최전방 구간 이상 — 단계 구간이 그보다 뒤면 올린다(`ResolveStageArea(step, mask, passed)`·`AreaOrder(step, mask, passed)`).
  지난 배리어 앞면 여유(`BackBarrierClearance`) 안 격자점은 버린다. G10 을 지난 사람은 예외로 보스 진입 존. 마스크 0(뒤에 남은 동료)은 위 규칙 그대로.
  같은 탐색이 추락 복구 스폰 폴백의 대체 지점(`PlayerSafePointTracker.ReturnPointFallback`)도 준다. 테스트: `TutorialDeathRulesTests` 의 뒤 배리어 4건.
- **보스 진입 패드 제외(C3, 2026-10-12)**: G10 을 지난 사람은 보스 진입 존에서 부활하므로, 부활 격자점이 진입 패드 위면 부활만으로 보스방 카운트다운이 시작될 수 있었다.
  - 패드 범위 = 서버 씬의 모든 `BossEnterTrigger` GameObject 의 켜진 **트리거** 콜라이더 bounds 를 XZ 로 투영(`TutorialDeathRules.ServerPadAreas()` — 박스·메시 = 사각형, 구·캡슐 = 원, 프레임당 1회 갱신).
    트리거는 서버에만 붙으므로 클라·비튜토리얼·스폰 전이면 빈 목록 = 기존 동작.
  - 순수 규칙 `TutorialPadArea`·`TutorialRevivePointRules.IsOnPad`·`TrySelect(…, pads, padMargin, …)`: 패드 범위 + `DefaultPadMargin`(1m) 안 후보는 안전 후보·'가장 먼 후보' 폴백 모두에서 뺀다.
  - 빼고 나서 후보가 없고 그 사람이 G10 을 지났으면 폴백 = `PadFallbackCandidates` — G10 배리어 앞면에서 보스 진입 존 쪽 1m·가로 가운데.
    그 점이 패드 여유 안이면 더 안쪽(패드 쪽)으로 가지 않고 ① 배리어 쪽 `BackBarrierClearance`(0.75m) 지점 ② 배리어를 따라 옆으로 0.5m 간격(배리어 박스 가로 끝 1m 안쪽까지).
    배리어 앞면 0.75m 미만으로는 붙이지 않는다(개인 뒤 배리어 콜라이더 끼임 방지). 바닥·점유 검사를 통과한 첫 점. 그래도 없으면 기존대로 그 자리.
  - 추락 복구: 거름(`TutorialBackBarrierRules.IsReturnPointAllowed(…, pads)` — `TutorialBarriers.IsReturnPointAllowed` 가 넘김)이 패드 범위 + 1m 를 **누구에게나** 거부하고,
    스폰 폴백 대체(`ReturnPointFallback`)는 위 부활 탐색을 그대로 쓴다. 테스트: `TutorialDeathRulesTests` 패드 5건 · `TutorialBackBarrierRulesTests` 패드 1건.
- **전멸 리스폰 · 보스 재도전 되감기(할 일 30 (2), 2026-10-13)** — 상세 [party-lives-respawn.md §3](party-lives-respawn.md#3-전멸--복도4-리스폰-2단계--구현됨).
  - 공용 목숨 0 전멸 + G10 열림이면 결과 화면 대신 `PartyWipeRespawner` 가 전원을 **복도4** 에 리스폰(`TutorialDeathRules.TryFindWipeRespawnPoints` — 복도4 격자·바닥·점유·G9 앞면 여유·패드 + 1m 밖·2m 간격).
  - 되감기 `TutorialDirector.RewindForBossRetryServer`(순수 `TutorialFlowModel.RewindForBossRetry`) — **단계·게이트 '앞으로만' 규칙의 유일한 예외**: Finished → BossEnter, G11 닫기, 도착 래치 해제. G1~G10·장부·카운터 그대로.
  - 개인 뒤 배리어 `ResetBackBarrierMaskServer(clientId, Corridor4)` = `TutorialBackBarrierRules.MaskWithFrontmost(Corridor4)`(G1~G9, G10 제외) — **되감기보다 먼저** 쓴다(되감기로 진행 중이 되는 순간 G10 뒤 배리어가 앞길을 막지 않게).
  - 사망 규칙 `TutorialDeathRules.ResumeForBossRetry` — `_finished` 해제·대기 기록 비움. **보스방 재도착 전 사망 = 차감 없음**(복도4 자동 부활), **재도착(G11 다시 열림) 뒤 사망 = 다시 차감**(사양 그대로). 테스트: `TutorialBossRetryTests`.
- **리스폰 뒤 특성 초기화 · 재선택(할 일 30 (3), 2026-10-13)** — 상세 [party-lives-respawn.md §4](party-lives-respawn.md#4-특성-초기화--재선택-3단계--구현됨) · [tutorial-traits.md §9](tutorial-traits.md#9-전멸-리스폰-뒤-특성-초기화--재선택-할-일-30-3단계).
  - 리스폰 직후 각자 "특성을 초기화하고 다시 선택할까요?" 팝업(`TutorialTraitDirector` — `PartyWipeRespawned` 구독, 튜토리얼일 때만). 예 = 그 플레이어 특성 효과만 원복 + 혼자 4라운드(3장 중 1장, 고른 것 제외, 선택 중 무적). 아니오 = 유지.
  - 🔴 **팝업·재선택 중인 사람이 있으면 보스 진입 패드가 잠긴다**(`TutorialBossPath.IsBossPadEnabled` — G10 전 잠금과 같은 경로·테두리 숨김). 패드는 한 명만 서도 전원을 끌고 가기 때문(D3).
    끝나면 이미 서 있던 사람으로 카운트다운. 사망 규칙·뒤 배리어·되감기는 건드리지 않는다. 마감 게이지·자동 선택 없음(진행 중 복도가 없음).

## 12. 스텝별 고정 안전 지점 (할 일 80 — 1안, 2026-10-22)
> 코드: `TutorialSafePointRules`(순수 — 감시 조건·구간 순서·기본 위치) · `TutorialSafePoints`(4.MapScene 상주, 서버) ·
> 훅 `PlayerSafePointTracker.ReturnPointOverride`(신규, 기본 null = 기존 동작). 저작: `Tools/Map/Authoring/Tutorial/8. Place Safe Points (열린 씬)`.
> 테스트: `TutorialSafePointRulesTests`(튜토리얼 러너). 디렉터·흐름 코드는 바꾸지 않았다(디렉터 공개 API·`TutorialDeathRules.TryFindAreaSpreadPoints` 만 부른다).

**데이터** — `4.MapScene` 루트 `TutorialSafePoints`(비네트워크) + 자식 `SafePoint_{구간}` 9개(존1~4·복도1~4·보스 진입). 지점 = 씬 Transform 참조(`points`).
저작 메뉴가 **빠진 구간만** 기본 위치에 만든다(멱등 — 손으로 옮긴 지점은 그대로). 기본 위치(2026-10-22 배치, Y = 스테이지 0):

| 구간 | 기본 위치(월드 XZ) | 규칙 |
|---|---|---|
| 존1 | (29.83, −200.40) | 가운데(스폰 존) |
| 복도1 | (17.93, −200.40) | 입구 배리어 G1 앞면 + 1.5m — 중간 지점(특성 창) 전 |
| 존2 | (7.68, −200.40) | 복도1 출구 G2 앞면 + 2.5m |
| 복도2 | (0.18, −188.50) | G3 앞면 + 1.5m |
| 존3 | (0.18, −178.05) | G4 앞면 + 2.5m |
| 복도3 | (0.18, −138.65) | G6 앞면 + 1.5m |
| 존4 | (0.18, −128.40) | G7 앞면 + 2.5m |
| 복도4 | (−11.73, −101.11) | G9 앞면 + 1.5m |
| 보스 진입 | (−21.97, −101.11) | G10 앞면 + 2.5m(패드 + 1m 안이면 런타임 거름이 다른 점으로) |

튜닝 값(전부 `TutorialSafePoints` 인스펙터 — 씬 데이터): 낙사 사용 `useOnFall` · 바닥 찾기 ±1.5m · 점유 반경 0.4m · 평지 10°.

**언제(서버, 튜토리얼 활성 · 단계 None/Finished 아님 · 보스방 이동(`IsCountdownActive`/`IsEncounterBusy`) 아님)**
- **낙사**: 추락 생존·추락 사망의 복귀 지점(`ResolveReturnPoint`)을 큐·Ring 탐색 대신 고정 지점으로. 연출(낙하 카메라·0.75초 지연·무적·입력 잠금)은 기존 그대로.
- 낙사 말고는 되돌리지 않는다 — 처음 구현에 있던 구간 이탈(1.5초)·끼임(2초) 자동 복귀는 요청 범위 밖이라 뺐다(2026-10-22).

**어느 지점**: 자동 부활과 같은 '그 플레이어 기준 지금 단계 구간'(`TutorialRevivePointRules.AreaOrder(step, mask, passed)` — 단계 구간을 배리어 상한으로 내리고,
그 플레이어의 지나온 최전방 구간 아래로는 안 감)부터 거꾸로 — 지점이 있고, 바닥·점유가 괜찮고, `ReturnPointFilter`(닫힌 배리어·뒤 배리어·지난 배리어 앞면 여유·패드 + 1m) 를 통과하는 첫 구간.
지점이 막혔으면 같은 구간에서 지점에 가장 가까운 안전 격자점(`TryFindAreaSpreadPoints` — 전멸 리스폰·구간 스킵과 같은 탐색). 하나도 없으면 기존 동작(큐).

**로그(서버, `Edit.Log`)**: `[Tutorial] 안전 지점 켜짐 — 지점 9개 · 낙사 켬.` · `client n 낙사 → Zone2 안전 지점 …` · `[Fall/진단] 고정 안전 지점으로 — …`.

**알려진 한계 · Play 로 볼 것**
- 낙사가 '최근 밟은 자리'가 아니라 구간 입구로 돌아간다(1안 사양). 존2 물 구멍처럼 자주 떨어지는 곳은 이동 거리가 늘 수 있다 — 싫으면 `useOnFall` 끔.
- 지점 Y 는 스테이지 높이(0) — 존 바닥은 런타임 존 프리팹이라 에디터에서 검증하지 못했다. 바닥이 없으면 런타임이 격자 탐색으로 내려간다.
- 할 일 86(존1·존3 검사 로비 이전) 뒤 구간이 바뀌면 지점만 지우고 메뉴를 다시 돌린다.
