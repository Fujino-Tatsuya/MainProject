# 구역 벽 투명화 Shader Graph 배선

대상은 우선 `Generic_Standard.shadergraph`다. `Generic_Basic.shadergraph` 적용은 Standard 검증 뒤에 한다.

1. Graph Inspector에서 Surface Type을 **Opaque**로 유지하고 **Alpha Clipping**을 켠다.
2. Blackboard에 노출 `Vector 1` 프로퍼티를 추가한다.
   - Reference: `_WallOcclusionOpacity`
   - Default: `1`
   - Mode/범위: Slider, `0~1`
3. Blackboard에 `Boolean Keyword`를 추가한다.
   - Reference: `WALL_OCCLUSION_DITHER`
   - Definition: `Shader Feature`
   - Scope: `Local`
   - 벽용 Material Variant에서만 이 키워드를 켠다.
4. `Screen Position` 노드(Default)의 XY에 `Screen Parameters`의 XY를 곱해 **화면 픽셀 좌표**를 만든다.
5. `Custom Function` 노드를 File 모드로 만들고 아래처럼 설정한다.
   - File: `Assets/3.Materials/Level1_Materials/Occlusion/WallTransparencyDither.hlsl`
   - Name: `WallTransparencyDither` (정밀도 접미사 `_float`/`_half`는 적지 않는다)
   - Input `ScreenPixelPosition`: Vector 2 — 4단계 결과
   - Input `Opacity`: Vector 1 — `_WallOcclusionOpacity`
   - Output `Alpha`: Vector 1
6. `Branch`의 Predicate에 `WALL_OCCLUSION_DITHER`를, True에 Custom Function의 `Alpha`를,
   False에 상수 `1`을 연결한다. Branch 출력을 Master Stack의 **Alpha**에 연결한다.
7. Master Stack의 **Alpha Clip Threshold**에는 반드시 `0`을 넣는다. 함수 출력이
   `불투명도 - 디더 임계값`이므로 0을 기준으로 클립해야 한다.
8. 그래프를 저장한 뒤 벽용 Material Variant를 만들고 `WALL_OCCLUSION_DITHER`를 켠다.
   바닥·파이프·기계·문처럼 같은 부모 머티리얼을 공유하지만 투명화 대상이 아닌 재질은 키워드를 끈다.

코드 쪽 `WallTransparencyGroup`에는 키워드가 켜진 머티리얼을 사용하는 Renderer만 넣는다.
인스펙터에서 `_WallOcclusionOpacity`가 없는 머티리얼 경고가 나오면 잘못 섞인 슬롯을 먼저 제거한다.
