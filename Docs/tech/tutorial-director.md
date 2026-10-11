# 튜토리얼 진행 상태 — TutorialDirector

> 할 일 15 / S1 (2026-10-11). 원본 시나리오: [../design/tutorial-scenario.md](../design/tutorial-scenario.md) §2.1·§4 · 계획: [../../PLAN-tutorial.md](../../PLAN-tutorial.md).
> 코드: `Assets/1.Scripts/Map/Tutorial/` — `TutorialFlowModel`(순수 규칙) · `TutorialDirector`(서버 소유·복제) · `TutorialStageMarker`(판별).
> 테스트: 메뉴 `Tools/Tests/튜토리얼 EditMode 테스트 실행`(`TutorialFlowModelTests` · `TutorialAreaTrackerTests`). 튜토리얼 단계의 새 픽스처는 이 러너에 넣는다.
> S2 구간 감지는 §6. S3 배리어는 §7. S4 존 장부·몬스터 휴면은 §8. S5 타이머·보스 길은 §9. **다음 레인 인계는 §10.**

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

S2~S5 에서 늘어난 API 전체 목록은 §10.1.

다음 단계가 붙일 자리는 `TutorialDirector` 클래스 주석(S3 배리어 → `GateOpened`/`IsGateOpen`, S4 → G3·Zone2Kills, S9 → Zone3Interrupts, S12 → Timeout 열림 시 특성 자동 획득).

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

**8개 = 복도 n 입구(앞 존 쪽)·출구(다음 존 쪽)**: 복도1 G1/G2 · 복도2 G3/G4 · 복도3 G6/G7 · 복도4 G9/G10. G5·G8 은 배리어 없음.
- 모양 = §6 복도 사각형의 양 끝에서 **복도 안쪽으로** 두께 0.4m(존 사각형을 침범하지 않음), 가로 = 복도 너비 + 양옆 0.3m(복도 벽 안으로 파고듦), 높이 3m.
  2026-10-11 프리팹 점검: 8개 모두 양옆 벽 안쪽 면 ±1.38m, 벽 바깥 면 ±2.63m(배리어 반폭 2.3m — 벽 밖으로 안 나옴).
- 어디에: Play 중에만 `TutorialStageMarker.Start` 가 StageTutorial 루트에 `TutorialBarriers` 를 붙이고, 그것이 루트 아래 `TutorialBarriers/Barrier_G*` 를 만든다.
  🔴 공용 존 프리팹(Zones/*)·씬에는 아무것도 저장하지 않는다. 프리팹에 저장된 것은 마커의 머티리얼 참조 한 줄뿐.

**언제 · 어떻게 여나**
- 디렉터가 튜토리얼을 켠 것(`IsTutorialActive`)이 그 피어에서 처음 보일 때 만든다(`StateChanged`). 튜토리얼이 아니면 만들지 않는다.
- 이미 열린 게이트는 만들 때 바로 끈다(늦은 클라 = 스폰 직후 `StateChanged` 로 현재 상태). 이후 `IsGateOpen` 이 켜지면 콜라이더·카브를 **즉시** 끄고
  0.35초 페이드(실제 시간, 높이·알파) 뒤 비활성. 다시 닫히는 일은 없다(게이트는 앞으로만).

**막기**
- 플레이어: **Wall 레이어** 비트리거 BoxCollider. 플레이어 모터 스윕 마스크(`PlayerGameRuleData.obstacleMask` = Default|Ground|Wall|Enemy|Env)가 본다.
  걷기·대시·이동 스킬(어쌔신 Q 돌진·평타 전진, 거너 E 백스텝·우클릭 반동, 팔라딘 전진)·넉백이 모두 같은 스윕(`PlayerMovementSimulation`)이라 막힌다.
  스윕을 안 타는 것 = 순간이동(`TeleportAuthoritative` — 스폰·추락 복구·보스방 이동)과 23호 잡기/밀기(`PlayerRestrainedState` — 보스방 전용).
  Wall 은 거너 평타·차지 레이저 차단 레이어에도 있어 배리어 너머로는 거너 빔이 막힌다.
- 몬스터: NavMeshObstacle 카브(Box, `carveOnlyStationary` 끔, 위아래 +2m — `ZoneBridgeGate` 와 같은 설정). Wall 은 NavMesh 베이크 수집(Default·Ground)에 안 들어가 다리 재베이크에 섞이지 않는다.
- 추락 복구: 서버 `ResolveReturnPoint` 가 기준점·Ring 후보마다 `ReturnPointFilter` 를 부른다 → `TutorialBarrierRules.IsPointReachable` —
  점을 품은 구간이 모두 '아직 못 들어가는 구간'(들어가는 배리어가 닫힘)이면 거부. 튜토리얼 구간 밖(보스방)은 허용. 스폰 폴백은 거르지 않는다(존1).
- 타이머 만료·패드 이동은 `BossTeleportManager` 순간이동이라 배리어와 무관(§4 "만료 강제 이동은 G9·G10 과 무관").

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

## 9. 보스 타이머 · 보스 길 (S5)
> 코드: `TutorialBossPathRules`(순수) · `TutorialBossPath`(판별 한 곳 — 경석 코드가 묻는 곳) · `TutorialElevatorDoor`(모든 피어) · `TutorialDirector`(G8·HUD·도착) ·
> 경석 코드 훅(`BossTimerManager`·`BossTimerHUD`·`BossEnterTrigger`·`BossEnterZoneVisual`) — 무엇을 왜는 [handoff-tutorial.md](handoff-tutorial.md) §3.
> 테스트: `TutorialBossPathRulesTests`(튜토리얼 러너). 회귀: `BossTimerClockTests`(슬로우 모션 러너). 디버그: `Tools/Tutorial/Debug/` 보스 길 5개.

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
| 도착 | 타이머 Stopped·게이지 0(기존) + 디렉터 단계 `Finished` + `BossRoomArrived` | 기존 |

- **G8 과 타이머 시작은 하나다**: 타이머를 직접 켜는 API 는 디렉터에 없다 — `StartBossTimerServer()` = `TryOpenGateServer(G8)`, 실제 시작은 G8 열림 처리 한 곳.
  '타이머 전/후'(S11 목숨 차감, S10 존4 기상)는 **`IsBossTimerStarted`(= G8 열림, 모든 피어)** 하나로 본다. `BossTimerManager.State` 는 만료·도착(Stopped)으로 바뀌어 기준이 못 된다.
- 엘리베이터 문 컨트롤러(`4.Animations/.../MV_BossroomEnter_Doors.controller`)에는 파라미터·전이가 없다(Closed 기본, Open·Close 고립 상태). 애셋을 고치지 않고 상태를 직접 재생한다.
  문을 다시 닫는 연출(보스방 이동 때 Close 등)은 없다 — 필요하면 같은 방식으로 `Close`.
- 도착 = `BossTeleportManager.AlivePlayersArrived`(서버, 패드·강제 공통). 판 전체 1번. ACK 실패(`ArrivalAborted`)는 도착이 아니다.

## 10. 인계 (S0~S5 레인 마무리 — S6~S12 가 읽을 것)

### 10.1 공개 API · 이벤트 전체
**`TutorialDirector`** (씬 상주 NetworkObject, `Instance`)

| 쪽 | 시그니처 |
|---|---|
| 읽기(모든 피어) | `bool IsTutorialActive` · `TutorialStep Step` · `bool IsGateOpen(TutorialGate)` · `bool WasGateAutoOpened(TutorialGate)` · `int GetCounter(TutorialCounter)` · `int GetCounterTarget(TutorialCounter)` · `bool TryGetPlayerProgress(ulong, out TutorialPlayerProgress)` · `bool TryGetWait(out TutorialGate, out int done, out int total, out float autoAdvanceRemaining)` · `bool IsTimerHudShown` · `bool IsBossTimerStarted`(= G8) · `bool IsFinished`(단계 Finished) · `float BossTimerLimitSeconds` · `float AreaEnterMargin/AreaExitMargin` |
| 서버 쓰기 | `bool ReportCheckServer(ulong, TutorialCheck)` · `bool AddCounterServer(TutorialCounter, int = 1)` · `bool TryOpenGateServer(TutorialGate, TutorialGateOpenReason = Manual)`(순서 어기면 거부) · `bool RequestGateServer(TutorialGate)`(차례 전이면 보류) · `bool SetStepServer(TutorialStep)`(앞으로만) · `void ShowTimerHudServer()` · `bool StartBossTimerServer()`(= G8) |
| 서버 몬스터 | `int SetZoneDormantServer(TutorialArea, bool, TutorialMonsterFilter = All)` · `int WakeZoneServer(TutorialArea, filter = All)` · `bool SetMonsterDormantServer(MonsterBase, bool)` · `void GetZoneMonstersServer(TutorialArea, filter, List<MonsterBase>)` · `bool TryGetZoneMonsterCountServer(zone, filter, out total, out alive)` · `bool IsZoneClearedServer(TutorialArea)` · `string DescribeMonsterZonesServer()` |
| 서버 구간 | `TutorialArea GetPlayerAreaServer(ulong)` · `bool HasPartyEnteredServer(TutorialArea)` · `bool HasReachedCorridorMidServer(ulong, int)` |
| 오너 클라 | `void ReportLocalCheck(TutorialCheck)`(Move·PassiveTooltip만 → ServerRpc, 서버 재검증) · `static bool IsOwnerReportable(TutorialCheck)` |
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
  `TutorialGate`(G1~G10) · `TutorialCheck`(Move·Dash·AttackHit·PassiveTooltip·SkillQ·SkillE·TraitCorridor1~4) · `TutorialCounter`(Zone2Kills·Zone3Interrupts·Zone4Panels) ·
  `TutorialArea`(None·Zone1~4·Corridor1~4·BossEnter) · `TutorialMonsterFilter`(All·NormalOnly·KeyOnly) · `TutorialGateOpenReason`(Manual·AllDone·Timeout·CounterReached).

### 10.2 S6~S12 가 붙일 자리
| 단계 | 자리 | 할 일 |
|---|---|---|
| S6 안내 UI | `TutorialDirector.HandleAreaFirstEntered` `TODO(S6)` · `HandleModelGateOpened` `TODO(S6)`(G10 → `TUT_B_PAD_WARN` 전원) | 단계 갱신 규칙(`SetStepServer` — 아직 아무도 안 부른다: 활성 때 Zone1, 도착 때 Finished 뿐) · 목표 패널·자막(늦은 클라 = `StateChanged` 로 재구성) |
| S7 존1 | `ReportOwnerCheckServerRpc` `TODO(S7)`(이동 거리 검증) | Dash·AttackHit = `ReportCheckServer` · Move·PassiveTooltip = `ReportLocalCheck` |
| S8 존2·복도 | `HandleCorridorMidReached` `TODO(S8/S12)` · `AreaFirstEntered(Zone2)` | 존2 안내 4초 뒤 `WakeZoneServer(Zone2)` · 특성 창 → `ReportCheckServer(TraitCorridorN)` |
| S9 존3 | `AreaFirstEntered(Zone3)` · handoff-tutorial.md §2 | 스피너봇 휴면 해제 → 연습 모드 → `AddCounterServer(Zone3Interrupts)` → G5 자동 → 본 모드 |
| S10 존4 | `AreaFirstEntered(Zone4)` | Z4-0 `ShowTimerHudServer()` + 자막 → 끝나면 `StartBossTimerServer()`(G8) → 3초 뒤 `WakeZoneServer(Zone4, NormalOnly)` · 다리 개통 `KeyOnly` · 패널 `AddCounterServer(Zone4Panels)` · 만료 문구 = `BossTimerManager.State == Expired` |
| S11 사망 | — | 목숨 차감 기준 `IsBossTimerStarted` · 패드/강제 이동 시작(`BossTeleportManager.IsCountdownActive`) 때 대기 부활 취소·즉시 부활 |
| S12 통합 | `HandleBossRoomArrived` `TODO(S12)` | 안내 종료(`BossRoomArrived`/`IsFinished`) · 개인 게이트 `Timeout` 열림 → 미선택자 자동 획득 · 만료 시 열린 특성 창 닫기 |

### 10.3 함정
- **스폰 순서**: `MapGenerator.OnGenerated` 가 디렉터·타이머 스폰보다 먼저 올 수 있다 — 서버는 보류 후 `OnNetworkSpawn` 에서 적용. 스폰 전 NetworkVariable/NetworkList 쓰기는 버려진다.
  스폰 전에는 `NetworkManager`(NetworkBehaviour 프로퍼티)가 null → `NetworkManager.Singleton` 으로 판정. 클라의 존 스폰(맵 생성)과 디렉터 스폰도 순서가 없다 — 존 오브젝트를 찾는 클라 코드는 재시도(엘리베이터 문처럼).
- **튜토리얼 판별은 `TutorialStageMarker` 하나**(경석 코드는 `TutorialBossPath` 경유). 디렉터 `_active` 복제를 기다리는 판정은 스폰 전에 false 라 늦다.
- **게이트 순서·보류**: G1→G10 순서만. `TryOpenGateServer` 는 차례가 아니면 거부, 존 클리어처럼 조건이 이미 충족된 것은 `RequestGateServer`(보류 → 앞 게이트 열리는 `Evaluate` 에서 연다).
  G8 이 열리면 보류된 G9 가 같이 열린다(중간보스를 타이머 전에 잡은 경우). 게이트 이벤트로 일을 붙일 때 **어느 경로로 열려도** 오는 `HandleModelGateOpened`(서버)·`GateOpened` 에 붙일 것.
- **휴면은 `MonsterBase.SetDormantServer` 하나**(I0 에서 통합 완료) — 스피너봇 연습 컴포넌트에는 휴면이 없고 연습/전환 대기/본 모드만 있다(handoff-tutorial.md §2).
- **씬 hash**: `4.MapScene` 의 `TutorialDirector` NetworkObject `GlobalObjectIdHash 114710176`. development 재반영·씬 머지 때 이 값이 0/중복이 되면 스폰이 깨진다 — 머지 뒤 확인.
  씬 상주 NetworkBehaviour 에 NetworkVariable 을 더하는 것(이번 `_timerHudShown`·`_limitOverride`)은 hash 와 무관하지만 모든 피어가 같은 빌드여야 한다.
- **Stage1 = 모든 판**(Start → 항상 튜토리얼). 튜토리얼 규칙(타이머 지연·패드 잠금·휴면)이 사실상 본게임 규칙이 된다 → D1 팀장 합의 전 development 병합 금지.
  절차 맵(`Stage1_Procedural`)에서 기존 동작이 그대로인지는 마커 유무 하나로 갈린다.
- 서버 전용 이벤트(`AreaFirstEntered`·`CorridorMidReached`·`ZoneCleared`·`BossRoomArrived`)는 클라에서 안 온다 — 클라 표시는 복제 값(`Step`·`IsGateOpen`·카운터·`StateChanged`)으로.
- 네트워크 없이 단독 실행하면 디렉터·타이머가 스폰되지 않아 아무것도 돌지 않는다 — 확인은 호스트(MPPM).

### 10.4 Play 확인 대기 (S0~S5 누적 — 사람이 MPPM 에서)
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
## 11. 사망 규칙 (S11)
> 코드: `TutorialDeathRulesModel`·`TutorialRevivePointRules`(순수) · `TutorialDeathRules`(서버, 디렉터에 런타임 부착). 테스트: `TutorialDeathRulesTests`(튜토리얼 러너).
> 훅(기본값 = 기존 동작): `PlayerLifeCycleController.ServerDeathKeepsSoul` · `PlayerReviveController.TryCompleteReviveOnServer(bool consumeLife)` · `PlayerFallRecovery.RelocateServer`.

- 부착: 서버에서 `StateChanged` 때 `IsTutorialActive` 면 디렉터 GameObject 에 `AddComponent`(씬·프리팹 저장 없음). 비튜토리얼은 안 붙는다.
- 사망(Alive → DeadPresentation) → 3초 뒤 자동 부활 + 2초 무적(`InvulnerabilityCause.Revive`). 차감 = **사망 시점** `IsGateOpen(G8)`(단계 기준, 위치 무관).
  G8 전은 목숨 수와 무관하게 Soul(훅), 부활 시 차감 없음. G8 뒤는 기존 규칙(목숨 0 → PermanentDead → 전원이면 PartyWipeWatcher).
- 부활 지점 = 열린 배리어로 들어갈 수 있는 가장 앞 구간(존1~복도4, **보스 진입 제외 — 패드**) → 앞 구간 순. 구간 안쪽 1m·1.5m 격자 중
  1층 바닥(스테이지 높이 ±1m)·점유 없음(Wall 포함)·살아 있는 `MonsterBase` 에서 10m(고정 — 몬스터 인지 반경 공개 API 없음) 밖, 사망 지점에 가장 가까운 점.
  안전한 곳이 없으면 몬스터에서 가장 먼 후보, 후보가 없으면 그 자리.
- 보스방 이동: `BossTeleportManager.IsCountdownActive`(패드·만료 강제 공통)가 켜지면 대기 부활 전부 '즉시·그 자리'(Soul 이 되는 대로). 이동 중 사망도 같다.
  튜토리얼 구간 밖(보스방)에 있는 Soul 은 늘 그 자리. 도착(`AlivePlayersArrived`/`ArrivalAborted`) 또는 단계 Finished 뒤 사망은 일반 규칙.
- 연출 잠금(`PlayerEncounterLock`) 중이면 보류 + 경고 1회, 풀리면 다음 프레임 부활(사망 연출 해소도 잠금 해제 뒤 진행).
- 추락 생존 복구는 그대로. 추락 사망은 기존대로 안전지점에 Soul 로 옮겨진 뒤 같은 규칙으로 부활.
- **TODO(S7~S10)**: '지금 단계 구간'(부활 지점의 기준 구간)은 **배리어 기준 임시 구현**이다 — 열린 배리어로 들어갈 수 있는 가장 앞 구간을
  '지금 단계'로 본다(`TutorialRevivePointRules`). S7~S10 에서 디렉터 `Step` 이 존·복도 진행에 맞게 갱신되면 Step 기준으로 바꿀지 **재확인**할 것(코드는 I0 에서 그대로 둠).
