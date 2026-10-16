# 보스 진입 엘리베이터 연출

> 할 일 46(2026-10-16) 기준. 코드: `Assets/1.Scripts/Map/BossElevatorTimeline.cs`(순수 시간표) · `BossElevatorRig.cs`(로컬 재생·보행 경로) ·
> `BossTeleportManager.cs`(서버 시퀀스·페이드·카메라·실루엣·오너 보행). 프리팹 `ZoneS_typeBossEnter.prefab` 의 `[Elevator]`(BossElevatorRig).
> 테스트 `Assets/1.Scripts/Map/Editor/BossElevatorTimelineTests.cs`.

## 1. 흐름

발판 → 카운트다운 → **입력 잠금 · 페이드 아웃** → (암전 중 문 앞 FrontPoints 로 이동 + **카메라가 엘리베이터로 컷**) → **페이드 인**(이미 엘리베이터를 보고 있음) →
**하강** → **문 열림** → **P1 → P2 → P3 순서로 걸어 들어감**(Walk 애니, 걷는 동안 실루엣 꺼짐) → **문 닫힘** → **상승**(숨김, 카메라는 칸을 따라감) →
페이드 아웃 → 완전 암전 → 보스방 도착(도착 ACK → 보스 등장 연출, 기존 그대로).

| 단계(`BossElevatorPhase`) | 시작 | 누가 무엇을 |
|---|---|---|
| `FrontFadeOut` | 0(카운트다운 끝) | **서버**: 입력 잠금(보이는 채로·무적). 참가자 화면이 검게 |
| `FrontBlackout` | `TeleportFrontAt` = `CameraFocusAt` | **서버**: 참가자를 슬롯 순서대로 FrontPoints 로 순간이동. **각 피어**: 참가자 카메라가 착지 중앙으로 컷 |
| `FrontFadeIn` | `FrontFadeInAt` | 화면이 밝아짐(아웃 → 암전 → 인이 끊김 없이 한 번) |
| `WaitDescend` | `FrontFadeInEndAt` | **서버**: 오너에게 보행 지시. 하강 대기(`waitBeforeDescend`) — 별도 카메라 포커스 대기는 없다 |
| `Descending` | `DescendStartAt` | 칸 하강(OutQuint) |
| `DoorOpening` | `DoorOpenAt` | 문 Open 클립 |
| `WalkingIn` | `WalkInAt` | 슬롯 s 가 `WalkStartAt(s)` 에 걷기 시작해 `WalkDuration` 뒤 칸 안 도착. 걷기 시작하면 그 플레이어 실루엣 꺼짐 |
| `DoorClosing` | 마지막 도착 + `closeDelayAfterWalk` | 문 Close 클립 |
| `Ascending` | `AscendStartAt` | **서버**: 참가자 숨김. 칸 상승(InCirc 가속 뒤 등속). 참가자 카메라가 칸을 따라감 |
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
| `_timing.waitBeforeDescend` | 0.5 | 0.5 | 페이드 인 끝 → 하강 시작(초) |
| `_timing.descendDuration` | **2.1** | 1.2 | 하강 시간(초, 1.75배) |
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
- 3인 기준 총 길이: 입력 잠금 0 · 문 앞 이동·카메라 전환 +0.4 · 페이드 인 +0.6 · 페이드 인 끝(보행 지시) +1.1 · 하강 +1.6 · 문 열림 +3.7 · P1 출발 +5.66 · P3 도착 +8.65 · 문 닫힘 +9.15 · 상승 +10.83 · 암전 +13.43초(1인 +12.23).
- 시트에 행이 생기기 전(병합 Export 전)에는 프리팹 값으로 돈다(data-table.md §3).

## 3. 구현 결정

- **보행 = 오너 권한 연출 이동.** 서버가 페이드 인 끝(`FrontFadeInEndAt`)에 오너에게 `ElevatorWalkOwnerClientRpc(목표, 연출 시작 시각, 출발 시각, 시간)` 를 보내고,
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
  🔴 다른 계통이 자기 임시 입력 잠금을 풀 때는 `PlayerInputReader.SetInputEnabled(true)` 가 아니라 `PlayerLifeInputPolicy.ApplyCurrentAccess()` 로 푼다.
  예전 `PlayerFallRecovery.CancelRecoveryOwnerRpc` 가 직접 켜서, 문 앞 이동(`TeleportParticipant` → `CancelRecoveryServer`, 낙하 중이 아니어도 항상 보냄)이
  엘리베이터 입력 잠금을 +0.4초에 지웠다(할 일 46 후속 2).
- **상승 때는 숨긴다(칸과 함께 옮기지 않는다).** 칸은 각 피어가 로컬 시간표로 움직이고 플레이어는 오너 NetworkTransform 보간(지연)으로 보인다 —
  14m/s 로 올라가는 칸에 태우면 원격 화면에서 플레이어가 칸 바닥을 뚫고 처지거나 떨린다. 문이 다 닫힌 순간 숨기므로 보이는 차이가 없다.
- **실루엣**: 각 피어가 `WalkStartAt(슬롯)` 부터 `PlayerSilhouetteTag.SetSuppressed(true)`, 연출이 끝나면(시작 시각 0 — 도착 완료·도착 무효·재도전·디스폰) 되켠다(보는 사람 기준 색 그대로).
- **페이드 번쩍임 수정**: 문 앞 이동 RPC 가 따로 "암전 → 페이드 인" 을 걸던 것을 도착 이동에만 남겼다. 엘리베이터가 있으면 카운트다운 직전 암전도 하지 않는다 — 시간표 페이드 하나로 이어진다.
- **카메라**: 문 앞 이동 암전(`CameraFocusAt` = `TeleportFrontAt`, 컷 — `CameraTargetSwitcher.SetTemporaryFollow` 가 감쇠 없이 넘긴다) ~ 상승 전 = 착지 중앙(`BossElevatorRig.FocusPoint`, 칸은 위에서 내려오므로), 상승 ~ 도착 = 칸. 도착·연출 끝에 플레이어로 컷 복귀.

## 4. 다른 경로와의 관계

- **재도전**(`ResetForRetryServer`)·**도착 무효**(ACK 타임아웃·전멸)·**도착 완료** → `EndElevatorServer` 가 입력 잠금·숨김 해제, 시작 시각 0·참가자 목록 비움 → 각 피어가 리그 대기 복귀·카메라·실루엣 복구, 오너 보행 중단.
- **접속 끊김**: 서버 단계마다 접속 중인 참가자만 다룬다(끊긴 슬롯은 건너뜀, 시간표는 그대로). 끊긴 플레이어의 잠금은 플레이어 오브젝트와 함께 사라지고,
  남은 참가자는 도착 완료·무효(전원 끊김이면 암전 시각에 `TeleportAlivePlayers` → `AbortArrival`)에서 풀린다.
- **제한시간 강제 진입**: 같은 경로(Soul 부활 뒤 연출).
- **튜토리얼 G10**: `TutorialElevatorDoor` 가 `IdleDoorOpen` 을 켜 대기 문 포즈만 열림. 재생 중엔 리그가 문을 쥐고, 끝나면 대기 포즈(열림)로 돌아간다.
- 보스 등장 연출 연결(`AlivePlayersArrived` → `BossEncounterDirector`)은 바뀌지 않았다.
