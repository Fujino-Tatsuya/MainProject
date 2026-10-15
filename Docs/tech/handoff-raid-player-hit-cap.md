# handoff — 레이드 보스전 플레이어 단일 피해 상한 (경석 → 은희, 2026-10-14)

## 무엇을 바꿨나
`Player.ReceiveAttack`(`Assets/1.Scripts/Player/Player.cs`) 맨 앞, `base.ReceiveAttack` 호출 직전에 **한 줄**:

```csharp
attackInfo.damage = BossRaidRules.CapHitDamage(attackInfo.damage, FinalMaxHp, PlayerHitDamageCap.Ratio);
```

- `PlayerHitDamageCap.Ratio` 는 평소 **0 = 상한 없음** → 기존 동작 그대로.
- 23호 **레이드 변형**(`TwentyThree_Raid` / `No23_Raid.asset` 의 `playerHitDamageCapRatio = 0.9`)이 서버에서 스폰될 때만 켜지고, 디스폰 때 꺼진다.
- 켜져 있으면 한 번의 피해가 플레이어 `FinalMaxHp × 0.9` 를 넘지 않는다 → **체력이 가득 찬 상태에서 한 방에 죽지 않는다.**

## 왜 플레이어 쪽인가
팀장 결정(10-14, [PLAN-boss-raid.md](../../PLAN-boss-raid.md)): "목숨 쓸 틈도 없이 한 방에 즉사하는 경험 금지". 보스 피해 생성부가 근접·잡기·점프·돌진·장판·드론·존으로 8곳 이상 흩어져 있어
수신부 한 곳이 가장 좁다. 보스방에서는 보스 계열만 피해를 주므로 출처는 구분하지 않는다.

## 영향 범위
- 쉴드·방어·무적·추락 Bypass 는 그 뒤(`base.ReceiveAttack` 이후) 그대로 — 상한은 원본 피해에만 걸린다.
- 추락 피해는 `ReceiveAttack` 을 안 거치면 영향 없음(확인 대상).
- 기존 23호·일반 몬스터 전투: Ratio 0 이라 변화 없음.

## 검증
- `BossRaidRulesTests`(전투 EditMode 러너) — `CapHitDamage` 상한·0 비활성.
- 팀장 Play(레이드 변형) 대기.
