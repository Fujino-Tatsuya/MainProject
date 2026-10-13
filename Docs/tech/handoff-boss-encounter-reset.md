# 인계 — 보스 인카운터 재도전 리셋 훅 (할 일 30 (2), 은희 → 경석)

> 작성 2026-10-13 · 브랜치 `feature/PartyLivesRespawn` · 설계 원본 [party-lives-respawn.md §3](party-lives-respawn.md).
> 경석 영역(보스) 파일에 **새 메서드만 추가**했다. 기존 메서드·필드·기본 동작은 바꾸지 않았고, 아래 메서드는 **전멸 리스폰이 부를 때만** 동작한다.

## 1. 왜

공용 목숨 0 으로 전멸하면 결과 화면 대신 복도4(절차 맵 = 보스 진입 패드 밖)에 전원 리스폰하고, 보스 23호를 **처음부터** 다시 싸운다.
보스 쪽에는 "처음 상태로" API 가 없어(2026-10-13 조사) 최소 훅을 넣었다. 호출자는 `PartyWipeRespawner.TryRespawnServer()`(`Assets/1.Scripts/Managers/`) 하나.

## 2. 훅 목록

| 파일 | 추가한 것 | 하는 일 |
|---|---|---|
| `Map/BossEncounterDirector.cs` (≈ 566~605행, `SnapBossToNavMesh` 바로 위) | `bool IsBossDefeatedServer` | 서버 — 보스가 쓰러져 결과 화면 전환을 기다리는 중(`_defeatHandled`). 이때 전멸은 리스폰하지 않는다(클리어) |
| 〃 | `bool ResetForRetryServer()` | 참가자 잠금 해제(`UnlockAllParticipants`) → 보스 디스폰(`DespawnBoss`) → `_bossMonster`·`_bossEntranceAnim`·참가자 스냅샷·`_combatStarted`·`_resultTransitionAt` 초기화 → 아레나 송전탑 전부 `EndCharge()`(멱등) → 페이즈 **Idle**. 보스가 이미 쓰러졌으면 거부(false) |
| `Map/BossTeleportManager.cs` (≈ 480~507행, `HandleClientDisconnected` 바로 위) | `void ResetForRetryServer()` | 카운트다운(패드·강제) 취소(`CancelCountdown`) · 도착 ACK 타임아웃 중단 · 대기/도착 명단 비움 · `_encounterSequence++`(늦은 ACK 버림) · 점유 `false`. 이벤트는 안 낸다. 다음에 패드(`BossEnterTrigger`)에 들어오면 기존 경로로 카운트다운 |
| `Map/BossTimerManager.cs` (≈ 323~341행, `_expired` 선언 위) | `void StopForRetryServer()` | 만료 재시도(`_expired`) 해제, 상태를 **Stopped**(게이지 0)로. 이미 Stopped 면 그대로. 타이머는 **재가동하지 않는다**(결정 — §4) |
| `Monster/Boss/TwentyThreeBoss.cs` | **수정 없음** | 리셋하지 않고 디스폰 → 다음 도착 때 새로 스폰(§3) |

## 3. 왜 '디스폰 → 다음 도착 때 새로 스폰' 인가 (제자리 리셋 대신)

- `TwentyThreeBoss` 는 FSM·쿨·차징·웰즈·전기 장판·드론·잡기·카운터 상태가 흩어져 있어, 제자리 리셋이면 그걸 빠짐없이 되돌리는 메서드를 새로 만들고 계속 맞춰야 한다.
- 디스폰 정리(`OnNetworkDespawn` — 장판·드론 `ResetState`, 웰즈 억제, `AbortAttackChain` 잡기 해제)와 스폰 초기화(`OnNetworkSpawn` — 공격 슬롯·쿨·웰즈 세팅)는 **매 판 쓰는 검증된 경로**다.
- 디렉터의 스폰(`SpawnBossServer`)은 이미 "스폰돼 있지 않으면 새로 만든다" 이고, 도착 처리(`HandleAlivePlayersArrived`)는 Idle/FailedSafe 에서 받는다 → **페이즈를 Idle 로만 돌리면 기존 코드가 등장 연출(Preparing → Descending → Impact)부터 다시 돈다.**
- 그래서 보스 쪽 새 코드는 "치우고 Idle" 뿐이다. 체력·패턴·FSM·등장 연출은 새 인스턴스라 자동으로 처음부터.

## 4. 호출 순서 (`PartyWipeRespawner.TryRespawnServer`)

```
0) 판정·지점 탐색(실패면 아무것도 안 바꾸고 결과 화면) — IsBossDefeatedServer 가 true 면 리스폰 안 함
1) BossEncounterDirector.ResetForRetryServer()   ← false 면 중단(결과 화면)
2) BossTeleportManager.ResetForRetryServer()
3) BossTimerManager.StopForRetryServer()
4) (튜토리얼 되감기 · 공용 목숨 5 · 플레이어 Alive·체력 가득·복도4 이동 — 은희 영역)
```

- 1) 이 잠금을 먼저 풀어야 4) 의 플레이어 리스폰(`TryRespawnAfterWipeOnServer` — 연출 잠금 중 거부)이 된다.
- 타이머: 재가동 안 함 — 리스폰 지점이 패드 바로 앞이라 시간 압박이 무의미하고, 재가동하면 리스폰 직후 특성 재선택(3단계) 중 강제 이동이 터질 수 있다.
  대안(원하면): `Idle` 복귀 API + `StartTimerServer()` — 지금은 없다.

## 5. 남은 것 · 확인 부탁

- **보스방 잔여물**: 이미 던져진 폭탄(`BossBomb`)은 5초 퓨즈로 터지고 사라진다(플레이어는 보스방 밖). 다른 씬 상주 잔여물(장판 풀·예고 장판)이 남으면 알려 주면 그 정리를 `ResetForRetryServer` 에 넣겠다.
- 송전탑은 씬 상주라 `EndCharge()` 로 내린다(`BossChargeSequence.Cancel` 과 같은 호출).
- 웰즈(`BossWells`)는 보스 자식이라 보스와 함께 사라졌다 새로 생긴다.
- Play 확인(사람): 보스전 전멸 → 등장 연출 다시 · 보스 체력 가득 · 패드 다시 동작 · MPPM 클라 화면에서도 보스·장판·잡기가 남지 않음.
