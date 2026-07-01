# Player

> 이 문서는 `Player`의 코드/Prefab 책임을 다룬다. `Character`와 `Ability` 상세 규칙은 각각 `character.md`, `ability.md`에서 다룬다.

## 개념 분리

| 개념 | 의미 | 상세 문서 |
|------|------|-----------|
| `Unit` | 체력, 방어력, 쉴드, 상태 이상 같은 전투 가능한 공통 기반 | `unit.md` |
| `Player` | 접속한 사용자가 조작하는 네트워크 오브젝트 | 이 문서 |
| `Character` | Player에 장착되는 캐릭터 데이터와 표현 | `character.md` |
| `Ability` | Character가 사용하는 액티브/패시브 능력 | `ability.md` |

`Player`와 `Character`는 같은 개념이 아니다.

- `Player`는 게임 안에서 조작되는 주체이다.
- `Character`는 그 Player가 어떤 전투 스타일과 스킬 세트를 사용할지 결정한다.
- Garen, Jinx 같은 세부 캐릭터는 별도 캐릭터 문서로 분리한다.

## Prefab 구조

캐릭터는 `Player Prefab`의 자식 오브젝트로 장착한다. 네트워크 오브젝트는 부모 `Player`만 가지며, Character Prefab은 비-NetworkObject 표현 계층으로 둔다.

```text
PlayerPrefab
├── Player
├── PlayerMovement
├── PlayerInputReader
├── PlayerGrabController
├── PlayerCharacterController
├── PlayerAbilityController
└── CharacterSlot
    └── Garen 또는 Jinx Character Prefab
```

부모 `PlayerPrefab`은 네트워크와 조작의 기준점이다. 자식 `Character`는 외형, 애니메이션, 스킬 소켓, 캐릭터별 데이터와 스킬 구성을 담당한다.

`CharacterSlot`은 Player Prefab의 필수 Transform이다. `PlayerCharacterController`가 serialized field로 참조하며, 없으면 서버에서 에러로 처리한다.

## 책임 분리

| 책임 | 위치 |
|------|------|
| 네트워크 오브젝트 소유권 | `Player` |
| 체력/방어력/쉴드/상태 이상 | 부모 `Player : Unit` |
| 입력 읽기 | `PlayerInputReader` |
| Ability 입력 전달 | `PlayerAbilityController` |
| 이동/회전 | `PlayerMovement` 및 회전 컴포넌트 |
| 보스 잡기 대응 | `PlayerGrabController` |
| Character 장착/초기화 | `PlayerCharacterController` |
| 캐릭터별 기본 스탯 | 장착된 `Character` 데이터 |
| 캐릭터별 스킬/공격 | 장착된 `Character` 또는 Character Ability 컴포넌트 |
| 모델/Animator/VFX 소켓 | 자식 `Character` 오브젝트 |

보스가 잡거나 공격하는 대상은 자식 `Character`가 아니라 부모 `Player`이다. 체력과 피격 판정이 부모 `Player : Unit`에 있어야 UI, 네트워크 동기화, 사망 처리, 보스 패턴 연동이 한 곳으로 모인다.

## Character 장착 흐름

로비에서 플레이어가 사용할 Character는 게임 시작 전에 확정된다.

예상 흐름:

1. 로비에서 플레이어가 사용할 Character를 확정한다.
2. 서버가 선택된 Character 데이터와 UserProfile을 확인한다.
3. Character 기본 스탯과 UserProfile 보정값을 합산해 최종 스탯을 계산한다.
4. Player Prefab이 네트워크로 스폰된다.
5. 선택된 Character가 `CharacterSlot` 하위에 장착된다.
6. 최종 스탯으로 부모 `Player : Unit`을 초기화한다.
7. 캐릭터별 스킬/공격 컴포넌트가 부모 Player의 입력, 상태, 타겟팅 정보를 참조한다.

이 구조에서는 체력과 피격 대상이 하나로 유지된다. Garen/Jinx가 서로 다른 체력, 공격력, 이동 속도, 스킬을 가져도 실제 피해를 받는 네트워크 Unit은 부모 `Player` 하나이다.

## Ability 입력 전달

Ability 입력은 각 Ability가 직접 구독하지 않는다. Player 입력 계층이 입력을 읽고, 슬롯 단위로 `PlayerAbilityController`에 전달한다.

```text
PlayerInput
→ PlayerInputReader
→ PlayerAbilityController
→ UseSlot(MainSkill)
→ 해당 Ability 상태 인스턴스 실행 요청
→ 서버 판정
```

이 구조에서는 키 리바인딩, 입력 잠금, 상태 이상으로 인한 입력 차단, UI 잠금 같은 공통 처리를 Player 입력 계층에서 일관되게 처리할 수 있다.

`PlayerAbilityController`는 Ability 실행만 담당한다. Character 장착, Character Prefab 생성, 스탯 초기화, Ability 목록 구성은 `PlayerCharacterController` 책임으로 분리한다.

슬롯 실행 API는 `UseSlot(AbilitySlot slot)`으로 확정한다. 입력 계층은 이 public API만 호출한다. 클라이언트가 서버에 Ability 사용을 요청할 때는 `RequestUseSlotRpc(AbilitySlot slot)`을 사용하고, 서버 내부에서는 `bool TryUseSlot(AbilitySlot slot)`이 현재 장착된 Character의 해당 슬롯 Ability를 검증/실행한다.

## 현재 코드 기준

현재 `Assets/1.Scripts/Player`에는 다음 컴포넌트가 있다.

| 파일 | 현재 역할 |
|------|-----------|
| `Player.cs` | `Unit`을 상속하며, 서버 `OnNetworkSpawn`에서 기본 전투 수치를 초기화한다. |
| `PlayerInputReader.cs` | Unity Input System의 `Move` 액션을 읽어 이동 입력 방향을 제공한다. |
| `PlayerMovement.cs` | `Rigidbody` 기반 이동과 회전을 처리한다. |
| `PlayerInputScript.cs` | `CharacterController` 기반 이동 실험/이전 구현으로 보인다. 현재 주 이동 구조와 중복될 수 있다. |
| `PlayerGrabController.cs` | 보스 잡기 패턴에 의해 플레이어가 고정, 피해, 던지기, 착지 피해를 받는 흐름을 처리한다. |
| `PlayerRotation.cs` / `PlayerRotation_RotateTowards.cs` | 회전 처리 실험/대안 구현으로 보인다. `PlayerMovement`의 회전 처리와 역할 중복 여부를 검토해야 한다. |

## 추후 논의 필요

| 주제 | 논의할 내용 |
|------|-------------|
| 런타임 Character 교체 | 대부분의 Character는 런타임 중 교체하지 않는다. 예외는 추후 별도 논의한다. |
| Ability 입력 전달 컴포넌트 | `PlayerAbilityController`로 확정한다. 이 컴포넌트는 Ability 실행만 담당하며, public API는 `UseSlot(AbilitySlot slot)`을 사용한다. |
| 중복 이동/회전 컴포넌트 정리 | `PlayerInputScript`, `PlayerRotation`, `PlayerRotation_RotateTowards`를 유지할지 정리할지 결정해야 한다. |
