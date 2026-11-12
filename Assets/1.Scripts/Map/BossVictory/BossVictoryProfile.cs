using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// 보스 처치 연출 캐릭터별 프로필(할 일 267 — A안: 캐릭터별 값을 에셋 통째로). 타임라인 전체(카메라 컷·처치자 평타·마커·끝 자르기·느려짐·보스 노드·화면 효과 세기 클립) +
/// 백대쉬 + 대상별 시간 배율 + 처치 연출 스킬 칸 + 팔레트 기준 색(271). 화면 효과 모양(노이즈·찢김·CRT·셰이더·벽 숨김 기준)과 기준(팔라딘) 색은 공용 <see cref="BossVictoryGlitchSettings"/> — 실제 색 = <see cref="BossVictoryPaletteCalc"/>.
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
    [Tooltip("처치 연출의 낙하 검 수명(초) — 생기는 시각은 그대로(처치 연출 스킬 클립 Custom0 이벤트), 이 시간 뒤 걷는다. 0 = 엔트리 수명 그대로. 엔트리보다 길게 잡으면 파티클이 끝난 뒤 빈 시간만 늘어난다. 이 연출에서만.")]
    [Min(0f)] public float fallingSwordLife;

    [Header("처치자 이동(272 — 백대쉬 / 순간이동 Q(어쌔신) / 후퇴 차징(거너)). 이동은 처치자 오너가 실제로, 다른 피어는 복제 위치. 특성 연출 없음")]
    [Tooltip("백대쉬 = 지금 흐름(백대쉬 → 처치 연출 스킬). 순간이동 = 타임라인 '어쌔신 순간이동·Q 이동' 클립마다 그 시작 지점으로 순간이동 → 클립 길이 동안 아래 Q 상태로 도착 지점까지(처치 연출 스킬 대신, 마지막 뒤 정지 프레임). " +
             "후퇴 차징 = 제자리 — 보스와 '후퇴 거리'보다 가까우면 백대쉬 시작에 후퇴 스킬(E) 표시 + 백대쉬, 'Q 차징 시작' 마커에 처치 연출 스킬(Q) / 멀면 백대쉬 시작 시각에 바로 Q.")]
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
    [Tooltip("[후퇴 차징] Q 차징 구체 시작 크기(배율 — 거너 평소 0.25).")]
    [Min(0f)] public float chargeScaleFrom = 0.25f;
    [Tooltip("[후퇴 차징] Q 차징 구체 목표 크기 — 최대 충전(발사 순간)에 이 크기. 레이저 폭은 그대로.")]
    [Min(0f)] public float chargeScaleTo = 1f;
    [Tooltip("[후퇴 차징] 차징 구체 성장 곡선(가로 = 충전 0~1, 세로 = 시작 → 목표 0~1).")]
    public AnimationCurve chargeScaleCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

    [Header("타임라인(트랙별 클립·마커 — 창 Tools/Boss Victory/처치 연출 타임라인 에서 캐릭터를 골라 편집)")]
    [Tooltip("t = 처치 판정 순간(복제 처치 시각) 뒤 초. 화면 효과 세기·HUD 숨김·TV 꺼짐·카메라 컷 = 클립, 카메라 처치자 포커스·처치자 외 숨김·로비 전환 = 마커. 처치 연출 스킬은 이 속에서 재생.")]
    public BossVictoryTimeline timeline = BossVictoryTimeline.CreateDefault();

    [Header("팔레트(271 — 기준 색 하나로 화면 효과 색 자동, 계산 = BossVictoryPaletteCalc)")]
    [Tooltip("이 캐릭터 기준 색. 공용 설정 색들은 '공용 amberColor 기준' 관계로 해석해 이 색을 따라간다(색조 회전 + 채도·밝기 비율). 공용 amberColor 와 같으면 공용 색 그대로.")]
    public Color baseColor = BossVictoryPaletteCalc.DefaultBaseColor;
    [Tooltip("대비 그룹(① 마젠타·② 블루 원 좌우·TV 꺼짐 발광) 기준 색 — 자동 = 기준 색 색조 + 색조 차이 / 직접 지정 = 아래 대비 색.")]
    public BossVictoryContrastMode contrastMode = BossVictoryContrastMode.Auto;
    [Tooltip("자동일 때 대비 기준 색 색조 = 기준 색 색조 + 이 값(도). 기본 = 공용 TV 꺼짐 파랑 − 공용 amberColor 에서 역산(팔라딘 결과 그대로).")]
    [Range(-180f, 180f)] public float contrastHueOffset = BossVictoryPaletteCalc.DefaultContrastHueOffset;
    [Tooltip("직접 지정일 때 대비 기준 색(TV 꺼짐 발광 색이 그대로 이 색).")]
    public Color contrastColor = BossVictoryPaletteCalc.DefaultContrastColor;
    [Tooltip("자동일 때 대비 기준 색 채도 하한 — 기준 색이 회색에 가까워도 대비색이 죽지 않게.")]
    [Range(0f, 1f)] public float contrastMinSaturation = BossVictoryPaletteCalc.DefaultContrastMinSaturation;
    [Tooltip("자동일 때 대비 기준 색 밝기 하한 — 기준 색이 어두워도 대비색이 보이게.")]
    [Range(0f, 1f)] public float contrastMinValue = BossVictoryPaletteCalc.DefaultContrastMinValue;
    [Tooltip("칸별 직접 지정(기본 = 비어 있음 = 전부 자동). manual 켬 = 그 칸은 이 색 그대로.")]
    public List<BossVictoryPaletteOverride> paletteOverrides = new List<BossVictoryPaletteOverride>();

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
    [Tooltip("주황 채움 왼쪽 색(W 쪽) — 팔레트 칸 OrangeLeft 의 기준 색(기준 색을 따라 자동 변환, 칸 직접 지정이 있으면 그 색).")]
    public Color rectifyOrangeLeft = BossVictoryOrangeLook.DefaultLeft;
    [Tooltip("주황 채움 오른쪽 색(T 쪽) — 팔레트 칸 OrangeRight 의 기준 색.")]
    public Color rectifyOrangeRight = BossVictoryOrangeLook.DefaultRight;
    [Tooltip("좌→우 색 보간 세기(0 = 왼쪽 색만).")]
    [Range(0f, 1f)] public float rectifyOrangeGradient = BossVictoryOrangeLook.DefaultGradient;
    [Tooltip("안쪽 주사선 간격(화면 높이 비율).")]
    [Min(0.0005f)] public float rectifyScanlineSpacing = BossVictoryOrangeLook.DefaultScanlineSpacing;
    [Tooltip("안쪽 주사선 어둡기(0 = 없음 — 기본. 다시 켜려면 0.25 근처).")]
    [Range(0f, 1f)] public float rectifyScanlineDark = BossVictoryOrangeLook.DefaultScanlineDark;

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
    public static readonly Color DefaultLeft = new Color(1f, 0.2696447f, 0f, 1f);
    public static readonly Color DefaultRight = new Color(1f, 0f, 0.455451f, 1f);
    public const float DefaultGradient = 1f;
    public const float DefaultScanlineSpacing = 0.003f;
    public const float DefaultScanlineDark = 0f;

    public readonly float Hold, Wipe, Jitter, RowPixels, Gradient, ScanlineSpacing, ScanlineDark;
    public readonly bool RightToLeft;
    /// <summary>기준 색(팔레트 칸 OrangeLeft/Right 의 원본 — 실제 그리는 색은 팔레트 결과).</summary>
    public readonly Color Left, Right;

    BossVictoryOrangeLook(float hold, float wipe, bool rightToLeft, float jitter, float rowPixels, Color left, Color right,
        float gradient, float scanlineSpacing, float scanlineDark)
    {
        Hold = Mathf.Max(0f, hold);
        Wipe = Mathf.Max(0f, wipe);
        RightToLeft = rightToLeft;
        Jitter = Mathf.Clamp01(jitter);
        RowPixels = Mathf.Max(1f, rowPixels);
        Left = left;
        Right = right;
        Gradient = Mathf.Clamp01(gradient);
        ScanlineSpacing = scanlineSpacing;
        ScanlineDark = Mathf.Clamp01(scanlineDark);
    }

    public static BossVictoryOrangeLook Default => new BossVictoryOrangeLook(DefaultHold, DefaultWipe, DefaultRightToLeft, DefaultJitter,
        DefaultRowPixels, DefaultLeft, DefaultRight, DefaultGradient, DefaultScanlineSpacing, DefaultScanlineDark);

    public static BossVictoryOrangeLook From(BossVictoryProfile p) => p == null ? Default
        : new BossVictoryOrangeLook(p.rectifyOrangeHold, p.rectifyOrangeWipe, p.rectifyOrangeRightToLeft, p.rectifyOrangeJitter,
            p.rectifyOrangeRowPixels, p.rectifyOrangeLeft, p.rectifyOrangeRight, p.rectifyOrangeGradient,
            p.rectifyScanlineSpacing, p.rectifyScanlineDark);
}
