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
/// </list>
/// 끝·강제 리셋이면 가중치 0(Volume 끔).
/// </summary>
public sealed class InterruptHitLightPlayer : IDisposable
{
    private readonly Func<InterruptHitLightLook> _look;
    private readonly Func<InterruptHitLightFx> _fx;

    private GameObject _root;
    private Volume _bloomVolume;
    private float _startTime;
    private InterruptHitLightRequest _request;

    /// <param name="look">수치 SO(프레임마다 읽는다).</param>
    /// <param name="fx">월드 이펙트 인스턴스(첫 재생 때 디렉터가 만든다). null 이면 월드 이펙트 없음.</param>
    public InterruptHitLightPlayer(Func<InterruptHitLightLook> look, Func<InterruptHitLightFx> fx)
    {
        _look = look;
        _fx = fx;
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

        if (InterruptHitLightRules.Finished(look, elapsed))
            Finish();
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
    /// <summary>모든 곡선(라이트·블룸)이 끝났는가. 데이터가 없으면 끝.</summary>
    public static bool Finished(InterruptHitLightLook look, float elapsed) =>
        look == null || elapsed >= Duration(look);

    /// <summary>진행기 길이 — 곡선 길이 중 가장 긴 것.</summary>
    public static float Duration(InterruptHitLightLook look) =>
        look == null ? 0f : Mathf.Max(look.LightDuration, look.BloomDuration);
}
