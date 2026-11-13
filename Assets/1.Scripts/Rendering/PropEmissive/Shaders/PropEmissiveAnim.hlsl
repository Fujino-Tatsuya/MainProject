// 277 프랍 이미시브 애니(시간 기반) — PropEmissiveLitInput.hlsl 끝에서 include.
// 마스크(_EmissionMap) A 채널로 영역을 고른다: >0.75 = CRT 화면, 0.25~0.75 = 패널 버튼, 그 외 = 기존 정적 이미시브.
// 인스턴스 시드 = 오브젝트 원점 위치 해시(MPB 없음 → SRP Batcher 유지). 각 피어 로컬 표시 전용.
#ifndef PROP_EMISSIVE_ANIM_INCLUDED
#define PROP_EMISSIVE_ANIM_INCLUDED

TEXTURE2D(_ScreenTex);
SAMPLER(sampler_ScreenTex);

float PE_Hash11(float n) { return frac(sin(n * 12.9898 + 4.1414) * 43758.5453); }
float PE_Hash21(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }

float PE_Noise1(float x, float seed)
{
    float i = floor(x);
    float f = frac(x);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(PE_Hash11(i + seed * 17.13), PE_Hash11(i + 1.0 + seed * 17.13), f);
}

float PE_InstanceSeed()
{
    float3 o = float3(UNITY_MATRIX_M._m03, UNITY_MATRIX_M._m13, UNITY_MATRIX_M._m23);
    return floor(PE_Hash21(o.xz * 3.17 + o.y * 1.31) * 997.0);
}

// 평균 간격마다 한 번, 칸 안 무작위 위치에 길이만큼 1 (C# PropEmissiveAnimator.DipFactor 와 같은 칸 방식)
float PE_Glitch(float t, float seed)
{
    if (_GlitchInterval <= 0.0 || _GlitchDuration <= 0.0) return 0.0;
    float slotLen = max(_GlitchInterval, _GlitchDuration * 2.0);
    float k = floor(t / slotLen);
    float start = k * slotLen + PE_Hash11(k + seed * 3.3) * (slotLen - _GlitchDuration);
    return (t >= start && t < start + _GlitchDuration) ? 1.0 : 0.0;
}

float PE_Screen(float2 s, float seed, float t)
{
    float g = PE_Glitch(t, seed);
    // 지직: 가로 찢김(띠마다 좌우로 밀림)
    if (g > 0.0)
    {
        float band = floor(s.y * 28.0);
        float j = PE_Hash11(band * 1.7 + floor(t * 30.0) * 7.0 + seed);
        s.x += (j - 0.5) * 0.14 * _GlitchStrength * step(0.5, j);
    }

    float variant = floor(PE_Hash11(seed * 3.7 + 1.0) * 3.0); // 0 글자줄 · 1 파형/막대 · 2 레이더
    float c = _ScreenBgLevel;

    if (variant < 0.5)
    {
        // 터미널 — 텍스처 R(64줄, 줄 단위로 위로 스크롤), 화면에 22줄
        const float rowsOnScreen = 22.0;
        float rate = _ScreenScroll * (0.6 + 0.8 * PE_Hash11(seed + 5.0));
        float rowsScrolled = floor(t * rate) + floor(PE_Hash11(seed + 9.0) * 64.0);
        float2 tuv = float2(s.x * 0.72, (s.y * rowsOnScreen - rowsScrolled) / 64.0);
        float text = SAMPLE_TEXTURE2D(_ScreenTex, sampler_ScreenTex, tuv).r;
        // 맨 아래 줄 커서 깜빡임
        float cursor = step(s.y, 1.0 / rowsOnScreen) * step(0.02, s.x) * step(s.x, 0.045) * step(0.5, frac(t * 1.6));
        c += text * 0.85 + cursor * 0.8;
    }
    else if (variant < 1.5)
    {
        // 진단 그래프 — 격자(텍스처 G) + 흐르는 파형 + 바닥 막대
        float grid = SAMPLE_TEXTURE2D(_ScreenTex, sampler_ScreenTex, s).g;
        float ph = seed * 0.37;
        float y = 0.62 + 0.16 * sin(s.x * 9.0 + t * 2.3 + ph) + 0.06 * sin(s.x * 31.0 - t * 5.1 + ph * 2.0);
        float waveLine = smoothstep(0.022, 0.0, abs(s.y - y));
        float col = floor(s.x * 18.0);
        float h = 0.06 + 0.24 * PE_Hash11(col * 1.3 + floor(t * 2.5) * 13.0 + seed);
        float bar = step(s.y, h) * step(0.18, frac(s.x * 18.0));
        c += grid * 0.35 + waveLine * 0.95 + bar * 0.55;
    }
    else
    {
        // 레이더 — 동심원·십자 + 회전 스윕 잔상 + 깜빡이는 점
        float2 p = s - 0.5;
        p.x *= 1.5;
        float r = length(p) * 2.2;
        float ang = atan2(p.y, p.x) / 6.2831853 + 0.5;
        float inside = step(r, 1.0);
        float rings = step(frac(r * 4.0), 0.07) * inside;
        float crossLine = (step(abs(p.x), 0.004) + step(abs(p.y), 0.006)) * inside;
        float sweep = frac(ang - t * 0.28 - seed * 0.01);
        float trail = pow(1.0 - sweep, 7.0) * inside;
        float2 cell = floor(p * 9.0);
        float blip = step(0.94, PE_Hash21(cell + seed)) * smoothstep(0.35, 0.0, length(frac(p * 9.0) - 0.5)) * pow(1.0 - sweep, 2.0);
        c += rings * 0.4 + saturate(crossLine) * 0.3 + trail * 0.65 + blip * inside;
    }

    // 스캔라인 · 롤링 띠
    c *= 1.0 - _ScanlineDepth * (0.5 + 0.5 * sin(s.y * _ScanlineCount * 6.2831853));
    float rollY = frac(s.y - t * _RollSpeed) - 0.5;
    c += _RollStrength * 0.5 * exp(-rollY * rollY * 90.0);
    // 미세 깜빡임
    c *= 1.0 + _FlickerAmount * (PE_Noise1(t * _FlickerFreq, seed) * 2.0 - 1.0);
    // 지직 노이즈 띠
    if (g > 0.0)
    {
        float n = PE_Hash21(floor(s * float2(90.0, 45.0)) + floor(t * 40.0));
        float rowOn = step(0.45, PE_Hash11(floor(s.y * 12.0) + floor(t * 20.0) * 3.0 + seed));
        c = c * 0.6 + n * 0.45 * _GlitchStrength * rowOn;
    }
    // 브라운관 가장자리 어둡게
    float2 e = smoothstep(0.0, 0.07, s) * smoothstep(1.0, 0.93, s);
    c *= lerp(0.5, 1.0, e.x * e.y);
    return max(c, 0.0);
}

float PE_Button(float id, float t)
{
    t *= _ButtonSpeed;
    float kind = PE_Hash11(id * 1.7 + 0.3);
    float ph = PE_Hash11(id * 5.3 + 0.7);
    float on;
    if (kind < 0.22) on = 1.0;                                                          // 계속 켜짐
    else if (kind < 0.5) { float per = lerp(1.6, 3.2, ph); on = step(frac(t / per + ph), 0.5); }   // 느린 점멸
    else if (kind < 0.75) { float per = lerp(0.35, 0.7, ph); on = step(frac(t / per + ph), 0.5); } // 빠른 점멸
    else { float hold = lerp(0.6, 2.2, ph); on = step(0.4, PE_Hash11(floor(t / hold + ph) * 1.31 + id)); } // 무작위 켜짐/꺼짐
    return lerp(_ButtonOffLevel, 1.0, on);
}

half3 PE_AnimatedEmission(float2 uv, float3 positionWS, half3 staticEmission)
{
    half4 m = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, uv);
    float t = fmod(_Time.y, 1800.0);
    float seed = PE_InstanceSeed();
    if (m.a > 0.75)
    {
        float2 s = saturate((uv - _ScreenRect.xy) / max(_ScreenRect.zw - _ScreenRect.xy, 1e-4));
        return _EmissionColor.rgb * PE_Screen(s, seed, t);
    }
    if (m.a > 0.25)
    {
        float3 pos = TransformWorldToObject(positionWS);
        float side = pos.x > 0.0 ? 1.0 : 0.0;
        float cx = round((abs(pos.x) - _ButtonGrid.x) / _ButtonGrid.z);
        float cz = round((pos.z - _ButtonGrid.y) / _ButtonGrid.w);
        float id = side * 31.0 + cx * 7.0 + cz * 3.0 + seed * 13.0;
        return m.rgb * _EmissionColor.rgb * PE_Button(id, t);
    }
    return staticEmission;
}

#endif
