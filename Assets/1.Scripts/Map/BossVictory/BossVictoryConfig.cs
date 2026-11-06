using TMPro;
using UnityEngine;

/// <summary>
/// 보스 B 승리 연출 수치·리소스(PLAN-boss-victory-cinematic K6 — 인스펙터, xlsx 제외). 시간 경계는 <see cref="BossVictoryRules"/> 상수(기획 v02 §1).
/// 정본 에셋: <c>Resources/BossVictory/BossVictoryConfig.asset</c>. 없으면 코드 기본값으로 돈다(셰이더는 Shader.Find — 빌드에선 에셋이 있어야 한다).
/// </summary>
[CreateAssetMenu(menuName = "MainProject/Boss Victory Config", fileName = "BossVictoryConfig")]
public sealed class BossVictoryConfig : ScriptableObject
{
    public const string ResourcePath = "BossVictory/BossVictoryConfig";

    [Header("카메라(기획 v02 §2)")]
    [Tooltip("최종 정지 구도 부감(도). 콘티 24°.")]
    public float finalPitch = 24f;
    [Tooltip("3.2초 종료 구도 부감(도). 콘티 32°.")]
    public float endPitch = 32f;
    [Tooltip("렌즈 FOV(도). 플레이 카메라와 같은 34° 시작값.")]
    public float fov = 34f;
    [Tooltip("구도 후보를 고르는 기준 화면비. 피어마다 해상도가 달라도 같은 후보를 고르도록 고정한다.")]
    public float referenceAspect = 16f / 9f;
    [Tooltip("주시점(타격점)에서 카메라까지 거리 후보 범위(m).")]
    public float minDistance = 4f;
    public float maxDistance = 22f;
    public float distanceStep = 0.5f;
    [Tooltip("32° 종료 구도 거리 배수(위로 올라가며 후퇴).")]
    public float endDistanceScale = 1.25f;
    [Tooltip("플레이어 몸 높이(m) — 막타·팀원이 화면에 들어오는 범위.")]
    public float characterHeight = 1.8f;
    [Tooltip("보스 몸 반지름·키(m) — 콜라이더가 없을 때 쓴다.")]
    public float bossRadiusFallback = 1.5f;
    public float bossHeightFallback = 4f;
    [Tooltip("방위각 후보(도, 막타 뒤 기준). 음수 = 카메라가 막타 오른쪽 뒤 → 막타가 화면 왼쪽. 앞에 있을수록 먼저 본다(같은 점수면 먼저 것).")]
    public float[] yawCandidates = { -25f, -35f, -15f, -45f, -55f, -5f, 15f, 30f };

    [Header("색(기획 v02 §4 — 최종 화면 목표색, sRGB)")]
    public Color backgroundTop = new Color32(0xED, 0x9B, 0x25, 0xFF);
    public Color backgroundBottom = new Color32(0xD6, 0x7A, 0x16, 0xFF);
    public Color killerColor = new Color32(0xE9, 0xE9, 0xDF, 0xFF);
    public Color teammateColor = new Color32(0xC8, 0xD1, 0xD1, 0xFF);
    public Color bossColor = new Color32(0x4D, 0x56, 0x60, 0xFF);
    public Color shadowColor = new Color32(0x12, 0x17, 0x1C, 0xFF);
    [Tooltip("배경 주황 틴트 최대 강도(0 = 원래 화면, 1 = 단색 주황). 단색은 빼고 배경이 비치게 — 경석 10-27: 화면 캡처(약 40%)보다 15% 약하게 = 0.34.")]
    [Range(0f, 1f)] public float backgroundTint = 0.34f;
    [Tooltip("⚠️ 미사용(10-27 틴트로 대체). 예전: 단색 배경에 남기던 환경 명암.")]
    [Range(0f, 1f)] public float backgroundDetail = 0.35f;
    [Tooltip("인물 명도 노출(조명 결과 명도에 곱한다). HDR 밝은 부분이 뭉치면 내린다.")]
    public float characterExposure = 1.6f;
    [Tooltip("인물 명도 곡선 지수(1 = 그대로, >1 = 어두운 쪽을 더 어둡게).")]
    public float characterGamma = 1.1f;

    [Header("글리치(지직거림 — 레퍼런스 1:30~1:33, 경석 10-27)")]
    [Tooltip("터지는 구간 (시작초, 끝초, 세기 0~1). 기본: 막타~카메라 하강 · 카메라 멈춤 · 다시 움직임.")]
    public Vector3[] glitchBursts =
    {
        new Vector3(0.12f, 0.72f, 0.85f),
        new Vector3(0.72f, 0.90f, 1.00f),
        new Vector3(2.00f, 2.14f, 0.65f),
    };
    [Tooltip("가로 띠가 좌우로 밀리는 최대 거리(화면 폭 비율).")]
    public float glitchShift = 0.18f;
    [Tooltip("가로 잔상(스트릭) 길이 배수.")]
    public float glitchStreak = 1f;
    [Tooltip("회색으로 밝게 날아가는 정도(0~1).")]
    [Range(0f, 1f)] public float glitchWash = 0.85f;
    [Tooltip("구간 안에서 세기가 프레임마다 흔들리는 정도(0 = 일정, 1 = 크게 깜빡임).")]
    [Range(0f, 1f)] public float glitchFlicker = 0.5f;

    [Header("외곽선(Flat Kit — 경석 10-27: 몬스터 외곽선이 너무 진하다)")]
    [Tooltip("연출 동안 보스 외곽선 두께 배수(1 = 그대로). 보스 디졸브가 시작되면 원래대로.")]
    [Range(0f, 1f)] public float bossOutlineScale = 0.35f;
    [Tooltip("연출 동안 보스 외곽선 색(깔끔한 짙은 선).")]
    public Color bossOutlineColor = new Color32(0x12, 0x17, 0x1C, 0xFF);
    [Tooltip("연출 동안 플레이어 외곽선 두께 배수(1 = 그대로).")]
    [Range(0f, 1f)] public float characterOutlineScale = 1f;

    [Header("슬로모션(경석 10-27 — 0.12–2.00 완전 정지 대신)")]
    [Tooltip("0.12–2.00 동안 인물·보스·핵심 VFX 재생 배율(0 = 완전 정지). 보스 사망 연출 서버 대기도 같은 배율로 센다.")]
    [Range(0f, 1f)] public float slowMotionScale = 0.1f;

    [Header("타이포(기획 v02 §5)")]
    public string typoText = "BOSS DOWN";
    [Tooltip("0.92 부터 불투명도 0 → 1 에 걸리는 초(기획 0.16 — 경석 10-27 '너무 빠르다').")]
    [Min(0.01f)] public float typoFadeInSeconds = 0.4f;
    [Tooltip("0.92 부터 오른쪽에서 안착·두 띠 정렬에 걸리는 초(기획 0.15).")]
    [Min(0.01f)] public float typoSettleSeconds = 0.45f;
    [Tooltip("비우면 TMP 기본 폰트.")]
    public TMP_FontAsset typoFont;
    [Tooltip("글자 폭 = 화면 폭 × 이 값(1.09 = 양 끝 일부 잘림).")]
    public float typoWidthRatio = 1.09f;
    [Tooltip("글자 띠 중심 높이 — 화면 위에서부터 비율.")]
    [Range(0f, 1f)] public float typoCenterFromTop = 0.51f;
    [Tooltip("글자 높이 = 화면 높이 × 이 값(폭 비율과 함께 맞춘다 — 둘 중 작은 쪽).")]
    public float typoMaxHeightRatio = 0.26f;
    [Tooltip("기울임(TMP 이탤릭) + 약한 회전(도, 반시계 +).")]
    public float typoRotation = 4f;
    [Tooltip("진입 시작 때 오른쪽으로 밀린 거리 — 화면 폭 비율.")]
    public float typoEnterOffset = 0.35f;
    [Tooltip("가로로 잘린 얇은 두 띠의 어긋남 — 화면 폭 비율(진입 끝에 0).")]
    public float typoBandOffset = 0.06f;
    [Tooltip("두 띠의 세로 범위(글자 높이 기준 0 = 아래, 1 = 위).")]
    public Vector2 typoBandA = new Vector2(0.30f, 0.38f);
    public Vector2 typoBandB = new Vector2(0.62f, 0.70f);
    [Tooltip("좌 → 우 4색(청록 → 보라 → 분홍 → 노랑).")]
    public Color[] typoGradient =
    {
        new Color32(0x2C, 0xC9, 0xC6, 0xFF),
        new Color32(0x8B, 0x5C, 0xE0, 0xFF),
        new Color32(0xF2, 0x6B, 0xAE, 0xFF),
        new Color32(0xF6, 0xD3, 0x3C, 0xFF),
    };
    [Tooltip("얇은 밝은 테두리.")]
    public Color typoOutlineColor = new Color32(0xFF, 0xF9, 0xE6, 0xFF);
    [Range(0f, 1f)] public float typoOutlineWidth = 0.22f;
    [Tooltip("우하단 짙은 그림자.")]
    public Color typoShadowColor = new Color32(0x12, 0x17, 0x1C, 0xD0);
    public Vector2 typoShadowOffset = new Vector2(0.7f, -0.7f);

    [Header("핵심 타격 이펙트(Q2 — 승리 전용, 타격점에 새로 띄움)")]
    [Tooltip("파티클 프리팹. 0.12초에 멈추고 2.00초에 재개, 3.20초 뒤 지운다. 비우면 없음.")]
    public GameObject hitVfxPrefab;
    public float hitVfxScale = 1.5f;

    [Header("사운드 키(K4 — 비우면 무음)")]
    public string sfxHitStop = "";
    public string sfxTypo = "";
    public string sfxResume = "";

    [Header("셰이더(빌드 포함용 참조 — 비우면 Shader.Find)")]
    public Shader maskShader;
    public Shader compositeShader;

    static BossVictoryConfig _cached;

    /// <summary>Resources 에셋, 없으면 코드 기본값 인스턴스.</summary>
    public static BossVictoryConfig Load()
    {
        if (_cached != null) return _cached;
        _cached = Resources.Load<BossVictoryConfig>(ResourcePath);
        if (_cached == null)
        {
            Debug.LogWarning($"[BossVictory] Resources/{ResourcePath} 가 없어 코드 기본값으로 돈다(셰이더·폰트·이펙트 참조 없음).");
            _cached = CreateInstance<BossVictoryConfig>();
        }
        return _cached;
    }

    public BossVictoryRules.Framing ToFraming() => new BossVictoryRules.Framing
    {
        FinalPitch = finalPitch,
        EndPitch = endPitch,
        Fov = fov,
        Aspect = referenceAspect,
        MinDistance = minDistance,
        MaxDistance = maxDistance,
        DistanceStep = distanceStep,
        EndDistanceScale = endDistanceScale,
        CharacterHeight = characterHeight,
        YawCandidates = yawCandidates,
    };
}
