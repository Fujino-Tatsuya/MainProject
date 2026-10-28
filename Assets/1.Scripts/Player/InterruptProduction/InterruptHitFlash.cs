using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 적중 섬광 단계(할 일 132 §15 — 레퍼런스 영상 30fps 기준 재구성). 번호 = 계획서 ②~⑥(① = 성공 Started 순간·히트스톱 시작).
/// ②③④ = 오너 암전 위 오너 화면 그림, 그 합의 끝 = 섬광 순간(오너 암전 걷힘·월드 섬광·팀원 암순응), ⑤⑥ = 실제 화면 위 오너 화면 효과.
/// </summary>
public enum InterruptHitFlashStage
{
    None = 0,
    /// <summary>② 적중 지점 중심 베이지 스타버스트 렌즈 플레어(부드러운 코어 + 길이 제각각 빛살, 아나모픽 가로 빛살 최장) + 얇고 흐린 푸른 고리. 배경은 오너 암전 그대로.</summary>
    Starburst = 2,
    /// <summary>③ 화면 전체 밝은 회청색(약 0.75) + 적중 지점 중심 강한 방사형 블러, 오너·대상 블러 실루엣이 비친다 — 유일하게 화면을 덮는 단계.</summary>
    Whiteout = 3,
    /// <summary>④ 검정 배경 위 마스크 암전 남김 영역(오너·대상·빔)을 키워 공격 방향으로 늘인 흰 덩어리 한 장.</summary>
    Smear = 4,
    /// <summary>⑤ 섬광 순간에 시작 — 실제 화면 위 방사형 블러 + Volume 색수차(가장자리) + 블룸 최대.</summary>
    Aberration = 5,
    /// <summary>⑥ 블룸 덩어리가 남다가 가라앉음, 블러·색수차 0 으로 감소.</summary>
    Settle = 6,
}

/// <summary>적중 섬광 칸(할 일 132 §15·§17) — 디렉터가 구현하고, 성공 사건마다 읽는다(캐싱 없음).</summary>
public interface IInterruptHitFlashSettings
{
    /// <summary>②③④ 길이(렌더 프레임 수, §17-3). 0 = 그 단계 건너뜀.</summary>
    int HitFlashStarburstFrames { get; }
    int HitFlashWhiteoutFrames { get; }
    int HitFlashSmearFrames { get; }
    /// <summary>⑤⑥ 길이(초, <see cref="HitFlashTimeBase"/> 시계). 0 = 그 단계 건너뜀.</summary>
    float HitFlashAberrationDuration { get; }
    float HitFlashSettleDuration { get; }
    /// <summary>⑤⑥ 시계(할 일 132 §14) — 실시간(기본) = 슬로우와 무관, 스케일 = 성공 슬로우를 따라 느려진다. ②③④·섬광 순간은 프레임 수라 이 칸을 타지 않는다.</summary>
    InterruptEffectTimeBase HitFlashTimeBase { get; }
}

/// <summary>
/// 적중 섬광 시간표(할 일 132 §15·§17) — 성공 순간 칸을 한 번 복사한다.
/// ②③④ = 성공 Started 프레임부터 렌더 프레임 수, 그 끝 프레임 = 섬광 순간(⑤ 시작). ⑤⑥ = 섬광 순간부터 진행기 시계 초.
/// 히트스톱은 성공 Started 순간부터라 ②는 늘 히트스톱 안에서 시작한다(히트스톱 길이 0 이어도 같은 순간).
/// </summary>
public readonly struct InterruptHitFlashTimeline
{
    /// <summary>단계 순서 — 시간표·진행기·테스트가 같은 목록을 쓴다.</summary>
    public static readonly InterruptHitFlashStage[] Order =
    {
        InterruptHitFlashStage.Starburst, InterruptHitFlashStage.Whiteout, InterruptHitFlashStage.Smear,
        InterruptHitFlashStage.Aberration, InterruptHitFlashStage.Settle,
    };

    public readonly int StarburstFrames;
    public readonly int WhiteoutFrames;
    public readonly int SmearFrames;
    public readonly float Aberration;
    public readonly float Settle;

    public InterruptHitFlashTimeline(int starburstFrames, int whiteoutFrames, int smearFrames, float aberration, float settle)
    {
        StarburstFrames = System.Math.Max(0, starburstFrames);
        WhiteoutFrames = System.Math.Max(0, whiteoutFrames);
        SmearFrames = System.Math.Max(0, smearFrames);
        Aberration = Sanitize(aberration);
        Settle = Sanitize(settle);
    }

    public static InterruptHitFlashTimeline From(IInterruptHitFlashSettings settings) =>
        settings == null
            ? default
            : new InterruptHitFlashTimeline(settings.HitFlashStarburstFrames, settings.HitFlashWhiteoutFrames,
                settings.HitFlashSmearFrames, settings.HitFlashAberrationDuration, settings.HitFlashSettleDuration);

    /// <summary>섬광 순간(⑤ 시작) 프레임 — 성공 Started 프레임 + ②+③+④. 오너 암전 걷힘·월드 섬광·라이트·팀원 암순응이 이 프레임이다.</summary>
    public int FlashMomentFrames => StarburstFrames + WhiteoutFrames + SmearFrames;

    /// <summary>섬광 순간 뒤 길이(⑤+⑥, 초).</summary>
    public float AfterFlashDuration => Aberration + Settle;

    /// <summary>프레임 수로 재는 단계(②③④).</summary>
    public static bool IsFrameStage(InterruptHitFlashStage stage) =>
        stage == InterruptHitFlashStage.Starburst || stage == InterruptHitFlashStage.Whiteout || stage == InterruptHitFlashStage.Smear;

    /// <summary>②③④ 시작 프레임(성공 Started 기준). ⑤⑥·None = 섬광 순간 프레임.</summary>
    public int StartFrameOf(InterruptHitFlashStage stage)
    {
        switch (stage)
        {
            case InterruptHitFlashStage.Starburst: return 0;
            case InterruptHitFlashStage.Whiteout: return StarburstFrames;
            case InterruptHitFlashStage.Smear: return StarburstFrames + WhiteoutFrames;
            default: return FlashMomentFrames;
        }
    }

    /// <summary>⑤⑥ 시작 시각(섬광 순간 기준 초). None = ⑥ 끝. ②③④ = 0.</summary>
    public float StartAfterFlashOf(InterruptHitFlashStage stage)
    {
        switch (stage)
        {
            case InterruptHitFlashStage.Settle: return Aberration;
            case InterruptHitFlashStage.None: return AfterFlashDuration;
            default: return 0f;
        }
    }

    /// <summary>
    /// 지금 단계와 그 단계 진행도 [0, 1). <paramref name="elapsedFrames"/> = 성공 Started 프레임 뒤 지난 렌더 프레임 수,
    /// <paramref name="secondsSinceFlash"/> = 섬광 순간 뒤 지난 시간(초 — 섬광 순간 전엔 쓰지 않는다). 시작 전·끝난 뒤 = None. 길이 0 단계는 건너뛴다.
    /// </summary>
    public InterruptHitFlashStage StageAt(int elapsedFrames, float secondsSinceFlash, out float progress)
    {
        progress = 0f;
        if (elapsedFrames < 0)
            return InterruptHitFlashStage.None;

        if (elapsedFrames < FlashMomentFrames)
        {
            for (int i = 0; i < 3; i++)
            {
                InterruptHitFlashStage stage = Order[i];
                int start = StartFrameOf(stage);
                int length = FramesOf(stage);
                if (elapsedFrames < start + length)
                {
                    progress = (float)(elapsedFrames - start) / length;
                    return stage;
                }
            }
        }

        if (secondsSinceFlash < 0f || float.IsNaN(secondsSinceFlash))
            return InterruptHitFlashStage.None;

        if (secondsSinceFlash < Aberration)
        {
            progress = secondsSinceFlash / Aberration;
            return InterruptHitFlashStage.Aberration;
        }

        if (secondsSinceFlash < Aberration + Settle)
        {
            progress = (secondsSinceFlash - Aberration) / Settle;
            return InterruptHitFlashStage.Settle;
        }

        return InterruptHitFlashStage.None;
    }

    /// <summary>②③④ 프레임 수. ⑤⑥·None = 0.</summary>
    public int FramesOf(InterruptHitFlashStage stage)
    {
        switch (stage)
        {
            case InterruptHitFlashStage.Starburst: return StarburstFrames;
            case InterruptHitFlashStage.Whiteout: return WhiteoutFrames;
            case InterruptHitFlashStage.Smear: return SmearFrames;
            default: return 0;
        }
    }

    private static float Sanitize(float value) => float.IsNaN(value) || value < 0f ? 0f : value;
}

/// <summary>
/// 한 프레임의 오너 화면 효과 세기(할 일 132 §15) — 전부 [0, 1]. 실제 크기는 각 칸(플레어 세기·블러 세기·Volume 프로필 값)이 곱한다.
/// </summary>
public readonly struct InterruptHitFlashLook
{
    /// <summary>② 렌즈 플레어(LensFlareComponentSRP 세기 배율).</summary>
    public readonly float Flare;
    /// <summary>적중 지점 중심 방사형 블러(렌더 기능 패스).</summary>
    public readonly float Blur;
    /// <summary>색수차 Volume 가중치.</summary>
    public readonly float Aberration;
    /// <summary>블룸 Volume 가중치.</summary>
    public readonly float Bloom;

    public InterruptHitFlashLook(float flare, float blur, float aberration, float bloom)
    {
        Flare = Mathf.Clamp01(flare);
        Blur = Mathf.Clamp01(blur);
        Aberration = Mathf.Clamp01(aberration);
        Bloom = Mathf.Clamp01(bloom);
    }
}

/// <summary>오너 화면 섬광 출력(②~⑥) — 실제 = <see cref="InterruptHitFlashOverlay"/>(오버레이 캔버스·렌즈 플레어·방사형 블러·Volume).</summary>
public interface IInterruptHitFlashOverlayOutput
{
    /// <param name="worldCenter">적중 지점(월드) — 출력이 화면 위치로 바꾼다.</param>
    /// <param name="worldDirection">공격 방향(월드, 근접 = 시전 방향·대쉬 = 대시 방향) — 출력이 화면 방향으로 투영해 ④ 스미어를 늘린다. 0 = 모름(재질 기본 방향).</param>
    void Show(InterruptHitFlashStage stage, float progress, Vector3 worldCenter, Vector3 worldDirection);
    /// <summary>즉시 전부 원래대로(오버레이 끔·플레어 끔·블러 0·Volume 가중치 복구).</summary>
    void Hide();
}

/// <summary>섬광 순간 한 건 — 디렉터가 월드 섬광·암순응을 튼다.</summary>
public readonly struct InterruptHitFlashMoment
{
    public readonly InterruptProductionEvent Event;
    /// <summary>이 화면이 발동자 본인.</summary>
    public readonly bool ViewerIsCaster;
    /// <summary>이 화면에서 오너 화면 섬광(②~⑥)이 돌았다 — 본인 self 암순응을 대신한다.</summary>
    public readonly bool OwnerOverlay;

    public InterruptHitFlashMoment(in InterruptProductionEvent e, bool viewerIsCaster, bool ownerOverlay)
    {
        Event = e;
        ViewerIsCaster = viewerIsCaster;
        OwnerOverlay = ownerOverlay;
    }
}

/// <summary>본인/남 화면의 섬광 순간 암순응 선택.</summary>
public enum InterruptHitFlashDarkAdaptation
{
    None,
    /// <summary>본인 화면 — 오너 화면 섬광이 안 돈 경우(약 예측이 성공)만.</summary>
    Self,
    /// <summary>다른 사람 화면.</summary>
    Others,
}

/// <summary>적중 섬광 순수 규칙(할 일 132 §15).</summary>
public static class InterruptHitFlashRules
{
    /// <summary>오버레이 덮어쓸 텍스처 칸(셰이더 속성) — ②③④(⑤⑥은 실제 화면 효과라 그림 칸 없음).</summary>
    public static readonly string[] OverlayTextureProperties = { "_StarburstTex", "_WhiteoutTex", "_SmearTex" };
    /// <summary>위 칸이 차 있을 때 켜는 셰이더 키워드 — 꺼져 있으면 그 단계는 절차 생성(②는 렌즈 플레어 층 + 절차 고리).</summary>
    public static readonly string[] OverlayTextureKeywords = { "_STARBURST_TEX_ON", "_WHITEOUT_TEX_ON", "_SMEAR_TEX_ON" };

    /// <summary>오너 화면 섬광을 트는가 — 내 성공이고 그 순간 오너 암전이 켜져 있을 때(강 시도)만. 약 예측·대상 없음·실패·팀원 = 없음.</summary>
    public static bool ShouldPlayOwnerOverlay(SlowMotionPurpose purpose, bool viewerIsCaster, bool successDimmed) =>
        purpose == SlowMotionPurpose.Success && viewerIsCaster && successDimmed;

    /// <summary>
    /// 섬광 순간 암순응 — 남 화면 = others, 본인 화면 = 오너 화면 섬광이 돌았으면 없음(마스크 암전·섬광이 대신 — 겹치면 두 번 어두워진다),
    /// 안 돌았으면 self(약 예측이 성공한 경우).
    /// </summary>
    public static InterruptHitFlashDarkAdaptation SelectDarkAdaptation(bool viewerIsCaster, bool ownerOverlay) =>
        !viewerIsCaster ? InterruptHitFlashDarkAdaptation.Others
        : ownerOverlay ? InterruptHitFlashDarkAdaptation.None
        : InterruptHitFlashDarkAdaptation.Self;

    /// <summary>단계의 덮어쓸 텍스처 칸 순번(0~2). ⑤⑥·None = -1.</summary>
    public static int OverlayTextureIndex(InterruptHitFlashStage stage)
    {
        switch (stage)
        {
            case InterruptHitFlashStage.Starburst: return 0;
            case InterruptHitFlashStage.Whiteout: return 1;
            case InterruptHitFlashStage.Smear: return 2;
            default: return -1;
        }
    }

    /// <summary>오버레이 재질의 덮어쓸 텍스처 칸 셋에 맞춰 키워드를 맞춘다(셰이더 GUI·런타임 사본). 하나라도 바뀌면 true.</summary>
    public static bool ApplyOverlayKeywords(Material material)
    {
        bool changed = false;
        for (int i = 0; i < OverlayTextureProperties.Length; i++)
            changed |= InterruptLightGatherRules.ApplyOverrideKeyword(material, OverlayTextureProperties[i], OverlayTextureKeywords[i]);
        return changed;
    }

    /// <summary>
    /// 단계·진행도 → 오너 화면 효과 세기. ② 플레어, ③ 블러 최대, ④ 그림만, ⑤ 블러 0.8→0.6·색수차 1·블룸 1(섬광 순간부터 최대 — 코어가 번진다, §16),
    /// ⑥ 블러 0.6→0·색수차 1→0·블룸 1→0(끝까지 남다가 가라앉음). 끝 = 전부 0.
    /// </summary>
    public static InterruptHitFlashLook Look(InterruptHitFlashStage stage, float progress)
    {
        float t = Mathf.Clamp01(progress);
        switch (stage)
        {
            case InterruptHitFlashStage.Starburst: return new InterruptHitFlashLook(1f - 0.4f * t, 0f, 0f, 0f);
            case InterruptHitFlashStage.Whiteout: return new InterruptHitFlashLook(0f, 1f, 0f, 0f);
            case InterruptHitFlashStage.Aberration: return new InterruptHitFlashLook(0f, Mathf.Lerp(0.8f, 0.6f, t), 1f, 1f);
            case InterruptHitFlashStage.Settle: return new InterruptHitFlashLook(0f, 0.6f * (1f - t), 1f - t, 1f - t * t);
            default: return default;
        }
    }

    /// <summary>오버레이 캔버스가 그림을 그리는 단계 — ②③④. ⑤⑥ 은 실제 화면 효과(블러·Volume)뿐이라 캔버스를 끈다(§16 무지개 고리 제거).</summary>
    public static bool OverlayDraws(InterruptHitFlashStage stage) =>
        stage == InterruptHitFlashStage.Starburst || stage == InterruptHitFlashStage.Whiteout || stage == InterruptHitFlashStage.Smear;

    /// <summary>적중 지점 뷰포트 좌표 → 화면 효과 중심. 카메라 뒤(z ≤ 0)·NaN 이면 화면 가운데.</summary>
    public static Vector2 ViewportCenter(Vector3 viewportPoint) =>
        viewportPoint.z <= 0f || float.IsNaN(viewportPoint.x) || float.IsNaN(viewportPoint.y)
            ? new Vector2(0.5f, 0.5f)
            : new Vector2(viewportPoint.x, viewportPoint.y);

    /// <summary>
    /// ④ 스미어 방향 투영(§16) — 적중 지점과 그 지점 + 공격 방향의 뷰포트 좌표 → 화면 방향(화면 높이 단위, 길이 1).
    /// 두 점 중 하나라도 카메라 뒤·NaN 이거나 화면 위 길이가 거의 0(카메라 축과 나란한 방향)이면 false — 재질 기본 방향을 쓴다.
    /// </summary>
    public static bool TryScreenDirection(Vector3 viewportCenter, Vector3 viewportAhead, float aspect, out Vector2 direction)
    {
        direction = Vector2.zero;
        if (viewportCenter.z <= 0f || viewportAhead.z <= 0f)
            return false;

        var delta = new Vector2((viewportAhead.x - viewportCenter.x) * Mathf.Max(aspect, 1e-4f), viewportAhead.y - viewportCenter.y);
        if (float.IsNaN(delta.x) || float.IsNaN(delta.y) || delta.sqrMagnitude < 1e-10f)
            return false;

        direction = delta.normalized;
        return true;
    }
}

/// <summary>
/// 섬광 Volume 가중치 스냅샷(할 일 132 §15) — 재생 첫 프레임에 색수차·블룸 Volume 의 지금 가중치를 저장하고, 끝·강제 리셋에 그 값으로 되돌린다.
/// 재생 중 다시 잡지 않는다(새 성공이 이어져도 처음 값으로 돌아간다). Volume 프로필 에셋은 건드리지 않는다 — 가중치만.
/// </summary>
public sealed class InterruptHitFlashVolumeSnapshot
{
    public bool Captured { get; private set; }
    public float Aberration { get; private set; }
    public float Bloom { get; private set; }

    /// <summary>재생 시작 — 이미 잡아 뒀으면 무시(true = 이번에 잡음).</summary>
    public bool Capture(float aberrationWeight, float bloomWeight)
    {
        if (Captured)
            return false;

        Captured = true;
        Aberration = aberrationWeight;
        Bloom = bloomWeight;
        return true;
    }

    /// <summary>끝·리셋 — 잡아 둔 값을 돌려주고 비운다. 잡은 적이 없으면 false(되돌릴 것 없음).</summary>
    public bool Release(out float aberrationWeight, out float bloomWeight)
    {
        aberrationWeight = Aberration;
        bloomWeight = Bloom;
        if (!Captured)
            return false;

        Captured = false;
        return true;
    }
}

/// <summary>
/// 적중 섬광 진행기(할 일 132 §15·§17) — 순수 로직. 디렉터가 성공 단계(purpose==Success) 안에서 사건을 넘기고 매 프레임 Tick 한다.
/// 프레임(frame)은 렌더 프레임 번호(디렉터 = Time.frameCount) — ②③④·섬광 순간을 잰다.
/// 시각(now)은 <see cref="Clock"/> 시계 — 디렉터가 <see cref="InterruptEffectTime.Now"/> 로 읽어 넘긴다(할 일 132 §14). ⑤⑥ 만 잰다.
/// <list type="bullet">
/// <item>오너 화면 섬광(<see cref="BeginOwnerOverlay"/>): ②~⑥ 을 출력에 넘기고 끝나면 숨긴다(전부 복구). 새 성공이 오면 처음부터.</item>
/// <item>섬광 순간(<see cref="ScheduleFlash"/>): 모든 피어가 성공 Started 프레임 + ②③④ 프레임 수에 <c>onFlash</c> 를 부른다(월드 섬광·암순응·오너 암전 끔).
///       여러 발동자의 성공은 각각 따로 온다.</item>
/// <item>강제 리셋(<see cref="ForcedReset"/>): 화면 효과 즉시 복구, 남은 섬광 순간 취소.</item>
/// </list>
/// 오너 암전 끄기는 이 진행기의 섬광 순간(<c>onFlash</c>)에 디렉터가 오너 상태 기계에 알린다 — 시간표는 이 진행기 하나다.
/// </summary>
public sealed class InterruptHitFlashSequencer
{
    private struct Pending
    {
        public int DueFrame;
        public InterruptHitFlashMoment Moment;
    }

    private readonly IInterruptHitFlashSettings _settings;
    private readonly Action<InterruptHitFlashMoment> _onFlash;
    private readonly List<Pending> _pending = new List<Pending>();
    private readonly List<InterruptHitFlashMoment> _due = new List<InterruptHitFlashMoment>();
    private IInterruptHitFlashOverlayOutput _overlay;

    private InterruptHitFlashTimeline _overlayTimeline;
    private int _overlayStartFrame;
    private bool _overlayFlashed;   // 화면 섬광이 섬광 순간 프레임을 지났다 — ⑤⑥ 은 그때 시각(_overlayFlashTime)부터
    private float _overlayFlashTime;
    private Vector3 _overlayCenter;
    private Vector3 _overlayDirection;
    private InterruptEffectTimeBase _clock = InterruptEffectTimeBase.Unscaled;

    public InterruptHitFlashSequencer(IInterruptHitFlashSettings settings, IInterruptHitFlashOverlayOutput overlay,
        Action<InterruptHitFlashMoment> onFlash)
    {
        _settings = settings;
        _overlay = overlay;
        _onFlash = onFlash;
    }

    public bool OverlayActive { get; private set; }
    /// <summary>마지막으로 출력에 넘긴 단계(숨김 = None).</summary>
    public InterruptHitFlashStage OverlayStage { get; private set; }
    public float OverlayProgress { get; private set; }
    public int PendingFlashCount => _pending.Count;

    /// <summary>
    /// 진행기 시계 — 쉬는 동안(화면 섬광·남은 섬광 순간 없음)만 칸(<see cref="IInterruptHitFlashSettings.HitFlashTimeBase"/>)을 따라간다.
    /// 진행 중에 칸을 바꾸면 다음 성공부터(두 시계는 값이 달라 섞으면 단계가 튄다).
    /// </summary>
    public InterruptEffectTimeBase Clock
    {
        get
        {
            if (!OverlayActive && _pending.Count == 0 && _settings != null)
                _clock = _settings.HitFlashTimeBase;
            return _clock;
        }
    }

    /// <summary>출력 교체(디버그·테스트). 돌던 효과는 옛 출력에서 복구한다.</summary>
    public void SetOverlayOutput(IInterruptHitFlashOverlayOutput overlay)
    {
        _overlay?.Hide();
        _overlay = overlay;
    }

    /// <summary>
    /// [오너] 화면 섬광 시작 — 성공 Started 순간(<paramref name="now"/>·<paramref name="frame"/>). 칸은 이 순간 읽는다.
    /// <paramref name="worldDirection"/> = 이번 시도의 공격 방향(오너 상태 <see cref="InterruptOwnerPresentation.AttackDirection"/>, 0 = 모름).
    /// </summary>
    public void BeginOwnerOverlay(Vector3 worldCenter, Vector3 worldDirection, float now, int frame)
    {
        _overlayTimeline = InterruptHitFlashTimeline.From(_settings);
        _overlayStartFrame = frame;
        _overlayFlashed = false;
        _overlayCenter = worldCenter;
        _overlayDirection = worldDirection;
        OverlayActive = true;
        UpdateOverlay(now, frame);
    }

    /// <summary>[각 피어] 섬광 순간 예약 — 성공 Started 프레임 + ②③④ 프레임 수. 그 합이 0 이면 바로 부른다.</summary>
    public void ScheduleFlash(in InterruptHitFlashMoment moment, int frame)
    {
        int due = frame + InterruptHitFlashTimeline.From(_settings).FlashMomentFrames;
        if (due <= frame)
        {
            _onFlash?.Invoke(moment);
            return;
        }

        _pending.Add(new Pending { DueFrame = due, Moment = moment });
    }

    /// <summary>매 프레임 — <paramref name="now"/> = <see cref="Clock"/> 시계, <paramref name="frame"/> = 렌더 프레임 번호.</summary>
    public void Tick(float now, int frame)
    {
        if (OverlayActive)
            UpdateOverlay(now, frame);

        if (_pending.Count == 0)
            return;

        // 부르는 동안 목록이 바뀌어도(새 예약) 안전하게 먼저 떼어 낸다.
        _due.Clear();
        for (int i = 0; i < _pending.Count; i++)
        {
            if (_pending[i].DueFrame > frame)
                continue;

            _due.Add(_pending[i].Moment);
            _pending.RemoveAt(i);
            i--;
        }

        for (int i = 0; i < _due.Count; i++)
            _onFlash?.Invoke(_due[i]);
    }

    /// <summary>강제 리셋(컷신·씬 전환·세션 종료)·디렉터 꺼짐 — 화면 효과 즉시 복구, 남은 섬광 순간 취소.</summary>
    public void ForcedReset()
    {
        _pending.Clear();
        HideOverlay();
    }

    private void UpdateOverlay(float now, int frame)
    {
        // 섬광 순간 프레임에 처음 닿은 Tick 의 시각이 ⑤ 시작 — 같은 Tick 에서 부르는 섬광 순간(onFlash)과 같은 프레임이다.
        int elapsedFrames = frame - _overlayStartFrame;
        if (!_overlayFlashed && elapsedFrames >= _overlayTimeline.FlashMomentFrames)
        {
            _overlayFlashed = true;
            _overlayFlashTime = now;
        }

        InterruptHitFlashStage stage = _overlayTimeline.StageAt(elapsedFrames,
            _overlayFlashed ? now - _overlayFlashTime : -1f, out float progress);
        if (stage == InterruptHitFlashStage.None)
        {
            HideOverlay();
            return;
        }

        OverlayStage = stage;
        OverlayProgress = progress;
        _overlay?.Show(stage, progress, _overlayCenter, _overlayDirection);
    }

    private void HideOverlay()
    {
        bool wasShowing = OverlayActive || OverlayStage != InterruptHitFlashStage.None;
        OverlayActive = false;
        OverlayStage = InterruptHitFlashStage.None;
        OverlayProgress = 0f;
        if (wasShowing)
            _overlay?.Hide();
    }
}
