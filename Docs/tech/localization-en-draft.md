# 로컬라이징 en 번역 초안 (AI 초안)

> 로컬라이징 L2~L4b 에서 코드가 등록하는 **모든 키의 en 초안**이다. 규칙·키 체계는 [localization.md](localization.md).
> 작성: 2026-10-12, 브랜치 `feature/LocalizationContent`. **Strings 100행**(L2 스킬 툴팁 48 · 캐릭터 이름 3 · UiStrings 49) · **ErrorCodes 35행**.

## 사용법

- 🔸 이 표는 **초안**이다 — 기획 검수 전, 문구 확정이 아니다.
- **xlsx 반영은 병합 뒤 메인이 한다**: `Tools/Data/Export Template` 으로 새 키 행이 시트에 붙은 다음, 이 표의 en 초안을 시트 `en` 칸에 옮긴다.
  🔴 **지금 `GameData.xlsx` 를 직접 고치지 않는다**(SVN 원본 — 병합 전 수정은 Export 와 엇갈린다).
- 키·ko 는 작성 시점의 실제 값이다 — Export 수집 코드(`DataTableTemplate.CollectTargets` → `DataTableTextKeys.Collect` + `StringKeyRegistry.Collect`, `StringKeyRegistry.CollectErrorCodes`)를 에디터에서 돌려 뽑았다.
  옮길 때 시트의 ko 가 이 표와 다르면(기획이 고쳤으면) en 도 다시 맞춘다.
- 표 안 문구는 `` ` `` 로 감쌌다 — 시트에는 감싸지 않은 안쪽 글자만 넣는다. 지금 줄바꿈이 있는 문구는 없다.

### 지킨 규칙 (L2 공용 — [localization.md §2 검증 규칙](localization.md#검증-규칙))

- ko·en 의 `{자리표시자}` 집합이 같다 — `{shieldDuration:0.0}` 처럼 형식까지 이름으로 그대로 옮겼다.
- `<style=…>` 짝·`<sprite …>` 태그를 그대로 뒀다. 줄바꿈 수가 같다.
- HUD 라벨은 짧게. 키보드 기호(`Q`·`E`·`R`·`[Shift]`)는 그대로 두고 메모에 **키 토큰 후보**([§8 조작 키 토큰](localization.md#조작-키-토큰--key액션-규칙만-적용은-해당-문구를-키화할-때))로 표시했다.
- 스킬 툴팁 설명 앞의 `TODO` 는 ko 에 있는 미확정 표시라 en 에도 그대로 뒀다.

### 검증 결과

작성 중 에디터에서 Export 수집 결과(키·ko)와 이 표의 en 을 `LocText` 규칙으로 한 행씩 검사했다(일회성 실행, 테스트 파일 없음):
`SamePlaceholders`(ko↔en) · `BraceError`·`StyleTagError`(ko·en 각각) · 줄바꿈 수 · 수집 키 ↔ 표 키 1:1.
**Strings 100 · ErrorCodes 35 — 문제 0건.**
스킬 툴팁의 "필드가 실제로 있는가"(`SkillTooltipFormatter`)는 ko 와 자리표시자 집합이 같아 ko 결과를 따른다.

## 용어집

| ko | en | 근거·메모 |
|---|---|---|
| 간파 | **Interrupt** | 플레이어 스킬 코드·문서가 `Interrupt`(`PlayerInterruptSkillBase`·`GunnerInterruptData`·`isInterruptAttack`). `Counter` 는 보스 쪽 판정(`CounterWindow`) 용어라 화면에는 안 쓴다 — 결과 표 키 `UI.Result.Counters` 도 화면 글자는 `Interrupts` |
| 변신 | Transform / Transformed | 스킬 이름·HUD = `Transform`, 부제 상태 = `Transformed` |
| 분노 게이지 | Rage | |
| 과열 / 과열도 | Overheat (HUD 단계 = `Heat Lv.`) | |
| 집중도 | Focus | 상태이상 `Focus` 와 같은 단어 |
| 강타 | Power Strike | 어쌔신 강화 기본 공격 |
| 백어택 | Back Attack | |
| 보호막 | shield | `<style=shield>` 키워드 색 그대로 |
| 궁극기 | Ultimate | |
| Q 스킬 · E 스킬 · 우클릭 스킬 | Q Skill · E Skill · Right-Click Skill | 키 이름은 키 토큰 후보 |
| 쿨타임 | cooldown | |
| 초 | s | 공용 `UI.Unit.Seconds` = `{sec}s`. 툴팁 설명 안에 박힌 `초` 도 `s` |
| 조인코드 | join code | |
| 호스트 · 클라이언트 | host · client | ko 의 `Host`·`Client` 표기도 en 은 소문자 일반명사 |

## 기획 결정 필요

| 항목 | 키 | 결정할 것 |
|---|---|---|
| ko 가 영어인 문구 | `UI.Loading.Preparing`·`Loading`·`WaitingForPlayers`·`Activating`·`StandBy` · `UI.Loading.Ready1~3` · `UI.Result.Cleared`·`Failed`·`Aborted` · `UI.SkillTooltip.PassiveBadge` · `UI.Options.Language` | ko 칸을 한국어로 할지(en 은 지금 글자 그대로) |
| 상태이상 이름 | `UI.StatusEffect.*` 14개 | ko 를 한국어(`#설명` 의 기절·둔화…)로 할지 · en 을 띄어 쓸지(`Super Armor`·`Move Speed`·`Max HP` 등) |
| 로딩 장난 팁 | `UI.Loading.Tip1~3` | 문구 교체 후보 — 실제 팁으로 바꿀지 |
| 스킬 툴팁 `TODO` 문구 | `*/tooltip.description` 전부 | ko 확정 뒤 en 재번역 |
| 문구에 박힌 수치 | `AssassinTransformSkillData/tooltip.description`(`2초`) · `AssassinCircleStrikeSkillData/tooltip.description`(`5번`) | 자리표시자로 뺄지(빼면 ko·en 같이) |
| 조작 키 이름 | `UI.SkillTooltip.ShiftHint` · `UI.AssassinHUD.TransformReleasable` · `UI.AssassinHUD.EnhancedReady` · `AssassinStateData/tooltip.description`(`R`) · 부제 `우클릭` | `{key:액션}` 토큰 적용 시점 |
| 길이 | `AssassinStateData/tooltip.description` | en 이 길다 — 툴팁 상자에서 넘치는지 Play 확인 |

## Strings

### 스킬 툴팁 (L2 자동 키 — `[DataTableText]`)

| 시트 | key | ko(현재 값) | en 초안 | 메모 |
|---|---|---|---|---|
| Strings | `GunnerInterruptData/tooltip.displayName` | `근접 간파` | `Melee Interrupt` |  |
| Strings | `GunnerInterruptData/tooltip.subtitle` | `우클릭 스킬` | `Right-Click Skill` | 우클릭 = 키 토큰 후보(리바인딩) |
| Strings | `GunnerInterruptData/tooltip.description` | `TODO 근접 공격을 간파해 {dmg} 피해를 줍니다.` | `TODO Interrupt a melee attack, dealing {dmg} damage.` | ko·en 앞 `TODO` = 문구 미확정 표시(그대로 둠) |
| Strings | `GunnerHeatData/tooltip.displayName` | `과열` | `Overheat` |  |
| Strings | `GunnerHeatData/tooltip.description` | `TODO 과열 단계에 따라 기본 공격 피해가 {dmg} 범위로 강화됩니다.` | `TODO Basic attack damage scales up to {dmg} with your Overheat stage.` | ko·en 앞 `TODO` = 문구 미확정 표시(그대로 둠) |
| Strings | `AssassinTransformedDashStrikeSkillData/tooltip.displayName` | `관통 돌진 (변신)` | `Piercing Dash (Transformed)` |  |
| Strings | `AssassinTransformedDashStrikeSkillData/tooltip.subtitle` | `Q 스킬 · 변신` | `Q Skill · Transformed` |  |
| Strings | `AssassinTransformedDashStrikeSkillData/tooltip.description` | `TODO 돌진하며 경로의 적에게 {dmg} 피해를 줍니다. 적중 시 쿨타임이 줄어듭니다.` | `TODO Dash forward, dealing {dmg} damage to enemies in your path. Hits reduce the cooldown.` | ko·en 앞 `TODO` = 문구 미확정 표시(그대로 둠) |
| Strings | `FirstMeleePassive/tooltip.displayName` | `불굴의 의지` | `Indomitable Will` | 프리팹 컴포넌트 키(Player_Paladin) |
| Strings | `FirstMeleePassive/tooltip.description` | `TODO 준비되면 다음 공격에 {dmg} 추가 피해를 주고 최대 체력의 {healPercent}%를 회복합니다.` | `TODO When ready, your next attack deals {dmg} bonus damage and restores {healPercent}% of max HP.` | ko·en 앞 `TODO` = 문구 미확정 표시(그대로 둠) · `{healPercent}%` 의 `%` 는 문구 글자 |
| Strings | `FirstMeleeUltimateSkillData/tooltip.displayName` | `최후의 심판` | `Final Judgment` |  |
| Strings | `FirstMeleeUltimateSkillData/tooltip.subtitle` | `궁극기` | `Ultimate` |  |
| Strings | `FirstMeleeUltimateSkillData/tooltip.description` | `TODO 대상을 심판해 {dmg} 피해를 줍니다.` | `TODO Pass judgment on the target, dealing {dmg} damage.` | ko·en 앞 `TODO` = 문구 미확정 표시(그대로 둠) |
| Strings | `AssassinDashStrikeSkillData/tooltip.displayName` | `관통 돌진` | `Piercing Dash` |  |
| Strings | `AssassinDashStrikeSkillData/tooltip.subtitle` | `Q 스킬` | `Q Skill` |  |
| Strings | `AssassinDashStrikeSkillData/tooltip.description` | `TODO 바라보는 방향으로 돌진하며 경로의 적에게 {dmg} 피해를 줍니다.` | `TODO Dash in the direction you face, dealing {dmg} damage to enemies in your path.` | ko·en 앞 `TODO` = 문구 미확정 표시(그대로 둠) |
| Strings | `AssassinTransformSkillData/tooltip.displayName` | `변신` | `Transform` |  |
| Strings | `AssassinTransformSkillData/tooltip.subtitle` | `궁극기` | `Ultimate` |  |
| Strings | `AssassinTransformSkillData/tooltip.description` | `TODO 분노 게이지 전부를 연료로 변신합니다. 게이지가 0이 될 때까지 유지되며, 2초 뒤 다시 누르면 해제하고 남은 게이지를 보존합니다.` | `TODO Consume all Rage to transform. Lasts until Rage reaches 0. Press again after 2s to revert and keep the remaining Rage.` | ko·en 앞 `TODO` = 문구 미확정 표시(그대로 둠) · `2초` 가 문구에 박힌 수치 — 자리표시자화는 기획 결정 |
| Strings | `GunnerChargeLaserData/tooltip.displayName` | `충전 레이저` | `Charge Laser` |  |
| Strings | `GunnerChargeLaserData/tooltip.subtitle` | `Q 스킬` | `Q Skill` |  |
| Strings | `GunnerChargeLaserData/tooltip.description` | `TODO 집중도와 과열 단계에 따라 {dmg} 피해를 주고, <style=shield><sprite name="shield" color=#74C7EC>{shieldAmount}</style>의 보호막을 {shieldDuration:0.0}초 동안 얻습니다.` | `TODO Deals {dmg} damage based on Focus and Overheat stage, and grants a <style=shield><sprite name="shield" color=#74C7EC>{shieldAmount}</style> shield for {shieldDuration:0.0}s.` | ko·en 앞 `TODO` = 문구 미확정 표시(그대로 둠) · `<style=shield>` 짝·`<sprite>` 그대로 · `{shieldDuration:0.0}` 형식 포함 일치 · 툴팁 안 `초` → `s` |
| Strings | `AssassinStateData/tooltip.displayName` | `백어택 · 분노 게이지` | `Back Attack · Rage` |  |
| Strings | `AssassinStateData/tooltip.description` | `TODO 보스 뒤에서 공격하면 피해가 증가합니다. 기본 공격·Q·강타가 적에게 적중하면 분노 게이지가 찹니다(최대 {maxRage}). 게이지가 {minTransformRage} 이상이면 R로 변신하고, 변신 중에는 초당 {transformRageDecayPerSecond}씩 줄어듭니다. 변신 중에는 모든 공격이 백어택입니다.` | `TODO Attacks from behind the boss deal more damage. Basic attacks, Q, and Power Strikes that hit fill Rage (max {maxRage}). At {minTransformRage}+ Rage, press R to transform; while transformed, Rage drains by {transformRageDecayPerSecond} per second and every attack counts as a Back Attack.` | ko·en 앞 `TODO` = 문구 미확정 표시(그대로 둠) · 🔸 길이 김(툴팁 상자 확인) · `R` = 키 토큰 후보 |
| Strings | `AssassinInterruptSkillData/tooltip.displayName` | `간파` | `Interrupt` |  |
| Strings | `AssassinInterruptSkillData/tooltip.subtitle` | `우클릭 스킬` | `Right-Click Skill` | 우클릭 = 키 토큰 후보 |
| Strings | `AssassinInterruptSkillData/tooltip.description` | `TODO 무기를 올려쳐 적을 간파하고 {dmg} 피해를 줍니다.` | `TODO Swing your weapon upward to interrupt enemies, dealing {dmg} damage.` | ko·en 앞 `TODO` = 문구 미확정 표시(그대로 둠) |
| Strings | `GunnerCoolBackstepData/tooltip.displayName` | `냉각 백스텝` | `Cooling Backstep` |  |
| Strings | `GunnerCoolBackstepData/tooltip.subtitle` | `E 스킬` | `E Skill` |  |
| Strings | `GunnerCoolBackstepData/tooltip.description` | `TODO 뒤로 {distance}m 이동하고 과열도를 초기화합니다.` | `TODO Move back {distance}m and reset Overheat.` | ko·en 앞 `TODO` = 문구 미확정 표시(그대로 둠) |
| Strings | `FirstMeleeInterruptSkillData/tooltip.displayName` | `단죄의 방패` | `Shield of Condemnation` |  |
| Strings | `FirstMeleeInterruptSkillData/tooltip.subtitle` | `우클릭 스킬` | `Right-Click Skill` | 우클릭 = 키 토큰 후보 |
| Strings | `FirstMeleeInterruptSkillData/tooltip.description` | `TODO 적을 간파해 {dmg} 피해를 줍니다.` | `TODO Interrupt enemies, dealing {dmg} damage.` | ko·en 앞 `TODO` = 문구 미확정 표시(그대로 둠) |
| Strings | `FirstMeleeMainSkillData/tooltip.displayName` | `진격의 방패` | `Advancing Shield` |  |
| Strings | `FirstMeleeMainSkillData/tooltip.subtitle` | `Q 스킬` | `Q Skill` |  |
| Strings | `FirstMeleeMainSkillData/tooltip.description` | `TODO 전진하며 적에게 {dmg} 피해를 줍니다.` | `TODO Advance forward, dealing {dmg} damage to enemies.` | ko·en 앞 `TODO` = 문구 미확정 표시(그대로 둠) |
| Strings | `AssassinEnhanceSkillData/tooltip.displayName` | `단검 강화` | `Empowered Dagger` |  |
| Strings | `AssassinEnhanceSkillData/tooltip.subtitle` | `E 스킬` | `E Skill` |  |
| Strings | `AssassinEnhanceSkillData/tooltip.description` | `TODO 다음 기본 공격이 강타로 바뀝니다. 강타가 적중하면 분노 게이지가 크게 찹니다.` | `TODO Your next basic attack becomes a Power Strike. Landing it fills a large amount of Rage.` | ko·en 앞 `TODO` = 문구 미확정 표시(그대로 둠) |
| Strings | `FirstMeleeSubSkillData/tooltip.displayName` | `수호자의 의지` | `Guardian's Will` |  |
| Strings | `FirstMeleeSubSkillData/tooltip.subtitle` | `E 스킬` | `E Skill` |  |
| Strings | `FirstMeleeSubSkillData/tooltip.description` | `TODO <style=shield><sprite name="shield" color=#74C7EC>{shieldAmount}</style>의 보호막을 {shieldDuration:0.0}초 동안 얻습니다.` | `TODO Gain a <style=shield><sprite name="shield" color=#74C7EC>{shieldAmount}</style> shield for {shieldDuration:0.0}s.` | ko·en 앞 `TODO` = 문구 미확정 표시(그대로 둠) · `<style=shield>` 짝·`<sprite>` 그대로 · 툴팁 안 `초` → `s` |
| Strings | `AssassinCircleStrikeSkillData/tooltip.displayName` | `원형 난격` | `Circle Barrage` |  |
| Strings | `AssassinCircleStrikeSkillData/tooltip.subtitle` | `E 스킬 · 변신` | `E Skill · Transformed` |  |
| Strings | `AssassinCircleStrikeSkillData/tooltip.description` | `TODO 지정한 원 범위에 {dmg} 피해를 5번 줍니다. 공격 중 무적입니다.` | `TODO Deal {dmg} damage 5 times in the target circle. Invulnerable while attacking.` | ko·en 앞 `TODO` = 문구 미확정 표시(그대로 둠) · `5번` 이 문구에 박힌 수치 |
| Strings | `GunnerTrackingLaserData/tooltip.displayName` | `추적 레이저` | `Tracking Laser` |  |
| Strings | `GunnerTrackingLaserData/tooltip.subtitle` | `궁극기` | `Ultimate` |  |
| Strings | `GunnerTrackingLaserData/tooltip.description` | `TODO 대상을 추적하며 틱마다 {dmg} 피해를 줍니다.` | `TODO Track the target, dealing {dmg} damage per tick.` | ko·en 앞 `TODO` = 문구 미확정 표시(그대로 둠) |

### 캐릭터 이름 (L2 — `Character.<프리팹>.Name`)

| 시트 | key | ko(현재 값) | en 초안 | 메모 |
|---|---|---|---|---|
| Strings | `Character.Player_Paladin.Name` | `팔라딘` | `Paladin` |  |
| Strings | `Character.Player_Gunner.Name` | `거너` | `Gunner` |  |
| Strings | `Character.Player_Assassin.Name` | `어쌔신` | `Assassin` |  |

### UiStrings (L3·L4b)

| 시트 | key | ko(현재 값) | en 초안 | 메모 |
|---|---|---|---|---|
| Strings | `UI.Result.Character` | `캐릭터` | `Character` | 결과 표 머리글 — 짧게 |
| Strings | `UI.Result.Damage` | `데미지` | `Damage` | 〃 |
| Strings | `UI.Result.Kills` | `처치` | `Kills` | 〃 |
| Strings | `UI.Result.Counters` | `간파` | `Interrupts` | 〃 · 키는 `Counters` 지만 화면 용어는 간파 = Interrupt(용어집) |
| Strings | `UI.Result.Deaths` | `사망` | `Deaths` | 〃 |
| Strings | `UI.AssassinHUD.EnhancedReady` | `E 강화 준비` | `E Ready` | HUD — 짧게 · `E` = 키 토큰 후보 |
| Strings | `UI.AssassinHUD.TransformRemaining` | `변신 {time}` | `Transform {time}` | HUD · `{time}` = `UI.Unit.Seconds` 결과 |
| Strings | `UI.AssassinHUD.TransformReleasable` | `변신 {time} · R 해제 가능` | `Transform {time} · R to Revert` | HUD · `R` = 키 토큰 후보 |
| Strings | `UI.AssassinHUD.TransformEndPending` | `변신 종료 대기` | `Transform Ending` | HUD — 짧게 |
| Strings | `UI.GunnerHeat.Stage` | `과열 {stage}단계` | `Heat Lv.{stage}` | HUD — 짧게 · `{stage}` 0부터 |
| Strings | `UI.GunnerHeat.Overheated` | `과열!` | `Overheated!` | HUD |
| Strings | `UI.Interact.Prompt` | `상호 작용` | `Interact` |  |
| Strings | `UI.Overhead.PlayerLabel` | `P{n}` | `P{n}` | 기호 — 같음 |
| Strings | `UI.Options.Language` | `Language` | `Language` | ko 가 영어 — ko 를 한국어로 할지 기획 결정 |
| Strings | `UI.Unit.Seconds` | `{sec}초` | `{sec}s` | 공용 초 단위 — 쿨타임·상태이상·변신이 모두 이 칸을 쓴다 |
| Strings | `UI.Loading.Preparing` | `Preparing...` | `Preparing...` | ko 가 영어 — ko 를 한국어로 할지 기획 결정 |
| Strings | `UI.Loading.Loading` | `Loading...` | `Loading...` | ko 가 영어 — ko 를 한국어로 할지 기획 결정 |
| Strings | `UI.Loading.WaitingForPlayers` | `Waiting for players...` | `Waiting for players...` | ko 가 영어 — ko 를 한국어로 할지 기획 결정 |
| Strings | `UI.Loading.Activating` | `Starting...` | `Starting...` | ko 가 영어 — ko 를 한국어로 할지 기획 결정 |
| Strings | `UI.Loading.StandBy` | `Stand by...` | `Stand by...` | ko 가 영어 — ko 를 한국어로 할지 기획 결정 |
| Strings | `UI.SkillTooltip.ShiftHint` | `자세한 정보를 보려면 [Shift] 키를 누르세요` | `Press [Shift] for details` | `[Shift]` = 키 토큰 후보(`{key:Detail}`) |
| Strings | `UI.SkillTooltip.Error` | `[오류: {error}]` | `[Error: {error}]` | 개발용 — `{error}` 는 번역 안 함 |
| Strings | `UI.SkillTooltip.PassiveBadge` | `Passive` | `Passive` | ko 가 영어 — ko 를 한국어로 할지 기획 결정 · 배지 — 짧게 |
| Strings | `UI.Result.Cleared` | `CLEAR` | `CLEAR` | ko 가 영어 — ko 를 한국어로 할지 기획 결정 |
| Strings | `UI.Result.Failed` | `FAILED` | `FAILED` | ko 가 영어 — ko 를 한국어로 할지 기획 결정 |
| Strings | `UI.Result.Aborted` | `ABORTED` | `ABORTED` | ko 가 영어 — ko 를 한국어로 할지 기획 결정 |
| Strings | `UI.Result.Survival` | `생존 시간  {time}` | `Survival Time  {time}` | ko 처럼 공백 두 칸 유지 · `{time}` = mm:ss |
| Strings | `UI.Result.PlayerSlot` | `P{n}` | `P{n}` | 기호 — 같음 |
| Strings | `UI.TrainingDummy.Hp` | `{hp} / {max}` | `{hp} / {max}` | 기호 — 같음 |
| Strings | `UI.Loading.Tip1` | `으아ㅏㅏㅏㅏ` | `Waaaaaah` | 문구 교체 후보 |
| Strings | `UI.Loading.Tip2` | `커피ㅣㅣㅣㅣ` | `Coffeeeee` | 문구 교체 후보 |
| Strings | `UI.Loading.Tip3` | `밥 뭐 먹지....` | `What's for lunch....` | 문구 교체 후보 |
| Strings | `UI.Loading.Ready1` | `Enter...` | `Enter...` | ko 가 영어 — ko 를 한국어로 할지 기획 결정 |
| Strings | `UI.Loading.Ready2` | `All players are ready.` | `All players are ready.` | ko 가 영어 — ko 를 한국어로 할지 기획 결정 |
| Strings | `UI.Loading.Ready3` | `Starting...` | `Starting...` | ko 가 영어 — ko 를 한국어로 할지 기획 결정 |

### UiStrings — 상태이상 이름 (L4b)

| 시트 | key | ko(현재 값) | en 초안 | 메모 |
|---|---|---|---|---|
| Strings | `UI.StatusEffect.Airborne` | `Airborne` | `Airborne` | ko 가 영어 — ko 를 한국어로 할지 기획 결정 (뜻: 공중에 뜸) |
| Strings | `UI.StatusEffect.Stunned` | `Stunned` | `Stunned` | ko 가 영어 — ko 를 한국어로 할지 기획 결정 (기절) |
| Strings | `UI.StatusEffect.Slowed` | `Slowed` | `Slowed` | ko 가 영어 — ko 를 한국어로 할지 기획 결정 (둔화) |
| Strings | `UI.StatusEffect.Rooted` | `Rooted` | `Rooted` | ko 가 영어 — ko 를 한국어로 할지 기획 결정 (속박) |
| Strings | `UI.StatusEffect.Silenced` | `Silenced` | `Silenced` | ko 가 영어 — ko 를 한국어로 할지 기획 결정 (침묵) |
| Strings | `UI.StatusEffect.Debilitated` | `Debilitated` | `Debilitated` | ko 가 영어 — ko 를 한국어로 할지 기획 결정 (약화) |
| Strings | `UI.StatusEffect.SuperArmor` | `SuperArmor` | `SuperArmor` | ko 가 영어 — ko 를 한국어로 할지 기획 결정 (슈퍼아머) · en 띄어쓰기 `Super Armor` 후보 |
| Strings | `UI.StatusEffect.MoveSpeedModifier` | `MoveSpeed` | `MoveSpeed` | ko 가 영어 — ko 를 한국어로 할지 기획 결정 (이동 속도 증감) · en `Move Speed` 후보 |
| Strings | `UI.StatusEffect.AttackDamageModifier` | `AttackDamage` | `AttackDamage` | ko 가 영어 — ko 를 한국어로 할지 기획 결정 (공격력 증감) · en `Attack Damage` 후보 |
| Strings | `UI.StatusEffect.AttackSpeedModifier` | `AttackSpeed` | `AttackSpeed` | ko 가 영어 — ko 를 한국어로 할지 기획 결정 (공격 속도 증감) · en `Attack Speed` 후보 |
| Strings | `UI.StatusEffect.DefenseModifier` | `Defense` | `Defense` | ko 가 영어 — ko 를 한국어로 할지 기획 결정 (방어력 증감) |
| Strings | `UI.StatusEffect.MaxHpModifier` | `MaxHp` | `MaxHp` | ko 가 영어 — ko 를 한국어로 할지 기획 결정 (최대 체력 증감) · en `Max HP` 후보 |
| Strings | `UI.StatusEffect.PassiveCharge` | `PassiveCharge` | `PassiveCharge` | ko 가 영어 — ko 를 한국어로 할지 기획 결정 (패시브 충전) · en `Passive Charge` 후보 |
| Strings | `UI.StatusEffect.Focus` | `Focus` | `Focus` | ko 가 영어 — ko 를 한국어로 할지 기획 결정 (정신 집중) |

## ErrorCodes

> `#분류`·`#원인`·`#조치` 메모 열은 번역 대상이 아니다(런타임 표에 안 들어감) — en 칸만 채운다.

### ErrorCatalog (L4a)

| 시트 | code | ko(현재 값) | en 초안 | 메모 |
|---|---|---|---|---|
| ErrorCodes | `LOBBY_LAUNCHER_MISSING` | `네트워크 세션 런처를 찾을 수 없습니다.` | `Network session launcher not found.` | 개발 문제 — 유저 노출 드묾 |
| ErrorCodes | `LOBBY_UI_MISSING` | `로비 UI 컨트롤러를 찾을 수 없습니다.` | `Lobby UI controller not found.` | 개발 문제 |
| ErrorCodes | `LOBBY_NOT_ALL_READY` | `모든 플레이어가 준비되지 않았습니다.` | `Not all players are ready.` |  |
| ErrorCodes | `NET_IP_EMPTY` | `IP를 입력하세요.` | `Enter an IP address.` |  |
| ErrorCodes | `NET_IP_INVALID` | `IP 형식이 올바르지 않습니다: {ip}` | `Invalid IP format: {ip}` | `{ip}` = 입력값 |
| ErrorCodes | `NET_IPV4_INVALID` | `IPv4 형식이 올바르지 않습니다: {ip}` | `Invalid IPv4 format: {ip}` | `{ip}` = 입력값 |
| ErrorCodes | `NET_PORT_INVALID` | `Port는 1~65535 범위의 숫자여야 합니다: {port}` | `Port must be a number from 1 to 65535: {port}` | `{port}` = 입력값 |
| ErrorCodes | `NET_PROVIDER_MISSING` | `{mode} 연결 방식의 프로바이더가 등록되지 않았습니다.` | `No provider is registered for the {mode} connection mode.` | `{mode}` = 연결 방식 enum 이름(번역 안 됨) |
| ErrorCodes | `NET_START_CANCELED` | `세션 시작이 취소되었습니다.` | `Session start was canceled.` |  |
| ErrorCodes | `NET_PREPARE_FAILED` | `세션 연결 준비에 실패했습니다.` | `Failed to prepare the session connection.` |  |
| ErrorCodes | `NET_START_EXCEPTION` | `세션 시작 중 예외가 발생했습니다.` | `An error occurred while starting the session.` | ko `예외` → en `error`(유저용) |
| ErrorCodes | `NET_START_FAILED` | `세션 시작에 실패했습니다.` | `Failed to start the session.` |  |
| ErrorCodes | `NET_HOST_START_FAILED` | `Host 시작에 실패했습니다. 포트가 이미 사용 중인지 확인하세요.` | `Failed to start host. Check whether the port is already in use.` |  |
| ErrorCodes | `NET_CLIENT_START_FAILED` | `Client 시작에 실패했습니다.` | `Failed to start client.` |  |
| ErrorCodes | `NET_CONNECT_FAILED` | `서버 접속에 실패했습니다. IP/Port와 Host 상태를 확인하세요.` | `Failed to connect to the server. Check the IP/Port and host status.` |  |
| ErrorCodes | `NET_REJECTED_BY_HOST` | `호스트가 접속을 거절했습니다. 양쪽 빌드가 같은 버전인지 확인하세요. (내 {cfg})` | `The host rejected the connection. Make sure both builds are the same version. (mine: {cfg})` | `{cfg}` = 내 설정 해시 |
| ErrorCodes | `NET_DISCONNECTED` | `서버와의 연결이 끊어졌습니다.` | `Disconnected from the server.` |  |
| ErrorCodes | `NET_PLAYER_DISCONNECTED` | `플레이어(ClientId={clientId})의 연결이 끊어졌습니다.` | `Player (ClientId={clientId}) disconnected.` | `{clientId}` = NGO ClientId |
| ErrorCodes | `NET_TRANSPORT_FAILURE` | `네트워크 전송 오류가 발생했습니다.` | `A network transport error occurred.` |  |
| ErrorCodes | `RELAY_TRANSPORT_MISSING` | `Relay 연결에 필요한 UnityTransport를 찾을 수 없습니다.` | `UnityTransport, required for Relay, was not found.` | 개발 문제 |
| ErrorCodes | `RELAY_JOIN_CODE_EMPTY` | `Relay 조인코드를 입력하세요.` | `Enter a Relay join code.` |  |
| ErrorCodes | `RELAY_JOIN_CODE_INVALID` | `Relay 조인코드가 잘못되었거나 만료되었습니다.` | `The Relay join code is invalid or has expired.` |  |
| ErrorCodes | `RELAY_INACTIVE_PROJECT` | `Unity Dashboard에서 이 프로젝트의 Relay 서비스를 활성화하세요.` | `Enable the Relay service for this project in the Unity Dashboard.` | 개발 문제 |
| ErrorCodes | `RELAY_UNAUTHORIZED` | `Relay 인증 또는 프로젝트 접근 권한이 없습니다.` | `No Relay authorization or project access.` |  |
| ErrorCodes | `RELAY_PAYMENT_REQUIRED` | `Relay 사용 한도 또는 결제 설정을 확인하세요.` | `Check your Relay usage limit or billing settings.` | 개발 문제 |
| ErrorCodes | `RELAY_RATE_LIMITED` | `Relay 요청 한도를 초과했습니다. 잠시 후 다시 시도하세요.` | `Too many Relay requests. Please try again shortly.` |  |
| ErrorCodes | `RELAY_UNAVAILABLE` | `사용 가능한 Relay 서버가 없습니다. 잠시 후 다시 시도하세요.` | `No Relay server is available. Please try again shortly.` |  |
| ErrorCodes | `RELAY_JOIN_REJECTED` | `Relay 방에 참가할 수 없습니다. 조인코드와 방 정원을 확인하세요.` | `Cannot join the Relay room. Check the join code and room capacity.` |  |
| ErrorCodes | `RELAY_HOST_FAILED` | `Relay 방 생성에 실패했습니다.` | `Failed to create a Relay room.` |  |
| ErrorCodes | `RELAY_JOIN_FAILED` | `Relay 방 참가에 실패했습니다.` | `Failed to join the Relay room.` |  |
| ErrorCodes | `UGS_PROJECT_NOT_LINKED` | `Unity Services 프로젝트 연결(cloudProjectId)이 설정되지 않았습니다.` | `The Unity Services project link (cloudProjectId) is not set.` | 개발 문제 |
| ErrorCodes | `UGS_INIT_FAILED` | `Unity Services 초기화 또는 익명 로그인에 실패했습니다.` | `Failed to initialize Unity Services or sign in anonymously.` |  |
| ErrorCodes | `UGS_NOT_INITIALIZED` | `Unity Services가 초기화되지 않았습니다.` | `Unity Services is not initialized.` |  |
| ErrorCodes | `UGS_INITIALIZING` | `Unity Services 초기화 중입니다. 잠시 후 다시 시도하세요.` | `Unity Services is initializing. Please try again shortly.` |  |
| ErrorCodes | `UGS_NOT_SIGNED_IN` | `Unity Services 익명 로그인이 완료되지 않았습니다.` | `Unity Services anonymous sign-in has not completed.` |  |
