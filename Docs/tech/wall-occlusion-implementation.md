# Wall Occlusion — per-pixel 월드공간 페이드

작성일: 2026-07-28
브랜치: `feature/map-player-merge`

이 문서가 벽 투명화의 **유일한 현행 설계 문서**다. 이전의
`wall-occlusion-plan.md`와 `wall-occlusion-runtime-bounds.md`는 폐기된 구조를
설명하고 있어 삭제했다(백업은 세션 스크래치패드).

## 무엇이 바뀌었나

이전 구조는 벽 오브젝트 하나당 스칼라 불투명도 하나를 CPU가 계산해
MaterialPropertyBlock으로 밀어 넣었다. 그래서 벽은 **통째로 사라지거나 통째로
남거나** 둘 중 하나였고, 벽 표면 위의 그라데이션이 원천적으로 불가능했다.

현재 구조는 불투명도를 **프래그먼트의 월드 좌표**로 셰이더가 직접 계산한다.
ㅡ자 벽이라면 카메라-플레이어 시선축에 가까운 쪽 끝은 비고, 반대쪽 끝으로 갈수록
원래 불투명도로 매끄럽게 돌아온다. 벽 모듈 경계와 무관하게 연속적이다.

부수 효과로 아래가 전부 사라졌다.

- 물리 쿼리 (SphereCast, ClosestPoint) — 0회
- 벽별 컴포넌트(`WallOcclusionUnit`)와 등록 맵
- MaterialPropertyBlock (SRP Batcher 이탈 원인이었다)
- 페이드/복원 타이밍과 히스테리시스 — 공간 그라데이션에는 on/off 이벤트가 없으므로
  깜빡임을 막을 장치 자체가 필요 없다
- 렌더러 이름 문자열 필터 — 벽/바닥 구분은 이제 셰이더가 노멀+높이로 한다

## 동작

### 1. 머티리얼 바인딩 (맵당 1회)

`WallOcclusionDriver`가 `MapGenerator.OnGenerated`와 자신의 `OnEnable`에서
`WallOcclusionMaterialBinder.Bind()`를 부른다. 하는 일은 설정에 매핑된 소스
머티리얼을 오클루전 변종으로 바꿔 끼우는 것뿐이다. 멱등이라 몇 번 불러도 안전하다.

`OnEnable`에서도 부르므로 절차 생성 없이 이미 배치된 정적 스테이지(`Stage1`)도
잡힌다. 이전 구조는 `OnGenerated`에서만 바인딩해서 정적 씬이 영원히 누락됐다.

### 2. 전역 유니폼 갱신 (매 프레임)

`WallOcclusionDriver.LateUpdate`가 `CameraTargetSwitcher`에서 게임플레이 카메라와
오너 팔로우 타깃을 읽어 `Shader.SetGlobalVector` 네 개를 설정한다.
`Camera.main`이나 씬 전체 카메라 검색은 쓰지 않는다.

| 유니폼 | 내용 |
|---|---|
| `_WallOccPlayerWS` | xyz = 플레이어 월드 위치 |
| `_WallOccCameraWS` | xyz = 게임플레이 카메라 월드 위치 |
| `_WallOccRange` | x=innerRadius, y=outerRadius, z=minimumOpacity, w=enable |
| `_WallOccShape` | x=wallnessThreshold, y=behindFalloff |

카메라나 타깃이 없으면 `w=0`으로 두어 셰이더가 페이드를 통째로 건너뛴다.
벽이 투명한 채로 남는 상태가 생기지 않는다.

### 3. 셰이더 계산 (프래그먼트마다)

```
upness   = smoothstep(floorNormalThreshold, 1, |normalWS.y|)      // 위를 향한 면인가
below    = saturate((playerY - positionWS.y) / floorGuardDepth)   // 플레이어보다 아래인가
protect  = upness * below                                          // 진짜 바닥만 보호

along    = dot(positionWS - cam, sightDir)
onSight  = cam + sightDir * clamp(along, 0, sightLength)
radial   = distance(positionWS, onSight)   // 카메라-플레이어 선분까지의 수직 거리
opacity  = lerp(minOpacity, 1, smoothstep(inner, outer, radial))
opacity  = lerp(opacity, 1, saturate((along - sightLength) / behindFalloff))
result   = lerp(opacity, 1, protect)
```

**바닥 보호에 노멀만 쓰면 안 된다.** 처음엔 `wallness = 1 - |normalWS.y|` 하나로 갈랐는데,
그러면 벽 윗면·선반·창틀 윗면 같은 수평 디테일까지 전부 보호된다. 탑다운 카메라에서는
그 면들이 가장 크게 보이므로, 벽 몸통만 지워지고 윤곽선이 통째로 남아 오히려 더
어색해진다. 그래서 "위를 향한 면"에 **"플레이어보다 아래"** 조건을 곱한다. 플레이어보다
위에 있는 수평면은 실제로 시야를 가리므로 지우는 게 맞다.

선분 밖은 끝점으로 clamp되므로 플레이어 뒤쪽에서도 값이 이어진다. 그 위에
`behindFalloff`로 서서히 불투명하게 되돌려, 플레이어 깊이 평면에서 생길 이음새를
없앤다.

최종 불투명도는 화면 공간 interleaved gradient noise로 디더 클립한다.
ForwardLit / ShadowCaster / DepthOnly / DepthNormals **네 패스 모두** 같은 기준으로
클립하므로, 사라진 벽이 그림자나 depth 기반 효과에 남지 않는다.

## 파일

| 역할 | 경로 |
|---|---|
| 셰이더 | `Assets/3.Materials/Level1_Materials/Occlusion/WallOcclusionDither.shader` |
| 진입점 (Assembly-CSharp) | `Assets/1.Scripts/Rendering/WallOcclusionDriver.cs` |
| 유니폼 변환 | `Assets/1.Scripts/Rendering/Occlusion/WallOcclusionGlobals.cs` |
| 머티리얼 스왑 | `Assets/1.Scripts/Rendering/Occlusion/WallOcclusionMaterialBinder.cs` |
| 튜닝값 | `Assets/1.Scripts/Rendering/Occlusion/WallOcclusionSettings.cs` |
| 설정 에셋 | `Assets/99.Settings/WallOcclusionSettings.asset` |
| 저작 도구 | `Assets/1.Scripts/Rendering/Editor/WallOcclusionAuthoring.cs` |
| EditMode 테스트 | `Assets/Tests/EditMode/Occlusion/WallOcclusionRuntimeTests.cs` |

`WallOcclusionDriver`만 Assembly-CSharp에 있다. `CameraTargetSwitcher`를 참조해야
하기 때문이고, 나머지는 프로젝트 타입을 참조하지 않는 `VeyTrace.Rendering.Occlusion`
어셈블리에 있어 그대로 테스트 가능하다.

## 저작 절차

아트를 새로 받은 뒤에는 이 순서로 한 번 돌린다.

1. `Tools > Map > Authoring > Add Floor+Wall MeshColliders` — 오클루전과는 무관하지만
   플레이어 충돌과 NavMesh에 필요하다
2. `Tools > Rendering > Wall Occlusion > Apply All`
3. `Tools > Rendering > Wall Occlusion > Validate` — `errors=0` 확인
4. `Tools > Rendering > Wall Occlusion > Run EditMode Tests`

`Apply All`은 `Assets/50.Art/MapGen/MapObj/material`을 스캔해 바닥 계열을 뺀 모든
`.mat`의 변종을 만들고 설정에 매핑한다. 경로 하드코딩이 없으므로 아트 교체로
머티리얼이 추가·개명돼도 따라간다.

## 튜닝

`Assets/99.Settings/WallOcclusionSettings.asset`

| 값 | 기본 | 의미 |
|---|---|---|
| `innerRadius` | 1.2 | 시선축에서 이 거리 안쪽은 `minimumOpacity`로 완전히 비운다 |
| `outerRadius` | 4.5 | 이 거리 바깥은 원래 불투명도. inner~outer가 그라데이션 구간 |
| `minimumOpacity` | 0.15 | 가장 많이 가려진 지점에 남길 불투명도. 0이면 완전히 빈다 |
| `behindFalloff` | 1.5 | 플레이어보다 뒤로 이만큼 지나면 원래 불투명도로 복귀 |
| `floorNormalThreshold` | 0.35 | 노멀이 이만큼 위를 향하면 바닥 후보 |
| `floorGuardDepth` | 0.5 | 바닥 후보가 플레이어보다 이만큼 아래여야 실제로 보호 |

그라데이션 폭을 넓히려면 `outerRadius`를 키우고, 더 확실히 비우려면
`minimumOpacity`를 낮춘다.

벽에 붙은 오브젝트가 남아 어색하면 대개 둘 중 하나다.

- **벽 윗면·선반이 남는다** → 바닥 가드가 과보호. `floorGuardDepth`를 줄이거나
  `floorNormalThreshold`를 올린다.
- **시선축에서 조금 떨어진 것만 남는다** → 그라데이션 반경 부족. `outerRadius`를 키운다.

반대로 플레이어가 선 바닥이 비쳐 보이면 `floorGuardDepth`를 키운다.

## 검증 상태

2026-07-28 기준 자동 검증까지 완료했다.

- Unity 6000.3.16f1 C# 컴파일: 오류 0 / 경고 0
- 셰이더 컴파일: `ShaderUtil.ShaderHasError` = false, 메시지 0건
  (MapScene을 연 채 Scene 뷰가 해당 머티리얼을 Forward+로 실제 렌더링하는 상태에서 측정)

  > **셰이더 변종은 지연 컴파일된다.** 아직 렌더된 적 없는 변종의 오류는
  > `ShaderHasError`에 잡히지 않는다. 실제로 이 기능에서 한 번, 대상이 렌더되기 전에
  > 측정한 "컴파일 정상"을 믿었다가 Play에서 Forward+ 클러스터 변종이 깨져 벽이 전부
  > 자주색이 된 적이 있다. 셰이더 검증은 **대상 머티리얼이 실제로 화면에 그려지는
  > 상태에서** 해야 의미가 있다. `Tools > Rendering > Wall Occlusion > Dump Shader
  > Messages`가 이 확인용이다.
- EditMode 테스트: passed 15 / failed 0 / skipped 0
- `Apply All`: 매핑 5쌍 (MA_Wall_basic, MA_Wall_window, MA_prop01, MA_prop02, MA_prop03),
  프리팹 쓰기 0
- `Validate`: errors 0, persistedOcclusionSlots 0

**Play Mode 실검증은 아직이다.** 아래를 사용자가 확인해야 한다.

- ㅡ자 벽에서 시선축 쪽 끝이 비고 반대쪽으로 갈수록 불투명해지는 그라데이션이 보이는가
- 바닥과 천장이 페이드되지 않는가
- 플레이어 뒤쪽 벽이 유지되는가
- 사라진 벽의 그림자가 남지 않는가
- `[`/`]` 카메라 대상 전환 후 새 대상 기준으로 갱신되는가
- MPPM 2~3인에서 각 인스턴스가 독립적인가

## 알려진 한계 / 후속 과제

1. **전역 유니폼 = 프로세스당 플레이어 1명.** MPPM은 별도 프로세스라 문제없지만,
   한 프로세스 안의 분할화면이나 다중 게임플레이 카메라는 지원하지 않는다.

2. **스킬 장판(Telegraph) 채널이 빠졌다.** 이전 구조의
   `WallOcclusionVisibilityContributor`는 삭제했다. 장판 주변 벽을 따로 비우려면
   전역 벡터 배열(최대 N개 구체)을 추가하고 셰이더에서 `min()`으로 합성하면 된다.

3. **변종 머티리얼은 원본 Shader Graph의 근사치다.** 소스 머티리얼 8종은 각각
   별도의 `.shadergraph`(SVN 관리)이고, 변종은 BaseMap / BaseColor / BumpMap /
   Metallic / Smoothness만 재현한다. 원본이 이미시브나 디테일맵을 쓰면 그 부분은
   사라진다. `Validate`가 `_BaseMap`이 빈 변종을 오류로 잡아 흰 벽 회귀는 막는다.

   **더 나은 방향:** 원본 그래프에 Custom Function 노드로 오클루전 불투명도를 넣고
   Dither 노드 → Alpha, Alpha Clipping을 켜면 룩이 그대로 보존되고 변종 머티리얼과
   머티리얼 스왑 자체가 필요 없어진다(바인더도 삭제 가능). 다만 `Assets/50.Art`의
   `.shadergraph` 8개를 편집해야 해서 SVN 커밋과 아트 쪽 합의가 필요하다.

4. **Forward+ 키워드.** `PC_Renderer.asset`이 `m_RenderingMode: 2`(Forward+)인데
   이전 셰이더에는 `_CLUSTER_LIGHT_LOOP` 선언이 없어 추가 광원이 벽에 들어오지
   않았다. 이번에 추가했다. 다른 커스텀 셰이더(`ToonLit`, `WaterDark` 등)도 같은
   누락이 있는지 별도 점검이 필요하다.

   **⚠️ 이 키워드를 손으로 쓴 패스에 그냥 추가하면 셰이더가 깨진다.**
   `_CLUSTER_LIGHT_LOOP`가 켜지면 `LIGHT_LOOP_BEGIN` 매크로가 `inputData`를 직접
   참조하도록 확장된다(`RealtimeLights.hlsl`).

   ```hlsl
   ClusterIterator it = ClusterInit(inputData.normalizedScreenSpaceUV,
                                    inputData.positionWS, 0);
   ```

   따라서 루프 앞에서 `InputData`를 선언하고 이 두 필드를 채워야 한다. 안 하면
   `undeclared identifier 'inputData'`로 컴파일이 실패하고 대상이 전부 자주색이 된다.
   다른 셰이더에 같은 수정을 할 때 반드시 함께 적용할 것.

## 관련되지만 별개인 이슈

- **콜라이더**: 2026-07-28 아트 교체 후 맵 프리팹 12개의 콜라이더가 전부 사라졌다
  (렌더러 1,823개 / 콜라이더 0개). 재설계 후 벽 투명화는 콜라이더를 쓰지 않으므로
  이 기능을 막지는 않지만, 플레이어 충돌·NavMesh·낙하 방지에 필요하다.
  `Tools > Map > Authoring > Add Floor+Wall MeshColliders`로 복구한다.

- **NavMesh non-readable mesh**: `RuntimeNavMeshBuilder: Source mesh ... does not
  allow read access`는 `MapNavMeshBaker`가 Read/Write가 꺼진 MeshCollider mesh를
  읽어서 나는 별개 문제다. Editor Play는 베이크까지 진행하지만 Player 빌드 전에
  importer 정책이나 NavMesh 전용 단순 geometry 정책을 정해야 한다.
