# Unit

> 현재 브랜치: `feature/Wells&No.23`
>
> 기준 코드: `Assets/1.Scripts/Unit/Unit.cs`, `Health.cs`, `StatusEffectType.cs`
>
> `Unit`은 전투 가능한 오브젝트의 공통 전투 스탯, 체력/방어력/쉴드, 이동/공격 속도, 상태 이상을 서버 권한으로 관리하는 네트워크 단위이다.

이 문서는 현재 브랜치에 존재하는 `Unit.cs` 구현을 기준으로 한다. 목표 설계가 아니라, 지금 코드가 실제로 제공하는 책임과 제약을 정리한 문서이다. `feature/Wells&No.23` 브랜치에서는 보스 웰즈/23호 작업이 함께 진행되고 있으므로, `Unit`은 플레이어와 보스가 공유할 수 있는 전투 상태 기반으로 해석한다.

## 역할

`Unit`은 `NetworkBehaviour`를 상속하며 다음 값을 관리한다.

| 구분 | 값 | 설명 |
|------|----|------|
| 공격 | `AttackDamage` | 기본 공격력 |
| 체력 | `Health` | 현재 체력, 최대 체력, 방어력, 현재 쉴드, 최대 쉴드 |
| 속도 | `MoveSpeed`, `AttackSpeed` | 이동 속도와 공격 속도 |
| 상태 이상 | `StatusEffectType` | `[Flags]` 기반 상태 이상 비트마스크 |
| 네트워크 동기화 | `_currentHp`, `_currentShield`, `_hasShield` | 클라이언트에 복제할 체력/쉴드 상태 |
| 넉백 | `IKnockbackable` | 선택적으로 연결되는 넉백 처리 인터페이스 |

`Unit` 자체는 플레이어 입력, 보스 AI, 스킬 발동, 애니메이션 제어를 직접 담당하지 않는다. 이 클래스는 “전투 수치가 어떻게 변경되는가”를 서버에서 판정하는 공통 기반으로 둔다.

현재 브랜치의 보스 구현(`Assets/1.Scripts/Enemy/Boss`)은 상태 머신, 공격 선택, 잡기 판정 같은 보스 전용 흐름을 별도 코드로 가진다. 따라서 `Unit` 문서에서는 보스 패턴 자체가 아니라 보스/플레이어가 공통으로 사용할 수 있는 체력, 쉴드, 방어력, 상태 이상 기준만 다룬다.

## 현재 상속 관계

현재 브랜치에서 `Unit`을 직접 상속하는 클래스는 다음과 같다.

| 클래스 | 위치 | 현재 역할 |
|--------|------|-----------|
| `Player : Unit` | `Assets/1.Scripts/Player/Player.cs` | 플레이어의 기본 전투 수치를 직렬화 필드로 받고, 서버 `OnNetworkSpawn`에서 `Initialize`를 호출한다. |
| `Enemy : Unit` | `Assets/1.Scripts/Enemy/Enemy.cs` | 적/보스 계열의 기본 전투 수치를 초기화하고, Behavior Graph 블랙보드에 이동 속도와 그로기 값을 연결한다. |
| `ChargingObject : Unit` | `Assets/1.Scripts/Enemy/Boss/ChargingObject.cs` | 보스 패턴용 충전 오브젝트. 체력, 방어력, 쉴드를 가지며 도달 후에만 피해를 받고, 체력 0에서 이벤트를 발생시킨다. |

`ChargingObject`는 일반 캐릭터라기보다 보스 패턴 오브젝트에 가깝다. 현재는 체력/피해/서버 권한 처리를 재사용하기 위해 `Unit`을 상속한다.

## 초기화

`Initialize(...)`는 Unit을 상속하거나 포함하는 쪽에서 기본 능력치를 주입할 때 호출한다.

```csharp
Initialize(
    int attackDamage,
    float moveSpeed,
    float attackSpeed,
    int maxHp,
    int defense,
    int maxShield
)
```

초기화 시 처리되는 값은 다음과 같다.

| 값 | 초기 상태 |
|----|-----------|
| 공격력 | `attackDamage` |
| 이동 속도 | `moveSpeed` |
| 공격 속도 | `attackSpeed` |
| 현재 체력 | `maxHp` |
| 최대 체력 | `maxHp` |
| 방어력 | `defense` |
| 현재 쉴드 | `0` |
| 최대 쉴드 | `maxShield` |
| 쉴드 보유 여부 | `false` |

`Health` 객체는 `Initialize`에서 생성된다. 따라서 `TakeDamage`, `HealHp`, `IncreaseShield` 같은 체력 관련 API는 초기화 이후 호출되어야 한다.

`Initialize`는 마지막에 같은 GameObject에서 `IKnockbackable` 구현을 찾아 `_knockback`에 보관한다. 넉백을 받는 Unit은 `LinearKnockback` 같은 `IKnockbackable` 컴포넌트를 함께 붙여야 한다.

## 서버 권한

모든 수치 변경 함수는 서버에서만 실제 값을 변경한다.

```csharp
if (!IsServer) return;
```

클라이언트가 값을 바꾸려면 대응되는 `Rpc(SendTo.Server)` 함수를 호출한다. RPC는 `SenderClientId`가 `OwnerClientId`와 같은 경우에만 처리된다.

| 직접 함수 | 서버 RPC |
|-----------|----------|
| `ChangeAttackDamageValue` | `ChangeAttackDamageValueRpc` |

현재 `Unit.cs`에는 공격력 변경 RPC만 남아 있다. 체력, 방어력, 쉴드, 속도, 상태 이상 변경 RPC는 현재 코드 기준으로 존재하지 않는다.

`_currentHp`, `_currentShield`, `_hasShield`는 `NetworkVariableReadPermission.Everyone`, `NetworkVariableWritePermission.Server`로 생성된다. 즉 모든 클라이언트가 읽을 수 있지만, 값 변경은 서버만 할 수 있다.

## 피해 처리

`TakeDamage(int damage)`는 서버에서 다음 순서로 피해를 처리한다.

1. 입력 피해량을 `remainingDamage`로 둔다.
2. 방어력(`CurrentDefense`)만큼 피해를 먼저 감소시킨다.
3. 쉴드가 있으면 남은 피해를 쉴드에 적용한다.
4. 쉴드 처리 후 남은 피해가 있으면 체력에 적용한다.
5. 변경된 현재 체력을 `_currentHp` 네트워크 변수에 반영한다.

방어력 계산은 단순 감산이다.

```csharp
remainingDamage = Mathf.Max(remainingDamage - _health.CurrentDefense, 0);
```

체력은 0 아래로 내려가지 않고, 회복은 최대 체력을 넘지 않는다.

`TakeDamage`는 `virtual`이므로 상속 클래스가 피해 조건이나 추가 처리를 붙일 수 있다. 현재 `Enemy`는 `TakeDamage`를 override해 그로기 체크 지점을 만들고, `ChargingObject`는 특정 위치에 도달한 뒤에만 피해를 받도록 제한한다.

`Revive()`는 현재 체력을 최대 체력으로 회복시키고 `_currentHp` 네트워크 변수에 반영한다.

## 쉴드

쉴드는 `Health` 내부에서 현재 쉴드와 최대 쉴드를 가진다.

| 함수 | 동작 |
|------|------|
| `IncreaseShield(int shieldAmount)` | 현재 쉴드를 증가시키며 최대 쉴드를 넘지 않게 제한한다. |
| `SetShield(int shieldValue)` | 현재 쉴드를 지정 값으로 설정한다. |
| `TakeShieldDamage(int damage)` | 현재 쉴드를 감소시키며 0 아래로 내려가지 않게 제한한다. |

`_hasShield`는 현재 쉴드가 0보다 크면 `true`, 아니면 `false`이다.

쉴드 변경 후에는 `UpdateNetworkShield()`를 호출해 `_currentShield`, `_hasShield` 네트워크 변수에 반영한다.

## 방어력

방어력은 `Health.CurrentDefense`로 관리된다.

| 함수 | 동작 |
|------|------|
| `IncreaseDefense(int increaseAmount)` | 방어력을 증가시킨다. |
| `DecreaseDefense(int decreaseAmount)` | 방어력을 감소시키며 0 아래로 내려가지 않게 제한한다. |

현재 구현에서 방어력에는 최대치 제한이 없다.

## 속도

`Unit`은 이동 속도와 공격 속도를 단순 값으로 보관한다.

| 값 | 변경 함수 |
|----|-----------|
| `MoveSpeed` | `ChangeMoveSpeedValue(float newMoveSpeed)` |
| `AttackSpeed` | `ChangeAttackSpeedValue(float newAttackSpeed)` |

현재 구현에서 속도 값에는 최소/최대 제한이 없다. 둔화, 버프, 장비 효과가 적용될 경우 호출하는 쪽에서 유효 범위를 정하거나, 이후 `Unit` 내부에 검증 규칙을 추가해야 한다.

## 상태 이상

상태 이상은 `[Flags]` enum인 `StatusEffectType`으로 표현한다.

| 상태 | 값 | 의미 |
|------|----|------|
| `None` | `0` | 상태 이상 없음 |
| `Airborne` | `1 << 0` | 공중에 뜸 |
| `Stunned` | `1 << 1` | 기절 |
| `Slowed` | `1 << 2` | 둔화 |
| `Rooted` | `1 << 3` | 속박 |
| `Silenced` | `1 << 4` | 침묵, 스킬 봉인 |
| `Debilitated` | `1 << 5` | 약화, 대쉬 불가 및 둔화 |

여러 상태를 동시에 적용할 수 있다. 상태 추가/제거/포함 확인은 `BitMaskHelper<StatusEffectType>`를 사용한다.

```csharp
var next = BitMaskHelper<StatusEffectType>.Add(current, StatusEffectType.Stunned);
```

현재 `Unit`은 상태 이상 값을 저장하고 변경하는 책임만 가진다. 상태 지속 시간, 중첩, 만료 처리, 이동/입력 제한 적용은 아직 `Unit` 코드 안에 없다.

## 넉백

`Unit`은 선택적으로 `IKnockbackable`을 통해 넉백을 위임한다.

| 함수 | 동작 |
|------|------|
| `Knockback(Vector3 direction, float strength)` | 서버에서만 실행되며, 연결된 `IKnockbackable`의 `ApplyKnockback`을 호출한다. |

`Initialize` 시점에 `GetComponent<IKnockbackable>()`로 구현체를 찾는다. 구현체가 없으면 넉백 호출은 아무 동작도 하지 않는다.

## 현재 구현의 경계

현재 `Unit.cs` 기준으로 아직 담당하지 않는 영역은 다음과 같다.

| 영역 | 현재 상태 |
|------|-----------|
| 사망 처리 | 체력이 0이 되었을 때 이벤트나 상태 전환이 없다. |
| 체력/쉴드 변경 이벤트 | UI, VFX, 사운드에 알릴 이벤트가 없다. |
| 상태 이상 지속 시간 | 상태 적용 시각, 종료 시각, 중첩 정보가 없다. |
| 스탯 최대/최소값 | 공격력, 속도, 방어력의 범위 검증이 없다. |
| 빌드/장비 보정 | 기본값과 보정값의 분리 구조가 없다. |
| 클라이언트 표시용 접근자 | `_currentHp`, `_currentShield`, `_hasShield`는 protected이며, 외부 UI가 직접 읽기 위한 public API는 없다. |

## 추후 논의 필요

아래 항목은 현재 문서에서 결론을 내리지 않는다. 이후 전투 판정, 팀 판정, 상태 이상 처리 방식이 더 구체화되면 다시 논의한다.

| 주제 | 논의할 내용 |
|------|-------------|
| 아군/적군 구분 | `Unit`에 팀 또는 진영을 구분하는 멤버 변수가 필요한지 검토한다. 피해 판정, 타겟팅, 투사체, 보스 패턴 오브젝트까지 포함해 결정해야 한다. |
| 이동 불가 상태 | 현재 `Rooted`가 이동 불가에 대응될 수 있다. 별도 상태를 추가할지, 기존 상태의 의미를 명확히 할지 논의가 필요하다. |
| 스킬 사용 불가 상태 | 현재 `Silenced`가 스킬 사용 불가에 대응될 수 있다. 일반 공격, 이동, 대쉬, 스킬을 각각 어떻게 막을지 기준을 정해야 한다. |

## 구현 메모

- 이 문서는 `feature/Wells&No.23` 브랜치의 현재 코드 기준이다. 이후 `Unit`이 현재 코드의 `Player`, `Enemy`, 보스 전용 컴포넌트, 공격/스킬 처리 흐름과 직접 연결되면 문서도 함께 갱신해야 한다.
- `ChangeAttackDamageValueRpc`는 새 공격력 값을 인자로 받지 않고 현재 `AttackDamage` 값을 다시 전달한다. 클라이언트 요청으로 공격력을 바꾸려는 의도라면 RPC 시그니처를 재검토해야 한다.
- `TakeDamage`의 쉴드 처리 로직은 `shieldDamage`라는 이름과 실제 의미가 섞여 있어 읽기 어렵다. “쉴드가 흡수한 피해량”과 “쉴드를 뚫고 남은 피해량”을 분리하면 버그 가능성이 줄어든다.
- 현재 체력/방어력/쉴드/속도/상태 이상 변경 RPC가 없으므로, 클라이언트 요청이 필요한 경우 서버 전투 판정 시스템이나 별도 요청 흐름이 필요하다.
- `Debilitated`는 주석상 “대쉬 불가 + 둔화”지만 enum 값 하나로만 존재한다. 실제 입력 제한과 이동 속도 감소를 어디서 해석할지 별도 규칙이 필요하다.

## 권장 확장 방향

다음 단계에서는 `Unit`이 직접 모든 규칙을 커지게 만들기보다 책임을 분리하는 편이 좋다.

| 책임 | 권장 위치 |
|------|-----------|
| 체력/방어력/쉴드 계산 | `Health` |
| 서버 권한 스탯 변경 API | `Unit` |
| 상태 이상 지속 시간/중첩/만료 | `StatusEffectController` 또는 `UnitStatus` |
| 이동 제한/입력 제한 적용 | 이동/입력 컴포넌트 |
| 공격 판정과 피해 요청 | 서버 전투 시스템 또는 Ability 계층 |
| UI 갱신 | NetworkVariable 구독 또는 Unit 이벤트 |

`Unit`은 최종적으로 “전투 가능한 대상의 서버 권한 상태 저장소”에 가깝게 유지하고, 개별 게임플레이 규칙은 전용 컴포넌트가 해석하는 구조를 목표로 한다.
