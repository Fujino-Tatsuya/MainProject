/// <summary>
/// 팔라딘 특성 콘텐츠 등록(할 일 126 팔라딘 레인 전용 파일 — 공용 파일·다른 캐릭터 파일은 고치지 않는다, 할 일 125 5/5).
/// 여기서만 등록한다: 카드 설명 {dmg} 계산식(<c>r.Damage</c>) · 툴팁 별칭(<c>r.Value</c>) · 2차 상충 규칙(<c>r.Conflict</c>).
/// 다른 캐릭터 ID 를 등록하면 등록부 오류(EditMode <c>PaladinTraitTests</c> 가 잡는다).
/// 별칭 = 명세 '문법 원문'의 제안 필드 이름이 효과 필드 이름과 다를 때(템플릿은 명세 원문 그대로 둔다).
/// </summary>
public static partial class TraitContent
{
    static partial void RegisterPaladin(TraitContentRegistry.Registrar r)
    {
        // 수호 재정비 — 명세 cooldownReduction(0.15) = 효과 cooldownChange(−0.15)의 부호 반대.
        r.Value("PAL_1_E_COOLDOWN", "cooldownReduction", trait => PaladinTraitValues.CooldownReduction(trait));
        // 돌파의 방패 — {dmg} = 교체 Q 1회(DQfull × 1.25, 명세 §8). dashDistance 는 효과 필드 이름 그대로라 별칭 없음.
        // 2차 상충 없음 — 1차(진격 강화·수호 재정비·불굴의 회복) 중 이 모드에서 효과를 잃는 것이 없다(진격 강화는 Dref 로 들어간다).
        r.Damage("PAL_2_DASH", (trait, player) => PaladinTraitValues.DashDamage(trait, player));
        // 나누는 불굴 — 명세 allyRange(12m) = 효과 overflowShareRange.
        r.Value("PAL_2_SHARE_HEAL", "allyRange", trait => PaladinTraitValues.AllyRange(trait));
        // 제압의 진격 — 명세 stunDuration(0.75초) = 밀치기 효과 concussionDuration.
        r.Value("PAL_3_Q_STUN", "stunDuration", trait => PaladinTraitValues.StunDuration(trait));
        // 수호의 잔광 — 명세 shieldMaxHpRatio(0.1)·shieldDuration(4초) = 보호막 효과 amountRatio·duration.
        r.Value("PAL_3_E_SHIELD", "shieldMaxHpRatio", trait => PaladinTraitValues.ShieldMaxHpRatio(trait));
        r.Value("PAL_3_E_SHIELD", "shieldDuration", trait => PaladinTraitValues.ShieldDuration(trait));
        // 불굴의 순환 — 명세 refundSeconds = 효과 필드 이름 그대로라 별칭 없음.
        // 수호의 파동 — 명세 radius(3m) = 스킬 수치 효과 PAL_E_PUSH_RADIUS. pushDistance·pushDuration 은 밀치기 효과 필드 이름 그대로.
        r.Value("PAL_4_E_PUSH", "radius", trait => PaladinTraitValues.EPushRadius(trait));
        // 진격의 충격 — {dmg} = 현재 Q 묶음(DQpacket) × 0.30(명세 §8).
        r.Damage("PAL_4_Q_BLAST", (trait, player) => PaladinTraitValues.QBlastDamage(trait, player));
        // 불굴의 가호 — 명세 healToShieldRatio(0.75)·shieldDuration(4초)·shieldCapMaxHpRatio(0.1) = 보호막 효과 amountRatio·duration·capMaxHpRatio.
        r.Value("PAL_4_HEAL_SHIELD", "healToShieldRatio", trait => PaladinTraitValues.HealToShieldRatio(trait));
        r.Value("PAL_4_HEAL_SHIELD", "shieldDuration", trait => PaladinTraitValues.ShieldDuration(trait));
        r.Value("PAL_4_HEAL_SHIELD", "shieldCapMaxHpRatio", trait => PaladinTraitValues.ShieldCapMaxHpRatio(trait));    }
}
