using TMPro;
using UnityEngine;

/// <summary>
/// 보스 처치 연출 — ZZZ 글리치(PLAN-boss-victory-glitch) 수치. 정본 에셋: <c>Resources/BossVictory/BossVictoryGlitchSettings.asset</c>
/// (없으면 코드 기본값). 단계가 늘 때마다 칸을 더한다 — 2단계 = 대상별 시간 배율·처치 연출 스킬 · 3단계 = 글리치 타임라인 · 4단계 = 카메라 컷 · 5단계 = Rectify 글자.
/// </summary>
[CreateAssetMenu(menuName = "MainProject/Boss Victory Glitch Settings", fileName = "BossVictoryGlitchSettings")]
public sealed class BossVictoryGlitchSettings : ScriptableObject
{
    public const string ResourcePath = "BossVictory/BossVictoryGlitchSettings";

    [Header("대상별 시간 배율(전역 timeScale 아님 — 각 피어 로컬 애니·이펙트)")]
    [Tooltip("처치자 — 처치 연출 스킬이 End 에 닿은 뒤 배율(그 전엔 1).")]
    [Range(0f, 1f)] public float killerAfterFinisherScale = 0.1f;
    [Tooltip("처치자 외 플레이어 — 처치 판정 즉시 배율.")]
    [Range(0f, 1f)] public float otherPlayersScale = 0.05f;
    [Tooltip("보스 — 처치 연출 스킬이 End 에 닿은 뒤 배율(그 전엔 0 = 마지막 피격 자세로 정지).")]
    [Range(0f, 1f)] public float bossAfterFinisherScale = 0.1f;

    [Header("처치 연출 스킬(Q9)")]
    [Tooltip("캐릭터 Variant 에 PlayerVictoryFinisher 가 없을 때 쓰는 스킬 칸. 그 스킬의 애니·이펙트를 표시로만 재생하고 클립 End 이벤트에서 처치자·보스가 느려진다.")]
    public PlayerSkillSlot defaultFinisherSlot = PlayerSkillSlot.Ultimate;

    [Header("글리치 타임라인(3단계 — t = 처치 판정 순간(복제 처치 시각) 뒤 초. 처치 연출 스킬은 이 속에서 재생)")]
    [Tooltip("타임라인 길이(초). 이 뒤로는 마지막 값으로 정지 화면(유지 시간은 5단계).")]
    [Min(0.1f)] public float glitchDuration = 3.5f;
    [Tooltip("전체 세기(0 = 원래 화면). 0 이면 렌더 패스를 아예 건너뛴다.")]
    public AnimationCurve master = Ramp(0.08f, 0.15f);
    [Tooltip("화면 흑백(~0.17 부터 끝까지).")]
    public AnimationCurve desaturate = Ramp(0.1f, 0.2f);
    [Tooltip("배경 호박색 틴트(플레이어 제외 — 캐릭터는 흑백 유지). 와이드 컷(~0.33)부터.")]
    public AnimationCurve amber = Ramp(0.3f, 0.45f);
    [Tooltip("CRT 주사선(끝까지 유지).")]
    public AnimationCurve scanline = Ramp(0.1f, 0.2f);
    [Tooltip("가로줄 물결 왜곡.")]
    public AnimationCurve wave = Pulses(0.17f, 0.12f, 2.5f, 0.12f);
    [Tooltip("계단식 세로 어긋남.")]
    public AnimationCurve stepShift = Pulses(0.22f, 0.12f, 2.52f, 0.1f);
    [Tooltip("가로 늘어남.")]
    public AnimationCurve stretch = Pulses(0.22f, 0.1f);
    [Tooltip("블록 모자이크 — 컷 전환을 가린다(0.33 · 1.67 · 2.6).")]
    public AnimationCurve block = Pulses(0.33f, 0.08f, 1.67f, 0.06f, 2.58f, 0.1f);
    [Tooltip("가로 스미어.")]
    public AnimationCurve smear = Pulses(0.24f, 0.08f);
    [Tooltip("슬라이스.")]
    public AnimationCurve slice = Pulses(1.52f, 0.08f, 2.55f, 0.1f);
    [Tooltip("RGB 분리.")]
    public AnimationCurve rgbSplit = Pulses(1.52f, 0.08f, 2.55f, 0.1f);
    [Tooltip("흑백 플래시.")]
    public AnimationCurve flash = Pulses(1.34f, 0.06f);
    [Tooltip("플레이어 실루엣 가로 흔들림(캐릭터 글리치 1회).")]
    public AnimationCurve characterGlitch = Pulses(0.95f, 0.05f);
    [Tooltip("보스 노이즈 세기(보스 실루엣 안).")]
    public AnimationCurve bossNoise = Ramp(0.12f, 0.17f);
    [Tooltip("보스 외곽 가로 지글.")]
    public AnimationCurve bossJiggle = Ramp(0.12f, 0.17f, 0.5f);
    [Tooltip("HUD 숨김(0.5 넘으면 숨김).")]
    public AnimationCurve hudHide = Ramp(0.08f, 0.12f);

    [Header("글리치 색·모양")]
    [Tooltip("배경 호박색(sRGB).")]
    public Color amberColor = new Color32(0xD2, 0xA6, 0x4E, 0xFF);
    [Min(1f)] public float scanlineCount = 360f;
    [Min(0f)] public float waveFrequency = 60f;
    [Tooltip("블록 모자이크 블록 크기(px).")]
    [Min(1f)] public float blockPixelSize = 48f;
    [Tooltip("슬라이스 가로 띠 수.")]
    [Min(1f)] public float sliceCount = 40f;

    public enum NoiseSpace { Screen, Boss }

    [Header("보스 노이즈(실루엣 안 흑백 노이즈 UV 애니메이션 — 흰 = 밝은 색, 검 = 어두운 색)")]
    public Texture2D bossNoiseTexture;
    [Tooltip("Screen = 화면 공간(띠 굵기가 화면 기준 일정 — 레퍼런스 추천) · Boss = 보스 기준(보스 화면 높이 = 1, 보스와 같이 움직이고 클로즈업에서 커진다).")]
    public NoiseSpace bossNoiseSpace = NoiseSpace.Screen;
    [Tooltip("타일링(화면 세로 또는 보스 화면 높이 1 당 반복 수).")]
    public Vector2 bossNoiseTiling = new Vector2(1f, 1f);
    [Tooltip("스크롤(초당 UV) — 주 흐름 가로.")]
    public Vector2 bossNoiseScroll = new Vector2(0.6f, 0f);
    public Color bossNoiseBrightColor = new Color32(0xFF, 0xF6, 0xEA, 0xFF);
    [Tooltip("밝은 색 배율(과노출 느낌 — 블룸이 받는다).")]
    [Min(0f)] public float bossNoiseBrightIntensity = 1.6f;
    public Color bossNoiseDarkColor = new Color32(0x3A, 0x17, 0x12, 0xFF);

    [Header("카메라 컷(4단계 — 첫 컷 전엔 게임 카메라, 컷은 블렌드 없이 끊는다)")]
    [Tooltip("컷 목록(시작 시각 순서 무관 — 시각 t 에 시작이 t 이하인 것 중 가장 늦은 컷). 방위 0 = 처치자 쪽에서 보스를 봄, 180 = 반대편에서 처치자 쪽을 봄.\n" +
             "기본 = 빈 목록(게임 카메라만 — 11-09 사용자 결정, 처치자 애니를 보여 주는 카메라 이동과 함께 다시 짠다). 예: 와이드(0.33·거리 16·각 22·방위 50) → 보스 클로즈업(1.67·7·6·−160) → 와이드(2.6).")]
    public BossVictoryCameraCuts.Cut[] cuts = new BossVictoryCameraCuts.Cut[0];
    [Tooltip("연출 카메라에서 앞면이 확실히 카메라를 향하지 않는 벽을 끈다 — 법선·시선 내적 문턱(옛 연출과 같은 기준).")]
    [Range(-1f, 1f)] public float wallFacingThreshold = 0.3f;
    [Tooltip("벽 숨김 후보 반경(m, 처치자·보스 가운데 기준).")]
    [Min(1f)] public float wallSearchRadius = 40f;

    [Header("Rectify 글자(5단계 — 문구 = Strings UI.BossVictory.Rectify, 시계 = 글리치 시각)")]
    [Tooltip("글꼴 — 서울알림 Heavy SDF. 비우면 디렉터 연출 글꼴(introFont).")]
    public TMP_FontAsset textFont;
    [Tooltip("글자 가운데 위치(화면 비율 0~1).")]
    public Vector2 textViewportPosition = new Vector2(0.5f, 0.5f);
    [Min(1f)] public float textSize = 220f;
    public float textCharacterSpacing = 6f;
    [Tooltip("다 찬 글자 색(노랑).")]
    public Color textColor = new Color32(0xFF, 0xD2, 0x3A, 0xFF);
    public Color textOutlineColor = new Color32(0xFF, 0xD2, 0x3A, 0xFF);
    [Range(0f, 1f)] public float textOutlineWidth = 0.18f;
    [Tooltip("채움 앞머리가 지나는 동안 깜빡이는 색(왼쪽부터 다색).")]
    public Color[] textFillPalette =
    {
        new Color32(0x3D, 0xF5, 0xFF, 0xFF),
        new Color32(0xFF, 0x3D, 0x9A, 0xFF),
        new Color32(0xFF, 0xFF, 0xFF, 0xFF),
        new Color32(0x8C, 0xFF, 0x5A, 0xFF),
    };
    [Tooltip("채움 앞머리 띠 너비(글자 줄 길이 비율) — 이 안의 글자가 다색으로 깜빡인다.")]
    [Range(0.05f, 1f)] public float textFillBand = 0.35f;
    [Tooltip("외곽선 글자 세기(0~1).")]
    public AnimationCurve textOutline = Ramp(0.15f, 0.2f);
    [Tooltip("채움 진행(0 = 없음 → 1 = 끝 글자까지 노랑). 왼쪽부터.")]
    public AnimationCurve textFill = Ramp(0.3f, 0.6f);
    [Tooltip("흰 섬광(0~1).")]
    public AnimationCurve textFlash = Pulses(0.67f, 0.07f);
    [Tooltip("가로 흔들림 세기(0~1) — 글리치 펄스와 맞춘다.")]
    public AnimationCurve textJitter = Pulses(0.33f, 0.06f, 1.52f, 0.08f, 2.55f, 0.1f);
    [Min(0f)] public float textJitterPixels = 24f;

    [Header("셰이더(빌드 포함용 참조 — 비우면 Shader.Find)")]
    public Shader maskShader;
    public Shader compositeShader;

    // 기본 곡선 — a 에서 b 까지 0 → peak, 이후 유지.
    static AnimationCurve Ramp(float a, float b, float peak = 1f) =>
        new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(a, 0f), new Keyframe(b, peak), new Keyframe(10f, peak));

    // 기본 곡선 — (중심, 반폭) 쌍마다 0 → 1 → 0 펄스.
    static AnimationCurve Pulses(params float[] centerHalf)
    {
        var keys = new System.Collections.Generic.List<Keyframe> { new Keyframe(0f, 0f) };
        for (int i = 0; i + 1 < centerHalf.Length; i += 2)
        {
            float c = centerHalf[i], h = centerHalf[i + 1];
            keys.Add(new Keyframe(c - h, 0f));
            keys.Add(new Keyframe(c, 1f));
            keys.Add(new Keyframe(c + h, 0f));
        }
        return new AnimationCurve(keys.ToArray());
    }

    static BossVictoryGlitchSettings _cached;

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
