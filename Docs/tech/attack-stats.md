# 플레이어 공격 스탯 종합 (할 일 83)

> 코드: `Assets/1.Scripts/Player/Stats/` — `AttackStatFormula`(순수 공식) · `PlayerAttackStats`(종합) · `IAttackStatSource`(영구 출처).
> 테스트: `Assets/Tests/EditMode/AttackStats/Editor/AttackStatFormulaTests.cs`, `SkillTooltipFormatterTests`.

## 1. 왜

클라에서 스킬 툴팁 `{dmg}` 가 틀렸다. `Player` 가 `Initialize` 를 서버에서만 불러 `Unit._attackDamage` 가 클라에선 0 이었기 때문이다.
공격력은 곧 런타임에 바뀐다(특성·성장). 그래서 기본값을 복제하는 대신, **서버와 클라가 같은 클래스로 같은 입력을 종합**한다.

## 2. 공식

```
최종 공격력 = round( 기본 × (1 + Σ기본공격력%) × (1 + Σ전체공격력%) )            ≥ 0
피해 스냅샷 = round( (round(최종 공격력 × 계수) + 고정값) × (1 + Σ가하는피해%) )    ≥ 0
```

- 같은 단계 안의 % 는 **더한다**. 단계끼리는 곱한다.
- 차지·과열 단계 배율 같은 기존 후처리는 스냅샷 **뒤**에 곱한다(거너 Q·R·기본 공격).
- 가하는 피해% 가 0 이면 이전 판정식 `max(0, round(공격력 × 계수) + 고정값)` 과 같다.

예) 기본 100, 기본 +10%·+20%, 전체 +50% → 195. 계수 1.8, 가하는 피해 +20% → 421.

## 3. 입력

| 입력 | 출처 | 피어 간 동기화 |
|---|---|---|
| 기본 공격력 | `Player.attackDamage`(xlsx Player 시트 → 빌드 포함) | 같은 빌드면 같은 값(로컬) |
| 기본 공격력 % | 상태이상 `BaseAttackDamageModifier` + `IAttackStatSource.BaseAttackPercent` | 상태이상 = `NetworkList` 복제 |
| 전체 공격력 % | 상태이상 `AttackDamageModifier` + `IAttackStatSource.TotalAttackPercent` | 〃 |
| 가하는 피해 % | 상태이상 `DealtDamageModifier` + `IAttackStatSource.DealtDamagePercent` | 〃 |

- 상태이상 magnitude 는 배율로 저장된다. 1.2 = +20%, 0.8 = −20%. 스택은 % × 스택 수다.
  다른 스탯(이동속도 등)은 여전히 `GetStatMultiplier` 의 곱(스택 = 거듭제곱)을 쓴다.
- **영구 출처(특성·성장)는 "무엇을 얻었는지"(id)만 복제한다.** 수치는 각 피어가 같은 SO 에서 읽는다.
  구현체는 `IAttackStatSource` 를 구현하고 `player.AttackStats.Register(source)` 로 등록한다. 기여가 바뀌면 `Changed` 를 낸다.
- 튜토리얼 공격력 특성은 지금 영구 상태이상 `AttackDamageModifier`(전체 공격력)로 들어온다.
- `Unit.ChangeAttackDamageValue(Rpc)` 와 `Unit._attackDamage` 는 플레이어 판정에 쓰이지 않는다(몬스터 전용).
  런타임 공격력 변경은 위 출처로 넣을 것.

## 4. 권한·검증

- **판정은 서버 값이다.** 데미지 스냅샷은 서버에서 `player.ResolveDamage(계수, 고정값)` 로 만든다.
  해당 지점: 스킬(`PlayerSkillController`), 기본 공격(Default·Gunner·Assassin), 팔라딘 패시브.
- 툴팁은 같은 식 `SkillTooltipDamage.FromLinear / FromScaledSnapshot(…, dealtDamagePercent)` 를 쓴다.
  Shift 계산식에는 계수·고정값만 보이고, 가하는 피해% 는 결과 숫자에만 들어간다.
- **서버 2차 검증.** 오너가 공격·스킬 시작 요청에 `AttackStatReport`(최종 공격력, 가하는 피해 천분율)를 싣는다. 서버는 자기 값과 비교한다.
  - 대상: `RequestUseSkillRpc`, Default `RequestStartAttackRpc`·`RequestQueueNextAttackRpc`, Assassin `RequestStartRpc`·`RequestQueueNextAttackRpc`, Gunner `RequestStartRpc`.
  - 다르면 `[AttackStats] … 불일치` 경고만 남긴다. 판정은 서버 값이고 요청은 거절하지 않는다.
  - 서버 쪽 입력(상태이상 목록·출처)이 바뀐 뒤 `Player.attackStatValidationGrace`(기본 0.5초) 안의 불일치는 복제 지연으로 보고 무시한다.
  - 경고가 계속 나면 피어마다 xlsx 기본값·특성 SO 가 같은지, 공식 밖에서 공격력을 바꾸는 코드가 있는지 본다.
