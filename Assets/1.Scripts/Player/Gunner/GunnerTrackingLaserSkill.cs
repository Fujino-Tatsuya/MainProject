using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 거너 R — 추적 레이저 (character_gunner.md §9, D12, PLAN-gunner.md G7).
///
/// 기존 타겟팅 궁극기와 같은 대상 지정 모드(SingleTarget, ClickToConfirm — R 재입력 취소, 빈 곳 무시, 취소 시 쿨 없음)를 쓴다.
/// 확정 순간(서버 승인) 과열 단계·피해를 저장하고(쿨타임도 이때부터), 클립 gunner_skill_ULT_cast 의 <b>Custom0</b> 이벤트에서
/// 대상 위치에 <see cref="GunnerTrackingLaser"/> 를 서버가 생성한다. Custom0 없이 끝나면(End·안전망·취소·사망) 끝나기 직전에 생성한다 — 레이저가 빠지지 않는다.
/// 데이터 targetingMode 를 GroundPoint 로 바꾸면 지점 지정으로도 동작한다 — 확정 지점에 대상 없이 생성되고 레이저가 재탐색으로 적을 찾는다.
/// 이후 레이저는 독립적으로 움직이고, 플레이어는 시전 모션의 <b>End</b> 이벤트 뒤 자유롭다(안 오면 maxActiveDuration 안전망). 과열도는 건드리지 않는다.
/// </summary>
public class GunnerTrackingLaserSkill : PlayerSkillBase
{
    const string CastClip = "gunner_skill_ULT_cast";

    private GunnerHeat heat;
    private GunnerBeamView view;

    // 서버 — 시전 순간 스냅샷, 생성은 Custom0 에서
    private bool laserPending;
    private int pendingDamage;
    private int pendingStage;
    private Unit pendingTarget;
    private Vector3 castTargetPosition;

    public override PlayerSkillSlot Slot => PlayerSkillSlot.Ultimate;
    public override bool CanMoveWhileActive => false;

    // 대상 지정 중 공용 대시 = 조준 취소, 쿨타임 없음(D1·D2)
    public override bool CanCancelAimByDash => true;

    private GunnerTrackingLaserData RData => Data as GunnerTrackingLaserData;

    public override void Initialize(Player owner, PlayerSkillController controller)
    {
        base.Initialize(owner, controller);
        heat = owner.GetComponent<GunnerHeat>();
        view = owner.GetComponent<GunnerBeamView>();
    }

    // 서버 권위 시전 조건 — 살아 있는 적, 사거리 안(§9.2).
    // 지점 지정(targetingMode = GroundPoint)으로 시전되면 대상 대신 지점이 사거리(+위치 오차 버퍼) 안인지 본다.
    public override bool CanUse(Vector3 direction, Unit target)
    {
        GunnerTrackingLaserData data = RData;
        if (data == null || data.LaserPrefab == null)
            return false;

        if (HasAimPoint)
            return GroundPointTargeting.IsApprovableRange(owner.transform.position, AimPoint, data.CastRange);

        if (target == null || target is Player || target.CurrentHealth <= 0)
            return false;

        Vector3 delta = target.transform.position - owner.transform.position;
        delta.y = 0f;
        return delta.sqrMagnitude <= data.CastRange * data.CastRange;
    }

    public override void OnServerStart(Vector3 direction, Unit target)
    {
        GunnerTrackingLaserData data = RData;
        State = SkillState.Active;

        // 과열 단계·피해는 시전 순간 그대로(생성이 Custom0 로 늦어져도 바뀌지 않는다).
        pendingStage = heat != null ? heat.CaptureStage() : 0;
        // 툴팁의 최소~최대 계산도 같은 과열 배율을 쓰므로 판정 공식을 바꿀 때 같이 바꿀 것.
        pendingDamage = Mathf.RoundToInt(damageSnapshot * data.StageDamageMultiplier(pendingStage));
        pendingTarget = HasAimPoint ? null : target;
        castTargetPosition = target != null ? target.transform.position : AimPoint;
        laserPending = true;
    }

    public override void OnAnimationEvent(SkillAnimationEventType eventType)
    {
        if (eventType == SkillAnimationEventType.Custom0)
            SpawnPendingLaser();
        else if (eventType == SkillAnimationEventType.End && State == SkillState.Active)
            EndSelf(SkillEndReason.Completed); // Custom0 가 아직이면 OnEnd 가 먼저 생성한다
    }

    public override void OnEnd(SkillEndReason reason)
    {
        // laserPending 은 서버 OnServerStart 에서만 켜진다 — 클라 피어의 OnEnd 는 여기서 아무것도 안 한다.
        if (laserPending)
        {
#if UNITY_EDITOR
            if (reason == SkillEndReason.Completed)
                Debug.LogWarning($"[Gunner/R] Custom0 이벤트 없이 End 가 와서 종료 직전에 레이저를 생성한다 — 클립 '{CastClip}' 확인", this);
#endif
            SpawnPendingLaser();
        }

        GunnerSkillEventRules.WarnIfFallbackEnded(reason, "R", CastClip, SkillAnimationEventType.End, this);
        base.OnEnd(reason);
    }

    // [서버] 1회. 대상이 그 사이 죽었거나 사라졌으면 시전 순간 위치에 대상 없이 → 레이저가 재탐색한다.
    private void SpawnPendingLaser()
    {
        if (!laserPending)
            return;
        laserPending = false;

        GunnerTrackingLaserData data = RData;
        if (data == null || data.LaserPrefab == null)
            return;

        Unit target = pendingTarget;
        pendingTarget = null;
        bool targetAlive = target != null && target.CurrentHealth > 0;
        Vector3 position = GunnerSkillEventRules.ResolveLaserSpawn(
            HasAimPoint, AimPoint, targetAlive, targetAlive ? target.transform.position : default,
            castTargetPosition, out bool keepTarget);
        // 지점 지정 = 그 지점에 대상 없이 생성 → 레이저가 retargetRadius 안 최근접 적을 스스로 찾는다.
        Unit laserTarget = keepTarget ? target : null;
        int damage = pendingDamage;
        int stage = pendingStage;

        GameObject instance = Instantiate(data.LaserPrefab, position, Quaternion.identity);
        var laser = instance.GetComponent<GunnerTrackingLaser>();
        var networkObject = instance.GetComponent<NetworkObject>();
        if (laser == null || networkObject == null)
        {
            Debug.LogError("[Gunner/R] 레이저 프리팹에 GunnerTrackingLaser·NetworkObject 가 필요하다.", this);
            Destroy(instance);
            return;
        }

        if (owner.IsSpawned)
            networkObject.Spawn(true); // destroyWithScene — 씬 전환 시 함께 제거(§9.6)
        laser.ServerInitialize(owner, laserTarget, damage, data, DamageAttackType, DamageHitPattern);

        string origin = laserTarget != null ? $"대상 {laserTarget.name}" : $"지점 {position}";
        Edit.Log($"[Gunner/R] 추적 레이저 생성 — {origin}, 틱 피해 {damage}, 저장 단계 {stage}", this);
    }

    // 시전 모션은 Data.AnimatorStateName 이 튼다. 여기서는 **총구 섬광**만.
    // 🔴 전 피어에서 돈다(PlaySkillClientRpc → OnClientPlay). 서버 가드를 넣으면 호스트에서만 보인다.
    //    레이저 본체는 GunnerTrackingLaser 프리팹이 스스로 들고 있다 — 여기서 띄우지 않는다.
    public override void OnClientPlay(Vector3 direction) => view?.ShowUltCast();
}
