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
    NoiseScrollV, // 보스 노이즈 UV 수직 이동 — 클립 구간 동안 시작 → 끝 오프셋(이징), 끝난 뒤 끝 값 유지, 클립끼리 더함(267 후속). 노이즈 텍스처 V 오프셋(_NoiseST.w)
    GroundCircle, // 발밑 회색 원(246 복원 — 267 후속) — 세기 = 반경 비율(설정 SO groundCircleRadius × 세기). 원 안 배경은 호박색 대신 흑백
    Rectify,      // 배경 글자 'RECTIFY'(267 후속) — 세기 = 알파(들어오는 동안 가로줄이 무작위로 켜지며 찢기듯 등장). 외곽선만, 인물·보스 뒤·배경 앞
    RectifyFill,  // RECTIFY 안쪽 색(267 후속) — Ring Wipe Fill(링 3개). 세기 = 채움 불투명도, 링 시간표 = 클립 시작 기준
    CameraBob,    // 카메라 두둥실(267 후속) — 화면 세로 흔들림 = 세기 × 진폭(화면 높이 %) × e^(−감쇠·경과) × sin(2π(경과/주기 + 위상)). 투영 행렬 세로 이동(카메라 위치는 안 건드림)
    AssassinDash, // 어쌔신 순간이동·Q 이동(272 후속) — 클립 = 시작에 그 클립 시작 지점으로 순간이동 → 길이 동안 도착 지점까지 Q 이동. 여러 개 = 시작 순 #1 #2 …(겹치면 앞 클립을 그 순간 끝냄). 처치자 이동이 순간이동일 때만 쓴다
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
    ChargeStart,      // Q 차징 시작(거너 — 처치자 이동 '후퇴 차징'이고 보스와 가까울 때만, 할 일 272). 기본(-1) = 백대쉬 시작 + 0.2초
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
    /// <summary>[노이즈 UV 수직 이동 트랙만] 시작·끝 오프셋(노이즈 UV, 1 = 텍스처 한 장 — 텍스처 Repeat 라 감김)·이징(267 후속).</summary>
    public float uvFrom;
    public float uvTo;
    public BossVictoryKeyCurve uvEase;
    /// <summary>[카메라 두둥실 트랙만] 진폭(화면 높이 %, + = 화면 내용이 위로)·주기(초)·시작 위상(0~1 주기)·감쇠(1/초) — 267 후속.</summary>
    public float bobAmplitude;
    public float bobPeriod;
    public float bobPhase;
    public float bobDamping;
    /// <summary>[어쌔신 순간이동·Q 이동 트랙만] 시작·도착 지점 = 보스 기준 오프셋(m, +Z = 보스 정면 — <see cref="BossVictoryKillerMoves.BossPoint"/>, 272 후속).</summary>
    public Vector3 dashFrom;
    public Vector3 dashTo;

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

    /// <summary>[노이즈 UV 수직 이동] 시각 t 의 오프셋 — 시작 전 0 · 구간 안 = 시작 → 끝(이징) · 끝 뒤 = 끝 값 유지. 길이 0 이면 시작부터 끝 값.</summary>
    public float UvOffset(float t)
    {
        if (t < start) return 0f;
        if (length <= 0f || t >= End) return uvTo;
        return Mathf.LerpUnclamped(uvFrom, uvTo, BossVictoryKeys.Ease(uvEase, (t - start) / length));
    }

    /// <summary>[카메라 두둥실] 시각 t 의 화면 세로 오프셋(화면 높이 %) — 세기(키·페이드) × 진폭 × e^(−감쇠·경과) × sin(2π(경과/주기 + 위상)). 클립 밖 0.</summary>
    public float BobOffset(float t)
    {
        float w = Evaluate(t);
        if (w <= 0f || bobAmplitude == 0f) return 0f;
        float e = t - start;
        float wave = bobPeriod > 0.0001f ? Mathf.Sin(2f * Mathf.PI * (e / bobPeriod + bobPhase)) : 0f;
        return w * bobAmplitude * Mathf.Exp(-Mathf.Max(0f, bobDamping) * e) * wave;
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
    /// <summary>보스 노이즈 UV 수직 이동 클립(267 후속) — <see cref="NoiseScrollV"/>.</summary>
    public List<BossVictoryClip> noiseScrollV = new List<BossVictoryClip>();
    /// <summary>카메라 두둥실 클립(267 후속) — <see cref="BossVictoryTrack.CameraBob"/>.</summary>
    public List<BossVictoryClip> cameraBob = new List<BossVictoryClip>();
    /// <summary>배경 글자 'RECTIFY' 클립(267 후속) — 세기 = 알파.</summary>
    public List<BossVictoryClip> rectify = new List<BossVictoryClip>();
    [SerializeField, HideInInspector] bool rectifySeeded;
    /// <summary>RECTIFY 안쪽 색 클립(267 후속) — 원형 확장 색상 전환.</summary>
    public List<BossVictoryClip> rectifyFill = new List<BossVictoryClip>();
    [SerializeField, HideInInspector] bool rectifyFillSeeded;
    // 옛 에셋(이 트랙 전)에 기본 두둥실 클립을 한 번 넣었는가 — 지운 뒤엔 다시 안 넣음(<see cref="SeedCameraBob"/>).
    [SerializeField, HideInInspector] bool cameraBobSeeded;
    /// <summary>발밑 회색 원 클립(246 복원 — 267 후속) — 세기 = 반경 비율. 페이드 인 = 0 → 목표 반경으로 커짐.</summary>
    public List<BossVictoryClip> groundCircle = new List<BossVictoryClip>();
    /// <summary>어쌔신 순간이동·Q 이동 클립(272 후속) — <see cref="BossVictoryAssassinDash"/>.</summary>
    public List<BossVictoryClip> assassinDash = new List<BossVictoryClip>();
    // 옛 에셋(이 트랙 전)에 기본 원 클립을 한 번 넣었는가 — 넣은 뒤 사용자가 지우면 그대로 둔다(<see cref="SeedGroundCircle"/>).
    [SerializeField, HideInInspector] bool groundCircleSeeded;

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
    [Tooltip("보스 피격 애니 끝 자르기(클립 시간 초 — 프레임 단위). 0 = 안 자름(클립 끝까지). 그 프레임에서 보스 배율 0 — 뒤에 슬로우 노드가 오면 다시 움직임(255 후속 10).")]
    [Min(0f)] public float bossHitTrimSeconds;
    [Tooltip("보스 정지(초) — 보스 배율 0. 0 = 끔.")]
    [Min(0f)] public float bossFreezeTime;
    [Tooltip("보스 슬로우(초) — 보스 End 후 배율. 0 = 끔(느려짐 시작 → 그것도 끔이면 스킬 End).")]
    [Min(0f)] public float bossSlowTime;
    [Tooltip("Q 차징 시작(초, 거너 후퇴 차징 — 보스와 가까워 E·백대쉬를 할 때만, 할 일 272). 음수 = 자동(백대쉬 시작 + 0.2초). 멀면 백대쉬 시작 시각에 바로 차징.")]
    public float chargeStartTime = -1f;

    /// <summary>Q 차징 시작 기본 = 백대쉬 시작 뒤 이 초(할 일 272).</summary>
    public const float DefaultChargeDelay = 0.2f;

    /// <summary>Q 차징 시작 실제 시각 — 칸이 음수(자동)면 백대쉬 시작 + <see cref="DefaultChargeDelay"/>.</summary>
    public float ChargeStartTime => chargeStartTime >= 0f ? chargeStartTime : backDashStartTime + DefaultChargeDelay;

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
        BossVictoryTrack.NoiseScrollV => noiseScrollV ??= new List<BossVictoryClip>(),
        BossVictoryTrack.CameraBob => cameraBob ??= new List<BossVictoryClip>(),
        BossVictoryTrack.Rectify => rectify ??= new List<BossVictoryClip>(),
        BossVictoryTrack.RectifyFill => rectifyFill ??= new List<BossVictoryClip>(),
        BossVictoryTrack.GroundCircle => groundCircle ??= new List<BossVictoryClip>(),
        BossVictoryTrack.AssassinDash => assassinDash ??= new List<BossVictoryClip>(),
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
        BossVictoryMarker.ChargeStart => ChargeStartTime,
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
            case BossVictoryMarker.ChargeStart: chargeStartTime = time; break;
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
        track != BossVictoryTrack.CrtOff && track != BossVictoryTrack.CameraCut && track != BossVictoryTrack.KillerAction && track != BossVictoryTrack.NoiseScrollV && track != BossVictoryTrack.AssassinDash;

    /// <summary>카메라 두둥실 트랙인가(세기 키 + 진폭·주기·위상·감쇠 칸 — 267 후속).</summary>
    public static bool IsBob(BossVictoryTrack track) => track == BossVictoryTrack.CameraBob;

    /// <summary>레퍼런스(ZZZ WIPEOUT 1.3~2.4초 60fps 측정) 기본값 — 진폭 0.22%·주기 1.6초·위상 0·감쇠 0.</summary>
    public const float DefaultBobAmplitude = 0.22f, DefaultBobPeriod = 1.6f;

    /// <summary>값 = UV 오프셋(시작·끝·이징)인 트랙인가(세기 키 없음 — 267 후속).</summary>
    public static bool IsUvScroll(BossVictoryTrack track) => track == BossVictoryTrack.NoiseScrollV;

    /// <summary>값 = 시작·도착 지점인 트랙인가(어쌔신 순간이동·Q 이동 — 세기 키 없음, 272 후속).</summary>
    public static bool IsAssassinDash(BossVictoryTrack track) => track == BossVictoryTrack.AssassinDash;

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
        BossVictoryTrack.NoiseScrollV => "노이즈 UV 수직 이동",
        BossVictoryTrack.CameraBob => "카메라 두둥실",
        BossVictoryTrack.Rectify => "배경 글자 RECTIFY",
        BossVictoryTrack.RectifyFill => "RECTIFY 안쪽 색",
        BossVictoryTrack.GroundCircle => "발밑 회색 원",
        BossVictoryTrack.AssassinDash => "어쌔신 순간이동·Q 이동",
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
        BossVictoryMarker.ChargeStart => "Q 차징 시작(거너)",
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

    /// <summary>카메라 두둥실 화면 세로 오프셋(화면 높이 %) = 클립마다 <see cref="BossVictoryClip.BobOffset"/> 의 합(267 후속).</summary>
    public float CameraBobOffset(float t)
    {
        if (cameraBob == null) return 0f;
        float v = 0f;
        for (int i = 0; i < cameraBob.Count; i++) v += cameraBob[i].BobOffset(t);
        return v;
    }

    /// <summary>노이즈 UV 수직 오프셋 = 클립마다 <see cref="BossVictoryClip.UvOffset"/> 의 합(이어 붙이면 이어서 흐른다 — 267 후속).</summary>
    public float NoiseScrollOffset(float t)
    {
        if (noiseScrollV == null) return 0f;
        float v = 0f;
        for (int i = 0; i < noiseScrollV.Count; i++) v += noiseScrollV[i].UvOffset(t);
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

    /// <summary>컷 <paramref name="index"/> 바로 앞 컷(시작 순 — 같은 시작이면 번호 순, <see cref="CameraCutEnd"/> 와 같은 순서). 첫 컷이면 -1.</summary>
    public int PreviousCameraCut(int index)
    {
        float start = cameraCut[index].start;
        int best = -1;
        for (int j = 0; j < cameraCut.Count; j++)
        {
            float s = cameraCut[j].start;
            if (j == index || s > start || (s == start && j > index)) continue;
            if (best < 0 || s > cameraCut[best].start || (s == cameraCut[best].start && j > best)) best = j;
        }
        return best;
    }

    /// <summary>컷 블렌드 최대(초) = 앞 컷 길이(앞 컷 시작 ~ 이 컷 시작) · 첫 컷이면 컷 시작 시각(처치 순간 전으로 못 감, 255 후속 8).</summary>
    public float CameraCutBlendLimit(int index)
    {
        int prev = PreviousCameraCut(index);
        return Mathf.Max(0f, cameraCut[index].start - (prev >= 0 ? cameraCut[prev].start : 0f));
    }

    /// <summary>컷 블렌드 시간(초) — 칸 값을 최대로 자른 것. 0 = 툭 끊김.</summary>
    public float CameraCutBlendSeconds(int index) => Mathf.Clamp(CameraCutShot(index).blendSeconds, 0f, CameraCutBlendLimit(index));

    /// <summary>
    /// 시각 <paramref name="t"/> 에 블렌드 중인 컷(들어오는 컷) — 블렌드 구간 = [컷 시작 − 블렌드, 컷 시작). 그 동안 화면은 지금 컷(<see cref="ActiveCameraCut"/>,
    /// 첫 컷이면 게임 카메라)에서 들어오는 컷 첫 화면으로 섞인다. <paramref name="weight"/> = 곡선 뒤 섞는 정도(0 → 1). 없으면 -1·weight 1.
    /// </summary>
    public int BlendingCameraCut(float t, out float weight)
    {
        weight = 1f;
        for (int i = 0; i < cameraCut.Count; i++)
        {
            float b = CameraCutBlendSeconds(i);
            if (b <= 0f) continue;
            float start = cameraCut[i].start;
            if (t < start - b || t >= start) continue;
            weight = BossVictoryKeys.Ease(CameraCutShot(i).blendCurve, (t - (start - b)) / b);
            return i;
        }
        return -1;
    }

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
    /// <summary>보스 노드. <paramref name="trimAt"/> = 피격 애니가 자른 끝에 닿은 글리치 시각(아직·안 자름이면 음수, 255 후속 10).</summary>
    public BossVictoryBossTimeScale.Nodes BossNodes(bool hasHitAnim, float trimAt = -1f) => new BossVictoryBossTimeScale.Nodes
    {
        HasHitAnim = hasHitAnim,
        Trimmed = trimAt >= 0f,
        TrimAt = trimAt,
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

    // ── 다른 캐릭터 프로필에 노드 복사(267 — 타임라인 창 '다른 캐릭터에 복사') ──

    /// <summary>
    /// 마커(하나뿐인 노드)를 <paramref name="src"/> 와 같은 시각으로 덮어쓴다. 애니 클립 노드(스킬 연출 시작·보스 피격 애니 시작)는 끝 자르기도 같이.
    /// </summary>
    public void CopyMarkerFrom(BossVictoryTimeline src, BossVictoryMarker marker)
    {
        SetMarkerTime(marker, src.MarkerTime(marker));
        if (marker == BossVictoryMarker.FinisherStart) finisherTrimSeconds = src.finisherTrimSeconds;
        else if (marker == BossVictoryMarker.BossHitAnim) bossHitTrimSeconds = src.bossHitTrimSeconds;
    }

    /// <summary>
    /// <paramref name="src"/> 의 클립 하나를 같은 시각으로 복사 — 하나뿐인 트랙(TV 꺼짐·처치자 평타)은 덮어쓰기, 나머지는 새로 추가.
    /// 키 목록·카메라 컷 값은 새 목록(공유 안 함). 결과 클립 번호(덮어쓰기면 0).
    /// </summary>
    public int CopyClipFrom(BossVictoryTimeline src, BossVictoryTrack track, int index)
    {
        BossVictoryClip c = src.Clips(track)[index].WithCopiedKeys();
        List<BossVictoryClip> clips = Clips(track);
        int at;
        if (KindOf(track) == TrackKind.Single && clips.Count > 0)
        {
            clips[0] = c;
            at = 0;
        }
        else
        {
            clips.Add(c);
            at = clips.Count - 1;
        }
        if (track == BossVictoryTrack.CameraCut)
        {
            SyncCameraCutShots();   // 앞 번호 보정 뒤 이 클립 값
            cameraCutShots[at] = src.CameraCutShot(index).WithCopiedKeys();
            FitCameraCutLengths();
        }
        return at;
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

    /// <summary>발밑 회색 원 기본 클립 — 노란 틴트 시작(0.3)부터 0.6초에 걸쳐 커져 끝까지(옛 246: 주황 들어오는 순간부터 0.6초 smoothstep, 씬 끝까지 유지).</summary>
    public const float DefaultGroundCircleStart = 0.3f;
    public const float DefaultGroundCircleGrow = 0.6f;

    /// <summary>
    /// 이 트랙이 생기기 전 에셋이면 기본 원 클립을 한 번 넣는다(멱등 — 넣은 표시를 남겨 사용자가 지운 뒤엔 다시 안 넣음). 넣었으면 true.
    /// 세기 키 이관(<see cref="MigrateKeys"/>) 전에 부른다.
    /// </summary>
    /// <summary>
    /// 발밑 회색 원 중심(267 후속 — 사용자: 카메라 기준) — 카메라 바로 아래 바닥 점(카메라 x·z, 높이 = 처치자 발 높이), 매 프레임 따라감.
    /// 단 '카메라 처치자로' 마커 전(<paramref name="focusKillerTime"/>)은 피어마다 게임 카메라가 달라 처치자 발밑(연출 시작 위치)을 쓴다 — 모든 피어 같은 화면.
    /// 카메라가 없으면 처치자 발밑.
    /// </summary>
    public static Vector3 GroundCircleCenter(float t, float focusKillerTime, Vector3 killerFloor, bool hasCamera, Vector3 cameraPos)
    {
        if (!hasCamera || t < focusKillerTime) return killerFloor;
        return new Vector3(cameraPos.x, killerFloor.y, cameraPos.z);
    }

    /// <summary>카메라 두둥실 기본 클립 — 0.3~5.6초, 들어올 때 0.2초·나갈 때 0.3초, 레퍼런스 값(0.22%·1.6초·위상 0.25).</summary>
    public static BossVictoryClip DefaultCameraBobClip()
    {
        BossVictoryClip c = BossVictoryClip.Make(DefaultGroundCircleStart, DefaultEnd - DefaultGroundCircleStart, 0.2f, 0.3f);
        c.bobAmplitude = DefaultBobAmplitude;
        c.bobPeriod = DefaultBobPeriod;
        c.bobPhase = 0.25f;
        return c;
    }

    /// <summary>이 트랙 전 에셋이면 기본 두둥실 클립을 한 번 넣는다(멱등 — 지운 뒤엔 다시 안 넣음). 세기 키 이관 전에 부른다. 넣었으면 true.</summary>
    public bool SeedCameraBob()
    {
        if (cameraBobSeeded) return false;
        cameraBobSeeded = true;
        cameraBob ??= new List<BossVictoryClip>();
        if (cameraBob.Count > 0) return false;
        cameraBob.Add(DefaultCameraBobClip());
        return true;
    }

    /// <summary>배경 글자 기본 클립 — 0.3초(노란 틴트 시작)부터 0.1초 동안 찢기며 들어와 TV 꺼짐 시작(4.5초)까지(TV 꺼짐 캡처에 남는다).</summary>
    public const float DefaultRectifyStart = 0.3f, DefaultRectifyFadeIn = 0.1f, DefaultRectifyEnd = 4.5f;

    /// <summary>이 트랙 전 에셋이면 기본 글자 클립을 한 번 넣는다(멱등 — 지운 뒤엔 다시 안 넣음). 세기 키 이관 전에. 넣었으면 true.</summary>
    /// <summary>RECTIFY 안쪽 색 기본 클립 — 글자가 다 나타난 0.4초부터 4.5초(TV 꺼짐 시작)까지 반복.</summary>
    public const float DefaultRectifyFillStart = 0.4f;

    public bool SeedRectifyFill()
    {
        if (rectifyFillSeeded) return false;
        rectifyFillSeeded = true;
        rectifyFill ??= new List<BossVictoryClip>();
        if (rectifyFill.Count > 0) return false;
        rectifyFill.Add(BossVictoryClip.Make(DefaultRectifyFillStart, DefaultRectifyEnd - DefaultRectifyFillStart));
        return true;
    }

    /// <summary>시각 t 에 켜진 첫 클립(시작 순) — 클립 시작 기준 시간표가 있는 트랙용(RECTIFY 안쪽 색). 없으면 false.</summary>
    public bool TryActiveClip(BossVictoryTrack track, float t, out BossVictoryClip clip)
    {
        List<BossVictoryClip> clips = Clips(track);
        clip = default;
        bool found = false;
        for (int i = 0; i < clips.Count; i++)
        {
            BossVictoryClip c = clips[i];
            if (t < c.start || t > c.End) continue;
            if (!found || c.start < clip.start) { clip = c; found = true; }
        }
        return found;
    }

    public bool SeedRectify()
    {
        if (rectifySeeded) return false;
        rectifySeeded = true;
        rectify ??= new List<BossVictoryClip>();
        if (rectify.Count > 0) return false;
        rectify.Add(BossVictoryClip.Make(DefaultRectifyStart, DefaultRectifyEnd - DefaultRectifyStart, DefaultRectifyFadeIn));
        return true;
    }

    public bool SeedGroundCircle()
    {
        if (groundCircleSeeded) return false;
        groundCircleSeeded = true;
        groundCircle ??= new List<BossVictoryClip>();
        if (groundCircle.Count > 0) return false;
        groundCircle.Add(BossVictoryClip.Make(DefaultGroundCircleStart, DefaultEnd - DefaultGroundCircleStart, DefaultGroundCircleGrow));
        return true;
    }

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
        tl.SeedGroundCircle();
        tl.SeedCameraBob();
        tl.SeedRectify();
        tl.SeedRectifyFill();
        tl.MigrateKeys();   // 효과 세기 클립 = 세기 키(같은 화면 — 255 후속 6)
        return tl;
    }
}
