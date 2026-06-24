// ----------------------------------------------------------------------------
//  FogCore.hlsl - 통합 해석적(analytic) 스크린스페이스 포그 코어
//  전역(높이/거리) + 로컬 박스/스피어 볼륨 + 페인트 마스크 + 노이즈 + 태양 인스캐터
//
//  높이/거리/밀도모드/노이즈 수학은 meryuhi/URPFog (MIT) 개념을 참조해 재작성.
//  자세한 고지는 THIRD_PARTY_NOTICES.md 참조.
// ----------------------------------------------------------------------------
#ifndef FOG_CORE_INCLUDED
#define FOG_CORE_INCLUDED

#define MAX_FOG_VOLUMES 16

// ---------------- 전역(Global) ----------------
float  _FogGlobalEnabled;
float4 _FogColor;            // rgb = 포그 색
float  _FogDensity;          // 거리 포그 밀도(exp/exp2 스케일)
int    _FogDistanceMode;     // 0 Linear, 1 Exp, 2 Exp2
float  _FogDistanceStart;
float  _FogDistanceEnd;      // Linear 전용
float  _FogHeightStart;      // 이 고도 이하 = 풀 포그
float  _FogHeightEnd;        // 이 고도 이상 = 포그 0
float  _FogHeightStrength;   // 저고도 그라운드 포그 세기(거리 무관)
float  _FogMaxOpacity;       // 최종 불투명도 클램프
float  _FogSkyboxInfluence;  // 스카이박스 픽셀에 적용할 비율(0~1)

// ---------------- 태양 인스캐터 ----------------
float4 _FogSunColor;
float4 _FogSunDir;           // xyz = 광원 방향(정규화), w 미사용
float  _FogSunIntensity;
float  _FogSunPower;

// ---------------- 노이즈 ----------------
float  _FogNoiseEnabled;
float  _FogNoiseUseTexture;
float  _FogNoiseScale;
float  _FogNoiseStrength;
float4 _FogNoiseScroll;      // xy = 월드 x,z 스크롤 속도
TEXTURE2D(_FogNoiseTex);
SAMPLER(sampler_FogNoiseTex);

// ---------------- 페인트 마스크 ----------------
float  _FogMaskEnabled;
float4 _FogMaskRect;         // xy = 월드 최소(x,z), zw = 월드 크기(x,z)
float  _FogMaskTintStrength;
TEXTURE2D(_FogMaskTex);
SAMPLER(sampler_FogMaskTex);

// ---------------- 로컬 볼륨 ----------------
int      _FogVolumeCount;
float4   _FogVolumeParams0[MAX_FOG_VOLUMES];      // x:type(0 box,1 sphere) y:density z:softBorder w:hasTint
float4   _FogVolumeColor[MAX_FOG_VOLUMES];        // rgb:tint
float4x4 _FogVolumeWorldToLocal[MAX_FOG_VOLUMES]; // 월드->로컬(박스=[-0.5,0.5]^3, 스피어=반지름 0.5)

// ---------------- 노이즈 헬퍼 ----------------
float Fog_Hash21(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

float Fog_ValueNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    float a = Fog_Hash21(i);
    float b = Fog_Hash21(i + float2(1, 0));
    float c = Fog_Hash21(i + float2(0, 1));
    float d = Fog_Hash21(i + float2(1, 1));
    float2 u = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
}

float Fog_SampleNoise(float3 worldPos)
{
    if (_FogNoiseEnabled < 0.5)
        return 1.0;

    float2 uv = worldPos.xz * _FogNoiseScale + _FogNoiseScroll.xy * _Time.y;
    float n;
    if (_FogNoiseUseTexture > 0.5)
        n = SAMPLE_TEXTURE2D_LOD(_FogNoiseTex, sampler_FogNoiseTex, uv, 0).r;
    else
        n = Fog_ValueNoise(uv);

    return lerp(1.0 - _FogNoiseStrength, 1.0 + _FogNoiseStrength, n);
}

// ---------------- 전역 포그 항 ----------------
float Fog_Distance(float dist)
{
    if (_FogDistanceMode == 0)
    {
        return saturate((dist - _FogDistanceStart) / max(1e-4, _FogDistanceEnd - _FogDistanceStart));
    }
    else if (_FogDistanceMode == 1)
    {
        float f = exp(-max(0.0, dist - _FogDistanceStart) * _FogDensity);
        return saturate(1.0 - f);
    }
    else
    {
        float d = max(0.0, dist - _FogDistanceStart) * _FogDensity;
        return saturate(1.0 - exp(-d * d));
    }
}

float Fog_Height(float y)
{
    // start 이하 = 1, end 이상 = 0
    return saturate((_FogHeightEnd - y) / max(1e-4, _FogHeightEnd - _FogHeightStart));
}

// ---------------- 로컬 볼륨 기여 ----------------
float Fog_VolumeContribution(float3 worldPos, int i)
{
    float3 local = mul(_FogVolumeWorldToLocal[i], float4(worldPos, 1.0)).xyz;
    float type = _FogVolumeParams0[i].x;
    float soft = max(1e-4, _FogVolumeParams0[i].z);

    float inside;
    if (type < 0.5)
    {
        // 박스 SDF (half-extent 0.5)
        float3 q = abs(local) - 0.5;
        float sdf = length(max(q, 0.0)) + min(max(q.x, max(q.y, q.z)), 0.0);
        inside = saturate(-sdf / soft);
    }
    else
    {
        float d = length(local);
        inside = saturate((0.5 - d) / soft);
    }
    return inside * _FogVolumeParams0[i].y;
}

// ---------------- 메인 평가 ----------------
// 반환: 포그 양(0~1), outColor = 포그 색
float Fog_Evaluate(float3 worldPos, float dist, float skyMask, out float3 outColor)
{
    float noise = Fog_SampleNoise(worldPos);

    float distFog = Fog_Distance(dist);
    float h = Fog_Height(worldPos.y);
    float ground = h * _FogHeightStrength;
    float fGlobal = saturate(max(distFog * h, ground)) * noise;

    // 볼륨 누적
    float fVol = 0.0;
    float3 volTint = 0.0;
    float volTintW = 0.0;
    [loop]
    for (int i = 0; i < _FogVolumeCount; i++)
    {
        float c = Fog_VolumeContribution(worldPos, i) * noise;
        fVol += c;
        if (_FogVolumeParams0[i].w > 0.5)
        {
            volTint += _FogVolumeColor[i].rgb * c;
            volTintW += c;
        }
    }
    fVol = saturate(fVol);

    // 마스크
    float maskMul = 1.0;
    float3 maskTint = 0.0;
    float maskTintAmt = 0.0;
    if (_FogMaskEnabled > 0.5)
    {
        float2 muv = (worldPos.xz - _FogMaskRect.xy) / max(1e-4, _FogMaskRect.zw);
        if (all(muv >= 0.0) && all(muv <= 1.0))
        {
            float4 m = SAMPLE_TEXTURE2D_LOD(_FogMaskTex, sampler_FogMaskTex, muv, 0);
            maskMul = m.a * 2.0; // neutral 0.5 → 1.0
            maskTint = m.rgb;
            maskTintAmt = saturate(max(m.r, max(m.g, m.b))) * _FogMaskTintStrength;
        }
    }

    float f = saturate(saturate(fGlobal + fVol) * maskMul);
    f = lerp(f, f * _FogSkyboxInfluence, skyMask);
    f = saturate(f) * _FogMaxOpacity;

    // 색
    float3 baseCol = _FogColor.rgb;
    if (volTintW > 0.0)
        baseCol = lerp(baseCol, volTint / max(1e-4, volTintW), saturate(volTintW));
    baseCol = lerp(baseCol, maskTint, maskTintAmt);

    // 태양 인스캐터
    float3 viewDir = normalize(worldPos - _WorldSpaceCameraPos);
    float sun = pow(saturate(dot(viewDir, normalize(_FogSunDir.xyz))), max(1e-3, _FogSunPower)) * _FogSunIntensity;
    baseCol += _FogSunColor.rgb * sun * f;

    outColor = baseCol;
    return f;
}

#endif // FOG_CORE_INCLUDED
