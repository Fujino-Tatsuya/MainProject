# V3 맵 전환 및 캐릭터 가림 투명화 Git 인계서

작성일: 2026-08-09

대상 브랜치: `feature/trensparent`

대상 Unity: `6000.3.16f1`

## 1. 이 변경을 한 이유

이번 변경은 단순히 벽의 알파값을 낮추는 작업이 아니다. 다음 문제가 동시에 연결되어 있어 V3 맵 데이터와 투명화 판정을 함께 정리했다.

- 기존 Stage/Zone의 좌표·회전·슬롯 정보와 V3 프리팹 정보가 섞여, 랜덤 생성 결과에서 Zone이 어긋났다.
- 미니맵이 Legacy footprint와 V3 실배치를 함께 사용해 실제 맵과 다른 실루엣과 마커 위치를 표시했다.
- 기존 투명화는 넓은 SphereCast에 맞은 등록 오브젝트를 그대로 처리하여, 캐릭터를 실제로 가리지 않는 옆 물체도 투명해졌다.
- 별도 투명 머티리얼로 교체하는 방식은 원본 머티리얼의 색감, PBR 값, 발광과 애니메이션을 달라지게 했다.
- 1층·2층·지하가 있는 맵에서는 단순 레이 판정만으로 현재 층보다 높은 구조물을 일관되게 다루기 어렵다.
- 아트 하이어라키만 옮기고 런타임 등록 배열을 갱신하지 않으면 계단·경사로가 새 층에 속해 보여도 실제 투명화에는 반영되지 않았다.

따라서 최종 원칙은 다음과 같다.

1. V3 `ElevationStack`이 층 후보를 분류한다.
2. 현재 플레이어가 있는 층보다 높은 층만 후보가 된다.
3. 후보 중 카메라와 플레이어 캡슐 사이 시야선을 실제로 가리는 Collider만 투명화한다.
4. 원본 머티리얼은 교체하지 않고 공통 셰이더 컷아웃 값만 `MaterialPropertyBlock`으로 제어한다.

## 2. 런타임 투명화 변경

### 2.1 후보 수집과 실제 가림 판정을 분리

`WallOcclusionDriver`는 매 프레임 다음 순서로 동작한다.

1. 플레이어 기본 충돌 캡슐의 중심, 상·하단, 반경을 구한다.
2. 카메라에서 플레이어 중심까지 `SphereCastNonAlloc`을 실행해 넓은 후보를 빠르게 수집한다.
3. 화면 공간 플레이어 캡슐에 11개의 시야선 샘플을 만든다.
   - 캡슐 양 끝 2개
   - 캡슐 길이 25%, 50%, 75% 지점에서 좌·중·우 3개씩
4. 각 후보 Collider에 `Collider.Raycast`를 실행한다.
5. 11개 중 하나라도 플레이어보다 앞에서 맞은 Collider만 실제 가림 대상으로 인정한다.

이 2단계 판정이 필요한 이유는 SphereCast가 플레이어 캡슐 폭을 포괄하는 넓은 후보 검사이므로, 그 결과만 사용하면 난간이나 옆 프랍처럼 실제 실루엣을 가리지 않는 물체까지 선택되기 때문이다.

새 파일 `WallOcclusionSightlineFilter.cs`가 11개 샘플 생성과 Collider별 정밀 판정을 담당한다.

### 2.2 고도 스택 판정

- 플레이어가 접지한 Collider가 `ElevationLevel`에 등록되어 있으면 그 층을 최우선으로 사용한다.
- 접지 정보를 얻을 수 없을 때는 발 Y와 수직 이동 방향을 사용한다.
- 상승 중에는 두 층 높이 구간의 20%를 넘으면 위층으로 전환한다.
- 하강 중에는 60% 진행했을 때 아래층으로 전환한다.
- 현재 층보다 높은 `ElevationLevel`만 층 단위 투명화 후보가 된다.
- 점프나 낙하처럼 진입로를 통과하지 않는 이동도 전체 층 영역과 접지/Y 판정으로 복구한다.

### 2.3 화면 공간 캡슐 컷아웃

공통 `WallOcclusionClip.hlsl`이 화면 공간에서 플레이어 충돌 캡슐 모양의 컷아웃을 만든다.

- 중심부는 완전히 비운다.
- 바깥쪽은 디더 임계값을 점진적으로 변화시켜 부드러운 feather를 만든다.
- 플레이어보다 뒤에 있는 픽셀은 깊이 제한으로 보호한다.
- Forward, DepthOnly, DepthNormals, ShadowCaster 패스가 같은 규칙을 사용한다.

현재 전역 설정값은 다음과 같다.

| 항목 | 값 | 의미 |
|---|---:|---|
| Screen Capsule Radius Scale | 1.0 | 플레이어 투영 캡슐 반경을 그대로 사용 |
| Hole Padding Pixels | 0.5 | 완전 컷아웃 반경 추가 여유 |
| Feather Radius Scale | 3.0 | 투영 반경의 3배를 feather 목표 폭으로 사용 |
| Min / Max Feather Pixels | 1 / 143.9 | 최종 feather 폭 제한 |
| Behind Falloff | 1.5 | 플레이어 뒤 픽셀 보호 깊이 여유 |
| Fade In / Grace / Restore | 0.1 / 0.1 / 0.2초 | 투명화 진입·유지·복원 시간 |

`Screen Capsule Radius Scale`은 정밀 시야선 폭과 컷아웃 중심 반경에 함께 영향을 준다. `Feather Radius Scale`은 바깥 그라데이션 폭만 바꾸므로 두 값을 같은 용도로 조절하면 안 된다.

### 2.4 원본 머티리얼 유지

`WallOcclusionRendererController`는 더 이상 `sharedMaterials`를 별도 투명 머티리얼로 바꾸지 않는다. 원본 머티리얼을 유지한 채 Renderer별 `MaterialPropertyBlock`의 `_WallOcclusionStrength`만 변경한다.

이렇게 바꾼 이유는 별도 투명 머티리얼 복제본이 원본 셰이더의 색감·노멀·메탈릭·발광·컨베이어 애니메이션과 달라지는 문제를 제거하기 위해서다.

- 지원하지 않는 머티리얼은 안전하게 불투명 상태로 제외한다.
- 같은 문제에 대한 경고는 한 번만 출력한다.
- 기존 `*_Occlusion.mat` 복제 머티리얼과 `WallOcclusionDither.shader`는 제거했다.

## 3. V3 프리팹과 메인 맵 연결

### 3.1 추가된 V3 자산

- Stage: `PF_Stage_01_V3`
- Zone: Large 3개, Medium 5개, Small 3개로 총 11개
- V3 Zone이 참조하는 Legacy 호환용 dependency와 독립 프랍 wrapper
- 기존 GUID 연결을 유지하기 위한 각 에셋의 `.meta`

Stage와 Zone 모두 아래 최소 규칙을 동일하게 사용한다.

```text
PF_Stage_* 또는 PF_Zone_*
└─ Occlusion
   └─ ElevationStack_01 [ElevationStack]
      └─ Level_L01 / Level_L02 / Level_B01 ... [ElevationLevel]
         └─ Content
            ├─ Surfaces
            ├─ OccludableProps
            └─ LevelOnlyProps
```

- `Surfaces`: 해당 층의 바닥·벽·경사로·계단 등 층 전체 구조물
- `OccludableProps`: 플레이어를 실제로 가릴 때 개별 투명화할 프랍. 직속 자식 프랍마다 `OcclusionSection` 하나가 필요하다.
- `LevelOnlyProps`: 층과 함께만 사라지고 개별 시야선 가림 대상으로는 사용하지 않는 프랍
- `OccludableProps`와 `LevelOnlyProps`는 비어 있어도 반드시 하나씩 둔다. 누락/중복은 검증 오류다.

### 3.2 메인 플로우 전환

- `ZoneWiring`의 Catalog 경로와 11개 Zone 이름을 V3로 교체했다.
- 메인 Stage 참조를 `PF_Stage_01_V3`로 교체했다.
- Legacy 몬스터 스폰 마커 57개와 Stage Slot 10개의 저작 데이터를 V3에 이관했다.
- `4.MapScene`의 Catalog 11개, Stage 1개, Slot override 10개를 V3 참조로 연결했다.
- 구형 씬 인스턴스에 추가되어 있던 중복 MeshCollider 40개를 제거했다.
- V3 제작 방향(-Z Front)과 프로젝트 방향(+Z Front)의 차이는 플레이어·카메라를 바꾸지 않고 메인 V3 맵과 저장 배치를 180도 회전하는 방식으로 고정했다.
- 수정된 M Zone 저작 위치를 기준으로 Slot 4와 Slot 9의 저장 배치를 재정렬했다.

### 3.3 계단·경사로 재등록 주의

Unity 하이어라키에서 오브젝트를 다른 `Level_*` 아래로 옮기는 것만으로는 `ElevationLevel`의 직렬화된 Renderer/Collider 배열이 자동 갱신되지 않는다. 따라서 층 이동 후에는 반드시 `Register-Wire Selected Prefabs`를 다시 실행해야 한다.

마지막 재등록에서 다음 층 소속 변경을 반영했다.

- `PF_Zone_L_Type_B_V3`: 계단/경사 관련 4개를 L01에서 L02로 이동
- `PF_Zone_M_Type_A_V3`: 2개 재등록
- `PF_Zone_M_Type_B_V3`: 1개 재등록
- `PF_Zone_Quest_02_V3`: 2개를 B01에서 L01로 이동

## 4. 추가된 에디터 도구

### 4.1 Wall Occlusion 도구

메뉴: `Tools > Rendering > Wall Occlusion`

#### Register-Wire Selected Prefabs

선택한 Stage/Zone/독립 프랍 프리팹을 현재 하이어라키 기준으로 다시 등록하고 저장한다.

- `ElevationStack`, `ElevationLevel`, `OcclusionSection` 연결을 재구성한다.
- Renderer/Collider 배열을 현재 자식 구성으로 다시 만든다.
- 이름·직속 자식·중복 소유·머티리얼 지원 여부를 검증한다.
- 하이어라키를 옮긴 뒤 런타임 배열이 과거 상태로 남는 휴먼 에러를 막기 위한 도구다.

#### Validate Selected Prefabs

선택한 프리팹의 현재 등록 상태를 검사한다. 오류는 빌드를 막지 않고 Console 로그로 보고한다. 수정 작업 후 빠른 확인용이며, 하이어라키를 변경했다면 검증 전에 Register-Wire를 먼저 실행한다.

#### Dump Shader Messages

투명화가 연결된 네 Shader Graph의 컴파일 메시지를 한 번에 출력한다.

- `Generic_Standard.shadergraph` — SVN
- `Generic_Basic.shadergraph` — SVN
- `ConvayorBelt_Graph.shadergraph` — Git
- `ConvayorBelt_Corner_Graph.shadergraph` — Git

#### Run EditMode Tests

Wall Occlusion EditMode 테스트만 실행한다. 런타임 등록, 고도 전환, 셰이더 바인딩, 화면 캡슐, 정밀 시야선 판정을 확인한다.

### 4.2 Level Delivery V3 이관 도구

메뉴: `Tools > Map > Level Delivery V3`

- `Register ZoneLayout Classification`: 11개 V3 Zone에 Size/Role/Difficulty/DefaultGroup 분류를 기록한다.
- `Validate ZoneLayout Classification`: 초기 분류 등록 상태를 확인한다.
- `Copy Legacy Monster Spawn Authoring`: Legacy 몬스터 포인트와 entry를 V3 Zone으로 복제한다.
- `Validate Legacy Monster Spawn Authoring`: 11개 Zone과 57개 마커의 이관 결과를 비교한다.
- `Copy Legacy Stage Slots`: Legacy Stage의 Slot 10개와 Zone 참조를 V3로 복제한다.
- `Validate Legacy Stage Slots`: 저장 위치·회전·Zone 참조를 비교한다.
- `Switch Main Flow Stage and Catalog to V3`: `4.MapScene`과 Catalog의 Legacy 참조를 V3로 교체한다.
- `Validate Main Flow V3 Connection`: Catalog 11, Stage 1, Slot 10, Legacy 참조 0 조건을 검사한다.
- `Validate V3 NavMesh (seed 12345)`: 재현 가능한 고정 시드로 V3 생성 및 NavMesh 연결을 검사한다.

이관용 `Copy`와 `Switch` 메뉴는 반복 실행 시 저작값을 다시 덮어쓸 수 있으므로 일상 검증 버튼처럼 사용하지 않는다. 이미 이관된 프로젝트에서는 대응하는 `Validate` 메뉴를 사용한다.

`Validate ZoneLayout Classification`은 몬스터 저작 데이터를 넣기 전 초기 구조 검사용 조건을 일부 포함하므로, 마커 이관 완료 후의 최종 승인 도구는 아니다. 최종 확인은 Monster Spawn, Stage Slots, Main Flow 검증을 각각 사용한다.

### 4.3 V3 Slot 배치 도구

메뉴: `Tools > Map > Authoring > V3 Slots`

- `Align Slot 4 and 9 Baselines From Authored M Zones`: 수정된 M Zone 저작 위치로 Slot 4·9의 기준 배치를 맞춘다.
- `Rotate Main V3 Map 180 (One Time)`: V3 Stage와 저장된 배치/회전을 함께 180도 돌린다. 이름 그대로 한 번만 사용하는 마이그레이션이다.
- `Validate Main V3 Map 180 Orientation`: Stage와 저장 배치의 방향 고정을 검증한다.
- `Apply Canonical V3 Stage Bays`: V3 Stage의 표준 bay 위치로 Slot 배치를 맞춘다. 수동 오프셋을 덮을 수 있으므로 의도한 경우에만 사용한다.
- `Report Placement Bays`: 현재 Slot의 배치 좌표를 로그로 출력한다.

## 5. NavMesh와 미니맵 변경

### NavMesh

`MapNavMeshBaker`가 Editor 검증과 런타임 Bake 모두에서 같은 `NavMeshSurface` 설정을 보장하도록 정리했다. V3의 기존 보행면 모양은 수정하지 않고, 물리 Collider 및 Ground/Default 레이어를 동일한 조건으로 수집한다.

### 미니맵

Legacy footprint와 Stage1 전용 복도 데이터 사용을 제거했다.

- 활성 `ElevationLevel.ContentRenderers`를 수집한다.
- 모든 층 Renderer Bounds의 XZ 합집합으로 맵 실루엣을 만든다.
- Player, Monster, UI, Water 등 지형이 아닌 레이어는 제외한다.
- 역할 아이콘은 저장 Slot 추정값 대신 실제 생성된 V3 Zone Renderer 중심에 놓는다.
- V3 Renderer가 없으면 Legacy 데이터로 조용히 폴백하지 않고 오류를 내고 미니맵을 비활성화한다.

이렇게 바꾼 이유는 Legacy 좌표와 V3 실제 배치를 혼합하면 맵은 정상 생성되어도 미니맵만 회전·이동이 어긋나기 때문이다.

## 6. 컨베이어와 무빙 플랫폼

- 직선/코너 컨베이어 Shader Graph에 공통 투명화 clip 함수를 연결했다.
- 컨베이어 프리팹 머티리얼 참조를 정리했다.
- 코너 변형 프리팹 `ConveyorTileCorner02`를 추가했다.
- `MovingPlatform`을 V3에서 사용하는 메시/머티리얼/Collider 구성에 맞췄다.
- 컨베이어 테스트 씬도 새 참조와 배치로 갱신했다.

원본 컨베이어 애니메이션 셰이더를 유지한 이유는 별도 투명 셰이더로 교체할 경우 벨트 이동 표현과 색감이 달라지기 때문이다.

## 7. 머티리얼 정리와 VCS 경계

실제 V3 프리팹에서 사용하는 머티리얼을 조사한 뒤 가능한 대상은 `Generic_01_A_V3` 계열로 통합했고, 텔레포터도 같은 계열로 교체했다. 사용하지 않는 투명 머티리얼 복제본은 삭제했다.

이 커밋은 Git 소유 파일만 포함한다.

- Git 포함: 코드, 씬, 프리팹, 설정, 테스트, 문서, 컨베이어 Shader Graph, 공통 HLSL, Git 소유 구형 투명 머티리얼 삭제
- Git 제외: `Assets/50.Art` 아래 `Generic_Standard.shadergraph`, `Generic_Basic.shadergraph`와 해당 `.meta`
- `Assets/50.Art` 변경은 SVN 소유이며 사용자가 별도로 반영했다. 협업자는 이 Git 커밋만 받아서는 Generic 계열 머티리얼의 투명화 셰이더 변경을 모두 받지 못하므로 대응 SVN 리비전도 함께 받아야 한다.

## 8. 검증 결과

이번 작업 중 Unity Editor에서 확인한 결과는 다음과 같다.

- C# 컴파일 오류: 0
- Wall Occlusion EditMode 테스트: 21/21 통과
- 지원 Shader Graph 컴파일 메시지: 오류 0
- Legacy 몬스터 마커 이관: Zone 11/11, Marker 57
- Legacy Stage Slot 이관: 10/10
- 메인 플로우 V3 연결: Catalog 11/11, Stage 1/1, Slot 10/10, Legacy 참조 0
- V3 하이어라키 이동 후 대상 프리팹 재등록 완료

## 9. 알려진 제약과 후속 작업

- `PF_Zone_M_Type_B_V3`의 manufacture 계열 일부는 내부 내장 `generic_01` 머티리얼 때문에 `_WallOcclusionStrength` 미지원 오류가 남을 수 있다. 해당 Renderer는 안전하게 불투명으로 제외된다. 아트 머티리얼 교체 여부는 별도 결정한다.
- `PF_Zone_L_Type_B_V3`의 다리와 조작 패널 기능 복원은 이번 범위에서 제외했다. 리팩터링 후 별도 작업한다.
- 계단/경사로가 하이어라키상 올바른 층에 있어도 Collider가 다른 오브젝트에 있거나 시야선 샘플을 맞지 않으면 투명화되지 않을 수 있다. 현재 확인된 경사로 사례는 실제 Collider 범위가 원인이었고 시스템 변경 없이 유지하기로 했다.
- 이 변경은 네트워크 권한이나 게임플레이 상태를 바꾸지 않는다. 투명화와 미니맵은 각 클라이언트 카메라의 로컬 표시 기능이다.

## 10. 협업자 체크리스트

1. Git에서 `feature/trensparent` 변경을 받는다.
2. 사용자가 별도로 커밋한 대응 SVN 셰이더 변경도 함께 받는다.
3. Unity `6000.3.16f1`로 열고 `.meta`를 재생성하거나 V3 폴더를 임의로 이동하지 않는다.
4. Stage/Zone 하이어라키에서 층 또는 프랍 소속을 바꿨다면 해당 프리팹을 선택하고 `Register-Wire Selected Prefabs`를 실행한다.
5. `Validate Selected Prefabs`, `Run EditMode Tests`, `Dump Shader Messages`로 투명화 구성을 확인한다.
6. 맵 이관 검증은 Monster Spawn, Stage Slots, Main Flow 검증을 각각 실행한다.
7. Play에서 다음을 확인한다.
   - 캐릭터를 실제로 가리는 벽/프랍만 투명화되는가
   - 아래층에서 겹쳐 보이는 위층 전체 구조가 후보가 되는가
   - 위층에 올라가면 그 층 바닥이 불필요하게 사라지지 않는가
   - 컷아웃 중심은 비고 바깥 feather가 자연스러운가
   - 미니맵 실루엣과 역할 아이콘이 실제 생성 Zone과 일치하는가
