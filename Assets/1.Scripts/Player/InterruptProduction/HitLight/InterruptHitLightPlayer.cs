using System;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>적중 라이트 연출 한 번의 요청(할 일 194) — 디렉터가 성공 사건에서 만든다.</summary>
public struct InterruptHitLightRequest
{
    /// <summary>섬광 중심(대상 위치 + 높이) — 화이트아웃 기준·월드 이펙트 대체 위치.</summary>
    public Vector3 Point;
    /// <summary>시전자(간파한 플레이어) — 월드 이펙트를 그 오른손 무기에서 낼 때 찾는다(194 후속).</summary>
    public ulong CasterClientId;
    /// <summary>월드 이펙트 발생 위치 — 시전자 오른손 무기(기본) / 대상.</summary>
    public InterruptHitLightOrigin Origin;
    /// <summary>이 화면이 발동자 본인인가.</summary>
    public bool ViewerIsCaster;
    public InterruptSuccessLook Look;
    public InterruptDarkAdaptMode DarkAdapt;
}

/// <summary>
/// 적중 라이트 연출 진행기(V1·V2, 할 일 194) — 성공 순간 시작해 매 프레임 실시간으로 진행한다(Q8). 값은 들지 않고 <see cref="InterruptHitLightLook"/> 을 프레임마다 읽는다.
/// <list type="bullet">
/// <item>월드 이펙트(<see cref="InterruptHitLightFx"/> — 라이트·불꽃·파편·빛줄기): 모든 피어. 시작 순간 한 번 튼다 — 위치 = 그 순간 시전자 오른손 무기(못 찾으면 대상), 따라다니지 않는다.</item>
/// <item>블룸·색 보정(V3 채도·색 필터): 이 출력 소유의 전역 Volume 2개(우선순위 <see cref="InterruptHitFlashOverlay.VolumePriority"/>) 가중치만 곡선대로 — 모든 피어.</item>
/// <item>데이터·월드 이펙트는 요청의 섬광 종류로 고른다 — V3 는 전용 SO·프리팹(194 후속), V1·V2 는 공용.</item>
/// <item>암순응 가(화면 패스): 기존 <see cref="InterruptDarkAdaptation"/> 에 프레임마다 어둡힘을 넣는다(본인 화면 강·팀원 화면 약, Q9).</item>
/// <item>암순응 나(씬 조명): <see cref="SceneLightDim"/> 에 태양광·환경광 배율을 넣는다 — 23호 차징의 태양광 끄기와 배율 곱이라 복구 순서가 꼬이지 않는다.</item>
/// <item>V2 화이트아웃: 본인 화면만, 기존 ③ 화이트아웃 그림을 렌더 프레임 수만큼(<see cref="InterruptHitLightLook.WhiteoutFrames"/>).</item>
/// </list>
/// 끝·강제 리셋이면 가중치 0(Volume 끔)·암순응 0·씬 조명 배율 뺌·화이트아웃 숨김.
/// </summary>
public sealed class InterruptHitLightPlayer : IDisposable
{
    private readonly Func<InterruptSuccessLook, InterruptHitLightLook> _look;
    private readonly Func<InterruptSuccessLook, InterruptHitLightFx> _fx;
    private readonly Func<ISuppressDarkAdaptOutput> _darkAdapt;
    private readonly SceneLightDim _sceneDim;
    private readonly IInterruptHitFlashOverlayOutput _whiteout;
    private readonly Func<ulong, Vector3?> _casterWeapon;

    private GameObject _root;
    private Volume _bloomVolume;
    private Volume _gradeVolume;
    private InterruptHitLightFx _playedFx; // 마지막으로 튼 월드 이펙트(V1·V2 와 V3 는 인스턴스가 다르다)
    private float _startTime;
    private InterruptHitLightRequest _request;
    private ISuppressDarkAdaptOutput _darkAdaptShown; // 어둡힘을 넣은 출력(끝에 0 으로) — 없으면 null
    private int _whiteoutStartFrame;
    private int _whiteoutFrames;   // 이번 연출의 화이트아웃 프레임 수(0 = 없음·끝남)
    private bool _whiteoutShown;

    /// <param name="look">수치 SO(프레임마다 읽는다).</param>
    /// <param name="fx">월드 이펙트 인스턴스(첫 재생 때 디렉터가 만든다). null 이면 월드 이펙트 없음.</param>
    /// <param name="darkAdapt">암순응 가 출력(<see cref="InterruptDarkAdaptation"/>). null 이면 가 없음.</param>
    /// <param name="sceneDim">암순응 나 창구. null 이면 나 없음.</param>
    /// <param name="whiteout">V2 화이트아웃 출력(③ 그림만 그리는 적중 섬광 오버레이). null 이면 화이트아웃 없음.</param>
    /// <param name="casterWeapon">시전자 → 지금 오른손 무기 위치(없으면 null). null 이면 월드 이펙트는 늘 대상 적중점.</param>
    public InterruptHitLightPlayer(Func<InterruptHitLightLook> look, Func<InterruptHitLightFx> fx,
        Func<ISuppressDarkAdaptOutput> darkAdapt = null, SceneLightDim sceneDim = null, IInterruptHitFlashOverlayOutput whiteout = null,
        Func<ulong, Vector3?> casterWeapon = null)
        : this(look != null ? _ => look() : (Func<InterruptSuccessLook, InterruptHitLightLook>)null,
            fx != null ? _ => fx() : (Func<InterruptSuccessLook, InterruptHitLightFx>)null,
            darkAdapt, sceneDim, whiteout, casterWeapon)
    {
    }

    /// <summary>버전별 데이터·월드 이펙트(194 후속 — V3 는 전용 SO·프리팹). 인자 = 이번 요청의 <see cref="InterruptHitLightRequest.Look"/>.</summary>
    public InterruptHitLightPlayer(Func<InterruptSuccessLook, InterruptHitLightLook> look, Func<InterruptSuccessLook, InterruptHitLightFx> fx,
        Func<ISuppressDarkAdaptOutput> darkAdapt = null, SceneLightDim sceneDim = null, IInterruptHitFlashOverlayOutput whiteout = null,
        Func<ulong, Vector3?> casterWeapon = null)
    {
        _look = look;
        _fx = fx;
        _darkAdapt = darkAdapt;
        _sceneDim = sceneDim;
        _whiteout = whiteout;
        _casterWeapon = casterWeapon;
    }

    /// <summary>마지막 연출의 월드 이펙트 위치 — 확인용.</summary>
    public Vector3 FxPoint { get; private set; }

    /// <summary>지금 화이트아웃을 그리고 있다 — 확인용.</summary>
    public bool WhiteoutShown => _whiteoutShown;

    public bool IsPlaying { get; private set; }
    /// <summary>마지막 요청 — 확인용.</summary>
    public InterruptHitLightRequest Request => _request;
    /// <summary>지금 블룸 Volume 가중치(꺼짐 = 0) — 확인용.</summary>
    public float BloomWeight => _bloomVolume != null && _bloomVolume.enabled ? _bloomVolume.weight : 0f;
    /// <summary>지금 색 보정(채도) Volume 가중치(꺼짐 = 0) — 확인용.</summary>
    public float GradeWeight => _gradeVolume != null && _gradeVolume.enabled ? _gradeVolume.weight : 0f;

    /// <summary>성공 순간 시작(<paramref name="now"/> = 실시간, <paramref name="frame"/> = 렌더 프레임). 돌던 연출은 새 것으로 바뀐다.</summary>
    public void Begin(in InterruptHitLightRequest request, float now, int frame = 0)
    {
        InterruptHitLightLook look = _look?.Invoke(request.Look);
        // 앞 연출의 암순응 방식이 다를 수 있다 — 넣어 둔 것을 먼저 걷는다(곧바로 새 값이 들어간다).
        ReleaseDarkAdapt();
        _request = request;
        _startTime = now;
        _whiteoutStartFrame = frame;
        _whiteoutFrames = look != null ? InterruptPresentationTierRules.WhiteoutFrames(request.Look, request.ViewerIsCaster, look.WhiteoutFrames) : 0;
        IsPlaying = true;

        // 무기 위치는 이 순간 한 번만 읽는다 — 이펙트는 월드에 남고 시전자를 따라가지 않는다.
        Vector3? weapon = request.Origin == InterruptHitLightOrigin.CasterWeapon ? _casterWeapon?.Invoke(request.CasterClientId) : null;
        FxPoint = InterruptHitLightRules.FxPoint(request.Origin, request.Point, weapon);
        InterruptHitLightFx fx = _fx?.Invoke(request.Look);
        // 버전이 바뀌어 다른 인스턴스를 틀면 앞 것은 지운다(같은 인스턴스는 Play 가 다시 튼다).
        if (_playedFx != null && _playedFx != fx)
            _playedFx.Stop();
        _playedFx = fx;
        if (fx != null)
            fx.Play(FxPoint, look);

        Tick(now, frame);
    }

    /// <summary>매 프레임 — <paramref name="now"/> = 실시간, <paramref name="frame"/> = 렌더 프레임(화이트아웃).</summary>
    public void Tick(float now, int frame = 0)
    {
        if (!IsPlaying)
            return;

        InterruptHitLightLook look = _look?.Invoke(_request.Look);
        float elapsed = now - _startTime;
        float bloom = look != null ? InterruptEffectLight.Pulse(look.BloomCurve, 1f, elapsed, look.BloomDuration) : 0f;
        _bloomVolume = ApplyVolume(_bloomVolume, "Bloom", look != null ? look.BloomProfile : null, bloom);
        _gradeVolume = ApplyVolume(_gradeVolume, "Grade", look != null ? look.GradeProfile : null, InterruptHitLightRules.GradeWeight(look, elapsed));
        ApplyDarkAdapt(look, elapsed);
        ApplyWhiteout(frame);

        if (InterruptHitLightRules.Finished(look, elapsed) && _whiteoutFrames == 0)
            Finish();
    }

    // V2 화이트아웃 — 시작 프레임부터 N 프레임 동안 ③ 그림, 그다음 프레임에 숨김.
    private void ApplyWhiteout(int frame)
    {
        if (_whiteoutFrames <= 0)
            return;

        int index = frame - _whiteoutStartFrame;
        if (index >= 0 && index < _whiteoutFrames && _whiteout != null)
        {
            _whiteout.Show(InterruptHitFlashStage.Whiteout, (float)index / _whiteoutFrames, _request.Point, Vector3.zero);
            _whiteoutShown = true;
            return;
        }

        HideWhiteout();
        _whiteoutFrames = 0;
    }

    private void HideWhiteout()
    {
        if (!_whiteoutShown)
            return;

        _whiteout?.Hide();
        _whiteoutShown = false;
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
            output.ShowDarkAdaptCut(darkness);
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
        if (_playedFx != null)
            _playedFx.Stop();
        Finish();
    }

    public void Dispose()
    {
        Stop();
        (_whiteout as IDisposable)?.Dispose();
        if (_root != null)
        {
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(_root);
            else
                UnityEngine.Object.DestroyImmediate(_root);
        }

        _root = null;
        _bloomVolume = null;
        _gradeVolume = null;
        _playedFx = null;
    }

    private void Finish()
    {
        IsPlaying = false;
        SetWeight(_bloomVolume, 0f);
        SetWeight(_gradeVolume, 0f);
        ReleaseDarkAdapt();
        HideWhiteout();
        _whiteoutFrames = 0;
    }

    // 블룸·색 보정 Volume — 프로필이 있고 가중치가 있으면 처음 한 번 만들고 가중치만 움직인다.
    private Volume ApplyVolume(Volume volume, string name, VolumeProfile profile, float weight)
    {
        if (profile == null || weight <= 0f)
        {
            SetWeight(volume, 0f);
            return volume;
        }

        if (volume == null)
        {
            EnsureRoot();
            var volumeObject = new GameObject(name);
            volumeObject.transform.SetParent(_root.transform, false);
            volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = InterruptHitFlashOverlay.VolumePriority;
        }

        // sharedProfile — 에셋을 복제·수정하지 않는다(가중치만).
        volume.sharedProfile = profile;
        SetWeight(volume, weight);
        return volume;
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

    /// <summary>진행기 길이 — 곡선 길이 중 가장 긴 것. 색 보정은 프로필이 있을 때만 센다(V1·V2 는 비어 있다).</summary>
    public static float Duration(InterruptHitLightLook look) =>
        look == null
            ? 0f
            : Mathf.Max(Mathf.Max(Mathf.Max(look.LightDuration, look.BloomDuration), Mathf.Max(look.DarkAdaptDuration, look.SceneDimDuration)),
                look.GradeProfile != null ? look.GradeDuration : 0f);

    /// <summary>색 보정(채도) Volume 가중치(194 후속 V3) — 프로필이 있으면 곡선대로 [0, 1], 없거나 끝이면 0.</summary>
    public static float GradeWeight(InterruptHitLightLook look, float elapsed) =>
        look != null && look.GradeProfile != null ? Mathf.Clamp01(InterruptEffectLight.Pulse(look.GradeCurve, 1f, elapsed, look.GradeDuration)) : 0f;

    /// <summary>암순응 가 최대 어둡힘 — 본인 화면 = self, 팀원 화면 = others(Q9).</summary>
    public static float DarkAdaptPeak(bool viewerIsCaster, float self, float others) =>
        Mathf.Clamp01(viewerIsCaster ? self : others);

    /// <summary>월드 이펙트 위치(194 후속) — 무기 선택이고 무기 위치가 있으면 무기, 아니면(대상 선택·무기 못 찾음) 대상 적중점.</summary>
    public static Vector3 FxPoint(InterruptHitLightOrigin origin, Vector3 targetPoint, Vector3? weaponPoint) =>
        origin == InterruptHitLightOrigin.CasterWeapon && weaponPoint.HasValue ? weaponPoint.Value : targetPoint;

    /// <summary>암순응 나 배율 — 1 − 낮춤 비율 × 곡선 값. [0, 1].</summary>
    public static float DimFactor(float depth, float envelope) =>
        Mathf.Clamp01(1f - Mathf.Clamp01(depth) * Mathf.Max(0f, envelope));
}
