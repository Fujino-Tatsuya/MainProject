using UnityEngine;

/// <summary>
/// 우클릭 단죄의 방패 — 전방 방패 강타 1회. 서버가 앵커 범위에 인터럽트 히트를 넣는다
/// (<see cref="AttackInfo.isInterruptAttack"/>).
///
/// 이 스킬의 계약은 "이 히트는 인터럽트 스킬이다"를 실어 보내는 것까지다.
/// 카운터 창 여부·정면 각도·그로기/Break 전이는 <b>전부 맞는 쪽(보스) 책임</b>이다 — 여기서 판단하지 않는다.
///
/// 판정·종료 = Garen_Parry 의 Hit·End 이벤트(PLAN-anim-event-timing P1-④). 시점 튜닝은 클립에서 이벤트를 옮긴다.
/// <see cref="FirstMeleeInterruptSkillData.HitDelay"/>·SkillDuration 은 쓰지 않는다 — 이벤트가 안 오면 컨트롤러 안전망(MaxActiveDuration)이 끝낸다.
/// </summary>
public class FirstMeleeInterruptSkill : PlayerInterruptSkillBase, ISkillPreviewSource, IInterruptCueSink, IInterruptSuccessRelay
{
    public override System.Collections.Generic.IReadOnlyList<SkillAnimationEventType> RequiredAnimationEvents => HitAndEndEvents;

    // 🔴 여기는 RPC 가 필요 없다 — OnClientPlay 와 OnEnd 는 둘 다 전 피어에서 돈다:
    //    시작 → 서버는 TryStartSkillServer 가, 클라는 PlaySkillClientRpc 가 PlaySkillPresentation 을 탄다
    //    종료 → 서버는 EndActiveSkillServer 가, 클라는 EndSkillClientRpc 가 OnEnd 를 부른다
    //    (FirstMeleeMainSkill 의 스매시 루프와 같은 구조다)
    // 연출 칸(방패 발광·루프·기 모으기)은 팔라딘 연출 창구가 든다(2026-11-02).
    // 🔴 적중 충격파만 경로가 다르다. ResolveHit 은 서버에서만 도는데(OnTick=TickServer,
    //    애니 이벤트=HandleAnimationEvent 의 IsServer 게이트) 이 클래스는 MonoBehaviour 라 RPC 를 못 단다 — 창구가 전 피어로 보낸다.
    [Header("연출")]
    [Tooltip("팔라딘 연출 창구(방패 발광·루프·기 모으기·적중 충격파). 플레이어 루트의 PaladinSkillView 를 물린다.\n비워두면 연출만 빠진다")]
    [SerializeField] private PaladinSkillView skillView;

    public override void OnClientPlay(Vector3 direction)
    {
        // 애니메이션은 PlayerSkillController가 AnimatorStateName으로 재생한다. 여기는 방패 발광만.
        // 간파 연출 디렉터 시도 사건(할 일 118) — B1 칸이 켜져 있으면 디렉터가 PlayCue 로 돌려 부른다. 끄는 쪽(OnEnd)은 늘 돈다.
        InterruptProductionDirector.LocalAttempt(InterruptProductionEvent.LocalAttempt(owner, this));
    }

    // [각 피어] B1 방패 발광·루프 — 디렉터가 칸을 확인한 뒤 부른다. 디렉터는 시도 사건의 스킬(e.Skill)을 sink 로 되부르므로
    // 이 메서드는 여기 남기고 재생은 창구에 넘긴다.
    void IInterruptCueSink.PlayCue(InterruptProductionCue cue, in InterruptProductionEvent e)
    {
        if (cue != InterruptProductionCue.PaladinShieldGlow)
            return;

        skillView?.BeginInterruptGlow();
    }

    public override void OnEnd(SkillEndReason reason)
    {
        base.OnEnd(reason);

        // 🔴 종료 사유를 가리지 않는다. 취소·사망·안전망 만료 어느 쪽으로 끝나도 여기를 지나므로
        //    연출이 켜진 채 남는 경로가 없다. (발광·루프·기 모으기 회수 — 이미 꺼져 있으면 조용한 no-op 이다)
        skillView?.EndInterruptVfx();
    }

    protected override void OnMissingInterruptData()
    {
        Debug.LogError("[Player] 단죄의 방패에는 FirstMeleeInterruptSkillData가 필요합니다.", this);
    }

    protected override void OnMissingHitboxAnchor()
    {
        Debug.LogError("[Player] 단죄의 방패에 판정 앵커(hitboxAnchor)가 배정되지 않았습니다.", this);
    }

    public bool TryGetPreview(Vector3 origin, Vector3 forward, out SkillPreviewShape shape)
    {
        if (HitboxAnchor == null ||
            !HitboxAnchor.TryGetLocalBox(out Vector3 center, out Vector3 size))
        {
            shape = default;
            return false;
        }

        shape = SkillPreviewShapes.Hitbox(center, size);
        return true;
    }

    protected override void OnInterruptTargetResolved(Object target, AttackInfo attackInfo)
    {
        // Unit이 아닌 상자도 실제 수신 대상을 로그에 남긴다.
        Edit.Log($"[Skill] 단죄의 방패 적중 — {target.name} 피해 {attackInfo.damage} (Interrupt)", this);
    }

    protected override bool CompleteBeforeAttackLanded => true;

    // 충격파 = 간파 성공(카운터 창 소비) 때만(2026-10-26 민경). 예전엔 적중 1건 이상이면 터졌다.
    // 성공은 디렉터 성공 단계가 판정 1회에 한 번(여러 대상이 성공해도) 시전자의 relay 를 부른다 — 서버에서만 난다(할 일 118).
    // 전송로(PaladinSkillView.InterruptWaveRpc)는 그대로, B3 칸은 받는 피어에서 디렉터가 본다.
    void IInterruptSuccessRelay.ServerRelaySuccess(in InterruptProductionEvent e)
    {
        // 🔴 시전자 아래 relay 를 다 부르므로 자기 시전만(대체 슬롯 포함). 꺼진 컴포넌트는 옛 OnEnable 구독처럼 빠진다.
        if (e.Skill != this || !isActiveAndEnabled)
            return;

        skillView?.ServerInterruptWave();
    }
}
