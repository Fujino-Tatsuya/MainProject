using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// CRT 파라미터를 커브로 흔드는 구동부. 연출은 전부 이 컴포넌트를 거친다.
/// </summary>
/// <remarks>
/// 🔴 머티리얼 저작값은 건드리지 않는다. <see cref="RetroCRTController"/> 의 런타임 오버라이드만 쓰고,
/// 버스트가 끝나면 <b>자기가 건 것만</b> 해제한다(다른 드라이버의 오버라이드를 지우지 않기 위해).
/// </remarks>
[DisallowMultipleComponent]
public sealed class CrtFxDriver : MonoBehaviour
{
    /// <summary>파라미터 하나를 커브 0→1 구간에서 <see cref="from"/> → <see cref="to"/> 로 움직인다.</summary>
    [Serializable]
    public sealed class Channel
    {
        public CrtParam param = CrtParam.WarpStrength;

        [Tooltip("커브가 0일 때의 값. 보통 머티리얼 저작값과 같게 둔다.")]
        public float from;

        [Tooltip("커브가 1일 때의 값.")]
        public float to = 1f;

        [Tooltip("시간 0~1 을 세기 0~1 로 바꾸는 커브.")]
        public AnimationCurve curve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        public float Evaluate(float t01) => Mathf.LerpUnclamped(from, to, curve.Evaluate(Mathf.Clamp01(t01)));
    }

    /// <summary>이름으로 재생하는 연출 한 덩어리.</summary>
    [Serializable]
    public sealed class Burst
    {
        [Tooltip("코드에서 이 이름으로 재생한다.")]
        public string id = "glitch";

        [Tooltip("초. 0 이하이면 한 프레임만 적용하고 끝난다.")]
        public float duration = 0.25f;

        [Tooltip("타임스케일 영향을 받지 않게 한다. 타이틀·일시정지 연출은 켜 두는 쪽이 맞다.")]
        public bool unscaledTime = true;

        public List<Channel> channels = new();
    }

    [SerializeField] private List<Burst> _bursts = new();

    [Tooltip("컨트롤러를 직접 꽂는다. 비우면 재생 시점의 ActiveController 를 쓴다.")]
    [SerializeField] private RetroCRTController _controller;

    private Coroutine _routine;
    private int _ownedMask;

    /// <summary>재생 중인 버스트가 있는가.</summary>
    public bool IsPlaying => _routine != null;

    private RetroCRTController Controller =>
        _controller != null ? _controller : RetroCRTController.ActiveController;

    /// <summary>이름으로 버스트를 재생한다. 이미 재생 중이면 중단하고 새로 시작한다.</summary>
    public void Play(string id)
    {
        Burst burst = Find(id);
        if (burst == null)
            return;

        Stop();

        if (!isActiveAndEnabled)
        {
            // 비활성 상태에서는 코루틴이 안 돈다. 값만 최종 상태로 찍고 끝낸다.
            Apply(burst, 1f);
            return;
        }

        _routine = StartCoroutine(Run(burst));
    }

    /// <summary>
    /// 코루틴 없이 진행도를 직접 먹인다. 카메라 블렌드 진행도처럼 <b>외부가 시간을 쥐고 있을 때</b> 쓴다.
    /// 끝나면 반드시 <see cref="Stop"/> 을 불러 해제한다.
    /// </summary>
    public void DriveSustain(string id, float t01)
    {
        Burst burst = Find(id);
        if (burst == null)
            return;

        Apply(burst, t01);
    }

    /// <summary>재생을 멈추고 이 드라이버가 건 오버라이드만 해제한다.</summary>
    public void Stop()
    {
        if (_routine != null)
        {
            StopCoroutine(_routine);
            _routine = null;
        }

        ReleaseOwned();
    }

    private IEnumerator Run(Burst burst)
    {
        float duration = Mathf.Max(0f, burst.duration);

        if (duration <= 0f)
        {
            Apply(burst, 1f);
            yield return null;
            Stop();
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            Apply(burst, elapsed / duration);
            yield return null;
            elapsed += burst.unscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        }

        Apply(burst, 1f);
        _routine = null;
        ReleaseOwned();
    }

    private void Apply(Burst burst, float t01)
    {
        RetroCRTController controller = Controller;
        if (controller == null)
            return;

        for (var i = 0; i < burst.channels.Count; i++)
        {
            Channel channel = burst.channels[i];
            if (channel == null)
                continue;

            controller.SetOverride(channel.param, channel.Evaluate(t01));
            _ownedMask |= 1 << (int)channel.param;
        }
    }

    private void ReleaseOwned()
    {
        if (_ownedMask == 0)
            return;

        RetroCRTController controller = Controller;
        if (controller != null)
        {
            for (var i = 0; i < CrtParamInfo.Count; i++)
            {
                if ((_ownedMask & (1 << i)) != 0)
                    controller.ClearOverride((CrtParam)i);
            }
        }

        _ownedMask = 0;
    }

    private Burst Find(string id)
    {
        for (var i = 0; i < _bursts.Count; i++)
        {
            if (_bursts[i] != null && _bursts[i].id == id)
                return _bursts[i];
        }

        Debug.LogWarning($"[CrtFx] '{id}' 버스트가 없다. 인스펙터의 Bursts 목록을 확인할 것.", this);
        return null;
    }

    private void OnDisable()
    {
        Stop();
    }
}
