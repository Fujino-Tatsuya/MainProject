using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 보스 처치 연출 — 애니 클립 노드 '끝 자르기'(255 후속 7, 테스트 <c>BossVictoryAnimTrimTests</c>). 1차 = 처치 연출 스킬 클립.
/// 자른 길이 <c>trim</c> = 클립 시간(초, 공격 속도 100% 기준 — 클립 프레임으로 자름). 0 = 안 자름(클립 끝까지 = 지금 동작).
/// 그 프레임에 닿으면 Animator 배율 0 으로 그 자세에 멈춘다 — 공속이 달라도 같은 프레임(클립 시간으로 판정).
/// 같은 장치를 23호 보스 피격 애니 노드에도 붙일 수 있게 Animator·상태 해시·자른 길이만 받는다(캐릭터·스킬을 모른다).
/// </summary>
public static class BossVictoryAnimTrim
{
    /// <summary>클립 프레임 레이트가 없으면(0) 쓰는 값.</summary>
    public const float FallbackFrameRate = 30f;

    public static float FrameRate(float clipFrameRate) => clipFrameRate > 0f ? clipFrameRate : FallbackFrameRate;

    /// <summary>초 → 프레임 수(반올림).</summary>
    public static int Frames(float seconds, float frameRate) => Mathf.Max(0, Mathf.RoundToInt(seconds * FrameRate(frameRate)));

    /// <summary>프레임 경계로 맞춘 초.</summary>
    public static float SnapToFrame(float seconds, float frameRate) => Frames(seconds, frameRate) / FrameRate(frameRate);

    /// <summary>
    /// 저장할 자른 길이 — 프레임에 맞추고 1프레임 이상, 원래 끝(<paramref name="fullLength"/>)에서 반 프레임 안이면 0(안 자름). 원래 끝보다 늘릴 수 없다.
    /// 원래 길이를 모르면(≤ 0) 프레임만 맞춘다.
    /// </summary>
    public static float ClampTrim(float trim, float fullLength, float frameRate)
    {
        if (trim <= 0f) return 0f;
        float fps = FrameRate(frameRate);
        float snapped = Mathf.Max(1f / fps, SnapToFrame(trim, fps));
        if (fullLength <= 0f) return snapped;
        return snapped >= fullLength - 0.5f / fps ? 0f : snapped;
    }

    /// <summary>잘렸는가(0 이 아니고 원래 끝보다 앞).</summary>
    public static bool IsTrimmed(float trim, float fullLength) => trim > 0f && (fullLength <= 0f || trim < fullLength);

    /// <summary>재생되는 길이 = 잘렸으면 자른 길이, 아니면 원래 길이.</summary>
    public static float PlayedLength(float trim, float fullLength) => IsTrimmed(trim, fullLength) ? trim : fullLength;

    /// <summary>"42/60f" — 재생되는 끝 프레임 / 원래 끝 프레임.</summary>
    public static string FrameLabel(float trim, float fullLength, float frameRate) =>
        $"{Frames(PlayedLength(trim, fullLength), frameRate)}/{Frames(fullLength, frameRate)}f";

    /// <summary>클립 시간 <paramref name="clipTime"/>(초)에 멈춰야 하는가.</summary>
    public static bool ShouldFreeze(float trim, float clipTime) => trim > 0f && clipTime >= trim;

    /// <summary>
    /// 잘린 끝에서 느려짐을 시작하는가 — '느려짐 시작' 마커가 꺼져 있고(≤ 0) 잘렸고, 자른 끝이 End 이벤트보다 앞(End 이벤트가 없으면 늘 — 안 그러면 영영 안 느려진다).
    /// End 이벤트가 자른 끝보다 앞이면 End 이벤트가 이미 느려짐을 시작했다(지금 규칙).
    /// </summary>
    public static bool SlowAtTrimEnd(float slowStart, float trim, float endEventTime) =>
        slowStart <= 0f && trim > 0f && (endEventTime < 0f || trim < endEventTime);

    static readonly List<AnimatorClipInfo> s_clips = new List<AnimatorClipInfo>(2);

    /// <summary>
    /// [이 피어] 0층이 <paramref name="stateHash"/>(짧은 이름 해시) 상태를 재생 중이고 클립 시간이 <paramref name="trim"/> 에 닿았으면 그 프레임으로 맞추고 true —
    /// 멈춤(배율 0)은 부른 쪽이 건다. 그 상태로 넘어가는 전이 중이면 다음 상태를 본다. <paramref name="clip"/> = 그 상태 클립(못 찾으면 null).
    /// </summary>
    public static bool TryReachTrim(Animator animator, int stateHash, float trim, out AnimationClip clip)
    {
        clip = null;
        if (animator == null || !animator.isActiveAndEnabled || trim <= 0f || stateHash == 0) return false;
        bool next = animator.IsInTransition(0) && animator.GetNextAnimatorStateInfo(0).shortNameHash == stateHash;
        AnimatorStateInfo info = next ? animator.GetNextAnimatorStateInfo(0) : animator.GetCurrentAnimatorStateInfo(0);
        if (info.shortNameHash != stateHash) return false;
        if (next) animator.GetNextAnimatorClipInfo(0, s_clips);
        else animator.GetCurrentAnimatorClipInfo(0, s_clips);
        if (s_clips.Count == 0 || s_clips[0].clip == null || s_clips[0].clip.length <= 0f) return false;
        clip = s_clips[0].clip;
        if (!ShouldFreeze(trim, info.normalizedTime * clip.length)) return false;
        animator.Play(stateHash, 0, Mathf.Clamp01(trim / clip.length));   // 넘친 1프레임 안쪽을 자른 프레임으로 되돌림(뒤로 건너뛰기라 이벤트 없음)
        return true;
    }
}
