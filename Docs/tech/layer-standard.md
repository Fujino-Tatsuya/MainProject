# 레이어 표준 — 확정안

> 작성 2026-08-07 · 경석 · **전수 실측 기반**(프리팹 전체 스캔 + 콜라이더 상태 확인).
> 보스 재작성의 선행 작업이다. [boss-rebuild-standard.md](boss-rebuild-standard.md) §5 의 확정판.
>
> 🔴 **Codex 레인 2개가 이 영역에서 오류를 냈다.** 아래 수치는 전부 **내가 직접 실측한 것**이고
> Codex 주장과 다른 부분은 §6 에 정정으로 남겼다.

---

## 1. 왜 지금 하는가

보스와 일반 몬스터를 **다른 사람이 각각 구현해서 레이어 정리가 서로 다르다.** 보스를 재작성하기
전에 맞춰 두지 않으면 새 코드가 낡은 불일치 위에 얹힌다.

그리고 이미 이 불일치로 **버그가 났었다** — §3.1 참조.

---

## 2. 실측 현황

### 2.1 레이어 정의 (`ProjectSettings/TagManager.asset`)

```
 0 Default        6 Player        12 Weapon           17~31 미명명
 1 TransparentFX  7 Wall          13 PlayerHurtbox
 2 Ignore Raycast 8 Enemy         14 EnemyHurtBox
 3 Ground         9 HazardArea    15 Corpse
 4 Water         10 Projectile    16 Soul
 5 UI            11 Env
```

⚠️ **`PlayerHurtbox`(소문자 b) vs `EnemyHurtBox`(대문자 B)** — 문자열 조회 시 주의.

### 2.2 프리팹 전체 레이어 사용 빈도 (실측)

| 레이어 | 오브젝트 수 | 비고 |
|---|---:|---|
| 0 Default | **1496** | 압도적 다수. 뼈·모델·앵커 전부 |
| 3 Ground | 826 | 맵 |
| 5 UI | 106 | HUD |
| 6 Player | 22 | 플레이어 루트 + **공격 앵커 전부** |
| 8 Enemy | 11 | 몬스터·보스 루트 |
| 7 Wall | 6 | |
| 14 EnemyHurtBox | 10 | 몬스터·보스 허트박스 |
| 13 PlayerHurtbox | 2 | |
| 10 Projectile | 2 | **Bomb 루트·Sphere 뿐** |
| 9 HazardArea | 1 | **Bomb 의 장판(`Circle`) 뿐** |
| **19 미명명** | **14** | 🔴 ChompBot 뼈·아마추어 |
| **21 미명명** | **3** | 🔴 ChompBot 모델 |
| **12 Weapon** | **0** | 🔴 **어떤 프리팹도 안 쓴다** |
| 4 Water / 11 Env / 15 Corpse / 16 Soul | 0 | Corpse·Soul 은 런타임에 `NameToLayer` 로 부여 |

### 2.3 충돌 매트릭스

**사실상 전면 허용**이다. `Corpse(15)`/`Soul(16)` 만 `Default/Ground/Wall/Env` 와의 충돌로 제한돼 있고
나머지는 서로 거의 다 충돌한다.

→ **대상 구분은 매트릭스가 아니라 코드 `LayerMask` 가 전부 하고 있다.** 매트릭스를 손보는 것은
이번 범위 밖이다(§5 위험 6).

---

## 3. 🔴 확인된 문제 4건

### 3.1 공격 히트박스 앵커가 `Default(0)` 에 살아 있다 — **실제 버그를 냈다**

23호 실측:

| 앵커 | 콜라이더 | 상태 | 레이어 |
|---|---|---|---|
| `LeftHookAttack` / `RightHookAttack` / `UpperAttack` | Box | **enabled=1, trigger=1** | **0 Default** |
| `DashAttack` / `Rage` / `Floor` | Sphere | **enabled=1, trigger=1** | **0 Default** |
| `Grab` | Box | enabled=0 | 0 Default |

**`ColliderInfo` 는 이 프리팹에 2개뿐**이다. 즉 대부분의 앵커 콜라이더는 `ColliderInfo` 가
관리하지 않아 **런타임에 꺼지지 않고 물리 트리거로 살아 있다**(`KnockbackAttack`·
`TriggerKnockbackAttack`·`AttackTriggerRelay` 가 `OnTrigger*` 로 쓴다).

🔴 **그 결과 실제로 물렸다** — `GroundProbe` 가 `Default|Ground` 를 강제 OR 하므로,
**폭탄이 보스의 공격 콜라이더를 "바닥"으로 오인해 손 높이에서 멈췄다.**
`BombController.cs:363~373` 에 그 사고 기록이 장문 주석으로 남아 있다.

일반 몬스터도 같다 — `MeleeHitbox` 가 전부 `Default(0)`.

→ **`Weapon(12)` 로 이관하면 이 버그 클래스가 구조적으로 사라진다.** 미용 문제가 아니다.

### 3.2 `Weapon(12)` 이 정의만 되고 0건 사용

이름만 있고 아무도 안 쓴다. **§3.1 의 이관 대상 레이어로 재정의한다.**

### 3.3 ChompBot 이 미명명 레이어 19·21 을 쓴다

17개 오브젝트 — `Armature` / `Root` / `Master` / `Head` / `UpperJaw` / `LowerJaw` /
`Hip_L·R` / `Leg_L·R` / `ForeLeg_L·R` / `Foot_L·R`(19) + `Model` / `ChumpBot` / `R_Chompbot_01`(21).

**전부 뼈·모델이다.** 판정에 관여하지 않으므로 `Default(0)` 로 내리는 것이 안전하다.
(다른 7종 몬스터는 이 문제가 없다 — ChompBot 만의 임포트 잔재로 보인다.)

### 3.4 투사체 레이어가 갈려 있다

| 프리팹 | 레이어 |
|---|---|
| `Bomb`(루트·Sphere) | **10 Projectile** ✅ |
| `Bomb` 의 장판 `Circle` | **9 HazardArea** ✅ |
| `P_MonsterProjectile` | **0 Default** ❌ |

폭탄 쪽이 이미 옳다. 몬스터 투사체만 안 맞는다.

---

## 4. 확정 표준

| 레이어 | 의미 | 대상 |
|---:|---|---|
| **0 Default** | 전투 의미 없는 미분류·렌더·뼈 | 모델, Armature, 순수 시각물 |
| **3 Ground** | 지면 | 맵 보행면 |
| **6 Player** | **플레이어 본체 = 탐색 기준점** | 플레이어 루트 (solid capsule) |
| **7 Wall** | 투사체·돌진을 막는 벽 | |
| **8 Enemy** | **몬스터·보스 본체 = 탐색 기준점** | 몬스터·보스 루트 (solid capsule) |
| **9 HazardArea** | **지속 영역**(장판) | `AreaZone` 전부 (화염/늪/독/번개) |
| **10 Projectile** | **모든 활성 투사체** (진영 무관) | 폭탄, 몬스터 투사체 |
| **11 Env** | 환경 충돌물 | |
| **12 Weapon** | 🔴 **공격 히트박스 앵커** (재정의) | 근접 판정 앵커 전부 — 플레이어·몬스터·보스 |
| **17 CombatTarget** | 🔴 **`Unit` 은 아니지만 피해를 받는 오브젝트** (신설) | 송전기·차징 기둥, **부술 수 있는 props(상자·통 등)** |
| **13 PlayerHurtbox** | **플레이어 피해 수신면** | 플레이어 `Hurtbox` (trigger) |
| **14 EnemyHurtBox** | **몬스터·보스 피해 수신면** | 몬스터·보스 `Hurtbox` (trigger) |
| 15 Corpse / 16 Soul | 시체 / 영혼 | 런타임 부여 |

**원칙 한 줄: 본체는 진영 레이어(6/8), 피격면은 전용(13/14), 공격 앵커는 `Weapon(12)`, 장판은
`HazardArea(9)`, 투사체는 `Projectile(10)`.**

🔴 **레이어 번호는 절대 재배치하지 않는다.** 삽입·재정렬하면 직렬화된 `m_Bits` 의미가 전부 바뀐다.

---

## 4.5 ✅ 이관 실행 완료 (2026-08-07)

**A·B·C·D 를 적용했다. 32개 오브젝트.** `CombatTarget(17)` 도 등록했다.

| 결과 | 이전 → 이후 |
|---|---|
| 미명명 19·21 | 17개 → **0** |
| `Weapon(12)` | 0 → **14** (몬스터 `MeleeHitbox` 8 + 23호 앵커 6) |
| `Projectile(10)` | 2 → **3** (`P_MonsterProjectile` 편입) |
| `CombatTarget(17)` | 없음 → **등록** |

**23호에서 옮긴 6개**: `LeftHookAttack` · `RightHookAttack` · `UpperAttack` ·
`DashAttack` · `Rage` · `Grab` — 전부 콜라이더 보유 확인 후 이관.

### 🟢 부수 효과 — 투사체 상쇄가 살아났다

`P_MonsterProjectile` 이 `Default(0)` 이라 플레이어 공격 마스크
(`17664 = Enemy + Projectile + EnemyHurtBox`)에 **안 걸리고 있었다.**
`Projectile(10)` 로 옮기면서 **의도된 투사체 상쇄가 이제 실제로 동작한다.**
→ 검증 항목에 추가(§8). 원치 않는 동작이면 마스크에서 `Projectile` 을 빼는 게 아니라
투사체 쪽에 별도 가드를 둔다(§5.5-2 참조).

### 아직 안 옮긴 것

| 대상 | 이유 |
|---|---|
| **차징 기둥 → `CombatTarget(17)`** | `bossroom.prefab` + 씬 4곳에 흩어져 있다. **`ChargeController` 재작성과 함께** 옮긴다 |
| **부술 수 있는 props → `CombatTarget(17)`** | 아직 파괴 가능한 props 가 없다(`Map/Props/` 는 전부 순수 시각물). **파괴 기능 구현 시** 함께 |
| **23호 `Floor`/`FloorBase`/`FloorGrow`** | JumpAttack 장판 예고. `AreaZone` 작업에서 `HazardArea(9)` 로 정리 |
| **F. 플레이어 공격 앵커 `Player(6)` → `Weapon(12)`** | §5.3 — 보스 검증 후 별도로 |

---

## 5. 이관 계획 — 순서와 위험

### 5.1 이관 대상

| # | 대상 | 현재 → 표준 | 개수 | 위험 |
|---|---|---|---:|---|
| A | ChompBot 뼈·모델 | 19·21 → **0 Default** | 17 | 낮음 (판정 무관) |
| B | `P_MonsterProjectile` | 0 → **10 Projectile** | 1 | 낮음. 단 마스크에 `Projectile` 이 있는지 확인 |
| C | 몬스터 `MeleeHitbox` | 0 → **12 Weapon** | 8종 | 중간 |
| D | 23호 공격 앵커 6개 | 0 → **12 Weapon** | 6 | 중간 |
| E | Wells `Attack`/`BombAttack`/`BombSocket` | 0 → **12 Weapon** | 3 | 낮음 |
| F | 플레이어 공격 앵커 | 6 Player → **12 Weapon** | ~10 | **높음** — §5.3 |

### 5.2 권장 순서

**A → B → C·D·E → (검증) → F**

A·B 는 독립이고 안전하다. C·D·E 는 "공격 앵커를 Default 에서 빼는" 하나의 묶음이라 같이 하고,
**여기서 한 번 Play 검증**한다(폭탄이 보스 몸통을 바닥으로 오인하지 않는지 = 원래 버그 재현 확인).
F 는 마지막에 따로.

### 5.3 🔴 F(플레이어 앵커)를 마지막에 두는 이유

플레이어 공격 앵커가 **`Player(6)`** 에 있다. 이걸 옮기면:

- 몬스터의 `playerMask` = `Player(6)` 단독이므로, **몬스터가 플레이어 공격 앵커를 타겟으로
  잡고 있었다면** 동작이 바뀐다. (앵커에 `Unit` 이 없어 실제 피해는 안 들어갔겠지만
  `FindNearestTarget` 의 최근접 계산에는 들어갔을 수 있다.)
- 보스 재작성과 무관하므로 **급하지 않다.** 보스 검증이 끝난 뒤 별도로 한다.

### 5.4 함께 해야 하는 코드 변경 — 탐색/피해 마스크 분리

지금은 `MonsterBase.playerMask` 하나가 **탐색과 피해 판정 양쪽**에 들어간다
(`SetTargetLayer(playerMask)` 로 `BaseAttack` 에 그대로 주입).

그 결과 비대칭이 굳어져 있다:

```
플레이어 → 몬스터 : EnemyHurtBox(14) 를 친다   ← Hurtbox 경유
몬스터 → 플레이어 : Player(6) 루트 캡슐을 친다  ← Hurtbox 를 안 거친다
```

→ 보스(그리고 새 몬스터 코드)는 **두 개로 나눈다**:

| 마스크 | 값 | 쓰는 곳 |
|---|---|---|
| `targetAcquisitionMask` | `Player(6)` / `Enemy(8)` | 타겟 탐색 (`OverlapSphere`) |
| `damageMask` | `PlayerHurtbox(13)` / `EnemyHurtBox(14)` | 히트 판정 (`Overlap*` → `Hurtbox`) |

⚠️ **전환기에 본체 + 허트박스를 함께 조회하면 한 공격이 두 번 맞는다.** 플레이어 평타는
`HashSet` dedup 이 있지만 몬스터 공격은 히트 윈도우 단위라 없다. **둘을 동시에 넣지 말 것.**

### 5.5 건드리면 안 되는 것

1. 🔴 **레이어 번호 재배치·이름 변경 금지** — 직렬화 `m_Bits` 와 `NameToLayer`/`GetMask` 문자열이 깨진다.
2. 🔴 **플레이어 공격 마스크에서 `Projectile(10)` 을 빼지 말 것** — 투사체 상쇄가 깨진다.
   (`PlayerDefaultAttackData.hittableLayers = 17664 = Enemy + Projectile + EnemyHurtBox`)
3. **`GroundProbe` 의 `Default|Ground` 강제 OR 는 의도된 것** — 레거시 맵 때문이다. 유지한다.
   (단 §3.1 이 해결되면 이 강제 OR 의 위험이 크게 줄어든다.)
4. **`Enemy(8)` 는 차징 기둥에도 쓰인다**(`BossRoomAuthoring`). 본체 전용으로 좁히려면 저작 코드를
   함께 이관해야 한다 → §7 미결.
5. **레이어 이관과 충돌 매트릭스 축소를 동시에 하지 말 것.**

---

## 6. Codex 레인 오류 정정 (실측 기준)

| Codex 주장 | 실측 | 판정 |
|---|---|---|
| `17664 = Enemy + **Weapon** + EnemyHurtBox` (5.5) | `Enemy(8) + Projectile(10) + EnemyHurtBox(14)`. Weapon = 4096 이라 합이 20736 이 된다 | ❌ 산술 오류 |
| "Bomb 루트가 **HazardArea(9)**, 자식 일부가 Projectile(10)" (5.5) | **반대다.** 루트·Sphere = `Projectile(10)`, 장판 `Circle` = `HazardArea(9)` | ❌ 뒤집어 읽음 |
| 프리팹 경로 `Assets/2.Prefabs/Monster/Boss/...` (5.6-sol) | 실제는 `Assets/2.Prefabs/Wells&No.23/`. 중복 프리팹 없음 | ❌ 경로 지어냄 |
| `MonsterBase.playerMask` 탐색·판정 겸용 | 확인 | ✅ (Claude 레인과 독립 일치) |
| ChompBot 미명명 19·21 | 확인 (17개, 전부 뼈·모델) | ✅ (3레인 일치) |
| `Weapon(12)` 실사용 증거 없음 | 확인 — **프리팹 0건** | ✅ |

**교훈**: Codex 결과는 **결론의 방향은 쓸 만하지만 숫자와 경로는 반드시 재확인**해야 한다.
이번 레이어 작업에서만 3건이 틀렸다.

---

## 7. 팀장 확인이 필요한 것 2건

| # | 질문 | 제 의견 |
|---|---|---|
| **L1** | `Weapon(12)` 재정의·이관 | ✅ **승인·실행 완료**(§4.5). | **찬성.** §3.1 이 실제 버그를 냈고(폭탄이 보스 공격 콜라이더를 바닥으로 오인), 이관하면 그 버그 클래스가 구조적으로 사라집니다. 지금 `Weapon` 은 0건 사용이라 충돌 위험도 없습니다 |
| **L2** | `17 CombatTarget` 신설 | ✅ **승인 — 내 보류 권고를 뒤집는 이유가 있었다.** 차징 기둥 분리만이면 보류가 맞지만, **부술 수 있는 props(상자 등)를 추가할 예정**이라 "`Unit` 은 아니지만 피해를 받는 오브젝트"라는 **분류 자체가 필요**하다. 레이어를 등록했다 |

---

## 8. 검증 방법

이관 후 확인할 것:

- [ ] **폭탄이 보스 앞에서 멈추지 않는다** (§3.1 원래 버그의 재현 시험)
- [ ] 플레이어 평타가 **보스·몬스터에 정상 적중** (허트박스 경유)
- [ ] 몬스터 근접 공격이 **플레이어에 정상 적중**
- [ ] 몬스터 투사체가 정상 비행·명중 (B 이관 후)
- [ ] 🟢 **플레이어 평타로 몬스터 투사체를 상쇄할 수 있다** (이관으로 새로 살아난 기능)
- [ ] **한 공격이 두 번 들어가지 않는다** (§5.4 중복 적중)
- [ ] ChompBot 이 정상 동작 (A 이관 후)
- [ ] NavMesh 베이크·접지가 안 깨졌다 (`GroundProbe` 관련)
- [ ] MPPM 2인에서 위 전부

---

## 9. 다음 단계 — 보스를 SO 로

팀장 방향(2026-08-07): **보스를 밀어버리고 몬스터 체계에 맞춘다. `MonsterDataSO` 에 보스 타입을
enum 으로 추가하는 식으로 데이터 주도로 간다.**

이 레이어 정리가 그 선행 작업이다. 표준이 확정돼야 새 보스 프리팹을 올바른 레이어로 조립할 수 있다.
보스 SO 설계는 [boss-rebuild-standard.md](boss-rebuild-standard.md) §7(새로 만들어야 하는 것 6가지)에서 이어간다.
