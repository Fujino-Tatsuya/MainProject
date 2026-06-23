# Jinx

> Character 공통 규칙은 `../character.md`, Ability 공통 규칙은 `../ability.md`를 따른다.
>
> 이 문서는 Jinx의 캐릭터 고유 컨셉과 Ability 슬롯 구성을 요약한다. 실제 수치는 ScriptableObject 에셋을 단일 출처로 둔다.

## 기본 정보

| 항목 | 값 |
|------|----|
| 표시 이름 | Jinx |
| `characterId` | `jinx` |
| CharacterDefinition | `CharacterDefinition_Jinx.asset` |
| Character Prefab | TBD |

## 캐릭터 요약

Jinx는 원거리 전투를 담당하는 캐릭터이다. 기본 공격 또는 스킬을 통해 쌓는 스택과 그 스택을 활용하는 보조 액션 후보를 가진다.

## Ability 슬롯

| 슬롯 | AbilityDefinition | 요약 |
|------|-------------------|------|
| `basicAttack` | TBD | 원거리 기본 공격 |
| `secondaryAction` | TBD | 스택 발동 계열 보조 액션 후보 |
| `mainSkill` | TBD | 주력 원거리 스킬 |
| `subSkill` | TBD | 보조 원거리 스킬 |
| `ultimateSkill` | TBD | 궁극기 |

## Character Passives

| 패시브 | 요약 |
|--------|------|
| TBD | Jinx 고유 패시브 또는 스택 규칙 후보 |

## 범위 밖

- UserProfile 보정은 이 문서에서 다루지 않는다.
- `profilePassives`는 이 문서에서 다루지 않는다.
- `equipmentPassives`는 이 문서에서 다루지 않는다.
- 실제 피해량, 쿨타임, 계수 같은 수치는 AbilityDefinition 에셋에서 관리한다.
