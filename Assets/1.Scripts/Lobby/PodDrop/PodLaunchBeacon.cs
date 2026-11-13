using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 로비 포드 출격 호박색 회전 경광등(할 일 284) — 표시 전용, 각 피어 로컬(네트워크 없음).
/// <see cref="LobbyPodLaunch"/> 가 매 프레임 <see cref="Apply"/>(켬 여부·가속 단계)를 넘기고, 연출이 끝나거나 취소되면 <see cref="TurnOff"/> 한다.
/// <list type="bullet">
/// <item>켜짐: 회전 피벗(<see cref="rotor"/>)을 Y축으로 돌리고(단계별 한 바퀴 주기 <see cref="stagePeriods"/>), Spot 들을 켜고, 돔 이미시브를 <see cref="color"/> × <see cref="emissionIntensity"/> 로.</item>
/// <item>바닥 무늬(284 후속): 프리팹 <c>Aim</c>(벽 반대쪽으로 30° 기울임) → <c>Rotor</c> → 아래를 보는 Spot 1개 + 쿠키 <c>T_PodLaunchBeaconCookie</c>(원을 4등분해 마주 보는 두 칸만 밝음 — 충돌 시험 표식).
/// Rotor 의 Y 축 = Spot 빛 축이라, 돌면 바닥의 원은 제자리에서 무늬만 돈다. 벽 반대쪽 방향은 씬 인스턴스 루트의 Y 회전으로 정한다.
/// 쿠키는 URP 에셋의 Light Cookies 가 켜져 있어야 보인다(PC_RPAsset·Mobile_RPAsset).</item>
/// <item>꺼짐: Spot 끔, 돔 이미시브 = <see cref="color"/> × <see cref="offEmissionIntensity"/>(기본 0).</item>
/// <item>돔 색은 <see cref="MaterialPropertyBlock"/> 으로만 바꾼다(공유 머티리얼 수정 없음, 277 <see cref="PropEmissiveAnimator"/> 와 같은 방식).</item>
/// <item>Spot 의 세기·각도·거리는 프리팹 Light 칸이 원본이고, 색만 이 컴포넌트의 <see cref="color"/> 를 쓴다.</item>
/// </list>
/// 회전하는 오브젝트라 Static(Batching Static) 을 켜면 안 된다.
/// </summary>
[DisallowMultipleComponent]
public sealed class PodLaunchBeacon : MonoBehaviour
{
    [Tooltip("Y축으로 도는 피벗 — 아래에 Spot 들을 둔다.")]
    [SerializeField] private Transform rotor;

    [Tooltip("이미시브를 켜고 끄는 돔 렌더러(머티리얼에 _EMISSION 키워드가 켜져 있어야 한다).")]
    [SerializeField] private Renderer[] domeRenderers = Array.Empty<Renderer>();

    [Tooltip("켜짐 동안만 켜는 Spot(Realtime·그림자 끔 — 세기·각도·거리는 Light 칸).")]
    [SerializeField] private Light[] spots = Array.Empty<Light>();

    [Tooltip("경광등 색(Spot 색·돔 이미시브 색).")]
    [SerializeField] private Color color = new Color(1f, 0.55f, 0.08f, 1f);

    [Tooltip("켜짐 동안 돔 이미시브 세기(HDR 배율).")]
    [Min(0f)] [SerializeField] private float emissionIntensity = 10f;

    [Tooltip("꺼짐 동안 돔 이미시브 세기(0 = 꺼진 유리).")]
    [Min(0f)] [SerializeField] private float offEmissionIntensity = 0f;

    [Tooltip("한 바퀴 주기(초) — [0] 발사 전·1배속, [1] 가속 1단계, [2] 가속 2단계.")]
    [SerializeField] private float[] stagePeriods = { 1.2f, 0.8f, 0.5f };

    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    private MaterialPropertyBlock _mpb;
    private bool _initialized;
    private bool? _lit;      // 마지막으로 적용한 켬 상태(null = 아직)
    private float _yaw;

    /// <summary>지금 켜져 있나.</summary>
    public bool IsOn => _lit == true;

    /// <summary>지금 회전 각(도) — 테스트·디버그용.</summary>
    public float Yaw => _yaw;

    /// <summary>이 경광등의 Spot — 씬 암전(<see cref="PodLaunchLightBlackout"/>)이 끄지 않고 남기는 라이트.</summary>
    public IReadOnlyList<Light> Spots => spots;

    private void Awake()
    {
        Init();
        TurnOff();
    }

    private void OnValidate()
    {
        if (stagePeriods == null || stagePeriods.Length == 0)
            stagePeriods = new[] { 1.2f, 0.8f, 0.5f };
    }

    private void Init()
    {
        if (_initialized) return;
        _initialized = true;
        _mpb = new MaterialPropertyBlock();
        foreach (Light spot in spots)
            if (spot != null) spot.color = color;
    }

    /// <summary>[전 피어] 매 프레임 — 켬 여부와 발사 클립 가속 단계(0·1·2). 켜져 있으면 회전을 이번 프레임만큼 진행한다.</summary>
    public void Apply(bool on, int stage)
    {
        Init();
        SetLit(on);
        if (!on) return;

        _yaw = PodLaunchBeaconRules.AdvanceYaw(_yaw, Time.unscaledDeltaTime, PodLaunchBeaconRules.PeriodFor(stage, stagePeriods));
        if (rotor != null) rotor.localRotation = Quaternion.Euler(0f, _yaw, 0f);
    }

    /// <summary>[전 피어] 끈다(멱등). 회전 각은 처음으로 되돌린다 — 다음 회차도 같은 자세에서 시작.</summary>
    public void TurnOff()
    {
        Init();
        SetLit(false);
        _yaw = 0f;
        if (rotor != null) rotor.localRotation = Quaternion.identity;
    }

    private void SetLit(bool on)
    {
        if (_lit == on) return;
        _lit = on;

        foreach (Light spot in spots)
            if (spot != null) spot.enabled = on;

        Color emission = color * (on ? emissionIntensity : offEmissionIntensity);
        foreach (Renderer r in domeRenderers)
        {
            if (r == null) continue;
            r.GetPropertyBlock(_mpb);
            _mpb.SetColor(EmissionColorId, emission);
            r.SetPropertyBlock(_mpb);
        }
    }
}

/// <summary>경광등 켜고 끄는 구간·회전(할 일 284) — 순수 함수, EditMode 테스트 대상.</summary>
public static class PodLaunchBeaconRules
{
    /// <summary>최소 한 바퀴 주기(초) — 0·음수·NaN 칸을 막는다.</summary>
    public const float MinPeriod = 0.05f;

    /// <summary>
    /// 켬 구간 = 포드 연출 시작(경과 0 — 출격 확정, 시작 시각 복제) ~ 끝 페이드가 다 검어진 순간(<see cref="PodDropLobbyTimeline.EndBlackAt"/>).
    /// 다 검어질 시각을 아직 모르면(+∞ — 하강 시작 전) 켜진 채다.
    /// </summary>
    public static bool IsOn(double elapsed, double endBlackAt)
    {
        if (double.IsNaN(elapsed) || double.IsNaN(endBlackAt)) return false;
        return elapsed >= 0d && elapsed < endBlackAt;
    }

    /// <summary>가속 단계 = 복제된 가속 1·2단계 경과 시각(+∞ = 아직)과 이 피어가 이미 들어간 단계(자기 클립 이벤트) 중 큰 쪽.</summary>
    public static int Stage(double elapsed, double fastAt, double fast2At, int localStage = 0)
    {
        int replicated = elapsed >= fast2At ? 2 : elapsed >= fastAt ? 1 : 0;
        return Math.Max(Math.Max(0, Math.Min(2, localStage)), replicated);
    }

    /// <summary>단계별 한 바퀴 주기(초). 칸이 모자라면 마지막 칸, 비면 1초. <see cref="MinPeriod"/> 보다 짧지 않다.</summary>
    public static float PeriodFor(int stage, float[] periods)
    {
        if (periods == null || periods.Length == 0) return 1f;
        float p = periods[Mathf.Clamp(stage, 0, periods.Length - 1)];
        return float.IsNaN(p) || p < MinPeriod ? MinPeriod : p;
    }

    /// <summary>회전 각 진행(도, 0~360).</summary>
    public static float AdvanceYaw(float yaw, float deltaSeconds, float period)
    {
        if (deltaSeconds <= 0f || float.IsNaN(deltaSeconds)) return Mathf.Repeat(yaw, 360f);
        return Mathf.Repeat(yaw + 360f * deltaSeconds / Mathf.Max(MinPeriod, period), 360f);
    }
}

/// <summary>
/// 경광등 켜진 동안 씬 라이트 끄기(할 일 284 후속) — 켬 전이 순간 1회, 넘겨받은 라이트 중 켜져 있던 것(남길 것·태양 제외)을 끄고 기억했다가,
/// 끔 전이에 그 라이트만 다시 켠다(원래 꺼져 있던 라이트는 그대로). 태양(Directional)·환경광은 <see cref="SceneLightDim"/> 배율로 따로 다룬다 —
/// 23호 차징·간파 암순응과 같은 창구라 원래 값을 따로 저장하지 않아 복구가 꼬이지 않는다. 이미시브 머티리얼은 라이트가 아니라 건드리지 않는다.
/// 각 피어 로컬(네트워크 없음).
/// </summary>
public sealed class PodLaunchLightBlackout
{
    private readonly List<Light> _turnedOff = new List<Light>();

    /// <summary>지금 끄고 있나.</summary>
    public bool Active { get; private set; }

    /// <summary>이번 암전으로 끈 라이트 수(태양 제외).</summary>
    public int TurnedOffCount => _turnedOff.Count;

    /// <summary>끈 라이트 목록(진단용).</summary>
    public IReadOnlyList<Light> TurnedOff => _turnedOff;

    /// <summary>
    /// <paramref name="sceneLights"/> 중 켜져 있는 것을 끈다 — <paramref name="keep"/>(경광등 Spot)·<paramref name="sun"/>(<see cref="SceneLightDim"/> 몫)는 건너뛴다.
    /// 이미 켜짐 상태면 아무것도 안 한다(멱등).
    /// </summary>
    public void Begin(IEnumerable<Light> sceneLights, ICollection<Light> keep, Light sun)
    {
        if (Active) return;
        Active = true;
        _turnedOff.Clear();
        if (sceneLights == null) return;

        foreach (Light light in sceneLights)
        {
            if (light == null || !light.enabled || light == sun) continue;
            if (keep != null && keep.Contains(light)) continue;
            light.enabled = false;
            _turnedOff.Add(light);
        }
    }

    /// <summary>끈 라이트만 다시 켠다(사라진 라이트는 건너뜀). 멱등.</summary>
    public void End()
    {
        if (!Active) return;
        Active = false;
        foreach (Light light in _turnedOff)
            if (light != null) light.enabled = true;
        _turnedOff.Clear();
    }
}
