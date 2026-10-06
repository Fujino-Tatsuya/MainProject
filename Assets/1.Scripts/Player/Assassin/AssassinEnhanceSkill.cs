using UnityEngine;

/// <summary>
/// 일반 E 단검 강화 — Buff 동작과 동시에 강화 준비(character_assassin.md §8.1). 판정 없음, 무적 없음.
/// 쿨타임은 여기서 시작하지 않는다(데이터 commitCooldownManually) — 강타 시작·강화 제거 때 <see cref="AssassinState"/> 가 시작한다.
/// Buff 는 스킬 상태라 평타·Q·R·간파 입력이 읽히지 않는다(예약 없음). 종료 = Buff 클립 End 이벤트.
/// </summary>
public sealed class AssassinEnhanceSkill : PlayerInstantSkill
{
    private AssassinState assassinState;

    public override PlayerSkillSlot Slot => PlayerSkillSlot.Sub;

    // 🔸 Buff 0.5초 동안 제자리. 기획 "이동·대시는 기존 공통 처리"(가붕이 E = 이동 가능)와 다를 수 있다 — Play 에서 확인.
    public override bool CanMoveWhileActive => false;

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

    public override void OnClientPlay(Vector3 direction) { }
}
