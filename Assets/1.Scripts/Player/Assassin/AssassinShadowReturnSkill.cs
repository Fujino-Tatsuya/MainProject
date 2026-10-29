using UnityEngine;

/// <summary>
/// 그림자 회귀(ASN_2_RETURN, 할 일 128 5b) 복귀 돌진 — 일반·변신 Q 각각의 후속 스킬(공용 후속 시전 장치, <see cref="PlayerSkillBase.FollowUpSkill"/>).
/// 원래 Q(<see cref="AssassinDashStrikeSkill"/>)의 돌진 단계 코드를 그대로 쓰고 세 가지만 바꾼다:
/// - 방향·거리 = 사용 순간 현재 위치 → 원래 Q 가 저장한 출발점, 최대 = 현재 Q 최대 거리(<see cref="AssassinShadowReturnRules.Resolve"/>).
///   지형·낭떠러지 앞 정지는 기존 대시 이동 규칙, 저장점 도착 보장 없음, 순간이동·무적 추가 없음. 출발점은 서버가 판정용으로, 각 피어가 이동·연출용으로 저장한다.
/// - 피해 = 이 단계 실행 때 원래 Q 기준 피해(Dref) × 복귀 비율 — 같은 적도 출발·복귀 각각 맞는다. 추격의 궤적도 이 값 기준(왕복 각 1회).
/// - 적중 장부 = 원래 Q 장부를 이어 써 분노·변신 Q 쿨 차감은 왕복 전체 최초 유효 적중 1회(출발 빗나감 + 복귀 적중이면 복귀에서 인정).
/// 시전 ID 는 원래 Q 를 이어받는다(장치의 followUpInheritsCastId). 쿨은 없다(데이터 0) — 사용 순간 장치가 원래 Q 쿨을 시작한다.
/// 데이터 = 원래 Q 와 같은 애니 상태·아이콘의 <see cref="PlayerSkillData"/>(돌진 수치는 원래 Q 데이터를 읽는다), HUD 색 = followUpIconTint.
/// </summary>
public sealed class AssassinShadowReturnSkill : AssassinDashStrikeSkill
{
    private PlayerMovement movement;

    private AssassinDashStrikeSkill Source => FollowUpSource as AssassinDashStrikeSkill;

    // 돌진 거리·속도·폭·판정 레이어 = 원래 Q(현재 상태) 데이터.
    internal override AssassinDashStrikeSkillData DashData => Source != null ? Source.DashData : null;

    protected override PlayerSkillBase CooldownRewardSkill => Source != null ? Source : this;

    public override void Initialize(Player owner, PlayerSkillController controller)
    {
        base.Initialize(owner, controller);
        movement = owner != null ? owner.GetComponent<PlayerMovement>() : null;
    }

    protected override void BeginServerStage(Vector3 requestedDirection, AssassinDashStrikeSkillData data,
        out Vector3 direction, out float distance, out int stageDref, out AssassinDashHitLedger stageLedger)
    {
        AssassinDashStrikeSkill source = Source;
        Vector3 position = owner.transform.position;
        if (source == null || !source.ServerReturnOrigin.TryTake(out Vector3 origin))
        {
            // 출발점 없음(장치 밖 호출 등) — 제자리에서 끝난다(이동·판정 0).
            Debug.LogWarning("[Assassin/Q] 그림자 회귀 출발점이 없어 복귀 돌진을 제자리에서 끝낸다.", this);
            origin = position;
        }

        AssassinShadowReturnRules.Resolve(position, origin, data.DashDistance, owner.transform.forward, out direction, out distance);

        // 이 단계 실행 때의 원래 Q 기준 피해 × 복귀 비율 — 지금 형태(일반/변신, 장치가 원래 Q 세트로 본다)의 특성 배율 포함.
        PlayerSkillData sourceData = source != null ? source.Data : null;
        int dref = sourceData != null
            ? owner.ResolveDamage(sourceData.AttackDamageMultiplier, sourceData.FlatDamageBonus,
                TraitAttackKey.ForSkill(Slot, controller != null && controller.IsAlternateSkill(this)))
            : 0;
        stageDref = AssassinShadowReturnRules.StageDamage(dref, ReturnDamageRatio);

        stageLedger = source != null ? source.Ledger : new AssassinDashHitLedger();
        stageLedger.BeginNextStage();
        Edit.Log($"[Assassin/Q] 그림자 회귀 복귀 — {distance:F2}m, 기준 {dref} × {ReturnDamageRatio:0.##} = {stageDref}", this);
    }

    protected override void ResolveLocalStage(Vector3 requestedDirection, AssassinDashStrikeSkillData data,
        out Vector3 direction, out float distance)
    {
        Vector3 position = owner != null ? owner.transform.position : Vector3.zero;
        AssassinDashStrikeSkill source = Source;
        if (source == null || !source.LocalReturnOrigin.TryTake(out Vector3 origin))
            origin = position;

        Vector3 forward = owner != null ? owner.transform.forward : Vector3.forward;
        AssassinShadowReturnRules.Resolve(position, origin, data != null ? data.DashDistance : 0f, forward, out direction, out distance);

        // 데이터 snapRotationOnStart = false(조준이 아니라 출발점 쪽) — 이 피어에서 돌진 방향으로 돌린다.
        movement?.RotateImmediately(direction);
    }

    private float ReturnDamageRatio =>
        TraitModifiers != null
            ? Mathf.Max(0f, TraitModifiers.ResolveSkillValue(AssassinShadowReturnRules.DamageRatioKey, AssassinShadowReturnRules.DefaultDamageRatio))
            : AssassinShadowReturnRules.DefaultDamageRatio;
}
