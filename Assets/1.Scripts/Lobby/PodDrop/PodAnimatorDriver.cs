using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 발사대·포드 Animator 를 1배속으로 재생한다(할 일 106 — 로비 <see cref="LobbyPodLaunch"/>·맵 <see cref="MapPodArrival"/>).
/// 상태마다 정해진 시각에 한 번 <c>Play</c> 하고 Animator 가 그대로 돌게 둔다 — 클립 애니 이벤트가 뜨고(<see cref="ClipEvent"/>, <see cref="PodAnimEventRelay"/>),
/// 끝난 클립은 끝 자세로 남는다. 늦게 시작한 피어는 지난 만큼 클립 중간에서 시작한다(그 사이 이벤트는 뜨지 않는다 — 단계 확정은 서버 몫이라 상관없다).
/// 컨트롤러(AC_DropPod_LaunchStation_v001)에 파라미터·전환이 없어 상태를 직접 Play 한다. 상태 이름 = 클립 이름이라 초 → normalizedTime 변환용 길이는 컨트롤러 클립 목록에서 찾는다.
/// 연출 동안은 화면 밖에서도 돌게(서버 이벤트) cullingMode 를 AlwaysAnimate 로 두고, <see cref="Release"/> 에서 되돌린다.
/// </summary>
public sealed class PodAnimatorDriver
{
    private const string Tag = "[PodAnim]";

    private readonly Animator _animator;
    private readonly PodAnimEventRelay _relay;
    private readonly Dictionary<string, float> _lengths = new Dictionary<string, float>();
    private readonly Dictionary<string, int> _layers = new Dictionary<string, int>();
    private readonly HashSet<string> _seen = new HashSet<string>();
    private readonly HashSet<string> _posed = new HashSet<string>();
    private readonly HashSet<string> _held = new HashSet<string>();
    private readonly HashSet<string> _warned = new HashSet<string>();
    private AnimatorCullingMode _cullingMode;
    private bool _driven;

    /// <summary>[전 피어] 클립 애니 이벤트(<see cref="PodAnimEventRelay.FunctionName"/>) — 이벤트가 실린 클립 이름.</summary>
    public event Action<string> ClipEvent;

    public PodAnimatorDriver(Animator animator)
    {
        _animator = animator;
        if (_animator == null)
            return;

        _relay = _animator.GetComponent<PodAnimEventRelay>();
        if (_relay == null) _relay = _animator.gameObject.AddComponent<PodAnimEventRelay>();
        _relay.Fired += HandleFired;
    }

    /// <summary>이 상태를 이미 재생 시작했는가(없는 상태도 시작한 것으로 친다 — 다시 찾지 않는다).</summary>
    public bool HasStarted(string state) => !string.IsNullOrEmpty(state) && _layers.ContainsKey(state);

    /// <summary>
    /// 상태를 1배속으로 재생 시작한다 — 이미 시작했으면 아무것도 안 한다. <paramref name="secondsIn"/> = 재생 시작 시각에서 지난 초(늦게 본 피어가 따라잡는다, 클립 끝을 넘으면 끝 자세).
    /// <paramref name="layer"/> −1 = 그 상태가 있는 가장 위 레이어(마스크 레이어 먼저).
    /// </summary>
    public void Play(string state, int layer, float secondsIn)
    {
        if (_animator == null || _animator.runtimeAnimatorController == null || string.IsNullOrEmpty(state) || HasStarted(state))
            return;

        int hash = Animator.StringToHash(state);
        if (layer < 0)
            layer = TopLayerWith(hash);
        if (layer < 0 || layer >= _animator.layerCount || !_animator.HasState(layer, hash))
        {
            _layers[state] = -1;
            Warn(state, $"상태 '{state}' 가 컨트롤러 '{_animator.runtimeAnimatorController.name}' 에 없다 — 이 애니는 건너뛴다(끝난 것으로 친다).");
            return;
        }

        BeginDrive();
        _held.Remove(state);
        float length = ClipLength(state);
        float normalized = length > 0f ? Mathf.Clamp01(Mathf.Max(0f, secondsIn) / length) : 0f;
        _layers[state] = layer;
        _animator.speed = 1f;
        _animator.Play(hash, layer, normalized);
    }

    /// <summary>
    /// 재생 전 대기 자세 — 상태를 클립 처음 자세로 세우고 멈춘다(Animator 전체 속도 0). 이미 세웠거나 재생 시작했으면 아무것도 안 한다.
    /// 다음 <see cref="Play"/> 가 1배속으로 처음부터 돌린다. 없는 상태는 조용히 건너뛴다(<see cref="Play"/> 가 경고).
    /// </summary>
    public void PoseStart(string state, int layer)
    {
        if (_animator == null || _animator.runtimeAnimatorController == null || string.IsNullOrEmpty(state) || HasStarted(state) || !_posed.Add(state))
            return;

        int hash = Animator.StringToHash(state);
        if (layer < 0)
            layer = TopLayerWith(hash);
        if (layer < 0 || layer >= _animator.layerCount || !_animator.HasState(layer, hash))
            return;

        BeginDrive();
        _animator.speed = 0f;
        _animator.Play(hash, layer, 0f);
    }

    /// <summary>
    /// 이 상태만 클립 처음 자세에 고정한다 — 매 프레임 불러야 한다(다른 레이어가 1배속으로 돌아도 이 레이어는 매번 0 으로 되돌린다).
    /// 고정 중 이 클립의 이벤트는 버린다(<see cref="ClipEvent"/> 없음). 다음 <see cref="Play"/> 가 고정을 풀고 재생한다. 이미 재생 시작했거나 없는 상태면 아무것도 안 한다.
    /// 맵 해치(닫힌 자세)를 날개·다리 펴기 동안 물고 있을 때 쓴다 — 마스크 레이어라 다른 레이어 클립이 덮지 않는다.
    /// </summary>
    public void HoldAtStart(string state, int layer)
    {
        if (_animator == null || _animator.runtimeAnimatorController == null || string.IsNullOrEmpty(state) || HasStarted(state))
            return;

        int hash = Animator.StringToHash(state);
        if (layer < 0)
            layer = TopLayerWith(hash);
        if (layer < 0 || layer >= _animator.layerCount || !_animator.HasState(layer, hash))
            return;

        BeginDrive();
        _held.Add(state);
        _animator.Play(hash, layer, 0f);
    }

    private void BeginDrive()
    {
        if (_driven)
            return;

        _driven = true;
        _cullingMode = _animator.cullingMode;
        _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
    }

    /// <summary>지금 자세로 멈춘다(Animator 전체 속도 0) — 다음 <see cref="Play"/> 가 다시 1배속으로 돌린다. 로비 해치를 이벤트 프레임에 세울 때 쓴다.</summary>
    public void Hold()
    {
        if (_animator != null && _driven)
            _animator.speed = 0f;
    }

    /// <summary>
    /// 재생한 상태가 끝까지 돌았는가(이벤트가 빠졌을 때의 예비 판정). 비운 이름·Animator/컨트롤러 없음·컨트롤러에 없는 상태 = 참, 아직 시작 안 함 = 거짓.
    /// 같은 레이어가 다른 상태로 넘어간 뒤면 참. LateUpdate 에서 부르면 이번 프레임 이벤트가 먼저 처리돼 있다.
    /// </summary>
    public bool IsFinished(string state)
    {
        if (string.IsNullOrEmpty(state) || _animator == null || _animator.runtimeAnimatorController == null) return true;
        if (!_layers.TryGetValue(state, out int layer)) return false;
        if (layer < 0 || _animator == null) return true;

        AnimatorStateInfo info = _animator.GetCurrentAnimatorStateInfo(layer);
        if (info.shortNameHash != Animator.StringToHash(state))
            return _seen.Contains(state); // Play 직후(아직 이전 상태) 는 거짓, 지나간 뒤는 참
        _seen.Add(state);
        return info.normalizedTime >= 1f;
    }

    /// <summary>손을 뗀다 — 속도 1, 컬링 원래대로, 기본 상태(DP_Idle·PassThrough)로 되돌린다. 멱등.</summary>
    public void Release()
    {
        _layers.Clear();
        _seen.Clear();
        _posed.Clear();
        _held.Clear();
        if (!_driven)
            return;

        _driven = false;
        if (_animator == null)
            return;

        _animator.speed = 1f;
        _animator.cullingMode = _cullingMode;
        _animator.Rebind();
    }

    private void HandleFired(string clipName)
    {
        if (_held.Contains(clipName))
            return; // 처음 자세에 고정한 클립 — 단계 전환 이벤트로 치지 않는다

        ClipEvent?.Invoke(clipName);
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
            Warn(state, $"클립 '{state}' 를 컨트롤러 '{_animator.runtimeAnimatorController.name}' 에서 못 찾았다(상태 이름 = 클립 이름이어야 한다) — 처음부터 재생한다.");
        _lengths[state] = length;
        return length;
    }

    private void Warn(string state, string message)
    {
        if (_warned.Add(state))
            Debug.LogWarning($"{Tag} {message}", _animator);
    }
}

/// <summary>
/// 같이 재생하는 클립 묶음의 다음 단계 문(할 일 106, 순수) — 묶음의 클립마다 이벤트(<see cref="PodAnimEventRelay"/>)가 오면 열린다.
/// 이벤트가 빠진 클립은 끝까지 돈 것(<see cref="PodAnimatorDriver.IsFinished"/>)으로 대신한다 — 연출이 멈춰 갇히지 않게(호출자가 경고한다).
/// 비운 상태 이름은 묶음에서 뺀다(전부 비면 처음부터 열려 있다).
/// </summary>
public sealed class PodClipGate
{
    private readonly List<string> _states = new List<string>();
    private readonly HashSet<string> _received = new HashSet<string>();

    public PodClipGate(IEnumerable<string> states)
    {
        if (states == null) return;
        foreach (string state in states)
        {
            if (!string.IsNullOrEmpty(state) && !_states.Contains(state))
                _states.Add(state);
        }
    }

    /// <summary>묶음의 상태(비운 칸 뺀 것).</summary>
    public IReadOnlyList<string> States => _states;

    /// <summary>이벤트가 온 클립을 적는다 — 묶음에 없는 클립이면 false.</summary>
    public bool Notify(string clipName)
    {
        if (!_states.Contains(clipName)) return false;
        _received.Add(clipName);
        return true;
    }

    /// <summary>
    /// 열렸는가 — 묶음의 모든 클립이 이벤트를 받았거나 끝까지 돌았다(<paramref name="isFinished"/>).
    /// <paramref name="fallbacks"/> = 이벤트 없이 끝으로 대신한 클립(열렸을 때만 채운다, 없으면 null).
    /// </summary>
    public bool IsOpen(Func<string, bool> isFinished, out List<string> fallbacks)
    {
        fallbacks = null;
        foreach (string state in _states)
        {
            if (_received.Contains(state)) continue;
            if (isFinished == null || !isFinished(state))
            {
                fallbacks = null;
                return false;
            }

            (fallbacks ??= new List<string>()).Add(state);
        }

        return true;
    }
}
