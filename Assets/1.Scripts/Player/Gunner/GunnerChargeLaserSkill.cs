using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 거너 Q — 충전 레이저 / 정신 집중 (character_gunner.md §6, D3·D4·D5, PLAN-gunner.md G4).
///
/// Q → 정신 집중(Focus 버프 + 감속, 이동 가능, 조준 방향을 계속 바라봄) — 시작 순간 과열 단계 저장.
/// 좌클릭 또는 (최대 집중 + 유지시간) 자동 → 현재 조준으로 발사: 사거리·피해는 집중 시간으로 최소→최대 보간,
/// 지형에서 끊기는 굵은 직선 안의 적·오브젝트에 피해, 아군(시전자 제외)에 보호막(합산) — 전부 대상별 1회.
/// 쿨타임은 발사 순간부터(수동 커밋). 발사 없이 끝나면(쓰러짐·사망·조작 불가·피격 취소) 쿨타임 없음.
/// Q 재입력은 취소가 아니다(§3.3 — 실행 중 같은 슬롯 시전은 컨트롤러가 거절). 과열도는 건드리지 않는다.
/// </summary>
public class GunnerChargeLaserSkill : PlayerSkillBase
{
    private GunnerHeat heat;
    private GunnerBeamAttack beam;
    private GunnerBeamView view;
    private PlayerMovement movement;
    private readonly List<Player> allies = new List<Player>();

    // 서버 런타임
    private float chargeStartTime;
    private int storedStage;
    private Vector3 aim;
    private bool fired;
    private float endTime;
    private bool statusApplied;

    public override PlayerSkillSlot Slot => PlayerSkillSlot.Main;
    public override PlayerActionState EntryActionState => PlayerActionState.Focus; // 발사하면 Skill(GunnerBeamView)
    public override bool CanMoveWhileActive => true;
    public override bool CanMovementRotateWhileActive => false; // 이동 방향이 아니라 조준을 바라본다(§6.2)
    public override bool ConsumesPrimaryInput => true;
    public override bool WantsAimUpdates => true; // 자동 발사도 "현재 조준 방향"

    // 공용 대시는 정신 집중만 끊는다(D1). 발사 후 회복(Skill)은 끊지 않는다.
    public override bool CanBeCanceledByDash(PlayerActionState phase) => phase == PlayerActionState.Focus;

    private GunnerChargeLaserData QData => Data as GunnerChargeLaserData;
    private ulong SourceId => owner != null ? owner.NetworkObjectId : 0;
    private bool HasAuthority => owner != null && (!owner.IsSpawned || owner.IsServer);

    public override void Initialize(Player owner, PlayerSkillController controller)
    {
        base.Initialize(owner, controller);
        heat = owner.GetComponent<GunnerHeat>();
        beam = owner.GetComponent<GunnerBeamAttack>();
        view = owner.GetComponent<GunnerBeamView>();
        movement = owner.GetComponent<PlayerMovement>();
    }

    public override bool CanUse(Vector3 direction, Unit target) => QData != null && beam != null;

    public override void OnServerStart(Vector3 direction, Unit target)
    {
        State = SkillState.Charging;
        chargeStartTime = Time.time;
        aim = direction;
        fired = false;
        endTime = 0f;

        // 시전 시작 순간의 단계를 저장 — 과열 중이면 3단계(§5.6)
        storedStage = heat != null ? heat.CaptureStage() : 0;

        StatusEffectController effects = owner.StatusEffects;
        if (effects != null)
        {
            effects.Apply(StatusEffectType.Focus, 0f, SourceId);
            effects.Apply(StatusEffectType.MoveSpeedModifier, QData.ChargeMoveSpeedMultiplier, 0f, SourceId);
            statusApplied = true;
        }

        Edit.Log($"[Gunner/Q] 정신 집중 시작 — 저장 단계 {storedStage}", this);
    }

    public override void OnClientPlay(Vector3 direction) { } // 집중 애니는 Data.AnimatorStateName 이 튼다

    public override void OnOwnerTick(Vector3 aimDirection)
    {
        movement?.RotateImmediately(aimDirection);
    }

    public override void OnAimUpdated(Vector3 direction) => aim = direction;

    public override void OnPrimaryPressed(Vector3 direction)
    {
        if (State == SkillState.Charging)
            Fire(direction);
    }

    public override void OnTick()
    {
        GunnerChargeLaserData data = QData;
        if (data == null)
            return;

        if (State == SkillState.Charging && Time.time >= chargeStartTime + data.MaxChargeTime + data.AutoFireHoldTime)
            Fire(aim);
        else if (fired && Time.time >= endTime)
            EndSelf(SkillEndReason.Completed);
    }

    private void Fire(Vector3 direction)
    {
        GunnerChargeLaserData data = QData;
        State = SkillState.Active;
        fired = true;
        endTime = Time.time + data.FireRecovery;

        direction.y = 0f;
        direction = direction.sqrMagnitude > 0.001f ? direction.normalized : owner.transform.forward;
        movement?.RotateImmediately(direction);

        float factor = data.ChargeFactor(Time.time - chargeStartTime);
        float range = data.RangeAt(factor);
        int damage = Mathf.Max(0, Mathf.RoundToInt(
            damageSnapshot * data.DamageMultiplierAt(factor) * data.StageDamageMultiplier(storedStage)));
        int shield = Mathf.Max(0, Mathf.RoundToInt(data.ShieldAmount * data.StageShieldMultiplier(storedStage)));

        Vector3 origin = owner.transform.position + Vector3.up * data.MuzzleHeight;
        float length = beam.CastLength(origin, direction, range, data.BeamWidth * 0.5f, data.BlockingLayers);

        int hitCount = beam.FirePiercing(origin, direction, length, data.BeamWidth, data.BeamHeight,
                                         data.HittableLayers, damage, AttackType.Skill, data.TriggersOnHit);

        beam.CollectAllies(origin, direction, length, data.BeamWidth, data.BeamHeight, data.AllyLayers, allies);
        for (int i = 0; i < allies.Count; i++)
            allies[i].AddShield(ShieldType.GunnerCharge, SourceId, shield, data.ShieldDuration);

        controller.CommitCooldownServer(Slot);
        ClearStatus();
        view?.ServerChargeLaserFired(origin, origin + direction * length, data.BeamWidth);

        Edit.Log($"[Gunner/Q] 발사 — 집중 {factor:0.00}, 사거리 {length:0.0}/{range:0.0}, 피해 {damage} ×{hitCount}, " +
                 $"보호막 {shield} ×{allies.Count}, 저장 단계 {storedStage}", this);
    }

    public override void OnEnd(SkillEndReason reason)
    {
        if (HasAuthority)
        {
            if (!fired && reason == SkillEndReason.DashCancelled)
            {
                // 대시로 끊으면 쿨타임 적용(D2 — "Q 는 취소 불가" 의도를 지킨다)
                controller.CommitCooldownServer(Slot);
                Edit.Log("[Gunner/Q] 대시로 집중 취소 — 발사 없음, 쿨타임 적용", this);
            }
            else if (!fired)
            {
                // 쓰러짐·사망·조작 불가·피격 취소 = 쿨타임 없음(수동 커밋이라 자연히 안 돈다 — §6.6)
                Edit.Log($"[Gunner/Q] 발사 없이 종료({reason}) — 쿨타임 없음", this);
            }
            ClearStatus();
        }

        fired = false;
        base.OnEnd(reason);
    }

    private void ClearStatus()
    {
        if (!statusApplied || !HasAuthority)
            return;

        StatusEffectController effects = owner.StatusEffects;
        if (effects != null)
        {
            effects.Remove(StatusEffectType.Focus, SourceId);
            effects.Remove(StatusEffectType.MoveSpeedModifier, SourceId);
        }
        statusApplied = false;
    }
}
