using UnityEngine;

/// <summary>
/// 애니메이션 클립 관련 계산 유틸리티.
/// </summary>
public static class AnimClipUtility
{
    /// <summary>
    /// Animator가 가진 특정 클립의 재생 시간을, 잘라낸 구간과 배속을 고려해 계산합니다.
    /// (BT의 GetAnimClipPlayTimeAction과 동일한 로직의 static 버전)
    /// </summary>
    /// <param name="animator">클립을 찾을 대상 Animator</param>
    /// <param name="animClip">재생 시간을 구할 클립 이름</param>
    /// <param name="multiplier">애니메이션 배속 파라미터 이름(없으면 빈 문자열 → 배속 1)</param>
    /// <param name="clipStart">잘라낸 구간 시작(normalized %, 0~100)</param>
    /// <param name="clipEnd">잘라낸 구간 끝(normalized %, 0~100). 0이면 100으로 간주</param>
    /// <returns>배속·구간을 반영한 실제 재생 시간(초). 실패 시 0을 반환</returns>
    public static float GetPlayTime(Animator animator, string animClip, string multiplier = "", float clipStart = 0f, float clipEnd = 100f)
    {
        if (animator == null)
        {
            Debug.LogError("[AnimClipUtility] animator is null");
            return 0f;
        }

        if (string.IsNullOrEmpty(animClip))
        {
            Debug.LogError("[AnimClipUtility] animClip is null or empty");
            return 0f;
        }

        float animSpeed = 1f;
        if (!string.IsNullOrEmpty(multiplier))
        {
            animSpeed = animator.GetFloat(multiplier);
        }

        if (animSpeed <= 0f)
        {
            Debug.LogError($"[AnimClipUtility] animSpeed({animSpeed})가 0 이하입니다. 배속 파라미터를 확인하세요.");
            return 0f;
        }

        if (clipEnd == 0f)
        {
            clipEnd = 100f;
        }

        RuntimeAnimatorController controller = animator.runtimeAnimatorController;
        if (controller == null)
        {
            Debug.LogError("[AnimClipUtility] runtimeAnimatorController is null");
            return 0f;
        }

        AnimationClip targetClip = null;
        foreach (var clip in controller.animationClips)
        {
            if (clip.name == animClip)
            {
                targetClip = clip;
                break;
            }
        }

        if (targetClip == null)
        {
            Debug.LogError($"[AnimClipUtility] '{animClip}' 클립을 Animator에서 찾을 수 없습니다.");
            return 0f;
        }

        float baseLength = targetClip.length;                          // 클립 기본 재생 길이
        float trimmedLength = baseLength * (clipEnd - clipStart) / 100f; // 잘라낸 구간 길이
        float realPlayTime = trimmedLength / animSpeed;                 // 배속 반영 실제 시간

        return realPlayTime;
    }
}
