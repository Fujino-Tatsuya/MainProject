# Physics

이 문서는 Unit, Player, Enemy, Boss가 공유하는 물리/충돌/피격 판정 기준을 정리한다.

## 기본 원칙

- `Unit`은 피해를 받는 공통 전투 주체다.
- 공격 판정 컴포넌트는 어떤 `Unit`을 맞췄는지 판단한다.
- 최종 피해, 회복, 상태이상, 넉백 적용은 `Unit`에서 처리한다.
- 서버 권한 게임플레이에서는 피해와 상태 변경을 서버에서 확정한다.

```text
Attacker Unit
-> AttackHitbox / DamageDealer / Ability / Boss Pattern
-> Target Unit
-> Unit.TakeDamage / Unit.Heal / Unit.Knockback
```

## Collider 역할 분리

하나의 Collider에 물리 충돌, 피격 판정, 공격 판정을 모두 맡기지 않는다.

권장 구조:

```text
Unit Root
- Rigidbody / NavMeshAgent
- Unit

Body Collider
- 물리 공간용
- 벽, 바닥, 유닛 간 막힘/충돌 처리
- isTrigger = false

Hurtbox Collider
- 피격 체크용
- 공격 Hitbox가 맞춘 대상인지 감지
- isTrigger = true

AttackHitbox Collider
- 공격 판정용
- 공격 유효 프레임에만 enabled
- isTrigger = true
```

`Hurtbox Collider`는 물리 반응을 만들지 않아야 한다. `isTrigger = true`로 두고, Physics Layer Collision Matrix에서 `AttackHitbox`와만 상호작용하도록 제한한다.

예시 레이어 매트릭스:

```text
UnitHurtbox x Environment = OFF
UnitHurtbox x UnitBody = OFF
UnitHurtbox x UnitHurtbox = OFF
UnitHurtbox x AttackHitbox = ON
```

## Collider 활성 조건

Unity Physics에서 Collider가 충돌/트리거 후보가 되려면 다음 조건을 만족해야 한다.

- GameObject가 hierarchy에서 활성 상태여야 한다.
  - `gameObject.activeInHierarchy == true`
  - 부모 GameObject가 비활성이면 자식의 `activeSelf`가 true여도 실제로는 비활성이다.
- Collider 컴포넌트가 활성 상태여야 한다.
  - `collider.enabled == true`
- Trigger 이벤트라면 관련 Collider 중 하나가 `isTrigger == true`여야 한다.
- Trigger/Collision 이벤트가 발생 가능한 Rigidbody/Collider 조합이어야 한다.
- Physics Layer Collision Matrix가 해당 레이어 조합을 허용해야 한다.

공격 판정은 보통 애니메이션 이벤트로 `AttackHitbox Collider`만 켜고 끈다.

```csharp
attackCollider.enabled = true;  // 공격 유효 프레임 시작
attackCollider.enabled = false; // 공격 유효 프레임 종료
```

## LayerMask와 Faction

`LayerMask`와 `Faction`은 역할이 다르다.

```text
LayerMask
- 물리 후보군을 줄이는 1차 필터
- Unity Physics 단계에서 빠르게 검사 대상을 제한한다.

Faction / Relation
- 후보 Unit이 실제 유효 대상인지 판단하는 게임 규칙
- Ally, Neutral, Monster 관계를 기준으로 피해/회복/무시를 결정한다.
```

권장 흐름:

```text
1. AttackHitbox가 Collider를 감지한다.
2. LayerMask로 물리 후보를 제한한다.
3. Collider에서 Unit을 찾는다.
4. Attacker Unit과 Target Unit의 Faction/Relation을 검사한다.
5. Skill/Attack 규칙에 따라 Damage, Heal, Ignore를 결정한다.
```

예:

```text
Holy Skill
- Self 또는 Ally: Heal
- Enemy: Damage
- Neutral: Ignore 또는 스킬별 예외 처리
```

## Unit Layer 전략

Player, Enemy, Boss를 물리 레이어로 과하게 세분화하지 않는다. 전투 대상은 넓게 `Unit` 또는 `UnitHurtbox` 계열 레이어로 묶고, 세부 적대 관계는 `Unit.Faction`으로 판단한다.

권장 레이어 예:

```text
UnitBody
UnitHurtbox
AttackHitbox
Environment
Projectile
Interaction
```

이 구조는 다음 장점이 있다.

- Player/Enemy/Boss/소환물/중립 오브젝트가 같은 판정 파이프라인을 쓴다.
- 아군 대상 힐, 적 대상 피해 같은 양면 스킬을 구현하기 쉽다.
- LayerMask는 물리 최적화에 집중하고, Faction은 게임 규칙에 집중한다.

## 최적화

Unity Physics는 렌더링 화면 안에 있는 Collider만 검사하지 않는다. 카메라에 보이지 않아도 물리 공간에서 Collider bounds가 겹치고, Layer Collision Matrix가 허용하면 충돌/트리거 후보가 될 수 있다.

Physics 후보군은 대략 다음 순서로 줄어든다.

```text
1. 활성 Collider인지 확인
2. Layer Collision Matrix 확인
3. Broadphase에서 AABB 기반 공간 후보 추림
4. Narrowphase에서 실제 shape overlap/contact 확인
5. OnTrigger / OnCollision 이벤트 발생
6. 게임 코드에서 LayerMask, Unit, Faction, Relation 검사
```

`Unit`이 많아도 공격 범위가 작고 동시에 활성화되는 공격 Hitbox가 적으면 `LayerMask = UnitHurtbox` + `Faction 체크` 구조는 충분히 유효하다.

주의해야 할 경우:

- 거대한 장판이나 전역 범위 공격이 자주 활성화된다.
- 한 지점에 많은 Unit이 몰린다.
- 한 Unit에 자식 Collider가 너무 많다.
- `OnTriggerStay`에서 매 프레임 많은 후보를 처리한다.
- 멀리 있는 몬스터의 공격/피격 Collider가 항상 켜져 있다.

### 거리 기반 Collider 비활성화

플레이어와 충분히 멀리 떨어진 몬스터는 전투용 Collider를 꺼서 Physics 후보군에서 제외할 수 있다.

권장:

```text
멀리 있는 몬스터
- Hurtbox Collider: OFF 가능
- AttackHitbox Collider: OFF 가능
- AI 감지 Trigger: OFF 가능
- Body Collider: 게임 규칙에 따라 신중히 결정
```

`Body Collider`를 끄면 물리 공간에서 없는 존재가 될 수 있으므로 주의한다. 벽 통과, 유닛 겹침, 낙하, NavMesh/물리 반응 문제가 생길 수 있다.

거리 기준은 카메라 화면 기준보다 플레이어 거리 기준이 안전하다. 멀티플레이에서는 모든 플레이어로부터 멀 때만 비활성화한다.

권장 방식:

```text
combatActiveDistance = 25m
combatSleepDistance = 35m

25m 안으로 들어오면 전투 Collider ON
35m 밖으로 나가면 전투 Collider OFF
```

ON/OFF 거리를 다르게 두어 경계에서 계속 켜졌다 꺼지는 현상을 막는다.

서버 권한 게임에서는 이 판단도 서버 기준으로 수행한다. 서버에서 Collider가 꺼져야 실제 피해/피격 판정 후보에서 제외된다.

## 현재 코드와의 대응

현재 프로젝트의 `BaseWeapon`, `ColliderBasicAttack`, `AttackTriggerRelay`, `KnockbackAttack`은 이미 다음 방향에 가깝다.

```text
BaseWeapon
- damage
- layerMask
- server check

ColliderBasicAttack
- trigger enter/stay/exit 판정
- 대상 Unit 탐색
- Unit.TakeDamage 호출

AttackTriggerRelay
- Trigger 이벤트를 서버에서 공격 판정 컴포넌트로 전달

KnockbackAttack
- Unit.TakeDamage
- Unit.Knockback
```

장기적으로 `BaseWeapon`이라는 이름은 장비 교체형 무기처럼 보일 수 있다. 실제 책임에 맞게 `DamageDealer`, `AttackHitbox`, `KnockbackDamageDealer` 같은 이름을 검토할 수 있다.
