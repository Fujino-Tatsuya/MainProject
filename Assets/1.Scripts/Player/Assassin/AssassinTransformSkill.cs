using UnityEngine;

/// <summary>
/// R 변신 — Parry_R 시작과 동시에 분노 게이지 전부를 연료로 감소 시작(character_assassin.md §10.1).
/// 조건 = 게이지 ≥ 최소 변신량 · R 쿨 종료 · 행동 가능(컨트롤러 승인 — 서버 <see cref="CanUse"/> 가 최종 판정). 쿨타임은 실제 변신 종료 시점부터라
/// 데이터 commitCooldownManually 로 두고 <see cref="AssassinState"/> 가 종료 때 시작한다.
///
/// Parry_R 동안 이동·대시·평타·다른 스킬 불가(스킬 상태 + 이동 잠금). 일반 피격·경직으로 끊기지 않도록
/// <b>슈퍼아머</b>를 건다 — 넉백·밀기·스턴·잡기를 거부하고(A1), 피해는 그대로 받는다(무적 없음).
/// 끊는 것은 쓰러짐·사망뿐이다(<see cref="AssassinState"/> 가 <see cref="SkillEndReason.CasterDied"/> 로 종료).
/// </summary>
public sealed class AssassinTransformSkill : PlayerInstantSkill
{
    private static readonly SkillAnimationEventType[] RequiredEvents = { SkillAnimationEventType.End };

    private AssassinState assassinState;

    public override PlayerSkillSlot Slot => PlayerSkillSlot.Ultimate;
    public override System.Collections.Generic.IReadOnlyList<SkillAnimationEventType> RequiredAnimationEvents => RequiredEvents;
    public override bool CanMoveWhileActive => false;

    private ulong SourceId => owner != null && owner.NetworkObject != null ? owner.NetworkObjectId : 0UL;

    public override void Initialize(Player owner, PlayerSkillController controller)
    {
        base.Initialize(owner, controller);
        assassinState = owner != null ? owner.GetComponent<AssassinState>() : null;
    }

    // 변신 중 R 입력은 시전이 아니라 수동 해제다 — 해제 요청은 AssassinState 가 따로 받는다.
    public override bool CanUse(Vector3 direction, Unit target) =>
        assassinState != null && assassinState.CanBeginTransform;

    public override void OnServerStart(Vector3 direction, Unit target)
    {
        base.OnServerStart(direction, target);

        if (assassinState == null)
        {
            Debug.LogError("[Assassin] R 변신에는 같은 플레이어의 AssassinState 가 필요합니다.", this);
            return;
        }

        assassinState.ServerBeginTransform();

        // 서버 전용 쓰기 — 정상/강제 종료 모두 OnEnd 가 해제한다. 만료 안전망은 지속시간.
        if (owner != null && owner.StatusEffects != null && Data != null)
            owner.StatusEffects.Apply(StatusEffectType.SuperArmor, Data.MaxActiveDuration, SourceId);
    }

    // 시전 1회 연출 없음 — 변신 루프(Blade Vortex)의 차오름이 시전 연출을 겸한다.
    // 루프는 AssassinSkillView 가 AssassinState.TransformChanged 로 켠다(2026-10-14 민경).
    public override void OnClientPlay(Vector3 direction) { }

    // 보스 처치 연출 표시 전용(267 후속) — 변신 오라(Blade Vortex)를 이 피어에서만 켠다. 실제 변신 상태·게이지는 안 건드린다.
    // 처치 연출이 끝날 때까지 유지, 끝·취소면 AssassinSkillView 가 흩어지게 한다(새 RPC 없음 — 모든 피어가 같은 처치 상태로 각자).
    public override void OnPresentationOnlyStart(Transform target)
    {
        AssassinSkillView view = owner != null ? owner.GetComponent<AssassinSkillView>() : null;
        view?.PlayPresentationVortex();
    }

    public override void OnEnd(SkillEndReason reason)
    {
        base.OnEnd(reason);

        // 클라에서는 서버 쓰기 가드로 no-op
        if (owner != null && owner.StatusEffects != null)
            owner.StatusEffects.Remove(StatusEffectType.SuperArmor, SourceId);
    }
}
