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
    /// <summary>월드 이펙트 발생 위치 — 시전자 오른손 무기 / 대상 / 틈 수직(기본).</summary>
    public InterruptHitLightOrigin Origin;
    /// <summary>이 화면이 발동자 본인인가.</summary>
    public bool ViewerIsCaster;
    public InterruptSuccessLook Look;
    public InterruptDarkAdaptMode DarkAdapt;
    /// <summary>성공 단계(할 일 219) — V3 단계별 SO·프리팹(대쉬·제압)과 제압 화이트아웃을 고른다. 기본값(실패 = 0)은 일반 묶음.</summary>
    public InterruptPresentationTier Tier;
    /// <summary>디렉터 스위치 '제압 화이트아웃'(할 일 219) — 꺼지면 V3 제압 단계 화이트아웃 없음(SO 프레임 수와 무관). V2 화이트아웃과는 무관.</summary>
    public bool SuppressWhiteout;
}

/// <summary>
/// 적중 라이트 요청 하나에 쓸 수치 SO·월드 이펙트(할 일 219 — 버전 + 단계). 디렉터가 구현한다(V1·V2 공용 / V3 일반·대쉬·제압 묶음, 빈 칸 = 일반).
/// </summary>
public interface IInterruptHitLightAssets
{
    InterruptHitLightLook LookFor(in InterruptHitLightRequest request);
    InterruptHitLightFx FxFor(in InterruptHitLightRequest request);

    /// <summary>색 보정 Volume 프로필(219 후속 11-05 — 단계별). V3 대쉬·제압 SO 칸이 비면 일반 V3 프로필, V1·V2 는 없음(null).</summary>
    VolumeProfile GradeFor(in InterruptHitLightRequest request);
}

/// <summary>
/// 월드 이펙트를 놓는 곳(194 후속) — 위치 + (틈 수직이면) 시전자→대상 축·그 수직선·카메라 시선. 성공 순간 한 번 계산해 고정한다.
/// 방향이 없으면(무기·대상 모드, 대체 경로) 이펙트는 V1·V2 처음 모습 그대로다.
/// </summary>
public readonly struct InterruptHitLightPlacement
{
    public readonly Vector3 Position;
    /// <summary>축·수직선이 있다(틈 수직이고 시전자·대상 위치가 겹치지 않음).</summary>
    public readonly bool HasDirection;
    /// <summary>시전자 → 대상(바닥 평면 단위 벡터).</summary>
    public readonly Vector3 Axis;
    /// <summary>축에 수직인 바닥 선(단위 벡터 — 부호는 의미 없다, 양쪽으로 뻗는다).</summary>
    public readonly Vector3 Perpendicular;
    /// <summary>카메라 시선(단위 벡터) — 납작한 이펙트를 이쪽으로 기울인다.</summary>
    public readonly Vector3 ViewForward;

    private InterruptHitLightPlacement(Vector3 position, bool hasDirection, Vector3 axis, Vector3 perpendicular, Vector3 viewForward)
    {
        Position = position;
        HasDirection = hasDirection;
        Axis = axis;
        Perpendicular = perpendicular;
        ViewForward = viewForward;
    }

    /// <summary>위치만(방향 없음).</summary>
    public static InterruptHitLightPlacement At(Vector3 position) =>
        new InterruptHitLightPlacement(position, false, Vector3.zero, Vector3.zero, Vector3.zero);

    /// <summary>위치 + 방향 — <paramref name="axis"/>·<paramref name="viewForward"/> 는 단위 벡터, 축은 바닥 평면.</summary>
    public static InterruptHitLightPlacement Directed(Vector3 position, Vector3 axis, Vector3 viewForward) =>
        new InterruptHitLightPlacement(position, true, axis, InterruptHitLightRules.FlatPerpendicular(axis), viewForward);
}

/// <summary>
/// 틈 수직 위치 계산에 쓰는 이 피어 화면의 정보(194 후속) — 디렉터가 구현한다. 모든 피어가 자기 화면의 시전자·대상으로 같은 계산을 한다(새 RPC 없음).
/// </summary>
public interface IInterruptHitLightScene
{
    /// <summary>시전자 몸 중심(무기 앵커가 없을 때 대체). 시전자를 못 찾으면 false.</summary>
    bool TryGetCasterCenter(ulong casterClientId, out Vector3 center);
    /// <summary><paramref name="targetPoint"/> 근처 간파 대상 몸 표면 중 <paramref name="from"/> 에 가장 가까운 점(시전자 쪽 표면). 못 구하면 false.</summary>
    bool TryGetTargetSurface(Vector3 targetPoint, Vector3 from, out Vector3 surface);
    /// <summary>이 화면 카메라 시선. 카메라가 없으면 false.</summary>
    bool TryGetViewForward(out Vector3 forward);
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
    private readonly IInterruptHitLightAssets _assets;
    private readonly Func<ISuppressDarkAdaptOutput> _darkAdapt;
    private readonly SceneLightDim _sceneDim;
    private readonly IInterruptHitFlashOverlayOutput _whiteout;
    private readonly Func<ulong, Vector3?> _casterWeapon;
    private readonly IInterruptHitLightScene _scene;

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
    /// <param name="casterWeapon">시전자 → 지금 오른손 무기 위치(없으면 null). null 이면 월드 이펙트는 늘 대상 적중점(틈 수직이면 시전자 중심과의 틈).</param>
    /// <param name="scene">틈 수직 계산용 시전자 중심·대상 표면·카메라 시선(194 후속). null 이면 틈 수직은 무기와 대상 적중점 사이, 시선은 수직 아래.</param>
    public InterruptHitLightPlayer(Func<InterruptHitLightLook> look, Func<InterruptHitLightFx> fx,
        Func<ISuppressDarkAdaptOutput> darkAdapt = null, SceneLightDim sceneDim = null, IInterruptHitFlashOverlayOutput whiteout = null,
        Func<ulong, Vector3?> casterWeapon = null, IInterruptHitLightScene scene = null)
        : this(look != null ? _ => look() : (Func<InterruptSuccessLook, InterruptHitLightLook>)null,
            fx != null ? _ => fx() : (Func<InterruptSuccessLook, InterruptHitLightFx>)null,
            darkAdapt, sceneDim, whiteout, casterWeapon, scene)
    {
    }

    /// <summary>버전별 데이터·월드 이펙트(194 후속 — V3 는 전용 SO·프리팹). 인자 = 이번 요청의 <see cref="InterruptHitLightRequest.Look"/>.</summary>
    public InterruptHitLightPlayer(Func<InterruptSuccessLook, InterruptHitLightLook> look, Func<InterruptSuccessLook, InterruptHitLightFx> fx,
        Func<ISuppressDarkAdaptOutput> darkAdapt = null, SceneLightDim sceneDim = null, IInterruptHitFlashOverlayOutput whiteout = null,
        Func<ulong, Vector3?> casterWeapon = null, IInterruptHitLightScene scene = null)
        : this(new LookFuncAssets(look, fx), darkAdapt, sceneDim, whiteout, casterWeapon, scene)
    {
    }

    /// <summary>요청(버전 + 단계)별 데이터·월드 이펙트(할 일 219 — V3 단계별 SO·프리팹). 디렉터가 쓴다.</summary>
    public InterruptHitLightPlayer(IInterruptHitLightAssets assets,
        Func<ISuppressDarkAdaptOutput> darkAdapt = null, SceneLightDim sceneDim = null, IInterruptHitFlashOverlayOutput whiteout = null,
        Func<ulong, Vector3?> casterWeapon = null, IInterruptHitLightScene scene = null)
    {
        _assets = assets;
        _darkAdapt = darkAdapt;
        _sceneDim = sceneDim;
        _whiteout = whiteout;
        _casterWeapon = casterWeapon;
        _scene = scene;
    }

    /// <summary>마지막 연출의 월드 이펙트 위치 — 확인용.</summary>
    public Vector3 FxPoint => Placement.Position;
    /// <summary>마지막 연출의 월드 이펙트 위치·방향 — 확인용.</summary>
    public InterruptHitLightPlacement Placement { get; private set; }

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
        InterruptHitLightLook look = _assets?.LookFor(request);
        // 앞 연출의 암순응 방식이 다를 수 있다 — 넣어 둔 것을 먼저 걷는다(곧바로 새 값이 들어간다).
        ReleaseDarkAdapt();
        _request = request;
        _startTime = now;
        _whiteoutStartFrame = frame;
        // V2 = 그 SO 프레임 수, V3 = 제압 단계 ∧ 스위치 켬일 때만(할 일 219).
        _whiteoutFrames = look != null
            ? InterruptPresentationTierRules.WhiteoutFrames(request.Look, request.Tier, request.ViewerIsCaster, look.WhiteoutFrames, request.SuppressWhiteout)
            : 0;
        IsPlaying = true;

        // 위치·방향은 이 순간 한 번만 계산한다 — 이펙트는 월드에 남고 시전자를 따라가지 않는다.
        Placement = ResolvePlacement(request);
        InterruptHitLightFx fx = _assets?.FxFor(request);
        // 버전이 바뀌어 다른 인스턴스를 틀면 앞 것은 지운다(같은 인스턴스는 Play 가 다시 튼다).
        if (_playedFx != null && _playedFx != fx)
            _playedFx.Stop();
        _playedFx = fx;
        if (fx != null)
            fx.Play(Placement, look);

        Tick(now, frame);
    }

    // 이 피어 화면의 시전자·대상으로 위치·방향을 구한다. 대상 모드는 무기를 찾지 않고, 틈 수직만 시전자 중심·대상 표면·시선을 묻는다.
    private InterruptHitLightPlacement ResolvePlacement(in InterruptHitLightRequest request)
    {
        Vector3? weapon = request.Origin != InterruptHitLightOrigin.Target ? _casterWeapon?.Invoke(request.CasterClientId) : null;
        if (request.Origin != InterruptHitLightOrigin.Gap)
            return InterruptHitLightRules.Place(request.Origin, request.Point, weapon, null, null, Vector3.down);

        Vector3? center = null;
        if (!weapon.HasValue && _scene != null && _scene.TryGetCasterCenter(request.CasterClientId, out Vector3 c))
            center = c;
        Vector3? casterSide = weapon ?? center;
        Vector3? surface = null;
        if (casterSide.HasValue && _scene != null && _scene.TryGetTargetSurface(request.Point, casterSide.Value, out Vector3 s))
            surface = s;
        Vector3 view = _scene != null && _scene.TryGetViewForward(out Vector3 f) ? f : Vector3.down;
        return InterruptHitLightRules.Place(request.Origin, request.Point, weapon, center, surface, view);
    }

    /// <summary>매 프레임 — <paramref name="now"/> = 실시간, <paramref name="frame"/> = 렌더 프레임(화이트아웃).</summary>
    public void Tick(float now, int frame = 0)
    {
        if (!IsPlaying)
            return;

        InterruptHitLightLook look = _assets?.LookFor(_request);
        float elapsed = now - _startTime;
        float bloom = look != null ? InterruptEffectLight.Pulse(look.BloomCurve, 1f, elapsed, look.BloomDuration) : 0f;
        _bloomVolume = ApplyVolume(_bloomVolume, "Bloom", look != null ? look.BloomProfile : null, bloom);
        VolumeProfile grade = look != null ? _assets.GradeFor(_request) : null;
        _gradeVolume = ApplyVolume(_gradeVolume, "Grade", grade, InterruptHitLightRules.GradeWeight(look, grade, elapsed));
        ApplyDarkAdapt(look, elapsed);
        ApplyWhiteout(frame);

        if (InterruptHitLightRules.Finished(look, grade, elapsed) && _whiteoutFrames == 0)
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

    // 버전만으로 고르는 옛 생성자(테스트·194)용 — 단계는 보지 않는다.
    private sealed class LookFuncAssets : IInterruptHitLightAssets
    {
        private readonly Func<InterruptSuccessLook, InterruptHitLightLook> _look;
        private readonly Func<InterruptSuccessLook, InterruptHitLightFx> _fx;

        public LookFuncAssets(Func<InterruptSuccessLook, InterruptHitLightLook> look, Func<InterruptSuccessLook, InterruptHitLightFx> fx)
        {
            _look = look;
            _fx = fx;
        }

        public InterruptHitLightLook LookFor(in InterruptHitLightRequest request) => _look?.Invoke(request.Look);
        public InterruptHitLightFx FxFor(in InterruptHitLightRequest request) => _fx?.Invoke(request.Look);
        public VolumeProfile GradeFor(in InterruptHitLightRequest request)
        {
            InterruptHitLightLook look = LookFor(request);
            return look != null ? look.GradeProfile : null;
        }
    }
}

/// <summary>적중 라이트 연출 순수 규칙(할 일 194) — EditMode 테스트 대상.</summary>
public static class InterruptHitLightRules
{
    /// <summary>모든 곡선(라이트·블룸·암순응)이 끝났는가. 데이터가 없으면 끝.</summary>
    public static bool Finished(InterruptHitLightLook look, float elapsed) => Finished(look, look != null ? look.GradeProfile : null, elapsed);

    /// <summary><see cref="Finished(InterruptHitLightLook, float)"/> — 색 보정 프로필을 따로 받는다(219 후속 단계별 프로필·빈 칸 대체).</summary>
    public static bool Finished(InterruptHitLightLook look, VolumeProfile grade, float elapsed) =>
        look == null || elapsed >= Duration(look, grade);

    /// <summary>진행기 길이 — 곡선 길이 중 가장 긴 것. 색 보정은 프로필이 있을 때만 센다(V1·V2 는 비어 있다).</summary>
    public static float Duration(InterruptHitLightLook look) => Duration(look, look != null ? look.GradeProfile : null);

    /// <summary><see cref="Duration(InterruptHitLightLook)"/> — 색 보정 프로필을 따로 받는다.</summary>
    public static float Duration(InterruptHitLightLook look, VolumeProfile grade) =>
        look == null
            ? 0f
            : Mathf.Max(Mathf.Max(Mathf.Max(look.LightDuration, look.BloomDuration), Mathf.Max(look.DarkAdaptDuration, look.SceneDimDuration)),
                grade != null ? look.GradeDuration : 0f);

    /// <summary>색 보정(채도) Volume 가중치(194 후속 V3) — 프로필이 있으면 곡선대로 [0, 1], 없거나 끝이면 0.</summary>
    public static float GradeWeight(InterruptHitLightLook look, float elapsed) => GradeWeight(look, look != null ? look.GradeProfile : null, elapsed);

    /// <summary><see cref="GradeWeight(InterruptHitLightLook, float)"/> — 색 보정 프로필을 따로 받는다.</summary>
    public static float GradeWeight(InterruptHitLightLook look, VolumeProfile grade, float elapsed) =>
        look != null && grade != null ? Mathf.Clamp01(InterruptEffectLight.Pulse(look.GradeCurve, 1f, elapsed, look.GradeDuration)) : 0f;

    /// <summary>
    /// 단계 색 보정 프로필(219 후속 11-05) — 단계 SO 에 있으면 그것, 비면 일반 V3 SO 의 것. 단계 SO 자체가 없으면 없음.
    /// </summary>
    public static VolumeProfile SelectGrade(InterruptHitLightLook tierLook, InterruptHitLightLook normalLook) =>
        tierLook == null ? null
        : tierLook.GradeProfile != null ? tierLook.GradeProfile
        : normalLook != null ? normalLook.GradeProfile : null;

    /// <summary>암순응 가 최대 어둡힘 — 본인 화면 = self, 팀원 화면 = others(Q9).</summary>
    public static float DarkAdaptPeak(bool viewerIsCaster, float self, float others) =>
        Mathf.Clamp01(viewerIsCaster ? self : others);

    /// <summary>월드 이펙트 위치(194 후속) — 무기 선택이고 무기 위치가 있으면 무기, 아니면(대상 선택·무기 못 찾음) 대상 적중점.</summary>
    public static Vector3 FxPoint(InterruptHitLightOrigin origin, Vector3 targetPoint, Vector3? weaponPoint) =>
        origin == InterruptHitLightOrigin.CasterWeapon && weaponPoint.HasValue ? weaponPoint.Value : targetPoint;

    private const float MinSqr = 1e-6f;

    /// <summary>
    /// 월드 이펙트 위치·방향(194 후속). 무기·대상 모드 = <see cref="FxPoint"/> 위치만.
    /// 틈 수직 = (무기, 없으면 시전자 중심)과 (대상 표면, 없으면 대상 적중점)의 중간점 + 축(시전자 쪽 → 대상 적중점, 바닥 평면).
    /// 시전자를 못 찾으면 대상 적중점·방향 없음, 시전자·대상이 겹쳐 축이 없으면 중간점·방향 없음.
    /// </summary>
    public static InterruptHitLightPlacement Place(InterruptHitLightOrigin origin, Vector3 targetPoint, Vector3? weaponPoint,
        Vector3? casterCenter, Vector3? targetSurface, Vector3 viewForward)
    {
        if (origin != InterruptHitLightOrigin.Gap)
            return InterruptHitLightPlacement.At(FxPoint(origin, targetPoint, weaponPoint));

        Vector3? casterSide = weaponPoint ?? casterCenter;
        if (!casterSide.HasValue)
            return InterruptHitLightPlacement.At(targetPoint);

        Vector3 position = GapPoint(casterSide.Value, targetSurface ?? targetPoint);
        Vector3 axis = Flat(targetPoint - casterSide.Value);
        if (axis.sqrMagnitude < MinSqr)
            return InterruptHitLightPlacement.At(position);

        Vector3 view = viewForward.sqrMagnitude < MinSqr ? Vector3.down : viewForward.normalized;
        return InterruptHitLightPlacement.Directed(position, axis.normalized, view);
    }

    /// <summary>틈 — 시전자 쪽 점과 대상 쪽 점의 중간.</summary>
    public static Vector3 GapPoint(Vector3 casterSide, Vector3 targetSide) => (casterSide + targetSide) * 0.5f;

    /// <summary>바닥 평면의 축에 수직인 바닥 선(단위 벡터). 축이 없으면 0.</summary>
    public static Vector3 FlatPerpendicular(Vector3 axis)
    {
        Vector3 perpendicular = Vector3.Cross(Vector3.up, Flat(axis));
        return perpendicular.sqrMagnitude < MinSqr ? Vector3.zero : perpendicular.normalized;
    }

    /// <summary>
    /// 납작한 이펙트(빛줄기·가로 빛줄) 회전 — 로컬 Z = 카메라 시선(면이 화면을 향함), 로컬 X = <paramref name="line"/> 을 시선에 수직인 면에 투영한 방향.
    /// 바닥 선을 카메라 쪽으로 기울인 것이라 화면에서 늘 그 선 각도로 보인다(탑다운에서 납작해지지 않는다). 선이 시선과 평행하면 화면 가로.
    /// </summary>
    public static Quaternion ViewTilted(Vector3 line, Vector3 viewForward)
    {
        Vector3 forward = viewForward.sqrMagnitude < MinSqr ? Vector3.down : viewForward.normalized;
        Vector3 x = Vector3.ProjectOnPlane(line, forward);
        if (x.sqrMagnitude < MinSqr)
        {
            x = Vector3.ProjectOnPlane(Vector3.right, forward);
            if (x.sqrMagnitude < MinSqr)
                x = Vector3.ProjectOnPlane(Vector3.forward, forward);
        }

        x.Normalize();
        return Quaternion.LookRotation(forward, Vector3.Cross(forward, x));
    }

    /// <summary>
    /// 틈 수직 불꽃 한 알의 방향 — 수직선 한쪽(<paramref name="positiveSide"/>)을 중심으로 반각 <paramref name="spreadDegrees"/> 원뿔 안.
    /// <paramref name="tilt01"/>·<paramref name="twist01"/> = [0, 1] 난수(기울기 비율·원뿔 둘레 각).
    /// </summary>
    public static Vector3 SparkDirection(Vector3 perpendicular, bool positiveSide, float spreadDegrees, float tilt01, float twist01)
    {
        Vector3 side = positiveSide ? perpendicular : -perpendicular;
        Vector3 tilted = Quaternion.AngleAxis(Mathf.Clamp01(tilt01) * Mathf.Clamp(spreadDegrees, 0f, 90f), Vector3.up) * side;
        return Quaternion.AngleAxis(Mathf.Clamp01(twist01) * 360f, side) * tilted;
    }

    /// <summary>화면 빛줄기를 놓는 깊이 — 카메라 근평면 바로 앞(이만큼 더). 깊이 무시 재질이라 값은 보이는 모습에 영향 없다.</summary>
    public const float ScreenRayDepthPadding = 0.05f;

    /// <summary>
    /// 화면(렌즈) 빛줄기 자세(194 후속) — <paramref name="origin"/>(월드 적중점)을 <paramref name="camera"/> 로 투영한 화면 점에, 카메라 근평면 바로 앞·카메라 정면으로 놓는다.
    /// 로컬 X = <paramref name="line"/> 을 화면에 투영한 방향(없거나 시선과 평행하면 화면 가로). <paramref name="worldPerUnit"/> = 메시 1 단위가 화면 높이 × <paramref name="screenSize"/> 가 되는 월드 배율
    /// — 그 깊이의 화면 높이(원근 = 2·깊이·tan(FOV/2), 직교 = 2·orthographicSize)로 정하므로 카메라 거리·줌과 무관하게 같은 화면 크기다.
    /// 원점이 카메라 뒤·근평면 안·화면 밖이면 false(그리지 않는다).
    /// </summary>
    public static bool ScreenRayPose(Camera camera, Vector3 origin, Vector3 line, float screenSize,
        out Vector3 position, out Quaternion rotation, out float worldPerUnit)
    {
        position = default;
        rotation = Quaternion.identity;
        worldPerUnit = 0f;
        if (camera == null)
            return false;

        Vector3 viewport = camera.WorldToViewportPoint(origin);
        if (viewport.z <= camera.nearClipPlane || viewport.x < 0f || viewport.x > 1f || viewport.y < 0f || viewport.y > 1f)
            return false;

        float depth = camera.nearClipPlane + ScreenRayDepthPadding;
        position = camera.ViewportToWorldPoint(new Vector3(viewport.x, viewport.y, depth));
        float screenHeight = camera.orthographic
            ? 2f * camera.orthographicSize
            : 2f * depth * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        worldPerUnit = Mathf.Max(0f, screenSize) * screenHeight;

        float angle = 0f;
        if (line.sqrMagnitude >= MinSqr)
        {
            // 원점 근처 짧은 선분을 화면으로 투영한 방향(픽셀 — 가로세로 비가 반영된다).
            Vector3 a = camera.WorldToScreenPoint(origin);
            Vector3 b = camera.WorldToScreenPoint(origin + line.normalized * (0.1f * viewport.z));
            Vector2 d = new Vector2(b.x - a.x, b.y - a.y);
            if (d.sqrMagnitude >= MinSqr)
                angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
        }

        rotation = camera.transform.rotation * Quaternion.AngleAxis(angle, Vector3.forward);
        return true;
    }

    private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

    /// <summary>암순응 나 배율 — 1 − 낮춤 비율 × 곡선 값. [0, 1].</summary>
    public static float DimFactor(float depth, float envelope) =>
        Mathf.Clamp01(1f - Mathf.Clamp01(depth) * Mathf.Max(0f, envelope));
}
