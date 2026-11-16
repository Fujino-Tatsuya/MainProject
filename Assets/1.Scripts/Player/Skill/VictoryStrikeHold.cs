using UnityEngine;

/// <summary>
/// 보스 처치 연출 낙하 검 수명(프로필 <c>fallingSwordLife</c>) — 순수 시계(EditMode 테스트).
/// 낙하 검 엔트리(FX_Lightening_Strike)는 전부 파티클이라 칼 입자 수명(2.15초)이 끝나면 화면에서 사라진다 — 매니저 반납 시각(durationOverride)을
/// 늦춰도 그림은 그대로였다. 그래서: 이펙트 시간으로 <see cref="HoldAt"/> 에 이르면(칼이 꽂혀 아직 불투명할 때) 멈추고(재생 배율 0 —
/// 파티클과 매니저 수명이 함께 언다), <see cref="HoldFor"/> 초(unscaled) 뒤 원래 배율로 풀어 엔트리의 원래 끝(페이드)으로 사라진다.
/// </summary>
public struct VictoryStrikeHold
{
    /// <summary>
    /// 멈출 시점 = 엔트리 자연 길이 × 이 비율. 칼(HolySword) 알파는 입자 수명 81% 까지 1, 그 뒤 0 으로 페이드 —
    /// 그 전(엔트리 2.2초 × 0.75 = 1.65초)에서 멈춰야 꽂힌 모습이 선명하게 남는다.
    /// </summary>
    public const float HoldAtFraction = 0.75f;

    public enum Action { None, Freeze, Resume }
    public enum Phase { Off, Running, Holding }

    public float HoldAt { get; private set; }
    public float HoldFor { get; private set; }
    public Phase State { get; private set; }
    float _elapsed;
    float _held;

    /// <summary><paramref name="naturalDuration"/> = 엔트리 길이(초), <paramref name="life"/> = 꽂힌 채 남을 시간(초). life ≤ 0 이면 꺼짐(원래 그대로).</summary>
    public static VictoryStrikeHold Create(float naturalDuration, float life)
    {
        if (life <= 0f || naturalDuration <= 0f)
            return default;
        return new VictoryStrikeHold
        {
            HoldAt = naturalDuration * HoldAtFraction,
            HoldFor = life,
            State = Phase.Running,
        };
    }

    /// <summary>
    /// 한 프레임. <paramref name="effectDelta"/> = 이펙트 시간(deltaTime × 재생 배율, 멈추기 전까지), <paramref name="unscaledDelta"/> = 멈춘 동안 셀 실제 시간.
    /// 멈출 순간 Freeze, 풀 순간 Resume(그 뒤 Off), 그 밖엔 None.
    /// </summary>
    public Action Step(float effectDelta, float unscaledDelta)
    {
        switch (State)
        {
            case Phase.Running:
                _elapsed += Mathf.Max(0f, effectDelta);
                if (_elapsed < HoldAt) return Action.None;
                State = Phase.Holding;
                return Action.Freeze;
            case Phase.Holding:
                _held += Mathf.Max(0f, unscaledDelta);
                if (_held < HoldFor) return Action.None;
                State = Phase.Off;
                return Action.Resume;
            default:
                return Action.None;
        }
    }
}
