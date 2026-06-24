# Ability

> 현재 브랜치: `feature/Wells&No.23`
>
> 이 문서는 Character가 사용하는 Ability 데이터와 실행 타입, 입력 슬롯, 패시브 구분을 다룬다.

## Character와 Ability 데이터 분리

`CharacterDefinition`은 스킬 상세 데이터를 직접 가지지 않는다. Character는 어떤 Ability를 사용하는지만 참조하고, Ability의 상세 수치와 실행 정보는 별도 `AbilityDefinition`으로 분리한다.

| 데이터 | 책임 |
|--------|------|
| `CharacterDefinition` | 캐릭터 ID, 표시 이름, 기본 스탯, Character Prefab, 사용할 Ability 목록 참조 |
| `AbilityDefinition` | ScriptableObject. 피해량, 쿨타임, 범위, 타겟팅 방식, VFX/애니메이션 키 등 Ability 기본 데이터 |

`AbilityDefinition`의 기본 데이터는 ScriptableObject로 확정한다. 런타임에 이 원본 값을 직접 수정하지 않는다.

AbilityDefinition 에셋 파일명은 캐릭터와 슬롯을 드러낸다.

```text
AbilityDefinition_Garen_BasicAttack.asset
AbilityDefinition_Garen_MainSkill.asset
AbilityDefinition_Jinx_BasicAttack.asset
```

공용 Ability는 `Common`을 사용한다.

```text
AbilityDefinition_Common_None.asset
```

쿨다운 남은 시간, 현재 차지량, 현재 스택 수, 임시 강화값처럼 전투 중 변하는 값은 `AbilityDefinition`에 저장하지 않는다. 이런 값은 Player 또는 Ability runtime 상태에서 별도로 관리한다.

전투 중 변하는 Ability 상태는 Ability별 인스턴스로 분리한다. 단, 실제 클래스 이름을 `Runtime`으로 확정하지는 않는다.

```text
AbilityDefinition
└── Ability 상태 인스턴스
    ├── definition 참조
    ├── remainingCooldown
    ├── currentStacks
    └── chargeAmount
```

Player가 모든 Ability 상태 값을 한 곳에 직접 들기보다, 각 Ability 상태 인스턴스가 자신의 현재 쿨다운, 스택, 차지량을 가진다. 실제 클래스명은 구현 시 결정한다.

Ability 상태의 권한은 서버에 둔다. 클라이언트에는 UI 표시를 위한 표시 상태만 복제한다. 초기 구현에서는 Ability 실행을 로컬 예측하지 않는다.

| 상태 | 책임 |
|------|------|
| 서버 권한 상태 | 실제 쿨다운, 스택, 차지량, 사용 가능 여부, 효과 적용 판정 |
| 클라이언트 표시 상태 | UI 쿨다운 표시, 스택 표시, 버튼 활성/비활성 표시 |

클라이언트가 Ability 사용 입력을 보내더라도 최종 사용 가능 판정과 효과 적용은 서버가 결정한다.

Ability 표시 상태는 슬롯 5개를 하나의 묶음으로 복제한다. 슬롯별 NetworkVariable을 따로 두기보다, UI가 한 번에 읽을 수 있는 표시 상태 묶음을 우선한다. 표시 상태 묶음 타입 이름은 아직 확정하지 않는다.

슬롯별 표시 상태 타입 이름은 `AbilitySlotDisplayState`로 확정한다. 표시 상태 묶음은 Ability 슬롯과 동일하게 명시 필드 5개를 가진다.

```text
Ability 표시 상태 묶음
├── basicAttack: AbilitySlotDisplayState
├── secondaryAction: AbilitySlotDisplayState
├── mainSkill: AbilitySlotDisplayState
├── subSkill: AbilitySlotDisplayState
└── ultimateSkill: AbilitySlotDisplayState
```

`AbilitySlotDisplayState`에는 다음 값을 포함한다.

| 값 | 의미 |
|----|------|
| `cooldownEndTime` | 서버 권한 `GameTime` 기준 쿨다운 종료 시각 |
| `currentStacks` | UI에 표시할 현재 스택 수 |
| `isUsable` | 서버가 판단한 현재 사용 가능 여부 |

`AbilitySlotDisplayState`는 NGO `INetworkSerializable` struct로 둔다.

| 필드 | 타입 | 기준 |
|------|------|------|
| `cooldownEndTime` | `double` | 서버 권한 `GameTime` 기준 |
| `currentStacks` | `int` | 스택 없는 Ability는 0 |
| `isUsable` | `bool` | 서버 판정 결과 |

`isUsable`은 클라이언트가 자체 계산하지 않고 서버 판정 결과를 복제한다. 상태 이상, 자원, 쿨다운, Character/Ability 특수 규칙이 섞이면 클라이언트 계산이 틀릴 수 있기 때문이다.

표시 상태는 런타임에 변하는 값만 가진다. UI 아이콘, 이름, 설명 같은 정적 표시는 `AbilityDefinition`에서 읽는다.

기본 해석 규칙:

| 상황 | 표시 상태 |
|------|-----------|
| 스택 없는 Ability | `currentStacks = 0` |
| 쿨다운 없는 Ability | `cooldownEndTime = 0` 또는 현재 `GameTime` 이하 |
| `NoneAbility` | `isUsable = false` |
| 입력으로 재발동 가능한 `SlotPassive` | 서버 판정에 따라 `isUsable`과 `cooldownEndTime`을 사용 |

`currentStacks`의 최대값은 초기에는 복제하지 않는다. 기본 최대 스택은 `AbilityDefinition`에서 읽고, 런타임에 최대값이 변하는 규칙이 생기면 표시 상태에 추가한다.

`isUsable == false`인 이유는 초기에는 복제하지 않는다. 필요해지면 `AbilityUseResult` 확장으로 처리한다.

표시 상태 묶음도 NGO `INetworkSerializable` struct로 둔다. 구체 타입명은 아직 확정하지 않지만, 내부 구조는 명시 필드 5개로 고정한다.

Ability 표시 상태 묶음은 `NetworkVariable<T>` 하나로 복제한다. 초기 구현에서는 슬롯별 NetworkVariable을 따로 두지 않는다.

표시 상태 갱신 타이밍:

- Ability 사용 성공
- 쿨다운 종료
- 스택 변경
- 상태 이상 변경
- 자원/조건 변경으로 `isUsable`이 바뀌는 경우

쿨다운 종료 시점에는 서버 타이머로 표시 상태를 갱신한다. 클라이언트가 종료 시각을 보고 임의로 최종 사용 가능 상태를 확정하지 않는다. UI 표시와 실제 판정 모두 서버 복제 상태를 기준으로 한다.

## 쿨다운

Ability 쿨다운은 서버 권한 상태를 기준으로 한다. 클라이언트는 `UseSlot(...)` 호출 직후 로컬에서 즉시 쿨다운 UI를 시작하지 않고, 서버 승인/복제 상태를 받은 뒤 표시를 갱신한다.

쿨다운 시간 기준은 서버 권한 `GameTime`이다. 클라이언트 UI에는 매 tick 남은 시간을 복제하지 않고, 쿨다운 종료 `GameTime`을 복제한다. 클라이언트는 복제된 종료 시각과 표시용 `GameTime`을 바탕으로 남은 시간을 계산해 보여준다.

기본 쿨다운은 `AbilityDefinition` ScriptableObject에 둔다. 쿨다운 감소 같은 보정은 부가 전투 스탯 또는 Modifier에서 계산하고, 서버가 최종 쿨다운을 결정한다. 쿨다운은 주로 `Active` Ability에 적용하지만, `SlotPassive`도 설계에 따라 쿨다운/재발동 대기 시간이 필요할 수 있으므로 구조상 배제하지 않는다.

```text
AbilityDefinition.baseCooldown
+ cooldown modifiers
= final cooldown
→ cooldown end GameTime
→ replicated display state
```

## 입력 전달

Ability는 직접 입력을 구독하지 않는다. Player 입력 계층이 입력을 읽고, Ability 슬롯 단위로 `PlayerAbilityController`에 전달한다.

```text
PlayerInput
→ PlayerInputReader
→ PlayerAbilityController
→ UseSlot(MainSkill)
→ 해당 Ability 상태 인스턴스 실행 요청
→ 서버 판정
```

이 구조에서는 Ability가 키보드/마우스 입력을 직접 알 필요가 없다. Ability는 자신이 어떤 슬롯에 배치되었는지와 실행 요청이 들어왔는지만 알면 된다.

`PlayerAbilityController`는 Ability 실행만 담당한다. Character 장착, Character Prefab 생성, 스탯 초기화, Ability 목록 구성은 `PlayerCharacterController` 책임으로 분리한다.

슬롯 실행 API는 `UseSlot(AbilitySlot slot)`으로 확정한다. 입력 계층은 이 public API만 호출한다.

```csharp
public enum AbilitySlot
{
    BasicAttack,
    SecondaryAction,
    MainSkill,
    SubSkill,
    UltimateSkill
}
```

클라이언트가 Ability 사용을 요청할 때도 `AbilitySlot`을 서버 RPC로 보낸다. 서버 요청 RPC 이름은 `RequestUseSlotRpc(AbilitySlot slot)`으로 한다. 클라이언트는 로컬 표시 상태의 `isUsable == true`일 때만 요청을 보낸다. 이 로컬 게이트는 입력 반응성과 불필요한 RPC 감소를 위한 것이며, 최종 판정은 서버가 수행한다. 서버는 현재 장착된 Character의 해당 슬롯 Ability를 찾아 사용 가능 여부를 검증하고 실행한다.

서버 내부 실행 함수는 `TryUseSlot(AbilitySlot slot)`으로 둔다. 초기 반환 타입은 `bool`로 둔다. 쿨다운, 상태 이상, `NoneAbility`, 자원 부족 같은 이유로 실패할 수 있다.

```text
UseSlot(slot)
→ RequestUseSlotRpc(slot)
→ TryUseSlot(slot)
→ 성공 시 Ability 실행
→ 실패 시 스킬 미사용으로 처리
```

서버가 Ability 요청을 거절하면 실패 UI는 표시하지 않는다. 거절된 요청은 스킬 미사용으로 처리하며, 필요한 경우 서버 디버그 로그만 남긴다.

`NoneAbility` 사용 요청은 조용히 무시한다. 빈 슬롯 입력은 실패 UI를 띄우지 않는다.

추후 실패 사유를 UI에 표시해야 하면 결과 enum으로 확장한다. 이때 enum 이름은 `AbilityUseResult`로 두고, 성공 값은 `Success`를 포함한다.

`NoneAbility`는 별도의 `AbilitySlot` 값을 갖지 않는다. 슬롯은 `CharacterDefinition`의 필드 위치가 결정하고, `AbilityDefinition`은 자신의 `AbilityExecutionType`만 가진다.

## 슬롯 Ability

Ability 슬롯은 아래 이름을 기준으로 고정한다.

| 슬롯 | 의미 |
|------|------|
| `BasicAttack` | 기본 공격 |
| `SecondaryAction` | 캐릭터별 보조 액션. 인터럽트, 방어, 특수 공격, 스택 발동 등 공격이 아닌 행동도 포함할 수 있다. |
| `MainSkill` | 캐릭터의 주력 스킬 |
| `SubSkill` | 캐릭터의 보조 스킬 |
| `UltimateSkill` | 궁극기 |

기본 입력 매핑은 다음과 같다.

| 기본 입력 | Ability 슬롯 |
|-----------|--------------|
| 좌클릭 | `BasicAttack` |
| 우클릭 | `SecondaryAction` |
| Q | `MainSkill` |
| E | `SubSkill` |
| R | `UltimateSkill` |

문서와 코드에서는 가능하면 “Q 스킬”보다 `MainSkill`처럼 슬롯 이름을 기준으로 부른다. 키 리바인딩이 생겨도 게임플레이 슬롯 이름은 유지되고, 입력 매핑만 바뀌어야 한다.

모든 캐릭터가 모든 입력 슬롯을 반드시 채울 필요는 없다. 특정 캐릭터는 일부 슬롯이 비어 있을 수 있다.

## Ability 실행 타입

Ability 실행 타입은 명시적으로 구분한다.

```csharp
public enum AbilityExecutionType
{
    None,
    Active,
    SlotPassive,
    GlobalPassive
}
```

| 타입 | 의미 |
|------|------|
| `None` | 빈 슬롯을 나타내는 No-op Ability |
| `Active` | 입력으로 발동되는 Ability |
| `SlotPassive` | 슬롯에 표시되지만 입력으로 발동하지 않는 패시브 Ability |
| `GlobalPassive` | 특정 슬롯과 무관한 캐릭터 전체 패시브 Ability |

| 위치 | 허용 타입 | 개수 |
|------|-----------|------|
| 슬롯 Ability 필드 | `None`, `Active`, `SlotPassive` | 명시 필드 5개 |
| `globalPassiveAbilities` | `GlobalPassive` | 가변 개수 |

예상 구조:

```text
CharacterDefinition
├── basicAttack
├── secondaryAction
├── mainSkill
├── subSkill
├── ultimateSkill
└── characterPassives
```

## 슬롯 패시브와 UI

슬롯에 들어간 Ability가 패시브인 경우에도 해당 슬롯 UI에는 표시한다. 이때 입력 가능/쿨다운 UI가 아니라 패시브 아이콘과 설명으로 보여준다.

예를 들어 `MainSkill` 슬롯이 패시브라면 플레이어는 Q 입력으로 발동하지 않지만, Q 슬롯 위치에서 해당 캐릭터의 패시브 능력을 확인할 수 있어야 한다.

입력 슬롯이 비어 있는 경우, 해당 슬롯에는 `NoneAbility`를 넣는다. `NoneAbility`는 입력을 받아도 아무 동작도 하지 않는 명시적 빈 Ability이다.

`NoneAbility`도 `AbilityDefinition` ScriptableObject로 만든다. `NoneAbility`의 `AbilityExecutionType`은 `None`이다. 이렇게 하면 5개 슬롯 필드가 항상 `AbilityDefinition` 참조로 채워지고, null 체크를 줄일 수 있다. UI에서는 `NoneAbility`를 빈 슬롯 아이콘/설명으로 처리한다.

`NoneAbility`는 슬롯 Ability 필드 전용이다. `globalPassiveAbilities`는 가변 리스트이므로 패시브가 없으면 빈 리스트로 둔다.

`NoneAbility`는 슬롯별로 따로 만들지 않고 `AbilityDefinition_Common_None.asset` 하나를 공용으로 사용한다.

## 글로벌 패시브

`GlobalPassive`는 슬롯과 같은 레이어나 개수를 가질 필요가 없다. 대신 출처별로 분리해 관리한다.

| 구분 | 의미 | 예시 |
|------|------|------|
| `characterPassives` | 캐릭터가 기본으로 가진 고유 패시브 | Garen 기본 방어 보정, Jinx 스택 규칙 |
| `profilePassives` | 유저 성장/프로필로 추가되는 패시브 | 계정 레벨 보너스, 캐릭터 숙련도 효과 |
| `equipmentPassives` | 장비/빌드/아이템으로 추가되는 패시브 | 특정 장비 착용 시 흡혈, 치명타 효과 |

`CharacterDefinition`에는 `characterPassives`만 둔다. `profilePassives`는 UserProfile의 ID를 서버가 `AbilityDefinition`으로 변환해 런타임에 조립하고, `equipmentPassives`는 빌드/장비 시스템이 런타임에 공급한다.

`equipmentPassives`는 Player/Character의 내부 규칙이 아니라 빌드/장비 시스템이 공급하는 외부 패시브이다. 이 문서에서는 Ability로 주입될 수 있는 연결 지점만 다루고, 장비 슬롯, 빌드 구성, 해금, 중복 규칙 같은 상세는 `builds.md`에서 다룬다.

저장/구성은 출처별로 분리하지만, 실행/평가 시에는 통합된 글로벌 패시브 목록처럼 처리한다.

```text
allGlobalPassives
= characterPassives
+ profilePassives
+ equipmentPassives
```

## 추후 논의 필요

| 주제 | 논의할 내용 |
|------|-------------|
| AbilityDefinition 상세 구조 | 피해량, 쿨타임, 범위, 타겟팅, VFX/애니메이션 참조를 어떤 필드로 나눌지 결정해야 한다. |
| Ability 상태 클래스명 | Ability별 상태 인스턴스를 분리한다. 단, 실제 클래스명을 `Runtime`으로 확정하지 않는다. |
| Ability 상태 복제 | 슬롯 5개 표시 상태를 하나의 `NetworkVariable<T>` 묶음으로 복제한다. 묶음 타입명은 미정이며, 슬롯별 상태 타입은 `AbilitySlotDisplayState`로 확정한다. |
| 표시 상태 직렬화 | `AbilitySlotDisplayState`와 표시 상태 묶음은 NGO `INetworkSerializable` struct로 둔다. |
| Ability 입력 전달 API | `PlayerAbilityController.UseSlot(AbilitySlot slot)`으로 확정한다. 서버 RPC는 `RequestUseSlotRpc(AbilitySlot slot)`, 서버 내부 실행은 `bool TryUseSlot(AbilitySlot slot)`을 사용한다. |
| Ability 사용 결과 타입 | 초기에는 `bool`을 사용한다. 추후 세분화가 필요하면 `AbilityUseResult` enum으로 확장하고 `Success` 값을 포함한다. |
| 쿨다운 복제 구조 | 표시 상태에 `double cooldownEndTime`을 포함한다. `Active`를 기본 대상으로 하되 `SlotPassive` 쿨다운 가능성도 열어둔다. |
| Ability 표시 상태 묶음 타입명 | 아직 확정하지 않는다. |
| 쿨다운 종료 갱신 | 서버 타이머로 처리한다. 클라이언트는 서버 복제 상태를 기준으로 표시한다. |
| 슬롯 Ability 실행 방식 | 각 슬롯의 Ability가 입력 발동 액티브인지, 입력을 받지 않는 패시브인지 구분하는 기준이 필요하다. |
| 글로벌 패시브 Ability 처리 | 출처별로 `characterPassives`, `profilePassives`, `equipmentPassives`를 분리한다. 각 패시브를 어떤 이벤트/시점에 평가할지 결정해야 한다. |
| Ability UI 표시 규칙 | 슬롯 패시브는 해당 슬롯 UI에 패시브로 표시하고, 글로벌 패시브는 별도 패시브 UI 또는 캐릭터 정보 UI에 표시한다. |
| 장비/빌드 패시브 상세 | `equipmentPassives`의 상세 규칙은 `builds.md`에서 다룬다. |
