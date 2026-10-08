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

    private AssassinState assassinState;
    private Animator animator;

    public override PlayerSkillSlot Slot => PlayerSkillSlot.Sub;

    // Buff 중 걸으며 사용(기획 "이동·대시는 기존 공통 처리" = 가붕이 E 와 같다). 감속 없음 — 판정 없는 0.5초 준비 동작이다.
    // Buff 는 상체 레이어에서 재생하고 하체는 0층의 Idle/Run 을 그대로 따른다.
    public override bool CanMoveWhileActive => true;

    public override void Initialize(Player owner, PlayerSkillController controller)
    {
        base.Initialize(owner, controller);
        assassinState = owner != null ? owner.GetComponent<AssassinState>() : null;
        animator = owner != null ? owner.GetComponentInChildren<Animator>() : null;
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

    public override void OnClientPlay(Vector3 direction) =>
        AssassinSkillView.Play(owner, v => v.PlayEnhanceCast());

    // 전 피어 — 컨트롤러의 종료 CrossFade(Idle)는 0층만 되돌린다. 피격·사망 등 강제 종료에서 상체 Buff 가 남지 않게 비운다.
    public override void OnEnd(SkillEndReason reason)
    {
        if (animator != null)
        {
            int layer = animator.GetLayerIndex(UpperBodyLayerName);
            if (layer >= 0)
                animator.CrossFadeInFixedTime(UpperBodyEmptyHash, 0.08f, layer);
        }

        base.OnEnd(reason);
    }
}
