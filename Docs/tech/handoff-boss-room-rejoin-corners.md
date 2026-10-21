# handoff — 보스 룸 재합류 모서리 4지점 (2026-10-19, 은희 → 경석)

할 일 48 C14 R3c. 공유만 한다(사전 동의 없이 진행 — 사용자 결정 Q-F). 보스 FSM·`TwentyThreeBoss`·`BossDataSO`·`BossEncounterDirector` 동작은 고치지 않았다.

## 왜
판 도중 끊긴 플레이어가 다시 들어오면(재합류) 서버가 새로 스폰한다. 보스 룸 밖이면 맵 첫 스폰 지점에 선다.
보스 룸 안에서 싸우는 중이면 첫 스폰에 세우면 파티와 멀어지고 보스 옆에 세우면 바로 맞는다.
그래서 **아레나 네 모서리 중 다른 플레이어·보스에서 가장 먼 곳**에 세운다.

## 바뀐 것
- `Assets/2.Prefabs/Environment/Layouts/Zones/bossroom.prefab` — 루트 바로 아래 `RejoinCornerPoints` 그룹과 자식 `Corner1~4` 추가(빈 Transform, 다른 오브젝트 무변경).
  - 위치 기준 = `InvisibleBoundaries/Boundary_*` 의 **안쪽 면에서 2m 안쪽**, 바닥 높이(루트 로컬 y 0.5). 지금 값은 (±11.366, 0.5, ±11.366).
  - 순서 = 방 로컬 (−X,−Z)·(+X,−Z)·(+X,+Z)·(−X,+Z). 회전 = 방 중심을 본다.
  - 확인: 네 점 모두 아래로 레이캐스트하면 `BossFloorCollider` 에 맞는다. 송전탑(`TwentyThreeCharger`, 루트 로컬 ±3.7)·`NavMeshMargin`(폭 1m)과 겹치지 않는다.
- `Assets/1.Scripts/Map/BossTeleportManager.cs` — 읽기 전용 getter `IsElevatorRunning`(서버 전용, `_elevatorRunning` 그대로 반환) 추가. 동작 무변경.
  재합류 스폰 보류(엘리베이터 연출 중엔 세우지 않는다)가 읽는다.
- `Assets/1.Scripts/Map/Editor/BossRoomAuthoring.cs`
  - `Rebuild Boss Room Bounds` 의 `BuildReferencePoints` 끝에서 `RejoinCornerPoints` 를 **없을 때만** 만든다. 있으면 건드리지 않는다(옮긴 지점 보존 — `PlayerArrivalPoints` 와 같은 규칙).
  - 새 메뉴 `Tools/Map/Authoring/Add Boss Room Rejoin Corners` — 모서리만 추가하고 저장한다(경계·바닥 재생성 없음). 그룹이 있으면 아무것도 안 한다.

## 런타임이 읽는 방식
- `Assets/1.Scripts/Network/Session/BossRoomRejoin.cs` 가 `GameObject.Find("RejoinCornerPoints")` 의 자식 `Corner1~4` 를 이름으로 찾는다(넷 다 있어야 쓴다).
- 못 찾으면 `InvisibleBoundaries` 안쪽 면에서 2m 인셋으로 같은 네 점을 계산하고 경고를 1회 남긴다(`[Rejoin] 보스 룸 'RejoinCornerPoints/Corner1~4' 를 못 찾아…`).
  경계는 23호와 같은 방 로컬 축으로 읽는다(`TwentyThreeBoss.ResolveArena` — 씬에서 yaw −90).
- 고르는 규칙(`Assets/1.Scripts/Network/Session/RejoinSpawnPicker.cs` `FarthestCorner`): 모서리마다 다른 플레이어·보스(`TwentyThreeBoss`)까지 수평 최소 거리를 구해 가장 큰 곳, 동률이면 앞 번호.
  모서리 Pose 그대로 세운다(텔레포트 도착 지점처럼 바닥 높이 — 첫 스폰의 10m 낙하 없음).
- "보스 룸인가" 판정(`BossRoomRejoin.IsInBossRoom`): `BossEncounterDirector.Phase == Combat` 이거나, `Phase != Idle` 이고 다른 플레이어(영구 사망 제외) 중 1명 이상이 `BossArea` 태그 박스 안(수평).
  Director 는 공개 `Phase` 만 읽는다. 보스 위치는 Director 의 private 참조 대신 씬의 `TwentyThreeBoss` 를 찾는다.
- 호출 위치: `Assets/1.Scripts/Loading/NetworkLoadingFlowController.cs` `SpawnRejoiner`·`TickPendingRejoin`.

## 지점을 옮기는 법
`bossroom.prefab` 을 열어 `RejoinCornerPoints/Corner1~4` 를 옮기면 끝이다. 이름만 유지하면 된다(그룹은 어디에 둬도 이름으로 찾는다 — 단 씬에 같은 이름이 둘이면 안 된다).
기둥·장판 기믹 위가 아닌 바닥이면 된다. 회전은 스폰 직후 바라보는 방향이다.

## 되돌리는 법
`RejoinCornerPoints` 그룹을 지우면 런타임은 경계 계산 폴백(위와 같은 2m 인셋 네 점)으로 동작한다. 경고 로그만 1회 생긴다.
다시 만들려면 `Tools/Map/Authoring/Add Boss Room Rejoin Corners`.
`IsElevatorRunning` getter 를 지우면 `NetworkLoadingFlowController.TickPendingRejoin` 컴파일이 깨진다 — 같이 고쳐야 한다.

## 2026-10-21 QA B-2(M5 `Boundary_ZMax` P8) 조사 — 모서리 스폰은 원인 아님
할 일 71. 코드·프리팹은 고치지 않았다(가설이 반증돼서).
- QA 의 "모서리 3(0.0m)" 은 **재합류 위치 ↔ 모서리 마커** 거리다(`QALobbyScenarios.More.cs` `CheckFarthestCorner`). 벽 거리가 아니다.
  모서리 3 = 방 로컬 (11.366, 0.5, 11.366), `Boundary_ZMax` 안쪽 면 z=13.366 → 벽까지 2m(캡슐 반경 0.38m 밖 여유 1.62m). 저작·폴백 모두 같은 2m 인셋이다.
- P8 은 **재합류하지 않은 클라**(10-20 `152302-client-mppm951030ef`)에서 났다. 그 판 M5 재합류자는 `mppm2bdbbfaf`(에러 0)다.
  P8 탐지기는 `Player.LocalPlayer` 만 검사한다(`GameplayInvariantDetector.InspectWallPenetration`).
- 시각도 다르다: 951030ef P8 = 30.6s, 같은 틱에 P5 순간이동(보스룸 입장 텔레포트) 재발. 재합류자의 끊김 31.2s·재합류 스폰 ≈36s 보다 앞선다.
  → 남은 후보는 **보스룸 입장 텔레포트 도착 틱의 1회 겹침**이다(도착 지점 `PlayerArrivalPoints` 는 z≈-1.5~-8.5 로 ZMax 와 멀어 지점 자체는 아님). 별도 할 일로 본다.
- 참고: `development` 의 `bossroom.prefab` 에는 `RejoinCornerPoints` 가 없다(02e130f9 Work4 에디터 일괄 저장에서 빠짐). 런타임 폴백이 같은 좌표를 내므로 동작 차이는 없고 경고 1회만 난다. 되살리려면 위 메뉴.

## 관련
- 테스트: `Assets/1.Scripts/Network/Session/Editor/BossRoomRejoinTests.cs`(모서리 선택·폴백·판정 표)
- 재합류 전체: `Assets/1.Scripts/Network/Session/RejoinSpawnGate.cs`(보류 판정), `Assets/1.Scripts/Network/Session/MatchRoster.cs`(판 명단)
