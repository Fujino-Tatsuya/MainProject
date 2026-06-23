# Character

> 현재 브랜치: `feature/Wells&No.23`
>
> 이 문서는 `Player`에 장착되는 Character의 데이터, Prefab, UserProfile 보정 흐름을 다룬다. Ability 상세는 `ability.md`에서 다룬다.

## 역할

`Character`는 Player에 장착되는 전투 스타일과 표현이다.

| 개념 | 의미 |
|------|------|
| `Player` | 네트워크/입력/이동/피격의 주체 |
| `Character` | Player가 사용하는 캐릭터 데이터와 외형 |
| `CharacterDefinition` | 캐릭터의 기본 데이터 |
| `Character Prefab` | 모델, Animator, VFX 소켓 등 런타임 표현 |

Garen, Jinx 같은 세부 캐릭터는 Character의 구체 사례이며, `Docs/design/character/character_garen.md`, `Docs/design/character/character_jinx.md`처럼 분리한다.

## 세부 캐릭터 문서

| Character | 문서 |
|-----------|------|
| Garen | `character/character_garen.md` |
| Jinx | `character/character_jinx.md` |

## 데이터와 Prefab 분리

Character 기본값과 런타임 표현은 분리한다.

| 항목 | 책임 |
|------|------|
| `CharacterDefinition` | ScriptableObject. 캐릭터 ID, 표시 이름, 기본 스탯, Character Prefab 참조, 사용할 Ability 참조, 캐릭터 고유 패시브 참조 |
| `Character Prefab` | 모델, Animator, 무기/스킬/VFX 소켓, 애니메이션 이벤트 릴레이, 캐릭터별 시각 표현 |

`CharacterDefinition`은 ScriptableObject 원본 데이터로 확정한다. 런타임에 유저별 성장/강화 값을 적용할 때 이 원본 값을 직접 수정하지 않는다.

`UserProfile`은 ScriptableObject가 아니라 유저 저장 데이터이다. UserProfile에는 선택/해금/성장 ID와 수치만 저장하고, 서버가 이를 게임 데이터와 매칭해 최종 스탯과 패시브를 계산한다.

`CharacterDefinition`은 Ability 상세 데이터를 직접 갖지 않고, 사용할 AbilityDefinition을 참조한다. 슬롯/실행 타입/패시브 상세 규칙은 `ability.md`에서 다룬다.

| Ability 필드 | 의미 |
|--------------|------|
| `basicAttack` | 기본 공격 슬롯 |
| `secondaryAction` | 보조 액션 슬롯 |
| `mainSkill` | 주력 스킬 슬롯 |
| `subSkill` | 보조 스킬 슬롯 |
| `ultimateSkill` | 궁극기 슬롯 |
| `characterPassives` | 캐릭터가 기본으로 가진 글로벌 패시브 목록 |

## Character Prefab 책임

`Character Prefab`은 자식 오브젝트로 장착되는 표현 계층이다.

포함한다:

- 모델
- Animator
- 무기/손/총구/스킬/VFX 소켓
- `CharacterAnimationEventRelay`
- 캐릭터별 시각 표현 컴포넌트

포함하지 않는다:

- 체력/피격 주체
- 네트워크 소유권
- 입력 처리
- Ability 실행 판정
- 최종 스탯 계산

Ability 실행 로직은 `PlayerAbilityController`와 Ability 상태 인스턴스 쪽에 둔다. Character Prefab은 필요한 경우 `CharacterAnimationEventRelay`를 통해 애니메이션 이벤트를 Ability 시스템으로 전달하는 릴레이 역할까지만 맡는다.

`CharacterAnimationEventRelay`는 Character Prefab에 붙는 컴포넌트 이름으로 확정한다. Animator 이벤트를 직접 처리하지 않고, 상위 Player/Ability 시스템으로 전달한다.

Animator는 자식 `Character Prefab`에만 둔다. 부모 `Player`는 네트워크/입력/이동/피격 주체로 유지하고, 외형과 애니메이션은 장착된 Character가 담당한다.

## Character 장착/복제 규칙

대부분의 Character는 런타임 중 교체되지 않는다. 기본 흐름은 로비에서 확정된 Character를 Player 스폰 시 한 번 장착하는 방식이다. 런타임 교체가 필요한 예외 캐릭터나 특수 규칙은 추후 별도 논의한다.

Character Prefab 생성은 서버가 결정하고 복제 흐름을 제어한다. 다만 Character Prefab 자체는 별도 `NetworkObject`를 갖지 않는다. 네트워크 오브젝트는 부모 `Player`만 가진다.

```text
Player(NetworkObject)
└── CharacterSlot
    └── Character Prefab(non-NetworkObject)
```

Character 선택/조회에는 `characterId`를 사용한다. 표시 이름과 구분되는 안정적인 ID로 두며, 로비 선택, UserProfile 저장, 서버 검증, CharacterDefinition 조회에 사용한다.

`characterId`는 문자열로 둔다. 표기 규칙은 snake_case를 사용한다.

```text
garen
jinx
wells_no23
```

Character Prefab은 `CharacterDefinition`에서 직접 참조한다. Resources나 Addressables 기반 로딩은 초기 범위에 포함하지 않는다.

CharacterDefinition 에셋 파일명은 타입과 표시명을 함께 드러낸다.

```text
CharacterDefinition_Garen.asset
CharacterDefinition_Jinx.asset
```

`characterId`는 에셋 파일명이 아니라 내부 필드로 관리한다.

## Player 초기화 흐름

`Character`는 별도의 `Unit`을 가지지 않는다. Character 데이터가 부모 `Player : Unit`의 스탯을 초기화하거나 수정한다.

Player에 붙는 `PlayerCharacterController`가 Character 장착/초기화를 담당한다. 이 컴포넌트는 선택된 `CharacterDefinition`, UserProfile 보정 결과, Character Prefab 장착, Ability 목록 구성을 연결한다.

최종 스탯 계산은 `PlayerCharacterController`가 직접 수행하지 않는다. `StatCalculator`가 `CharacterDefinition`과 `UserProfile`을 입력받아 최종 스탯을 계산하고, `PlayerCharacterController`는 그 결과를 부모 `Player : Unit` 초기화에 적용한다.

```text
CharacterDefinition
+ UserProfile.accountModifiers
+ UserProfile.characterModifiers[characterId]
= 최종 Unit 초기화 값
```

최종 스탯 계산은 서버 권한으로 수행한다. 클라이언트가 선택한 Character ID나 표시 UI는 요청/표시에 사용할 수 있지만, 실제 전투 수치는 서버가 Character 데이터와 UserProfile을 기준으로 결정한다.

```text
CharacterDefinition + UserProfile
→ StatCalculator
→ 최종 Unit 초기화 값
→ PlayerCharacterController
→ Player.Initialize(...)
```

현재 기본 Unit 초기화 값은 `Unit.Initialize(...)`가 받는 필드에 맞춘다.

| 값 | 의미 |
|----|------|
| `attackDamage` | 기본 공격력 |
| `moveSpeed` | 이동 속도 |
| `attackSpeed` | 공격 속도 |
| `maxHp` | 최대 체력 |
| `defense` | 방어력 |
| `maxShield` | 최대 쉴드 |

치명타, 사거리, 자원, 쿨다운 감소, 특수 자원 같은 추가 전투 스탯은 기본 Unit 초기화 값에 섞지 않는다. 이런 값은 부가 스탯 또는 Ability/Build/Character 전용 보정 요소로 분리한다.

## UserProfile 보정

UserProfile 보정은 두 종류를 모두 지원할 수 있어야 한다.

| 보정 종류 | 의미 | 예시 |
|-----------|------|------|
| 계정 공통 보정 | 모든 캐릭터에 적용되는 유저 성장값 | 최대 체력 +5%, 공격력 +3 |
| 캐릭터별 보정 | 특정 캐릭터에만 적용되는 숙련도/강화값 | Garen 방어력 +2, Jinx 공격속도 +0.1 |

초기 UserProfile 보정은 Add와 Multiply만 허용한다.

| 계산 방식 | 의미 | 예시 |
|-----------|------|------|
| Add | 기본값에 고정 수치를 더한다. | 공격력 +3, 방어력 +2 |
| Multiply | 기본값에 비율을 곱한다. | 최대 체력 +5%, 공격속도 +10% |

Add와 Multiply의 계산 순서, 반올림 규칙, 중첩 방식은 아직 확정하지 않는다. 이 항목은 추후 기획 측 결정이 필요하다.

체력 30% 이하일 때 방어력 증가, 특정 스킬 적중 후 공격력 증가 같은 조건부 효과는 UserProfile의 기본 스탯 계산에 넣지 않는다. 이런 효과는 추후 패시브, 버프, 상태 효과, 전투 중 Modifier 시스템에서 별도로 다룬다.

## UserProfile 패시브

`UserProfile`은 `AbilityDefinition` 에셋 참조를 직접 들지 않는다. UserProfile은 해금/성장/패시브 ID만 저장하고, 서버가 해당 ID를 검증한 뒤 게임 데이터에서 `AbilityDefinition`으로 변환한다.

Profile에서 공급되는 패시브는 `ability.md`의 `profilePassives`로 들어간다.

## 추후 논의 필요

| 주제 | 논의할 내용 |
|------|-------------|
| UserProfile 데이터 구조 | 계정 공통 보정과 캐릭터별 보정을 어떤 저장 형식으로 관리할지 결정해야 한다. |
| Add/Multiply 계산 규칙 | 계산 순서, 반올림, 중첩 방식을 기획 측에서 결정해야 한다. |
| 조건부 효과 | UserProfile 보정에서는 제외한다. 추후 패시브/버프/상태 효과/전투 중 Modifier 시스템에서 다룬다. |
| StatCalculator API | `CharacterDefinition`, `UserProfile`을 어떤 입력 타입으로 받고 최종 Unit 초기화 값을 어떤 구조로 반환할지 결정해야 한다. |
| 부가 전투 스탯 | 치명타, 사거리, 자원, 쿨다운 감소 같은 추가 스탯은 기본 Unit 초기화 값과 분리해 다룬다. |
