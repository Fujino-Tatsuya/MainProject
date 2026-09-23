// 타이틀 CRT 공통 계산 — 모니터 메시(Title/CRTScreen)와 오버레이 UI(Title/CRTUI)가 같이 쓴다.
// 계획서 PLAN-title-flow §0 · Codex 연출 검토(2026-09-23) 반영:
//  - 시간은 C#(TitleCrtFx)이 unscaled 로 넘기는 _FxTime 만 쓴다(_Time 금지 — 정지·저속에서 C# 커브와 갈라진다).
//  - 불규칙 찢김은 셰이더가 확률로 뽑지 않는다. C# 이 사건을 예약해 _Tear 로 넘긴다(프레임률 무관).
//  - 스캔라인은 표면 UV 에 붙이고, 화면에서 촘촘해지면 fwidth 로 대비를 죽인다(모아레 방지).
#ifndef TITLE_CRT_COMMON_INCLUDED
#define TITLE_CRT_COMMON_INCLUDED

float _FxTime;
float _Glitch;        // 버스트 0..1
float _ChromaPx;      // RGB 분리(텍셀)
float _ScanCount;     // 표면 세로 스캔라인 개수
float _ScanStrength;
float _Glow;
float _GlowRadiusPx;
float _Grain;
float _Jitter;        // 상시 라인 지터(UV)
float _Brightness;
float _Vignette;
float4 _Tear;         // x=중심Y, y=반높이, z=가로변위, w=세기(0..1)

float TitleHash11(float p)
{
    p = frac(p * 0.1031);
    p *= p + 33.33;
    p *= p + p;
    return frac(p);
}

float TitleHash21(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

// 가로 변위만 준다 — 세로를 흔들면 클릭 판정(메시 UV 기준)과 어긋나는 폭이 커진다.
float2 TitleCrtDistort(float2 uv)
{
    float row = floor(uv.y * 240.0);
    float tick = floor(_FxTime * 24.0);
    uv.x += (TitleHash21(float2(row, tick)) - 0.5) * _Jitter * (1.0 + _Glitch * 10.0);

    float d = abs(uv.y - _Tear.x);
    float tear = _Tear.w * saturate(1.0 - d / max(_Tear.y, 1e-4));
    uv.x += tear * _Tear.z;

    // 버스트: 가로 블록 단위로 튀는 변위
    float blk = floor(uv.y * 18.0) + floor(_FxTime * 30.0) * 7.0;
    float on = step(1.0 - _Glitch * 0.45, TitleHash11(blk));
    uv.x += on * (TitleHash11(blk * 1.7) - 0.5) * 0.09 * _Glitch;
    return uv;
}

float TitleCrtScan(float phase)
{
    float footprint = fwidth(phase);
    float vis = 1.0 - smoothstep(0.25, 0.5, footprint);
    return 1.0 - _ScanStrength * vis * (0.5 + 0.5 * cos(6.2831853 * phase));
}

float TitleCrtVignette(float2 uv)
{
    float2 c = uv - 0.5;
    return saturate(1.0 - dot(c, c) * _Vignette * 2.2);
}

#endif
