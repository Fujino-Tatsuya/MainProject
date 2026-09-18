// ----------------------------------------------------------------------------
//  RenderCostAB.cs — 가림 처리 두 방식의 비용을 같은 자리에서 재기 위한 측정기 (개발용)
//
//  재려는 것: 「구 투명화(디더 클립)」 vs 「신 실루엣(윤곽선)」 의 프레임 비용 차이.
//
//  🔴 왜 ProfilerHUD 를 안 쓰고 콘솔로 찍는가: HUD 는 IMGUI 라 게임뷰 스크린샷에 안 잡힌다.
//     사람이 눈으로 읽을 때는 HUD 가 낫지만, 자동 측정은 숫자를 **텍스트로** 남겨야
//     읽는 쪽이 픽셀을 해석하지 않는다. 읽는 값은 HUD 와 같은 ProfilerRecorder 다.
//
//  🔴 왜 한 번의 Play 로 구/신을 왕복할 수 없는가(이게 이 파일의 존재 이유다):
//     구 시스템은 렌더 패스가 아니라 **머티리얼 교체**다. WallOcclusionDriver 가 OnEnable 에서
//     벽 머티리얼을 오클루전 변종으로 바꾸는데 **되돌리는 코드가 없다.**
//     그래서 Play 중에 껐다 켜면 "껐다"가 원래 상태로 돌아가지 않아 비교가 오염된다.
//     구 시스템은 반드시 **Play 시작 시점에** 정해야 한다 → Play 를 두 번 돌린다.
//
//  자동 측정 순서 (Play 시작하면 알아서 돈다)
//     워밍업 → [상태 A 측정] → 실루엣 토글 → 워밍업 → [상태 B 측정] → 요약 1줄
//
//  측정 계획
//     Play 1 : startWithWallOcclusion = false → 실루엣 ON/OFF 차이 = 신 실루엣 비용
//     Play 2 : startWithWallOcclusion = true  → 같은 두 상태를 구 투명화가 켜진 채로
//              구 투명화 비용 = (Play2 의 실루엣 OFF) − (Play1 의 실루엣 OFF)
//
//  ⚠️ 이 컴포넌트는 결과물이 아니라 판정 도구다(LookToggle 과 같은 부류).
//     숫자가 나오면 이 스크립트와 씬 오브젝트를 지우는 것이 마무리다.
// ----------------------------------------------------------------------------
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Text;
using Unity.Profiling;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[DisallowMultipleComponent]
public sealed class RenderCostAB : MonoBehaviour
{
#if ENABLE_INPUT_SYSTEM
    // F8 = ProfilerHUD, F9 = LookToggle, F10 = 디버그 부활. F7 이 비어 있다.
    [Tooltip("실루엣 ON/OFF 수동 토글 키. 자동 측정 중에는 누르지 말 것.")]
    [SerializeField] private Key toggleKey = Key.F7;
#endif

    [Header("시작 상태")]
    [Tooltip("Play 시작 시 실루엣을 켤지.")]
    [SerializeField] private bool startWithSilhouette = true;

    [Tooltip("Play 시작 시 구 투명화(WallOcclusionDriver)를 켤지. " +
             "🔴 Play 중에는 바꿀 수 없다 — 머티리얼 교체가 되돌아오지 않기 때문이다.")]
    [SerializeField] private bool startWithWallOcclusion;

    [Header("자동 측정")]
    [Tooltip("켜면 Play 시작 후 알아서 두 상태를 재고 콘솔에 요약을 찍는다. "
             + "🔴 기본값이 꺼짐인 이유: 이 측정은 실루엣을 껐다 켰다 한다. 켜 둔 채로 잊으면 "
             + "게임이 '한동안 되다가 안 되는' 상태가 된다 — 실제로 MPPM 검증 중에 그렇게 새어나갔다. "
             + "잴 때만 켠다.")]
    [SerializeField] private bool autoMeasure;

    [Tooltip("측정 전에 버리는 프레임 수. 맵 생성·셰이더 워밍업·GC 가 섞이지 않게 넉넉히 둔다.")]
    [SerializeField] private int warmupFrames = 180;

    [Tooltip("한 구간에서 평균낼 프레임 수.")]
    [SerializeField] private int sampleFrames = 120;

    [Tooltip("A/B 를 몇 번 왕복할지. 🔴 1 이면 안 된다 — 두 구간 사이에 몬스터가 움직이고 "
             + "스폰되므로 그 드리프트가 통째로 '차이'로 잡힌다. 교대하면 드리프트가 상쇄된다.")]
    [SerializeField] private int alternations = 4;

    [Tooltip("교대 사이에 버리는 프레임 수(짧은 재워밍업).")]
    [SerializeField] private int switchSettleFrames = 30;

    private WallOcclusionDriver _driver;

    private ProfilerRecorder _mainThread;
    private ProfilerRecorder _drawCalls;
    private ProfilerRecorder _setPass;
    private ProfilerRecorder _tris;
    private ProfilerRecorder _maskPass;
    private ProfilerRecorder _compositePass;

    private readonly FrameTiming[] _frameTimings = new FrameTiming[1];

    private enum Phase { Warmup, Sample, Done }
    private Phase _phase = Phase.Warmup;
    private int _phaseFrames;
    private int _stateIndex;            // 0 = 시작 상태, 1 = 토글한 상태

    private Sample _acc;
    private readonly Sample[] _totals = new Sample[2];   // 상태별 누적(교대 전부 합산)
    private readonly string[] _labels = new string[2];
    private int _segment;                                 // 지금까지 끝낸 구간 수

    private struct Sample
    {
        public double cpuMs, gpuMs, mainMs, maskMs, compositeMs;
        // 🔴 렌더 스레드를 따로 재야 하는 이유: cpuFrameTime 은 "CPU 가 한 일"이 아니라
        //    프레임 간격(= 1/fps)이다. 메인 스레드가 병목이면 렌더 스레드가 아무리 늘어도
        //    프레임 간격은 안 변한다 — 드로우콜이 1000개 늘어도 "공짜"로 보인다.
        public double cpuMainMs, cpuRenderMs;
        public double draws, setPass, tris;
        public int n;
        // 🔴 프레임 타이밍은 매 프레임 나오지 않는다(몇 프레임 지연되어 채워진다).
        //    그런 프레임까지 n 으로 나누면 0 을 평균에 섞어 **시간이 실제보다 작게 나온다**.
        //    그래서 타이밍 전용 분모를 따로 센다.
        public int timingN;

        public void Reset() => this = default;

        public Sample Average()
        {
            if (n <= 0) return this;
            var a = this;
            int t = timingN > 0 ? timingN : n;
            a.cpuMs /= t; a.gpuMs /= t; a.cpuMainMs /= t; a.cpuRenderMs /= t;
            a.mainMs /= n;
            a.maskMs /= n; a.compositeMs /= n;
            a.draws /= n; a.setPass /= n; a.tris /= n;
            return a;
        }
    }

    private void OnEnable()
    {
        _mainThread = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 15);
        _drawCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
        _setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
        _tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");

        // 🔴 마커 이름이 RenderGraph 패스 이름과 한 글자라도 다르면 조용히 0.00 이 찍힌다.
        //    "이 패스는 공짜"로 읽히는 거짓 신호라 특히 위험하다 — 아래 상수는
        //    PlayerSilhouetteFeature.PassNames 에서 그대로 가져온다.
        _maskPass = ProfilerRecorder.StartNew(
            ProfilerCategory.Render, PlayerSilhouetteFeature.PassNames.Mask, 15);
        _compositePass = ProfilerRecorder.StartNew(
            ProfilerCategory.Render, PlayerSilhouetteFeature.PassNames.Composite, 15);
    }

    private void OnDisable()
    {
        _mainThread.Dispose();
        _drawCalls.Dispose();
        _setPass.Dispose();
        _tris.Dispose();
        _maskPass.Dispose();
        _compositePass.Dispose();
    }

    private void Start()
    {
        PlayerSilhouetteFeature.DevEnabled = startWithSilhouette;

        _driver = FindFirstObjectByType<WallOcclusionDriver>(FindObjectsInactive.Include);
        if (_driver != null)
            _driver.enabled = startWithWallOcclusion;

        _acc.Reset();
        _phase = Phase.Warmup;
        _phaseFrames = 0;
        _stateIndex = 0;

        Debug.Log($"[RenderCostAB] 시작 — {StateLabel()}. " +
                  (autoMeasure
                      ? $"자동 측정: 워밍업 {warmupFrames}프레임 → {sampleFrames}프레임 평균 × 2상태."
                      : "자동 측정 꺼짐. F7 로 수동 토글."), this);
    }

    private void OnDestroy()
    {
        // 정적 필드는 Play 를 나가도 남는다(도메인 리로드를 끈 경우). 원상복구해 둔다.
        PlayerSilhouetteFeature.DevEnabled = true;
    }

    private void Update()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current[toggleKey].wasPressedThisFrame)
        {
            PlayerSilhouetteFeature.DevEnabled = !PlayerSilhouetteFeature.DevEnabled;
            Debug.Log($"[RenderCostAB] 수동 토글 — {StateLabel()}", this);
        }
#endif
        if (!autoMeasure || _phase == Phase.Done) return;

        _phaseFrames++;

        if (_phase == Phase.Warmup)
        {
            int need = _segment == 0 ? warmupFrames : switchSettleFrames;
            if (_phaseFrames < need) return;
            _phase = Phase.Sample;
            _phaseFrames = 0;
            _acc.Reset();
            return;
        }

        Accumulate();

        if (_phaseFrames < sampleFrames) return;

        Sample seg = _acc.Average();
        _labels[_stateIndex] = StateLabel();
        Accumulate(ref _totals[_stateIndex], seg);
        LogSample($"{_labels[_stateIndex]} #{_segment / 2 + 1}", seg);

        _segment++;

        if (_segment < alternations * 2)
        {
            // 실루엣만 뒤집는다(구 투명화는 Play 중에 못 건드린다).
            PlayerSilhouetteFeature.DevEnabled = !PlayerSilhouetteFeature.DevEnabled;
            _stateIndex = 1 - _stateIndex;
            _phase = Phase.Warmup;
            _phaseFrames = 0;
            return;
        }

        LogSummary();
        _phase = Phase.Done;

        // 🔴 측정이 끝나면 반드시 시작 상태로 되돌린다.
        //    마지막 구간이 OFF 라서 그대로 두면 실루엣이 꺼진 채 게임이 계속된다.
        //    "처음엔 되는데 조금 뒤부터 안 되는" 증상으로 나타나고, 원인을 찾기가 매우 어렵다.
        if (PlayerSilhouetteFeature.DevEnabled != startWithSilhouette)
        {
            PlayerSilhouetteFeature.DevEnabled = startWithSilhouette;
            Debug.Log($"[RenderCostAB] 측정 종료 — 실루엣을 시작 상태로 복구했다 " +
                      $"({(startWithSilhouette ? "켜짐" : "꺼짐")}).", this);
        }
    }

    private void Accumulate()
    {
        FrameTimingManager.CaptureFrameTimings();
        if (FrameTimingManager.GetLatestTimings(1, _frameTimings) > 0)
        {
            _acc.cpuMs += _frameTimings[0].cpuFrameTime;
            _acc.gpuMs += _frameTimings[0].gpuFrameTime;
            _acc.cpuMainMs += _frameTimings[0].cpuMainThreadFrameTime;
            _acc.cpuRenderMs += _frameTimings[0].cpuRenderThreadFrameTime;
            _acc.timingN++;
        }

        _acc.mainMs += Average(_mainThread) * 1e-6;
        _acc.maskMs += Average(_maskPass) * 1e-6;
        _acc.compositeMs += Average(_compositePass) * 1e-6;

        _acc.draws += _drawCalls.LastValue;
        _acc.setPass += _setPass.LastValue;
        _acc.tris += _tris.LastValue;
        _acc.n++;
    }

    // 구간 평균을 상태별 총합에 더한다(교대 횟수만큼 쌓였다가 마지막에 다시 평균).
    private static void Accumulate(ref Sample total, in Sample seg)
    {
        total.cpuMs += seg.cpuMs; total.gpuMs += seg.gpuMs; total.mainMs += seg.mainMs;
        total.cpuMainMs += seg.cpuMainMs; total.cpuRenderMs += seg.cpuRenderMs;
        total.maskMs += seg.maskMs; total.compositeMs += seg.compositeMs;
        total.draws += seg.draws; total.setPass += seg.setPass; total.tris += seg.tris;
        total.n++; total.timingN++;   // 구간 평균끼리의 합산이므로 분모가 같다
    }

    private static double Average(ProfilerRecorder rec)
    {
        if (!rec.Valid || rec.Capacity == 0) return 0;
        int count = rec.Count;
        if (count == 0) return 0;

        double sum = 0;
        for (int i = 0; i < count; i++)
            sum += rec.GetSample(i).Value;
        return sum / count;
    }

    private string StateLabel()
    {
        string wall = _driver == null
            ? "드라이버없음"
            : (_driver.enabled ? "구투명화ON" : "구투명화OFF");
        return $"{(PlayerSilhouetteFeature.DevEnabled ? "실루엣ON" : "실루엣OFF")} / {wall}";
    }

    private void LogSample(string label, Sample s)
    {
        Debug.Log(
            $"[RenderCostAB] 측정 [{label}] n={s.n}\n" +
            $"  CPU {s.cpuMs:F3} ms   GPU {s.gpuMs:F3} ms   Main {s.mainMs:F3} ms\n" +
            $"  Draw {s.draws:F1}   SetPass {s.setPass:F1}   Tris {s.tris:F0}\n" +
            $"  패스: Mask {s.maskMs:F4} ms   Composite {s.compositeMs:F4} ms", this);
    }

    private void LogSummary()
    {
        Sample a = _totals[0].Average(), b = _totals[1].Average();
        var sb = new StringBuilder(512);
        sb.AppendLine("[RenderCostAB] ===== 요약 =====");
        sb.AppendLine($"  A = {_labels[0]}");
        sb.AppendLine($"  B = {_labels[1]}");
        sb.AppendLine($"  Δ프레임 {b.cpuMs - a.cpuMs:+0.000;-0.000;0.000} ms  " +
                      $"ΔGPU {b.gpuMs - a.gpuMs:+0.000;-0.000;0.000} ms");
        sb.AppendLine($"  ΔCPU메인 {b.cpuMainMs - a.cpuMainMs:+0.000;-0.000;0.000} ms  " +
                      $"ΔCPU렌더 {b.cpuRenderMs - a.cpuRenderMs:+0.000;-0.000;0.000} ms");
        sb.AppendLine($"  ΔDraw {b.draws - a.draws:+0.0;-0.0;0.0}  " +
                      $"ΔSetPass {b.setPass - a.setPass:+0.0;-0.0;0.0}  " +
                      $"ΔTris {b.tris - a.tris:+0;-0;0}");
        sb.AppendLine($"  A 패스: Mask {a.maskMs:F4} / Composite {a.compositeMs:F4} ms");
        sb.AppendLine($"  B 패스: Mask {b.maskMs:F4} / Composite {b.compositeMs:F4} ms");
        sb.Append("  ⚠️ B − A 다. 부호를 뒤집어 읽지 말 것.");
        Debug.Log(sb.ToString(), this);
    }
}
#endif
