using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 보스 처치 연출 — ZZZ 글리치(PLAN-boss-victory-glitch) 공용 수치. 정본 에셋: <c>Resources/BossVictory/BossVictoryGlitchSettings.asset</c>
/// (없으면 코드 기본값). 화면 효과 모양(글리치 색·노이즈·찢김·CRT·셰이더·벽 숨김 기준)·보스 피격 애니·마커 옵션은 여기(공용).
/// <b>캐릭터별 값</b>(타임라인 전체·백대쉬·시간 배율·처치 연출 스킬 칸)은 <see cref="BossVictoryProfile"/> — '캐릭터 → 프로필' 표(<see cref="characterProfiles"/>)로 고르고,
/// 없거나 비면 기본 프로필(<see cref="defaultProfile"/>, 정본은 비어 있어 코드 기본값 — 11-12). 할 일 267(Docs/tech/boss-victory-character-profiles.md).
/// </summary>
[CreateAssetMenu(menuName = "MainProject/Boss Victory Glitch Settings", fileName = "BossVictoryGlitchSettings")]
public sealed class BossVictoryGlitchSettings : ScriptableObject
{
    public const string ResourcePath = "BossVictory/BossVictoryGlitchSettings";

    /// <summary>표 한 줄 — 캐릭터(스폰 전 고른 캐릭터, 로스터 <see cref="TutorialCharacter"/>) → 프로필.</summary>
    [Serializable]
    public struct CharacterProfile
    {
        public TutorialCharacter character;
        public BossVictoryProfile profile;
    }

    [Header("캐릭터별 프로필(267 — 처치자 캐릭터로 고름, 네트워크 추가 없음)")]
    [Tooltip("표에 없거나 프로필이 빈 캐릭터(처치자를 못 찾음 포함)가 쓰는 프로필. 비우면 코드 기본값 프로필(BossVictoryProfile.CreateDefault) — 정본은 비워 둔다(11-12, 기본 프로필 에셋 삭제).")]
    public BossVictoryProfile defaultProfile;
    [Tooltip("캐릭터 → 프로필. 같은 캐릭터가 두 줄이면 앞 줄. 줄을 비우면 그 캐릭터는 기본 프로필(비었으면 코드 기본값).")]
    public List<CharacterProfile> characterProfiles = new List<CharacterProfile>();

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


    [Header("글리치 색·모양")]
    [Tooltip("배경 호박색(sRGB) = 팔레트 기준 색의 기준(271). 이 SO 의 색들은 이 색 기준 관계로 해석돼 처치자 프로필 baseColor 를 따라간다(TV 꺼짐 발광 = 대비 기준 색의 기준). 실제 색 = BossVictoryPaletteCalc.")]
    public Color amberColor = new Color32(0xD2, 0xA6, 0x4E, 0xFF);
    [Min(1f)] public float scanlineCount = 360f;
    [Tooltip("CRT 주사선을 바닥에서 뺀다(267 후속) — 깊이로 복원한 표면 법선의 위쪽 성분(normal.y)이 이 값 이하 = 벽(주사선 그대로).")]
    [Range(0f, 1f)] public float scanlineFloorNormalMin = 0.55f;
    [Tooltip("normal.y 가 이 값 이상 = 바닥(주사선 없음). 사이는 부드럽게 섞는다. 하늘(먼 평면)·플레이어·보스는 늘 주사선, RECTIFY 글자 위는 주사선 없음.")]
    [Range(0f, 1f)] public float scanlineFloorNormalMax = 0.8f;
    [Tooltip("바닥에 남기는 주사선 비율(0 = 바닥 없음, 1 = 예전처럼 전체).")]
    [Range(0f, 1f)] public float scanlineFloorKeep = 0f;
    [Tooltip("하늘(먼 평면 깊이) 호박색 밝기 하한(267 후속) — 호박색 = 흑백 명도 × 색이라 검은 하늘은 칙칙한 겨자색이 된다. 하늘만 명도를 이 값 이상으로 올린다(0 = 예전대로).")]
    [Range(0f, 1f)] public float skyAmberMinLuma = 0.6f;
    [Tooltip("배경 호박색 명도 리프트(267 후속 — 하이키) — 호박색 명도 = 리프트 + (1 − 리프트) × 원래 명도. 어두운 벽도 밝은 호박색이 된다. 0 = 예전(곱하기 그대로). 하늘 하한은 그 위에.")]
    [Range(0f, 1f)] public float amberLumaLift = 0.45f;
    [Tooltip("CRT 주사선 어둡기(267 후속) — 어두운 줄 밝기 = 1 − 이 값(예전 고정 0.28). 벽·하늘·플레이어·보스 위 주사선 전부.")]
    [Range(0f, 1f)] public float scanlineDarkness = 0.15f;

    [Header("처치 연출 전용 볼륨(267 후속 — 연출 동안만, 각 피어 로컬)")]
    [Tooltip("연출 동안 맵 볼륨 위에 덮는 전역 볼륨을 켠다(글리치 시작 ~ 연출 끝·취소). 끄면 맵 볼륨 그대로.")]
    public bool victoryVolume = true;
    [Tooltip("연출 동안 비네트 세기(맵 볼륨 0.22 를 덮는다). 0 = 비네트 없음.")]
    [Range(0f, 1f)] public float victoryVignetteIntensity = 0f;
    [Tooltip("연출 동안 노출(Post Exposure)도 덮을지. 끄면 맵 볼륨 값(0.25) 그대로.")]
    public bool victoryOverrideExposure;
    [Tooltip("덮을 노출(EV). 맵 볼륨은 0.25.")]
    public float victoryPostExposure = 0.25f;

    [Header("가로줄 찢김 모양")]
    [Tooltip("찢김 줄 기본 굵기(px, 화면 해상도 기준).")]
    [Min(1f)] public float tearLinePixels = 1.75f;
    [Tooltip("줄 굵기 최대 배수 — 줄마다 기본 굵기 × 1~이 값(가는 줄·조금 굵은 줄이 섞인다).")]
    [Min(1f)] public float tearLineMaxMultiplier = 1.75f;
    [Tooltip("최대 밀림 거리(화면 너비 비율) — 줄마다 0~이 값(짧은 밀림이 많고 긴 밀림은 드물다), 좌우 무작위.")]
    [Range(0f, 1f)] public float tearMaxShift = 0.04f;
    [Tooltip("찢김 세기 1 일 때 켜지는 줄 비율(빈도).")]
    [Range(0f, 1f)] public float tearDensity = 0.9f;
    [Tooltip("찢김 무늬가 바뀌는 횟수(초당).")]
    [Min(1f)] public float tearRefreshRate = 30f;
    [Tooltip("밝은 부분 끌림 길이(화면 너비 비율).")]
    [Range(0f, 0.5f)] public float tearStreakLength = 0.025f;

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

    [Header("발밑 회색 원(246 복원 — 켜는 시각·커지는 시간 = 타임라인 '발밑 회색 원' 클립)")]
    [Tooltip("원 목표 반경(m, 수평 거리) — 클립 세기 1 일 때. 중심 = 카메라 바로 아래 바닥(매 프레임 따라감, 높이 = 처치자 발 — '카메라 처치자로' 마커 전엔 처치자 발밑). 위에서 봤을 때 원 안(높이 한도 없음 — 원기둥)의 배경은 " +
             "호박색 대신 원래 무늬를 살린 흑백. 플레이어·보스 노이즈는 그대로. 0 = 끔.")]
    [Min(0f)] public float groundCircleRadius = 6f;
    [Tooltip("원 가장자리 부드러움(m) — 반경 안쪽으로 이만큼에 걸쳐 흑백 → 호박색.")]
    [Min(0f)] public float groundCircleSoftness = 1f;
    [Tooltip("원 안 흑백 밝기 배율(원래 명도 × 이 값).")]
    [Min(0f)] public float groundCircleBrightness = 1.8f;

    [Header("배경 글자 RECTIFY(267 후속 — 켜는 시각·알파 = 타임라인 '배경 글자 RECTIFY' 클립)")]
    [Tooltip("폰트(SVN Assets/50.Art/UI/Fonts/JalnanGothicTTF). 비면 글자 없음.")]
    public Font rectifyFont;
    [Tooltip("글자(최대 8자).")]
    public string rectifyText = "RECTIFY";
    [Tooltip("글자 전체 폭(화면 너비 비율) — 1 넘으면 양끝 글자가 조금 잘린다.")]
    [Min(0.05f)] public float rectifyWidth = 1.06f;
    [Tooltip("대문자 높이 가운데(뷰포트 세로, 0 아래 · 1 위) — 레퍼런스 = 화면 위에서 45%.")]
    [Range(0f, 1f)] public float rectifyCenterY = 0.55f;
    [Tooltip("외곽선 두께(대문자 높이 비율). 안쪽은 비운다.")]
    [Range(0.005f, 0.1f)] public float rectifyOutline = 0.04f;
    public Color rectifyColor = Color.white;

    /// <summary>RECTIFY 안쪽 원 하나(267 후속 — 원 3개 통합). 시각은 'RECTIFY 안쪽 색' 클립 시작 기준 초.</summary>
    [Serializable]
    public struct RectifyCircle
    {
        [Tooltip("생성 시각(초, 클립 시작 기준).")]
        [Min(0f)] public float start;
        [Tooltip("시작 반지름(단어 높이 비율) — 이 크기로 생겨 페이드한다.")]
        [Min(0f)] public float startRadius;
        [Tooltip("페이드 인(초) — 시작 반지름 그대로 알파 0→1.")]
        [Min(0f)] public float fade;
        [Tooltip("확장(초, easeOutCubic) — 시작 반지름 → 단어 사각형 네 모서리까지. 마지막(흰) 원은 회색→흰 전환이 끝난 순간부터.")]
        [Min(0.01f)] public float expand;
        [Tooltip("중심(단어 사각형 UV 0~1).")]
        public Vector2 center;
        [Tooltip("왼쪽 색(단어 사각형 UV x = 0).")]
        public Color leftColor;
        [Tooltip("오른쪽 색(UV x = 1) — 원 안 색은 왼쪽 → 오른쪽 보간.")]
        public Color rightColor;

        public static RectifyCircle Make(float start, float startRadius, float fade, float expand, Vector2 center, Color left, Color right) =>
            new RectifyCircle { start = start, startRadius = startRadius, fade = fade, expand = expand, center = center, leftColor = left, rightColor = right };
    }

    public static RectifyCircle[] DefaultRectifyCircles() => new[]
    {
        RectifyCircle.Make(0f, 0.15f, 0.15f, 0.6f, new Vector2(0.46f, 0.5f), new Color32(0xFF, 0x2D, 0xB4, 0xFF), new Color32(0xA0, 0x40, 0xFF, 0xFF)),   // ① 마젠타 → 보라
        RectifyCircle.Make(4f / 60f, 0.15f, 0.15f, 0.6f, new Vector2(0.46f, 0.5f), new Color32(0x5A, 0xC8, 0xFF, 0xFF), new Color32(0x24, 0x38, 0xA8, 0xFF)),   // ② 하늘 → 남색
        RectifyCircle.Make(0.2f, 0.15f, 0.15f, 0.6f, new Vector2(0.46f, 0.5f), Color.white, new Color32(0xFF, 0xF6, 0xE0, 0xFF)),   // ③ 흰 → 연크림(회색 단계 뒤)
    };

    [Header("RECTIFY 안쪽 원 3개(267 후속 — 켜는 구간 = 타임라인 'RECTIFY 안쪽 색' 클립)")]
    [Tooltip("원 ① 마젠타 → ② 블루 → ③ 흰색. 원마다 생성 시각·시작 반지름·페이드·확장·중심·좌우 색. 나중 원이 앞 원을 덮고(painter's order) 마지막 원이 다 덮으면 고정.\n" +
             "③은 작게 생겨 불투명한 회색으로 페이드 → 회색에서 흰색으로 전환 → 흰색이 된 순간부터 확장. 3개만 쓴다.")]
    public RectifyCircle[] rectifyCircles = DefaultRectifyCircles();
    [Tooltip("③(흰 원)이 페이드되는 동안의 회색(불투명).")]
    public Color rectifyCircleGrayColor = new Color32(0x8A, 0x8A, 0x8A, 0xFF);
    [Tooltip("③ 회색 → 흰색 전환 시간(초) — 페이드가 끝난 뒤. 전환이 끝난 순간부터 확장.")]
    [Min(0f)] public float rectifyCircleGrayToWhite = 0.1f;
    [Tooltip("원 가장자리 부드러움(단어 높이 비율). 화면 픽셀보다 작으면 픽셀 기준 AA.")]
    [Min(0f)] public float rectifyFillSoftness = 0.01f;
    [Tooltip("색(원·주황 채움)이 지나간 자리는 흰 외곽선도 그 색으로 덮는다 — 채움을 외곽선 바깥 경계까지 넓힌다(267 후속). 끄면 예전처럼 안쪽만 칠하고 외곽선은 늘 흰색.")]
    public bool rectifyFillCoversOutline = true;

    // 옛 칸(링 3개 + 따로 있던 흰 원, 267 후속 원 3개 통합 전) — 옛 에셋 값을 rectifyCircles 로 한 번 옮기는 데만 쓴다(MigrateRectifyCircles). 새 인스턴스는 비어 있어 옮기지 않는다.
    [SerializeField, HideInInspector] int rectifyCirclesVersion;
    [HideInInspector] public Color[] rectifyFillColors = new Color[0];
    [HideInInspector] public Vector2 rectifyFillCenter = new Vector2(0.46f, 0.5f);
    [HideInInspector] public float rectifyFillStartRadius = 0.15f;
    [HideInInspector] public float rectifyFillDelay = 4f / 60f;
    [HideInInspector] public float rectifyFillFade = 0.15f;
    [HideInInspector] public float rectifyFillExpand = 0.6f;
    [HideInInspector] public Color rectifyWhiteColor = Color.white;
    [HideInInspector] public float rectifyWhiteStart = 0.2f;
    [HideInInspector] public Vector2 rectifyWhiteCenter = new Vector2(0.46f, 0.5f);
    [HideInInspector] public float rectifyWhiteStartRadius = 0.15f;
    [HideInInspector] public float rectifyWhiteFade = 0.15f;
    [HideInInspector] public float rectifyWhiteExpand = 0.6f;

    void OnEnable() => MigrateRectifyCircles();

    /// <summary>
    /// 옛 링·흰 원 값 → 원 3개(한 번만). ① = 링 0(생성 0 · 색 0 을 왼쪽 색) · ② = 링 1(생성 = 옛 간격, 색 = 새 블루 그라데이션) ·
    /// ③ = 옛 흰 원(생성·반지름·페이드·확장·중심, 흰색 = 왼쪽 색), 회색 = 옛 링 2 색(있으면). 옛 방사형 그라데이션은 없어졌다. 옮겼으면 true.
    /// </summary>
    public bool MigrateRectifyCircles()
    {
        if (rectifyCirclesVersion >= 1 || rectifyFillColors == null || rectifyFillColors.Length == 0) return false;   // 옮겼거나 새 인스턴스(옛 값 없음)
        rectifyCirclesVersion = 1;
        RectifyCircle[] c = DefaultRectifyCircles();
        c[0] = RectifyCircle.Make(0f, rectifyFillStartRadius, rectifyFillFade, rectifyFillExpand, rectifyFillCenter, rectifyFillColors[0], c[0].rightColor);
        c[1] = RectifyCircle.Make(rectifyFillDelay, rectifyFillStartRadius, rectifyFillFade, rectifyFillExpand, rectifyFillCenter, c[1].leftColor, c[1].rightColor);
        c[2] = RectifyCircle.Make(rectifyWhiteStart, rectifyWhiteStartRadius, rectifyWhiteFade, rectifyWhiteExpand, rectifyWhiteCenter, rectifyWhiteColor, c[2].rightColor);
        if (rectifyFillColors.Length > 2) rectifyCircleGrayColor = rectifyFillColors[2];
        rectifyCircles = c;
        rectifyFillColors = new Color[0];
        return true;
    }

    [Header("RECTIFY 주황 채움 — 흰 원이 다 퍼진 뒤(267 후속)")]
    [Tooltip("흰 원이 다 퍼진 뒤 흰색으로 머무는 시간(초).")]
    [Min(0f)] public float rectifyOrangeHold = 0.05f;
    [Tooltip("흰 → 주황 와이프 시간(초) — 레퍼런스: 가로줄마다 들쭉날쭉한 경계가 한쪽에서 지나감(약 6프레임).")]
    [Min(0f)] public float rectifyOrangeWipe = 0.1f;
    [Tooltip("와이프 방향 — 켬 = 오른쪽에서 왼쪽(레퍼런스).")]
    public bool rectifyOrangeRightToLeft = true;
    [Tooltip("와이프 경계 줄마다 어긋남(단어 폭 비율).")]
    [Range(0f, 1f)] public float rectifyOrangeJitter = 0.2f;
    [Tooltip("와이프 줄 굵기(px).")]
    [Min(1f)] public float rectifyOrangeRowPixels = 4f;
    [Tooltip("주황 채움 왼쪽 색(W 쪽).")]
    public Color rectifyOrangeLeft = new Color32(0xFF, 0xA5, 0x3A, 0xFF);
    [Tooltip("주황 채움 오른쪽 색(T 쪽).")]
    public Color rectifyOrangeRight = new Color32(0xF8, 0xE8, 0x62, 0xFF);
    [Tooltip("좌→우 색 보간 세기(0 = 왼쪽 색만).")]
    [Range(0f, 1f)] public float rectifyOrangeGradient = 1f;
    [Tooltip("안쪽 주사선 간격(화면 높이 비율).")]
    [Min(0.0005f)] public float rectifyScanlineSpacing = 0.003f;
    [Tooltip("안쪽 주사선 어둡기(0 = 없음 — 기본, 267 후속 사용자 요청으로 RECTIFY 위 주사선을 뺐다. 다시 켜려면 0.25 근처).")]
    [Range(0f, 1f)] public float rectifyScanlineDark = 0f;

    public enum RectifyGlowTarget { WhiteCircle, Orange, Both }

    [Header("RECTIFY 전용 글로우(267 후속 — 맵 블룸과 무관, 글자 채움만 빛남)")]
    [Tooltip("빛 세기(더하기 배율). 0 = 끔(글로우 패스를 아예 건너뛴다).")]
    [Min(0f)] public float rectifyGlowIntensity = 1f;
    [Tooltip("빛 퍼짐 반경(실제 화면 픽셀). 절반 해상도 텍스처에서 9탭 가로·세로 블러 — 한 칸 간격 = 반경 ÷ 4.")]
    [Range(1f, 96f)] public float rectifyGlowRadius = 24f;
    [Tooltip("빛 색(채움 색에 곱하기). 흰색 = 채움 색 그대로.")]
    public Color rectifyGlowColor = Color.white;
    [Tooltip("어느 채움이 빛날지 — 흰 원 / 주황 채움 / 둘 다.")]
    public RectifyGlowTarget rectifyGlowTarget = RectifyGlowTarget.Both;

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
    static BossVictoryProfile _codeDefaultProfile;

    /// <summary>이 캐릭터의 프로필 — 표 → 기본 프로필 → 코드 기본값(기본 프로필이 비었을 때 — 정본은 비어 있다, 11-12). null 아님.</summary>
    public BossVictoryProfile ProfileFor(TutorialCharacter character)
    {
        BossVictoryProfile p = BossVictoryProfileSelect.Select(characterProfiles, character, defaultProfile);
        if (p != null) return p;
        if (_codeDefaultProfile == null)
        {
            Debug.Log($"[BossVictory] {name} — 표에 없는 캐릭터({character})는 코드 기본값 프로필로 돈다(기본 프로필 칸 비어 있음).");
            _codeDefaultProfile = BossVictoryProfile.CreateDefault();
        }
        return _codeDefaultProfile;
    }

    /// <summary>표에 그 캐릭터 줄(프로필 있음)이 있나 — 없으면 기본 프로필을 쓴다.</summary>
    public bool HasOwnProfile(TutorialCharacter character) =>
        character != TutorialCharacter.None && BossVictoryProfileSelect.Select(characterProfiles, character, null) != null;

    /// <summary>Resources 에셋, 없으면 코드 기본값 인스턴스.</summary>
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

/// <summary>'캐릭터 → 프로필' 표 고르기(순수 함수 — EditMode <c>BossVictoryProfileSelectTests</c>). 267.</summary>
public static class BossVictoryProfileSelect
{
    /// <summary>
    /// 표에서 <paramref name="character"/> 의 첫 줄 프로필(비지 않은 것). 캐릭터 모름(None)·줄 없음·프로필 빔 → <paramref name="fallback"/>(기본 프로필).
    /// </summary>
    public static BossVictoryProfile Select(IReadOnlyList<BossVictoryGlitchSettings.CharacterProfile> table, TutorialCharacter character, BossVictoryProfile fallback)
    {
        if (table == null || character == TutorialCharacter.None) return fallback;
        for (int i = 0; i < table.Count; i++)
        {
            if (table[i].character != character) continue;
            if (table[i].profile != null) return table[i].profile;
        }
        return fallback;
    }
}
