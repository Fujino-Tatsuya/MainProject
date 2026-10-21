# 플레이어 스탯 종합 (할 일 83)

> 코드: `Assets/1.Scripts/Player/Stats/` — `PlayerStatFormula`(순수 공식·`PlayerStatKind`·`PlayerStatReport`) · `PlayerStats`(종합) · `IPlayerStatSource`(영구 출처).
> 테스트: `Assets/Tests/EditMode/PlayerStats/Editor/PlayerStatFormulaTests.cs`, `SkillTooltipFormatterTests`.

## 1. 왜

클라에서 스킬 툴팁 `{dmg}` 가 틀렸다. `Player` 가 `Initialize` 를 서버에서만 불러 `Unit._attackDamage` 가 클라에선 0 이었다.
스탯은 곧 런타임에 바뀐다(특성·성장). 그래서 기본값을 복제하는 대신, **서버와 클라가 같은 클래스로 같은 입력을 종합**한다.

## 2. 공식

```
최종 스탯   = 기본 × (1 + Σ기본%) × (1 + Σ전체%)          (공격력·방어력·최대 체력은 반올림), ≥ 0
피해 스냅샷 = round( (round(최종 공격력 × 계수) + 고정값) × (1 + Σ가하는피해%) ), ≥ 0
```

- 같은 단계 안의 % 는 **더한다**. 단계끼리는 곱한다.
- 차지·과열 단계 배율 같은 기존 후처리는 스냅샷 **뒤**에 곱한다(거너 Q·R·기본 공격).
- 가하는 피해% 가 0 이면 이전 판정식 `max(0, round(공격력 × 계수) + 고정값)` 과 같다.

예) 공격력 100, 기본 +10%·+20%, 전체 +50% → 195. 계수 1.8, 가하는 피해 +20% → 421.

## 3. 스탯 표

| `PlayerStatKind` | 기본값(피어마다 로컬) | 기본% 상태이상 | 전체% 상태이상 | 쓰는 곳 |
|---|---|---|---|---|
| AttackDamage | `Player.attackDamage` | `BaseAttackDamageModifier` | `AttackDamageModifier` | 데미지 스냅샷(`Player.ResolveDamage`)·툴팁 |
| MoveSpeed | `PlayerMovement.BaseMaxSpeed` | `MoveSpeedBaseModifier` | `MoveSpeedModifier` | `PlayerMovement`(서버·오너 이동) — `Stats.Multiplier(MoveSpeed)` |
| AttackSpeed | `Player.attackSpeed` | `AttackSpeedBaseModifier` | `AttackSpeedModifier` | 값만 있음 — 플레이어 소비처 없음(후속) |
| Defense | `Player.defense` | `DefenseBaseModifier` | `DefenseModifier` | 피해 경감 `Unit.ApplyMitigatedHealthDamage`(`FinalDefense`) |
| MaxHp | `Player.maxHp` | `MaxHpBaseModifier` | `MaxHpModifier` | `FinalMaxHp` 소비처(HUD·재생·추락 피해·피격 상한) |

- 가하는 피해%: `DealtDamageModifier`(공격력 전용 3단계).
- 기본값은 xlsx Player 시트 값이고 빌드에 들어간다. 같은 빌드면 모든 피어가 같은 값을 갖는다.
- `Unit.Final*` 5개는 virtual 이고, `Player` 가 이 종합값으로 덮어쓴다. 몬스터는 기존 식(원본 × 곱 배율)을 그대로 쓴다.
- **새 스탯 추가 절차:**
  1. `PlayerStatKind` 에 한 줄 추가
  2. `PlayerStatFormula.StatusTypes` 에 한 줄 추가
  3. 상태이상 두 타입을 정한다
  4. `Player.BaseStatValue` 에 기본값을 연결한다
  5. `PlayerStatReport` 필드를 추가한다
  6. `UiStrings` 이름과 `StatusEffectCategories` 마스크에 추가한다

## 4. 입력

- 상태이상 magnitude 는 배율로 저장된다. 1.2 = +20%, 0.8 = −20%. 스택은 % × 스택 수다.
  `StatusEffectController.GetStatPercentSum` 이 계산한다. 몬스터·`IStatusEffectFacade` 는 여전히 `GetStatMultiplier` 의 곱을 쓴다.
- **영구 출처(특성·성장)는 "무엇을 얻었는지"(id)만 복제한다.** 수치는 각 피어가 같은 SO 에서 읽는다.
  `IPlayerStatSource` 를 구현해 `player.Stats.Register(source)` 로 등록한다. 기여가 바뀌면 `Changed` 를 낸다.
- 튜토리얼 특성은 지금 영구 상태이상 `AttackDamageModifier`·`MoveSpeedModifier`(전체%)로 들어온다.
- `Unit.Change*Value(Rpc)` 와 `Initialize` 의 Unit 필드는 플레이어 스탯에 쓰이지 않는다(몬스터 전용). 런타임 변경은 위 출처로 넣는다.
  예외: 체력 상한은 여전히 `Health.MaxHp`(= `Player.maxHp`)다. 최대 체력 % 가 상한을 올리지는 않는다(기존 동작).

## 5. 권한·검증

- **판정은 서버 값이다.**
  - 데미지 스냅샷·방어 경감은 서버에서 계산한다.
  - 이동은 원래 오너 권한이다. 이동속도는 서버와 오너가 같은 종합 배율을 쓴다.
- 툴팁은 같은 식 `SkillTooltipDamage.FromLinear / FromScaledSnapshot(…, dealtDamagePercent)` 를 쓴다.
  Shift 계산식에는 계수·고정값만 보이고, 가하는 피해% 는 결과 숫자에만 들어간다.
- **서버 2차 검증.** 오너가 공격·스킬 시작 요청에 `PlayerStatReport` 를 싣는다. 내용은 5종 최종값과 가하는 피해%이고, 실수 값은 천분율이다. 서버는 자기 값과 비교한다.
  - 대상: `RequestUseSkillRpc`, Default `RequestStartAttackRpc`·`RequestQueueNextAttackRpc`, Assassin `RequestStartRpc`·`RequestQueueNextAttackRpc`, Gunner `RequestStartRpc`.
  - 다르면 `[PlayerStats] … 불일치` 경고만 남긴다. 판정은 서버 값이고 요청은 거절하지 않는다.
  - 서버 쪽 입력(상태이상 목록·출처)이 바뀐 뒤 `Player.statValidationGrace`(기본 0.5초) 안의 불일치는 복제 지연으로 보고 무시한다.
  - 경고가 계속 나면 피어마다 xlsx 기본값·특성 SO 가 같은지, 공식 밖에서 스탯을 바꾸는 코드가 있는지 본다.

## 6. 바뀐 동작 (2026-10-21)

- 같은 스탯의 상태이상이 겹치면 곱이 아니라 % 를 더한다.
  예) 이동 특성 1.2 + 거너 Q 감속 0.5 → 이전 0.6 → 이제 0.7.
- 방어력 상태이상이 처음으로 피해 경감에 들어간다. 지금은 거는 곳이 없다.
