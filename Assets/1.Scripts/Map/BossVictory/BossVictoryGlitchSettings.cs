using UnityEngine;

/// <summary>
/// 보스 처치 연출 — ZZZ 글리치(PLAN-boss-victory-glitch) 수치. 정본 에셋: <c>Resources/BossVictory/BossVictoryGlitchSettings.asset</c>
/// (없으면 코드 기본값). 단계가 늘 때마다 칸을 더한다 — 2단계 = 대상별 시간 배율·처치 연출 스킬 · 3단계 = 글리치 타임라인(→ 클립 목록 <see cref="BossVictoryTimeline"/>) · 4단계 = 카메라 컷 · 6단계 = TV 꺼짐.
/// </summary>
[CreateAssetMenu(menuName = "MainProject/Boss Victory Glitch Settings", fileName = "BossVictoryGlitchSettings")]
public sealed class BossVictoryGlitchSettings : ScriptableObject, ISerializationCallbackReceiver
{
    public const string ResourcePath = "BossVictory/BossVictoryGlitchSettings";

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
    [Tooltip("캐릭터 Variant 에 PlayerVictoryFinisher 가 없을 때 쓰는 스킬 칸. 그 스킬의 애니·이펙트를 표시로만 재생하고 클립 End 이벤트에서 처치자·보스가 느려진다.")]
    public PlayerSkillSlot defaultFinisherSlot = PlayerSkillSlot.Ultimate;

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

    [Header("보스 피격 애니(처치 연출 — 타임라인 '보스 피격 애니 시작' 마커, 255 후속 5)")]
    [Tooltip("보스 Animator 상태 이름 — 처치 연출에서 처음부터 튼다. 23호 기본 getowned(클립 Boss_23_ParryHit_InPlace, 루트모션 없음). 비우면 마지막 피격 자세 그대로(255 후속 4 까지와 같음).")]
    public string bossHitStateName = "getowned";
    [Tooltip("보스 피격 애니로 넘어가는 CrossFade(초). 0 = 바로.")]
    [Min(0f)] public float bossHitFadeSeconds = 0.05f;

    public enum HideOthersTarget { Teammates, TeammatesAndAttachedEffects }

    [Header("마커 옵션(시각 = 타임라인 마커, 255 후속 3)")]
    [Tooltip("카메라 → 처치자: 0 = 즉시 컷(기본), > 0 = 이 시간(초) 동안 부드럽게 옮김(ease in/out).")]
    [Min(0f)] public float focusKillerBlendSeconds;
    [Tooltip("처치자 외 숨김 대상 — 팀원만(기본) / 팀원 + 팀원에게 붙어 따라다니는 이펙트(EffectManager 추종 이펙트). 소환물은 지금 플레이어 캐릭터에 없다.")]
    public HideOthersTarget hideOthersTarget = HideOthersTarget.Teammates;

    [Header("타임라인(트랙별 클립·마커 — 창 Tools/Boss Victory/처치 연출 타임라인 에서 편집)")]
    [Tooltip("t = 처치 판정 순간(복제 처치 시각) 뒤 초. 화면 효과 세기·HUD 숨김·TV 꺼짐·카메라 컷 = 클립, 카메라 처치자 포커스·처치자 외 숨김·로비 전환 = 마커. 처치 연출 스킬은 이 속에서 재생.")]
    public BossVictoryTimeline timeline = BossVictoryTimeline.CreateDefault();

    [Header("글리치 색·모양")]
    [Tooltip("배경 호박색(sRGB).")]
    public Color amberColor = new Color32(0xD2, 0xA6, 0x4E, 0xFF);
    [Min(1f)] public float scanlineCount = 360f;

    [Header("가로줄 찢김 모양")]
    [Tooltip("찢김 줄 기본 굵기(px, 화면 해상도 기준).")]
    [Min(1f)] public float tearLinePixels = 3f;
    [Tooltip("줄 굵기 최대 배수 — 줄마다 기본 굵기 × 1~이 값(가는 줄·조금 굵은 줄이 섞인다).")]
    [Min(1f)] public float tearLineMaxMultiplier = 3f;
    [Tooltip("최대 밀림 거리(화면 너비 비율) — 줄마다 0~이 값(짧은 밀림이 많고 긴 밀림은 드물다), 좌우 무작위.")]
    [Range(0f, 1f)] public float tearMaxShift = 0.12f;
    [Tooltip("찢김 세기 1 일 때 켜지는 줄 비율(빈도).")]
    [Range(0f, 1f)] public float tearDensity = 0.35f;
    [Tooltip("찢김 무늬가 바뀌는 횟수(초당).")]
    [Min(1f)] public float tearRefreshRate = 24f;
    [Tooltip("밝은 부분 끌림 길이(화면 너비 비율).")]
    [Range(0f, 0.5f)] public float tearStreakLength = 0.08f;

    public enum NoiseSpace { Screen, Boss }

    [Header("보스 노이즈(실루엣 안 흑백 노이즈 UV 애니메이션 — 흰 = 밝은 색, 검 = 어두운 색)")]
    public Texture2D bossNoiseTexture;
    [Tooltip("Screen = 화면 공간(띠 굵기가 화면 기준 일정 — 레퍼런스 추천) · Boss = 보스 기준(보스 화면 높이 = 1, 보스와 같이 움직이고 클로즈업에서 커진다).")]
    public NoiseSpace bossNoiseSpace = NoiseSpace.Screen;
    [Tooltip("타일링(화면 세로 또는 보스 화면 높이 1 당 반복 수).")]
    public Vector2 bossNoiseTiling = new Vector2(1f, 1f);
    [Tooltip("카메라 이동 → 노이즈 UV 배율(카메라 1m 당 UV, x = U · y = V). 시간 흐름은 없다 — 카메라가 움직일 때만 움직인다.\n" +
             "카메라 오른쪽 이동 → U 감소 · 왼쪽 → U 증가 · 위 → V 증가 · 아래 → V 감소(카메라 right/up 축 투영). 컷(텔레포트) 프레임은 뺀다.")]
    public Vector2 bossNoiseCameraScale = new Vector2(0.1f, 0.1f);
    public Color bossNoiseBrightColor = new Color32(0xFF, 0xF6, 0xEA, 0xFF);
    [Tooltip("밝은 색 배율(과노출 느낌 — 블룸이 받는다).")]
    [Min(0f)] public float bossNoiseBrightIntensity = 1.6f;
    public Color bossNoiseDarkColor = new Color32(0x3A, 0x17, 0x12, 0xFF);

    // 카메라 컷 목록은 타임라인 '카메라 컷' 트랙(timeline.cameraCut·cameraCutShots — 255, 옛 cuts 배열 삭제). 예: 와이드(0.33·거리 16·각 22·방위 50) → 보스 클로즈업(1.67·7·6·−160) → 와이드(2.6).
    [Header("카메라 컷 벽 숨김(컷·컷별 켜기/끄기 = 타임라인 '카메라 컷' 트랙)")]
    [Tooltip("연출 카메라에서 앞면이 확실히 카메라를 향하지 않는 벽을 끈다 — 법선·시선 내적 문턱(옛 연출과 같은 기준).")]
    [Range(-1f, 1f)] public float wallFacingThreshold = 0.3f;
    [Tooltip("벽 숨김 후보 반경(m, 처치자·보스 가운데 기준).")]
    [Min(1f)] public float wallSearchRadius = 40f;

    [Header("TV 꺼짐(시각·길이 = 타임라인 TV 꺼짐 클립)")]
    [Tooltip("CRT 꺼짐 발광 색(타이틀과 같은 파랑이 기본).")]
    public Color crtOffGlowColor = new Color(0.25f, 0.62f, 1f, 1f);

    [Header("셰이더(빌드 포함용 참조 — 비우면 Shader.Find)")]
    public Shader maskShader;
    public Shader compositeShader;
    [Tooltip("Title/CRTOff(UI/Title/Shaders/TitleCRTOff.shader).")]
    public Shader crtOffShader;

    static BossVictoryGlitchSettings _cached;

    /// <summary>Resources 에셋, 없으면 코드 기본값 인스턴스.</summary>
    void ISerializationCallbackReceiver.OnBeforeSerialize() { }

    // 옛 '처치자 동작 끝 → 백대쉬' 마커 → 처치자 평타 클립·백대쉬 시작·스킬 연출 시작(255 후속 4 — 사용자 값 손실 0). 다음 저장 때 새 형식으로 남는다.
    void ISerializationCallbackReceiver.OnAfterDeserialize() => timeline?.MigrateLegacyKillerActionEnd(backDashDuration, backDashIdleDelay);

    public static BossVictoryGlitchSettings Load()
    {
        if (_cached != null) return _cached;
        _cached = Resources.Load<BossVictoryGlitchSettings>(ResourcePath);
        if (_cached == null)
        {
            Debug.LogWarning($"[BossVictory] Resources/{ResourcePath} 가 없어 코드 기본값으로 돈다.");
            _cached = CreateInstance<BossVictoryGlitchSettings>();
        }
        return _cached;
    }
}
