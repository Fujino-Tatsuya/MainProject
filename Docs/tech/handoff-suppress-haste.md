# handoff — 제압 가속 버프(할 일 91) · 23호 보스 이벤트 추가

> 2026-10-22 · 은희 레인(A) → **경석 공유**. 계획: `PLAN-91-suppress-haste.md` §3.4 · §3.6.
> 보스 파일은 **이벤트 1개만** 추가했다. 다른 보스 동작은 그대로다.

## 1. 보스 쪽 변경 (`Assets/1.Scripts/Monster/Boss/TwentyThreeBoss.cs`)

| 무엇 | 내용 |
|---|---|
| 새 이벤트 | `public static event Action<TwentyThreeBoss, float> SuppressStarted` — (보스, 이번 제압 길이 초) |
| 내는 곳 | `EnterSuppress` 맨 끝, `StunForCounter(duration)` + 제압 진입 로그 **직후** 1회 |
| 안 내는 곳 | 필수 기믹 중 **예약만** 된 경우(`_pendingSuppress = true` 후 return). 예약이 소비될 때(`TryConsumePendingSuppress` → `EnterSuppress`) 낸다 |
| 길이 | `EnterSuppress` 가 이미 계산한 값 그대로 — `_suppressDurationOverride`(북진 압박 보상 등) > 0 이면 그 값, 아니면 `breakDuration` |
| 서버 전용 | `EnterSuppress` 가 `IsServer` 에서만 진행하므로 이벤트도 서버에서만 난다 |

- 정적인 이유: 구독자(`SuppressHaste`)가 보스 참조를 들고 있지 않다. 프로젝트의 `CombatStatsEvents` 와 같은 서버 정적 이벤트 방식.
- 🔴 이 줄을 옮기거나 지우면 제압 가속 버프가 안 걸린다. 제압 진입 경로를 새로 만들면 같은 이벤트를 내 달라.

## 2. 플레이어 쪽 (은희 영역 — 참고)

- `SuppressHaste.GrantServer(ulong sourceId, float suppressDuration)` — 보스 종류와 무관한 서버 API. 23호는 위 이벤트로 자동 호출.
  로비 훈련 상대(`TrainingSparringModel` "제압 중 쿨타임 가속" 보류 플래그)도 이것을 부르면 된다.
- 대상 = 그 순간 Alive 플레이어. 상태이상 3개(`CooldownRateModifier`·`MoveSpeedFinalMultiplier`·`AttackSpeedFinalMultiplier`), 출처 = 보스 `NetworkObjectId`, 스택 1(재진입 = 지속 갱신).
- 지속 = 시트 `durationOverride` > 0 이면 그 값, 0 이면 제압 길이. 만료는 상태이상 스윕 — 제압 종료 이벤트는 쓰지 않는다.
- Alive 를 벗어나면(쓰러짐 연출·Soul·사망) 그 출처 3개 제거, 부활해도 다시 안 건다.
- 규칙: `SuppressHasteRules`(EditMode `SuppressHasteRulesTests`). 수치: `Assets/Resources/Player/SuppressHasteData.asset` · xlsx `SuppressHaste` 시트.
