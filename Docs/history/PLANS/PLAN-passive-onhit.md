# PLAN — 패시브 전면 수정 + 범용 "적중 시" 이벤트 (2026-09-30, ✅ §1~12 완료·검증 · §13 독립 리뷰 반영)

> **승인 시 추가 결정 (2026-09-29 은희)**
> - **상태이상에 Buff/Debuff 분류 enum 을 우리가 추가한다** → §4.6. (경석의 `ClearAllServer()` 디버프 한정화가 이 분류를 쓴다)
> - **상태이상 HUD 에 아이콘 이미지를 띄운다.** 지금은 전 타입 `white_512.png`(`Assets/50.Art/TestAssets/Temp_Images/Images/`, SVN) → §4.7.
> - R-6: `PassiveCharge` 는 HUD 에 **표시**(아이콘 + 텍스트).
> - `ServerHitEnemiesResolved` 제거·일원화 **동의**.

작성: Claude / 담당: **은희**(플레이어·코어 영역) / 브랜치: `fix/Player` (Variant 작업 위에 이어서)
관련: [PLAN-player-variants.md](../../../Docs/history/PLANS/PLAN-player-variants.md) · [Docs/tech/player-prefabs.md](../../../Docs/tech/player-prefabs.md) ·
[Docs/temp/plan-passive-2026-07.md](../../../Docs/temp/plan-passive-2026-07.md)(현행 불굴의 의지 원 설계)

---

## 1. Goal

1. **패시브를 캐릭터 무관하게 붙일 수 있게** 한다 — base(`Player`·`PassiveHUD`)가 `FirstMeleePassive` 를 모르게.
2. **범용 적중 이벤트**를 만든다 — 플레이어 공격이 적을 맞히면 공격자 쪽에서 한 곳으로 발행.
   패시브 버프, 이후 징크스 스택·빌드/장비 효과가 같은 이벤트를 구독한다.
3. 불굴의 의지를 **버프 모델**로 다시 짠다:
   ① 패시브가 활성화되면 **Buff 가 생긴다** → ② Buff 보유 중 **적중시 발동 공격**이 맞으면 이벤트가 발동하고 Buff 가 **소모**된다 →
   ③ 소모되면 **쿨타임이 시작**되고, 끝나면 ①로.

## 2. 확정 사항 (grill, 2026-09-29 은희)

| 항목 | 결정 |
|------|------|
| 적중 이벤트 범위 | **범용.** 패시브 버프는 구독자 중 하나 |
| 소모 가능 공격 지정 | **공격이 지정** — 평타 스텝 데이터·스킬 데이터에 `triggersOnHit` 플래그 |
| 플래그 초기값 | **평타만 켠다.** 스킬(Q·우클릭·R)은 꺼 둔다 |
| 이벤트 배선 범위 | **플레이어 공격 전부**(평타 3경로 + Q·우클릭·R) — 플래그와 무관하게 이벤트는 발행 |
| 이벤트 단위 | **공격 판정 1회당 1번** — 맞은 대상 목록을 순서대로. "처음 맞은 한 명" = 목록[0] |
| 다중 적중 시 버프 효과 | **처음 맞은 한 명만** |
| 가붕이 소모 효과 | **첫 대상 추가피해(공격력×계수+고정) + 자신 최대체력 고정 % 회복**(현행 최소값 5%) |
| 피격 시 쿨타임 감소 | **유지**(쿨타임 중 피격마다 −2초, 데미지 무관) |
| 버프 저장소 | **기존 `StatusEffectController`** — 새 타입, `duration 0`(소모 전까지 유지) |
| 초기 상태 | **버프 보유로 시작**(스폰 즉시). 부활은 상태 유지 |
| 보스 연출 `ClearAllServer()` | 🔴 **경석이 "디버프만 제거" 로 수정 예정** — 이 작업의 **외부 선행 의존**(§6 R-1) |

## 3. Current understanding (조사 근거 — file:line)

- **버프 저장소**: `StatusEffectController`(`Unit/StatusEffectController.cs:44`) — 서버 권위 `NetworkList`, 전 피어 복제.
  `Apply(type, magnitude, duration, sourceId, maxStacks)`(:145), `duration <= 0` = 수동 해제 전까지 유지(:9-37),
  `Remove(type, sourceId)`(:178), `Has(type)`(:81). 타입은 `[Flags]` enum(`StatusEffectType.cs:5-25`, 최대 `MaxHpModifier = 1<<11`).
  상태이상 HUD 는 `type.ToString()` 텍스트로 표시(`UI/Combat/StatusEffectHUD.cs:57-64`).
- **공격자 쪽 공통 적중 지점이 없다.** 공통 지점은 **피격자** `Unit.ReceiveAttack`(`Unit.cs:150`)뿐.
  이벤트는 평타 Overlap 의 `PlayerDefaultAttack.ServerHitEnemiesResolved`(:14) 하나 — Raycast(:215)·투사체(`DefaultAttackProjectile.cs:40`)·스킬에는 없다.
- **현행 패시브 결합**: `Player.cs:67,191` `FirstMeleePassive passive` + `ReceiveAttack` 에서 `passive?.NotifyOwnerHit()`(:1601) ·
  `PassiveHUD.cs:31` `GetComponent<FirstMeleePassive>()`.
- **패시브 추가타가 `AttackType.Default` 로 찍힌다**(`FirstMeleePassive.cs:279`) — 피격자 쪽에서 "평타면 발동"을 걸면 자기 자신을 재발동시킨다.
  → 이번 설계는 **공격자 쪽 이벤트**라 추가타는 이벤트를 **발행하지 않는다**(§4.1).
- **스킬 적중 경로**: Q `FirstMeleeMainSkill.OnHoldTick`(:117-163) · 우클릭 `FirstMeleeInterruptSkill.ResolveHit`(:113-168) ·
  R `FirstMeleeUltimateSkill.OnChannelCompleted`(:98-112). E 는 적중 없음. Q·R 은 `hitContext.sourceUnit` 을 **안 채운다**(:151, :104).
- 평타 스텝 데이터 = `DefaultAttackStep`(`DefaultAttackController.cs:818`, `DefaultAttackData` SO 안). 스킬 데이터 = `PlayerSkillData`(SO).
- 투사체 적중은 서버 게이트(`DefaultAttackProjectile.cs:42 !IsServer return`). 단 투사체 자체는 일반 `Instantiate`(네트워크 스폰 아님) — 이 작업 범위 밖.

## 4. Approach

### 4.1 범용 적중 이벤트 (공격자 쪽, 서버 전용)

- `Player` 에 서버 이벤트 1개: **`ServerAttackLanded`** — 인자 `PlayerAttackLanded`(struct):
  `AttackType attackType` · `bool triggersOnHit` · `IReadOnlyList<Unit> targets`(판정 순서, 1명 이상) · `Object source`(발행 컴포넌트, 디버그용).
- 발행 헬퍼 `Player.RaiseServerAttackLanded(...)` 하나로 모든 경로가 부른다. 대상 0명이면 발행하지 않는다.
- 발행 지점 6곳: 평타 Overlap(기존 `ServerHitEnemiesResolved` 자리) · Raycast · 투사체(1발=1판정) · Q 틱 · 우클릭 판정 · R 완료.
- `PlayerDefaultAttack.ServerHitEnemiesResolved` 는 **제거**하고 새 이벤트로 일원화(구독자는 현행 패시브 하나뿐). — one mechanism.
- **발행하지 않는 것**: 패시브 추가타 같은 "효과가 만든 피해". 재귀 발동 차단을 플래그가 아니라 구조로 한다.
- 김에 Q·R 의 `hitContext.sourceUnit = owner` 를 채운다(피격 쪽 공격자 추적이 비어 있던 것 — 한 줄씩).

### 4.2 `triggersOnHit` 플래그

- `DefaultAttackStep` 에 `[SerializeField] bool triggersOnHit = true` · `PlayerSkillData` 에 `triggersOnHit = false`.
- 발행 시 현재 스텝/스킬 데이터의 값을 그대로 싣는다. 이벤트는 플래그와 무관하게 항상 발행(구독자가 판단).
- ⚠️ 기존 에셋에 새 필드가 없을 때 초기값이 들어가는지 구현 후 **에셋 재직렬화 결과로 확인**(평타 = true 여야 함).

### 4.3 패시브 인터페이스 + base 결합 제거

- `IPlayerPassive`(신규): `float CooldownTime` · `float RemainingCooldown` · `bool IsReady`(= 버프 보유). HUD 용 읽기 전용.
- `PassiveHUD` → `GetComponent<IPlayerPassive>()`.
- `Player` → `passive` 필드·`NotifyOwnerHit` 호출 **삭제**. 대신 서버 이벤트 **`ServerAttackReceived`** 를 `ReceiveAttack` 에서 발행,
  피격 감소가 필요한 패시브만 구독.
- 🟡 범위 밖이지만 기록: 같은 `ReceiveAttack` 의 `shieldVfx?.ServerHit(...)` 도 가붕이 전용 결합이다 — 이벤트가 생기면 옮길 수 있음(후속).

### 4.4 불굴의 의지 재작성 (`FirstMeleePassive`)

- 상태 = **버프 보유 여부**(`StatusEffectController.Has(PassiveCharge)`, 전 피어 복제) + **쿨타임 종료 서버 시각**(`readyServerTime`, 오너 읽기 — HUD).
- 새 타입 **`StatusEffectType.PassiveCharge = 1 << 12`** — magnitude 1, duration 0, sourceId = 자기 NetworkObjectId. 블로커·스탯 테이블에 넣지 않는다.
  캐릭터마다 타입을 늘리지 않고 이 하나를 공용으로 쓴다(한 플레이어에 패시브는 하나).
- 흐름(서버):
  - `OnNetworkSpawn` → 버프 부여.
  - `ServerAttackLanded` 수신 → `triggersOnHit && 버프 보유` 면: 버프 `Remove` → **targets[0]** 에 추가피해 → 자신 최대체력 `healPercent`% 회복 → `readyServerTime = now + cooldown` → 발동 연출 RPC.
  - `ServerAttackReceived` 수신 → 쿨타임 중이면 `readyServerTime -= hitCooldownReduction`(현행과 동일).
  - `Update`(서버) → 쿨타임 끝났고 버프 없으면 버프 부여.
- 칼날 발광은 **버프 보유**에 묶는다 → 기존 `readyReplicated` NetworkVariable **삭제**(버프가 이미 전원 복제). — one mechanism.
- 데이터: `minTargetThreshold`·`perTargetHealPercent` 삭제, `minHealPercent` → `healPercent`(5). 나머지 수치·연출 필드 유지.
- `[RequireComponent(PlayerDefaultAttack)]` 해제 — 평타가 아니라 Player 이벤트에 의존.

### 4.6 Buff/Debuff 분류

- `StatusEffectCategory { Buff, Debuff }` enum 신규(`Unit/StatusEffectType.cs`).
- 분류는 **인스턴스에서 파생**한다 — 네트워크 구조체(`StatusEffectInstance`) 변경 없음:
  - 스탯 modifier 5종(`MoveSpeed/AttackDamage/AttackSpeed/Defense/MaxHpModifier`)은 **magnitude 로 판정** — `>= 1` Buff, `< 1` Debuff
    (같은 타입이 버프도 디버프도 된다 — `AreaZone` 의 0.5 감속 vs 향후 가속 버프).
  - 나머지는 타입 고정표: `SuperArmor`·`PassiveCharge` = Buff / `Airborne`·`Stunned`·`Slowed`·`Rooted`·`Silenced`·`Debilitated` = Debuff.
  - 새 타입을 추가하면 이 표에 넣어야 한다 — 누락 시 **경고 로그와 함께 `Debuff` 취급**(조용한 오분류 방지).
- API: `StatusEffectCategories.Of(in StatusEffectInstance)`. `ClearAllServer()` 수정은 **경석 몫** — 이 API 를 쓰면 된다.

### 4.7 상태이상 HUD 아이콘

- `StatusEffectHUD.EffectWidget` 에 `Image icon` 추가. `CombatHUD.prefab` 의 `Effect0~5` 루트 `Image` 에 배선.
- 스프라이트: `[SerializeField] Sprite defaultIcon`(= `white_512.png`) + 타입별 `IconEntry[] icons`(비어 있음 — 아트 나오면 채움).
- 텍스트(타입명·스택·남은시간)는 유지.

### 4.5 문서

- [Docs/temp/plan-passive-2026-07.md](../../../Docs/temp/plan-passive-2026-07.md) 에 "2026-09-29 버프 모델로 대체" 표기 + 이 계획 링크.
- [Docs/design/status-effects.md](../../../Docs/design/status-effects.md) 에 `PassiveCharge`(버프) 추가 — 현재 디버프만 적혀 있다.
- [Docs/tech/player-prefabs.md](../../../Docs/tech/player-prefabs.md) §0 "빈 곳" 에서 패시브 결합 항목 해소 표기.

## 5. 수정 파일

`Unit/StatusEffectType.cs` · `Unit/Weapon/BaseAttack.cs`(필요 시 struct 위치) · `Player/Player.cs` · `Player/PlayerDefaultAttack.cs` ·
`Player/DefaultAttackProjectile.cs` · `Player/DefaultAttackController.cs`(`DefaultAttackStep`) · `Player/Skill/PlayerSkillData.cs` ·
`Player/Skill/FirstMeleeMainSkill.cs` · `FirstMeleeInterruptSkill.cs` · `FirstMeleeUltimateSkill.cs` · `Player/Skill/FirstMeleePassive.cs` ·
`UI/Combat/PassiveHUD.cs` · 신규 `Player/Skill/IPlayerPassive.cs` · 프리팹 `Player_Paladin.prefab`(패시브 직렬화 필드 정리 — 에디터 재저장) · 문서 3개.

## 6. 리스크

| # | 리스크 | 완화 |
|---|--------|------|
| R-1 | 🔴 **보스 연출 시작 시 `ClearAllServer()` 가 버프를 지운다**(`PlayerEncounterLock.cs:97`) | **경석 수정 대기**(디버프만 제거). 그 전까지도 §4.4 `Update` 가 "쿨타임 끝 + 버프 없음" 이면 부여하므로 **연출이 끝나면 버프가 다시 붙는다**(연출 중에는 R-2 로 거부). 즉 경석 수정 전의 증상은 "연출 동안만 버프가 없다" 뿐. `PassiveCharge` 가 "버프" 로 분류되게 경석과 **분류 기준 합의** 필요 |
| R-2 | 🔴 **연출 중에는 `Apply` 가 거부된다**(`StatusEffectController.cs:152`) — 연출 도중 쿨타임이 끝나면 버프 부여가 조용히 실패 | §4.4 의 `Update` 가 "쿨타임 끝 + 버프 없음" 을 매 틱 보므로 **연출이 끝나면 자연히 부여**된다(별도 재시도 장치 불필요). 경석 수정이 "거부"도 디버프 한정으로 바꾸면 무관 |
| R-3 | 새 직렬화 필드의 기존 에셋 초기값 | §4.2 재직렬화 확인 |
| R-4 | 패시브 필드 삭제로 `Player_Paladin` 에 죽은 오버라이드가 남는다 | 에디터 재저장 후 YAML 확인 |
| R-5 | 스킬 3경로에 발행 코드를 넣다가 판정 로직을 건드림 | 발행은 판정 루프가 끝난 뒤 목록만 넘긴다. 기존 판정 코드는 수정하지 않는다 |
| R-6 | 상태이상 HUD 에 "PassiveCharge" 텍스트가 뜬다 | 의도된 노출로 둘지 HUD 에서 숨길지 — **승인 시 결정**(기본: 표시) |

## 7. 검증

1. 컴파일 0 에러(Unity).
2. 정적: `FirstMeleePassive` 참조가 base(`Player.cs`·`PassiveHUD.cs`)에서 0. 발행 지점 6곳 grep.
3. **Play(은희)** — `Dev_Boot`:
   스폰 직후 칼날 발광·HUD Ready → 평타 적중 시 첫 대상 추가피해 + 5% 회복 + 발광 꺼짐 + HUD 쿨타임 시작 →
   쿨타임 중 피격 시 감소 → 30초 후 재부여. 다수 적 동시 적중 시 **추가피해는 한 명만**. Q·우클릭·R 적중은 **소모 안 함**.
4. **MPPM 2인** — 클라 캐릭터의 발광·발동 연출이 호스트/클라 양쪽에 보이는지, 클라 HUD 쿨타임.
5. 보스 입장 후 버프 유지는 **경석 수정 반영 후** 재검증(R-1).

## 8. 범위 밖

징크스 패시브·스택 · `shieldVfx` 결합 정리 · 투사체 네트워크 스폰 · 피격자 쪽 "적중 당함" 효과 · 빌드/장비 on-hit 효과 구현(이벤트만 준비).

## 9. 승인받을 것

1. 위 계획 전체.
2. R-6 — 상태이상 HUD 에 `PassiveCharge` 를 표시할지 숨길지.
3. `ServerHitEnemiesResolved` 제거(새 이벤트로 일원화) 동의.

## 10. 구현 결과 (2026-09-29, Claude)

- 코드: `StatusEffectType`(+`PassiveCharge`, `StatusEffectCategory`/`StatusEffectCategories`) · 신규 `PlayerAttackLanded` · 신규 `IPlayerPassive` ·
  `Player`(`ServerAttackLanded`/`ServerAttackReceived`/`RaiseServerAttackLanded`, 패시브 필드 삭제) ·
  발행 6곳(`PlayerDefaultAttack` Overlap·Raycast · `DefaultAttackProjectile` · Q·우클릭·R) · `triggersOnHit`(`DefaultAttackStep` true / `PlayerSkillData` false) ·
  `FirstMeleePassive` 재작성 · `PassiveHUD` → 인터페이스 · `StatusEffectHUD` 아이콘.
- 에셋: `CombatHUD` 위젯 아이콘 6 + `defaultIcon = white_512` · `PlayerDefaultAttackData` 재직렬화(4스텝 `triggersOnHit: 1` 확인) ·
  `Player_Paladin` 재직렬화(`minHealPercent` → `healPercent: 5`, 삭제 필드 2개 제거). 에디터 임시 스크립트로 수행 — 커밋 안 함.
- Q·R 의 `hitContext.sourceUnit = owner` 채움 — 소비처는 데미지 숫자 공격자 귀속뿐(`Unit.cs:172`, `TrainingDummy.cs:139`).
- 컴파일: Unity 0 에러. 신규 경고는 직렬화 필드 CS0649(기존 필드와 같은 종류)뿐.
- ⚠️ **오프라인(비네트워크) 실행에서는 패시브가 동작하지 않는다** — `StatusEffectController` 가 스폰된 서버에서만 쓰기를 받는다. 기존 오프라인 폴백(VFXScene)은 사라졌다.
- ✅ §7-3·4 Play/MPPM **은희 검증 완료(2026-09-29)**. ⏳ §7-5(보스 입장 후 유지)는 경석 `ClearAllServer()` 수정 후.

## 11. Codex 리뷰 반영 (2026-09-29, 리뷰 58da86f0)

**추가 결정 (은희): 죽은 대상은 피격을 거절한다.**
- 원인: `Unit.ReceiveAttack` 이 사망 여부와 무관하게 `true` 를 반환해, 시체에도 피격 연출·넉백·적중 통지가 났다.
  (사망 처리 자체는 `NotifyDeathTransition` 의 `_deathNotified` 가드로 재실행되지 않는다 — 확인함)
- 수정: `Unit.ReceiveAttack` 첫 줄 `CurrentHealth <= 0 → false`. `MonsterBase`·`Enemy` 의 피격 VFX RPC 를 `resolved` 로 게이트.
  `Player` 는 거절 시 `ServerAttackReceived`·쉴드 파문을 내지 않는다. `TwentyThreeBoss` 는 base 결과를 그대로 쓰고, `TrainingDummy` 는 체력 1 하한이라 무관.
  추락 피해는 `ApplyDirectHealthDamage` 경로라 무관. 공격 측 판정(`TryResolveHit`)이 이 값을 그대로 받으므로 시체는 적중 목록·명중 연출에서도 빠진다(투사체는 시체를 관통).
- 패시브 must-fix 1: 소비 대상 = 적중 목록의 **첫 대상 그대로**(생존 필터 제거). 막타였으면 추가타만 거절되고 소모·회복은 그대로, 추가타 VFX 는 생략.

**리뷰 항목 처리**

| 항목 | 처리 |
|------|------|
| must-fix 1 패시브 대상 생존 필터 | ✅ 위 결정으로 해소 |
| must-fix 2 `MonsterSceneBossSetup` 검증 이름 비교 | ✅ 경로 상수의 에셋과 직접 비교 |
| consider 1 재사용 버퍼 | ⏸ 문서 경고로 유지(`PlayerAttackLanded.Targets` 주석) — 비동기 구독자가 생길 때 스냅샷 |
| consider 2 발행 메서드 서버 가드 | ✅ `RaiseServerAttackLanded` 에 `IsServer` 가드 |
| consider 3 문서 모순(네트워크 목록) | ✅ player-prefabs.md §0·§7·§9 — "스폰 대상 아님" 과 "NGO 자동 등록" 분리, 확인 명령에서 `DefaultNetworkPrefabs` 제외 |
| consider 4 `PlayerEncounterLockAuthoring` 이 `Paladin_VFX` 순회 | ⏸ 경석(툴 작성자) 판단 대기 |

## 12. 후속 — 초과 피해 표시 + 패시브 추가피해를 막타에 합산 (2026-09-29, ✅ 구현 · 은희 Play 검증 완료 2026-09-30)

### 확정 사항 (grill, 은희)

| 항목 | 결정 |
|------|------|
| FloatingDamage 표시 값 | **방어 적용 후, 체력 클램프 전** 최종 피해 전부. 쉴드가 먹은 분은 쉴드 숫자로 따로 |
| 적용 경로 | **모든 피해 경로**(공격·추락·최대체력 %·직접 피해) |
| 패시브 추가피해 | **막타 한 방에 합친다** — 버프 보유 시 첫 대상의 피해에 더해 한 번에 넣는다. 숫자도 합산 1개 |

### 현재 사실

- 표시 필터는 `AllDamage`(`9.ScriptableObject/UI/FloatingDamageSettings.asset` `displayFilter: 0`) — **HP NetworkVariable 의 변화량**으로 숫자를 만든다(`Unit.OnHpReplicated`, `Unit.cs:542`). 체력이 0에서 멈추므로 초과분은 **구조적으로 알 수 없다**.
- 귀속 필터용 RPC(`ClientDamagedAttributedClientRpc`)도 `previousHp - CurrentHealth`(클램프 후)를 보낸다(`Unit.cs:164`). 게다가 `ReceiveAttack` 에서만 보내서 추락·% 피해는 빠진다.
- 체력 클램프는 `Health.TakeHpDamage`(`Health.cs:20`), 방어 경감·쉴드 분배는 `Unit.ApplyMitigatedHealthDamage`.

### 접근

**A. 표시용 피해 이벤트를 피해 적용 지점 한 곳에서** (`Unit.ApplyHealthDamage`)
1. 경감·쉴드 분배 결과를 **클램프 전 값**으로 계산: `shieldDealt`, `hpDealt`(쉴드 뒤 남은 최종 피해 그대로).
2. 서버가 `ClientDamageDealtRpc(hpDealt, shieldDealt, attackerClientId)` 를 **항상** 전 피어로 보낸다
   (기존 `ClientDamagedAttributedClientRpc` 를 이것으로 대체 — 필터 플래그로 켜고 끄던 조건 제거). 이벤트는 기존 `ClientDamagedAttributed` 를 그대로 쓴다.
3. 공격자 id 는 `ReceiveAttack` 이 `TakeDamage` 호출 동안만 세팅하는 서버 컨텍스트로 넘긴다(기본값 = 없음). 추락·% 피해는 공격자 없음으로 나간다.
4. `FloatingDamagePresenter` 는 **필터와 무관하게 이 이벤트만** 쓴다. `AllDamage` = 공격자 필터 없음. HP 변화량(`ClientDamagedAmount`) 경로는 FloatingDamage 에서 뗀다 — `HitFlash`·카메라는 그대로.
5. 부수효과: 보호막 **만료**(`SetShield(0)`)가 지금은 `AllDamage` 에서 "쉴드 피해" 숫자로 떴을 것이다 — 피해가 아니므로 더 이상 안 뜬다(의도).

**B. 패시브 추가피해를 막타에 합산** (적중 **전** 훅)
1. `IPlayerOnHitBonus`(신규): `int ServerConsumeOnHitBonus(Unit target)` — 서버, 이 판정의 첫 대상에 대해 1회 호출. 보너스를 쓰면 즉시 소모 처리하고 추가 피해량을 반환, 아니면 0.
2. `Player.ServerTakeOnHitBonus(bool triggersOnHit, Unit target)` — `triggersOnHit` 이고 대상이 살아 있을 때만 제공자에게 묻는다. 제공자는 Player 의 `GetComponents<IPlayerOnHitBonus>()`.
3. 공격 6경로가 **판정의 첫 Unit 대상**의 피해에 반환값을 더한다(평타 3경로는 `overrideDamage`, 스킬 3경로는 `AttackInfo.damage`). 스킬은 플래그가 false 라 지금은 0.
4. `FirstMeleePassive`: 소모·쿨타임 시작·회복·발동 연출을 **적중 전 훅에서** 처리하고 추가 피해량만 반환. `ServerAttackLanded` 구독은 뗀다(범용 이벤트는 그대로 남는다 — 스택·빌드용).
5. 별도 추가타(`target.ReceiveAttack(bonus)`)는 삭제 — 막타 합산이라 시체 거절 문제도 사라진다.
6. "첫 대상" = 판정 루프에서 중복·자기 자신 필터를 통과한 첫 Unit(현행 `targets[0]` 과 같은 기준).

### 리스크

| # | 리스크 | 완화 |
|---|--------|------|
| R-12a | 6경로 각각에 "첫 대상에만 1회" 로직 — 누락·중복 | 경로마다 판정 시작 시 플래그 1개로 1회 보장, grep 으로 호출 6곳 확인 |
| R-12b | 적중 전에 소모했는데 판정이 거절되면 버프만 날아감 | 거절 사유는 "이미 죽음" 뿐 — `ServerTakeOnHitBonus` 가 생존을 먼저 본다 |
| R-12c | RPC 가 항상 나가면서 트래픽 증가 | 기존 귀속 필터 모드와 같은 양(피격 1회당 소형 RPC 1개). 호스트 1 + 클라 1~2 규모라 무시 가능 |
| R-12d | 기존 `ClientDamagedAttributed` 소비자(카메라 쉐이크)가 초과 피해를 받는다 | 쉐이크 세기가 피해량 비례면 막타가 더 세진다 — 확인 후 필요 시 클램프 |

### 검증
- 컴파일. Play: 체력 10 몹에 50 → 숫자 50. 쉴드 있는 대상 → 쉴드/HP 숫자 분리. 추락 피해 숫자(타 피어 화면).
- 패시브 버프 보유 평타 막타 → 합산 숫자 1개, 소모·회복·발동 연출. 다수 적중 시 첫 대상에만 합산.
- MPPM 2인 — 클라 화면에서 동일.

### §12 구현 결과 (2026-09-29, Codex — 3번째 위임 a88e4f8f 에서 완료, Claude 검토)

- 위임 경과: 7a634d88 크레딧 소진으로 A 도중 중단 → 2846663c 워처가 옛 `codex.exe` 경로(업데이트로 삭제)를 잡고 있어 기동 실패 → 워처 재시작 후 a88e4f8f 완료.
- **A `11d46701`** — `Unit.ApplyHealthDamage` 가 클램프 전 `hpDealt`/`shieldDealt` 를 `ClientDamageDealtClientRpc` 로 항상 발송(모든 피해 경로). 공격자 id 는 `ReceiveAttack` 이 `TakeDamage` 동안만 세팅.
  `FloatingDamagePresenter` 는 이 이벤트만 사용(`ClientDamagedAmount` 미사용), `RequiresAttributedDamageRpc` 2개 삭제.
  카메라: 가한 피해 쉐이크는 **피해량 비례가 아니라 고정 진폭 + 최소 간격 가드**라 초과 피해로 세지지 않음. 로컬 피격 쉐이크는 기존 `ClientDamagedAmount` 유지.
- **B `4f5d8433`** — `IPlayerOnHitBonus` + `Player.ServerTakeOnHitBonus`(서버·`triggersOnHit`·생존 대상만). 호출 6곳(평타 Overlap·Raycast·투사체, Q·우클릭·R) — 판정당 1회, 첫 Unit 대상이 죽었으면 다음으로 넘기지 않음.
  `FirstMeleePassive` 가 훅에서 소모·쿨타임·회복·연출 처리 후 추가 피해량 반환 → 원래 공격 피해에 합산(`overrideDamage`/`AttackInfo.damage`). 별도 추가타·`ServerAttackLanded` 구독 삭제(범용 이벤트는 유지).
- 빌드: `dotnet build Assembly-CSharp-Editor` 오류 0(기존 경고 17). Unity 에디터 컴파일·Play 는 미확인.

## 13. 독립 코드리뷰 반영 (2026-09-30, Codex 7e449670 — 기존 코드·의도 배제 조건)

| 항목 | 판단 | 처리 |
|------|------|------|
| must-fix 1 시체가 "첫 대상" 자리를 차지 — 물리 쿼리 순서상 시체가 먼저 나오면 뒤의 생존 적에게 보너스 불발 | 버그 | ✅ 평타 Overlap·투사체·Q·우클릭 4곳에서 **살아 있는 첫 Unit** 만 첫 대상으로 센다 |
| consider 1 `HasCharge` 는 타입만, `Remove` 는 (type, sourceId) | 잠재 버그 | ✅ `HasCharge` 를 `GetStackCount(PassiveCharge, 자기 id) > 0` 으로 |
| consider 2 비활성 제공자도 호출됨 | 잠재 버그 | ✅ `isActiveAndEnabled` 검사 |
| consider 3 `ServerAttackLanded` 구독자 0 | 사실 | ⏸ **유지(은희 결정)** — 범용 적중 이벤트(징크스 스택·빌드용) |
| consider 4 `NetworkLoadingFlowController.cs.meta` 기본 참조가 base `Player.prefab` | 버그(새 컴포넌트·Reset 시 빈 base 스폰) | ✅ 기본 참조를 `Player_Paladin` 으로 |
| consider 5a 표시값이 실제 HP 감소가 아니라 클램프 전 값 | 의도(§12 결정) | — |
| consider 5b 직접·비율 피해 API 가 시체에 호출되면 HP 변화 없이 숫자만 뜸 | 버그 | ✅ `Unit.ApplyHealthDamage` 에 사망 가드 — 모든 피해 경로에서 "죽은 대상은 피해 거절" |
| note `StatusEffectCategories` 호출자 0 | 의도 | — 경석 `ClearAllServer()` 수정용 API |
| note `DefaultNetworkPrefabs` 에 base·Legacy 등록 | NGO 자동 생성 | — 무해(해시 상이) |

빌드: `dotnet build Assembly-CSharp-Editor` 오류 0.
