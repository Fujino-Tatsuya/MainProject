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
6. 🔴 **`Branch` 노드를 쓰면 안 된다.** `Branch` 는 런타임 select(lerp)라 양쪽이 **둘 다 컴파일되어
   항상 실행**된다 — 키워드로 코드를 덜어내려던 목적이 통째로 무효가 된다.
   Blackboard의 `WALL_OCCLUSION_DITHER` 를 **그래프 위로 드래그**해서 생기는 **`Keyword` 노드**를
   써야 `#if` / `#else` 로 스트립된다. On 포트에 Custom Function의 `Alpha`, Off 포트에 상수 `1`을
   연결하고, Keyword 노드 출력을 Master Stack의 **Alpha**에 연결한다.
7. Master Stack의 **Alpha Clip Threshold**에는 반드시 `0`을 넣는다. 함수 출력이
   `불투명도 - 디더 임계값`이므로 0을 기준으로 클립해야 한다.
8. 그래프를 저장한 뒤 벽용 Material Variant를 만들고 `WALL_OCCLUSION_DITHER`를 켠다.
   바닥·파이프·기계·문처럼 같은 부모 머티리얼을 공유하지만 투명화 대상이 아닌 재질은 키워드를 끈다.

코드 쪽 `WallTransparencyGroup`에는 키워드가 켜진 머티리얼을 사용하는 Renderer만 넣는다.
인스펙터에서 `_WallOcclusionOpacity`가 없는 머티리얼 경고가 나오면 잘못 섞인 슬롯을 먼저 제거한다.

## ⚠️ 키워드가 가리지 못하는 것 — Alpha Clipping

1번의 **Alpha Clipping 은 서페이스 옵션이라 키워드 바깥**이다. 키워드는 *디더 수식*만 가리지
*옵션*은 못 가린다. 즉 이 그래프를 쓰는 **모든 오브젝트**(바닥·파이프·기계·문 포함)가
`TransparentCutout` 취급을 받고 early-Z 이점을 잃는다.

이 프로젝트는 SSAO · Fog · PlayerSilhouette 이 전부 깊이에 의존하므로 **눈에 보이는 변화가 날 수
있다.** 키워드 OFF 의 실제 비용은 "0" 이 아니라 "디더 수식만 0" 이다.

배선 후 Play 로 **바닥과 SSAO 를 before/after 비교**할 것. 티가 나면 벽 전용 그래프 분리를
다시 논의한다.

---

# 셰이더 그래프 없이 지금 바로 검증하기

위 배선은 **톤 정확도와 바닥 비용** 을 위한 것이지, 동작 여부와는 무관하다.
메커니즘만 확인하려면 **이미 있는 변종 머티리얼로 오늘 바로 된다.**

되는 이유: `WallOcclusionDither.shader` 의 불투명도 계산은

```
opacity = WallOcclusionFactor(...) * _WallOcclusionOpacity
```

인데, 구 시스템 드라이버(`WallOcclusionDriver`)가 씬에서 비활성이라 전역 유니폼
`_WallOccRange.w` 가 0 이고 → `WallOcclusionFactor` 가 **즉시 1.0 을 반환**한다.
결국 `_WallOcclusionOpacity` **단독으로 구동되는 셰이더**가 된다. 우리가 필요한 그대로다.
디더 오프셋 전역값도 0 고정이라 **정적 패턴**이 되고, TAA 의존 함정을 피한다.

## 테스트 씬 최소 구성

1. 바닥 + 벽 몇 장(Cube 로 충분)
2. 벽의 머티리얼을 아래 중 하나로 지정 — 둘 다 이미 디더 셰이더를 쓰고
   `_WallOcclusionOpacity` 를 갖고 있다
   - `Assets/3.Materials/Environment/UsedInMap/Shared/Atlases/Occlusion/Generic_01_A_Occlusion.mat`
   - `Assets/3.Materials/Environment/NotUsedInMap/Shared/Atlases/Occlusion/PolygonConstruction_01_A_Occlusion.mat`
3. 빈 오브젝트에 `WallTransparencyGroup` → 대상 Renderer 에 위 벽들을 넣는다
4. 다른 빈 오브젝트에 `WallTransparencyZone` → `BoxCollider` 크기를 방만큼 키우고
   (isTrigger 는 자동으로 켜진다) 벽 그룹에 3번을 연결한다
5. **캡슐 하나를 만들어 레이어만 `Player(6)` 으로 바꾼다.** 컴포넌트도 네트워크도 필요 없다
6. Play 후 캡슐을 박스 안팎으로 움직인다

## 무엇을 볼 것인가

- 진입 시 벽이 디더로 옅어지고, 이탈 시 돌아오는가
- 정적 디더 격자가 눈에 거슬리는가 (거슬리면 표현 방식을 다시 논의)
- 구역 두 개가 같은 그룹을 가리킬 때 OR 로 동작하는가
  (한쪽에서 나와도 다른 쪽에 있으면 유지)
- 원본 머티리얼 벽과 나란히 두고 **톤 차이**를 본다 — 여기서 차이가 크면
  위의 Shader Graph 배선(원본 그래프 + Material Variant)으로 가야 한다는 뜻이다

## 주의

- `Generic_01_A_Occlusion.mat` 은 **근사치 변종**이다. 톤 비교는 이 단계의 목적 중 하나지
  결함이 아니다.
- 컨베이어 벨트 머티리얼에는 변종이 없다. 그룹에 넣으면 조용히 안 사라지고
  인스펙터 경고만 뜬다.
