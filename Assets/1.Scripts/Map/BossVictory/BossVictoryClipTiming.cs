using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 보스 처치 연출 — 애니 클립 노드의 '클립 시간 ↔ 타임라인 초' 변환(255 후속 — 타임라인과 실제 시각 어긋남 수정, 테스트 <c>BossVictoryClipTimingTests</c>).
/// <para>원인: 처치 연출 스킬·보스 피격 애니 클립 노드가 길이·자른 끝·End 를 <b>클립 시간(배율 1)</b> 그대로 타임라인 초로 그렸다. 실제 재생은
/// 상태 속도 파라미터(팔라딘 R UltimateSpeed = 스킬 animationSpeed × 맞기 전/후 배율)·공격 속도·연출 배율(처치자 ×0.1·보스 노드)을 타서
/// 같은 클립 시간이 다른 초에 온다(팔라딘 R 148f 끝 자르기: 창 5.58s · 실제 2.45s).</para>
/// <para>고침: 디버그 씬 재생 때 노드 시작부터 실제 진행(타임라인 초, 클립 시간) 표본을 남기고, 창은 그 표본으로 클립 시간을 타임라인 초로 바꿔 그린다.
/// 표본이 없으면(재생 전) 배율 1 가정 — 창에 '가정' 으로 표시. 런타임 판정(자른 프레임에서 멈춤)은 그대로 클립 시간.</para>
/// 표본 = (x = 노드 시작 뒤 타임라인 초, y = 클립 시간), 클립 시간이 늘 때만 쌓아 x·y 모두 증가.
/// </summary>
public static class BossVictoryClipTiming
{
    /// <summary>노드별 표본 키 — 클립마다(캐릭터마다 다르다).</summary>
    public static string Key(bool bossHit, string clipName) => (bossHit ? "BossHit:" : "Finisher:") + clipName;

    /// <summary>표본 하나를 더한다 — 클립 시간이 늘었을 때만(멈춤·되돌림은 버림). 더했으면 true.</summary>
    public static bool Append(List<Vector2> samples, float timelineSeconds, float clipTime)
    {
        if (samples.Count > 0)
        {
            Vector2 last = samples[samples.Count - 1];
            if (clipTime <= last.y + 1e-5f || timelineSeconds <= last.x) return false;
        }
        samples.Add(new Vector2(timelineSeconds, clipTime));
        return true;
    }

    /// <summary>클립 시간 → 노드 시작 뒤 타임라인 초. 표본이 없으면 그대로(배율 1). 표본 밖은 끝 구간 배율로 늘린다(앞 = 배율 1).</summary>
    public static float ClipToTimeline(IReadOnlyList<Vector2> samples, float clipTime)
    {
        if (samples == null || samples.Count == 0) return clipTime;
        Vector2 first = samples[0];
        if (clipTime <= first.y) return first.x - (first.y - clipTime);
        for (int i = 0; i < samples.Count - 1; i++)
        {
            Vector2 a = samples[i], b = samples[i + 1];
            if (clipTime > b.y) continue;
            return Mathf.Lerp(a.x, b.x, (clipTime - a.y) / (b.y - a.y));
        }
        Vector2 last = samples[samples.Count - 1];
        return last.x + (clipTime - last.y) / LastRate(samples);
    }

    /// <summary>노드 시작 뒤 타임라인 초 → 클립 시간(<see cref="ClipToTimeline"/> 의 역).</summary>
    public static float TimelineToClip(IReadOnlyList<Vector2> samples, float timelineSeconds)
    {
        if (samples == null || samples.Count == 0) return timelineSeconds;
        Vector2 first = samples[0];
        if (timelineSeconds <= first.x) return Mathf.Max(0f, first.y - (first.x - timelineSeconds));
        for (int i = 0; i < samples.Count - 1; i++)
        {
            Vector2 a = samples[i], b = samples[i + 1];
            if (timelineSeconds > b.x) continue;
            return Mathf.Lerp(a.y, b.y, (timelineSeconds - a.x) / (b.x - a.x));
        }
        Vector2 last = samples[samples.Count - 1];
        return last.y + (timelineSeconds - last.x) * LastRate(samples);
    }

    /// <summary>끝 구간 배율(클립 초 / 타임라인 초). 표본 2개 미만이면 1.</summary>
    public static float LastRate(IReadOnlyList<Vector2> samples)
    {
        if (samples == null || samples.Count < 2) return 1f;
        Vector2 a = samples[samples.Count - 2], b = samples[samples.Count - 1];
        float dx = b.x - a.x;
        return dx > 1e-6f && b.y > a.y ? (b.y - a.y) / dx : 1f;
    }

#if UNITY_EDITOR
    // [에디터] 마지막 재생 표본(키별) — 타임라인 창이 읽는다. 다음 컴파일(도메인 리로드)까지 남는다.
    static readonly Dictionary<string, List<Vector2>> s_runs = new Dictionary<string, List<Vector2>>();
    static readonly Dictionary<string, List<Vector2>> s_recording = new Dictionary<string, List<Vector2>>();

    /// <summary>[에디터] 이번 재생 표본을 새로 시작(그 키의 이전 기록은 이번 기록이 2개 이상 쌓이면 바뀐다).</summary>
    public static void BeginRun(string key) => s_recording[key] = new List<Vector2>(256);

    /// <summary>[에디터] 이번 재생 표본 하나.</summary>
    public static void Record(string key, float timelineSeconds, float clipTime)
    {
        if (!s_recording.TryGetValue(key, out List<Vector2> list)) return;
        if (Append(list, timelineSeconds, clipTime) && list.Count >= 2) s_runs[key] = list;
    }

    /// <summary>[에디터] 마지막 재생 표본(없으면 false).</summary>
    public static bool TryGet(string key, out IReadOnlyList<Vector2> samples)
    {
        bool ok = s_runs.TryGetValue(key, out List<Vector2> list);
        samples = list;
        return ok;
    }
#endif
}
