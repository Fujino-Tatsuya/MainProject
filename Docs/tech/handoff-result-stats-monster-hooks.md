# Handoff — 결과 화면 통계용 몬스터 훅 (2026-10-07 · 은희 → **경석**)

> 브랜치 `feature/ResultStats`. 계획 원본 = [PLAN-result-stats.md](../../PLAN-result-stats.md).
> 이 프로젝트는 PR 을 쓰지 않으므로 경석 영역(몬스터) 변경을 이 문서로 공유한다.

## 요약
결과 화면에 플레이어별 **가한 데미지 · 처치 수 · 간파 성공 · 사망 횟수**를 보여 주려고, 서버 정적 이벤트
채널 `CombatStatsEvents`(`Assets/1.Scripts/Unit/CombatStatsEvents.cs`)를 새로 만들었다.
몬스터 파일에는 **이벤트 발행 1줄씩만** 들어갔고, **판정 로직(창 열림·소비·그로기·데미지)은 하나도 바뀌지 않았다.**
`MonsterDeathEvents` 선례처럼 몬스터 쪽에는 통계 의존성이 없다(구독자는 `Managers/SessionStatsTracker`).

## 바뀐 몬스터 파일 (각 1줄)

| 파일 | 위치 | 들어간 줄 |
|---|---|---|
| `Monster/Boss/TwentyThreeBoss.cs` | `ReceiveAttack` 의 `EnterCounterSuccess()` 바로 다음 | `CombatStatsEvents.RaiseServerCounterSucceeded(this, GetAttackerClientId(hitContext));` |
| `Monster/Boss/GauntletBot.cs` | `TakeDamage(AttackInfo)` 의 `Counter.TryConsumeInterrupt()` 성공 → `CounterSucceeded()` 직전 | `CombatStatsEvents.RaiseServerCounterSucceeded(this, DamageAttackerClientId);` |
| `Monster/Boss/SpinnerBot.cs` | 위와 같음 | 위와 같음 |
| `Monster/Boss/WallBot.cs` | 위와 같음 | 위와 같음 |

- `MonsterBase.cs` 는 **바뀌지 않았다.** 계획에는 "MonsterBase 에 공격자 clientId protected getter" 였는데,
  공격자 필드(`_damageAttackerClientId`)가 `Unit` 의 private 이라 getter 를 **`Unit` 에 protected 로** 뒀다
  (`MonsterBase` 파생 전부에서 그대로 보인다):
  - `protected ulong DamageAttackerClientId` — `Unit.ReceiveAttack` 이 `TakeDamage(AttackInfo)` 를 부르는 동안만 유효.
    그 밖(추락·비율·직접 피해)에서는 `ulong.MaxValue`(공격자 없음 → 집계 안 됨).
  - `protected static ulong GetAttackerClientId(AttackHitContext)` — `base.ReceiveAttack` 이 끝나 위 값이 되돌려진 뒤에
    쓴다(23호는 카운터 성공을 `base.ReceiveAttack` **뒤**에 판정하므로 이쪽).
- `Unit.ApplyHealthDamage` 는 체력 반영 뒤·사망 통지 전에 `CombatStatsEvents.RaiseServerDamageApplied` 를 낸다
  (실제 감소량 HP+실드, 막타 초과분 제외). 판정 무수정 — [floating-damage-design.md](floating-damage-design.md) §2-1.

## 불변인 것 (확인용)
- 카운터 창 열림·닫힘·만료, `TryConsumeInterrupt()` 의 소비 규칙, 그로기 진입, 23호 `counter` 조건 스냅샷,
  취약 넉백 분기, 데미지 계산 — 전부 그대로다. 추가된 줄은 성공이 **이미 확정된 뒤** 알리기만 한다.
- 일반몹 그로기 누적(`maxGroggyCount`)·창 밖 인터럽트·23호 송전기 전멸(S7) 그로기는 간파 성공으로 **세지 않는다**.

## 🔴 새 보스·중간보스 규칙
간파(카운터) 창을 쓰는 보스·중간보스를 새로 만들면, **성공이 확정되는 분기에서만** 같은 이벤트를 1줄 발행한다.

```csharp
// TakeDamage(AttackInfo) 안(중간보스 3종 선례)
if (!Counter.TryConsumeInterrupt()) return;
CombatStatsEvents.RaiseServerCounterSucceeded(this, DamageAttackerClientId);

// base.ReceiveAttack 뒤에 판정하는 경우(23호 선례)
CombatStatsEvents.RaiseServerCounterSucceeded(this, GetAttackerClientId(hitContext));
```

- 빠뜨리면 그 보스의 간파 성공이 결과 화면에서 0 으로 나온다(게임 동작은 멀쩡해서 눈치채기 어렵다).
- 실패·만료·다른 원인 그로기에서는 부르지 않는다. 서버에서만 부른다(이미 `IsServer` 가드 안이면 그대로).
- 표준 문서에도 반영: [boss-rebuild-standard.md](boss-rebuild-standard.md) §3.4 · §8 체크리스트.

## 데미지·처치 집계 규칙 (참고)
- 대상이 `MonsterBase` 파생일 때만 센다. `TrainingDummy`·`BossChargingPylon`·`ChargingObject`·`Player` 는
  `Unit` 직계라 자동으로 빠진다. **새 "때리지만 몬스터가 아닌" 오브젝트를 `MonsterBase` 파생으로 만들면 집계에 들어간다** —
  그런 경우엔 은희에게 알려 줄 것.
- 처치 = 체력을 0 으로 만든 피해의 공격자(막타) 1회. 공격자 없는 막타(환경 피해)는 누구의 처치도 아니다.
