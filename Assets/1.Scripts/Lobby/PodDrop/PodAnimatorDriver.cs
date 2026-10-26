using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 발사대·포드 Animator 를 시간표 시각으로 세운다(할 일 100 C2i — 로비 <see cref="LobbyPodLaunch"/>·맵 <see cref="MapPodArrival"/>).
/// 처음 세울 때 <c>Animator.speed = 0</c> 으로 두고 매 프레임 <c>Play(상태, 레이어, normalizedTime)</c> 로 자세를 직접 정한다 —
/// 같은 경과 시각이면 모든 피어가 같은 자세다(늦게 받은 피어도). 컨트롤러(AC_DropPod_LaunchStation_v001)에 파라미터·전환이 없어 이 방식만 쓴다.
/// 상태 이름 = 클립 이름이라 초 → normalizedTime 변환용 클립 길이는 컨트롤러 클립 목록에서 이름으로 찾는다.
/// </summary>
public sealed class PodAnimatorDriver
{
    private const string Tag = "[PodAnim]";

    private readonly Animator _animator;
    private readonly Dictionary<string, float> _lengths = new Dictionary<string, float>();
    private readonly HashSet<string> _warned = new HashSet<string>();
    private bool _driven;

    public PodAnimatorDriver(Animator animator)
    {
        _animator = animator;
    }

    /// <summary>상태를 클립 시각(초)으로 세운다(클립 끝을 넘으면 끝 자세). <paramref name="layer"/> −1 = 그 상태가 있는 가장 위 레이어.</summary>
    public void PoseSeconds(string state, int layer, float seconds)
    {
        float length = ClipLength(state);
        if (length > 0f)
            PoseNormalized(state, layer, seconds / length);
    }

    /// <summary>상태를 진행률(0 = 클립 처음, 1 = 끝)로 세운다. <paramref name="layer"/> −1 = 그 상태가 있는 가장 위 레이어(마스크 레이어 먼저).</summary>
    public void PoseNormalized(string state, int layer, float normalized)
    {
        if (_animator == null || _animator.runtimeAnimatorController == null || string.IsNullOrEmpty(state))
            return;

        int hash = Animator.StringToHash(state);
        if (layer < 0)
            layer = TopLayerWith(hash);
        if (layer < 0 || layer >= _animator.layerCount || !_animator.HasState(layer, hash))
        {
            Warn(state, $"상태 '{state}' 가 컨트롤러 '{_animator.runtimeAnimatorController.name}' 에 없다 — 이 애니는 건너뛴다.");
            return;
        }

        if (!_driven)
        {
            _driven = true;
            _animator.speed = 0f;
        }

        _animator.Play(hash, layer, Mathf.Clamp01(normalized));
    }

    /// <summary>손을 뗀다 — 속도 1, 기본 상태(DP_Idle·PassThrough)로 되돌린다. 멱등.</summary>
    public void Release()
    {
        if (!_driven)
            return;

        _driven = false;
        if (_animator == null)
            return;

        _animator.speed = 1f;
        _animator.Rebind();
    }

    private int TopLayerWith(int hash)
    {
        for (int layer = _animator.layerCount - 1; layer >= 0; layer--)
        {
            if (_animator.HasState(layer, hash))
                return layer;
        }

        return -1;
    }

    private float ClipLength(string state)
    {
        if (string.IsNullOrEmpty(state) || _animator == null || _animator.runtimeAnimatorController == null)
            return 0f;

        if (_lengths.TryGetValue(state, out float length))
            return length;

        length = 0f;
        foreach (AnimationClip clip in _animator.runtimeAnimatorController.animationClips)
        {
            if (clip != null && clip.name == state)
            {
                length = clip.length;
                break;
            }
        }

        if (length <= 0f)
            Warn(state, $"클립 '{state}' 를 컨트롤러 '{_animator.runtimeAnimatorController.name}' 에서 못 찾았다(상태 이름 = 클립 이름이어야 한다) — 이 애니는 건너뛴다.");
        _lengths[state] = length;
        return length;
    }

    private void Warn(string state, string message)
    {
        if (_warned.Add(state))
            Debug.LogWarning($"{Tag} {message}", _animator);
    }
}
