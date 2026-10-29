using UnityEngine;

/// <summary>
/// 어쌔신 특성 정의(할 일 125 5/5 — 할 일 128 어쌔신 레인이 12개를 만든다). 효과·수치는 기반 <see cref="TraitDefinition"/> 과 같다.
/// 데이터 테이블 시트 <see cref="TraitSheets.Assassin"/>(<c>Assassin</c> 이 아니라 <c>Traits_Assassin</c>) — 열 = 이 타입 에셋 파일 이름, 행 = <c>effects[i].필드</c>.
/// 에셋은 <c>9.ScriptableObject/Traits/Assassin/</c> 에 두고 <c>TraitPool_Assassin.asset</c> 에 넣는다.
/// </summary>
[DataTableSheet(TraitSheets.Assassin)]
[CreateAssetMenu(fileName = "AssassinTrait", menuName = "Traits/Assassin Trait")]
public sealed class AssassinTraitDefinition : TraitDefinition
{
    public override TutorialCharacter Character => TutorialCharacter.Assassin;
}
