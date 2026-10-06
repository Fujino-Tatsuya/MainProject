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

    // 집중 애니는 Data.AnimatorStateName 이 튼다. 여기서는 **총구 충전 연출**만 켠다.
    // 🔴 이건 전 피어에서 돈다(PlayerSkillController.PlaySkillClientRpc → OnClientPlay).
    //    서버 가드를 넣으면 호스트에서만 보인다.
    public override void OnClientPlay(Vector3 direction)
    {
        if (QData != null)
            view?.BeginCharge(QData.MaxChargeTime);
    }

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
                                         data.HittableLayers, damage, DamageAttackType,
                                         DamageHitPattern, data.TriggersOnHit);

        beam.CollectAllies(origin, direction, length, data.BeamWidth, data.BeamHeight, data.AllyLayers, allies);
        for (int i = 0; i < allies.Count; i++)
            allies[i].AddShield(ShieldType.GunnerCharge, SourceId, shield, data.ShieldDuration);

        controller.CommitCooldownServer(Slot);
        ClearStatus();

        // 🔴 **지형에 막혔는지를 같이 보낸다.** 끝점 좌표만으로는 벽에 맞은 것과
        //    최대 사거리까지 뻗어 허공에서 끝난 것을 구분할 수 없다 —
        //    착탄 연출을 띄울지 말지가 여기서 갈린다(좌클릭의 stopped 와 같은 신호).
        bool stopped = length < range - 0.01f;
        // 보호막 연출은 안 싣는다 — 받은 아군의 PlayerShieldVfx 가 복제된 보호막 목록을 보고 띄운다.
        view?.ServerChargeLaserFired(origin, origin + direction * length, data.BeamWidth,
                                     stopped, BuildHitPoints(origin, direction, length));

        Edit.Log($"[Gunner/Q] 발사 — 집중 {factor:0.00}, 사거리 {length:0.0}/{range:0.0}, 피해 {damage} ×{hitCount}, " +
                 $"보호막 {shield} ×{allies.Count}, 저장 단계 {storedStage}", this);
    }

    /// <summary>
    /// 피해를 준 몹마다 <b>빔 위의 어디서 맞았는지</b>를 구해 전 피어에 보낸다.
    ///
    /// 🔴 몹의 위치를 그대로 쓰지 않고 <b>빔 축에 투영</b>한다. 판정은 폭 1.2m 직육면체라
    /// 몹 중심이 빔에서 비껴 있을 수 있는데, 그 자리에 연출을 찍으면 레이저 옆 허공에서 터진다.
    ///
    /// 높이는 몹 중심을 쓴다 — 빔 축 높이(총구 1m)로 고정하면 큰 몹은 발치에서 터진다.
    /// </summary>
    private Vector3[] BuildHitPoints(Vector3 origin, Vector3 direction, float length)
    {
        IReadOnlyList<Unit> landed = beam != null ? beam.LastPierceLanded : null;
        if (landed == null || landed.Count == 0)
            return System.Array.Empty<Vector3>();

        // 관통이라 이론상 제한이 없다. RPC 를 작게 유지하려고 자른다 —
        // 한 발에 8곳이면 눈으로는 이미 "전부 맞았다"로 읽힌다.
        int count = Mathf.Min(landed.Count, 8);
        var points = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            Vector3 center = landed[i].transform.position;
            float along = Mathf.Clamp(Vector3.Dot(center - origin, direction), 0f, length);
            Vector3 onBeam = origin + direction * along;
            points[i] = new Vector3(onBeam.x, center.y + 1f, onBeam.z);
        }
        return points;
    }

    public override void OnEnd(SkillEndReason reason)
    {
        // 🔴 **권한 가드 밖이다.** 발사 없이 끝나는 경로(대시 취소·피격·사망)에서도
        //    전 피어가 충전 루프를 꺼야 한다. 안 그러면 그 피어에 총구 연출이 남는다.
        //    (발사로 끝난 경우는 PlayChargeLaser 가 이미 껐고, Stop 은 두 번 불러도 무해하다.)
        view?.EndCharge();

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
