# Fog 다음 작업 계획 — 이터널리턴 FoW 마감 + 어비스 물안개 (다음 세션 이어받기)

> 대상 경로: `Assets/1.Scripts/Rendering/Fog/`
> 관련 커밋: `cac5e7e`(LoS 차폐 복구 + 경계 각도블러/지터)

---

## 0. 현재 상태 (이미 구현됨)
- 전역 포그: 거리/높이/밀도모드/볼륨/노이즈/페인트마스크 (`FogCore.hlsl` `Fog_Evaluate`)
- 층 디밍 + 시야범위 디밍(FOW `viewRange`) + **LoS 차폐**(`_LosBrightness`/`_LosSaturation` — 시야 밖 은은하게 어둡게+탈채도)
- **LoS 경계 자연스럽게**: 라디얼맵 각도 블러(`losAngleBlur`) + 셰이더 노이즈 각도 지터(`losAngleJitter`)
- 파이프라인: `FogManager`(글로벌 push + `_LosTex` 라디얼맵 CPU 생성) → `FullScreenFog.shader`(풀스크린 합성) → `FogRendererFeature`(URP 패스)

---

## 1. Task 1 — 이터널리턴 FoW 마감 (대부분 구현됨, 튜닝 위주)
현재 LoS/디밍 시스템으로 거의 커버됨. "선명한 2D 탑다운 시야"로 다듬기:

**인스펙터 세팅 (FogManager + FogProfile)**
- `losEnabled` ON, `dimEnabled` ON
- `viewRange` 15~25 (현재 0=끔 → 켜야 플레이어 반경 시야), `viewFade` 3~5
- `losEdgeFade` 5~8 (거리방향 소프트섀도우)
- `losAngleBlur` 6~10 / `losAngleJitter` 0.02~0.04 (부채꼴 경계 완화)
- 이터널리턴은 시야 밖이 거의 어둠 → `losBrightness` 0.1~0.2, `losSaturation` 0.15~0.25 (더 어둡게, 취향)

**개선 여지 (선택)**
- 시야 경계에 약한 비네팅/컬러 그레이딩
- LoS 라디얼맵 갱신 빈도(현재 매 LateUpdate) — 성능 이슈 시 N프레임마다

---

## 2. Task 2 — 어비스(바닥 구멍) 물안개 [신규 구현]
바닥이 뚫린 구멍 지형에 **짙은 심연색 + 노이즈 일렁임** 물안개.

### ⚠️ 먼저 결정할 것 (다음 세션 grill)
풀스크린 포그는 **depth(그려진 픽셀) 기반**이라, 구멍 아래에 **실제 지오메트리(물 평면 등)가 있어야** 픽셀이 존재해 어비스 색을 입힐 수 있음. 방식 택1:
- **(A) 물 평면 메시**: 구멍 아래 Y<0에 어두운 물 평면 배치 → 그 픽셀에 어비스 물안개 적용. (아트/기획 협의)
- **(B) 영역 마스크**: 구멍 위치를 마스크(기존 `_FogMaskTex` 확장 or 볼륨)로 지정 → 해당 영역 픽셀 어비스색. 지오메트리 없어도 됨.
- **(C) 높이 기반**: 픽셀 worldPos.y < 임계면 어비스. 단 구멍 아래 지오메트리 필요(A와 병행).
→ **B(영역 마스크) 또는 A(물 평면)+C(높이) 조합** 권장. 다음 세션에 맵 구조 보고 확정.

### C# — FogProfile.cs 추가 프로퍼티
```csharp
[Header("어비스 물안개 (바닥 구멍)")]
public bool abyssEnabled = false;
[Tooltip("이 월드 Y 이하부터 어비스 물안개 시작.")]
public float abyssHeightThreshold = 0f;
[Tooltip("이 깊이(m)에서 어비스 최대 강도.")]
[Min(0.01f)] public float abyssDepthRange = 8f;
[Tooltip("심연 색(어두운 남색/검정 계열, HDR).")]
[ColorUsage(true, true)] public Color abyssColor = new Color(0.02f, 0.04f, 0.08f, 1f);
[Range(0f, 1f)] public float abyssMaxOpacity = 0.95f;
[Tooltip("물안개 일렁임 세기(기존 노이즈 재활용).")]
[Range(0f, 3f)] public float abyssNoiseStrength = 1.5f;
[Tooltip("물 일렁임 스크롤 속도(월드 xz).")]
public Vector2 abyssNoiseScroll = new Vector2(0.15f, 0.1f);
```

### C# — FogManager.cs
- `_ID` 추가: `_AbyssEnabled` `_AbyssThreshold` `_AbyssDepthRange` `_AbyssColor` `_AbyssMaxOpacity` `_AbyssNoiseStrength` `_AbyssNoiseScroll`
- `PushFogGlobals(p)` 안에서 위 값 `Shader.SetGlobalXxx` push (기존 노이즈 push 패턴 참고)

### FogCore.hlsl — 어비스 평가 함수 추가
```hlsl
// ---------------- 어비스 물안개 ----------------
float  _AbyssEnabled;
float  _AbyssThreshold;
float  _AbyssDepthRange;
float4 _AbyssColor;
float  _AbyssMaxOpacity;
float  _AbyssNoiseStrength;
float4 _AbyssNoiseScroll;

// worldPos.y가 임계 이하로 깊어질수록 심연색+일렁임. 반환=어비스 양(0~1), col=심연색
float Abyss_Evaluate(float3 worldPos, out float3 col)
{
    col = _AbyssColor.rgb;
    if (_AbyssEnabled < 0.5) return 0.0;
    float depth = saturate((_AbyssThreshold - worldPos.y) / max(1e-4, _AbyssDepthRange));
    if (depth <= 0.0) return 0.0;
    // 물 일렁임: 스크롤 노이즈 강하게
    float2 uv = worldPos.xz * _FogNoiseScale + _AbyssNoiseScroll.xy * _Time.y;
    float n = _FogNoiseUseTexture > 0.5
        ? SAMPLE_TEXTURE2D_LOD(_FogNoiseTex, sampler_FogNoiseTex, uv, 0).r
        : Fog_ValueNoise(uv);
    float wobble = lerp(1.0, n, saturate(_AbyssNoiseStrength));
    return saturate(depth * _AbyssMaxOpacity * wobble);
}
```

### FogCore.hlsl — 최종 합성 (Fog_Evaluate 반환 후, 또는 FullScreenFog에서)
```hlsl
// 기존 f(포그양)/outColor 계산 뒤:
float3 abyssCol;
float abyssAmt = Abyss_Evaluate(worldPos, abyssCol);
// 어비스가 일반 포그를 덮어씀(더 짙고 어두움)
outColor = lerp(outColor, abyssCol, abyssAmt);
f = max(f, abyssAmt);
```

### 권장 테스트 세팅
- 구멍 아래 Y<0 물 평면(또는 마스크 영역) 배치
- `abyssThreshold` 0, `abyssDepthRange` 6~10, `abyssColor` (0.02,0.04,0.08), `abyssNoiseStrength` 1.5, `abyssNoiseScroll` (0.15,0.1)
- 탑다운 카메라에서 구멍이 짙은 남색 + 안개 일렁임으로 보이면 성공

---

## 3. 작업 순서 (다음 세션)
1. 어비스 방식 결정(A 물평면 / B 마스크 / C 높이) — 맵 구조 확인 후 grill
2. FogProfile.cs → FogManager.cs → FogCore.hlsl 순으로 어비스 구현
3. losEnabled/viewRange 켜고 이터널리턴 FoW 튜닝
4. 검증(Unity 스샷) → feature/map 커밋

## 4. 참고 — 파일 역할
| 파일 | 역할 |
|---|---|
| `FogManager.cs` | 글로벌 프로퍼티 push, `_LosTex` 라디얼맵 CPU 생성(각도 블러 포함) |
| `FogProfile.cs` | ScriptableObject 설정값 |
| `FogCore.hlsl` | 포그/디밍/LoS/(예정)어비스 수학 |
| `FullScreenFog.shader` | 풀스크린 합성 패스 |
| `FogRendererFeature.cs` | URP 렌더 패스 등록 |
