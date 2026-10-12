using System;
using System.Collections.Generic;

/// <summary>
/// [서버] 튜토리얼 쿨타임 초기화 실행 — 복도1 첫 진입(v8 C1-0)·존3 간파 성공(v8 Z3-4) 공용.
/// 🔴 플레이어 쿨 장부는 <b>공개 API 만</b> 쓴다(<see cref="PlayerSkillController.ReduceCooldownServer"/>·<see cref="PlayerSkillController.GetCooldownRemaining(PlayerSkillBase)"/>·
///    <see cref="PlayerSkillController.GetSkillIndex"/>) — 장부 내부를 고치지 않아 다른 레인의 '간파 성공 시 우클릭 쿨 감소'와 충돌하지 않고 그 위에 덮는다.
/// 대상 = 컨트롤러에 등록된 스킬 전부(기본 4 + 대체 4 — 어쌔신 변신 Q/E 처럼 일반/변신 쿨을 따로 갖는 것도 모두). 남은 시간 + 여유만큼 줄여 0 으로 만든다
/// (장부가 0 아래로 내리지 않는다). 오너 HUD 미러는 장부 API 가 보낸다.
/// </summary>
public static class TutorialCooldownReset
{
    // 남은 시간을 그대로 빼면 부동소수 오차로 한 프레임 '아직'일 수 있다 — 조금 더 뺀다(결과는 now 에서 멈춘다).
    private const float Slack = 0.05f;

    private static readonly List<PlayerSkillBase> Buffer = new List<PlayerSkillBase>();

    /// <summary>[서버] <paramref name="include"/> 슬롯에 드는 등록 스킬의 쿨타임을 0 으로. 실제로 줄인 스킬 수.</summary>
    public static int ResetSkillsServer(Player player, Func<PlayerSkillSlot, bool> include)
    {
        if (player == null || !player.IsServer) return 0;
        var controller = player.GetComponent<PlayerSkillController>();
        if (controller == null) return 0;

        int n = 0;
        Buffer.Clear();
        player.GetComponentsInChildren(true, Buffer);
        foreach (PlayerSkillBase skill in Buffer)
        {
            if (skill == null || controller.GetSkillIndex(skill) < 0 || !include(skill.Slot)) continue;
            float remaining = controller.GetCooldownRemaining(skill);
            if (remaining <= 0f) continue;
            controller.ReduceCooldownServer(skill, remaining + Slack);
            n++;
        }
        Buffer.Clear();
        return n;
    }

    /// <summary>
    /// [서버] 끝날 때 쿨이 걸리는 행동이 진행 중인가 — 실행 중 스킬(거너 Q 집중 포함) · 어쌔신 변신 · 어쌔신 E 강화 대기.
    /// 복도1 초기화 예약(<see cref="TutorialCooldownResetModel"/>)이 이 값이 false 가 될 때까지 기다린다.
    /// </summary>
    public static bool IsBusy(Player player)
    {
        if (player == null) return false;
        var controller = player.GetComponent<PlayerSkillController>();
        if (controller != null && controller.IsSkillActive) return true;
        var assassin = player.GetComponent<AssassinState>();
        return assassin != null && (assassin.IsTransformed || assassin.IsEnhancedReady);
    }
}
