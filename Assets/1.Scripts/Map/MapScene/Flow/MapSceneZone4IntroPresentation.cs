using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 튜토리얼 4구역 진입 전 소개 연출의 <b>로컬 표시</b>(PLAN-zone4-intro-cinematic) — 모든 피어가
/// 디렉터 복제 상태(<see cref="MapSceneDirector.Zone4Intro"/>: 단계·회차·시작 ServerTime·복귀 시각) + 연출 데이터(<see cref="MapSceneZone4IntroData"/>)만으로
/// 같은 순간 같은 샷·같은 대사 조각을 그린다. 늦게 붙은 클라도 지금 시각의 샷부터 본다. 판정·진행은 서버(<see cref="MapSceneZone4IntroFlow"/>).
///
/// · 카메라: 런타임 vcam 하나 — 게임 카메라 → 패널 A(블렌드) → B → C → D → 다리·중앙 섬(샷 사이는 직접 보간) → 복귀 블렌드.
///   패널 순서 = 화면 왼쪽 아래 패널부터 시계 방향(<see cref="CinematicFraming.ClockwiseFromBottomLeft"/> — 화면 방향만으로 정해 모든 피어가 같다).
/// · 레터박스·비네팅·스킵 위젯·HUD 끄기 = 보스 등장 연출과 같은 <see cref="CinematicOverlay"/>. 목표 패널·강조만 숨기고 자막은 남긴다(D6).
/// · 무전 = <see cref="CinematicRadioCaption"/> 한 줄씩(<see cref="CinematicLineSplitter"/>). 다음 대사가 시작되면 앞 대사의 남은 조각은 버린다.
///   복귀가 시작되면 소개 대사는 끊고, 복귀가 끝난 뒤 복귀 무전("어서 움직여.")을 낸다(플레이와 병행).
/// 붙는 곳: 디렉터 OnNetworkSpawn(모든 피어, 씬 무수정).
/// </summary>
[DisallowMultipleComponent]
public sealed class MapSceneZone4IntroPresentation : MonoBehaviour
{
    private const float CameraEnterBlendLate = 0.35f;   // 늦게 붙은 클라 · 복귀 블렌드 값이 없을 때
    private const float ReturnLineGiveUpSeconds = 30f;  // 복귀 무전은 이보다 길 일이 없다

    private MapSceneDirector _director;
    private MapSceneZone4IntroData _data;
    private CinematicOverlay _overlay;
    private CinematicRadioCaption _caption;

    // 샷 구도 — 회차마다 한 번 계산해 고정(연출 중 화면 기준이 흔들리지 않게).
    private int _posesSequence = -1;
    private readonly List<Vector3> _shotPositions = new List<Vector3>();
    private readonly List<Quaternion> _shotRotations = new List<Quaternion>();
    private readonly List<float> _shotFovs = new List<float>();
    private readonly List<Vector3> _shotFocus = new List<Vector3>();   // 샷이 바라보는 점 — 디밍 기준점
    private readonly List<int> _shotPanel = new List<int>();          // 패널 샷이 비추는 게이트 패널 번호(다리 샷 = -1) — 외곽선
    private bool _warnedNoGate;

    // 외곽선(경석 10-27 — 패널 샷마다 그 패널, 다리 샷에 다리 조각). 근접 하이라이트와 같은 머티리얼·슬롯.
    private ZoneBridgeGate _gate;
    private int _outlinedPanel = -1;
    private bool _outlinedBridge;
    private Vector3 _clipCenter;
    private float _clipRadius;
    private static readonly int ClipCenterId = Shader.PropertyToID("_CinematicClipCenter");
    private static readonly int ClipRadiusId = Shader.PropertyToID("_CinematicClipRadius");
    private static readonly int ClipEdgeId = Shader.PropertyToID("_CinematicClipEdge");

    // 대사 조각 캐시(문구가 바뀌면 다시 나눈다 — 언어 전환).
    private readonly Dictionary<string, (string source, List<string> chunks)> _chunks = new Dictionary<string, (string, List<string>)>();
    private readonly HashSet<string> _warnedIds = new HashSet<string>();
    private bool _suppressingObjective;

    private void Awake()
    {
        _director = GetComponent<MapSceneDirector>();
    }

    private void OnDestroy()
    {
        SetOutlines(-1, false);
        SetObjectiveSuppressed(false);
        _overlay?.Dispose();
        _caption?.Dispose();
    }

    private void Update()
    {
        MapSceneDirector.Zone4IntroState state = _director != null && _director.IsSpawned
            ? _director.Zone4Intro
            : MapSceneDirector.Zone4IntroState.Empty;

        if (state.Phase == MapSceneDirector.Zone4IntroPhase.None)
        {
            if (_overlay != null) Idle();
            return;
        }

        _data ??= MapSceneZone4IntroData.Load();
        _overlay ??= new CinematicOverlay("Zone4Intro", _data.SkipButtonSprite);
        _caption ??= new CinematicRadioCaption("Zone4Intro");

        NetworkManager nm = NetworkManager.Singleton;
        double now = nm != null ? nm.ServerTime.Time : 0d;
        float t = state.Elapsed(now);
        bool running = state.Phase == MapSceneDirector.Zone4IntroPhase.Running;
        bool returning = running && state.IsReturning(now);
        bool wantCinematic = running && !returning;

        // ── 카메라 ──
        if (state.Sequence != _posesSequence) BuildPoses(state.Sequence);
        bool hasPoses = _shotPositions.Count > 0;
        if (wantCinematic && hasPoses && !_overlay.IsCameraActive)
        {
            Sample(state, t, out Vector3 p, out Quaternion r, out float fov, out _);
            float first = _data.Shots != null && _data.Shots.Length > 0 ? _data.Shots[0].moveSeconds : 0f;
            float blend = t < first ? Mathf.Max(0f, first - t) : CameraEnterBlendLate;   // 처음이면 남은 첫 블렌드, 늦게 붙었으면 짧게
            _overlay.EnterCamera(p, r, fov, blend);
        }
        else if (!wantCinematic && _overlay.IsCameraActive)
        {
            _overlay.ExitCamera(running && state.ReturnSeconds > 0f ? state.ReturnSeconds : CameraEnterBlendLate);
        }
        if (_overlay.IsCameraActive)
        {
            Sample(state, t, out Vector3 p, out Quaternion r, out float fov, out _);
            _overlay.PlaceCamera(p, r, fov);
        }

        // ── 디밍 기준점 — 연출 동안 카메라가 보는 점(멀리 비춰도 어둡게 깔리지 않게, 경석 10-27). 복귀 블렌드 동안 플레이어 쪽으로 같이 옮긴다. ──
        _overlay.SetDimFocus(running && hasPoses ? DimFocus(state, t) : (Vector3?)null);
        _overlay.SetDimSuppress(running ? DimSuppress(state, t) : 0f);   // 층이 다른 곳(2층 발판·다리)도 보이게 디밍 세기 자체를 줄인다

        // ── 외곽선 — 카메라가 그 샷에 머무는(옮겨 가는 후반 포함) 동안 그 패널 / 다리 조각. 복귀가 시작되면 끈다. ──
        if (wantCinematic && hasPoses) TickOutlines(state, t);
        else SetOutlines(-1, false);

        // ── HUD — 연출 내내(복귀 블렌드 포함) 끄고, 복귀가 끝나는 순간(Done) 되살린다. 자막·무전 캡션은 남긴다. ──
        if (running) _overlay.HideHud(KeepsDuringIntro);
        else _overlay.RestoreHud();
        SetObjectiveSuppressed(running);

        // ── 스킵 ──
        bool skippable = running && !state.IsSkipClosed(now);   // 스킵이 정해지면(중간보스 샷으로 가는 중 포함) 버튼을 걷는다
        if (_overlay.TickSkip(skippable, state.Sequence, _data.SkipHoldSeconds))
            _director.RequestZone4IntroSkipVote(state.Sequence);
        _overlay.Apply(wantCinematic, skippable, false, _data.LetterboxHeight, _data.VignetteIntensity, _data.VignetteSmoothness,
                       _data.OverlayFadeSeconds, _data.SkipHoldSeconds, state.Votes, state.Voters, Time.unscaledDeltaTime);
        _overlay.ApplyFont(ResolveFont());

        // ── 무전 ──
        TickCaption(state, t);
    }

    private void Idle()
    {
        _overlay.ExitCamera(0f);
        _overlay.RestoreHud();
        _overlay.SetDimFocus(null);
        _overlay.SetDimSuppress(0f);
        SetOutlines(-1, false);
        _overlay.Apply(false, false, false, 0f, 0f, 0.4f, 0f, 1f, 0, 0, Time.unscaledDeltaTime);
        _caption?.Hide();
        SetObjectiveSuppressed(false);
    }

    private bool KeepsDuringIntro(Canvas canvas) =>
        CinematicOverlay.Has<TutorialGuideUI>(canvas.gameObject) || (_caption != null && _caption.Owns(canvas));

    private void SetObjectiveSuppressed(bool suppressed)
    {
        if (_suppressingObjective == suppressed) return;
        TutorialGuideUI ui = TutorialGuideUI.Instance;
        if (ui == null) return;   // 다음 프레임에 다시
        ui.SetObjectiveSuppressed(suppressed);
        _suppressingObjective = suppressed;
    }

    // ── 샷 구도 ─────────────────────────────────────────────────────────────

    private void BuildPoses(int sequence)
    {
        _shotPositions.Clear();
        _shotRotations.Clear();
        _shotFovs.Clear();
        _shotFocus.Clear();
        _shotPanel.Clear();
        SetOutlines(-1, false);

        ZoneBridgeGateManager manager = ZoneBridgeGateManager.Instance;
        if (manager == null || !manager.TryGetFirstGate(out ZoneBridgeGate gate) || gate.PanelCount == 0)
        {
            if (!_warnedNoGate) Edit.LogWarning("[Zone4Intro] 다리 게이트(ZoneL_typeB)를 못 찾았다 — 카메라 없이 대사·HUD 만 진행한다.", this);
            _warnedNoGate = true;
            _posesSequence = sequence;
            return;
        }
        _gate = gate;

        // 패널 바라보는 점 = 패널 메시 중심(없으면 패널 위치) — 패널이 화면 한가운데(경석 10-27).
        var panels = new List<Vector3>();
        var panelIndex = new List<int>();
        for (int i = 0; i < gate.PanelCount; i++)
        {
            if (gate.TryGetPanelBounds(i, out Bounds b)) panels.Add(b.center);
            else if (gate.TryGetPanelPosition(i, out Vector3 p)) panels.Add(p);
            else continue;
            panelIndex.Add(i);
        }
        if (panels.Count == 0) { _posesSequence = sequence; return; }

        Vector3 south = BossIntroPresentation.GameplayScreenSouth();
        List<int> order = CinematicFraming.ClockwiseFromBottomLeft(panels, south);
        Vector3 center = Vector3.zero;
        foreach (Vector3 p in panels) center += p;
        center /= panels.Count;

        for (int shot = 0; shot < MapSceneZone4IntroData.PanelShotCount; shot++)
        {
            int k = order[shot % order.Count];
            Vector3 panel = panels[k];
            Vector3 focus = Vector3.Lerp(panel, new Vector3(center.x, panel.y, center.z), _data.PanelTowardCenter) + Vector3.up * _data.PanelFocusHeight;
            AddPose(focus, south, _data.PanelPitch, _data.PanelDistance, _data.PanelFov, panelIndex[k]);
        }

        // 다리·중앙 섬 — 다리 조각 + 존 중앙을 감싸는 범위를 화면 가운데에, 자동이면 다 들어오는 거리(경석 10-27 — 다리가 안 보였다).
        var frame = new Bounds(center, Vector3.zero);
        bool hasBridge = gate.TryGetBridgeBounds(out Bounds bridge);
        if (hasBridge) frame.Encapsulate(bridge);
        Vector3 bridgeFocus = frame.center + CinematicFraming.Flat(south) * _data.BridgeFocusSouth + Vector3.up * _data.BridgeFocusHeight;
        float distance = _data.BridgeDistance;
        if (_data.BridgeAutoFit && hasBridge)
        {
            Camera cam = Camera.main;
            float aspect = cam != null ? cam.aspect : 16f / 9f;
            distance = CinematicFraming.FitDistance(frame.extents.magnitude, _data.BridgeFov, aspect, _data.BridgeFitMargin);
        }
        AddPose(bridgeFocus, south, _data.BridgePitch, distance, _data.BridgeFov, -1);

        // 다리 외곽선 반경 — 이동식 다리 중 2층 바닥 아래 부분은 외곽선에서 뺀다(경석 10-27). 인스펙터 값 > 0 이면 그 값, 아니면 자동 측정.
        _clipCenter = new Vector3(center.x, 0f, center.z);
        // 자동 측정은 늘 해 둔다(로그 — 인스펙터 값을 맞출 때 참고). 자동은 충돌체 기준이라 보이는 바닥 메시 가장자리와 어긋날 수 있다(10-27 실측 13.10 vs 보이는 끝 ≈ 12.3).
        _clipRadius = MeasureClipRadius(gate, _clipCenter);
        Edit.Log($"[Zone4Intro] 다리 외곽선 반경 — 인스펙터 {_data.BridgeClipRadius:0.00}m · 자동 {_clipRadius:0.00}m → 쓰는 값 {EffectiveClipRadius():0.00}m · 중심 {_clipCenter}.", this);

        _posesSequence = sequence;
        Edit.Log($"[Zone4Intro] 샷 구도 — 게이트 Slot {gate.SlotID} 패널 {panels.Count}개 · 중심 {center} · " +
                 $"다리 {(hasBridge ? $"범위 {bridge.center}±{bridge.extents}" : "없음")} · 다리 샷 거리 {distance:0.#}m.", this);
    }

    private void AddPose(Vector3 focus, Vector3 south, float pitch, float distance, float fov, int panel)
    {
        CinematicFraming.Pose(focus, south, pitch, distance, out Vector3 position, out Quaternion rotation);
        _shotPositions.Add(position);
        _shotRotations.Add(rotation);
        _shotFovs.Add(fov);
        _shotFocus.Add(focus);
        _shotPanel.Add(panel);
    }

    // 다리 외곽선 반경 자동 측정 — 조각마다 존 중심에서 바깥으로 0.1m 씩 가며 위에서 레이를 쏴, 다리 윗면이 보인 뒤 처음으로 더 높은 것(2층 바닥)에
    // 맞는 거리. 조각들 중 가장 짧은 값(안쪽을 넉넉히 자른다). 못 찾으면 0(인스펙터 대체값).
    private const float ClipSampleStep = 0.1f;
    private const float ClipCoverTolerance = 0.05f;
    private readonly List<Bounds> _segmentBounds = new List<Bounds>();
    private readonly List<float> _clipHeights = new List<float>();
    private readonly System.Text.StringBuilder _clipLog = new System.Text.StringBuilder();

    private float MeasureClipRadius(ZoneBridgeGate gate, Vector3 center)
    {
        gate.GetSegmentBounds(_segmentBounds, _data.BridgeOutlineNames);
        // 기준 윗면 = 외곽선 대상 조각들 중 가장 높은 윗면 — 조각마다 재면 이동식 다리 기준에서 조금 높은 고정 다리를 "덮은 바닥"으로 오인했다(10-27 첫 측정 10.9m).
        float top = float.NegativeInfinity;
        foreach (Bounds b in _segmentBounds) top = Mathf.Max(top, b.max.y);
        float best = float.PositiveInfinity;
        foreach (Bounds b in _segmentBounds)
        {
            Vector3 toward = new Vector3(b.center.x - center.x, 0f, b.center.z - center.z);
            if (toward.sqrMagnitude < 1e-4f) continue;
            Vector3 dir = toward.normalized;
            float along = Mathf.Abs(dir.x) * b.extents.x + Mathf.Abs(dir.z) * b.extents.z;
            float near = Mathf.Max(0f, toward.magnitude - along);
            float far = toward.magnitude + along + 0.5f;

            _clipHeights.Clear();
            for (float d = near; d <= far; d += ClipSampleStep)
            {
                Vector3 from = center + dir * d;
                from.y = top + 30f;
                _clipHeights.Add(Physics.Raycast(from, Vector3.down, out RaycastHit hit, 60f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                    ? hit.point.y : float.NaN);
            }
            int edge = CinematicFraming.FirstCoveredAfterVisible(_clipHeights, top, ClipCoverTolerance);
            if (edge >= 0) best = Mathf.Min(best, near + edge * ClipSampleStep);
            _clipLog.Append($" [{near:0.0}~{far:0.0}m → {(edge >= 0 ? $"{near + edge * ClipSampleStep:0.00}m" : "안 덮임")}]");
        }
        Edit.Log($"[Zone4Intro] 다리 외곽선 반경 측정 — 윗면 {top:0.00}m · 조각{_clipLog}", this);
        _clipLog.Clear();
        return float.IsPositiveInfinity(best) ? 0f : best;
    }

    // ── 외곽선 ──────────────────────────────────────────────────────────────

    // 옮겨 가는 중엔 도착할 샷 기준(절반 넘어가면) — 카메라가 그 패널에 다가가며 외곽선이 켜진다. 스킵 뒤(중간보스 샷으로 가는 중)는 다리.
    private void TickOutlines(MapSceneDirector.Zone4IntroState state, float t)
    {
        int shot;
        if (state.SkipAt >= 0f && t >= state.SkipAt) shot = _shotPositions.Count - 1;
        else
        {
            CinematicTimeline.Sample(_data.Shots, t, out int from, out int to, out float blend);
            shot = from >= 0 && from != to && blend < 0.5f ? from : to;
        }
        int panel = shot >= 0 && shot < _shotPanel.Count ? _shotPanel[shot] : -1;
        bool bridge = shot == _shotPanel.Count - 1;
        SetOutlines(_data.PanelOutline ? panel : -1, _data.BridgeOutline && bridge);
    }

    // 다리 외곽선 — 연출 데이터의 화면 공간 실루엣 외곽선 머티리얼을 **그대로** 쓴다(두께·색은 그 머티리얼에서 조절 — 경석 10-27).
    // 비어 있으면 근접 외곽선(반전 헐 — 바닥·벽 홈에 붙은 다리는 조각만 보인다).
    private Material BridgeOutlineMaterial() =>
        _data != null && _data.BridgeOutlineMaterialSource != null
            ? _data.BridgeOutlineMaterialSource
            : _gate != null ? _gate.HighlightOutlineMaterial : null;

    private void SetOutlines(int panel, bool bridge)
    {
        if (_gate == null) return;
        if (panel != _outlinedPanel)
        {
            // 근접 하이라이트와 같은 슬롯(패널 하나) — 내가 켠 것만 끈다.
            if (_outlinedPanel >= 0 && _gate.HighlightedPanel == _outlinedPanel) _gate.SetPanelHighlighted(_outlinedPanel, false);
            if (panel >= 0) _gate.SetPanelHighlighted(panel, true);
            _outlinedPanel = panel;
        }
        if (bridge != _outlinedBridge)
        {
            Material outline = BridgeOutlineMaterial();
            Material mask = _data != null && outline == _data.BridgeOutlineMaterialSource ? _data.BridgeOutlineMaskMaterial : null;   // 실루엣 외곽선일 때만
            _gate.SetBridgeHighlighted(bridge, _data != null ? _data.BridgeOutlineNames : null, outline, mask);
            _outlinedBridge = bridge;
        }
        // 범위 밖 버리기 — 셰이더 전역(머티리얼 에셋에 남지 않게). 켜져 있는 동안 매 프레임 — 인스펙터 반경을 Play 중에 바꾸면 바로 보인다(경석 10-27).
        Shader.SetGlobalVector(ClipCenterId, _clipCenter);
        Shader.SetGlobalFloat(ClipRadiusId, _outlinedBridge ? EffectiveClipRadius() : 0f);
        Shader.SetGlobalFloat(ClipEdgeId, _data != null ? _data.BridgeClipEdgeWidth : 0f);
    }

    // 인스펙터 반경(> 0)이 먼저, 없으면 자동 측정, 그것도 없으면 대체값.
    private float EffectiveClipRadius()
    {
        if (_data == null) return 0f;
        if (_data.BridgeClipRadius > 0f) return _data.BridgeClipRadius;
        return _clipRadius > 0f ? _clipRadius : _data.BridgeClipFallbackRadius;
    }

    // 시각 t 의 카메라 — 머묾이면 그 샷, 옮겨 가는 중이면 두 샷 사이 보간. 첫 샷 진입(게임 카메라 → 샷 0)은 Cinemachine 블렌드가 맡는다.
    // 스킵했으면(SkipAt) 그 시각의 자리에서 다리·중앙 섬 샷(중간보스)으로 옮겨 가 머문다(경석 10-27) — 그 뒤 복귀는 Cinemachine 블렌드.
    private void Sample(MapSceneDirector.Zone4IntroState state, float t, out Vector3 position, out Quaternion rotation, out float fov, out Vector3 focus)
    {
        if (state.SkipAt >= 0f && t >= state.SkipAt)
        {
            SampleTimeline(state.SkipAt, out Vector3 fromPos, out Quaternion fromRot, out float fromFov, out Vector3 fromFocus);
            int boss = _shotPositions.Count - 1;
            float k = _data.SkipToBossSeconds > 0f ? CinematicTimeline.Ease((t - state.SkipAt) / _data.SkipToBossSeconds) : 1f;
            position = Vector3.Lerp(fromPos, _shotPositions[boss], k);
            rotation = Quaternion.Slerp(fromRot, _shotRotations[boss], k);
            fov = Mathf.Lerp(fromFov, _shotFovs[boss], k);
            focus = Vector3.Lerp(fromFocus, _shotFocus[boss], k);
            return;
        }
        SampleTimeline(t, out position, out rotation, out fov, out focus);
    }

    private void SampleTimeline(float t, out Vector3 position, out Quaternion rotation, out float fov, out Vector3 focus)
    {
        int last = _shotPositions.Count - 1;
        CinematicTimeline.Sample(_data.Shots, t, out int from, out int to, out float blend);
        to = Mathf.Clamp(to, 0, last);
        if (from < 0 || from == to)
        {
            position = _shotPositions[to];
            rotation = _shotRotations[to];
            fov = _shotFovs[to];
            focus = _shotFocus[to];
            return;
        }
        from = Mathf.Clamp(from, 0, last);
        position = Vector3.Lerp(_shotPositions[from], _shotPositions[to], blend);
        rotation = Quaternion.Slerp(_shotRotations[from], _shotRotations[to], blend);
        fov = Mathf.Lerp(_shotFovs[from], _shotFovs[to], blend);
        focus = Vector3.Lerp(_shotFocus[from], _shotFocus[to], blend);
    }

    // 디밍 줄이기(0~1) — 첫 블렌드 동안 0 → 최대로 오르고, 복귀 블렌드 동안 0 으로 내려간다(끝에서 툭 바뀌지 않게).
    private float DimSuppress(MapSceneDirector.Zone4IntroState state, float t)
    {
        float max = _data.DimSuppress;
        if (state.ReturnAt >= 0f && t >= state.ReturnAt)
            return max * (1f - (state.ReturnSeconds > 0f ? CinematicTimeline.Ease((t - state.ReturnAt) / state.ReturnSeconds) : 1f));
        float enter = _data.Shots != null && _data.Shots.Length > 0 ? _data.Shots[0].moveSeconds : 0f;
        return max * (enter > 0f ? CinematicTimeline.Ease(t / enter) : 1f);
    }

    // 디밍 기준점 — 연출 중엔 카메라가 보는 점, 복귀 블렌드 동안은 그 점에서 플레이어(카메라 추적 대상)로 같은 속도로 옮겨 간다.
    private Vector3 DimFocus(MapSceneDirector.Zone4IntroState state, float t)
    {
        if (state.ReturnAt < 0f || t < state.ReturnAt)
        {
            Sample(state, t, out _, out _, out _, out Vector3 focus);
            return focus;
        }

        Sample(state, state.ReturnAt, out _, out _, out _, out Vector3 from);
        CameraTargetSwitcher switcher = CameraTargetSwitcher.Active;
        Transform player = switcher != null ? switcher.CurrentFollowTarget : null;
        if (player == null) return from;
        float k = state.ReturnSeconds > 0f ? CinematicTimeline.Ease((t - state.ReturnAt) / state.ReturnSeconds) : 1f;
        return Vector3.Lerp(from, player.position, k);
    }

    // ── 무전 ────────────────────────────────────────────────────────────────

    private void TickCaption(MapSceneDirector.Zone4IntroState state, float t)
    {
        _caption.Configure(_data.CaptionBackground, _data.CaptionBadge, _data.CaptionWalkie, ResolveFont(), _data.CaptionAnchor, _data.CaptionOffset,
                           _data.CaptionScale, _data.CaptionFontSize, _data.CaptionTextColor);
        _caption.ConfigureMotion(_data.CaptionRiseDistance, _data.CaptionRiseSeconds, _data.BadgePulseScale, _data.BadgePulseSeconds);

        // 복귀 무전 — 복귀 블렌드가 끝난 뒤(자연 진행·스킵 모두).
        if (state.ReturnAt >= 0f)
        {
            float returnStart = state.ReturnAt + state.ReturnSeconds + _data.ReturnLineDelaySeconds;
            if (t >= returnStart)
            {
                if (t - returnStart > ReturnLineGiveUpSeconds)
                {
                    _caption.Hide();   // 오래전에 끝났다 — 매 프레임 문구를 찾지 않는다
                    return;
                }
                ShowChunk(_data.ReturnLineId, t - returnStart, _data.ReturnLineChunkSeconds, _data.ReturnLineGapSeconds);
                return;
            }
            if (t >= state.ReturnAt || (state.SkipAt >= 0f && t >= state.SkipAt))
            {
                _caption.Hide();   // 복귀 중 · 스킵 뒤 중간보스 샷 — 소개 대사는 끊었다
                return;
            }
        }

        // 소개 대사 — 가장 늦게 시작한 줄이 이긴다(앞 줄의 남은 조각은 버린다).
        int best = -1;
        float bestStart = float.NegativeInfinity;
        CinematicLineTiming[] lines = _data.Lines;
        for (int i = 0; lines != null && i < lines.Length; i++)
        {
            float start = CinematicTimeline.ArrivalTime(_data.Shots, lines[i].shotIndex) + lines[i].delaySeconds;
            if (start > t || start < bestStart) continue;
            best = i;
            bestStart = start;
        }
        if (best < 0)
        {
            _caption.Hide();
            return;
        }

        // 마지막 조각 붙잡기 — 다음 대사 시작(없으면 자연 복귀 시작)까지 남긴다(장치 대사가 장치를 다 보여 줄 때까지, 경석 10-27).
        float holdUntil = -1f;
        if (lines[best].holdUntilNextLine)
        {
            float next = state.ReturnAt >= 0f ? state.ReturnAt : float.PositiveInfinity;
            for (int i = 0; i < lines.Length; i++)
            {
                float start = CinematicTimeline.ArrivalTime(_data.Shots, lines[i].shotIndex) + lines[i].delaySeconds;
                if (start > bestStart && start < next) next = start;
            }
            holdUntil = next - bestStart;
        }
        ShowChunk(lines[best].lineId, t - bestStart, lines[best].chunkSeconds, lines[best].gapSeconds, holdUntil);
    }

    private void ShowChunk(string id, float local, float chunkSeconds, float gapSeconds, float holdUntil = -1f)
    {
        List<string> chunks = ChunksOf(id);
        if (chunks == null || !CinematicLineSchedule.Sample(local, chunks.Count, chunkSeconds, gapSeconds, holdUntil,
                                                            out int index, out float within, out float duration))
        {
            _caption.Hide();
            return;
        }
        float fade = _data.CaptionFadeSeconds;
        float alpha = fade > 0f ? Mathf.Clamp01(Mathf.Min(within / fade, (duration - within) / fade)) : 1f;
        _caption.Show(chunks[index], alpha, within);   // within = 이 조각이 나타난 뒤 — 올라옴·배지 펄스
    }

    private List<string> ChunksOf(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        TutorialGuideUI ui = TutorialGuideUI.Instance;
        if (ui == null || ResolveFont() == null) return null;   // 글꼴 전에 재면 폭이 틀린 채로 굳는다(기본 글꼴엔 한글이 없다)
        if (!ui.TryResolveText(id, out string text))
        {
            if (_warnedIds.Add(id)) Edit.LogWarning($"[Zone4Intro] 무전 문구 '{id}' 가 TutorialGuideTexts/Strings 에 없다 — 건너뛴다.", this);
            return null;
        }
        if (_chunks.TryGetValue(id, out (string source, List<string> chunks) cached) && cached.source == text) return cached.chunks;

        List<string> split = CinematicLineSplitter.Split(text, _caption.Measure, _data.MaxLineWidth);
        _chunks[id] = (text, split);
        return split;
    }

    private TMPro.TMP_FontAsset ResolveFont()
    {
        if (_data != null && _data.CaptionFont != null) return _data.CaptionFont;
        TutorialGuideUI ui = TutorialGuideUI.Instance;
        return ui != null ? ui.SubtitleFont : null;
    }
}
