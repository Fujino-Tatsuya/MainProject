using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 제압 간파 섬광의 한 렌더 프레임 모양(할 일 179) — <see cref="SuppressFlashRules.Evaluate"/> 가 컷 데이터·경과 시간·섬광 줄이기로 만든다.
/// 출력(<see cref="ISuppressFlashOutput"/>)은 이 값만 그린다 — 시간표는 모른다.
/// </summary>
public readonly struct SuppressFlashFrame
{
    public static readonly SuppressFlashFrame Inactive = default;

    public readonly bool Active;
    public readonly int CutIndex;
    /// <summary>컷 데이터의 명암(섬광 줄이기와 무관).</summary>
    public readonly SuppressFlashTone Tone;
    /// <summary>3단 뒤집기 — 포지티브 컷이고 섬광 줄이기 꺼짐일 때만.</summary>
    public readonly bool Invert;
    public readonly SuppressFlashCross Cross;
    public readonly float StarSize;
    public readonly float SpeedLines;
    public readonly float Aberration;
    public readonly bool Hatching;
    public readonly float Letterbox;
    /// <summary>원래 색으로 돌아간 정도 [0, 1] — 복귀 컷만 양수.</summary>
    public readonly float ReturnBlend;
    /// <summary>3단 색 대비 [0, 1] — 1 = 그대로, 섬광 줄이기면 시퀀스의 낮춘 대비.</summary>
    public readonly float Contrast;
    public readonly Color Dark;
    public readonly Color Mid;
    public readonly Color Light;

    public SuppressFlashFrame(int cutIndex, SuppressFlashTone tone, bool invert, SuppressFlashCross cross, float starSize,
        float speedLines, float aberration, bool hatching, float letterbox, float returnBlend, float contrast,
        Color dark, Color mid, Color light)
    {
        Active = true;
        CutIndex = cutIndex;
        Tone = tone;
        Invert = invert;
        Cross = cross;
        StarSize = starSize;
        SpeedLines = speedLines;
        Aberration = aberration;
        Hatching = hatching;
        Letterbox = letterbox;
        ReturnBlend = returnBlend;
        Contrast = contrast;
        Dark = dark;
        Mid = mid;
        Light = light;
    }
}

/// <summary>
/// 제압 간파 섬광 시간표(할 일 179) — 순수 함수. 경과 시간(실제 시간, 초) → 프레임(× 프레임 속도) → 컷·컷 안 홀드 계단.
/// 컷 데이터는 부를 때마다 읽는다(캐싱 없음).
/// </summary>
public static class SuppressFlashRules
{
    // 1/24 초 경계에서 부동소수 오차로 한 프레임 늦게 넘어가지 않게.
    private const float FrameEpsilon = 1e-4f;

    /// <summary>
    /// 디렉터 '제압 간파' 프로필 선택 — 간파 성공이고 제압 간파 표식이 있고 칸이 켜져 있고 컷 데이터가 있으면 일반 성공 섬광(②~⑥) 대신 이 시퀀스.
    /// 아니면 일반 간파 그대로.
    /// </summary>
    public static bool ShouldPlay(SlowMotionPurpose purpose, bool suppressInterrupt, bool enabled, bool hasSequence) =>
        purpose == SlowMotionPurpose.Success && suppressInterrupt && enabled && hasSequence;

    public static int TotalFrames(IReadOnlyList<SuppressFlashCut> cuts)
    {
        if (cuts == null)
            return 0;

        int total = 0;
        for (int i = 0; i < cuts.Count; i++)
            total += FramesOf(cuts[i]);
        return total;
    }

    /// <summary>컷 길이 — 빈 칸·0 이하는 1 프레임으로 본다(저작 실수의 안전망). null 컷은 0.</summary>
    public static int FramesOf(SuppressFlashCut cut) => cut != null ? Mathf.Max(1, cut.frames) : 0;

    /// <summary>경과 실제 시간(초) → 경과 프레임. 음수는 0.</summary>
    public static int ElapsedFrames(float elapsedSeconds, float frameRate)
    {
        if (elapsedSeconds <= 0f || float.IsNaN(elapsedSeconds))
            return 0;
        return Mathf.FloorToInt(elapsedSeconds * Mathf.Max(1f, frameRate) + FrameEpsilon);
    }

    /// <summary>경과 프레임이 든 컷과 컷 안 프레임. 끝을 지났으면 false.</summary>
    public static bool TryLocate(IReadOnlyList<SuppressFlashCut> cuts, int frame, out int cutIndex, out int frameInCut)
    {
        cutIndex = -1;
        frameInCut = 0;
        if (cuts == null || frame < 0)
            return false;

        int start = 0;
        for (int i = 0; i < cuts.Count; i++)
        {
            int length = FramesOf(cuts[i]);
            if (length > 0 && frame < start + length)
            {
                cutIndex = i;
                frameInCut = frame - start;
                return true;
            }

            start += length;
        }

        return false;
    }

    /// <summary>홀드 계단 — 컷 안 프레임을 홀드 단위 시작 프레임으로 내린다(2프레임 홀드면 0,0,2,2,4…).</summary>
    public static int SteppedFrame(int frameInCut, int holdFrames)
    {
        int hold = Mathf.Max(1, holdFrames);
        return frameInCut - frameInCut % hold;
    }

    /// <summary>컷 안 진행도 [0, 1) — 홀드 계단 프레임 / 컷 길이. 시작→끝 칸과 복귀 정도가 이 값을 쓴다.</summary>
    public static float CutProgress(int frameInCut, int cutFrames, int holdFrames) =>
        cutFrames <= 0 ? 0f : Mathf.Clamp01(SteppedFrame(frameInCut, holdFrames) / (float)cutFrames);

    /// <summary>
    /// 제압 암순응 컷(할 일 192 Q7) — 시퀀스가 끝난 뒤 경과 실제 시간(초)이 든 컷과 그 어둡힘. 끝을 지났거나 컷이 없으면 false(= 원래 색).
    /// 컷 길이 0 이하는 0.01초로 본다(저작 실수의 안전망). null 컷은 건너뛴다.
    /// </summary>
    public static bool TryEvaluateDarkAdapt(IReadOnlyList<SuppressDarkAdaptCut> cuts, float elapsedSeconds, out int cutIndex, out float darkness)
    {
        cutIndex = -1;
        darkness = 0f;
        if (cuts == null || float.IsNaN(elapsedSeconds) || elapsedSeconds < 0f)
            return false;

        float start = 0f;
        for (int i = 0; i < cuts.Count; i++)
        {
            SuppressDarkAdaptCut cut = cuts[i];
            if (cut == null)
                continue;

            float end = start + Mathf.Max(0.01f, cut.duration);
            if (elapsedSeconds < end)
            {
                cutIndex = i;
                darkness = Mathf.Clamp01(cut.darkness);
                return true;
            }

            start = end;
        }

        return false;
    }

    /// <summary>
    /// 경과 실제 시간의 모양. 끝났거나 컷이 없으면 <see cref="SuppressFlashFrame.Inactive"/>.
    /// 섬광 줄이기 = 반전 끔(포지티브도 뒤집지 않음) + 대비를 <see cref="SuppressFlashSequence.reducedContrast"/> 로.
    /// </summary>
    public static SuppressFlashFrame Evaluate(SuppressFlashSequence sequence, float elapsedSeconds, bool reduceFlash)
    {
        if (sequence == null)
            return SuppressFlashFrame.Inactive;

        int frame = ElapsedFrames(elapsedSeconds, sequence.frameRate);
        if (!TryLocate(sequence.cuts, frame, out int cutIndex, out int frameInCut))
            return SuppressFlashFrame.Inactive;

        SuppressFlashCut cut = sequence.cuts[cutIndex];
        float progress = CutProgress(frameInCut, FramesOf(cut), sequence.holdFrames);
        bool isReturn = cut.tone == SuppressFlashTone.Return;

        return new SuppressFlashFrame(
            cutIndex,
            cut.tone,
            !reduceFlash && cut.tone == SuppressFlashTone.Positive,
            cut.cross,
            Mathf.Max(0f, cut.starSize),
            Mathf.Clamp01(cut.speedLines),
            Mathf.Max(0f, Mathf.Lerp(cut.aberration, cut.aberrationEnd, progress)),
            cut.hatching,
            Mathf.Clamp01(Mathf.Lerp(cut.letterbox, cut.letterboxEnd, progress)),
            isReturn ? progress : 0f,
            reduceFlash ? Mathf.Clamp01(sequence.reducedContrast) : 1f,
            cut.dark, cut.mid, cut.light);
    }
}

/// <summary>
/// [서버] 제압 간파 표식(할 일 179) — 대상(23호)이 간파 성공으로 제압 칸 0 에 닿은 순간 <see cref="Mark"/>, 같은 판정 호출의 성공 사건이 <see cref="Consume"/>.
/// 대상 수신과 성공 사건은 같은 프레임 같은 호출 안이라 프레임 번호로 묶는다 — 다른 프레임에 남은 표식은 버린다(판정 코드 무수정).
/// </summary>
public sealed class SuppressInterruptMark
{
    private int _frame = int.MinValue;

    public void Mark(int frame) => _frame = frame;

    /// <summary>이 프레임에 표식이 있었는가 — 읽으면 비운다.</summary>
    public bool Consume(int frame)
    {
        bool marked = _frame == frame;
        _frame = int.MinValue;
        return marked;
    }
}

/// <summary>제압 간파 섬광 화면 출력(할 일 179) — 렌더 출력은 <see cref="SuppressFlashScreenOutput"/>, 테스트는 가짜.</summary>
public interface ISuppressFlashOutput
{
    /// <summary>
    /// 실루엣 대상(오너·간파 대상, 191 이후 동시 성공자) — 재생 시작에 한 번 넘긴다. 목록은 그 순간 복사한다.
    /// </summary>
    void SetSubjects(IReadOnlyList<GameObject> subjects);

    /// <summary>이 렌더 프레임 모양. <paramref name="worldCenter"/> = 적중점(각 화면이 자기 카메라로 화면 좌표를 구한다).</summary>
    void Show(in SuppressFlashFrame frame, Vector3 worldCenter, SuppressFlashSequence sequence);

    /// <summary>끝·강제 리셋 — 즉시 원복(실루엣 비트 포함).</summary>
    void Hide();
}

/// <summary>
/// 제압 암순응 출력(할 일 192 Q7) — 렌더 출력은 <see cref="InterruptDarkAdaptation"/>, 테스트는 가짜. 모든 피어 같은 값(Q8 공용).
/// </summary>
public interface ISuppressDarkAdaptOutput
{
    /// <summary>컷 방식 — 이 어둡힘을 그대로 붙잡는다(곡선 없음). 컷이 바뀔 때마다 부른다.</summary>
    void ShowDarkAdaptCut(float darkness, ulong casterClientId, Vector3 targetPosition);

    /// <summary>부드럽게 방식 — 기존 암순응 곡선을 한 번 튼다(본인 강도, 모든 화면).</summary>
    void PlayDarkAdaptSmooth(ulong casterClientId, Vector3 targetPosition);

    /// <summary>즉시 0(컷 끝·강제 리셋).</summary>
    void ClearDarkAdapt();
}

/// <summary>
/// 제압 간파 섬광 진행기(할 일 179) — 순수 로직(MonoBehaviour 아님). 디렉터가 공용 성공 사건에서 <see cref="Begin"/>, 매 프레임 실제 시간으로 <see cref="Tick"/>.
/// 컷 데이터(어느 SO 인가)는 <see cref="Begin"/> 순간 공급자에서 한 번 골라 끝까지 쓴다(재생 중에 칸을 바꿔도 다음 성공부터).
/// 그 SO 의 값·섬광 줄이기는 Tick 마다 읽는다 — Play 중 SO 값을 바꾸면 다음 렌더 프레임부터 그 값.
/// 새 성공이 오면 처음부터 다시 튼다.
/// 시퀀스가 끝나면 제압 암순응(할 일 192 Q7)으로 이어진다 — 컷 = 그 SO 의 암순응 컷을 실제 시간으로 끊어서, 부드럽게 = 기존 암순응 곡선 한 번.
/// 방식은 <see cref="Begin"/> 순간 받은 값을 끝까지 쓴다.
/// </summary>
public sealed class SuppressFlashPlayer
{
    private readonly Func<SuppressFlashSequence> _sequence;
    private readonly Func<bool> _reduceFlash;
    private ISuppressFlashOutput _output;
    private ISuppressDarkAdaptOutput _darkAdaptOutput;
    private SuppressFlashSequence _playing;
    private float _startTime;
    private Vector3 _worldCenter;
    private SuppressDarkAdaptMode _darkAdaptMode;
    private ulong _casterClientId;
    private Vector3 _targetPosition;
    private float _darkAdaptStart;

    public SuppressFlashPlayer(Func<SuppressFlashSequence> sequence, Func<bool> reduceFlash, ISuppressFlashOutput output,
        ISuppressDarkAdaptOutput darkAdaptOutput = null)
    {
        _sequence = sequence;
        _reduceFlash = reduceFlash;
        _output = output;
        _darkAdaptOutput = darkAdaptOutput;
    }

    /// <summary>섬광 시퀀스가 도는 중(암순응 컷 제외 — A5 전투 표시 숨김·179 테스트가 보는 구간).</summary>
    public bool IsPlaying { get; private set; }

    /// <summary>제압 암순응 컷이 도는 중(할 일 192 — 시퀀스 끝 뒤, 컷 방식만).</summary>
    public bool DarkAdaptActive { get; private set; }

    /// <summary>지금 암순응 컷 번호(없으면 -1).</summary>
    public int DarkAdaptCutIndex { get; private set; } = -1;

    /// <summary>마지막 재생의 암순응 방식.</summary>
    public SuppressDarkAdaptMode DarkAdaptMode => _darkAdaptMode;

    /// <summary>암순응 출력 교체(테스트·디버그). 돌던 컷은 옛 출력에서 걷는다.</summary>
    public void SetDarkAdaptOutput(ISuppressDarkAdaptOutput output)
    {
        if (DarkAdaptActive)
            _darkAdaptOutput?.ClearDarkAdapt();
        _darkAdaptOutput = output;
    }
    /// <summary>지금(마지막) 재생에 고른 컷 데이터.</summary>
    public SuppressFlashSequence Playing => _playing;
    /// <summary>마지막으로 출력에 넘긴 모양(멈춤 = 비활성).</summary>
    public SuppressFlashFrame Current { get; private set; }

    /// <summary>출력 교체(테스트·디버그). 돌던 효과는 옛 출력에서 걷는다.</summary>
    public void SetOutput(ISuppressFlashOutput output)
    {
        _output?.Hide();
        _output = output;
    }

    /// <summary>
    /// 재생 시작 — <paramref name="now"/> = 실제 시간(Time.unscaledTime). 컷 데이터가 없으면 false(아무것도 안 그림).
    /// <paramref name="darkAdaptMode"/>·<paramref name="casterClientId"/>·<paramref name="targetPosition"/> = 시퀀스 뒤 제압 암순응(할 일 192).
    /// </summary>
    public bool Begin(float now, Vector3 worldCenter, IReadOnlyList<GameObject> subjects,
        SuppressDarkAdaptMode darkAdaptMode = SuppressDarkAdaptMode.Cut, ulong casterClientId = 0, Vector3 targetPosition = default)
    {
        SuppressFlashSequence sequence = _sequence?.Invoke();
        if (sequence == null)
            return false;

        StopDarkAdapt();
        _playing = sequence;
        _startTime = now;
        _worldCenter = worldCenter;
        _darkAdaptMode = darkAdaptMode;
        _casterClientId = casterClientId;
        _targetPosition = targetPosition;
        IsPlaying = true;
        _output?.SetSubjects(subjects);
        Tick(now);
        return IsPlaying || DarkAdaptActive;
    }

    /// <summary>매 프레임 — 실제 시간. 시퀀스 끝을 지나면 출력을 걷고 제압 암순응으로, 암순응 컷 끝이면 원래 색.</summary>
    public void Tick(float now)
    {
        if (IsPlaying)
        {
            SuppressFlashSequence sequence = _playing;
            bool reduce = _reduceFlash != null && _reduceFlash();
            SuppressFlashFrame frame = SuppressFlashRules.Evaluate(sequence, now - _startTime, reduce);
            if (frame.Active)
            {
                Current = frame;
                _output?.Show(frame, _worldCenter, sequence);
                return;
            }

            // 시퀀스 끝 = 복귀 컷 끝 → 제압 암순응. 끝난 순간 시각은 프레임 수로 정확히 잡는다(프레임 지연과 무관하게 컷 길이 그대로).
            float sequenceEnd = _startTime + SuppressFlashRules.TotalFrames(sequence != null ? sequence.cuts : null) /
                Mathf.Max(1f, sequence != null ? sequence.frameRate : 1f);
            EndFlash();
            BeginDarkAdapt(Mathf.Min(now, sequenceEnd));
        }

        if (DarkAdaptActive)
            TickDarkAdapt(now);
    }

    /// <summary>즉시 멈춤(끝·강제 리셋·디렉터 꺼짐) — 암순응 컷도 걷는다.</summary>
    public void Stop()
    {
        EndFlash();
        StopDarkAdapt();
    }

    private void EndFlash()
    {
        bool was = IsPlaying || Current.Active;
        IsPlaying = false;
        Current = SuppressFlashFrame.Inactive;
        if (was)
            _output?.Hide();
    }

    private void BeginDarkAdapt(float start)
    {
        if (_darkAdaptMode == SuppressDarkAdaptMode.Smooth)
        {
            _darkAdaptOutput?.PlayDarkAdaptSmooth(_casterClientId, _targetPosition);
            return;
        }

        _darkAdaptStart = start;
        DarkAdaptActive = true;
        DarkAdaptCutIndex = -1;
    }

    private void TickDarkAdapt(float now)
    {
        List<SuppressDarkAdaptCut> cuts = _playing != null ? _playing.darkAdaptCuts : null;
        if (!SuppressFlashRules.TryEvaluateDarkAdapt(cuts, now - _darkAdaptStart, out int index, out float darkness))
        {
            StopDarkAdapt();
            return;
        }

        if (index == DarkAdaptCutIndex)
            return;

        DarkAdaptCutIndex = index;
        _darkAdaptOutput?.ShowDarkAdaptCut(darkness, _casterClientId, _targetPosition);
    }

    private void StopDarkAdapt()
    {
        if (!DarkAdaptActive)
            return;

        DarkAdaptActive = false;
        DarkAdaptCutIndex = -1;
        _darkAdaptOutput?.ClearDarkAdapt();
    }
}
