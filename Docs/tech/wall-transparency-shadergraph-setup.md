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
mask     = saturate((worldY - baseY) / fadeHeight)   // 아래 0 → 위 1
strength = 1 - opacity                               // opacity = _WallOcclusionOpacity
result   = lerp(1, mask, strength)
Alpha    = result - 디더 임계값
```

구역 밖이면 `opacity = 1` → `strength = 0` → `result = 1` → **높이와 무관하게 아무 변화 없음.**

## 값의 의미

벽 한 층은 **2.5** 다(`Wall_2stack.prefab` 자식 Y = 0 / 2.5 / 5, 존 프리팹 벽 클러스터
= -5.5 / -3.0 / -0.5 에서 실측).

🔴 **방향은 아래가 사라지고 위가 남는 쪽이다.** `baseY` 에서 알파 0 으로 시작해 위로 갈수록
1 에 도달한다. `fadeHeight = 5.0`(= 2.5 × 2층)이면:

| 높이 | 알파 |
|---|---|
| 1층 (`baseY` ~ +2.5) | **0 → 0.5** (거의 사라짐) |
| 2층 (+2.5 ~ +5.0) | **0.5 → 1.0** |
| 3·4층 (+5.0 이상) | **1.0** — `saturate` 로 **그대로 남는다** |

꺾이는 구간이 없으므로 piecewise 가 필요 없다 — `saturate` 하나면 된다.

`baseY` 와 `fadeHeight` 는 **`WallTransparencyGroup` 이 머티리얼 인스턴스에 써 준다.**
`baseY` 는 그룹 오브젝트의 월드 Y + 인스펙터 오프셋이다 — 존 프리팹의 벽이 존 로컬 원점 기준
음수 좌표에 놓이고 존은 런타임에 배치되므로 절대값을 셰이더에 박을 수 없다.

---

## 배선 절차

1. Surface Type = **Opaque**, **Alpha Clipping** — `Generic_Standard` 는 **둘 다 이미 그렇다**
   (`m_SurfaceType: 0`, `m_AlphaClip: true`). **건드리지 않는다.**

   🔴 이 그래프는 **이미 알파 컷아웃을 쓰고 있다**: `Sample Texture 2D(Albedo) → Split → A` 가
   `Fragment.Alpha` 로, 노출 프로퍼티 `Alpha Clip Threshold` 가 `Fragment.Alpha Clip Threshold` 로
   들어간다. 즉 원래 동작이 `clip(A - T)` 다. **이걸 보존해야 한다** — 아래 7번 참조.
2. Blackboard 에 노출 프로퍼티 3개를 추가한다. **Reference 이름이 정확해야** 코드가 찾는다.

   | 종류 | Reference | 기본값 | 비고 |
   |---|---|---|---|
   | Float | `_WallOcclusionOpacity` | `1` | Slider 0~1 |
   | Float | `_WallOccBaseY` | `0` | 코드가 덮어쓴다 |
   | Float | `_WallOccFadeHeight` | `5` | 코드가 덮어쓴다 |

3. Blackboard 에 **Boolean Keyword** 를 추가한다.
   - Reference: `WALL_OCCLUSION_DITHER` / Scope: **Local** / Default: **Off**
   - Definition: 🔴 **`Multi Compile`** — `Shader Feature` 가 아니다.

   `Shader Feature` 는 **에셋 머티리얼이 켜놓은 조합만** 컴파일한다. 우리는 키워드를
   **코드에서** 켜므로(`WallTransparencyGroup` 이 런타임 인스턴스에 `EnableKeyword`) 켜진
   에셋이 하나도 없고, 그러면 그 변종이 **빌드에서 잘려나가 에디터에서만 동작한다.**
   찾기 어려운 종류의 버그다.

### 만들 노드는 6개다

그래프 빈 곳에서 **우클릭 → Create Node** 로 아래를 만든다.

| # | 노드 | 설정 |
|---|---|---|
| A | **Screen Position** | 모드 **Default** (기본값 그대로) |
| B | **Position** | Space 를 **World** 로 바꾼다 |
| C | **Custom Function** | 아래 5번에서 설정 |
| D | **Keyword** | Blackboard 의 키워드를 **그래프 위로 드래그**하면 생긴다 |
| E | **Subtract** | 기존 컷아웃 여유값을 만든다 (7번) |
| F | **Minimum** | 기존 컷아웃과 디더를 합친다 (7번) |

`Split`(월드 Y용)·`Multiply`·`Screen` 같은 노드는 **필요 없다.**
픽셀 변환과 Y 추출은 hlsl 안에서 한다.

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

Blackboard 프로퍼티는 왼쪽 목록에서 **그래프 위로 드래그**하면 노드가 생긴다.
`Screen Position` 출력이 Vector4 지만 Vector2 입력에 꽂으면 Shader Graph 가 **XY 만 자동으로**
넘긴다 — 별도 Split 이 필요 없다.

### 7. 🔴 기존 알파 컷아웃과 합치기 — 곱하지 말고 `min`

이 그래프는 이미 `clip(A - T)` 로 컷아웃을 하고 있다(A = Albedo 텍스처의 알파, T = 노출
프로퍼티 `Alpha Clip Threshold`). 이걸 **보존한 채** 디더를 얹어야 한다.

**곱하면 안 된다.** Custom Function 의 출력은 0~1 알파가 아니라 **부호 있는 여유값**
(`불투명도 - 디더임계값`, 0 미만이면 버림)이다. `A × 여유값` 으로 곱하면 **A 가 0 인 픽셀에서
결과가 0** 이 되는데, 0 은 음수가 아니라 **버려지지 않는다** — 원래 컷아웃으로 사라져야 할
픽셀이 살아난다.

조건이 둘이면(텍스처가 버리라거나 / 디더가 버리라거나) **더 작은 쪽**을 취한다.
이 레포의 다른 브랜치도 같은 식을 쓴다 — `min(BaseAlpha - BaseThreshold, occlusionMargin)`.

| 노드 | 입력 |
|---|---|
| **E** `Subtract` | A = 기존 `Split` 의 **A(1)** / B = `Alpha Clip Threshold` 프로퍼티 |
| **F** `Minimum` | A = **E** 출력 / B = **C** 의 `Alpha` |

| 출발 | 도착 |
|---|---|
| **F** 출력 | D(Keyword) 의 **`On`** |
| **E** 출력 | D(Keyword) 의 **`Off`** ← 상수 `1` 이 아니다 |
| D 출력 | `Fragment.Alpha` |
| 상수 **`0`** | `Fragment.Alpha Clip Threshold` (프로퍼티 연결을 **끊고** 0 을 넣는다) |

검산:

- **키워드 Off** → `Alpha = A - T`, threshold `0` → `clip(A - T)` — **원래와 완전히 동일**
- **키워드 On** → `clip(min(A - T, 디더여유))` — 둘 중 하나라도 버리라면 버린다

`Alpha Clip Threshold` 프로퍼티는 사라지지 않고 **E 의 Subtract 로 옮겨간다.** 머티리얼에서
값을 조절하던 동작은 그대로다.

🔴 **`Branch` 노드를 쓰면 안 된다.** 겉보기가 비슷하지만 `Branch` 는 런타임 select(lerp)라
양쪽이 **둘 다 컴파일되어 항상 실행**된다 — 키워드로 코드를 덜어내려던 목적이 무효가 된다.
반드시 **Blackboard 의 키워드를 드래그해서 생기는 `Keyword` 노드**여야 `#if` / `#else` 로
스트립된다.

### 8. 저장 — 그게 끝이다

**Save Asset.** Material Variant 는 **만들지 않는다.** 벽 프리팹의 머티리얼도 **건드리지 않는다.**

## 벽과 바닥은 런타임 키워드로 갈린다

`Generic_01_A` 는 **벽만 쓰지 않는다.** 바닥·계단·파이프·기계·문이 같이 쓴다.
키워드를 안 가르고 그라데이션이 켜지면 **기준 높이 근처의 바닥이 사라진다** —
아래가 사라지는 방향이므로 1층 바닥이 통째로 날아간다.

가르는 주체는 **`WallTransparencyGroup`** 이다. 그룹은 자기 대상 렌더러의 머티리얼마다
런타임 인스턴스를 하나 만들고 **거기서만** `EnableKeyword("WALL_OCCLUSION_DITHER")` 를 한다.

| | 결과 |
|---|---|
| 그룹에 담긴 벽 | 키워드 ON 인스턴스 → 그라데이션 동작 |
| 바닥·파이프·기계 | 원본 머티리얼 그대로 → **OFF 변종, 디더 코드가 컴파일에서 빠짐** |

그래서 **벽 프리팹의 머티리얼을 손으로 교체할 일이 없고**, 바닥에 잘못 배정할 위험도 없다.
대상을 정하는 곳이 **그룹의 렌더러 리스트 한 군데**로 모인다.

기존 셰이더(`WallOcclusionDither.shader`)의 **바닥 보호**(위를 향한 면 + 플레이어보다 아래면
보호)는 이렇게 가른 뒤에는 중복이다. 넣지 않아도 된다.

## 셰이더 컴파일 시점

| 단계 | 언제 | 비용 |
|---|---|---|
| 변종 생성 | **빌드 타임** | `Multi Compile` 이라 변종 1개 → 2개. 빌드 시간·용량만 |
| PSO 생성 | **그 변종을 처음 그리는 프레임** | 여기서 히치가 난다 |

그룹은 **`Awake` 에서** 인스턴스를 만들고 키워드를 켠다(구역 진입 시점이 아니다). 따라서 벽은
**맵 로드 직후 첫 프레임부터** ON 변종으로 그려지고, PSO 도 거기서 만들어진다 —
**게임 중 벽이 사라지는 순간에는 히치가 없다.**

키워드가 처음부터 켜져 있으므로 그룹에 담긴 벽은 평소에도 디더 수식을 돈다(opacity=1 이라
결과가 안 보일 뿐). ALU 몇 개라 무시할 수준이다. **비용이 0 인 쪽은 바닥·파이프·기계다.**

로딩 히치가 실제로 보이면 `ShaderVariantCollection` 프리워밍을 검토한다 — 다만 셰이더 하나에
불리언 하나라 미리 할 일은 아니다. Play 로 보고 판단한다.

## 참고 — Alpha Clipping 은 원래부터 켜져 있었다

Alpha Clipping 은 서페이스 옵션이라 키워드로 못 가린다. 이 그래프를 쓰는 모든 오브젝트가
`TransparentCutout` 취급을 받고 early-Z 이점을 잃는다.

**다만 이건 우리가 추가한 비용이 아니다.** `Generic_Standard` 는 이 작업 전부터
`m_AlphaClip: true` 였고 텍스처 알파로 컷아웃을 하고 있었다. 우리는 그 식을 보존만 한다.

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
- 배선 후에는 **1층이 사라지고 2층에서 돌아오는가**, 3·4층이 그대로 남는가

## 주의

- `Generic_01_A_Occlusion.mat` 은 **근사치 변종**이다. 톤 비교는 이 단계의 목적 중 하나지 결함이 아니다.
- 컨베이어 벨트 머티리얼에는 변종이 없다. 그룹에 넣으면 조용히 안 사라지고 인스펙터 경고만 뜬다.
- EditMode 테스트가 덮는 것은 **배관**(인스턴스 공유·참조 카운트·복원)이다.
  **벽이 실제로 사라지는지는 Play 로만 확인된다.**
