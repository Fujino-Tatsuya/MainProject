using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 서버 안전망(실행 중 스킬 강제 종료 시각) 규칙. 정상 종료는 스킬(End 이벤트·타이머)이 하고, 이건 그게 안 올 때의 그물이다.
/// - 타이머 방식 = maxActiveDuration + padding (기존).
/// - 이벤트 방식(<see cref="PlayerSkillBase.EndsOnClipEvent"/> — 요구 이벤트에 End) = 상태 클립 실제 재생 시간 + padding.
///   실제 재생 시간 = 클립 길이 ÷ |상태 speed × speed 파라미터(speedMultiplier) × Animator.speed|.
///   클립을 못 찾거나 속도가 0 이면 타이머 방식으로 대체한다.
/// 이벤트 방식 상태 클립은 전부 한 번 재생(Loop Time 꺼짐)이다 — 끝에 머무는 동안 normalizedTime 은 1 을 넘어 커진다.
/// </summary>
public static class PlayerSkillSafetyNet
{
    private const float MinPlaybackRate = 0.0001f;

    public enum ProbeResult
    {
        NotInState, // 애니메이터가 아직 그 상태가 아니다(시전 직후 갱신 전 등) — 다음 틱에 다시 본다
        Resolved,   // 상태를 찾았고 실제 재생 시간을 구했다
        Invalid     // 상태는 찾았는데 클립이 없거나 재생 속도가 0 — 타이머 방식으로 대체
    }

    private static readonly List<AnimatorClipInfo> ClipInfoBuffer = new List<AnimatorClipInfo>(4);

    /// <summary>타이머 방식 안전망 길이(초).</summary>
    public static float TimerSeconds(float maxActiveDuration, float padding) =>
        maxActiveDuration + Mathf.Max(0f, padding);

    /// <summary>클립 한 번 재생의 실제 시간(초). 클립 길이 ≤ 0 이거나 재생 속도가 0 이면 false.</summary>
    public static bool TryGetPlaybackSeconds(
        float clipLength, float stateSpeed, float speedMultiplier, float animatorSpeed, out float seconds)
    {
        float rate = Mathf.Abs(stateSpeed * speedMultiplier * animatorSpeed);
        if (clipLength <= 0f || rate < MinPlaybackRate)
        {
            seconds = 0f;
            return false;
        }

        seconds = clipLength / rate;
        return true;
    }

    /// <summary>
    /// 안전망 길이(초). 이벤트 방식이고 클립 재생 시간을 구했으면 그 시간 + padding, 아니면 타이머 방식.
    /// <paramref name="usedClip"/> = 클립 기준을 썼는가.
    /// </summary>
    public static float Seconds(
        bool endsOnClipEvent, float maxActiveDuration, float padding,
        bool hasClip, float clipLength, float stateSpeed, float speedMultiplier, float animatorSpeed,
        out bool usedClip)
    {
        float playback = 0f;
        usedClip = endsOnClipEvent && hasClip &&
                   TryGetPlaybackSeconds(clipLength, stateSpeed, speedMultiplier, animatorSpeed, out playback);
        return usedClip ? playback + Mathf.Max(0f, padding) : TimerSeconds(maxActiveDuration, padding);
    }

    /// <summary>지금 정규화 시각에서 클립 끝까지 남은 실제 시간(초). 끝을 넘겼으면(normalizedTime ≥ 1) 0.</summary>
    public static float RemainingSeconds(float playbackSeconds, float normalizedTime) =>
        playbackSeconds * Mathf.Max(0f, 1f - normalizedTime);

    /// <summary>
    /// [런타임] 애니메이터의 모든 레이어에서 <paramref name="stateHash"/>(상태 짧은 이름 해시) 상태를 찾아 클립 실제 재생 시간과
    /// 정규화 시각을 읽는다. 전이 중이면 다음 상태를 먼저 본다(CrossFade 직후). 에디터 전용 API(AnimatorController)를 쓰지 않는다 —
    /// 상태 진입 뒤의 AnimatorStateInfo(speed·speedMultiplier·normalizedTime)·AnimatorClipInfo(clip.length)만 쓴다.
    /// </summary>
    public static ProbeResult Probe(Animator animator, int stateHash, out float playbackSeconds, out float normalizedTime)
    {
        playbackSeconds = 0f;
        normalizedTime = 0f;
        if (animator == null || !animator.isActiveAndEnabled || animator.runtimeAnimatorController == null)
            return ProbeResult.NotInState;

        for (int layer = 0; layer < animator.layerCount; layer++)
        {
            bool isNext = false;
            AnimatorStateInfo state = default;
            if (animator.IsInTransition(layer))
            {
                state = animator.GetNextAnimatorStateInfo(layer);
                isNext = state.shortNameHash == stateHash;
            }

            if (!isNext)
            {
                state = animator.GetCurrentAnimatorStateInfo(layer);
                if (state.shortNameHash != stateHash)
                    continue;
            }

            ClipInfoBuffer.Clear();
            if (isNext)
                animator.GetNextAnimatorClipInfo(layer, ClipInfoBuffer);
            else
                animator.GetCurrentAnimatorClipInfo(layer, ClipInfoBuffer);

            float clipLength = 0f;
            float bestWeight = -1f;
            foreach (AnimatorClipInfo info in ClipInfoBuffer)
            {
                if (info.clip == null || info.weight <= bestWeight)
                    continue;
                bestWeight = info.weight;
                clipLength = info.clip.length;
            }

            normalizedTime = state.normalizedTime;
            return TryGetPlaybackSeconds(clipLength, state.speed, state.speedMultiplier, animator.speed, out playbackSeconds)
                ? ProbeResult.Resolved
                : ProbeResult.Invalid;
        }

        return ProbeResult.NotInState;
    }
}
