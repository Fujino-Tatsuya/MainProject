# 플레이어 스탯 종합 (할 일 83)

> 코드: `Assets/1.Scripts/Player/Stats/` — `PlayerStatFormula`(순수 공식·`PlayerStatKind`·`PlayerStatReport`) · `PlayerStats`(종합) · `IPlayerStatSource`(영구 출처).
> 테스트: `Assets/Tests/EditMode/PlayerStats/Editor/PlayerStatFormulaTests.cs`, `SkillTooltipFormatterTests`.

## 1. 왜

클라에서 스킬 툴팁 `{dmg}` 가 틀렸다. `Player` 가 `Initialize` 를 서버에서만 불러 `Unit._attackDamage` 가 클라에선 0 이었다.
스탯은 곧 런타임에 바뀐다(특성·성장). 그래서 기본값을 복제하는 대신, **서버와 클라가 같은 클래스로 같은 입력을 종합**한다.

## 2. 공식

```
최종 스탯   = (기본 × (1 + Σ기본%) × (1 + Σ전체%)) × Π최종배율   (공격력·방어력·최대 체력은 반올림), ≥ 0
피해 스냅샷 = round( (round(최종 공격력 × 계수) + 고정값) × (1 + Σ가하는피해%) ), ≥ 0
```

- 같은 단계 안의 % 는 **더한다**. 단계끼리는 곱한다.
- **최종 배율**(할 일 91)은 % 단계 **뒤**에 곱하고, 같은 최종 배율끼리도 **곱한다**(2.0 × 1.5 = 3.0). 이동속도·공격속도만 갖는다.
  집계는 `StatusEffectController.GetStatMultiplier`(스택 = magnitude^스택), 없으면 1 — 기존 값은 그대로다.
  예) 이동속도 5, 기본 +10%, 전체 +20%, 최종 ×2 → 5 × 1.1 × 1.2 × 2 = 13.2.
- 차지·과열 단계 배율 같은 기존 후처리는 스냅샷 **뒤**에 곱한다(거너 Q·R·기본 공격).
- 가하는 피해% 가 0 이면 이전 판정식 `max(0, round(공격력 × 계수) + 고정값)` 과 같다.

예) 공격력 100, 기본 +10%·+20%, 전체 +50% → 195. 계수 1.8, 가하는 피해 +20% → 421.

## 3. 스탯 표

| `PlayerStatKind` | 기본값(피어마다 로컬) | 기본% 상태이상 | 전체% 상태이상 | 최종 배율 상태이상 | 쓰는 곳 |
|---|---|---|---|---|---|
| AttackDamage | `Player.attackDamage` | `BaseAttackDamageModifier` | `AttackDamageModifier` | — | 데미지 스냅샷(`Player.ResolveDamage`)·툴팁 |
| MoveSpeed | `PlayerMovement.BaseMaxSpeed` | `MoveSpeedBaseModifier` | `MoveSpeedModifier` | `MoveSpeedFinalMultiplier` | `PlayerMovement`(서버·오너 이동) — `Stats.Multiplier(MoveSpeed)`(최종 배율 포함) |
| AttackSpeed | `Player.attackSpeed` | `AttackSpeedBaseModifier` | `AttackSpeedModifier` | `AttackSpeedFinalMultiplier` | 평타·스킬 재생 속도 — `Player.AttackSpeedRate` = `Stats.Multiplier(AttackSpeed)`(최종 ÷ 기본, 최종 배율 포함). §3.1 |
| Defense | `Player.defense` | `DefenseBaseModifier` | `DefenseModifier` | — | 피해 경감 `Unit.ApplyMitigatedHealthDamage`(`FinalDefense`) |
| MaxHp | `Player.maxHp` | `MaxHpBaseModifier` | `MaxHpModifier` | — | `FinalMaxHp` 소비처(HUD·재생·추락 피해·피격 상한) |

- 가하는 피해%: `DealtDamageModifier`(공격력 전용 3단계).
- 최종 배율 매핑은 `PlayerStatFormula.FinalMultiplierType`. 셋 다(`CooldownRateModifier` 포함) `StatusEffectCategories` 스탯 마스크라 배율 ≥ 1 = 버프 → 연출 일괄 해제(`ClearDebuffsServer`)가 지우지 않는다.
- `CooldownRateModifier`(쿨타임 줄어드는 속도 배율)는 스탯이 아니다 — 타입·분류만 있고 소비처(쿨 시계)는 할 일 91 T2~T4.
- 기본값은 xlsx Player 시트 값이고 빌드에 들어간다. 같은 빌드면 모든 피어가 같은 값을 갖는다.
- `Unit.Final*` 5개는 virtual 이고, `Player` 가 이 종합값으로 덮어쓴다. 몬스터는 기존 식(원본 × 곱 배율)을 그대로 쓴다.
- **새 스탯 추가 절차:**
  1. `PlayerStatKind` 에 한 줄 추가
  2. `PlayerStatFormula.StatusTypes` 에 한 줄 추가
  3. 상태이상 두 타입을 정한다
  4. `Player.BaseStatValue` 에 기본값을 연결한다
  5. `PlayerStatReport` 필드를 추가한다
  6. `UiStrings` 이름과 `StatusEffectCategories` 마스크에 추가한다

### 3.1 공격속도 소비 (할 일 91 T5)

규칙 한 곳: `PlayerAttackSpeedRules`(`Assets/1.Scripts/Player/Stats/`). 배율은 복제된 상태이상으로 각 피어가 계산한다 — 서버·오너·관찰자 재생 속도가 같다.
배율 하한 0.1(감속 디버프가 와도 멈추지 않게).

| 동작 | 연결 | 타이밍 기준 |
|---|---|---|
| 가붕이 콤보(`DefaultAttackController`) | 상태 파라미터 `AttackSpeed` = 데이터 공속 × 배율 | 클립 이벤트(Hit·ComboWindow·End). 종료 안전망·전진 속도도 같은 재생 시간을 쓴다 |
| 어쌔신 평타(`AssassinBasicAttack`) | 공격 중 `Animator.speed` = 배율 | 클립 이벤트. 종료 안전망 = 클립 길이(감속이면 ÷ 배율), 강타 전진 시간 ÷ 배율 |
| 거너 평타(`GunnerBasicAttack`) | 데이터 시간(준비·발사 간격·후속 동작·입력 창·발 잠금) ÷ 배율 + 공격 중 `Animator.speed` = 배율 | 데이터 시간 값. `FireSpeed` 는 배율 전 간격 기준(두 번 곱하지 않음) |
| 공속을 타는 스킬 | 실행 중 `Animator.speed` = 배율(`PlayerSkillController`) | 클립 이벤트. 임시 안전망 `maxActiveDuration`(감속이면 ÷ 배율), 클립 안전망은 `Animator.speed` 포함(`PlayerSkillSafetyNet.Probe`) |

- 시작 때 한 번 정하는 서버 시간 안전망은 `SafetyNetDuration` — **감속이면 늘리고 가속이면 줄이지 않는다.** 배율은 도중에 바뀌므로(가속 버프가 스킬 중간에 끝남)
  줄여 두면 느려진 나머지 재생보다 먼저 끊는다. 가속 중 정상 종료는 클립 이벤트가 먼저 낸다. 가붕이 콤보는 타마다 파라미터를 한 번 정해 그 타 안에서는 안 바뀌므로 재생 시간 그대로다.

- `Animator.speed` 쓰기는 `PlayerAttackSpeedAnimator` 한 곳뿐이다(`Player.Awake` 가 붙인다). 창구 `IPlayerAttackSpeedAnimatorUser` 중 하나라도 참이면 배율, 아니면 1.
- 스킬이 타는지 = `PlayerSkillBase.ScalesWithAttackSpeed`(virtual) — 기본 이벤트 방식(`EndsOnClipEvent`). 가붕이 E 는 클립 이벤트로만 돌아 참으로, 어쌔신 Q 돌진은 거짓으로 덮는다.
  - 탄다: 가붕이 E·R·우클릭 간파, 거너 우클릭 간파, 어쌔신 E(원형 베기·강화)·R 변신·우클릭 간파.
- **타지 않는 것(고정 타이밍):** 대시 지속, 타이머 방식 스킬(가붕이 Q 홀드, 거너 Q 충전·E 백스텝·R 추적 레이저), 채널링·홀드 틱 간격,
  어쌔신 Q 돌진, 스킬 안의 보호·연출 지속(`maxActiveDuration` 로 거는 슈퍼아머·무적·어쌔신 원형 베기 연출 — 무적·슈퍼아머는 스킬 종료 때 걷힌다).
- 전진 거리는 배율과 무관하게 같다 — 거리 ÷ 실제 재생 시간으로 속도만 바뀐다(가붕이 ScriptedForwardDistance·루트모션, 어쌔신 강타 전진).

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
- **서버 2차 검증.** 오너가 공격·스킬 시작 요청에 `PlayerStatReport` 를 싣는다. 내용은 5종 최종값(최종 배율까지 곱한 값)과 가하는 피해%이고, 실수 값은 천분율이다. 서버는 자기 값과 비교한다.
  - 대상: `RequestUseSkillRpc`, Default `RequestStartAttackRpc`·`RequestQueueNextAttackRpc`, Assassin `RequestStartRpc`·`RequestQueueNextAttackRpc`, Gunner `RequestStartRpc`.
  - 다르면 `[PlayerStats] … 불일치` 경고만 남긴다. 판정은 서버 값이고 요청은 거절하지 않는다.
  - 서버 쪽 입력(상태이상 목록·출처)이 바뀐 뒤 `Player.statValidationGrace`(기본 0.5초) 안의 불일치는 복제 지연으로 보고 무시한다.
  - 경고가 계속 나면 피어마다 xlsx 기본값·특성 SO 가 같은지, 공식 밖에서 스탯을 바꾸는 코드가 있는지 본다.

## 6. 바뀐 동작 (2026-10-21)

- 같은 스탯의 상태이상이 겹치면 곱이 아니라 % 를 더한다.
  예) 이동 특성 1.2 + 거너 Q 감속 0.5 → 이전 0.6 → 이제 0.7.
- 방어력 상태이상이 처음으로 피해 경감에 들어간다. 지금은 거는 곳이 없다.

## 7. 바뀐 동작 (2026-10-22, 할 일 91)

- 최종 배율 단계 추가: `최종 = (기본 × (1+Σ기본%) × (1+Σ전체%)) × Π최종배율`. 이동속도·공격속도만, 같은 최종 배율끼리는 곱.
  예) 이동 특성 1.2(전체%) + 제압 가속 ×2(최종) → 1.2 × 2 = 2.4.
- 새 상태이상 3종 `MoveSpeedFinalMultiplier`·`AttackSpeedFinalMultiplier`·`CooldownRateModifier` — 아직 거는 곳이 없어 기존 수치는 그대로다(제압 가속 버프가 T6 에서 건다).
- T5: 공격속도가 처음으로 평타·스킬 재생 속도에 들어간다(§3.1). 지금 공속을 바꾸는 상태이상·특성이 없어 배율 1 — 기존 동작 그대로다.
- 이 배율·타입을 거는 제압 가속 버프 전체(발동·대상·지속·데이터·비네팅)는 [suppress-haste.md](suppress-haste.md).
