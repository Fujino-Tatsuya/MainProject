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
public class FirstMeleeInterruptSkill : PlayerInterruptSkillBase, ISkillPreviewSource
{
    public override System.Collections.Generic.IReadOnlyList<SkillAnimationEventType> RequiredAnimationEvents => HitAndEndEvents;

    // 🔴 여기는 RPC 가 필요 없다 — OnClientPlay 와 OnEnd 는 둘 다 전 피어에서 돈다:
    //    시작 → 서버는 TryStartSkillServer 가, 클라는 PlaySkillClientRpc 가 PlaySkillPresentation 을 탄다
    //    종료 → 서버는 EndActiveSkillServer 가, 클라는 EndSkillClientRpc 가 OnEnd 를 부른다
    //    (FirstMeleeMainSkill 의 smashLoop/trailLoop 와 같은 구조다)
    [Header("연출")]
    [Tooltip("방패 메쉬 문양 발광(머티리얼 페이드). 방패의 MaterialFadeEffect 를 물린다.\n" +
             "비워두면 연출만 빠진다")]
    [SerializeField] private MaterialFadeEffect shieldGlow;

    [Tooltip("강타 동안 재생할 루프 연출(소켓 이펙트). 프리팹의 'VFX/ShieldInterrupt/ShieldGlow' 를 물린다.\n" +
             "비워두면 연출만 빠진다")]
    [SerializeField] private EffectSocketPlayer glowLoop;

    // 판정 전 방패 기 모으기(2026-10-26 민경 — handoff-paladin-interrupt-vfx). 켜고 끄는 것은 Garen_Parry 클립 이벤트
    // (0초 StartEffect · Hit 시각 StopEffect "ShieldCharge")가 전 피어에서 한다. 여기는 판정 전에 끊긴 경우의 회수만.
    [Tooltip("판정 전 방패 기 모으기(소켓 이펙트, Id 'ShieldCharge'). 프리팹의 'VFX/ShieldInterrupt/ShieldCharge' 를 물린다.\n" +
             "켜고 끄기는 클립 이벤트가 하고, 여기서는 스킬이 판정 전에 끝났을 때 끄기만 한다. 비워두면 회수만 빠진다")]
    [SerializeField] private EffectSocketPlayer chargeLoop;

    // 🔴 적중 충격파만 창구가 다르다. ResolveHit 은 서버에서만 도는데(OnTick=TickServer,
    //    애니 이벤트=HandleAnimationEvent 의 IsServer 게이트) 이 클래스는 MonoBehaviour 라 RPC 를 못 단다.
    [Tooltip("적중 충격파 전파 창구. 플레이어 루트의 PlayerShieldVfx 를 물린다.\n비워두면 연출만 빠진다")]
    [SerializeField] private PlayerShieldVfx shieldVfx;

    public override void OnClientPlay(Vector3 direction)
    {
        // 애니메이션은 PlayerSkillController가 AnimatorStateName으로 재생한다. 여기는 방패 발광만.
        shieldGlow?.FadeIn();

        // Play 는 이미 재생 중이면 먼저 회수하므로 연타로 두 번 불려도 겹치지 않는다.
        glowLoop?.Play();
    }

    public override void OnEnd(SkillEndReason reason)
    {
        base.OnEnd(reason);

        // 🔴 종료 사유를 가리지 않는다. 취소·사망·안전망 만료 어느 쪽으로 끝나도 여기를 지나므로
        //    연출이 켜진 채 남는 경로가 없다. (둘 다 꺼져 있으면 조용한 no-op 이다)
        shieldGlow?.FadeOut();

        // 🔴 루프는 반드시 회수한다 — 핸들을 버리면 풀 인스턴스가 영영 안 돌아온다.
        //    소켓의 safetyTimeout(5s)은 이 경로가 실패했을 때를 위한 그물이지 정상 경로가 아니다.
        glowLoop?.Stop();

        // 판정 전에 끊기면(경직·사망·취소) 클립의 Stop 이벤트가 안 온다. 이미 꺼져 있으면 조용한 no-op 이다.
        chargeLoop?.Stop();
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
    // 성공은 부모의 정적 이벤트 ServerInterruptSucceeded 가 판정 1회에 한 번(여러 대상이 성공해도) 알린다 — 서버에서만 난다.
    private void OnEnable() => ServerInterruptSucceeded += OnServerInterruptSucceeded;

    private void OnDisable() => ServerInterruptSucceeded -= OnServerInterruptSucceeded;

    private void OnServerInterruptSucceeded(Player caster, PlayerInterruptSkillBase skill)
    {
        // 🔴 정적 이벤트라 모든 팔라딘의 성공이 다 들어온다 — 자기 시전만.
        if (skill != this)
            return;

        shieldVfx?.ServerInterruptWave();
    }
}
