using UnityEngine;

/// <summary>
/// 일반 E 단검 강화 — Buff 동작과 동시에 강화 준비(character_assassin.md §8.1). 판정 없음, 무적 없음.
/// 쿨타임은 여기서 시작하지 않는다(데이터 commitCooldownManually) — 강타 시작·강화 제거 때 <see cref="AssassinState"/> 가 시작한다.
/// Buff 는 스킬 상태라 평타·Q·R·간파 입력이 읽히지 않는다(예약 없음). 이동은 허용. 종료 = Buff 클립 End 이벤트.
/// </summary>
public sealed class AssassinEnhanceSkill : PlayerInstantSkill
{
    // Animator 상체 레이어 — AssassinShellAuthoring 이 같은 이름으로 만든다.
    public const string UpperBodyLayerName = "UpperBody";
    public const string UpperBodyEmptyState = "Empty";
    private static readonly int UpperBodyEmptyHash = Animator.StringToHash(UpperBodyEmptyState);
    // 상체 레이어 가중치를 0↔1 로 옮기는 시간 — 레이어 전이와 같은 0.08초.
    private const float UpperBodyBlendSeconds = 0.08f;

    private AssassinState assassinState;
    private Animator animator;
    private int upperBodyLayer = -1;
    // 전 피어 — Buff 재생 중이면 상체 레이어 가중치를 1 로, 아니면 0 으로 옮긴다.
    private bool upperBodyActive;

    private static readonly SkillAnimationEventType[] RequiredEvents = { SkillAnimationEventType.End };

    public override PlayerSkillSlot Slot => PlayerSkillSlot.Sub;
    public override System.Collections.Generic.IReadOnlyList<SkillAnimationEventType> RequiredAnimationEvents => RequiredEvents;

    // Buff 중 걸으며 사용(기획 "이동·대시는 기존 공통 처리" = 가붕이 E 와 같다). 감속 없음 — 판정 없는 0.5초 준비 동작이다.
    // Buff 는 상체 레이어에서 재생하고 하체는 0층의 Idle/Run 을 그대로 따른다.
    public override bool CanMoveWhileActive => true;

    public override void Initialize(Player owner, PlayerSkillController controller)
    {
        base.Initialize(owner, controller);
        assassinState = owner != null ? owner.GetComponent<AssassinState>() : null;
        animator = owner != null ? owner.GetComponentInChildren<Animator>() : null;
        upperBodyLayer = animator != null ? animator.GetLayerIndex(UpperBodyLayerName) : -1;
    }

    // 강화 중 중복 사용 불가, 변신 중 불가(서버 승인 시점).
    public override bool CanUse(Vector3 direction, Unit target) =>
        assassinState != null && assassinState.CanPrepareEnhancement;

    public override void OnServerStart(Vector3 direction, Unit target)
    {
        base.OnServerStart(direction, target);

        if (assassinState == null)
        {
            Debug.LogError("[Assassin] 일반 E 에는 같은 플레이어의 AssassinState 가 필요합니다.", this);
            return;
        }

        assassinState.ServerTryPrepareEnhancement();
    }

    // 전 피어 — 컨트롤러가 상체 레이어의 Buff 로 CrossFade 한 직후다. 레이어 가중치를 올려 상체에 Buff 를 보인다.
    public override void OnClientPlay(Vector3 direction)
    {
        upperBodyActive = true;
        AssassinSkillView.Play(owner, v => v.PlayEnhanceCast());
    }

    // 전 피어 — 컨트롤러의 종료 CrossFade(Idle)는 0층만 되돌린다. 완료·대시·피격·사망 모두 상체 레이어 가중치를 내려 0층에 돌려준다.
    public override void OnEnd(SkillEndReason reason)
    {
        upperBodyActive = false;
        base.OnEnd(reason);
    }

    // 상체 레이어는 평소 가중치 0 이다 — Humanoid 의 빈 상태는 가중치가 있으면 상체를 굳힌다(평타 상체 정지 회귀).
    // 0 에 닿으면 빈 상태로 되돌려 다음 Buff 가 처음부터 재생되게 한다.
    private void Update()
    {
        if (animator == null || upperBodyLayer < 0)
            return;

        float current = animator.GetLayerWeight(upperBodyLayer);
        float target = upperBodyActive ? 1f : 0f;
        if (current == target)
            return;

        float next = Mathf.MoveTowards(current, target, Time.deltaTime / UpperBodyBlendSeconds);
        animator.SetLayerWeight(upperBodyLayer, next);
        if (next <= 0f)
            animator.Play(UpperBodyEmptyHash, upperBodyLayer, 0f);
    }
}
