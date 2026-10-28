using UnityEngine;

/// <summary>
/// 팔라딘 특성 정의(할 일 125 5/5 — 할 일 126 팔라딘 레인이 12개를 만든다). 효과·수치는 기반 <see cref="TraitDefinition"/> 과 같다.
/// 데이터 테이블 시트 <see cref="TraitSheets.Paladin"/>(<c>Paladin</c> 이 아니라 <c>Traits_Paladin</c>) — 열 = 이 타입 에셋 파일 이름, 행 = <c>effects[i].필드</c>.
/// 에셋은 <c>9.ScriptableObject/Traits/Paladin/</c> 에 두고 <c>TraitPool_Paladin.asset</c> 에 넣는다.
/// </summary>
[DataTableSheet(TraitSheets.Paladin)]
[CreateAssetMenu(fileName = "PaladinTrait", menuName = "Traits/Paladin Trait")]
public sealed class PaladinTraitDefinition : TraitDefinition
{
    public override TutorialCharacter Character => TutorialCharacter.Paladin;
}
