// ----------------------------------------------------------------------------
//  FogProfile.cs - 전역 포그 설정 프리셋 (ScriptableObject)
//  씬/존 간 재사용용. FogManager 가 참조해 글로벌 셰이더 프로퍼티로 푸시한다.
// ----------------------------------------------------------------------------
using UnityEngine;

public enum FogDistanceMode
{
    Linear = 0,
    Exponential = 1,
    ExponentialSquared = 2
}

[CreateAssetMenu(fileName = "FogProfile", menuName = "Rendering/Fog Profile")]
public sealed class FogProfile : ScriptableObject
{
    [Header("색 / 밀도")]
    [ColorUsage(true, true)] public Color color = new Color(0.6f, 0.65f, 0.72f, 1f);
    public FogDistanceMode distanceMode = FogDistanceMode.Exponential;
    [Min(0f)] public float density = 0.02f;
    public float distanceStart = 0f;
    public float distanceEnd = 120f;       // Linear 모드 전용
    [Range(0f, 1f)] public float maxOpacity = 1f;

    [Header("높이 기반")]
    public float heightStart = 0f;          // 이 고도 이하 = 풀 포그
    public float heightEnd = 25f;           // 이 고도 이상 = 포그 0
    [Range(0f, 1f)] public float heightStrength = 0.6f; // 저고도 그라운드 포그 세기

    [Header("스카이박스")]
    [Range(0f, 1f)] public float skyboxInfluence = 1f;

    [Header("태양 인스캐터")]
    public bool useMainLightDirection = true;
    public Vector3 sunDirection = new Vector3(0.5f, -0.7f, 0.3f);
    [ColorUsage(true, true)] public Color sunColor = new Color(1f, 0.95f, 0.8f, 1f);
    [Range(0f, 4f)] public float sunIntensity = 0.5f;
    [Range(1f, 64f)] public float sunPower = 16f;

    [Header("노이즈")]
    public bool noiseEnabled = true;
    public Texture2D noiseTexture;          // 없으면 절차적 노이즈
    [Min(0.0001f)] public float noiseScale = 0.05f;
    [Range(0f, 1f)] public float noiseStrength = 0.35f;
    public Vector2 noiseScroll = new Vector2(0.3f, 0.15f);

    [Header("층 디밍 (전장의 안개)")]
    [Tooltip("플레이어 y 기준 위로 이 거리(m)를 넘으면 디밍 시작.")]
    [Min(0f)] public float dimRangeUp = 5f;
    [Tooltip("플레이어 y 기준 아래로 이 거리(m)를 넘으면 디밍 시작.")]
    [Min(0f)] public float dimRangeDown = 5f;
    [Tooltip("위쪽 페이드 폭(m). 임계 도달 후 완전 디밍까지의 거리.")]
    [Min(0.0001f)] public float dimFadeUp = 3f;
    [Tooltip("아래쪽 페이드 폭(m).")]
    [Min(0.0001f)] public float dimFadeDown = 3f;
    [Tooltip("완전 디밍 시 채도 잔량(0 = 완전 흑백).")]
    [Range(0f, 1f)] public float dimSaturation = 0f;
    [Tooltip("완전 디밍 시 명도 곱(0 ≈ 검정).")]
    [Range(0f, 1f)] public float dimBrightness = 0.03f;
    [Tooltip("스카이박스 픽셀에 디밍을 적용할 비율(0 = 하늘 제외).")]
    [Range(0f, 1f)] public float dimAffectSky = 0f;

    [Header("시야범위 디밍 (FOW)")]
    [Tooltip("플레이어 중심 이 반경(m) 밖이면 디밍 시작. 0이면 시야범위 디밍 끔(층 디밍만).")]
    [Min(0f)] public float viewRange = 0f;
    [Tooltip("반경 경계 페이드 폭(m). 클수록 완만하게 어두워짐.")]
    [Min(0.0001f)] public float viewFade = 6f;

    [Header("시야 차폐 (LoS — 벽/노드 뒤)")]
    [Tooltip("차폐 영역 디밍 강도(0=영향 없음, 1=완전 디밍).")]
    [Range(0f, 1f)] public float losDarken = 1f;
    [Tooltip("자기 차폐 방지 여유(m). 차폐체 표면 자신이 어두워지는 것 방지.")]
    [Min(0f)] public float losDistanceBias = 0.5f;
    [Tooltip("차폐 경계 페이드 폭(m). 클수록 그림자 가장자리가 부드러움. 은은하게 점점 어두워지려면 크게(5~8).")]
    [Min(0.0001f)] public float losEdgeFade = 6f;
    [Tooltip("차폐 시 명도 곱(0≈검정, 1=영향없음). 층 디밍과 별개 — 은은한 어둠은 0.25~0.4.")]
    [Range(0f, 1f)] public float losBrightness = 0.32f;
    [Tooltip("차폐 시 채도 잔량(0=흑백, 1=원색 유지). 자연스러우려면 0.3 안팎.")]
    [Range(0f, 1f)] public float losSaturation = 0.35f;
    [Tooltip("차폐 경계 각도 흔들기(0=직선, 클수록 유기적으로 뭉갬). 노이즈로 부채꼴 경계 직선/삼각형을 완화. 0.01~0.04 권장.")]
    [Range(0f, 0.1f)] public float losAngleJitter = 0.02f;
}
