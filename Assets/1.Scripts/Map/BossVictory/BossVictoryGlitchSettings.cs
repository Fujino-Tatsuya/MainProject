using UnityEngine;

/// <summary>
/// 보스 처치 연출 — ZZZ 글리치(PLAN-boss-victory-glitch) 수치. 정본 에셋: <c>Resources/BossVictory/BossVictoryGlitchSettings.asset</c>
/// (없으면 코드 기본값). 단계가 늘 때마다 칸을 더한다 — 2단계 = 대상별 시간 배율·처치 연출 스킬 · 3단계 = 글리치 타임라인 · 4단계 = 카메라 컷 · 6단계 = 유지·CRT 꺼짐.
/// </summary>
[CreateAssetMenu(menuName = "MainProject/Boss Victory Glitch Settings", fileName = "BossVictoryGlitchSettings")]
public sealed class BossVictoryGlitchSettings : ScriptableObject
{
    public const string ResourcePath = "BossVictory/BossVictoryGlitchSettings";

    [Header("대상별 시간 배율(전역 timeScale 아님 — 각 피어 로컬 애니·이펙트)")]
    [Tooltip("처치자 — 처치 순간 하던 동작(평타·스킬 등 루프 아닌 클립)을 이 배율로 그 클립 끝까지. 그 뒤 백대쉬·처치 연출 스킬은 1.")]
    [Range(0.05f, 1f)] public float killerCurrentActionScale = 0.5f;
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
    [Tooltip("가로줄 좌우 찢김 세기(0~1) — 글리치는 이것 하나(사용자 11-09: 사각형·블록 노이즈 금지). 켜지는 줄 비율·밀림 거리에 곱한다.")]
    public AnimationCurve tear = Pulses(0.2f, 0.12f, 0.33f, 0.08f, 1.52f, 0.1f, 1.67f, 0.06f, 2.55f, 0.12f);
    [Tooltip("찢긴 줄의 밝은 부분 가로 끌림(스미어) 세기(0~1) — 찢긴 줄에만 걸린다.")]
    public AnimationCurve smear = Pulses(0.2f, 0.12f, 0.33f, 0.08f, 1.52f, 0.1f, 2.55f, 0.12f);
    [Tooltip("보스 노이즈 세기(보스 실루엣 안).")]
    public AnimationCurve bossNoise = Ramp(0.12f, 0.17f);
    [Tooltip("HUD 숨김(0.5 넘으면 숨김).")]
    public AnimationCurve hudHide = Ramp(0.08f, 0.12f);

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

    [Header("카메라 컷(4단계 — 첫 컷 전엔 게임 카메라, 컷은 블렌드 없이 끊는다)")]
    [Tooltip("컷 목록(시작 시각 순서 무관 — 시각 t 에 시작이 t 이하인 것 중 가장 늦은 컷). 방위 0 = 처치자 쪽에서 보스를 봄, 180 = 반대편에서 처치자 쪽을 봄.\n" +
             "기본 = 빈 목록(게임 카메라만 — 11-09 사용자 결정, 처치자 애니를 보여 주는 카메라 이동과 함께 다시 짠다). 예: 와이드(0.33·거리 16·각 22·방위 50) → 보스 클로즈업(1.67·7·6·−160) → 와이드(2.6).")]
    public BossVictoryCameraCuts.Cut[] cuts = new BossVictoryCameraCuts.Cut[0];
    [Tooltip("연출 카메라에서 앞면이 확실히 카메라를 향하지 않는 벽을 끈다 — 법선·시선 내적 문턱(옛 연출과 같은 기준).")]
    [Range(-1f, 1f)] public float wallFacingThreshold = 0.3f;
    [Tooltip("벽 숨김 후보 반경(m, 처치자·보스 가운데 기준).")]
    [Min(1f)] public float wallSearchRadius = 40f;

    [Header("끝 — 유지 → CRT 꺼짐 암전 → 로비 결과(6단계)")]
    [Tooltip("글리치 타임라인(glitchDuration)이 끝난 뒤 정지 화면 유지(초).")]
    [Min(0f)] public float holdSeconds = 1f;
    [Tooltip("CRT 꺼짐 길이(초) — 타이틀 → 로비 꺼짐(Title/CRTOff)과 같은 연출. 끝 = 검정, 호스트 화면에서 끝나면 서버가 로비(결과 패널)로 넘긴다.")]
    [Min(0.05f)] public float crtOffSeconds = 1.1f;
    [Tooltip("CRT 꺼짐 발광 색(타이틀과 같은 파랑이 기본).")]
    public Color crtOffGlowColor = new Color(0.25f, 0.62f, 1f, 1f);

    [Header("셰이더(빌드 포함용 참조 — 비우면 Shader.Find)")]
    public Shader maskShader;
    public Shader compositeShader;
    [Tooltip("Title/CRTOff(UI/Title/Shaders/TitleCRTOff.shader).")]
    public Shader crtOffShader;

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
