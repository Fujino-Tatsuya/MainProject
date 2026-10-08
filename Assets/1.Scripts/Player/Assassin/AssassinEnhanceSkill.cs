using UnityEngine;

/// <summary>
/// 일반 E 단검 강화 — Buff 동작과 동시에 강화 준비(character_assassin.md §8.1). 판정 없음, 무적 없음.
/// 쿨타임은 여기서 시작하지 않는다(데이터 commitCooldownManually) — 강타 시작·강화 제거 때 <see cref="AssassinState"/> 가 시작한다.
/// Buff 는 스킬 상태라 평타·Q·R·간파 입력이 읽히지 않는다(예약 없음). 이동은 허용. 종료 = Buff 클립 End 이벤트.
/// </summary>
public sealed class AssassinEnhanceSkill : PlayerInstantSkill
{
    private AssassinState assassinState;

    public override PlayerSkillSlot Slot => PlayerSkillSlot.Sub;

    // Buff 중 걸으며 사용(기획 "이동·대시는 기존 공통 처리" = 가붕이 E 와 같다). 감속 없음 — 판정 없는 0.5초 준비 동작이다.
    // 애니메이터는 단일 레이어라 Buff 동안은 전신 Buff 포즈로 미끄러지고, 클립이 끝나면(Exit Time) Idle 을 거쳐 Run 으로 이어진다.
    public override bool CanMoveWhileActive => true;

    public override void Initialize(Player owner, PlayerSkillController controller)
    {
        base.Initialize(owner, controller);
        assassinState = owner != null ? owner.GetComponent<AssassinState>() : null;
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
}
