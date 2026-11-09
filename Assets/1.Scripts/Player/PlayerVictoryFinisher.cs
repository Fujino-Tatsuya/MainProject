using UnityEngine;

/// <summary>
/// 보스 처치 연출(PLAN-boss-victory-glitch Q9) — 이 캐릭터의 "처치 연출" 스킬 칸. 캐릭터 Variant(<c>Player_Paladin</c> 등)에 붙여 고른다.
/// 없으면 <see cref="BossVictoryGlitchSettings.defaultFinisherSlot"/>. 그 스킬의 애니·이펙트를 각 피어가 표시로만 재생한다(판정·쿨다운·자원 없음).
/// 🔴 고른 스킬의 시작 상태 클립에 Skill End 이벤트가 있어야 한다(EditMode 테스트 <c>BossVictoryFinisherTests</c>가 잡는다).
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerVictoryFinisher : MonoBehaviour
{
    [SerializeField] private PlayerSkillSlot finisherSlot = PlayerSkillSlot.Ultimate;

    public PlayerSkillSlot FinisherSlot => finisherSlot;

    /// <summary>이 플레이어의 처치 연출 스킬 칸 — Variant 칸, 없으면 설정 기본값.</summary>
    public static PlayerSkillSlot ResolveSlot(GameObject player, BossVictoryGlitchSettings settings)
    {
        PlayerVictoryFinisher finisher = player != null ? player.GetComponentInChildren<PlayerVictoryFinisher>(true) : null;
        if (finisher != null) return finisher.finisherSlot;
        return settings != null ? settings.defaultFinisherSlot : PlayerSkillSlot.Ultimate;
    }
}
