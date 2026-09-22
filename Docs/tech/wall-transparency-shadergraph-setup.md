# 구역 벽 투명화 Shader Graph 배선

대상 그래프는 **`Assets/50.Art/MapGen/MapObj/material/Generic_Standard.shadergraph`** 다.
🔴 **SVN**(`/Assets/50.Art/` 는 gitignore)이므로 git PR 에 올라가지 않는다. 작업 후 **SVN 커밋과
팀 공지**가 필요하다.

영향 범위는 이 그래프를 쓰는 머티리얼 **2개**뿐이다(2026-09-22 실측):

| 머티리얼 | 비고 |
|---|---|
| `Assets/50.Art/Environment/Materials/UsedInMap/Shared/Atlases/Generic_01_A.mat` | 실사용 |
| `Assets/50.Art/legacy/.../LevelDeliveryV3/Materials/Generic_01_A_V3.mat` | legacy |

펜스(`PolygonConstruction_01_A`)가 쓰는 `Generic_Basic.shadergraph` 는 **Standard 검증 뒤에** 같은
절차로 한다.

---

## 수식은 그래프에 없다

높이 그라데이션과 구역 강도 합성은 전부
[`WallTransparencyDither.hlsl`](../../Assets/3.Materials/Level1_Materials/Occlusion/WallTransparencyDither.hlsl)(**git**)
안에 있다. 그래프에서는 **Custom Function 하나에 값만 물린다.** 노드를 10개 넘게 잇지 않는다.

hlsl 이 하는 일:

```
fade     = saturate((worldY - baseY) / fadeHeight)
mask     = 1 - fade
strength = 1 - opacity            // opacity = _WallOcclusionOpacity
result   = lerp(1, mask, strength)
Alpha    = result - 디더 임계값
```

구역 밖이면 `opacity = 1` → `strength = 0` → `result = 1` → **높이와 무관하게 아무 변화 없음.**

## 값의 의미

벽 한 층은 **2.5** 다(`Wall_2stack.prefab` 자식 Y = 0 / 2.5 / 5, 존 프리팹 벽 클러스터
= -5.5 / -3.0 / -0.5 에서 실측).

`fadeHeight = 5.0`(= 2.5 × 2층)이면 **1층 0~0.5 / 2층 0.5~1** 의 선형 램프가 되고,
**3·4층은 `saturate` 로 완전히 사라진다.** 꺾이는 구간이 없으므로 piecewise 가 필요 없다.

`baseY` 와 `fadeHeight` 는 **`WallTransparencyGroup` 이 머티리얼 인스턴스에 써 준다.**
`baseY` 는 그룹 오브젝트의 월드 Y + 인스펙터 오프셋이다 — 존 프리팹의 벽이 존 로컬 원점 기준
음수 좌표에 놓이고 존은 런타임에 배치되므로 절대값을 셰이더에 박을 수 없다.

---

## 배선 절차

1. Graph Inspector 에서 Surface Type = **Opaque** 유지, **Alpha Clipping 을 켠다.**
2. Blackboard 에 노출 프로퍼티 3개를 추가한다. **Reference 이름이 정확해야** 코드가 찾는다.

   | 종류 | Reference | 기본값 | 비고 |
   |---|---|---|---|
   | Float | `_WallOcclusionOpacity` | `1` | Slider 0~1 |
   | Float | `_WallOccBaseY` | `0` | 코드가 덮어쓴다 |
   | Float | `_WallOccFadeHeight` | `5` | 코드가 덮어쓴다 |

3. Blackboard 에 **Boolean Keyword** 를 추가한다.
   - Reference: `WALL_OCCLUSION_DITHER` / Definition: **Shader Feature** / Scope: **Local** / Default: **Off**
   - 벽용 Material Variant 에서만 켠다.

### 만들 노드는 4개뿐이다

그래프 빈 곳에서 **우클릭 → Create Node** 로 아래를 만든다.

| # | 노드 | 설정 |
|---|---|---|
| A | **Screen Position** | 모드 **Default** (기본값 그대로) |
| B | **Position** | Space 를 **World** 로 바꾼다 |
| C | **Custom Function** | 아래 5번에서 설정 |
| D | **Keyword** | Blackboard 의 `WALL_OCCLUSION_DITHER` 를 **그래프 위로 드래그**하면 생긴다 |

`Split`·`Multiply`·`Screen` 같은 노드는 **필요 없다.** 픽셀 변환과 Y 추출은 hlsl 안에서 한다.

### 5. Custom Function(C) 설정

노드를 선택하고 **Graph Inspector**(우측 패널)의 **Node Settings** 탭에서:

- **Type**: `File`
- **Name**: `WallTransparencyDither`
  🔴 **`_float` / `_half` 접미사를 붙이지 않는다.** Shader Graph 가 알아서 고른다.
- **Source**: `Assets/3.Materials/Level1_Materials/Occlusion/WallTransparencyDither.hlsl`
- **Inputs** — `+` 를 5번 눌러 추가한다. **이름과 타입이 hlsl 과 정확히 일치해야 한다**(순서도 같게):

  | 이름 | 타입 |
  |---|---|
  | `ScreenPosition` | Vector 2 |
  | `WorldPosition` | Vector 3 |
  | `BaseY` | Float |
  | `FadeHeight` | Float |
  | `Opacity` | Float |

- **Outputs** — `+` 1번:

  | 이름 | 타입 |
  |---|---|
  | `Alpha` | Float |

### 6. 연결

| 출발 | 도착 |
|---|---|
| A `Screen Position` 의 **Out(4)** | C 의 `ScreenPosition` |
| B `Position` 의 **Out(3)** | C 의 `WorldPosition` |
| Blackboard `_WallOccBaseY` | C 의 `BaseY` |
| Blackboard `_WallOccFadeHeight` | C 의 `FadeHeight` |
| Blackboard `_WallOcclusionOpacity` | C 의 `Opacity` |
| C 의 `Alpha` | **D(Keyword) 의 `On` 포트** |
| (D 의 `Off` 포트) | 🔴 **숫자 칸에 `1` 을 직접 입력** (연결하지 않은 Float 포트의 기본값은 0 이다) |
| D 의 출력 | **Master Stack 의 `Alpha`** |

Blackboard 프로퍼티는 왼쪽 목록에서 **그래프 위로 드래그**하면 노드가 생긴다.
`Screen Position` 출력이 Vector4 지만 Vector2 입력에 꽂으면 Shader Graph 가 **XY 만 자동으로**
넘긴다 — 별도 Split 이 필요 없다.

🔴 **`Branch` 노드를 쓰면 안 된다.** 겉보기가 비슷하지만 `Branch` 는 런타임 select(lerp)라
양쪽이 **둘 다 컴파일되어 항상 실행**된다 — 키워드로 코드를 덜어내려던 목적이 무효가 된다.
반드시 **Blackboard 의 키워드를 드래그해서 생기는 `Keyword` 노드**여야 `#if` / `#else` 로
스트립된다.

🔴 **`Off` 포트에는 `1` 을 직접 입력한다.** 연결하지 않은 Float 포트의 기본값은 **0** 이다.
0 으로 두면 Alpha Clip Threshold 가 0 인 지금은 우연히 통과하지만(`clip(0)` 은 버리지 않는다),
Threshold 를 조금이라도 올리는 순간 **키워드가 꺼진 머티리얼까지 통째로 사라진다.**
### 7. Alpha Clip Threshold

Master Stack 의 **Alpha Clip Threshold** 에 **`0`** 을 넣는다. 함수 출력이
`불투명도 - 디더 임계값` 이므로 0 을 기준으로 클립해야 한다.

### 8. 저장하고 Variant 만들기

저장한 뒤 **벽용 Material Variant** 를 만든다 — `Generic_01_A.mat` 을 부모로 하는 자식
    머티리얼에서 `WALL_OCCLUSION_DITHER` 만 **On**.

## 🔴 Variant 로 가르는 것이 선택이 아니라 필수다

`Generic_01_A` 는 **벽만 쓰지 않는다.** 바닥·계단·파이프·기계·문이 같이 쓴다.
키워드를 안 가르고 그라데이션을 무조건 켜면 **기준 높이 위의 바닥이 전부 사라진다** —
4층 구조면 2·3·4층 바닥이 통째로 날아간다.

Variant 는 부모를 **상속**하므로 텍스처·색이 자동으로 따라오고 **톤 차이가 0** 이다.
기존 `*_Occlusion` 변종 14개가 겪던 "손으로 만든 근사치라 톤이 튄다" 문제가 원천적으로 없다.

기존 셰이더(`WallOcclusionDither.shader`)의 **바닥 보호**(위를 향한 면 + 플레이어보다 아래면
보호)는 키워드로 가른 뒤에는 중복이다. 넣지 않아도 된다.

## ⚠️ 키워드가 가리지 못하는 것 — Alpha Clipping

1번의 **Alpha Clipping 은 서페이스 옵션이라 키워드 바깥**이다. 키워드는 *수식*만 가리지
*옵션*은 못 가린다. 즉 이 그래프를 쓰는 **모든 오브젝트**가 `TransparentCutout` 취급을 받고
early-Z 이점을 잃는다.

이 프로젝트는 SSAO · Fog · PlayerSilhouette 이 전부 깊이에 의존하므로 **눈에 보이는 변화가 날 수
있다.** 키워드 OFF 의 실제 비용은 "0" 이 아니라 "디더 수식만 0" 이다.

배선 후 Play 로 **바닥과 SSAO 를 before/after 비교**할 것. 티가 나면 벽 전용 그래프 분리를
다시 논의한다.

---

# 셰이더 그래프 없이 먼저 검증하기

위 배선은 **톤 정확도·바닥 비용·높이 그라데이션**을 위한 것이다. **구역 감지와 페이드가
동작하는지**는 기존 변종 머티리얼로 **지금 바로** 확인할 수 있다.

되는 이유: `WallOcclusionDither.shader` 의 계산은 `factor * _WallOcclusionOpacity` 인데,
구 시스템 드라이버(`WallOcclusionDriver`)가 씬에서 `m_Enabled: 0` 이라 전역 `_WallOccRange.w` 가
0 이고 → `WallOcclusionFactor` 가 **즉시 1.0 을 반환**한다. 결국 `_WallOcclusionOpacity` **단독으로
구동되는 셰이더**가 된다. 디더 오프셋 전역값도 0 고정이라 **정적 패턴**이 되어 TAA 의존 함정도
피한다. 단 **높이 그라데이션은 없다** — 그 셰이더는 `_WallOccBaseY` 를 모른다.

## 테스트 씬 최소 구성

1. 바닥 + 벽 몇 장(Cube 로 충분)
2. 벽 머티리얼을 아래 중 하나로 — 둘 다 디더 셰이더를 쓰고 `_WallOcclusionOpacity` 를 갖고 있다
   - `Assets/3.Materials/Environment/UsedInMap/Shared/Atlases/Occlusion/Generic_01_A_Occlusion.mat`
   - `Assets/3.Materials/Environment/NotUsedInMap/Shared/Atlases/Occlusion/PolygonConstruction_01_A_Occlusion.mat`
3. 빈 오브젝트에 `WallTransparencyGroup` → 대상 Renderer 에 위 벽들
4. 다른 빈 오브젝트에 `WallTransparencyZone` → `BoxCollider` 를 방만큼 키우고(isTrigger 는 자동)
   벽 그룹에 3번을 연결
5. **캡슐 하나를 만들어 레이어만 `Player(6)` 으로 바꾼다.** 컴포넌트도 네트워크도 필요 없다
6. Play 후 캡슐을 박스 안팎으로 움직인다

## 무엇을 볼 것인가

- 진입 시 벽이 디더로 옅어지고, 이탈 시 돌아오는가
- 정적 디더 격자가 눈에 거슬리는가 (거슬리면 표현 방식을 다시 논의)
- 구역 두 개가 같은 그룹을 가리킬 때 OR 로 동작하는가
- 배선 후에는 **1층은 남고 2층부터 사라지는가**, 3·4층이 완전히 사라지는가

## 주의

- `Generic_01_A_Occlusion.mat` 은 **근사치 변종**이다. 톤 비교는 이 단계의 목적 중 하나지 결함이 아니다.
- 컨베이어 벨트 머티리얼에는 변종이 없다. 그룹에 넣으면 조용히 안 사라지고 인스펙터 경고만 뜬다.
- EditMode 테스트가 덮는 것은 **배관**(인스턴스 공유·참조 카운트·복원)이다.
  **벽이 실제로 사라지는지는 Play 로만 확인된다.**
