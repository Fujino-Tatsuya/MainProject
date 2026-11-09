using UnityEngine;

/// <summary>
/// 보스 처치 연출 — 처치자 백대쉬 순수 계산(255 후속 3, 테스트 <c>BossVictoryBackDashTests</c>).
/// 값 = 설정 SO(<see cref="BossVictoryGlitchSettings.backDashDistance"/> 등, 기본 = 보스 등장 밀림 값 복사). 이동·리액션 애니는 <see cref="BossEncounterDirector.StepSkid"/> 그대로.
/// </summary>
public static class BossVictoryBackDash
{
    /// <summary>
    /// 백대쉬 방향(수평 단위 벡터) = 보스 → 처치자(보스 반대쪽)를 위에서 봐 <paramref name="angleDegrees"/> 만큼 시계 방향으로 돌린 것.
    /// 둘이 겹치면 처치자 뒤쪽(-정면), 그것도 없으면 월드 -Z.
    /// </summary>
    public static Vector3 Direction(Vector3 killer, Vector3 boss, Vector3 killerForward, float angleDegrees)
    {
        Vector3 away = killer - boss;
        away.y = 0f;
        if (away.sqrMagnitude <= 0.0001f)
        {
            away = -killerForward;
            away.y = 0f;
        }
        away = away.sqrMagnitude > 0.0001f ? away.normalized : Vector3.back;
        return Quaternion.Euler(0f, angleDegrees, 0f) * away;
    }
}
