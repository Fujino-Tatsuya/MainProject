using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>보스 처치 연출 타임라인 트랙(PLAN-boss-victory-glitch "타임라인 에디터 창"). 순서 = 창 위→아래.</summary>
public enum BossVictoryTrack
{
    Master,      // 전체 세기(옛 master 곡선 — 모든 화면 효과에 곱한다)
    Desaturate,  // 흑백
    Amber,       // 노란 틴트(배경만)
    Scanline,    // CRT 주사선
    BossNoise,   // 보스 노이즈
    Tear,        // 가로 찢김(단발 펄스)
    Smear,       // 밝은 부분 끌림(단발 펄스)
    HudHide,     // HUD 숨김(0.5 초과)
    Hold,        // 정지 유지 — 클립 동안 화면 효과 시각을 클립 시작에 고정
    CrtOff,      // TV 꺼짐 — 첫 클립만, 시작에 화면 캡처·길이 = 꺼짐 길이
}

/// <summary>보스 처치 연출 타임라인 순간 마커.</summary>
public enum BossVictoryMarker
{
    FocusKiller,      // 처치자가 아닌 피어 카메라 → 처치자(즉시 컷)
    HideOthers,       // 처치자 외 플레이어 캐릭터 숨김(모든 피어)
    LobbyTransition,  // 로비 전환 — 호스트 화면이 지나면 서버가 결과로 넘긴다
}

/// <summary>클립 한 개 — 시작·길이·페이드 인/아웃·세기(초, 처치 판정 뒤).</summary>
[Serializable]
public struct BossVictoryClip
{
    [Min(0f)] public float start;
    [Min(0f)] public float length;
    [Min(0f)] public float fadeIn;
    [Min(0f)] public float fadeOut;
    [Range(0f, 1f)] public float weight;

    public float End => start + length;

    public static BossVictoryClip Make(float start, float length, float fadeIn = 0f, float fadeOut = 0f, float weight = 1f) =>
        new BossVictoryClip { start = start, length = length, fadeIn = fadeIn, fadeOut = fadeOut, weight = weight };

    /// <summary>시각 t 의 값 = 세기 × min(페이드 인, 페이드 아웃). 페이드 = smoothstep(옛 곡선의 기울기 0 키 보간과 같은 식). 밖이면 0.</summary>
    public float Evaluate(float t)
    {
        if (length <= 0f || t < start || t > End) return 0f;
        float a = fadeIn > 0f ? Smooth((t - start) / fadeIn) : 1f;
        float b = fadeOut > 0f ? Smooth((End - t) / fadeOut) : 1f;
        return weight * Mathf.Min(a, b);
    }

    static float Smooth(float x)
    {
        x = Mathf.Clamp01(x);
        return x * x * (3f - 2f * x);
    }
}

/// <summary>
/// 보스 처치 연출 타임라인 — 트랙별 클립 목록 + 순간 마커(PLAN-boss-victory-glitch "타임라인 에디터 창" Q2·Q3).
/// 런타임(<see cref="BossVictoryGlitchPresentation"/>)과 에디터 창(<c>Tools/Boss Victory/처치 연출 타임라인</c>)이 같은 데이터를 읽는다.
/// 시각 = 처치 판정 뒤 초(복제 처치 시각 기준 글리치 시계).
/// </summary>
[Serializable]
public sealed class BossVictoryTimeline
{
    public enum TrackKind { Range, Pulse, Single }

    public List<BossVictoryClip> master = new List<BossVictoryClip>();
    public List<BossVictoryClip> desaturate = new List<BossVictoryClip>();
    public List<BossVictoryClip> amber = new List<BossVictoryClip>();
    public List<BossVictoryClip> scanline = new List<BossVictoryClip>();
    public List<BossVictoryClip> bossNoise = new List<BossVictoryClip>();
    public List<BossVictoryClip> tear = new List<BossVictoryClip>();
    public List<BossVictoryClip> smear = new List<BossVictoryClip>();
    public List<BossVictoryClip> hudHide = new List<BossVictoryClip>();
    public List<BossVictoryClip> hold = new List<BossVictoryClip>();
    public List<BossVictoryClip> crtOff = new List<BossVictoryClip>();

    [Min(0f)] public float focusKillerTime = 0.2f;
    [Min(0f)] public float hideOthersTime = 0.2f;
    [Min(0f)] public float lobbyTransitionTime = 5.6f;

    public static readonly BossVictoryTrack[] Tracks = (BossVictoryTrack[])Enum.GetValues(typeof(BossVictoryTrack));
    public static readonly BossVictoryMarker[] Markers = (BossVictoryMarker[])Enum.GetValues(typeof(BossVictoryMarker));

    public List<BossVictoryClip> Clips(BossVictoryTrack track) => track switch
    {
        BossVictoryTrack.Master => master,
        BossVictoryTrack.Desaturate => desaturate,
        BossVictoryTrack.Amber => amber,
        BossVictoryTrack.Scanline => scanline,
        BossVictoryTrack.BossNoise => bossNoise,
        BossVictoryTrack.Tear => tear,
        BossVictoryTrack.Smear => smear,
        BossVictoryTrack.HudHide => hudHide,
        BossVictoryTrack.Hold => hold,
        BossVictoryTrack.CrtOff => crtOff,
        _ => throw new ArgumentOutOfRangeException(nameof(track)),
    };

    public float MarkerTime(BossVictoryMarker marker) => marker switch
    {
        BossVictoryMarker.FocusKiller => focusKillerTime,
        BossVictoryMarker.HideOthers => hideOthersTime,
        BossVictoryMarker.LobbyTransition => lobbyTransitionTime,
        _ => throw new ArgumentOutOfRangeException(nameof(marker)),
    };

    public void SetMarkerTime(BossVictoryMarker marker, float time)
    {
        time = Mathf.Max(0f, time);
        switch (marker)
        {
            case BossVictoryMarker.FocusKiller: focusKillerTime = time; break;
            case BossVictoryMarker.HideOthers: hideOthersTime = time; break;
            case BossVictoryMarker.LobbyTransition: lobbyTransitionTime = time; break;
        }
    }

    public static TrackKind KindOf(BossVictoryTrack track) => track switch
    {
        BossVictoryTrack.Tear or BossVictoryTrack.Smear => TrackKind.Pulse,
        BossVictoryTrack.CrtOff => TrackKind.Single,
        _ => TrackKind.Range,
    };

    /// <summary>페이드·세기가 의미 있는 트랙인가(정지 유지·TV 꺼짐은 구간만).</summary>
    public static bool HasFades(BossVictoryTrack track) => track != BossVictoryTrack.Hold && track != BossVictoryTrack.CrtOff;

    public static string TrackLabel(BossVictoryTrack track) => track switch
    {
        BossVictoryTrack.Master => "전체 세기",
        BossVictoryTrack.Desaturate => "흑백",
        BossVictoryTrack.Amber => "노란 틴트",
        BossVictoryTrack.Scanline => "주사선",
        BossVictoryTrack.BossNoise => "보스 노이즈",
        BossVictoryTrack.Tear => "가로 찢김",
        BossVictoryTrack.Smear => "밝은 부분 끌림",
        BossVictoryTrack.HudHide => "HUD 숨김",
        BossVictoryTrack.Hold => "정지 유지",
        BossVictoryTrack.CrtOff => "TV 꺼짐",
        _ => track.ToString(),
    };

    public static string MarkerLabel(BossVictoryMarker marker) => marker switch
    {
        BossVictoryMarker.FocusKiller => "카메라 → 처치자",
        BossVictoryMarker.HideOthers => "처치자 외 숨김",
        BossVictoryMarker.LobbyTransition => "로비 전환",
        _ => marker.ToString(),
    };

    /// <summary>트랙 값 = 겹치는 클립 중 큰 값(0~1).</summary>
    public float Evaluate(BossVictoryTrack track, float t)
    {
        List<BossVictoryClip> clips = Clips(track);
        float v = 0f;
        for (int i = 0; i < clips.Count; i++) v = Mathf.Max(v, clips[i].Evaluate(t));
        return v;
    }

    /// <summary>화면 효과를 평가할 시각 — 정지 유지 클립 안이면 그 클립 시작에 고정.</summary>
    public float EffectTime(float t)
    {
        for (int i = 0; i < hold.Count; i++)
        {
            BossVictoryClip c = hold[i];
            if (c.length > 0f && t >= c.start && t < c.End) return c.start;
        }
        return t;
    }

    /// <summary>TV 꺼짐 = 첫 클립. 없으면 false.</summary>
    public bool TryGetCrtOff(out BossVictoryClip clip)
    {
        for (int i = 0; i < crtOff.Count; i++)
        {
            if (crtOff[i].length <= 0f) continue;
            clip = crtOff[i];
            return true;
        }
        clip = default;
        return false;
    }

    /// <summary>모든 클립 끝·마커 중 가장 늦은 시각(창 시간축 길이).</summary>
    public float Duration
    {
        get
        {
            float d = 0f;
            foreach (BossVictoryTrack track in Tracks)
            {
                List<BossVictoryClip> clips = Clips(track);
                for (int i = 0; i < clips.Count; i++) d = Mathf.Max(d, clips[i].End);
            }
            foreach (BossVictoryMarker marker in Markers) d = Mathf.Max(d, MarkerTime(marker));
            return d;
        }
    }

    public const float DefaultEnd = 5.6f;   // TV 꺼짐 끝 = 로비 전환

    /// <summary>
    /// 기본 타임라인 — 옛 기본 곡선(에셋 값)과 같은 화면(EditMode <c>BossVictoryTimelineTests</c>).
    /// 램프 곡선 → 끝(5.6)까지 가는 페이드 인 클립 · 펄스 곡선 → 키 사이 그대로의 페이드 인/아웃 클립 ·
    /// 옛 glitchDuration 3.5 + holdSeconds 1 → 정지 유지 3.5~4.5 · crtOffSeconds 1.1 → TV 꺼짐 4.5~5.6.
    /// </summary>
    public static BossVictoryTimeline CreateDefault()
    {
        const float end = DefaultEnd;
        static BossVictoryClip Ramp(float a, float b) => BossVictoryClip.Make(a, end - a, b - a);
        var tl = new BossVictoryTimeline();
        tl.master.Add(Ramp(0.08f, 0.15f));
        tl.desaturate.Add(Ramp(0.1f, 0.2f));
        tl.amber.Add(Ramp(0.3f, 0.45f));
        tl.scanline.Add(Ramp(0.1f, 0.2f));
        tl.bossNoise.Add(Ramp(0.12f, 0.17f));
        tl.hudHide.Add(Ramp(0.08f, 0.12f));
        tl.tear.Add(BossVictoryClip.Make(0.08f, 0.17f, 0.12f, 0.05f));
        tl.tear.Add(BossVictoryClip.Make(0.32f, 0.09f, 0.01f, 0.08f));
        tl.tear.Add(BossVictoryClip.Make(1.42f, 0.19f, 0.1f, 0.09f));
        tl.tear.Add(BossVictoryClip.Make(1.62f, 0.11f, 0.05f, 0.06f));
        tl.tear.Add(BossVictoryClip.Make(2.43f, 0.24f, 0.12f, 0.12f));
        tl.smear.Add(BossVictoryClip.Make(0.08f, 0.17f, 0.12f, 0.05f));
        tl.smear.Add(BossVictoryClip.Make(0.32f, 0.09f, 0.01f, 0.08f));
        tl.smear.Add(BossVictoryClip.Make(1.42f, 0.2f, 0.1f, 0.1f));
        tl.smear.Add(BossVictoryClip.Make(2.43f, 0.24f, 0.12f, 0.12f));
        tl.hold.Add(BossVictoryClip.Make(3.5f, 1f));
        tl.crtOff.Add(BossVictoryClip.Make(4.5f, 1.1f));
        tl.focusKillerTime = 0.2f;
        tl.hideOthersTime = 0.2f;
        tl.lobbyTransitionTime = end;
        return tl;
    }
}
