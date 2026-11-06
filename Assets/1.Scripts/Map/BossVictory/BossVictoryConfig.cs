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

    [Header("추가 연출 구간(할 일 200 — 타임라인 맨 앞, 아직 내용 없음)")]
    [Tooltip("처치 확정 직후 '추가 연출'이 차지할 초. 이 구간이 끝난 뒤에 슬로모션·카메라 하강·색·보스 사망 애니가 시작되고, " +
             "서버 사망 대기·결과 하한·안전망도 같은 만큼 밀린다. 0 = 예전 타임라인 그대로. 서버가 처치 순간 값을 복제 상태에 담는다(모든 피어 동일). " +
             "연출을 꽂을 곳 = BossVictoryPresentation.PreludeStarted / PreludeEnded 이벤트.")]
    [Min(0f)] public float preludeSeconds = 0f;

    [Header("색(최종 화면 목표색, sRGB)")]
    [Tooltip("배경 틴트 단색(할 일 200 D1 — 예전 위/아래 그라데이션 두 색 대신). #EAAD70.")]
    public Color backgroundColor = new Color32(0xEA, 0xAD, 0x70, 0xFF);
    public Color killerColor = new Color32(0xE9, 0xE9, 0xDF, 0xFF);
    public Color teammateColor = new Color32(0xC8, 0xD1, 0xD1, 0xFF);
    public Color bossColor = new Color32(0x4D, 0x56, 0x60, 0xFF);
    [Tooltip("무기 궤적·이펙트 등 반투명으로 그려진 픽셀의 단색 밝은 쪽(할 일 200 D2 — 인물뿐 아니라 이펙트까지 단색).")]
    public Color effectColor = new Color32(0xE9, 0xE9, 0xDF, 0xFF);
    public Color shadowColor = new Color32(0x12, 0x17, 0x1C, 0xFF);
    [Tooltip("배경 주황 틴트 최대 강도(0 = 원래 화면, 1 = 단색 주황). 단색은 빼고 배경이 비치게 — 경석 10-27: 0.34.")]
    [Range(0f, 1f)] public float backgroundTint = 0.34f;
    [Tooltip("인물·이펙트 명도 노출(조명 결과 명도에 곱한다). 할 일 200 D2: 1.6 → 1.75(아주 약간 밝게). HDR 밝은 부분이 뭉치면 내린다.")]
    public float characterExposure = 1.75f;
    [Tooltip("인물·이펙트 명도 곡선 지수(1 = 그대로, >1 = 어두운 쪽을 더 어둡게).")]
    public float characterGamma = 1.1f;
    [Tooltip("반투명 패스(이펙트·무기 궤적)가 바꾼 색 차이를 이펙트 픽셀로 보는 배율 — 클수록 옅은 이펙트도 단색이 된다.")]
    [Min(0f)] public float effectDetectGain = 6f;

    [Header("슬로모션(경석 10-27 — 완전 정지 대신)")]
    [Tooltip("슬로모션 구간(단계 0.12–2.00) 동안 인물·보스 재생 배율(0 = 완전 정지). 보스 사망 연출 서버 대기도 같은 배율로 센다.")]
    [Range(0f, 1f)] public float slowMotionScale = 0.1f;

    [Header("벽 숨김(할 일 200 추가 — 연출 카메라가 벽에 가리지 않게)")]
    [Tooltip("앞면(방 안쪽 면)이 이 값보다 확실히 카메라를 향한 벽만 남긴다 — dot(벽 안쪽 법선, 카메라 반대 시선) ≤ 값이면 렌더러를 끈다. " +
             "0 = 뒷면만 끔, 0.3 ≈ 시선과 72° 넘게 비스듬한 옆면도 끔. 카메라가 벽 바깥에 있으면 항상 끈다.")]
    [Range(-1f, 1f)] public float wallFacingThreshold = 0.3f;
    [Tooltip("벽 후보를 찾는 반경(m, 타격점 기준). 다른 구역 벽은 건드리지 않는다.")]
    [Min(1f)] public float wallSearchRadius = 40f;

    [Header("핵심 타격 이펙트(Q2 — 승리 전용, 타격점에 새로 띄움)")]
    [Tooltip("할 일 200 F: 비워 둠(나중에 추가 예정). 넣으면 슬로모션 시작에 늦추고 끝에 재개, 연출 단계 끝에 지운다.")]
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
