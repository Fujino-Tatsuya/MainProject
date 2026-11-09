using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>보스 처치 연출 타임라인 트랙(PLAN-boss-victory-glitch "타임라인 에디터 창"). 순서 = 창 위→아래.</summary>
public enum BossVictoryTrack
{
    Desaturate,  // 흑백
    Amber,       // 노란 틴트(배경만)
    Scanline,    // CRT 주사선
    BossNoise,   // 보스 노이즈
    Tear,        // 가로 찢김(단발 펄스)
    Smear,       // 밝은 부분 끌림(단발 펄스)
    HudHide,     // HUD 숨김(0.5 초과)
    CrtOff,      // TV 꺼짐 — 첫 클립만, 시작에 화면 캡처·길이 = 꺼짐 길이
    CameraCut,   // 카메라 컷 — 클립 시작 = 컷 시작, 다음 컷 시작까지 유지(길이는 자동). 컷 값 = cameraCutShots 같은 번호(255)
}

/// <summary>보스 처치 연출 타임라인 순간 마커.</summary>
public enum BossVictoryMarker
{
    FocusKiller,      // 처치자가 아닌 피어 카메라 → 처치자(즉시 컷)
    HideOthers,       // 처치자 외 플레이어 캐릭터 숨김(모든 피어)
    LobbyTransition,  // 로비 전환 — 호스트 화면이 지나면 서버가 결과로 넘긴다
    KillerActionEnd,  // 처치자 동작 끝 → 백대쉬 — 처치 순간 하던 동작(평타 등)을 이 시각에 끊고 백대쉬(0 = 끔, 클립 끝까지 — 255)
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
    public enum TrackKind { Range, Pulse, Single, Cut }

    public List<BossVictoryClip> desaturate = new List<BossVictoryClip>();
    public List<BossVictoryClip> amber = new List<BossVictoryClip>();
    public List<BossVictoryClip> scanline = new List<BossVictoryClip>();
    public List<BossVictoryClip> bossNoise = new List<BossVictoryClip>();
    public List<BossVictoryClip> tear = new List<BossVictoryClip>();
    public List<BossVictoryClip> smear = new List<BossVictoryClip>();
    public List<BossVictoryClip> hudHide = new List<BossVictoryClip>();
    public List<BossVictoryClip> crtOff = new List<BossVictoryClip>();
    /// <summary>카메라 컷 클립 — 시작만 의미(길이 = 다음 컷 시작까지, <see cref="FitCameraCutLengths"/> 가 맞춘다).</summary>
    public List<BossVictoryClip> cameraCut = new List<BossVictoryClip>();
    /// <summary>카메라 컷 값 — <see cref="cameraCut"/> 와 같은 번호(창이 추가·삭제·붙여넣기를 함께 한다, 개수 어긋나면 <see cref="SyncCameraCutShots"/>).</summary>
    public List<BossVictoryCameraCuts.Cut> cameraCutShots = new List<BossVictoryCameraCuts.Cut>();

    [Min(0f)] public float focusKillerTime = 0.2f;
    [Min(0f)] public float hideOthersTime = 0.2f;
    [Min(0f)] public float lobbyTransitionTime = 5.6f;
    [Tooltip("처치자 동작 끝 → 백대쉬(초). 0 = 끔 — 하던 동작 클립 끝까지 재생 뒤 백대쉬(255 전과 같음).")]
    [Min(0f)] public float killerActionEndTime;

    public static readonly BossVictoryTrack[] Tracks = (BossVictoryTrack[])Enum.GetValues(typeof(BossVictoryTrack));
    public static readonly BossVictoryMarker[] Markers = (BossVictoryMarker[])Enum.GetValues(typeof(BossVictoryMarker));

    public List<BossVictoryClip> Clips(BossVictoryTrack track) => track switch
    {
        BossVictoryTrack.Desaturate => desaturate,
        BossVictoryTrack.Amber => amber,
        BossVictoryTrack.Scanline => scanline,
        BossVictoryTrack.BossNoise => bossNoise,
        BossVictoryTrack.Tear => tear,
        BossVictoryTrack.Smear => smear,
        BossVictoryTrack.HudHide => hudHide,
        BossVictoryTrack.CrtOff => crtOff,
        BossVictoryTrack.CameraCut => cameraCut,
        _ => throw new ArgumentOutOfRangeException(nameof(track)),
    };

    public float MarkerTime(BossVictoryMarker marker) => marker switch
    {
        BossVictoryMarker.FocusKiller => focusKillerTime,
        BossVictoryMarker.HideOthers => hideOthersTime,
        BossVictoryMarker.LobbyTransition => lobbyTransitionTime,
        BossVictoryMarker.KillerActionEnd => killerActionEndTime,
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
            case BossVictoryMarker.KillerActionEnd: killerActionEndTime = time; break;
        }
    }

    public static TrackKind KindOf(BossVictoryTrack track) => track switch
    {
        BossVictoryTrack.Tear or BossVictoryTrack.Smear => TrackKind.Pulse,
        BossVictoryTrack.CrtOff => TrackKind.Single,
        BossVictoryTrack.CameraCut => TrackKind.Cut,
        _ => TrackKind.Range,
    };

    /// <summary>페이드·세기가 의미 있는 트랙인가(TV 꺼짐은 구간만 · 카메라 컷은 시작만).</summary>
    public static bool HasFades(BossVictoryTrack track) => track != BossVictoryTrack.CrtOff && track != BossVictoryTrack.CameraCut;

    public static string TrackLabel(BossVictoryTrack track) => track switch
    {
        BossVictoryTrack.Desaturate => "흑백",
        BossVictoryTrack.Amber => "노란 틴트",
        BossVictoryTrack.Scanline => "주사선",
        BossVictoryTrack.BossNoise => "보스 노이즈",
        BossVictoryTrack.Tear => "가로 찢김",
        BossVictoryTrack.Smear => "밝은 부분 끌림",
        BossVictoryTrack.HudHide => "HUD 숨김",
        BossVictoryTrack.CrtOff => "TV 꺼짐",
        BossVictoryTrack.CameraCut => "카메라 컷",
        _ => track.ToString(),
    };

    public static string MarkerLabel(BossVictoryMarker marker) => marker switch
    {
        BossVictoryMarker.FocusKiller => "카메라 → 처치자",
        BossVictoryMarker.HideOthers => "처치자 외 숨김",
        BossVictoryMarker.LobbyTransition => "로비 전환",
        BossVictoryMarker.KillerActionEnd => "처치자 동작 끝 → 백대쉬",
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

    // ── 카메라 컷(255) ──

    /// <summary>시각 t 에 켜진 카메라 컷 번호(없으면 -1) — 클립 시작이 t 이하인 것 중 가장 늦은 것.</summary>
    public int ActiveCameraCut(float t) => BossVictoryCameraCuts.ActiveIndex(cameraCut, t);

    /// <summary>컷 <paramref name="index"/> 의 값(값 목록이 모자라면 기본값).</summary>
    public BossVictoryCameraCuts.Cut CameraCutShot(int index) =>
        index >= 0 && index < cameraCutShots.Count ? cameraCutShots[index] : BossVictoryCameraCuts.Cut.Default;

    /// <summary>컷 값 개수를 클립 개수에 맞춘다(모자라면 기본값, 넘치면 버림). 바꿨으면 true.</summary>
    public bool SyncCameraCutShots()
    {
        bool changed = false;
        while (cameraCutShots.Count < cameraCut.Count) { cameraCutShots.Add(BossVictoryCameraCuts.Cut.Default); changed = true; }
        if (cameraCutShots.Count > cameraCut.Count) { cameraCutShots.RemoveRange(cameraCut.Count, cameraCutShots.Count - cameraCut.Count); changed = true; }
        return changed;
    }

    public const float MinCutLength = 0.1f;

    /// <summary>카메라 컷 클립 길이 = 다음(시작이 더 늦은) 컷 시작까지 · 마지막 컷 = 로비 전환 마커까지(최소 <see cref="MinCutLength"/>). 표시용 — 판정은 시작만 본다.</summary>
    public void FitCameraCutLengths()
    {
        for (int i = 0; i < cameraCut.Count; i++)
        {
            BossVictoryClip c = cameraCut[i];
            c.length = Mathf.Max(MinCutLength, CameraCutEnd(i) - c.start);
            c.fadeIn = c.fadeOut = 0f;
            c.weight = 1f;
            cameraCut[i] = c;
        }
    }

    /// <summary>컷 <paramref name="index"/> 구간 끝 = 다음(시작이 더 늦은) 컷 시작 · 마지막 컷 = 로비 전환 마커. 저장된 클립 길이와 무관(런타임·창 같은 식).</summary>
    public float CameraCutEnd(int index)
    {
        float start = cameraCut[index].start;
        float next = float.PositiveInfinity;
        for (int j = 0; j < cameraCut.Count; j++)
        {
            float s = cameraCut[j].start;
            if (j != index && (s > start || (s == start && j > index)) && s < next) next = s;
        }
        return float.IsPositiveInfinity(next) ? lobbyTransitionTime : next;
    }

    /// <summary>컷 <paramref name="index"/> 의 시각 t 진행(0 = 컷 시작, 1 = 구간 끝, 범위 밖은 자름). 구간이 0 이하면 1(끝 화면).</summary>
    public float CameraCutProgress(int index, float t)
    {
        float start = cameraCut[index].start;
        float length = CameraCutEnd(index) - start;
        return length > 0f ? Mathf.Clamp01((t - start) / length) : 1f;
    }

    // ── 처치자 동작 끝 → 백대쉬(255) ──

    /// <summary>처치자의 하던 동작을 지금 끊고 백대쉬로 넘어가는가 — 마커가 켜져 있고(&gt; 0) 시각이 지났다. 클립 끝은 호출부가 따로 본다(둘 중 이른 쪽).</summary>
    public static bool ShouldEndKillerAction(float markerTime, float t) => markerTime > 0f && t >= markerTime;

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
    /// 옛 전체 세기(master 0.08~0.15 램프, 11-09 삭제)는 흑백·주사선·첫 찢김/끌림 클립의 시작·페이드 인으로 녹였다(곱한 값에 가장 가까운 0.005초 격자 — 오차 ≤ 0.05, 0.08~0.2초 몇 프레임) ·
    /// 옛 crtOffSeconds 1.1 → TV 꺼짐 4.5~5.6(정지 유지 트랙은 255 후속 2 에서 삭제 — 끝의 정지 느낌은 처치자·보스 ×0.1 과 TV 꺼짐).
    /// </summary>
    public static BossVictoryTimeline CreateDefault()
    {
        const float end = DefaultEnd;
        static BossVictoryClip Ramp(float a, float b) => BossVictoryClip.Make(a, end - a, b - a);
        var tl = new BossVictoryTimeline();
        tl.desaturate.Add(Ramp(0.105f, 0.195f));
        tl.amber.Add(Ramp(0.3f, 0.45f));
        tl.scanline.Add(Ramp(0.105f, 0.195f));
        tl.bossNoise.Add(Ramp(0.12f, 0.17f));
        tl.hudHide.Add(Ramp(0.08f, 0.12f));
        tl.tear.Add(BossVictoryClip.Make(0.09f, 0.16f, 0.105f, 0.05f));
        tl.tear.Add(BossVictoryClip.Make(0.32f, 0.09f, 0.01f, 0.08f));
        tl.tear.Add(BossVictoryClip.Make(1.42f, 0.19f, 0.1f, 0.09f));
        tl.tear.Add(BossVictoryClip.Make(1.62f, 0.11f, 0.05f, 0.06f));
        tl.tear.Add(BossVictoryClip.Make(2.43f, 0.24f, 0.12f, 0.12f));
        tl.smear.Add(BossVictoryClip.Make(0.09f, 0.16f, 0.105f, 0.05f));
        tl.smear.Add(BossVictoryClip.Make(0.32f, 0.09f, 0.01f, 0.08f));
        tl.smear.Add(BossVictoryClip.Make(1.42f, 0.2f, 0.1f, 0.1f));
        tl.smear.Add(BossVictoryClip.Make(2.43f, 0.24f, 0.12f, 0.12f));
        tl.crtOff.Add(BossVictoryClip.Make(4.5f, 1.1f));
        tl.focusKillerTime = 0.2f;
        tl.hideOthersTime = 0.2f;
        tl.lobbyTransitionTime = end;
        return tl;
    }
}
