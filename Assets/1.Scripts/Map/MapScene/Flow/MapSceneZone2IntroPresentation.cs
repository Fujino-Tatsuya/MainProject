using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 2구역 진입 전 적 소개 연출의 <b>로컬 표시</b>(PLAN-main-radio-zone1-4 §4.2) — 모든 피어가 디렉터 복제 상태(<see cref="MapSceneDirector.Zone2Intro"/>:
/// 단계·회차·시작 ServerTime·복귀 시각·적 무리 중심·반경) + 연출 데이터(<see cref="MapSceneZone2IntroData"/>)만으로 같은 순간 같은 화면·같은 조각을 그린다.
/// · 카메라: 런타임 vcam(<see cref="CinematicOverlay"/>) — 게임 카메라 → 적 무리(블렌드) → 머묾 → 복귀 블렌드. 구도 = 무리 중심을 화면 한가운데, 무리 범위가 다 들어오는 거리.
/// · 레터박스·비네팅·스킵 위젯·HUD 끄기·디밍 = 4구역과 같은 오버레이·같은 화면 값(<see cref="MapSceneZone4IntroData"/>). 목표 패널만 숨기고 자막·무전 캡션은 남긴다.
/// · 경고 무전 = 샷 도착 뒤(명세 §6.3) 무전 캡슐 한 줄씩, 마지막 조각은 복귀 시작까지 남긴다. 스킵하면 끊는다.
/// 붙는 곳: 디렉터 OnNetworkSpawn(모든 피어, 씬 무수정).
/// </summary>
[DisallowMultipleComponent]
public sealed class MapSceneZone2IntroPresentation : MonoBehaviour
{
    private const float CameraEnterBlendLate = 0.35f;   // 늦게 붙은 클라 · 복귀 블렌드 값이 없을 때

    private MapSceneDirector _director;
    private MapSceneZone2IntroData _data;
    private MapSceneZone4IntroData _look;
    private CinematicOverlay _overlay;
    private MapSceneRadioCaptionDriver _caption;
    private bool _suppressingObjective;

    // 샷 구도 — 회차마다 한 번 계산해 고정.
    private int _poseSequence = -1;
    private Vector3 _shotPosition;
    private Quaternion _shotRotation;
    private Vector3 _shotFocus;

    private void Awake()
    {
        _director = GetComponent<MapSceneDirector>();
    }

    private void OnDestroy()
    {
        SetObjectiveSuppressed(false);
        _overlay?.Dispose();
        _caption?.Dispose();
    }

    private void Update()
    {
        MapSceneDirector.Zone2IntroState state = _director != null && _director.IsSpawned
            ? _director.Zone2Intro
            : MapSceneDirector.Zone2IntroState.Empty;

        if (state.Phase == MapSceneDirector.IntroPhase.None || state.Phase == MapSceneDirector.IntroPhase.Done && _overlay == null)
            return;   // 시작 전 · 늦게 붙었는데 이미 끝났다 — 그릴 것이 없다

        _data ??= MapSceneZone2IntroData.Load();
        _look ??= MapSceneZone4IntroData.Load();
        _overlay ??= new CinematicOverlay("Zone2Intro", _look.SkipButtonSprite);
        _caption ??= new MapSceneRadioCaptionDriver("Zone2Intro");

        NetworkManager nm = NetworkManager.Singleton;
        double now = nm != null ? nm.ServerTime.Time : 0d;
        float t = state.Elapsed(now);
        bool running = state.Phase == MapSceneDirector.IntroPhase.Running;
        bool returning = running && state.IsReturning(now);
        bool wantCinematic = running && !returning;

        if (state.Phase == MapSceneDirector.IntroPhase.Done)
        {
            Idle();
            return;
        }

        // ── 카메라 ──
        if (state.Sequence != _poseSequence) BuildPose(state);
        float enter = _data.Shots != null && _data.Shots.Length > 0 ? _data.Shots[0].moveSeconds : 0f;
        if (wantCinematic && !_overlay.IsCameraActive)
        {
            float blend = t < enter ? Mathf.Max(0f, enter - t) : CameraEnterBlendLate;   // 처음이면 남은 첫 블렌드, 늦게 붙었으면 짧게
            _overlay.EnterCamera(_shotPosition, _shotRotation, _data.Fov, blend);
        }
        else if (!wantCinematic && _overlay.IsCameraActive)
        {
            _overlay.ExitCamera(state.ReturnSeconds > 0f ? state.ReturnSeconds : CameraEnterBlendLate);
        }
        if (_overlay.IsCameraActive) _overlay.PlaceCamera(_shotPosition, _shotRotation, _data.Fov);

        // ── 디밍 — 연출 동안 기준점 = 적 무리, 세기는 진입 블렌드 동안 오르고 복귀 블렌드 동안 내려간다(4구역과 같은 값). ──
        _overlay.SetDimFocus(_shotFocus);
        _overlay.SetDimSuppress(DimSuppress(state, t, enter));

        // ── HUD — 연출 내내(복귀 블렌드 포함) 끄고, 복귀가 끝나는 순간(Done) 되살린다. 자막·무전 캡션은 남긴다. ──
        _overlay.HideHud(KeepsDuringIntro);
        SetObjectiveSuppressed(true);

        // ── 스킵 ──
        bool skippable = !state.IsSkipClosed(now);
        if (_overlay.TickSkip(skippable, state.Sequence, _data.SkipHoldSeconds))
            _director.RequestZone2IntroSkipVote(state.Sequence);
        _overlay.Apply(wantCinematic, skippable, false, _look.LetterboxHeight, _look.VignetteIntensity, _look.VignetteSmoothness,
                       _look.OverlayFadeSeconds, _data.SkipHoldSeconds, state.Votes, state.Voters, Time.unscaledDeltaTime);

        // ── 경고 무전 — 샷 도착 뒤 시작, 마지막 조각은 복귀 시작까지. 스킵·복귀 중엔 끊는다. ──
        CinematicLineTiming line = _data.Line;
        if (state.Skipped || returning)
        {
            _caption.Hide();
        }
        else
        {
            float start = _data.LineStart;
            float holdUntil = line.holdUntilNextLine && state.ReturnAt >= 0f ? state.ReturnAt - start : -1f;
            _caption.Show(line.lineId, t - start, line.chunkSeconds, line.gapSeconds, holdUntil);
        }
    }

    private void Idle()
    {
        _overlay.ExitCamera(0f);   // 이미 블렌드가 끝났다 — 남은 것만 정리
        _overlay.RestoreHud();
        _overlay.SetDimFocus(null);
        _overlay.SetDimSuppress(0f);
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

    // 적 무리 중심을 화면 한가운데, 무리 범위가 다 들어오는 거리(최소·최대 안). 화면 방향은 게임 카메라와 같은 남쪽.
    private void BuildPose(MapSceneDirector.Zone2IntroState state)
    {
        Vector3 south = BossIntroPresentation.GameplayScreenSouth();
        _shotFocus = state.Focus + Vector3.up * _data.FocusHeight;
        Camera cam = Camera.main;
        float aspect = cam != null ? cam.aspect : 16f / 9f;
        float distance = CinematicFraming.FitDistance(Mathf.Max(1f, state.Radius), _data.Fov, aspect, _data.FitMargin);
        distance = Mathf.Clamp(distance, _data.DistanceRange.x, Mathf.Max(_data.DistanceRange.x, _data.DistanceRange.y));
        CinematicFraming.Pose(_shotFocus, south, _data.Pitch, distance, out _shotPosition, out _shotRotation);
        _poseSequence = state.Sequence;
        Edit.Log($"[Zone2Intro] 샷 구도 — 적 무리 중심 {state.Focus} · 반경 {state.Radius:0.#}m · 거리 {distance:0.#}m.", this);
    }

    private float DimSuppress(MapSceneDirector.Zone2IntroState state, float t, float enter)
    {
        float max = _look.DimSuppress;
        if (state.ReturnAt >= 0f && t >= state.ReturnAt)
            return max * (1f - (state.ReturnSeconds > 0f ? CinematicTimeline.Ease((t - state.ReturnAt) / state.ReturnSeconds) : 1f));
        return max * (enter > 0f ? CinematicTimeline.Ease(t / enter) : 1f);
    }
}
