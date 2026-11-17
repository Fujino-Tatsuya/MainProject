using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// 보스 처치 연출 캐릭터별 프로필(할 일 267 — A안: 캐릭터별 값을 에셋 통째로). 타임라인 전체(카메라 컷·처치자 평타·마커·끝 자르기·느려짐·보스 노드·화면 효과 세기 클립) +
/// 백대쉬 + 대상별 시간 배율 + 처치 연출 스킬 칸 + 팔레트(271 — 칸마다 독립 색 17칸, <see cref="BossVictoryPalette"/>). 화면 효과 모양(노이즈·찢김·CRT·셰이더·벽 숨김 기준)은 공용 <see cref="BossVictoryGlitchSettings"/>.
/// 고르는 곳 = 공용 설정의 '캐릭터 → 프로필' 표(<see cref="BossVictoryGlitchSettings.characterProfiles"/>), 없거나 비면 기본 프로필(<see cref="BossVictoryGlitchSettings.defaultProfile"/>).
/// 정본 에셋 = <c>Resources/BossVictory/Profiles/</c>(저작 메뉴 <c>Tools/Boss Victory/캐릭터별 프로필 만들기 (267)</c>).
/// </summary>
[CreateAssetMenu(menuName = "MainProject/Boss Victory Profile", fileName = "BossVictoryProfile")]
public sealed class BossVictoryProfile : ScriptableObject, ISerializationCallbackReceiver
{
    [Header("대상별 시간 배율(전역 timeScale 아님 — 각 피어 로컬 애니·이펙트)")]
    [Tooltip("처치자 — 처치 순간 하던 동작(평타·스킬 등 루프 아닌 클립)을 이 배율로(타임라인 처치자 평타 클립 끝 또는 그 클립 끝까지). 그 뒤 백대쉬·처치 연출 스킬은 1.")]
    [Range(0.05f, 1f)] public float killerCurrentActionScale = 0.5f;
    [Tooltip("처치자 — 느려짐 시작 마커(꺼짐이면 처치 연출 스킬 End) 뒤 배율(그 전엔 1).")]
    [Range(0f, 1f)] public float killerAfterFinisherScale = 0.1f;
    [Tooltip("처치자 외 플레이어 — 처치 판정 즉시 배율.")]
    [Range(0f, 1f)] public float otherPlayersScale = 0.05f;
    [Tooltip("보스 — 보스 슬로우 마커(꺼짐이면 느려짐 시작 → 처치 연출 스킬 End) 뒤 배율. 그 전 = 피격 애니 ×1 / 보스 정지 0 / 처치 순간 0(마지막 피격 자세).")]
    [Range(0f, 1f)] public float bossAfterFinisherScale = 0.1f;

    [Header("처치 연출 스킬(Q9)")]
    [Tooltip("이 프로필 캐릭터의 처치 연출 스킬 칸. 그 스킬의 애니·이펙트를 표시로만 재생하고 클립 End 이벤트(또는 '느려짐 시작' 마커)에서 처치자·보스가 느려진다.")]
    [FormerlySerializedAs("defaultFinisherSlot")] public PlayerSkillSlot finisherSlot = PlayerSkillSlot.Ultimate;

    [Header("백대쉬(처치 연출 전용 — 기본값 = 보스 등장 밀림 BossEncounterDirector impactSkid 값 복사, 255 후속 3)")]
    [Tooltip("백대쉬 거리(m). 보스 등장 밀림과 따로 — 등장 연출은 영향 없음.")]
    [Min(0f)] public float backDashDistance = 1.5f;
    [Tooltip("백대쉬 시간(초).")]
    [Min(0.05f)] public float backDashDuration = 0.35f;
    [Tooltip("백대쉬 이동 곡선(기본 ease out = 보스 등장 밀림 SkidEase 와 같은 식).")]
    public BossVictoryCameraCuts.Ease backDashEase = BossVictoryCameraCuts.Ease.EaseOut;
    [Tooltip("방향 보정(도) — 0 = 보스 반대쪽, + = 위에서 봐 시계 방향으로 돌림.")]
    [Range(-180f, 180f)] public float backDashAngle;
    [Tooltip("백대쉬 후 대기(초) — 백대쉬 끝 ~ Idle 복귀. 기본 0.35 = 보스 등장 밀림 ReactionIdleDelay(등장 연출은 상수 그대로).")]
    [Min(0f)] public float backDashIdleDelay = 0.35f;
    [Tooltip("스킬 연출 시작 때 백대쉬를 끊지 않고 같이 간다(267 후속) — 스킬 애니가 몸을 쥐고 백대쉬는 이동만(밀림 리액션 애니·끝 Idle 복귀 없음). 스킬 연출 시작이 백대쉬 시작보다 앞이어도 백대쉬는 자기 마커 시각에 시작. 하던 평타는 스킬 재생(0층 CrossFade 0.05초)이 끊는다. 표시만(각 피어), 판정·RPC 무관.")]
    public bool finisherWithBackDash;

    [Header("처치자 검(269 — 팔라딘만. 처치자가 PaladinSkillView 를 가질 때만 동작, 다른 캐릭터는 무시)")]
    [Tooltip("처치 연출 시작부터 끝(끝·취소)까지 손에 든 검에 번개를 두른다 — 팔라딘 R 칼날 전기(FX_Sword_Electric) 재사용, 검을 따라간다(손 검 크기는 그대로). 엔트리 파트 delay(0.45초)만큼 늦게 보인다. 느려짐 구간에도 원래 속도. 각 피어 표시만(새 RPC 없음).")]
    public bool handSwordLightning;
    [Tooltip("처치 연출의 낙하 검(R 마무리 낙뢰 FX_Lightening_Strike — 하늘에서 떨어지는 검) 크기 배율(1 = 그대로). 이 연출에서만 — 평소 R 은 그대로.")]
    [Min(0.01f)] public float fallingSwordScale = 1f;
    [Tooltip("처치 연출의 낙하 검이 떨어지는 자리 = 보스 기준 오프셋(+Z = 보스 정면, 보스 수평 회전 따라감). 높이는 무시하고 그 아래 바닥에 꽂힌다. 0 = 지금처럼 보스 아래. 타임라인 창이 열려 있으면 Scene 뷰 핸들로 끈다. 이 연출에서만.")]
    public Vector3 fallingSwordOffset;
    [Tooltip("처치 연출의 낙하 검이 꽂힌 채 화면에 남는 시간(초) — 생기는 시각은 그대로(처치 연출 스킬 클립 Custom0 이벤트), 꽂혀 아직 선명할 때(엔트리 길이 × 0.75) 멈췄다가 이 시간 뒤 원래 끝 연출(페이드)로 사라진다. 처치 연출이 끝나면(로비 전환·취소) 남은 검은 걷는다. 0 = 엔트리 그대로(2.2초). 이 연출에서만.")]
    [Min(0f)] public float fallingSwordLife;

    [Header("처치자 이동(272 — 백대쉬 / 순간이동 Q(어쌔신) / 후퇴 차징(거너)). 이동은 처치자 오너가 실제로, 다른 피어는 복제 위치. 특성 연출 없음")]
    [Tooltip("백대쉬 = 지금 흐름(백대쉬 → 처치 연출 스킬). 순간이동 = 타임라인 '어쌔신 순간이동·Q 이동' 클립마다 그 시작 지점으로 순간이동 → 클립 길이 동안 아래 Q 상태로 도착 지점까지(처치 연출 스킬 대신, 마지막 뒤 정지 프레임). " +
             "후퇴 차징 = 제자리 — 보스와 '후퇴 거리'보다 가까우면 백대쉬 시작에 후퇴 스킬(E) 표시 + 백대쉬. Q 차징은 가깝든 멀든 '거너 Q' 줄 차징 시작 마커에(294) → 1·2·3단계 → 발사.")]
    public BossVictoryKillerMove killerMove = BossVictoryKillerMove.BackDash;

    [Tooltip("[순간이동] 클립마다 트는 애니 상태(기본 = Combo_Attack_03_04 를 든 상태) — Custom0 프레임부터, Custom0 → End 가 클립 길이에 맞게. 이 클립의 End 는 '스킬 끝'이 아니다. " +
             "정지 프레임 = 타임라인 '처치 연출 스킬' 클립 끝 자르기(기본 71프레임) — 마지막 클립 뒤에만.")]
    public string teleportMoveState = BossVictoryKillerMoves.DefaultTeleportMoveState;
    [Tooltip("[순간이동] 이동 곡선(가로 = 클립 0~1, 세로 = 시작 지점 → 도착 지점 0~1) — 모든 클립 공용.")]
    public AnimationCurve teleportMoveEase = AnimationCurve.Linear(0f, 0f, 1f, 1f);

    [Tooltip("[후퇴 차징] 처치 순간 보스와 수평 거리(m)가 이보다 짧으면 후퇴 스킬 + 백대쉬(백대쉬 칸 그대로, 보스 반대쪽) 뒤 Q 차징. 같거나 멀면 바로 Q 차징. 각 피어가 자기 화면의 처치자 위치로 판정.")]
    [Min(0f)] public float retreatDistance = 5f;
    [Tooltip("[후퇴 차징] 가까울 때 백대쉬와 같이 표시로만 트는 스킬 칸(거너 E 냉각 백스텝 — 애니·이펙트만, 이동은 백대쉬).")]
    public PlayerSkillSlot retreatSkillSlot = PlayerSkillSlot.Sub;
    [Tooltip("[후퇴 차징] 거너 Q 3단계 차징·큰 레이저(294) — 단계별 구체 크기·알림·과충전·섬광·레이저 굵기·길이·오프셋·지연·유지. 시각 = 타임라인 '거너 Q' 줄 마커 5개.")]
    public BossVictoryGunnerQ gunnerQ = new BossVictoryGunnerQ();

    // 옛 칸(272 — 시작 → 목표 크기·성장 곡선) — 읽기만 해서 gunnerQ·단계 마커로 옮긴다(294, OnEnable). 옮긴 뒤·새 프로필 = 시작 크기 -1.
    [SerializeField, HideInInspector] float chargeScaleFrom = -1f;
    [SerializeField, HideInInspector] float chargeScaleTo = 1f;
    [SerializeField, HideInInspector] AnimationCurve chargeScaleCurve;

    [Header("타임라인(트랙별 클립·마커 — 창 Tools/Boss Victory/처치 연출 타임라인 에서 캐릭터를 골라 편집)")]
    [Tooltip("t = 처치 판정 순간(복제 처치 시각) 뒤 초. 화면 효과 세기·HUD 숨김·TV 꺼짐·카메라 컷 = 클립, 카메라 처치자 포커스·처치자 외 숨김·로비 전환 = 마커. 처치 연출 스킬은 이 속에서 재생.")]
    public BossVictoryTimeline timeline = BossVictoryTimeline.CreateDefault();

    [Header("팔레트(271 — 칸마다 독립 색, 칸 순서 = BossVictoryPaletteSlot)")]
    [Tooltip("화면 효과 색 17칸(배경 호박색·보스 노이즈·RECTIFY 외곽선·①②③ 원·회색·주황 채움·글로우·TV 꺼짐·보스 림). 칸끼리 관계 없음 — 한 칸을 바꿔도 다른 칸은 그대로(11-16 관계 끊음, 그때 화면 색을 구워 넣음). 편집은 타임라인 창 '팔레트' 칸.")]
    [ColorUsage(true, true)] public Color[] paletteColors = BossVictoryPalette.CreateDefaultColors();

    [Header("RECTIFY 주황 채움(캐릭터별) — 흰 원이 다 퍼진 뒤(11-12 공용 설정에서 옮김)")]
    [Tooltip("흰 원이 다 퍼진 뒤 흰색으로 머무는 시간(초).")]
    [Min(0f)] public float rectifyOrangeHold = BossVictoryOrangeLook.DefaultHold;
    [Tooltip("흰 → 주황 와이프 시간(초) — 레퍼런스: 가로줄마다 들쭉날쭉한 경계가 한쪽에서 지나감(약 6프레임).")]
    [Min(0f)] public float rectifyOrangeWipe = BossVictoryOrangeLook.DefaultWipe;
    [Tooltip("와이프 방향 — 켬 = 오른쪽에서 왼쪽.")]
    public bool rectifyOrangeRightToLeft = BossVictoryOrangeLook.DefaultRightToLeft;
    [Tooltip("와이프 경계 줄마다 어긋남(단어 폭 비율).")]
    [Range(0f, 1f)] public float rectifyOrangeJitter = BossVictoryOrangeLook.DefaultJitter;
    [Tooltip("와이프 줄 굵기(px).")]
    [Min(1f)] public float rectifyOrangeRowPixels = BossVictoryOrangeLook.DefaultRowPixels;
    [Tooltip("좌→우 색 보간 세기(0 = 왼쪽 색만).")]
    [Range(0f, 1f)] public float rectifyOrangeGradient = BossVictoryOrangeLook.DefaultGradient;
    [Tooltip("안쪽 주사선 간격(화면 높이 비율).")]
    [Min(0.0005f)] public float rectifyScanlineSpacing = BossVictoryOrangeLook.DefaultScanlineSpacing;
    [Tooltip("안쪽 주사선 어둡기(0 = 없음 — 기본. 다시 켜려면 0.25 근처).")]
    [Range(0f, 1f)] public float rectifyScanlineDark = BossVictoryOrangeLook.DefaultScanlineDark;

    [Header("보스 표현(캐릭터별, 11-12) — '보스 노이즈' 트랙 구간 동안 보스를 노이즈 이미지 / 역림라이트로")]
    [Tooltip("노이즈 = 보스 노이즈 텍스처(지금까지). 림라이트 = 보스 안쪽 전부 밝은 색 × 세기(블룸이 받는 과노출) · 테두리(프레넬 — 카메라 기준 모든 가장자리)만 어두운 색. 색 = 팔레트 칸 '보스 림 안쪽'·'보스 림 테두리'.")]
    public BossVictoryBossLookMode bossLook = BossVictoryBossLook.DefaultMode;
    [Tooltip("림라이트 — 테두리 굵기(0~1, 프레넬 문턱 = 1 − 굵기). 클수록 어두운 테두리가 안쪽까지.")]
    [Range(0f, 1f)] public float bossRimWidth = BossVictoryBossLook.DefaultRimWidth;
    [Tooltip("림라이트 — 테두리 경계 부드러움(0 = 칼같이).")]
    [Range(0f, 1f)] public float bossRimSoftness = BossVictoryBossLook.DefaultRimSoftness;
    [Tooltip("림라이트 — 안쪽 밝은 색 배율(1 넘으면 블룸이 받는 과노출).")]
    [Min(0f)] public float bossRimInsideIntensity = BossVictoryBossLook.DefaultInsideIntensity;

    void ISerializationCallbackReceiver.OnBeforeSerialize() { }

    // 옛 형식 타임라인(255 후속 4·6 전)을 읽으면 새 형식으로 옮긴다 — 설정 SO 에 있던 이관을 프로필로(267).
    void ISerializationCallbackReceiver.OnAfterDeserialize()
    {
        if (timeline == null) return;
        timeline.MigrateLegacyKillerActionEnd(backDashDuration, backDashIdleDelay);
        timeline.SeedGroundCircle();   // 발밑 회색 원 트랙 전 에셋 = 기본 클립 한 번(267 후속)
        timeline.SeedCameraBob();      // 카메라 두둥실 트랙 전 에셋 = 기본 클립 한 번(267 후속)
        timeline.SeedRectify();        // 배경 글자 트랙 전 에셋 = 기본 클립 한 번(267 후속)
        timeline.SeedRectifyFill();    // RECTIFY 안쪽 색 트랙 전 에셋 = 기본 클립 한 번(267 후속)
        timeline.MigrateKeys();
    }

    // 거너 Q 옛 시작 → 목표 크기 → 단계 마커(294) — 곡선 Evaluate 를 쓰므로 직렬화 콜백(메인 스레드 아닐 수 있음) 대신 OnEnable(메인 스레드, 불러온 뒤).
    void OnEnable() => MigrateLegacyGunnerCharge();

    /// <summary>
    /// 옛 거너 Q 차징 칸(시작 → 목표 크기·성장 곡선, 272)이 남아 있으면 단계 마커 시각·크기로 옮기고(<see cref="BossVictoryGunnerCharge.Migrate"/>) 옛 칸을 비운다(멱등).
    /// 옮겼으면 true. 새 프로필·옮긴 뒤 = 아무것도 안 함.
    /// </summary>
    public bool MigrateLegacyGunnerCharge()
    {
        if (chargeScaleFrom < 0f || timeline == null) return false;
        gunnerQ ??= new BossVictoryGunnerQ();
        BossVictoryGunnerCharge.Migrate(gunnerQ, timeline, chargeScaleFrom, chargeScaleTo, chargeScaleCurve);
        chargeScaleFrom = -1f;
        chargeScaleCurve = null;
        return true;
    }

    /// <summary>다른 프로필 값을 통째로 덮어쓴다(키 목록·컷 값까지 새 목록 — 원본과 공유하지 않음).</summary>
    public void CopyFrom(BossVictoryProfile source)
    {
        if (source == null || source == this) return;
        JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(source), this);
    }

    /// <summary>코드 기본값 인스턴스(에셋 없음 — 저장 안 함).</summary>
    public static BossVictoryProfile CreateDefault()
    {
        var p = CreateInstance<BossVictoryProfile>();
        p.hideFlags = HideFlags.DontSave;
        p.name = "BossVictoryProfile(코드 기본값)";
        return p;
    }
}

/// <summary>RECTIFY 주황 채움 값(캐릭터별, 11-12) — 처치자 프로필에서 읽어 렌더에 넘긴다. 프로필 null = 코드 기본값.</summary>
public readonly struct BossVictoryOrangeLook
{
    public const float DefaultHold = 0.05f;
    public const float DefaultWipe = 0.1f;
    public const bool DefaultRightToLeft = false;
    public const float DefaultJitter = 0.2f;
    public const float DefaultRowPixels = 4f;
    public const float DefaultGradient = 1f;
    public const float DefaultScanlineSpacing = 0.003f;
    public const float DefaultScanlineDark = 0f;

    public readonly float Hold, Wipe, Jitter, RowPixels, Gradient, ScanlineSpacing, ScanlineDark;
    public readonly bool RightToLeft;

    BossVictoryOrangeLook(float hold, float wipe, bool rightToLeft, float jitter, float rowPixels,
        float gradient, float scanlineSpacing, float scanlineDark)
    {
        Hold = Mathf.Max(0f, hold);
        Wipe = Mathf.Max(0f, wipe);
        RightToLeft = rightToLeft;
        Jitter = Mathf.Clamp01(jitter);
        RowPixels = Mathf.Max(1f, rowPixels);
        Gradient = Mathf.Clamp01(gradient);
        ScanlineSpacing = scanlineSpacing;
        ScanlineDark = Mathf.Clamp01(scanlineDark);
    }

    public static BossVictoryOrangeLook Default => new BossVictoryOrangeLook(DefaultHold, DefaultWipe, DefaultRightToLeft, DefaultJitter,
        DefaultRowPixels, DefaultGradient, DefaultScanlineSpacing, DefaultScanlineDark);

    public static BossVictoryOrangeLook From(BossVictoryProfile p) => p == null ? Default
        : new BossVictoryOrangeLook(p.rectifyOrangeHold, p.rectifyOrangeWipe, p.rectifyOrangeRightToLeft, p.rectifyOrangeJitter,
            p.rectifyOrangeRowPixels, p.rectifyOrangeGradient,
            p.rectifyScanlineSpacing, p.rectifyScanlineDark);
}

/// <summary>보스 표현(캐릭터별, 11-12) — '보스 노이즈' 트랙 구간 동안의 보스 그림. 🔴 직렬화 값 — 새 값은 끝에.</summary>
public enum BossVictoryBossLookMode
{
    Noise = 0,     // 보스 노이즈 텍스처(지금까지)
    RimLight = 1,  // 역림라이트 — 안쪽 밝게(과노출)·프레넬 테두리 어둡게
}

/// <summary>보스 표현 값(처치자 프로필에서 읽어 렌더에 넘긴다, 프로필 null = 코드 기본값 = 노이즈).</summary>
public readonly struct BossVictoryBossLook
{
    public const BossVictoryBossLookMode DefaultMode = BossVictoryBossLookMode.Noise;
    public const float DefaultRimWidth = 0.35f;
    public const float DefaultRimSoftness = 0.1f;
    public const float DefaultInsideIntensity = 2f;

    public readonly BossVictoryBossLookMode Mode;
    public readonly float RimWidth, RimSoftness, InsideIntensity;

    public BossVictoryBossLook(BossVictoryBossLookMode mode, float rimWidth, float rimSoftness, float insideIntensity)
    {
        Mode = mode;
        RimWidth = Mathf.Clamp01(rimWidth);
        RimSoftness = Mathf.Clamp01(rimSoftness);
        InsideIntensity = Mathf.Max(0f, insideIntensity);
    }

    public bool IsRim => Mode == BossVictoryBossLookMode.RimLight;

    /// <summary>프레넬 문턱(이 값부터 테두리) = 1 − 굵기.</summary>
    public float RimThreshold => 1f - RimWidth;

    public static BossVictoryBossLook Default => new BossVictoryBossLook(DefaultMode, DefaultRimWidth, DefaultRimSoftness, DefaultInsideIntensity);

    public static BossVictoryBossLook From(BossVictoryProfile p) => p == null ? Default
        : new BossVictoryBossLook(p.bossLook, p.bossRimWidth, p.bossRimSoftness, p.bossRimInsideIntensity);

    /// <summary>
    /// 림 가중치(셰이더와 같은 식 — EditMode 확인용). <paramref name="fresnel"/> = 1 − saturate(N·V)(0 = 정면, 1 = 가장자리).
    /// 0 = 안쪽(밝은 색 × 세기), 1 = 테두리(어두운 색).
    /// </summary>
    public float RimWeight(float fresnel)
    {
        float t = RimThreshold;
        float lo = t - RimSoftness, hi = t + RimSoftness + 1e-4f;
        return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((fresnel - lo) / (hi - lo)));
    }
}
