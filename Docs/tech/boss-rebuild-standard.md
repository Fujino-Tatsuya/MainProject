# 보스 재작성 표준 — "일반 몬스터를 찍어내듯 보스도"

> 작성 2026-08-07 · **독립 4레인 교차 조사 종합**
> (Claude opus ×2 — 플레이어 규약 / 몬스터 규약 · Codex `gpt-5.5` · Codex `gpt-5.6-sol`)
> 네 레인 모두 **보스 코드와 BT 를 못 보게 막고** 조사했다. 버릴 코드에 오염되면 안 되기 때문이다.
>
> 이 문서가 **보스 구현의 정본**이다. [boss-fsm-detailed-spec.md](boss-fsm-detailed-spec.md) 의
> §1.1(15상태 단일 enum)·§4(15상태 전수표)는 **이 문서로 대체된다.**
> 폭탄·장판·카운터 창의 세부는 그 문서에 그대로 남아 있다.

---

## 0. 결론 — 이미 답이 프로젝트 안에 있었다

🔴 **중간보스 `WallBot` 은 C# 코드가 0줄이다.** `MonsterBase` + `MonsterDataSO` 값만으로 만들어졌다
(HP 600 / 방어 8 / 공격 중 슈퍼아머 / 그로기 4회 / `attackFinishTrigger` 로 2단 공격).

**"찍어내듯 만든다"는 목표가 아니라 이미 존재하는 실증이다.** 23호만 BT 라서 혼자 다른 길로 갔다.

그리고 중간보스 3종 중 **어느 것도 `MonsterState` 에 값을 추가하지 않았다.** 확장은 전부
**virtual 훅 4개** 안에서 끝났다. 팀장이 지시한 6상태 구조가 기존 규약과 정확히 일치한다.

---

## 1. 3층 구조

```
BossState   = Idle, Walk, Attack, Hit, Groggy, Dead        ← FSM 상태 (복제)
AttackId    = LeftHook, RightHook, Upper, Grab, Jump, Dash ← 공격 종류 (복제)
AttackPhase = Windup, Acquire, Hold, Throw, Recovery       ← 다단계 공격 내부 (필요시 복제)
```

- **공격마다 상태를 늘리지 않는다.** `Attack` 하나에서 `AttackId` 로 분기한다 — `GauntletBot` 선례.
- **잡기 체인은 `Attack` 안의 phase** 다 — `SpinnerBot` 선례(준비→돌진→Dizzy 를 Attack 안에서 처리).
- ⚠️ **FSM 상태(6) ≠ 애니메이터 `State` Int(15값).** 애니메이터는 이미 15값으로 돌고 있으므로
  `(BossState, AttackId)` → 애니 파라미터 **매핑 계층**을 둔다. 애니메이터를 6개로 줄이지 않는다.

### 1.1 기존 `MonsterState` 와의 대응

| `MonsterState`(8) | 보스(6) | 처리 |
|---|---|---|
| `Idle` | `Idle` | 그대로 |
| `Chase` | **`Walk`** | 이름만 다름 |
| `Attack` | `Attack` | 그대로 |
| `Hit` | `Hit` | 그대로. **보스는 카운터 성공에서만 진입**(§4) |
| `Groggy` | `Groggy` | 그대로. Break = 같은 상태에 **지속시간만 다름** |
| `Return` | — | **안 만든다.** 보스룸 이탈 불가 |
| `Knockback` | — | **안 만든다.** 보스는 안 밀린다 |
| `Dead` | `Dead` | 그대로 |

---

## 2. 확장 표면 — 손댈 곳은 훅 4개뿐

중간보스 3종이 전부 이 안에서 끝냈다.

| 훅 | 언제 override | 선례 |
|---|---|---|
| `StartAttack()` | **무엇을 공격할지 고를 때** | `GauntletBot:102` 근접 인원수 → 가중치 룰렛 → 7종 확정 |
| `HandleAttack(dt)` | **Attack 안에 서브 시퀀스를 접을 때** | `SpinnerBot:89` 준비→돌진→Dizzy 3단계 |
| `PerformAttackHit()` | **히트가 단타가 아닐 때**(AoE/투사체/CC) | `GauntletBot:119` Smash 자체 AoE |
| `PlayStateAnimation(s)` | **다지선다 애니를 ClientRpc 로 돌릴 때** | 둘 다 `if (s == Attack) return;` + ClientRpc CrossFade |

**base 가 이미 해 주는 것 (건드리지 말 것)**: 이동·추격·리쉬·타게팅·쿨다운·피격·그로기·넉백·
사망·디스폰·상태복제·HitFlash.

### 2.1 관용구 3개

1. **`base.StartAttack()` 을 부르되 선택 결과는 그 전에 확정한다.**
   base 가 `SetState(Attack)` 까지 하므로, `PlayStateAnimation` 이 참조할 `_currentAttack` 이
   이미 있어야 한다.
2. **다지선다 애니는 상태 복제로 못 싣는다** → `PlayStateAnimation(Attack)` 을 스킵하고
   `[ClientRpc] SafeCrossFade(상태명)` 으로 보낸다.
3. **Attack 안에 서브 시퀀스를 접을 땐 `_stateTimer` 를 통째로 덮어쓰고** 자체 elapsed 로 phase 를 나눈다.

---

## 3. 반드시 따라야 하는 규약

### 3.1 상태

- **전이 단일 지점은 `SetState()` 하나.** `_state.Value` 에 쓰는 곳은 그 한 곳뿐이다.
  서브클래스는 못 부른다(private) — `DecideNextAfterAction()` 과 `ForceGroggy()` 만 열려 있다.
- **Enter = 전용 `EnterXxx()`, 마지막 줄이 `SetState()`.** Stay = `TickServer` 의 단일 switch.
  **Exit 훅은 없다** — `OnStateChanged` 안의 애니 정리(`ResetToLocomotion`)가 유일한 대체물이다.
- **행동 종료는 전부 `DecideNextAfterAction()` 하나로 모인다** → 무조건 `Idle` → 다음 틱 재평가.
- 복제: `NetworkVariable<BossState>`(Read=Everyone / Write=Server). **`NetworkAnimator` 안 쓴다** —
  몬스터 프리팹 전수에 없다. 상태 복제 + `OnValueChanged` 로컬 재생이 규약이다.
  ⚠️ 단 현 `TwentyThree.prefab` 에는 `NetworkAnimator(AuthorityMode: 0)` 가 붙어 있다 →
  **재작성 시 제거**하고 몬스터 규약으로 통일한다.

### 3.2 애니메이션

- **파라미터 이름을 코드에 박지 않는다.** 전부 `MonsterDataSO` 의 문자열 필드다
  (`animSpeedParam`/`attackTrigger`/`attackFinishTrigger`/`hitTrigger`/`groggyBool`/
  `deathTrigger`/`locomotionState`).
- **상태→애니 매핑은 `PlayStateAnimation` 한 곳에만.**
- **접근은 전부 graceful** — `HasParameter` / `HasState` 확인 후에만 세팅한다.
  🔴 **그래서 이름이 틀려도 조용히 무시된다.** 실제로 죽은 설정값이 여럿 있다(§6).
  → **`Awake` 에서 애니메이터 계약을 검증해 `LogError` 를 남겨라.**
- 액션→로코모션 전이 시 `ResetToLocomotion()` 으로 액션 트리거를 `ResetTrigger` 하고 CrossFade.

### 3.3 애니메이션 이벤트 — 이름 3개 고정

**`OnAttackHit` / `OnAttackCommit` / `OnAttackEnd`**

- 릴레이(`MonsterAnimationEventRelay`)는 **프리팹에 붙이지 않는다. 런타임 자동 부착**이다
  (`animator.gameObject.AddComponent<...>()`). 릴레이가 `GetComponentInParent` 로 두뇌를 찾는다.
- 수신 가드 3개 동일: **`if (!IsServer || _state.Value != Attack) return;`**
- **히트가 커밋을 겸한다** — `attackFinishTrigger` 가 있고 아직 커밋 안 됐으면 히트가 대신 쳐 준다.
  WallBot 2단 공격이 이 경로다(별도 Commit 이벤트 없음).

#### 🔴 폴백은 비대칭이다

```
OnAttackEnd 누락  → attackDuration 타이머로 재판단.  필수.
OnAttackHit 누락  → 히트 없음.  폴백을 넣으면 이벤트 추가 후 두 번 맞는다.
```

**`attackDuration` 은 "모션 길이"가 아니라 데드락 방지 타임아웃이다.** 넉넉히 잡아라.

#### `End` 는 `exitTime` 앞에 — 실측 전수 일치

| 몹 | `OnAttackHit` | `OnAttackEnd` | exitTime | 마진 |
|---|---|---|---|---|
| ChompBot / HumanoidBot / PeekABot | 0.35~0.4 | 0.95 | 1.0 | 0.05 |
| MortarBot AttackShoot | 0.35 | 0.7 | 0.8 | 0.1 |
| WallBot AttackEnd | — | 0.6 | 0.674 | 0.074 |
| SpinnerBot Whip | 0.5 | 0.6 | 0.65 | 0.05 |
| GauntletBot Smash | 0.45 | 0.7 | 0.8 | 0.1 |

**단 하나도 1.0 에 놓여 있지 않다.** 0.05~0.1 앞이 하우스 스타일이다.

⚠️ **액션 상태의 복귀 전이를 트리거 조건만으로 걸지 마라.** GauntletBot 펀치 6종이
`FinishedCombo` 트리거로만 탈출하게 돼 있는데 **코드가 그 트리거를 어디서도 안 친다** —
실질 탈출구가 `ResetToLocomotion()` 의 CrossFade 뿐이다. **exitTime 을 반드시 걸어라.**

### 3.4 판정 / 권한

- **데미지 유입은 `BaseAttack → ReceiveAttack → TakeDamage(AttackInfo)` 서버 경로만.**
- 판정은 **한 순간의 `Physics.Overlap*NonAlloc`** 이다(지속 트리거 아님).
  지속 히트가 필요하면 `meleeAttack.BeginHitWindow()` / 매 틱 `Hit()` / `EndHitWindow()` —
  열려 있는 동안 유닛당 1회를 보장한다(SpinnerBot 선례).
- AoE 는 `HashSet<Unit>` 으로 유닛당 1회 + 자기 자신 제외.
- 서버: FSM·타게팅·이동·`AttackId`·`AttackPhase`·히트 판정·데미지·그로기·사망·투사체/장판 생성.
  클라: 애니·VFX·SFX만.

#### 🔴 `Unit.TakeDamage(AttackInfo)` 는 CC 필드를 안 읽는다

`knockbackStrength` / `knockbackDuration` / `staggerDuration` / `knockbackDirection` 을
**전혀 소비하지 않는다** — `damage` 만 쓴다. CC 반응은 **수신측이 직접 구현**해야 한다
(`MonsterBase.ReceiveAttack` 이 넉백 지시만 따로 해석하는 게 그 예다).
→ 돌진 캐리·어퍼 에어본 설계에 직접 영향.

#### ⚠️ `Unit.ReceiveAttack` 은 무조건 `true` 를 반환한다

피해가 `CanApplyHealthDamage` 에서 조용히 버려져도 공격 측은 "적중" 로그를 찍는다.

---

## 4. 보스 상태 전수표

`MonsterBase` 규약 위에 보스 고유 결정(카운터·페이즈)을 얹은 것이다.

| 상태 | Enter | Stay | 탈출 |
|---|---|---|---|
| **Idle** | `StopAgent()` · 타겟 해제 | 타겟 탐색(Soul 제외) | 타겟 有 → `Walk` |
| **Walk** | — | 추적 · `FaceTarget` · **선택기 실행** | `AttackId` 선택됨 → `Attack` / 타겟 無 → `Idle` |
| **Attack** | `AttackId` 확정 → `StopAgent` · `FaceTarget` · 쿨 기록 · (창 여는 공격이면) 카운터 창 Open | `AttackId` 별 분기. Grab 은 `AttackPhase` 진행 | `OnAttackEnd` → `DecideNextAfterAction()` |
| **Hit** | 🔴 **카운터 성공에서만.** 진행 중 공격 취소 · `getowned` 재생 | 타이머 | 종료 → `Groggy` (또는 `Break` 지속) |
| **Groggy** | `GroggyCount += 1` · `StopAgent` | 타이머(2초 / Break 5초) | 종료 → `DecideNextAfterAction()` |
| **Dead** | agent·콜라이더 off → `OnDeath()` → 디스폰 | — | — |

**`Hit` 는 일반 피격에 진입하지 않는다.** 기존 몬스터는 `Attack` 이 제외 목록에 없어서
공격 중 피격이 공격을 취소하지만, **보스는 카운터 전용**이다(회의 확정). 일반 피격은 색 변경만.

### 4.1 🔴 사망을 FSM 상태로 둘지 별도 생명주기로 둘지 — 하나만 골라라

플레이어 쪽에 실패 사례가 있다. **`PlayerActionState.Dead` 는 도달 불가능한 죽은 상태다** —
진입 호출이 코드베이스 전체에 **0건**인데 **5곳이 `CurrentState == Dead` 를 가드로 읽는다**(전부 항상 false).
실제 사망은 `PlayerLifeCycleController` 의 별도 `NetworkVariable` 이 담당한다.

→ 보스는 **`BossState.Dead` 하나로 통일**한다(몬스터 규약대로). 별도 생명주기를 만들지 않는다.

---

## 5. 레이어 표준

### 5.1 현황 (4레인 교차 확인)

```
0 Default · 3 Ground · 4 Water · 6 Player · 7 Wall · 8 Enemy · 9 HazardArea
10 Projectile · 11 Env · 12 Weapon · 13 PlayerHurtbox · 14 EnemyHurtBox
15 Corpse · 16 Soul        (17~31 미명명)
```

**충돌 매트릭스는 사실상 전면 허용이다**(`Corpse`/`Soul` 만 제한). 대상 구분은 전부 코드 `LayerMask`가 한다.

### 5.2 몬스터 표준 (8종 전수 동일)

| 오브젝트 | 레이어 | 콜라이더 |
|---|---|---|
| 루트 | **8 Enemy** | Capsule **solid** |
| `Hurtbox` 자식 | **14 EnemyHurtBox** | Capsule **trigger** + `Hurtbox` |
| `MeleeHitbox` 자식 | 0 Default | trigger + `ColliderInfo` (판정에 직접 안 쓰임 — 형상만 제공) |

**보스도 이 구조를 그대로 따른다.** `Hurtbox` 자식이 **없으면 플레이어 공격이 안 맞는다.**

### 5.3 🔴 불일치 5건

| # | 문제 | 조치 |
|---|---|---|
| 1 | **`ChompBot` 이 이름 없는 레이어 19·21 을 쓴다** (다른 7종은 안 그럼) | 렌더 전용이면 `Default`, 본체면 `Enemy` |
| 2 | **Wells 루트가 `Default(0)`** 인데 23호 루트는 `Enemy(8)` | Wells 가 전투 본체가 아니므로(피격 대상 아님) **현행 유지 가능**. 단 의도임을 문서화 |
| 3 | **`MonsterBase.playerMask` 하나가 탐색과 공격 판정에 겸용** | 🔴 **분리해야 한다** — §5.4 |
| 4 | 투사체 프리팹이 `Default(0)` (`Projectile(10)` 아님) | `Projectile` 로 이관 |
| 5 | `Weapon(12)` 이 어디에도 일관 적용 안 됨 | 폐기하거나 "공격 히트박스 앵커"로 재정의 |

### 5.4 🔴 탐색 마스크와 피해 마스크를 분리하라

지금은 `playerMask`(= `Player(6)` 단독) 하나가 `MonsterBase` 의 탐색에도, `BaseAttack.targetLayer`
에도 그대로 들어간다(`SetTargetLayer(playerMask)`).

**그 결과 비대칭이 생겨 있다:**

```
플레이어 → 몬스터 : EnemyHurtBox(14) 를 친다   ← Hurtbox 경유
몬스터 → 플레이어 : Player(6) 루트 캡슐을 친다  ← Hurtbox 안 거침
```

보스는 **`targetAcquisitionMask`(Player/Enemy) 와 `damageMask`(PlayerHurtbox/EnemyHurtBox) 를
나눠서** 갖는다.

### 5.5 이관 위험 — 한 번에 하지 말 것

1. 🔴 **레이어 번호를 삽입·재정렬하면 직렬화된 `m_Bits` 의미가 전부 바뀐다.** 이름 변경은
   `NameToLayer`/`GetMask` 문자열 조회를 깨뜨린다. **번호는 건드리지 않는다.**
2. 🔴 전환기에 본체+허트박스를 함께 조회하면 **한 공격이 두 번 맞는다.** 플레이어 평타는 dedup 이
   있지만 몬스터 공격은 히트 윈도우 단위라 없다.
3. 플레이어 공격 마스크의 `Projectile(10)` 을 빼면 **투사체 상쇄가 깨진다**.
4. `Enemy(8)` 는 차징 기둥에도 쓰인다(`BossRoomAuthoring`) — 본체 전용으로 바꾸려면 함께 이관.
5. `GroundProbe` 는 레거시 맵 때문에 **의도적으로 `Default + Ground`** 를 허용한다.
6. **레이어 이관과 충돌 매트릭스 축소를 동시에 하지 마라.**

---

## 6. 🔴 죽은 설정값 — 조용히 무시되고 있는 것들

애니 접근이 전부 graceful 이라 **이름이 틀려도 에러가 안 난다.** 실제로 이미 여럿 있다.

| # | 내용 |
|---|---|
| 1 | **`deathTrigger: "Death"` 가 7종 컨트롤러 어디에도 없다.** GauntletBot 만 `Defeat` 로 실존 → 사망 애니는 사실상 플레이스홀더(축소+빨강 틴트)만 돈다 |
| 2 | **`groggyBool` 도 대부분 없다.** SpinnerBot(`IsDizzy`)만 실배선. **WallBot 은 그로기 4회로 활성인데 애니 표현이 0** |
| 3 | GauntletBot 의 `attackTrigger: "Attack"` 은 실재하지 않는 파라미터(실제는 `AttackSmash`) |
| 4 | `hitTrigger` — HumanoidBot/GauntletBot 은 미배선, PeekABot/TeslaBot 은 파라미터 자체가 없음 |
| 5 | **TeslaBot 클립↔상태가 뒤집혀 있다** — 히트가 발사 상태가 아니라 **차징 상태**에서 난다 |
| 6 | `MonsterDataSO.attackWindup` 은 base 에서 **미사용**(MortarBot 이 3.5 로 세팅해 놨지만 무효) |
| 7 | `Controller_GauntletBot.controller` 를 아무도 참조하지 않는다(실제는 중첩 프리팹 오버라이드) |
| 8 | `RangedMobileData.asset` / `RangedTurretData.asset` — 참조 0 |
| 9 | **`LinearKnockback` 이 전 몹 프리팹에 붙어 있으나 `m_Enabled: 0`** — 몹의 실제 넉백은 서버 틱 경로. **두 넉백 시스템이 공존** |

→ **보스는 `Awake` 애니메이터 계약 검증을 반드시 넣는다.** 이 목록이 그 필요성의 증거다.

---

## 7. 보스에 없어서 **새로 만들어야** 하는 것

`MonsterBase` 규약으로 안 되는 것들이다. 이게 서브클래스의 실제 작업량이다.

| 필요 | 현황 |
|---|---|
| **페이즈 / HP 임계 전환** | `MonsterBase` 에 없음. `BossBase.EvaluatePhase` 가 참고 구현이나 **미사용 스켈레톤** |
| **공격별 개별 쿨다운** | 없음(`attackSpeed` 단일 쿨). SpinnerBot 자체 필드가 유일 선례 → Jump 10s·Dash 5s·Grab 10s 를 위해 필요 |
| **거리창 + 가중치 선택기** | `BossBasicAttackChoice` 가 있으나 미사용 + 🔴 **버릴 `Enemy/Boss` 디렉터리를 상속**(`BaseAttackChoice`·`WeightedAttack<T>`) → 재작성 필요 |
| **카운터 창 + 정면 판정** | 전례 없음. 신규 |
| **Dash / Jump 이동형 특수기** | 전용 상태 없음. SpinnerBot 돌진이 Attack 안에 접은 유일 사례 |
| **보스 상태 브로드캐스트(UI/연출)** | `BroadcastBossState` 는 no-op virtual |

---

## 8. 조립 체크리스트

**1순위 — WallBot 경로를 먼저 시도한다.** 코드 없이 SO 만으로 되는 부분은 그렇게 한다.

- [ ] 루트 layer **8 Enemy** + `NetworkObject`·`NetworkTransform`·`Rigidbody`·solid Capsule·
      `NavMeshAgent`·`MonsterStatusEffect`·보스 서브클래스
- [ ] **`Hurtbox` 자식 layer 14** + trigger collider + `Hurtbox` ← 없으면 안 맞는다
- [ ] `MeleeHitbox` 자식 + `ColliderInfo` + `MonsterMeleeAttack`
- [ ] **`MonsterAnimationEventRelay` 를 붙이지 말 것** — 런타임 자동 부착
- [ ] **`HitFlash` 도 붙이지 말 것** — `Unit.OnNetworkSpawn` 자동 부착
- [ ] `NetworkAnimator` **제거** (몬스터 규약은 상태 복제 + 로컬 재생)
- [ ] 인스펙터 수기 입력은 `data`(SO) + 마스크뿐. 나머지 참조는 비우면 자동 탐색
- [ ] SO: 스탯 5종 / detectionRadius·attackRange / **`attackDuration` = 데드락 타임아웃(넉넉히)** /
      애니 파라미터명 7개 / `maxGroggyCount` / `hasSuperArmorWhileAttacking`
- [ ] 애니 클립: `OnAttackHit`(타격 프레임) + `OnAttackEnd`(**exitTime 0.05~0.1 앞**)
- [ ] 액션 상태 복귀 전이에 **exitTime 을 반드시 걸 것**
- [ ] `Awake` 애니메이터 계약 검증 → `LogError`
- [ ] `NetworkManager` NetworkPrefabs 등록

**베낄 가치가 있는 플레이어 규약 2개**

- [ ] `[CallerMemberName/FilePath/LineNumber]` 기반 **`LastTransitionCause`** — "누가 내 상태를
      끊었나"가 로그 한 줄로 확정된다
- [ ] `[SerializeField] currentStateDebug` — 인스펙터에서 현재 상태를 눈으로 확인

---

## 9. 교차 검증 — 기각·정정된 것

| 주장 | 출처 | 판정 |
|---|---|---|
| `17664 = Enemy + **Weapon** + EnemyHurtBox` → "Weapon 을 마스크에서 제거해야" | Codex `gpt-5.5` | ❌ **산술 오류.** 실측 `17664 = Enemy(8) + Projectile(10) + EnemyHurtBox(14)`. `Weapon` 은 애초에 없다 → 권고 폐기 |
| 프리팹 경로 `Assets/2.Prefabs/Monster/Boss/TwentyThree.prefab` 등 | Codex `gpt-5.6-sol` | ❌ **경로 지어냄.** 실제는 `Assets/2.Prefabs/Wells&No.23/`. 중복 프리팹 없음(전수 확인). **내용은 맞지만 경로는 믿지 말 것** |
| 프리팹 인스펙터 공격 마스크 = `256`(Enemy만) | Claude 플레이어 레인 (코드 주석 인용) | ⚠️ 실측은 `17408`(Projectile+EnemyHurtBox). 주석이 낡았을 가능성 |
| `MonsterBase.playerMask` 가 탐색·판정 겸용 | Codex `5.6-sol` + Claude 몬스터 레인 | ✅ **양쪽 독립 확인** |
| `ChompBot` 미명명 레이어 19·21 | Codex 양쪽 + Claude 몬스터 레인 | ✅ **3레인 확인** |

**모델 정확도 소감**: `gpt-5.6-sol` 이 `gpt-5.5` 보다 내용 정확도가 높았으나(마스크 계산 정확),
**둘 다 파일 경로를 지어내는 경향**이 있었다. Claude 레인 2개는 경로가 정확했다.
→ **Codex 결과는 결론을 채택하되 경로·줄번호는 재확인**하는 것이 맞다.


---

## 10. 보스 SO 설계 (확정 2026-08-07)

**방향**: 보스를 밀어버리고 몬스터 체계에 편입한다. 데이터 주도로 간다.
팀장 확정 3건 — ① `BossDataSO : MonsterDataSO` **파생** ② `MonsterArchetype` 에 **`Boss` 추가**
③ 공격별 개별 쿨다운을 **`MonsterBase` 로 승격**.

### 10.1 `MonsterArchetype` 에 `Boss` 추가

```csharp
public enum MonsterArchetype { Melee = 0, RangedTurret = 1, RangedMobile = 2, Boss = 3 }
```

🔴 **끝에만 추가한다** — SO 에 정수로 직렬화돼 있어 중간 삽입 시 기존 몹의 아키타입이 밀린다.

`MonsterBase` 의 Seek 분기에 한 줄이 는다:

```
case MonsterArchetype.Boss: SeekBoss(dist); break;
```

`SeekBoss` 는 거리창 + 가중치 + 연속 감쇠 + 폴백(§3 선택기)을 돌린다.

### 10.2 공격별 쿨다운을 base 로 승격 — ✅ **구현 완료 (2026-08-07)**

실제로 들어간 표면 (`MonsterBase`):

```csharp
protected const int DefaultAttackSlot = 0;
protected const int NoAttack = -1;
protected int  CurrentAttackSlot { get; set; }          // StartAttack 이 이 슬롯에 쿨 기록
protected bool CooldownReady(int attackSlot = 0)
protected void ConfigureAttackSlots(int count)          // 파생이 스폰 시 1회
protected void SetAttackCooldown(int slot, float sec)   // 0 이하면 1/AttackSpeed 폴백
protected virtual int SelectAttackSlot(float dist)      // -1 = 지금 쓸 게 없다
```

**하위호환 근거** — 기존 몬스터 8종의 동작이 바뀌지 않는 이유:

- 기존 `CooldownReady()` 호출 5곳이 전부 **무인자** → 슬롯 0
- `CurrentAttackSlot` 기본 0이고 **기존 파생(Gauntlet·Spinner)은 이 프로퍼티를 모른다**
- `_cooldownByAttack[0] = 0` → `1f / AttackSpeed` 로 폴백 = **옛 공식 그대로**
- `_lastUsedByAttack[0] = -999f` 초기값 = 옛 `_lastAttackTime` 과 동일



**지금**: `CooldownReady() => Time.time - _lastAttackTime >= 1f / AttackSpeed` — 단일 쿨.
공격이 여러 종류여도 하나의 쿨을 공유한다.

**바꿀 것**:

```csharp
float[] _lastUsedByAttack;                       // 크기 = 공격 종류 수 (없으면 1)
protected bool CooldownReady(int attackId = 0)   // 기본값 0 → 기존 몹은 동작 동일
```

- **하위호환이 핵심이다.** 일반 몹 5종·중간보스 3종은 `attackId` 를 안 넘기므로 인덱스 0 하나만
  쓰고 지금과 똑같이 동작한다. 회귀 위험을 여기서 끊는다.
- 엔트리별 `cooldown` 이 `0` 이면 base 의 `1f / AttackSpeed` 로 폴백한다.

### 10.3 `BossDataSO : MonsterDataSO`

일반몹 SO 를 오염시키지 않으려고 파생으로 간다. 이미 `attackWindup`·`maxShield` 같은
**죽은 필드가 있는 프로젝트**라 더 늘리지 않는다.

```
BossDataSO : MonsterDataSO
─────────────────────────────────────────────────────────────
[공격 테이블]   BossAttackEntry[] attacks
    attackId             BossAttackId   (LeftHook/RightHook/Upper/Grab/Jump/Dash)
    animatorStateName    string         ← ClientRpc CrossFade 대상
    cooldown             float          ← 0 이면 base attackSpeed 폴백
    minDistance          float
    maxDistance          float
    ignoreDistanceWindow bool           ← JumpAttack 전용(거리 무관)
    targetRule           enum { CurrentTarget, FarthestPlayer }
    weight               float
    allowedFromPhase     int
    damage               int
    opensCounterWindow   bool           ← Grab·Dash 만 true
    superArmor           bool
    hitboxAnchorName     string         ← ColliderInfo 앵커 이름

[선택기]        repeatPenalty(0.3) · meleeRange · rangedThreshold

[페이즈]        BossPhaseEntry[] phases
    hpThreshold          float          (0.66 / 0.33)
    sequence             enum { None, ChargeSequence }
    damageMultiplier · speedMultiplier

[카운터/그로기]  counterFrontAngle(±60) · hitReactionState(getowned) · breakDuration(5)
                ※ maxGroggyCount(5) · groggyDuration(2) 는 base 에 이미 있다 — 재사용

[Wells]         bombThrowInterval · bombPrefab · throwImpulse · spreadAngle
[폭탄/장판]      §boss-fsm-detailed-spec.md §11 스키마 참조
```

⚠️ **`hitboxAnchorName` 은 문자열이다** — 애니 파라미터명과 같은 규약(SO 문자열 + graceful).
그래서 **오타가 조용히 무시된다.** §3.2 대로 **`Awake` 에서 앵커 실존을 검증해 `LogError`** 를 남긴다.
이 프로젝트에 이미 죽은 설정값이 9건 있다(§6) — 같은 함정을 또 파지 않는다.

### 10.4 서브클래스가 채우는 것 — 훅 4개

| 훅 | 보스가 하는 일 |
|---|---|
| `StartAttack()` | 선택기로 `AttackId` 확정 → `base.StartAttack()` → ClientRpc CrossFade |
| `HandleAttack(dt)` | Grab 체인(`AttackPhase`) · Dash 캐리 · Jump 시퀀스 |
| `PerformAttackHit()` | `AttackId` 별 히트(단타 / AoE / 캐리 시작) |
| `PlayStateAnimation(s)` | `if (s == Attack) return;` — 나머지는 base |

**§2.1 관용구 3개를 그대로 따른다.** 특히 선택 결과는 `base.StartAttack()` **전에** 확정할 것.

### 10.5 §7 의 6가지를 어디에 두는가

| 필요 | 위치 |
|---|---|
| 페이즈 / HP 임계 전환 | 보스 서브클래스 + `BossDataSO.phases` |
| **공격별 개별 쿨다운** | 🔴 **`MonsterBase` 로 승격**(§10.2) |
| 거리창 + 가중치 선택기 | 보스 서브클래스 `SeekBoss()`. **기존 `BossBasicAttackChoice` 는 폐기** |
| 카운터 창 + 정면 판정 | 보스 서브클래스 + `Hit` 상태 |
| Dash / Jump 이동형 | `HandleAttack` 안 (SpinnerBot 돌진 선례) |
| 상태 브로드캐스트 | 별도 채널 불필요 — `_state` `OnValueChanged` 로 충분 |

### 10.6 폐기 대상

```
Assets/1.Scripts/Monster/Boss/BossBase.cs
Assets/1.Scripts/Monster/Boss/BossState.cs
Assets/1.Scripts/Monster/Boss/BossBasicAttackType.cs
Assets/1.Scripts/Monster/Boss/BossBasicAttackChoice.cs
```

**전부 미사용**(프리팹·씬 부착 0건)이고, `BossBasicAttackChoice` 는 **버리기로 한
`Enemy/Boss` 디렉터리의 `BaseAttackChoice`·`WeightedAttack<T>` 를 상속**한다.
재사용이 불가능하므로 선택기는 새로 짠다.

`Assets/1.Scripts/Enemy/Boss/**` 와 `Assets/8.BehaviorTreeGraph/Boss/**` 는 전환 검증 후 삭제.

### 10.7 새 보스를 만드는 절차 (WallBot 경로 적용)

1. `Create > Monster > Boss Data` 로 `BossDataSO` 1개
2. 프리팹 조립 — §8 체크리스트 (루트 `Enemy(8)` / `Hurtbox` 자식 `EnemyHurtBox(14)` /
   공격 앵커 **`Weapon(12)`** ← 레이어 이관 완료)
3. `archetype = Boss`, 공격 테이블 6행 작성
4. 애니 클립에 `OnAttackHit` / `OnAttackEnd`(exitTime 0.05~0.1 앞) — **SVN 커밋**
5. 서브클래스는 훅 4개만 override
6. `Awake` 계약 검증(애니 파라미터 + 앵커 이름) → `LogError`
