using System;

/// <summary>간파 훈련 봇 "우클릭!" 표시 규칙(할 일 303) — 순수 함수(EditMode 테스트 대상). 실행은 <see cref="LobbyCounterPrompt"/>.</summary>
public static class LobbyCounterPromptRules
{
    /// <summary>표시할 창인가 — 훈련 중(T31~T40) 그 봇의 간파 창 복제값이 열려 있을 때. 취약·제압이면 창이 안 열려 자동으로 false.</summary>
    public static bool WindowActive(LobbyTutorialActivity activity, bool replicatedWindowOpen) =>
        LobbyTutorialRules.IsSparringActivity(activity) && replicatedWindowOpen;

    /// <summary>
    /// 넉백을 넣을 차례인가 — T32(취약 중 CC)·T34(지정 벽). 진행기 단계와 봇 단계(둘 다 복제)가 같을 때만이라 T33 위치 복귀·단계 전환 중엔 꺼진다.
    /// 밀림·기절 반응 중(T34 빗나감 뒤 위치 복구 전 포함)엔 숨긴다 — 인정된 넉백은 이 반응으로 바로 숨고, 그 사이 단계가 넘어간다.
    /// </summary>
    public static bool KnockbackTurn(LobbyTutorialActivity activity, TrainingSparringStage stage, bool crowdControlReacting)
    {
        if (crowdControlReacting)
            return false;

        return (activity == LobbyTutorialActivity.VulnerableCc && stage == TrainingSparringStage.VulnerableCc)
            || (activity == LobbyTutorialActivity.Wall && stage == TrainingSparringStage.Wall);
    }

    /// <summary>우클릭을 안내할 때인가 — 간파 창이 열렸거나(<see cref="WindowActive"/>) 넉백 차례(<see cref="KnockbackTurn"/>).</summary>
    public static bool PromptActive(LobbyTutorialActivity activity, TrainingSparringStage stage, bool replicatedWindowOpen, bool crowdControlReacting) =>
        WindowActive(activity, replicatedWindowOpen) || KnockbackTurn(activity, stage, crowdControlReacting);

    /// <summary>지금 그리는가 — 간파 암전·섬광(A5 <see cref="InterruptProductionDirector.CombatUiHidden"/>) 동안엔 머리 위 체력바처럼 숨긴다.</summary>
    public static bool Visible(bool promptActive, bool combatUiHidden) => promptActive && !combatUiHidden;

    /// <summary>펄스를 새로 시작하는가 — 안내가 켜지는 순간만(T34 빗나감 뒤 다시 켜질 때 포함). A5 숨김이 풀려 다시 보일 때는 시작하지 않는다.</summary>
    public static bool PulseStarts(bool wasActive, bool active) => active && !wasActive;

    /// <summary>펄스 세기 — 시작 1 → <paramref name="duration"/> 뒤 0(감속). 길이 0 이하는 0.</summary>
    public static float Pulse(float elapsed, float duration)
    {
        if (duration <= 0f || elapsed >= duration)
            return 0f;

        float left = 1f - Math.Max(0f, elapsed) / duration;
        return left * left;
    }
}
