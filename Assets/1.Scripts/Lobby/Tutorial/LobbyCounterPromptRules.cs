using System;

/// <summary>간파 훈련 봇 "우클릭!" 표시 규칙(할 일 303) — 순수 함수(EditMode 테스트 대상). 실행은 <see cref="LobbyCounterPrompt"/>.</summary>
public static class LobbyCounterPromptRules
{
    /// <summary>표시할 창인가 — 훈련 중(T31~T40) 그 봇의 간파 창 복제값이 열려 있을 때. 취약·제압이면 창이 안 열려 자동으로 false.</summary>
    public static bool WindowActive(LobbyTutorialActivity activity, bool replicatedWindowOpen) =>
        LobbyTutorialRules.IsSparringActivity(activity) && replicatedWindowOpen;

    /// <summary>지금 그리는가 — 간파 암전·섬광(A5 <see cref="InterruptProductionDirector.CombatUiHidden"/>) 동안엔 머리 위 체력바처럼 숨긴다.</summary>
    public static bool Visible(bool windowActive, bool combatUiHidden) => windowActive && !combatUiHidden;

    /// <summary>펄스를 새로 시작하는가 — 창이 열리는 순간만. A5 숨김이 풀려 다시 보일 때는 시작하지 않는다.</summary>
    public static bool PulseStarts(bool wasWindowActive, bool windowActive) => windowActive && !wasWindowActive;

    /// <summary>펄스 세기 — 시작 1 → <paramref name="duration"/> 뒤 0(감속). 길이 0 이하는 0.</summary>
    public static float Pulse(float elapsed, float duration)
    {
        if (duration <= 0f || elapsed >= duration)
            return 0f;

        float left = 1f - Math.Max(0f, elapsed) / duration;
        return left * left;
    }
}
