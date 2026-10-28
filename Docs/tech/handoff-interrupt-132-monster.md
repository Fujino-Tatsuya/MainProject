# 공유 — 간파 시도 예측용 판정 창 복제·예측 대상 캐시·맞은 쪽 섬광 꺼짐 (할 일 132, 경석)

> 작성: 은희 레인(claude-alt), 2026-10-28. 브랜치 `feature/InterruptPresentation2`(커밋 단계2 `0bda2b64` · 정정1 `900af062` · 단계6).
> 설계 원본: [interrupt-presentation.md §0](interrupt-presentation.md#0-132-체계--예측-3상태강약오너-암전빛-모임적중-섬광-할-일-132). PR 없음 규칙에 따라 이 문서로 공유한다.

## 1. 한 줄 요약

간파 연출이 **간파 입력 순간 성공 예측**(대상 없음/약/강)으로 갈린다. 서버는 기존 창으로 예측하지만, **오너 클라도 바로 예측**해야 해서
몬스터 쪽에 **판정 창 복제값 1개**와 **예측 대상 캐시 등록/해제**를 넣었다. **판정·피해·카운터 창 개폐·그로기·취약·통계는 바뀌지 않았다.**

## 2. 경석 파일 변경

| 파일 | 변경 |
|---|---|
| `Assets/1.Scripts/Monster/MonsterBase.cs` | `NetworkVariable<bool> _counterJudgeWindow`(서버 쓰기, 모두 읽기) 추가 — 서버 `MonsterCounterWindow` 개폐를 그대로 옮긴 **판정** 창(표시 `_counterVisual` 과 달리 0.15초 조기 소등 없음). 창이 없는 몹은 늘 false(전송 없음). `ServerReplicateCounterWindow(bool)`(internal, 서버·스폰일 때만) · `CounterJudgeWindowReplica`(protected 읽기) · 스폰 때 false 로 리셋(풀 재사용) · 값 변경 → `InterruptWindowReplicaTracker.Record`(클라 창 기록) |
| 〃 | `OnNetworkSpawn` 끝: `InterruptTargetRegistry.Register(this)`(이미 Dead 로 합류하면 안 함) · `OnNetworkDespawn`: `Unregister` + 트래커 `Forget` · `OnStateChanged`: Dead 진입 = `Unregister`, Dead 에서 복귀 = `Register` — **모든 피어** |
| `Assets/1.Scripts/Monster/MonsterCounterWindow.cs` | 개폐 기록 4곳을 `RecordTransition(open)` 하나로 — 기존 `InterruptLagMonitor.RecordWindow` 그대로 + `MonsterBase.ServerReplicateCounterWindow(open)`. 같은 틱 '다시 열기'는 복제로 '열림 유지'로 보인다 |
| `Assets/1.Scripts/Monster/Boss/TwentyThreeBoss.cs` | 클래스에 `IInterruptWindowReplica` — `ReplicatedWindowOpen` = 기존 `_counterWindow.Value`(새 복제 없음) · `InterruptFrontAngle` = `IsCounterFromFront` 와 같은 값(정면 판정 끔 = 180, 데이터 없음 = 60) · `_counterWindow.OnValueChanged` 구독/해제 → 클라 창 기록 |
| `Assets/1.Scripts/Monster/Boss/GauntletBot.cs` · `SpinnerBot.cs` · `WallBot.cs` | 클래스에 `IInterruptWindowReplica` — `ReplicatedWindowOpen` = `CounterJudgeWindowReplica`(정면 판정 없음 = 기본 180) |

- 등록 대상 = `IInterruptSlowMotionTarget` 를 가진 Unit 만(그 외 몹은 `Register` 가 무시). 23호는 보스전 시작 Spawn 이 곧 등록이다(MonsterBase 경로).
- 대역폭: 중간보스·23호 외 몬스터는 값이 늘 false 라 초기 동기화 외 전송 없음. 23호는 새 NetworkVariable 없음.
- 은희 영역도 같은 변경: `TrainingSparringBot`(MonsterBase + 정면 허용각 = `counterRules`), `TrainingDummyInterrupt`(기존 `_state`).

## 3. 불변 (확인한 것)

- 서버 판정(`ResolveHit` 오버랩·`ServerIsInterruptible`·창 소비·그로기·취약·`CombatStatsEvents`) — 예측 캐시는 판정에 쓰이지 않는다.
- 카운터 창 길이·개폐 시점·텔레그래프·`_counterVisual` 조기 소등.
- 섬광 RPC 발신 지점·속성.

## 4. 보이는 변화 — 맞은 쪽 섬광(E1) 꺼짐

132 단계 6 에서 디렉터 스위치를 정리했다: **E1 `targetFlash` = 꺼짐**(새 적중 섬광 — 적중 지점 월드 섬광 `FX_InterruptHitFlash` — 이 대신한다).
23호·건틀릿봇·스피너봇·월봇의 `Interrupt_Flash` 는 RPC 는 그대로 날아가지만 재생되지 않는다. 다시 켜려면 `NetworkManager.prefab › InterruptProductionDirector › targetFlash`.
빈 칸 경고(`WarnNoInterruptFlashOnce`)는 그대로다.

## 5. 새 간파 대상(보스·엘리트)을 추가할 때

1. `MonsterBase` 상속 + `IInterruptSlowMotionTarget` 이면 캐시 등록·해제는 자동이다.
2. `IInterruptWindowReplica` 구현 — `ReplicatedWindowOpen` 은 `MonsterCounterWindow` 를 쓰면 `CounterJudgeWindowReplica`, 자체 창이면 그 복제값(+ 값 바뀔 때 `InterruptWindowReplicaTracker.Record(gameObject, open)`).
   정면 판정이 있으면 `InterruptFrontAngle` 에 판정과 **같은 데이터 값**(새 칸 금지).
3. 구현하지 않으면 오너 예측은 '창 닫힘'(= 약)으로만 보고, 서버 예측은 그대로 동작한다.

## 6. 확인 부탁 (Play — 사람)

- 23호 보스전: 창 열린 뒤 정면에서 우클릭 = 강(오너 암전·빛 모임 → 성공 섬광), 등 뒤 = 대상 없음, 창 닫힘 = 약.
- 중간보스 3종: 창 열림/닫힘에서 강/약이 갈리는지, 처치 뒤 그 자리에서 우클릭해도 연출이 안 나는지(캐시 해제).
