using UnityEngine;

/// <summary>
/// 거너 특성 정의(할 일 125 5/5 — 할 일 127 거너 레인이 12개를 만든다). 효과·수치는 기반 <see cref="TraitDefinition"/> 과 같다.
/// 데이터 테이블 시트 <see cref="TraitSheets.Gunner"/>(<c>Gunner</c> 가 아니라 <c>Traits_Gunner</c>) — 열 = 이 타입 에셋 파일 이름, 행 = <c>effects[i].필드</c>.
/// 에셋은 <c>9.ScriptableObject/Traits/Gunner/</c> 에 두고 <c>TraitPool_Gunner.asset</c> 에 넣는다.
/// </summary>
[DataTableSheet(TraitSheets.Gunner)]
[CreateAssetMenu(fileName = "GunnerTrait", menuName = "Traits/Gunner Trait")]
public sealed class GunnerTraitDefinition : TraitDefinition
{
    public override TutorialCharacter Character => TutorialCharacter.Gunner;
}
