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
- `CooldownRateModifier`(쿨타임 줄어드는 속도 배율)는 스탯이 아니다(`PlayerStatKind`·`PlayerStatReport` 에 없음). 소비처 = 쿨 시계(할 일 91 T8):
  `Player.CooldownRate` = `StatusEffectController.GetStatMultiplier(CooldownRateModifier)`(최종 배율과 같은 곱 집계, 없으면 1) → `Player.Awake` 가 `PlayerCooldownClock.SetRateSource` 에 연결.
  스킬 쿨(우클릭·Q·E·궁극기)·대시 충전·가붕이 패시브 내부 쿨이 이 배율로 줄어든다. 피어마다 복제본으로 계산하고 시계는 동기화하지 않는다 — [suppress-haste.md](suppress-haste.md) §1.1.
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
| 가붕이 콤보(`DefaultAttackController`) | 상태 파라미터 `AttackSpeed` = 데이터 공속 × 배율 | 클립 이벤트(Hit·ComboWindow·End). 종료 안전망·전진 속도도 같은 재생 시간을 쓴다. 4타 → 1타 되감기 진입(`loopBackEntryTime`, 클립 시각)은 `CrossFadeInFixedTime` 오프셋이 상태 속도에 곱해지므로 ÷ `AttackSpeed` 로 넘긴다(`DefaultAttackChainRules.LoopBackFixedTimeOffset` — 안 나누면 제압 가속 때 1타 창 열림을 건너뛰어 홀드 순환이 끊겼다, 할 일 122) |
| 어쌔신 평타(`AssassinBasicAttack`) | 공격 중 `Animator.speed` = 배율 | 클립 이벤트. 종료 안전망 = 클립 길이(감속이면 ÷ 배율), 강타 전진 시간 ÷ 배율 |
| 거너 평타(`GunnerBasicAttack`) | 데이터 시간(준비·발사 간격·후속 동작·입력 창·발 잠금) ÷ 배율 + 공격 중 `Animator.speed` = 배율 | 데이터 시간 값. `FireSpeed` 는 배율 전 간격 기준(두 번 곱하지 않음) |
| 공속을 타는 스킬 | 실행 중 `Animator.speed` = 배율(`PlayerSkillController`) | 클립 이벤트. 임시 안전망 `maxActiveDuration`(감속이면 ÷ 배율), 클립 안전망은 `Animator.speed` 포함(`PlayerSkillSafetyNet.Probe`) |
| 가붕이 E(`FirstMeleeSubSkill`) | `Animator.speed` 아님 — 준비·해제·반격 상태의 속도 파라미터 `AttackSpeedRate` = 배율. **방어중(`SubSkillGuardIdle`) 제외** | 클립 이벤트. 방어중 길이는 공속과 무관. 안전망 `maxActiveDuration`(감속이면 ÷ 배율) — [suppress-haste.md](suppress-haste.md) §3 |

- 시작 때 한 번 정하는 서버 시간 안전망은 `SafetyNetDuration` — **감속이면 늘리고 가속이면 줄이지 않는다.** 배율은 도중에 바뀌므로(가속 버프가 스킬 중간에 끝남)
  줄여 두면 느려진 나머지 재생보다 먼저 끊는다. 가속 중 정상 종료는 클립 이벤트가 먼저 낸다. 가붕이 콤보는 타마다 파라미터를 한 번 정해 그 타 안에서는 안 바뀌므로 재생 시간 그대로다.

- `Animator.speed` 쓰기는 `PlayerAttackSpeedAnimator` 한 곳뿐이다(`Player.Awake` 가 붙인다). 창구 `IPlayerAttackSpeedAnimatorUser` 중 하나라도 참이면 배율, 아니면 1.
  같은 컴포넌트가 상태 속도 파라미터 `AttackSpeedRate`(float, 기본 1)도 전 피어에서 항상 배율로 쓴다 — 파라미터가 없는 컨트롤러(거너·어쌔신)는 건너뛴다.
  일부 상태만 공속을 타야 하는 동작은 그 상태의 Speed Multiplier 를 이 파라미터로 켠다(지금은 가붕이 E 준비·해제·반격뿐).
- 스킬이 `Animator.speed` 로 타는지 = `PlayerSkillBase.ScalesWithAttackSpeed`(virtual) — 기본 이벤트 방식(`EndsOnClipEvent`). 어쌔신 Q 돌진은 거짓으로 덮는다.
  가붕이 E 는 거짓 — 상태 파라미터로 준비·해제·반격만 탄다(할 일 91 A5). 임시 안전망을 배율로 늘리는지는 `ScalesSafetyNetWithAttackSpeed`(기본 = `ScalesWithAttackSpeed`, 가붕이 E 는 참).
  - `Animator.speed` 로 탄다: 가붕이 R·우클릭 간파, 거너 우클릭 간파, 어쌔신 E(원형 베기·강화)·R 변신·우클릭 간파.
  - 상태 파라미터로 탄다: 가붕이 E 준비·해제·반격(방어중 제외).
- **타지 않는 것(고정 타이밍):** 대시 지속, 타이머 방식 스킬(가붕이 Q 홀드, 거너 Q 충전·E 백스텝·R 추적 레이저), 채널링·홀드 틱 간격,
  어쌔신 Q 돌진, 스킬 안의 보호·연출 지속(`maxActiveDuration` 로 거는 슈퍼아머·무적·어쌔신 원형 베기 연출 — 무적·슈퍼아머는 스킬 종료 때 걷힌다).
- 전진 거리는 배율과 무관하게 같다 — 거리 ÷ 실제 재생 시간으로 속도만 바뀐다(가붕이 ScriptedForwardDistance·루트모션, 어쌔신 강타 전진).

## 4. 입력

- 상태이상 magnitude 는 배율로 저장된다. 1.2 = +20%, 0.8 = −20%. 스택은 % × 스택 수다.
  `StatusEffectController.GetStatPercentSum` 이 계산한다. 몬스터·`IStatusEffectFacade` 는 여전히 `GetStatMultiplier` 의 곱을 쓴다.
- **영구 출처(특성·성장)는 "무엇을 얻었는지"(id)만 복제한다.** 수치는 각 피어가 같은 SO 에서 읽는다.
  `IPlayerStatSource` 를 구현해 `player.Stats.Register(source)` 로 등록한다. 기여가 바뀌면 `Changed` 를 낸다.
- **특성 출처 `PlayerTraitModifiers`(할 일 125 2/5)** — 상태이상을 쓰지 않는 영구 출처. `Player.prefab`(base)에 붙고, 없으면 `Player.Awake` 가 붙여 `Stats.Register` 한다.
  복제는 서버가 고른 특성 해시 목록뿐이다(`TraitDirector` 획득 행 그대로 — 플레이어 단위 복제 없음). 서버·클라가 같은 SO 를 같은 순수 함수 `TraitModifierSet`(`Assets/1.Scripts/Traits/TraitModifiers.cs`)으로 합성한다.
  그래서 보스방 입장 디버프 정리(`ClearDebuffsServer`)·연출 잠금과 무관하고, **1 미만 배율(페널티)도 그대로** 남는다(예전 상태이상 경로는 1 로 올렸다).
  특성 1장은 `ITraitModifierContributor.GetModifiers` 로 `TraitModifier` 를 기여한다. 질의 표면과 합성 규칙은 아래와 같다.
  - 스탯 5종 기본%·전체%·가하는 피해% → `IPlayerStatSource`. 같은 단계 % 는 **합**한다(§2). 옛 `TraitEffectKind` 는 3/5 에서 폐지 — 스탯 % 는 효과 클래스 `TraitStatEffect`, 공격 계열 배율은 `TraitDamageEffect`([tutorial-traits.md](tutorial-traits.md) §4.1).
  - 3/5 질의 추가: 범위 배율 `AreaMultiplier(key, axis)`·스킬 수치 덮어쓰기 `ResolveSkillValue(이름, 기본값)`·특성 스위치 `HasTrait(id)`. 회복 배율은 `PlayerTraitEffects.HealServer`(팔라딘 패시브)가 소비한다.
  - 공격 계열 피해 배율 `DamageMultiplier(TraitAttackKey)`. 키 = 기존 `AttackType`(평타 Default·Q/E/R·우클릭 Skill·가붕이 패시브 추가피해 None) × 형태 `TraitAttackForm`(Normal·Alternate = 대체 슬롯/변신·Enhanced = 강타).
    서로 다른 특성끼리 **곱**한다. 기준 피해 Dref 에 **한 번만** 곱한다 — `Player.ResolveDamage(계수, 고정값, key)` → `PlayerStatFormula.Snapshot(…, traitMultiplier)` = round(1차 스냅샷 × (1+가하는 피해%) × 특성 배율). 중간 정수화는 없다.
    소비처는 평타 3종, 가붕이 패시브, `PlayerSkillController` 스킬 승인 스냅샷이다. 차지·과열 후처리는 지금처럼 그 뒤에 곱한다.
  - 슬롯 쿨 배율 `CooldownMultiplier(slot, form)`(곱, 명세 §6)은 `PlayerSkillController.GetEffectiveCooldownTime` 이 쓴다. 이 값 = 데이터 쿨 × 배율이고, 쿨 시작 3곳·HUD 채움 전체에 들어간다.
    제압 가속은 여기 곱하지 않는다. 쿨 시계 속도(`CooldownRateModifier`)로 따로 곱해진다. 배율이 바뀌면 서버가 진행률을 보존하고(`PlayerSkillCooldownLedger.Rescale`: 남은/옛 전체×새 전체, 준비·전체 0 은 그대로) 오너에 미러한다.
  - 회복 배율 `HealMultiplier`(곱)는 질의만 있다. 소비처는 3/5(`HealHp` 실제 회복량)에서 붙인다.
  - 같은 해시는 한 번만 센다. 잘못된 값(NaN·무한·음수 배율·0 이하 쿨 배율)은 버리고 `[TraitModifiers]` 오류를 남긴다. 빈 목록이면 배율은 전부 1, % 는 전부 0 이다.
  - 획득 행이 바뀌면(획득·초기화·재합류·디렉터 교체) 다음 LateUpdate 에 다시 합성하고 `Changed` 를 낸다. 서버 검증 유예(§5)도 이때 다시 센다.
  - 테스트: `TraitModifierSetTests`(맵씬 메뉴)·`PlayerTraitCooldownRescaleTests`(플레이어 메뉴).
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
- T2~T4·T8: 쿨 속도 배율 소비처 = 쿨 시계(스킬·대시·가붕이 패시브). 배율 1 이면 기존 쿨과 같다 — [suppress-haste.md](suppress-haste.md) §1.1.
- 이 배율·타입을 거는 제압 가속 버프 전체(발동·대상·지속·데이터·비네팅)는 [suppress-haste.md](suppress-haste.md).

## 8. 바뀐 동작 (2026-10-28, 할 일 125 2/5)

- 특성 배율이 상태이상(출처 `0x54524149…`)에서 `PlayerTraitModifiers` 출처로 옮겨 왔다(§4). `TraitDirector` 는 이제 적중 쿨감 구독만 건다.
  값은 그대로다(전체% 단계). 다만 1 미만이 1 로 올라가지 않고, 보스방 디버프 정리에 지워지지 않는다.
- 피해 스냅샷에 특성 공격 계열 배율 칸이 생기고, 스킬 전체 쿨에 특성 슬롯 배율이 들어간다. 지금 풀에 정의가 없어 전부 1 이다 — 기존 수치는 그대로다.
- 툴팁 `{dmg}`·툴팁 쿨 초에는 아직 특성 배율을 넣지 않는다(5/5 포매터 연결에서). 서버 2차 검증 보고(`PlayerStatReport`)는 스탯 5종·가하는 피해%만 싣는다. 공격 계열 배율은 보고에 없다(같은 해시 목록 → 같은 값).
