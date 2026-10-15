# Garen

> Character 공통 규칙은 `../character.md`, Ability 공통 규칙은 `../ability.md`를 따른다.
>
> 이 문서는 Garen의 캐릭터 고유 컨셉과 Ability 슬롯 구성을 요약한다. 실제 수치는 ScriptableObject 에셋을 단일 출처로 둔다.

## 기본 정보

| 항목 | 값 |
|------|----|
| 표시 이름 | Garen |
| `characterId` | `garen` |
| CharacterDefinition | `CharacterDefinition_Garen.asset` |
| Character Prefab | TBD |

## 캐릭터 요약

Garen은 근거리 전투를 담당하는 캐릭터이다. 방어적 보조 액션과 근접 교전 중심의 Ability 구성을 가진다.

## Ability 슬롯

| 슬롯 | AbilityDefinition | 요약 |
|------|-------------------|------|
| `basicAttack` | TBD | 근거리 기본 공격 |
| `secondaryAction` | TBD | 방어/패링 계열 보조 액션 후보 |
| `mainSkill` | TBD | 주력 근접 스킬 |
| `subSkill` | TBD | 보조 근접 스킬 |
| `ultimateSkill` | TBD | 궁극기 |

- 기본 공격 대시 취소(2026-10-09, 세 캐릭터 공통): 기본 공격 중 **언제든** 대시로 끊는다(판정이 나갔으면 피해는 유지, 남은 동작만 끊김). 취소하면 콤보는 1타부터, 예약된 다음 타는 버린다.

## Character Passives

| 패시브 | 요약 |
|--------|------|
| TBD | Garen 고유 패시브 후보 |

## 범위 밖

- UserProfile 보정은 이 문서에서 다루지 않는다.
- `profilePassives`는 이 문서에서 다루지 않는다.
- `equipmentPassives`는 이 문서에서 다루지 않는다.
- 실제 피해량, 쿨타임, 계수 같은 수치는 AbilityDefinition 에셋에서 관리한다.
