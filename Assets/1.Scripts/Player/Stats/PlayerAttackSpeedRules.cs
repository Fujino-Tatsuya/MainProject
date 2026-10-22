using UnityEngine;

/// <summary>
/// 공격속도 배율 소비 규칙(할 일 91 T5) — 평타·스킬 재생 속도에 곱하는 값과 시간 값을 나누는 규칙을 한 곳에 둔다.
/// 배율 = 최종 공격속도 ÷ 기본값(<see cref="PlayerStats.Multiplier"/>, 최종 배율 포함). 상태이상은 복제되므로 서버·오너·관찰자가 같은 값을 낸다.
///   · 클립 이벤트로 타이밍을 정하는 동작(가붕이 콤보·어쌔신 평타·이벤트 방식 스킬) = 애니 재생 속도 × 배율 — 이벤트가 같이 빨라진다.
///   · 데이터 시간 값으로 타이밍을 정하는 평타(거너 준비·발사 간격·입력 창) = 그 시간 ÷ 배율.
///   · 고정 타이밍(대시 지속, 거너 충전, 채널링·홀드 틱 간격, 어쌔신 돌진 거리·시간, 타이머 방식 스킬) = 배율을 쓰지 않는다.
/// 전진 거리는 배율과 무관하게 같다 — 거리 ÷ (재생 시간 ÷ 배율) 로 속도만 바뀐다.
/// MonoBehaviour 아님 — EditMode 테스트 대상.
/// </summary>
public static class PlayerAttackSpeedRules
{
    /// <summary>배율 하한 — 0 에 가까운 감속이 와도 재생이 멈추거나 시간이 무한대가 되지 않게.</summary>
    public const float MinRate = 0.1f;

    /// <summary>재생 속도에 쓸 배율. 비정상 값(NaN·무한대)은 1, 그 밖은 하한 <see cref="MinRate"/>.</summary>
    public static float Rate(float multiplier)
    {
        if (float.IsNaN(multiplier) || float.IsInfinity(multiplier))
            return 1f;

        return Mathf.Max(MinRate, multiplier);
    }

    /// <summary>클립·데이터 기준 시간(초)을 실제 시간으로 — 시간 ÷ 배율.</summary>
    public static float Duration(float seconds, float multiplier) => seconds / Rate(multiplier);

    /// <summary>
    /// 서버 시간 안전망(초) — 감속(배율 &lt; 1)이면 시간 ÷ 배율로 늘리고, 가속이면 줄이지 않는다.
    /// 안전망은 시작 때 한 번 정하는데 배율은 도중에 바뀐다(가속 버프가 스킬 중간에 끝남) — 줄여 두면 느려진 나머지 재생보다 먼저 끊는다.
    /// 가속 중 정상 종료는 클립 이벤트가 먼저 낸다.
    /// </summary>
    public static float SafetyNetDuration(float seconds, float multiplier) => seconds / Mathf.Min(1f, Rate(multiplier));

    /// <summary>Animator.speed — 공격속도를 타는 동작 중이면 배율, 아니면 1.</summary>
    public static float AnimatorSpeed(bool scalesWithAttackSpeed, float multiplier) =>
        scalesWithAttackSpeed ? Rate(multiplier) : 1f;
}
