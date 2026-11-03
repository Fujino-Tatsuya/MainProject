using System;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>적중 라이트 연출 한 번의 요청(할 일 194) — 디렉터가 성공 사건에서 만든다.</summary>
public struct InterruptHitLightRequest
{
    /// <summary>섬광 중심(대상 위치 + 높이).</summary>
    public Vector3 Point;
    /// <summary>판정 순간 대상 위치(암순응 원형 중심).</summary>
    public Vector3 TargetPosition;
    public ulong CasterClientId;
    /// <summary>이 화면이 발동자 본인인가.</summary>
    public bool ViewerIsCaster;
    public InterruptSuccessLook Look;
    public InterruptDarkAdaptMode DarkAdapt;
}

/// <summary>
/// 적중 라이트 연출 진행기(V1·V2, 할 일 194) — 성공 순간 시작해 매 프레임 실시간으로 진행한다(Q8). 값은 들지 않고 <see cref="InterruptHitLightLook"/> 을 프레임마다 읽는다.
/// <list type="bullet">
/// <item>월드 이펙트(<see cref="InterruptHitLightFx"/> — 라이트·불꽃·파편·빛줄기): 모든 피어. 시작 순간 한 번 튼다.</item>
/// <item>블룸: 이 출력 소유의 전역 Volume(우선순위 <see cref="InterruptHitFlashOverlay.VolumePriority"/>) 가중치만 곡선대로 — 모든 피어.</item>
/// <item>암순응 가(화면 패스): 기존 <see cref="InterruptDarkAdaptation"/> 에 프레임마다 어둡힘을 넣는다(본인 화면 강·팀원 화면 약, Q9).</item>
/// <item>암순응 나(씬 조명): <see cref="SceneLightDim"/> 에 태양광·환경광 배율을 넣는다 — 23호 차징의 태양광 끄기와 배율 곱이라 복구 순서가 꼬이지 않는다.</item>
/// </list>
/// 끝·강제 리셋이면 가중치 0(Volume 끔)·암순응 0·씬 조명 배율 뺌.
/// </summary>
public sealed class InterruptHitLightPlayer : IDisposable
{
    private readonly Func<InterruptHitLightLook> _look;
    private readonly Func<InterruptHitLightFx> _fx;
    private readonly Func<ISuppressDarkAdaptOutput> _darkAdapt;
    private readonly SceneLightDim _sceneDim;

    private GameObject _root;
    private Volume _bloomVolume;
    private float _startTime;
    private InterruptHitLightRequest _request;
    private ISuppressDarkAdaptOutput _darkAdaptShown; // 어둡힘을 넣은 출력(끝에 0 으로) — 없으면 null

    /// <param name="look">수치 SO(프레임마다 읽는다).</param>
    /// <param name="fx">월드 이펙트 인스턴스(첫 재생 때 디렉터가 만든다). null 이면 월드 이펙트 없음.</param>
    /// <param name="darkAdapt">암순응 가 출력(<see cref="InterruptDarkAdaptation"/>). null 이면 가 없음.</param>
    /// <param name="sceneDim">암순응 나 창구. null 이면 나 없음.</param>
    public InterruptHitLightPlayer(Func<InterruptHitLightLook> look, Func<InterruptHitLightFx> fx,
        Func<ISuppressDarkAdaptOutput> darkAdapt = null, SceneLightDim sceneDim = null)
    {
        _look = look;
        _fx = fx;
        _darkAdapt = darkAdapt;
        _sceneDim = sceneDim;
    }

    public bool IsPlaying { get; private set; }
    /// <summary>마지막 요청 — 확인용.</summary>
    public InterruptHitLightRequest Request => _request;
    /// <summary>지금 블룸 Volume 가중치(꺼짐 = 0) — 확인용.</summary>
    public float BloomWeight => _bloomVolume != null && _bloomVolume.enabled ? _bloomVolume.weight : 0f;

    /// <summary>성공 순간 시작(<paramref name="now"/> = 실시간). 돌던 연출은 새 것으로 바뀐다.</summary>
    public void Begin(in InterruptHitLightRequest request, float now)
    {
        InterruptHitLightLook look = _look?.Invoke();
        // 앞 연출의 암순응 방식이 다를 수 있다 — 넣어 둔 것을 먼저 걷는다(곧바로 새 값이 들어간다).
        ReleaseDarkAdapt();
        _request = request;
        _startTime = now;
        IsPlaying = true;

        InterruptHitLightFx fx = _fx?.Invoke();
        if (fx != null)
            fx.Play(request.Point, look);

        Tick(now);
    }

    /// <summary>매 프레임(실시간).</summary>
    public void Tick(float now)
    {
        if (!IsPlaying)
            return;

        InterruptHitLightLook look = _look?.Invoke();
        float elapsed = now - _startTime;
        float bloom = look != null ? InterruptEffectLight.Pulse(look.BloomCurve, 1f, elapsed, look.BloomDuration) : 0f;
        ApplyBloom(look, bloom);
        ApplyDarkAdapt(look, elapsed);

        if (InterruptHitLightRules.Finished(look, elapsed))
            Finish();
    }

    private void ApplyDarkAdapt(InterruptHitLightLook look, float elapsed)
    {
        if (look == null)
            return;

        if (_request.DarkAdapt == InterruptDarkAdaptMode.SceneLight)
        {
            if (_sceneDim == null)
                return;

            float envelope = InterruptEffectLight.Pulse(look.SceneDimCurve, 1f, elapsed, look.SceneDimDuration);
            _sceneDim.SetSun(this, InterruptHitLightRules.DimFactor(look.SunDimDepth, envelope));
            _sceneDim.SetAmbient(this, InterruptHitLightRules.DimFactor(look.AmbientDimDepth, envelope));
            return;
        }

        ISuppressDarkAdaptOutput output = _darkAdapt?.Invoke();
        if (output == null)
            return;

        float peak = InterruptHitLightRules.DarkAdaptPeak(_request.ViewerIsCaster, look.DarkAdaptSelf, look.DarkAdaptOthers);
        float darkness = InterruptEffectLight.Pulse(look.DarkAdaptCurve, peak, elapsed, look.DarkAdaptDuration);
        if (darkness > 0f)
        {
            output.ShowDarkAdaptCut(darkness, _request.CasterClientId, _request.TargetPosition);
            _darkAdaptShown = output;
        }
        else if (_darkAdaptShown != null)
        {
            _darkAdaptShown.ClearDarkAdapt();
            _darkAdaptShown = null;
        }
    }

    // 넣어 둔 암순응(가)·씬 조명 배율(나)을 걷는다.
    private void ReleaseDarkAdapt()
    {
        if (_darkAdaptShown != null)
        {
            _darkAdaptShown.ClearDarkAdapt();
            _darkAdaptShown = null;
        }

        _sceneDim?.Release(this); // 넣은 것이 없으면 아무것도 안 한다
    }

    /// <summary>강제 리셋·서버 거부 — 월드 이펙트·화면 효과 즉시 끔.</summary>
    public void Stop()
    {
        InterruptHitLightFx fx = _fx?.Invoke();
        if (fx != null)
            fx.Stop();
        Finish();
    }

    public void Dispose()
    {
        Stop();
        if (_root != null)
        {
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(_root);
            else
                UnityEngine.Object.DestroyImmediate(_root);
        }

        _root = null;
        _bloomVolume = null;
    }

    private void Finish()
    {
        IsPlaying = false;
        SetWeight(_bloomVolume, 0f);
        ReleaseDarkAdapt();
    }

    private void ApplyBloom(InterruptHitLightLook look, float weight)
    {
        VolumeProfile profile = look != null ? look.BloomProfile : null;
        if (profile == null || weight <= 0f)
        {
            SetWeight(_bloomVolume, 0f);
            return;
        }

        if (_bloomVolume == null)
        {
            EnsureRoot();
            var volumeObject = new GameObject("Bloom");
            volumeObject.transform.SetParent(_root.transform, false);
            _bloomVolume = volumeObject.AddComponent<Volume>();
            _bloomVolume.isGlobal = true;
            _bloomVolume.priority = InterruptHitFlashOverlay.VolumePriority;
        }

        // sharedProfile — 에셋을 복제·수정하지 않는다(가중치만).
        _bloomVolume.sharedProfile = profile;
        SetWeight(_bloomVolume, weight);
    }

    private void EnsureRoot()
    {
        if (_root != null)
            return;

        // Volume 이 카메라 Volume 마스크(Default)에 들도록 기본 레이어.
        _root = new GameObject("[InterruptHitLight]") { layer = 0 };
        if (Application.isPlaying)
            UnityEngine.Object.DontDestroyOnLoad(_root);
    }

    // 가중치 0 이면 꺼 둔다(Volume 섞기 비용 없음).
    private static void SetWeight(Volume volume, float weight)
    {
        if (volume == null)
            return;

        volume.weight = Mathf.Clamp01(weight);
        volume.enabled = volume.weight > 0f;
    }
}

/// <summary>적중 라이트 연출 순수 규칙(할 일 194) — EditMode 테스트 대상.</summary>
public static class InterruptHitLightRules
{
    /// <summary>모든 곡선(라이트·블룸·암순응)이 끝났는가. 데이터가 없으면 끝.</summary>
    public static bool Finished(InterruptHitLightLook look, float elapsed) =>
        look == null || elapsed >= Duration(look);

    /// <summary>진행기 길이 — 곡선 길이 중 가장 긴 것.</summary>
    public static float Duration(InterruptHitLightLook look) =>
        look == null
            ? 0f
            : Mathf.Max(Mathf.Max(look.LightDuration, look.BloomDuration), Mathf.Max(look.DarkAdaptDuration, look.SceneDimDuration));

    /// <summary>암순응 가 최대 어둡힘 — 본인 화면 = self, 팀원 화면 = others(Q9).</summary>
    public static float DarkAdaptPeak(bool viewerIsCaster, float self, float others) =>
        Mathf.Clamp01(viewerIsCaster ? self : others);

    /// <summary>암순응 나 배율 — 1 − 낮춤 비율 × 곡선 값. [0, 1].</summary>
    public static float DimFactor(float depth, float envelope) =>
        Mathf.Clamp01(1f - Mathf.Clamp01(depth) * Mathf.Max(0f, envelope));
}
