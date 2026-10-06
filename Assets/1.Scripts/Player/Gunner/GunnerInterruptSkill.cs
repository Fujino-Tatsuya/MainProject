using UnityEngine;

/// <summary>
/// 거너 우클릭 — 근접 간파 (character_gunner.md §8, D10·D11, PLAN-gunner.md G6).
///
/// 레이저포를 내질러 총구가 닿는 순간(HitDelay 또는 Hit 애니 이벤트, 1회) 앵커 범위에 피해 + <b>간파 공격 표시</b>를 싣는다.
/// 간파 성공 조건(유효 구간·전방 120도)·취약 넉백·벽/증기 벤트는 <b>맞는 쪽(보스)</b>이 판단한다 — 가붕이 우클릭과 같은 계약.
/// 그 순간부터 적중·간파 여부와 무관하게 후폭풍으로 공격 반대 방향으로 밀려난다 — 벽·오브젝트에만 막히고 유닛은 통과, 무적 없음.
/// 쿨타임은 사용 시점(빗나가도 적용 — §8.6). 과열도와 무관.
/// </summary>
public class GunnerInterruptSkill : PlayerInterruptSkillBase
{
    private PlayerMotor motor;
    private GunnerBeamView view;
    // 시뮬레이션 피어(오너 + 서버 권위 시 서버)
    private Vector3 attackDirection;
    private float recoilStartTime;
    private float recoilRemaining;
    private float recoilSpeed;

    private GunnerInterruptData IData => Data as GunnerInterruptData;

    public override void Initialize(Player owner, PlayerSkillController controller)
    {
        base.Initialize(owner, controller);
        motor = owner.GetComponent<PlayerMotor>();
        view = owner.GetComponent<GunnerBeamView>();
    }

    public override bool CanUse(Vector3 direction, Unit target) => IData != null && HitboxAnchor != null;

    // 전 피어. 후폭풍은 판정 시각(시작 + HitDelay)에 각 시뮬레이션 피어가 스스로 시작한다(RPC 없이 같은 시각 규칙).
    public override void OnClientPlay(Vector3 direction)
    {
        GunnerInterruptData data = IData;
        direction.y = 0f;
        attackDirection = direction.sqrMagnitude > 0.001f ? direction.normalized : owner.transform.forward;
        recoilStartTime = Time.time + (data != null ? data.HitDelay : 0f);
        recoilRemaining = data != null ? data.RecoilDistance : 0f;
        recoilSpeed = data != null ? data.RecoilDistance / data.RecoilDuration : 0f;
    }

    public override void OnFixedTick()
    {
        if (recoilRemaining <= 0f || motor == null || !owner.IsSimulating || Time.time < recoilStartTime)
            return;

        motor.PassThroughEnemiesOverride = true; // 유닛 통과(§8.5 — 벽·오브젝트에만 막힘)
        float step = Mathf.Min(recoilSpeed * Time.fixedDeltaTime, recoilRemaining);
        recoilRemaining -= step;
        motor.AddGroundedDisplacement(-attackDirection * step);
    }

    public override void OnEnd(SkillEndReason reason)
    {
        recoilRemaining = 0f;
        if (motor != null)
            motor.PassThroughEnemiesOverride = false;
        base.OnEnd(reason);
    }

    protected override bool ShouldSkipTarget(Unit unit) => unit != null && unit.CurrentHealth <= 0;
    protected override bool ConsumeOnHitBonusOnce => false;

    protected override void OnInterruptResolutionCompleted(int resolvedCount)
    {
        // 레이저·폭발 연출(임시) — 빗나가도 나간다(§8.6)
        Vector3 origin = owner.transform.position + Vector3.up;
        view?.ServerInterruptBlast(origin, origin + attackDirection * 1.5f);

        Edit.Log($"[Gunner/우클릭] 간파 판정 — 적중 {resolvedCount}", this);
    }
}
