# 보스 진입 엘리베이터 연출

> 할 일 46(2026-10-16) 기준. 코드: `Assets/1.Scripts/Map/BossElevatorTimeline.cs`(순수 시간표) · `BossElevatorRig.cs`(로컬 재생·보행 경로) ·
> `BossTeleportManager.cs`(서버 시퀀스·페이드·카메라·실루엣·오너 보행). 프리팹 `ZoneS_typeBossEnter.prefab` 의 `[Elevator]`(BossElevatorRig).
> 테스트 `Assets/1.Scripts/Map/Editor/BossElevatorTimelineTests.cs`.

## 1. 흐름

발판 → 카운트다운 → **입력 잠금 · 페이드 아웃** → (암전 중 문 앞 FrontPoints 로 이동 + **카메라가 엘리베이터 칸 안 지점으로 컷** + **하강 시작**, 이후 도착까지 그 지점을 화면 중앙에 두고 따라감) →
**페이드 인**(이미 엘리베이터가 내려오는 중) → 착지 → **문 열림** → **P1 → P2 → P3 순서로 걸어 들어감**(Walk 애니, 걷는 동안 실루엣 꺼짐) → **문 닫힘** → **상승**(숨김, 카메라는 칸을 따라감) →
페이드 아웃 → 완전 암전 → 보스방 도착(도착 ACK → 보스 등장 연출, 기존 그대로).

| 단계(`BossElevatorPhase`) | 시작 | 누가 무엇을 |
|---|---|---|
| `FrontFadeOut` | 0(카운트다운 끝) | **서버**: 입력 잠금(보이는 채로·무적). 참가자 화면이 검게 |
| `WaitDescend` | `TeleportFrontAt` = `CameraFocusAt` | **서버**: 참가자를 슬롯 순서대로 FrontPoints 로 순간이동. **각 피어**: 참가자 카메라가 칸으로 컷(도착까지 칸을 따라감). 하강 대기 `descendDelay`(기본 0 = 바로 하강) |
| `Descending` | `DescendStartAt` = `TeleportFrontAt + descendDelay` | 칸 하강(OutQuint). 완전 암전 속에서 출발 — 화면이 밝아질 때 이미 내려오는 중 |
| (페이드 트랙) | `FrontFadeInAt` → `FrontFadeInEndAt` | 하강과 겹쳐 화면이 밝아짐(아웃 → 암전 → 인이 끊김 없이 한 번, `FadeAlphaAt`). 끝 = **서버**: 오너에게 보행 지시 — 단계(`BossElevatorPhase`)가 아니다 |
| `DoorOpening` | `DoorOpenAt` | 문 Open 클립 |
| `WalkingIn` | `WalkInAt` | 슬롯 s 가 `WalkStartAt(s)` 에 걷기 시작해 `WalkDuration` 뒤 칸 안 도착. 걷기 시작하면 그 플레이어 실루엣 꺼짐 |
| `DoorClosing` | 마지막 도착 + `closeDelayAfterWalk` | 문 Close 클립 |
| `Ascending` | `AscendStartAt` | **서버**: 참가자 숨김. 칸 상승(InCirc 가속 뒤 등속). 참가자 카메라는 계속 칸을 따라감 |
| `FadingOut` → `Blackout` | `FadeOutStartAt` → `BlackoutAt` | 화면 페이드 아웃 → **서버**: 보스방 이동(도착 ACK) |

- 슬롯(P1, P2, P3) = **clientId 오름차순**(호스트 = P1). 지점보다 사람이 많으면 지점을 돌려 쓴다.
- 서버가 시작 시각(`_elevatorStartAt`)과 참가자 순서(`_elevatorWalkers` — 플레이어 NetworkObjectId)를 복제한다.
  늦게 합류한 피어·관전자도 같은 시각으로 칸·문·실루엣을 그린다. 화면 페이드·카메라는 **참가자 본인**만.
- 리그가 없는 구성(테스트 씬)은 예전처럼 카운트다운 직전 암전 → 바로 보스방 이동.

## 2. 수치 (데이터 테이블 `Map` 시트 · 대상 `ZoneS_typeBossEnter` · 타입 `BossElevatorRig`)

| 필드 | 기본값 | 이전 | 의미 |
|---|---|---|---|
| `_timing.frontFadeOutDuration` | 0.4 | (카운트다운 직전 0.3, `BossTeleportManager.fadeOutSeconds`) | 카운트다운 끝 → 완전 암전(초) |
| `_timing.frontBlackHold` | 0.2 | — | 문 앞 순간이동 뒤 암전 유지(초) |
| `_timing.frontFadeInDuration` | 0.5 | (0.5, `fadeInSeconds`) | 문 앞에서 밝아짐(초). 끝 = 보행 지시 |
| `_timing.descendDelay` | **0** | (`waitBeforeDescend` 0.5 — 페이드 인 끝 기준, 이름·의미 변경) | 문 앞 이동(암전·카메라 전환) → 하강 시작(초). 암전 유지(0.2)보다 길면 밝아질 때 칸이 아직 멈춰 있다 |
| `_timing.descendDuration` | **2.5** | 1.2 → 2.1 | 하강 시간(초). 하강 거리 20.14m(아래) 기준 평균 8.1m/s·출발 순간 최대 40m/s(OutQuint = 5H/T) |
| `_timing.doorOpenDuration` | 0 | 0 | 문 열림(초). 0 = Open 클립 길이(1.76) |
| `_timing.walkStartDelay` | 0.2 | — | 문 다 열림 → P1 출발(초) |
| `_timing.walkInterval` | 0.6 | — | 다음 사람 출발 간격(초) |
| `_timing.walkSpeed` | 3 | — | 걷는 속도(m/s). 보행 시간 = 가장 먼 경로(현재 5.37m) ÷ 속도 = 1.79초 |
| `_timing.closeDelayAfterWalk` | 0.5 | (`closeDelayAfterVanish` 1.5 — 삭제) | 마지막 도착 → 문 닫힘(초) |
| `_timing.doorCloseDuration` | 0 | 0 | 문 닫힘(초). 0 = Close 클립 길이(1.68) |
| `_timing.ascendDuration` | **2.6** | 1.5 | 상승 가속 시간(초) |
| `_timing.ascendSpeed` | **14** | 24 | 가속 뒤 상승 속도(m/s) |
| `_timing.fadeOutDelay` | **1** | 0 | 상승 시작 → 페이드 아웃(초) |
| `_timing.fadeOutDuration` | **1.6** | 1.5 | 페이드 아웃(초). 상승이 보이는 시간 1.5 → 2.6초(약 1.7배) |
| `_insideSpread` | 0.6 | — | 칸 안 도착 지점 간격 배율(문 앞 지점 배치를 칸 중앙 기준으로 줄임, 0 = 전원 정중앙) |

- 기술 값 `_timing.height`(0 = 자동, 칸 대기 높이)·`_landedLocalY` 는 `[DataTableIgnore]`.
- **하강 거리 = 칸 대기 localY − `_landedLocalY`**(리그 `Awake`·`BuildTimeline` 에서 프리팹에 놓인 칸 자리를 읽는다 — 코드에 높이 상수 없음).
  2026-10-16 사용자 배치: 칸 `PF_Prop_object_MV_bossroomenter` localY 20.69(이전 10.99) − 0.55 = **20.14m**(이전 10.44m). `[Elevator]` 루트 y 0.1 은 칸·`floor_charger` 가 같이 올라가 착지 높이 0.55(리그 기준)는 그대로.
- 시간표(3인): 입력 잠금 0 · 문 앞 이동·카메라 전환·**하강** +0.4 · 페이드 인 +0.6(칸 높이 13.3m, 34% 내려옴) · 페이드 인 끝(보행 지시) +1.1(3.9m 남음) · 착지·문 열림 +2.9 · 문 다 열림 +4.66 · P1 출발 +4.86 · P3 도착 +7.85 · 문 닫힘 +8.35 · 상승 +10.03 · 페이드 아웃 +11.03 · 암전 +12.63초.
  1인: P1 도착 +6.65 · 문 닫힘 +7.15 · 상승 +8.83 · 페이드 아웃 +9.83 · 암전 +11.43초. (하강 2.1 → 2.5 로 착지 이후 단계가 0.4초씩 밀림 — 할 일 46 후속 7.)
- 시트에 행이 생기기 전(병합 Export 전)에는 프리팹 값으로 돈다(data-table.md §3).

## 3. 구현 결정

- **하강 = 암전 기준**(할 일 46 후속 5). 예전엔 페이드 인 끝 + 0.5 에 출발해 밝아진 화면에 멈춘 칸이 보였다. 이제 문 앞 이동 순간(`TeleportFrontAt`) + `descendDelay` —
  밝아질 때 이미 내려오는 중이다. 문 열림·보행·닫힘·상승은 하강 끝 기준이라 같이 앞당겨진다. 암전 유지·페이드 인은 하강과 겹치므로 단계 enum 에서 뺐다(`FadeAlphaAt` 별도 트랙).
- **보행 = 오너 권한 연출 이동.** 서버가 페이드 인 끝(`FrontFadeInEndAt`, 하강 중 — 출발은 문 열림 뒤라 지시가 먼저 도착)에 오너에게 `ElevatorWalkOwnerClientRpc(목표, 연출 시작 시각, 출발 시각, 시간)` 를 보내고,
  오너가 `FixedUpdate` 에서 `PlayerMotor.SetPoseTarget` 로 지금 자리 → 칸 안 지점을 **직선 보간**한다(시간 기준 — 출발 자리가 문 앞에서 조금 어긋나 있어도 같은 시각에 도착).
  포즈 목표는 충돌 해석을 거치지 않아 문틀·칸 벽·다른 플레이어에 걸리지 않고, 매 틱 수직 속도 0 이라 떨어지지 않는다.
  방향은 `PlayerMovement.RotateImmediately`, Walk 애니는 `Player.SetAnimatorMoving`(IsMoving) — 위치는 오너 NetworkTransform, 애니는 NetworkAnimator(오너)로 복제된다.
  도착 뒤에도 보스방 도착 RPC·연출 끝까지 그 자리를 붙잡는다.
- **입력 잠금 = `PlayerEncounterLock.SetCinematicInputLockServer`**(새 복제 값). 숨김·보스 등장 잠금(`BeginCinematicServer`)과 독립 —
  FSM 을 Cinematic 으로 바꾸지 않아 보행 애니를 막지 않고, 도착 뒤 보스 등장 잠금이 겹쳐 걸려도 서로 풀지 않는다. 무적 토큰 `CinematicHidden` 은 입력 잠금·숨김 중 하나라도 켜진 동안.
- **입력 잠금 구간 = 카운트다운이 끝난 순간(연출 시작, `FrontFadeOut` t=0) ~ 보스방 도착 뒤 보스 등장 잠금으로 넘길 때**(사용자 결정 2026-10-16).
  페이드 아웃·문 앞 이동·페이드 인 동안에도 이동·공격·스킬·대시가 막힌다. 걸기 = `TeleportAfter` 가 시작 시각을 복제한 직후 `LockParticipants`,
  풀기 = `EndElevatorServer` 한 곳(도착 완료는 `AlivePlayersArrived` → 보스 등장 잠금이 걸린 **뒤** 부르므로 풀렸다 다시 걸리는 틈이 없다).
  리그가 없어 곧바로 이동하는 경로는 잠그지 않는다.
  잠금 순간 **진행 중이던 대시도 그 자리에서 끊긴다** — 오너가 복제 값을 받는 `PlayerLifeInputPolicy.ApplyAccess` → `CancelCombatActions`(`ShouldCancelToIdle`)가 스킬과 같은 경로로 Idle 로 바꾸고, `PlayerDashState.Exit` → `PlayerMotor.ClearDashMotion` 이 남은 대시 이동을 0 으로 만든다. 소비한 대시 충전은 환불하지 않는다(할 일 46 후속 3).
  🔴 다른 계통이 자기 임시 입력 잠금을 풀 때는 `PlayerInputReader.SetInputEnabled(true)` 가 아니라 `PlayerLifeInputPolicy.ApplyCurrentAccess()` 로 푼다.
  예전 `PlayerFallRecovery.CancelRecoveryOwnerRpc` 가 직접 켜서, 문 앞 이동(`TeleportParticipant` → `CancelRecoveryServer`, 낙하 중이 아니어도 항상 보냄)이
  엘리베이터 입력 잠금을 +0.4초에 지웠다(할 일 46 후속 2).
- **상승 때는 숨긴다(칸과 함께 옮기지 않는다).** 칸은 각 피어가 로컬 시간표로 움직이고 플레이어는 오너 NetworkTransform 보간(지연)으로 보인다 —
  14m/s 로 올라가는 칸에 태우면 원격 화면에서 플레이어가 칸 바닥을 뚫고 처지거나 떨린다. 문이 다 닫힌 순간 숨기므로 보이는 차이가 없다.
- **실루엣**: 각 피어가 `WalkStartAt(슬롯)` 부터 `PlayerSilhouetteTag.SetSuppressed(true)`, 연출이 끝나면(시작 시각 0 — 도착 완료·도착 무효·재도전·디스폰) 되켠다(보는 사람 기준 색 그대로).
- **페이드 번쩍임 수정**: 문 앞 이동 RPC 가 따로 "암전 → 페이드 인" 을 걸던 것을 도착 이동에만 남겼다. 엘리베이터가 있으면 카운트다운 직전 암전도 하지 않는다 — 시간표 페이드 하나로 이어진다.
- **카메라**: 문 앞 이동 암전(`CameraFocusAt` = `TeleportFrontAt`, 컷 — `CameraTargetSwitcher.SetTemporaryFollow` 가 감쇠 없이 넘긴다) ~ 도착 = **칸 안 카메라 지점**(`BossElevatorRig.CameraTarget`) 한 대상.
  지점 = 칸(`object_MV_bossroomenter.prefab`) 아래 `CameraFollowTarget` 인스턴스(이름으로 찾음, 없으면 칸 루트 폴백) — 칸과 함께 움직이고 화면 중앙에 온다
  (`PlayerFollowCamera` 는 데드존·컴포저 없이 `CinemachineFollow` 오프셋 (7,17,-7) 이 고정 회전(59.8°, -45°)의 시선 축과 일직선 — 대상 = 화면 중앙. 위치 감쇠 1 이라 하강 중엔 살짝 뒤따른다).
  이 인스턴스는 칸 프리팹에서 태그를 **Untagged** 로 덮어쓴다 — `CameraFollowTarget` 태그는 플레이어 추적 지점 표식이라(`CameraTargetSwitcher` 관전 목록·`FogManager` 폴백) 섞이면 안 된다(할 일 46 후속 6).
  하강 시작 때 칸이 화면 밖이어도 카메라가 칸과 함께 내려온다 — 하강·문 열림·보행·상승 내내 엘리베이터를 따라간다(판단 = `BossElevatorTimeline.CameraFollowsCabAt`).
  예전엔 상승 전까지 착지 중앙(`FocusPoint`) 고정점을 봐서 엘리베이터를 안 따라가는 것처럼 보였다(할 일 46 후속 4). 도착·연출 끝에 플레이어로 컷 복귀.

## 4. 다른 경로와의 관계

- **재도전**(`ResetForRetryServer`)·**도착 무효**(ACK 타임아웃·전멸)·**도착 완료** → `EndElevatorServer` 가 입력 잠금·숨김 해제, 시작 시각 0·참가자 목록 비움 → 각 피어가 리그 대기 복귀·카메라·실루엣 복구, 오너 보행 중단.
- **접속 끊김**: 서버 단계마다 접속 중인 참가자만 다룬다(끊긴 슬롯은 건너뜀, 시간표는 그대로). 끊긴 플레이어의 잠금은 플레이어 오브젝트와 함께 사라지고,
  남은 참가자는 도착 완료·무효(전원 끊김이면 암전 시각에 `TeleportAlivePlayers` → `AbortArrival`)에서 풀린다.
- **제한시간 강제 진입**: 같은 경로(Soul 부활 뒤 연출).
- **튜토리얼 G10**: `TutorialElevatorDoor` 가 `IdleDoorOpen` 을 켜 대기 문 포즈만 열림. 재생 중엔 리그가 문을 쥐고, 끝나면 대기 포즈(열림)로 돌아간다.
- 보스 등장 연출 연결(`AlivePlayersArrived` → `BossEncounterDirector`)은 바뀌지 않았다.
