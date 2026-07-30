using UnityEngine;
using UnityEngine.Profiling;

/// <summary>
/// 간이 성능 감지: 프레임타임 스파이크와 메모리 급증을 관측한다.
/// 세션 동안 최악값을 누적하고, 심한 스파이크는 즉시 Warning, 종료 시 요약을 남긴다.
/// (정밀 회귀 분석은 2단계에서 profiler(profile-analyzer) 연계로 강화)
/// </summary>
public sealed class PerfDetector : IQADetector
{
    public string Name => "Perf";

    private const float SevereSpikeSeconds = 0.25f;   // 250ms(=4fps 순간) 이상이면 즉시 보고
    private const float SpikeSeconds = 0.1f;          // 100ms(=10fps) 이상이면 스파이크 카운트
    private const float ReportCooldown = 10f;
    private const long MemGrowthWarnBytes = 512L * 1024 * 1024; // 시작 대비 +512MB

    private float _worstFrame;
    private int _spikeCount;
    private int _frameCount;
    private double _accumFrame;
    private float _lastReportTime = -999f;
    private long _startMem;
    private long _peakMem;
    private bool _memWarned;

    public void OnSessionStart(QARecorder recorder)
    {
        _worstFrame = 0f;
        _spikeCount = 0;
        _frameCount = 0;
        _accumFrame = 0;
        _startMem = Profiler.GetTotalAllocatedMemoryLong();
        _peakMem = _startMem;
        _memWarned = false;
    }

    public void Tick(QARecorder recorder, float deltaTime)
    {
        float frame = Time.unscaledDeltaTime;
        _frameCount++;
        _accumFrame += frame;
        if (frame > _worstFrame)
            _worstFrame = frame;

        if (frame >= SpikeSeconds)
            _spikeCount++;

        if (frame >= SevereSpikeSeconds && (Time.time - _lastReportTime) >= ReportCooldown)
        {
            _lastReportTime = Time.time;
            recorder.Add(QASeverity.Warning, Name,
                $"프레임 스파이크 {frame * 1000f:F0}ms (~{1f / Mathf.Max(frame, 0.0001f):F1}fps)");
        }

        long mem = Profiler.GetTotalAllocatedMemoryLong();
        if (mem > _peakMem)
            _peakMem = mem;

        if (!_memWarned && mem - _startMem >= MemGrowthWarnBytes)
        {
            _memWarned = true;
            recorder.Add(QASeverity.Warning, Name,
                $"메모리 급증: 시작 {_startMem / (1024 * 1024)}MB → 현재 {mem / (1024 * 1024)}MB");
        }
    }

    public void OnSessionEnd(QARecorder recorder)
    {
        float avgMs = _frameCount > 0 ? (float)(_accumFrame / _frameCount) * 1000f : 0f;
        recorder.Add(QASeverity.Info, Name,
            $"성능 요약: 평균 {avgMs:F1}ms, 최악 {_worstFrame * 1000f:F0}ms, 스파이크(≥{SpikeSeconds * 1000f:F0}ms) {_spikeCount}회, 피크메모리 {_peakMem / (1024 * 1024)}MB");
    }
}
