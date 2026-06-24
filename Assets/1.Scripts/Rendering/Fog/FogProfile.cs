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
}
