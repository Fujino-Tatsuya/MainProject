using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 거너 R — 추적 레이저 (character_gunner.md §9, D12, PLAN-gunner.md G7).
///
/// 기존 타겟팅 궁극기와 같은 대상 지정 모드(SingleTarget, ClickToConfirm — R 재입력 취소, 빈 곳 무시, 취소 시 쿨 없음)를 쓴다.
/// 확정 순간(서버 승인) 과열 단계를 저장하고 대상 위치에 <see cref="GunnerTrackingLaser"/> 를 서버가 생성한다 — 쿨타임도 이때부터.
/// 이후 레이저는 독립적으로 움직이고, 플레이어는 짧은 시전 모션(CastDuration) 뒤 자유롭다. 과열도는 건드리지 않는다.
/// </summary>
public class GunnerTrackingLaserSkill : PlayerSkillBase
{
    private GunnerHeat heat;
    private GunnerBeamView view;
    private float endTime;

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

    // 서버 권위 시전 조건 — 살아 있는 적, 사거리 안(§9.2)
    public override bool CanUse(Vector3 direction, Unit target)
    {
        GunnerTrackingLaserData data = RData;
        if (data == null || data.LaserPrefab == null || target == null || target is Player || target.CurrentHealth <= 0)
            return false;

        Vector3 delta = target.transform.position - owner.transform.position;
        delta.y = 0f;
        return delta.sqrMagnitude <= data.CastRange * data.CastRange;
    }

    public override void OnServerStart(Vector3 direction, Unit target)
    {
        GunnerTrackingLaserData data = RData;
        State = SkillState.Active;
        endTime = Time.time + data.CastDuration;

        int stage = heat != null ? heat.CaptureStage() : 0;
        // 툴팁의 최소~최대 계산도 같은 과열 배율을 쓰므로 판정 공식을 바꿀 때 같이 바꿀 것.
        int damage = Mathf.RoundToInt(damageSnapshot * data.StageDamageMultiplier(stage));

        Vector3 position = target.transform.position;
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
        laser.ServerInitialize(owner, target, damage, data, DamageAttackType, DamageHitPattern);

        Edit.Log($"[Gunner/R] 추적 레이저 생성 — 대상 {target.name}, 틱 피해 {damage}, 저장 단계 {stage}", this);
    }

    // 시전 모션은 Data.AnimatorStateName 이 튼다. 여기서는 **총구 섬광**만.
    // 🔴 전 피어에서 돈다(PlaySkillClientRpc → OnClientPlay). 서버 가드를 넣으면 호스트에서만 보인다.
    //    레이저 본체는 GunnerTrackingLaser 프리팹이 스스로 들고 있다 — 여기서 띄우지 않는다.
    public override void OnClientPlay(Vector3 direction) => view?.ShowUltCast();

    public override void OnTick()
    {
        if (State == SkillState.Active && Time.time >= endTime)
            EndSelf(SkillEndReason.Completed);
    }
}
