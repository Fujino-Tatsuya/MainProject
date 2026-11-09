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
    KillerAction, // 처치자 평타 — 첫 클립만, 시작 0 고정, 끝 = 처치 순간 하던 동작을 끊는 시각(없으면 자기 클립 끝까지 — 255 후속 4)
}

/// <summary>보스 처치 연출 타임라인 순간 마커.</summary>
public enum BossVictoryMarker
{
    FocusKiller,      // 처치자가 아닌 피어 카메라 → 처치자(즉시 컷)
    HideOthers,       // 처치자 외 플레이어 캐릭터 숨김(모든 피어)
    LobbyTransition,  // 로비 전환 — 호스트 화면이 지나면 서버가 결과로 넘긴다
    BackDashStart,    // 백대쉬 시작 — 하던 동작이 남아 있으면 끊고 백대쉬(255 후속 4 — 옛 '처치자 동작 끝 → 백대쉬' 마커는 처치자 평타 클립 끝으로)
    FinisherStart,    // 스킬 연출 시작 — 처치 연출 스킬을 표시용으로 바로(백대쉬가 덜 끝났으면 끊음, 이전 스킬을 기다리지 않음). 창은 '처치 연출 스킬' 클립(시작 = 이 값, 끝 = 클립 끝·자르기 finisherTrimSeconds — 255 후속 7)
    SlowStart,        // 느려짐 시작 — 처치자 End 후 배율(0 = 끔, 스킬 End 이벤트). 보스는 보스 슬로우가 꺼졌을 때만 따른다(255 후속 5)
    BossHitAnim,      // 보스 피격 애니 시작 — 설정 SO 상태(23호 getowned = Boss_23_ParryHit_InPlace)를 처음부터, 보스 ×1(상태 이름이 비면 없음 — 255 후속 5)
    BossFreeze,       // 보스 정지 — 보스 배율 0(그 자세로). 0 = 끔
    BossSlow,         // 보스 슬로우 — 보스 End 후 배율. 0 = 끔(느려짐 시작 → 그것도 끔이면 스킬 End)
}

/// <summary>
/// 클립 한 개 — 시작·길이(초, 처치 판정 뒤). 효과 세기 트랙은 세기 키 목록(<see cref="keys"/> — 시작·중간·끝, 255 후속 6).
/// 키가 없으면 옛 페이드 인/아웃·세기 식(코드로 만든 클립·이관 전). 설정 SO 를 읽을 때 효과 세기 트랙 클립은 같은 모양의 키로 옮긴다.
/// 🔴 구조체 복사는 키 목록을 같이 쓴다 — 붙여넣기·복제는 <see cref="WithCopiedKeys"/>.
/// </summary>
[Serializable]
public struct BossVictoryClip
{
    [Min(0f)] public float start;
    [Min(0f)] public float length;
    [Min(0f)] public float fadeIn;
    [Min(0f)] public float fadeOut;
    [Range(0f, 1f)] public float weight;
    /// <summary>세기 키(비율 위치 — 클립을 옮기거나 늘이면 비율대로 따라간다). 비면 옛 페이드 식.</summary>
    public List<BossVictoryKey> keys;

    public float End => start + length;

    public bool HasKeys => keys != null && keys.Count > 0;

    /// <summary>키 목록을 새 목록으로 복사한 클립(붙여넣기·복제용).</summary>
    public BossVictoryClip WithCopiedKeys()
    {
        BossVictoryClip c = this;
        if (keys != null) c.keys = new List<BossVictoryKey>(keys);
        return c;
    }

    /// <summary>옛 페이드 인/아웃·세기를 같은 모양의 세기 키로 바꾼다(페이드 0·세기 1 로 비움). 이미 키가 있으면 그대로. 바꿨으면 true.</summary>
    public bool MigrateFadesToKeys()
    {
        if (HasKeys) return false;
        keys = BossVictoryKeys.FromFades(length, fadeIn, fadeOut, weight);
        fadeIn = fadeOut = 0f;
        weight = 1f;
        return true;
    }

    public static BossVictoryClip Make(float start, float length, float fadeIn = 0f, float fadeOut = 0f, float weight = 1f) =>
        new BossVictoryClip { start = start, length = length, fadeIn = fadeIn, fadeOut = fadeOut, weight = weight };

    /// <summary>시각 t 의 값 — 키가 있으면 키 곡선, 없으면 세기 × min(페이드 인, 페이드 아웃)(페이드 = smoothstep). 밖이면 0.</summary>
    public float Evaluate(float t)
    {
        if (length <= 0f || t < start || t > End) return 0f;
        if (HasKeys) return BossVictoryKeys.Value(keys, (t - start) / length);
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
    /// <summary>처치자 평타 — 첫 클립만, 시작 0 · 끝 = 하던 동작을 끊는 시각(255 후속 4).</summary>
    public List<BossVictoryClip> killerAction = new List<BossVictoryClip>();

    [Min(0f)] public float focusKillerTime = 0.2f;
    [Min(0f)] public float hideOthersTime = 0.2f;
    [Min(0f)] public float lobbyTransitionTime = 5.6f;
    [Tooltip("백대쉬 시작(초). 하던 동작이 남아 있으면 끊고 백대쉬.")]
    [Min(0f)] public float backDashStartTime = DefaultActionEnd;
    [Tooltip("스킬 연출 시작(초). 처치 연출 스킬을 표시용으로 바로 튼다 — 백대쉬가 덜 끝났으면 끊고, 이전 스킬을 기다리지 않는다.")]
    [Min(0f)] public float finisherStartTime = DefaultFinisherStart;
    [Tooltip("처치 연출 스킬 클립 끝 자르기(클립 시간 초, 공속 100% 기준 — 프레임 단위). 0 = 안 자름(클립 끝까지). 그 프레임에서 처치자 애니가 멈춘다(배율 0, 255 후속 7).")]
    [Min(0f)] public float finisherTrimSeconds;
    [Tooltip("느려짐 시작(초). 처치자·보스 End 후 배율. 0 = 끔 — 처치 연출 스킬 End 이벤트에서.")]
    [Min(0f)] public float slowStartTime;
    [Tooltip("보스 피격 애니 시작(초) — 0 = 처치 순간. 상태 이름(설정 SO bossHitStateName)이 비면 쓰지 않는다.")]
    [Min(0f)] public float bossHitAnimTime;
    [Tooltip("보스 정지(초) — 보스 배율 0. 0 = 끔.")]
    [Min(0f)] public float bossFreezeTime;
    [Tooltip("보스 슬로우(초) — 보스 End 후 배율. 0 = 끔(느려짐 시작 → 그것도 끔이면 스킬 End).")]
    [Min(0f)] public float bossSlowTime;

    // 옛 '처치자 동작 끝 → 백대쉬' 마커(255) — 읽기만 해서 처치자 평타 클립 끝·백대쉬 시작·스킬 연출 시작으로 옮긴다(255 후속 4, 설정 SO OnAfterDeserialize). 옮긴 뒤 0.
    [SerializeField, HideInInspector] float killerActionEndTime;   // 칸 이름 그대로(옛 에셋 값을 읽는다)

    public const float DefaultActionEnd = 0.13f;   // 사용자 값(옛 마커 0.13) = 평타 끝·백대쉬 시작 기본
    public const float DefaultFinisherStart = 0.83f;   // 0.13 + 백대쉬 0.35 + 백대쉬 후 대기 0.35(BossVictoryKillerSequence.DefaultFinisherStart)

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
        BossVictoryTrack.KillerAction => killerAction,
        _ => throw new ArgumentOutOfRangeException(nameof(track)),
    };

    public float MarkerTime(BossVictoryMarker marker) => marker switch
    {
        BossVictoryMarker.FocusKiller => focusKillerTime,
        BossVictoryMarker.HideOthers => hideOthersTime,
        BossVictoryMarker.LobbyTransition => lobbyTransitionTime,
        BossVictoryMarker.BackDashStart => backDashStartTime,
        BossVictoryMarker.FinisherStart => finisherStartTime,
        BossVictoryMarker.SlowStart => slowStartTime,
        BossVictoryMarker.BossHitAnim => bossHitAnimTime,
        BossVictoryMarker.BossFreeze => bossFreezeTime,
        BossVictoryMarker.BossSlow => bossSlowTime,
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
            case BossVictoryMarker.BackDashStart: backDashStartTime = time; break;
            case BossVictoryMarker.FinisherStart: finisherStartTime = time; break;
            case BossVictoryMarker.SlowStart: slowStartTime = time; break;
            case BossVictoryMarker.BossHitAnim: bossHitAnimTime = time; break;
            case BossVictoryMarker.BossFreeze: bossFreezeTime = time; break;
            case BossVictoryMarker.BossSlow: bossSlowTime = time; break;
        }
    }

    public static TrackKind KindOf(BossVictoryTrack track) => track switch
    {
        BossVictoryTrack.Tear or BossVictoryTrack.Smear => TrackKind.Pulse,
        BossVictoryTrack.CrtOff or BossVictoryTrack.KillerAction => TrackKind.Single,
        BossVictoryTrack.CameraCut => TrackKind.Cut,
        _ => TrackKind.Range,
    };

    /// <summary>페이드·세기가 의미 있는 트랙인가(TV 꺼짐·처치자 평타는 구간만 · 카메라 컷은 시작만).</summary>
    public static bool HasFades(BossVictoryTrack track) =>
        track != BossVictoryTrack.CrtOff && track != BossVictoryTrack.CameraCut && track != BossVictoryTrack.KillerAction;

    /// <summary>시작이 0 고정인 트랙인가(처치자 평타 — 처치 순간부터, 끝만 고친다).</summary>
    public static bool StartFixed(BossVictoryTrack track) => track == BossVictoryTrack.KillerAction;

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
        BossVictoryTrack.KillerAction => "처치자 평타",
        _ => track.ToString(),
    };

    public static string MarkerLabel(BossVictoryMarker marker) => marker switch
    {
        BossVictoryMarker.FocusKiller => "카메라 → 처치자",
        BossVictoryMarker.HideOthers => "처치자 외 숨김",
        BossVictoryMarker.LobbyTransition => "로비 전환",
        BossVictoryMarker.BackDashStart => "백대쉬 시작",
        BossVictoryMarker.FinisherStart => "스킬 연출 시작",
        BossVictoryMarker.SlowStart => "느려짐 시작",
        BossVictoryMarker.BossHitAnim => "보스 피격 애니 시작",
        BossVictoryMarker.BossFreeze => "보스 정지",
        BossVictoryMarker.BossSlow => "보스 슬로우",
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

    // ── 처치자 순서(255 후속 4) ──

    /// <summary>처치자 평타 = 첫 클립(길이 &gt; 0). 없으면 false — 하던 동작은 자기 클립 끝까지.</summary>
    public bool TryGetKillerAction(out BossVictoryClip clip)
    {
        for (int i = 0; i < killerAction.Count; i++)
        {
            if (killerAction[i].length <= 0f) continue;
            clip = killerAction[i];
            return true;
        }
        clip = default;
        return false;
    }

    /// <summary>0 이 '끔'인 마커인가(느려짐 시작·보스 정지·보스 슬로우). 나머지는 0 = 처치 순간.</summary>
    public static bool ZeroMeansOff(BossVictoryMarker marker) =>
        marker is BossVictoryMarker.SlowStart or BossVictoryMarker.BossFreeze or BossVictoryMarker.BossSlow;

    /// <summary>보스 노드 시각(<see cref="BossVictoryBossTimeScale.Scale"/> 입력). <paramref name="hasHitAnim"/> = 설정 SO 피격 애니 상태 이름이 있다.</summary>
    public BossVictoryBossTimeScale.Nodes BossNodes(bool hasHitAnim) => new BossVictoryBossTimeScale.Nodes
    {
        HasHitAnim = hasHitAnim,
        HitAt = bossHitAnimTime,
        FreezeAt = bossFreezeTime,
        SlowAt = bossSlowTime,
    };

    /// <summary>처치자 단계 시각(<see cref="BossVictoryKillerSequence.Target"/> 입력). 평타 끝 = 클립 끝(시작은 0 고정이라 시작 칸 무시).</summary>
    public BossVictoryKillerSequence.Times KillerTimes()
    {
        bool has = TryGetKillerAction(out BossVictoryClip clip);
        return new BossVictoryKillerSequence.Times
        {
            HasActionClip = has,
            ActionEnd = has ? clip.End : 0f,
            BackDashStart = backDashStartTime,
            FinisherStart = finisherStartTime,
        };
    }

    /// <summary>
    /// 옛 형식 → 키(255 후속 6): 효과 세기 트랙 클립의 페이드 인/아웃·세기 → 같은 모양의 세기 키 · 카메라 컷 시작/끝 화면 → 화면 키(끝 없으면 키 1개 = 고정).
    /// 이미 키가 있으면 그대로(새 형식). 옮긴 개수.
    /// </summary>
    public int MigrateKeys()
    {
        int n = 0;
        foreach (BossVictoryTrack track in Tracks)
        {
            if (!HasFades(track)) continue;
            List<BossVictoryClip> clips = Clips(track);
            for (int i = 0; i < clips.Count; i++)
            {
                BossVictoryClip c = clips[i];
                if (!c.MigrateFadesToKeys()) continue;
                clips[i] = c;
                n++;
            }
        }
        for (int i = 0; i < cameraCutShots.Count; i++)
        {
            BossVictoryCameraCuts.Cut cut = cameraCutShots[i];
            if (!cut.MigrateToKeys()) continue;
            cameraCutShots[i] = cut;
            n++;
        }
        return n;
    }

    /// <summary>
    /// 옛 '처치자 동작 끝 → 백대쉬' 마커 값 L(&gt; 0 = 255 후속 4 전 형식 에셋)을 옮긴다 — 처치자 평타 = [0, L] · 백대쉬 시작 = L(평타 끝 직후) ·
    /// 스킬 연출 시작 = L + 백대쉬 시간 + 백대쉬 후 대기(그때 체감). 새 형식은 옛 칸이 0 이라 아무것도 안 한다. 옮겼으면 true.
    /// </summary>
    public bool MigrateLegacyKillerActionEnd(float backDashDuration, float backDashIdleDelay)
    {
        if (killerActionEndTime <= 0f) return false;
        float end = killerActionEndTime;
        killerAction ??= new List<BossVictoryClip>();
        killerAction.Clear();
        killerAction.Add(BossVictoryClip.Make(0f, end));
        backDashStartTime = end;
        finisherStartTime = BossVictoryKillerSequence.DefaultFinisherStart(end, backDashDuration, backDashIdleDelay);
        killerActionEndTime = 0f;
        return true;
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
        tl.killerAction.Add(BossVictoryClip.Make(0f, DefaultActionEnd));
        tl.backDashStartTime = DefaultActionEnd;
        tl.finisherStartTime = DefaultFinisherStart;
        tl.slowStartTime = 0f;
        tl.MigrateKeys();   // 효과 세기 클립 = 세기 키(같은 화면 — 255 후속 6)
        return tl;
    }
}
