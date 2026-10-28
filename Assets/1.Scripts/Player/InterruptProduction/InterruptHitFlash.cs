using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>적중 섬광 단계(할 일 132 단계 5). 1~3 = 오너 화면 오버레이, 4 = 실제 화면 복귀(오버레이가 걷히고 월드 섬광).</summary>
public enum InterruptHitFlashStage
{
    None = 0,
    /// <summary>1. 검은 배경 위 흰 폭발(중심 = 적중 지점 화면 위치).</summary>
    Burst = 1,
    /// <summary>2. 빛 갈라짐(스타버스트 글레어) + 원형 충격파 링.</summary>
    Glare = 2,
    /// <summary>3. 화면 전체 흰색 반전(+ 검은 집중선 칸).</summary>
    Invert = 3,
    /// <summary>4. 실제 화면 복귀 — 3단계 그림이 걷힌다. 시작 순간 = 월드 섬광·오너 암전 걷힘·팀원 암순응.</summary>
    Return = 4,
}

/// <summary>적중 섬광 칸(할 일 132 단계 5) — 디렉터가 구현하고, 성공 사건마다 읽는다(캐싱 없음).</summary>
public interface IInterruptHitFlashSettings
{
    /// <summary>단계 길이(초, <see cref="HitFlashTimeBase"/> 시계). 0 = 그 단계 건너뜀.</summary>
    float HitFlashBurstDuration { get; }
    float HitFlashGlareDuration { get; }
    float HitFlashInvertDuration { get; }
    float HitFlashReturnDuration { get; }
    /// <summary>3단계 검은 집중선.</summary>
    bool HitFlashSpeedLines { get; }
    /// <summary>진행기 시계(할 일 132 §14) — 실시간(기본) = 슬로우와 무관, 스케일 = 성공 슬로우·히트스톱을 따라 느려진다. 섬광 순간도 이 시계다.</summary>
    InterruptEffectTimeBase HitFlashTimeBase { get; }
}

/// <summary>
/// 적중 섬광 시간표(할 일 132 단계 5) — 성공 순간 칸을 한 번 복사한다. 시각은 성공 Started 기준 경과(진행기 시계 초).
/// 히트스톱은 성공 Started 순간부터라 1단계는 늘 히트스톱 안에서 시작한다(히트스톱 길이 0 이어도 같은 순간).
/// 스케일 시계면 1단계는 히트스톱(0.02배) 동안 거의 멈춰 있다.
/// </summary>
public readonly struct InterruptHitFlashTimeline
{
    public readonly float Burst;
    public readonly float Glare;
    public readonly float Invert;
    public readonly float Return;

    public InterruptHitFlashTimeline(float burst, float glare, float invert, float ret)
    {
        Burst = Sanitize(burst);
        Glare = Sanitize(glare);
        Invert = Sanitize(invert);
        Return = Sanitize(ret);
    }

    public static InterruptHitFlashTimeline From(IInterruptHitFlashSettings settings) =>
        settings == null
            ? default
            : new InterruptHitFlashTimeline(settings.HitFlashBurstDuration, settings.HitFlashGlareDuration,
                settings.HitFlashInvertDuration, settings.HitFlashReturnDuration);

    /// <summary>섬광 순간(4단계 시작) — 1~3단계 길이 합. 월드 섬광·오너 암전 걷힘·팀원 암순응이 이때다.</summary>
    public float FlashMoment => Burst + Glare + Invert;

    /// <summary>전체 길이(1~4단계).</summary>
    public float Total => FlashMoment + Return;

    /// <summary>경과 → 단계와 그 단계 진행도 [0, 1). 시작 전·끝난 뒤 = None. 길이 0 단계는 건너뛴다.</summary>
    public InterruptHitFlashStage StageAt(float elapsed, out float progress)
    {
        progress = 0f;
        if (elapsed < 0f || float.IsNaN(elapsed))
            return InterruptHitFlashStage.None;

        float start = 0f;
        for (int i = 1; i <= 4; i++)
        {
            float length = LengthOf((InterruptHitFlashStage)i);
            if (elapsed < start + length)
            {
                progress = (elapsed - start) / length;
                return (InterruptHitFlashStage)i;
            }

            start += length;
        }

        return InterruptHitFlashStage.None;
    }

    public float LengthOf(InterruptHitFlashStage stage)
    {
        switch (stage)
        {
            case InterruptHitFlashStage.Burst: return Burst;
            case InterruptHitFlashStage.Glare: return Glare;
            case InterruptHitFlashStage.Invert: return Invert;
            case InterruptHitFlashStage.Return: return Return;
            default: return 0f;
        }
    }

    private static float Sanitize(float value) => float.IsNaN(value) || value < 0f ? 0f : value;
}

/// <summary>섬광 순간 한 건 — 디렉터가 월드 섬광·암순응을 튼다.</summary>
public readonly struct InterruptHitFlashMoment
{
    public readonly InterruptProductionEvent Event;
    /// <summary>이 화면이 발동자 본인.</summary>
    public readonly bool ViewerIsCaster;
    /// <summary>이 화면에서 오너 섬광 오버레이(1~3단계)가 돌았다 — 본인 self 암순응을 대신한다.</summary>
    public readonly bool OwnerOverlay;

    public InterruptHitFlashMoment(in InterruptProductionEvent e, bool viewerIsCaster, bool ownerOverlay)
    {
        Event = e;
        ViewerIsCaster = viewerIsCaster;
        OwnerOverlay = ownerOverlay;
    }
}

/// <summary>오너 화면 섬광 오버레이(1~3단계 + 4단계 걷힘) 출력 — 실제 = <see cref="InterruptHitFlashOverlay"/>.</summary>
public interface IInterruptHitFlashOverlayOutput
{
    /// <param name="worldCenter">적중 지점(월드) — 출력이 화면 위치로 바꾼다.</param>
    void Show(InterruptHitFlashStage stage, float progress, Vector3 worldCenter, bool speedLines);
    void Hide();
}

/// <summary>본인/남 화면의 섬광 순간 암순응 선택.</summary>
public enum InterruptHitFlashDarkAdaptation
{
    None,
    /// <summary>본인 화면 — 오버레이가 안 돈 경우(약 예측이 성공)만.</summary>
    Self,
    /// <summary>다른 사람 화면.</summary>
    Others,
}

/// <summary>적중 섬광 순수 규칙(할 일 132 단계 5).</summary>
public static class InterruptHitFlashRules
{
    /// <summary>오버레이 덮어쓸 텍스처 칸(셰이더 속성) — 단계 1·2·3(4단계는 3단계 그림이 걷힌다).</summary>
    public static readonly string[] OverlayTextureProperties = { "_BurstTex", "_GlareTex", "_InvertTex" };
    /// <summary>위 칸이 차 있을 때 켜는 셰이더 키워드 — 꺼져 있으면 그 단계는 절차 생성.</summary>
    public static readonly string[] OverlayTextureKeywords = { "_BURST_TEX_ON", "_GLARE_TEX_ON", "_INVERT_TEX_ON" };

    /// <summary>오너 오버레이를 트는가 — 내 성공이고 그 순간 오너 암전이 켜져 있을 때(강 시도)만. 약 예측·대상 없음·실패 = 없음.</summary>
    public static bool ShouldPlayOwnerOverlay(SlowMotionPurpose purpose, bool viewerIsCaster, bool successDimmed) =>
        purpose == SlowMotionPurpose.Success && viewerIsCaster && successDimmed;

    /// <summary>
    /// 섬광 순간 암순응 — 남 화면 = others, 본인 화면 = 오버레이가 돌았으면 없음(마스크 암전·섬광이 대신 — 겹치면 두 번 어두워진다),
    /// 안 돌았으면 self(약 예측이 성공한 경우).
    /// </summary>
    public static InterruptHitFlashDarkAdaptation SelectDarkAdaptation(bool viewerIsCaster, bool ownerOverlay) =>
        !viewerIsCaster ? InterruptHitFlashDarkAdaptation.Others
        : ownerOverlay ? InterruptHitFlashDarkAdaptation.None
        : InterruptHitFlashDarkAdaptation.Self;

    /// <summary>오버레이 단계의 덮어쓸 텍스처 칸 순번(0~2). 4단계 = 3단계 칸. 없으면 -1.</summary>
    public static int OverlayTextureIndex(InterruptHitFlashStage stage)
    {
        switch (stage)
        {
            case InterruptHitFlashStage.Burst: return 0;
            case InterruptHitFlashStage.Glare: return 1;
            case InterruptHitFlashStage.Invert:
            case InterruptHitFlashStage.Return: return 2;
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
}

/// <summary>
/// 적중 섬광 진행기(할 일 132 단계 5) — 순수 로직. 디렉터가 성공 단계(purpose==Success) 안에서 사건을 넘기고 매 프레임 Tick 한다.
/// 시각(now)은 모두 <see cref="Clock"/> 시계 — 디렉터가 <see cref="InterruptEffectTime.Now"/> 로 읽어 넘긴다(할 일 132 §14).
/// <list type="bullet">
/// <item>오너 오버레이(<see cref="BeginOwnerOverlay"/>): 1~4단계를 출력에 넘기고 끝나면 숨긴다. 새 성공이 오면 처음부터.</item>
/// <item>섬광 순간(<see cref="ScheduleFlash"/>): 모든 피어가 성공 Started + 1~3단계 길이 뒤에 <c>onFlash</c> 를 부른다(월드 섬광·암순응·오너 암전 끔).
///       여러 발동자의 성공은 각각 따로 온다.</item>
/// <item>강제 리셋(<see cref="ForcedReset"/>): 오버레이 즉시 끔, 남은 섬광 순간 취소.</item>
/// </list>
/// 오너 암전 끄기는 이 진행기의 섬광 순간(<c>onFlash</c>)에 디렉터가 오너 상태 기계에 알린다 — 시간표는 이 진행기 하나다.
/// </summary>
public sealed class InterruptHitFlashSequencer
{
    private struct Pending
    {
        public float Due;
        public InterruptHitFlashMoment Moment;
    }

    private readonly IInterruptHitFlashSettings _settings;
    private readonly Action<InterruptHitFlashMoment> _onFlash;
    private readonly List<Pending> _pending = new List<Pending>();
    private readonly List<InterruptHitFlashMoment> _due = new List<InterruptHitFlashMoment>();
    private IInterruptHitFlashOverlayOutput _overlay;

    private InterruptHitFlashTimeline _overlayTimeline;
    private float _overlayStart;
    private Vector3 _overlayCenter;
    private bool _overlaySpeedLines;
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
    /// 진행기 시계 — 쉬는 동안(오버레이·남은 섬광 순간 없음)만 칸(<see cref="IInterruptHitFlashSettings.HitFlashTimeBase"/>)을 따라간다.
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

    /// <summary>오버레이 출력 교체(디버그·테스트). 돌던 오버레이는 옛 출력에서 숨긴다.</summary>
    public void SetOverlayOutput(IInterruptHitFlashOverlayOutput overlay)
    {
        _overlay?.Hide();
        _overlay = overlay;
    }

    /// <summary>[오너] 섬광 오버레이 시작 — 성공 Started 순간(<paramref name="now"/>). 칸은 이 순간 읽는다.</summary>
    public void BeginOwnerOverlay(Vector3 worldCenter, float now)
    {
        _overlayTimeline = InterruptHitFlashTimeline.From(_settings);
        _overlayStart = now;
        _overlayCenter = worldCenter;
        _overlaySpeedLines = _settings != null && _settings.HitFlashSpeedLines;
        OverlayActive = true;
        UpdateOverlay(now);
    }

    /// <summary>[각 피어] 섬광 순간 예약 — 성공 Started + 1~3단계 길이. 그 길이가 0 이면 바로 부른다.</summary>
    public void ScheduleFlash(in InterruptHitFlashMoment moment, float now)
    {
        float due = now + InterruptHitFlashTimeline.From(_settings).FlashMoment;
        if (due <= now)
        {
            _onFlash?.Invoke(moment);
            return;
        }

        _pending.Add(new Pending { Due = due, Moment = moment });
    }

    /// <summary>매 프레임(<see cref="Clock"/> 시계).</summary>
    public void Tick(float now)
    {
        if (OverlayActive)
            UpdateOverlay(now);

        if (_pending.Count == 0)
            return;

        // 부르는 동안 목록이 바뀌어도(새 예약) 안전하게 먼저 떼어 낸다.
        _due.Clear();
        for (int i = 0; i < _pending.Count; i++)
        {
            if (_pending[i].Due > now)
                continue;

            _due.Add(_pending[i].Moment);
            _pending.RemoveAt(i);
            i--;
        }

        for (int i = 0; i < _due.Count; i++)
            _onFlash?.Invoke(_due[i]);
    }

    /// <summary>강제 리셋(컷신·씬 전환·세션 종료)·디렉터 꺼짐 — 오버레이 즉시 끔, 남은 섬광 순간 취소.</summary>
    public void ForcedReset()
    {
        _pending.Clear();
        HideOverlay();
    }

    private void UpdateOverlay(float now)
    {
        InterruptHitFlashStage stage = _overlayTimeline.StageAt(now - _overlayStart, out float progress);
        if (stage == InterruptHitFlashStage.None)
        {
            HideOverlay();
            return;
        }

        OverlayStage = stage;
        OverlayProgress = progress;
        _overlay?.Show(stage, progress, _overlayCenter, _overlaySpeedLines);
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
