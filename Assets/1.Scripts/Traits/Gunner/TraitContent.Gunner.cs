/// <summary>
/// 거너 특성 콘텐츠 등록(할 일 127 거너 레인 전용 파일 — 공용 파일·다른 캐릭터 파일은 고치지 않는다, 할 일 125 5/5).
/// 여기서만 등록한다: 카드 설명 {dmg} 계산식(<c>r.Damage</c>) · 툴팁 별칭(<c>r.Value</c> — 예 집속 연사 <c>totalDamageRatio</c> = 발수 × 발당 배율) · 2차 상충 규칙(<c>r.Conflict</c>).
/// 다른 캐릭터 ID 를 등록하면 등록부 오류(EditMode <c>GunnerTraitTests</c> 가 잡는다).
/// </summary>
public static partial class TraitContent
{
    static partial void RegisterGunner(TraitContentRegistry.Registrar r)
    {
        // 예: r.Damage("GUN_4_EXTRA_BASIC", (trait, player) => SkillTooltipDamage.FromLinear(player, 계수, 고정값));
    }
}
