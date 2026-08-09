# V3 다리·조작 패널 프리팹 추가 개발 맥락 및 수정 사유서

- 작성일: 2026-08-09
- 대상 프로젝트: `C:\MainProject-git`
- 기준 Main 커밋: `bb35c9283d0194e68416454a0057e92068edec91`
- 대상 V3 Zone: `Assets/2.Prefabs/Map/LevelDeliveryV3/Zones/PF_Zone_L_Type_B_V3.prefab`
- 문서 목적: 새 세션에서 V3 다리 개통 기믹의 프리팹화 및 개발 방향을 결정하기 위한 맥락 인계

## 1. 현재 목표

LegacySafeV3로 전환된 Zone 프리팹에 기존 다리 개통 기능을 다시 연결한다.

플레이어 관점의 목표 동작은 다음과 같다.

1. `PF_Zone_L_Type_B_V3`가 메인 맵 생성 흐름에서 배치된다.
2. 네 모서리의 조작 패널을 플레이어가 상호작용할 수 있다.
3. 필요한 패널 활성화 조건이 충족되면 끊어진 다리가 열린 위치로 이동한다.
4. 호스트와 클라이언트가 같은 패널 상태와 다리 진행도를 본다.
5. 닫힌 상태에서는 몬스터가 허공을 건너지 못하고, 열린 상태에서는 다리를 이용할 수 있다.
6. 다리와 패널은 새 고도·카메라 가림 투명화 시스템에 정상 등록되어야 한다.

## 2. 작업이 필요한 이유

최종 V3 패키지는 Renderer, Collider, 고도 스택 및 투명화 컴포넌트는 보존했지만 기존 Zone의 게임플레이 MonoBehaviour와 저작 데이터는 포함하지 않았다.

기존 `ZoneL_typeB.prefab`에는 루트 `ZoneBridgeGate`가 있었지만 새 `PF_Zone_L_Type_B_V3.prefab`에는 없다. 따라서 V3 Zone을 ZoneLayoutCatalog에 연결하는 것만으로는 패널 상호작용과 다리 개통 기능이 복구되지 않는다.

확인된 전체 V3 게임플레이 컴포넌트 누락은 다음과 같다.

| 컴포넌트 | 기존 수량 | V3 수량 | 누락 |
|---|---:|---:|---:|
| `ZoneLayout` | 11 | 0 | 11 |
| `WaypointNode` | 5 | 0 | 5 |
| `MovingPlatform` | 2 | 0 | 2 |
| `ConveyorGroup` | 1 | 0 | 1 |
| `ConveyorTile` | 29 | 0 | 29 |
| `ZoneBridgeGate` | 1 | 0 | 1 |
| `ZoneSlot` | 10 | 0 | 10 |

사용자는 MovingPlatform과 ConveyorTile의 새 버전을 별도로 준비하여 해당 V3 Zone에 다시 배치했다. 다음으로 남은 독립 개발 대상이 다리와 조작 패널이다.

## 3. 기존 다리 시스템

### 런타임 컴포넌트

- `Assets/1.Scripts/Map/ZoneBridgeGate.cs`
- `Assets/1.Scripts/Map/ZoneBridgeGateManager.cs`
- `Assets/1.Scripts/Map/MapContentSpawner.cs`

`ZoneBridgeGate`는 Zone 프리팹 루트가 보유하는 순수 저작·로컬 연출 컴포넌트다.

- 조작 패널 Transform 목록
- 다리 Segment 목록
- 각 Segment의 닫힘 로컬 위치
- 각 Segment의 열림 로컬 위치
- 개통 보간 시간
- 상호작용 반경
- 조작 패널 링 표시 설정
- 닫힌 다리 구간의 `NavMeshObstacle` carve 처리

Zone 프리팹 자체는 네트워크 오브젝트가 아니다. 서버 판정과 상태 복제는 씬 상주 `ZoneBridgeGateManager`가 담당하고, 각 피어는 복제된 진행도로 자기 로컬 다리를 동일하게 이동한다.

`MapContentSpawner`는 생성된 Zone에서 `ZoneBridgeGate`를 찾아 슬롯 ID를 지정하고 매니저에 등록한다. 따라서 V3 Zone 루트 또는 생성된 Zone 하위에서 `GetComponent<ZoneBridgeGate>()`로 찾을 수 있는 배치가 필요하다. 현재 구현은 루트 컴포넌트를 전제로 한다.

### 기존 저작 도구

- `Assets/1.Scripts/Map/Editor/ZoneBridgeGateWiring.cs`

현재 도구에는 다음 레거시 값이 하드코딩되어 있다.

```text
ZonePath    = Assets/2.Prefabs/Map/Zoneprefab/ZoneL_typeB.prefab
PanelPrefix = Env_panel
BridgePrefix = Env_bridge
```

따라서 기존 메뉴를 V3 프리팹에 그대로 실행하면 대상을 찾지 못한다.

기존 메뉴:

```text
Tools/Map/Authoring/Wire Zone Bridge Gate (ZoneL_typeB)
Tools/Map/Authoring/Estimate Bridge Open Positions (ZoneL_typeB)
Tools/Map/Authoring/Record Bridge CLOSED Positions (prefab stage)
Tools/Map/Authoring/Record Bridge OPEN Positions (prefab stage)
```

기존 도구를 즉시 V3 전용으로 덮어쓰면 레거시 프리팹 재저작 경로가 사라진다. 새 세션에서 호환 정책을 먼저 결정해야 한다.

## 4. V3에서 확인된 다리·패널 대상

### 조작 패널 4개

V3 패널은 독립 Prop Wrapper 프리팹 인스턴스다.

| 기존 패널 순서 | V3 Wrapper 루트 | V3 로컬 XZ 위치 |
|---:|---|---:|
| 0 | `PF_Prop_object_panel_5334bdfe_V3` | `(-12.882, -12.910)` |
| 1 | `PF_Prop_object_panel_3aeb36ff_V3` | `(-13.091, 13.228)` |
| 2 | `PF_Prop_object_panel_8cfd7061_V3` | `(13.158, -12.900)` |
| 3 | `PF_Prop_object_panel_ea07916d_V3` | `(13.109, 13.224)` |

패널 인덱스는 복제 키로 사용되므로 프리팹을 저장할 때 순서를 고정해야 한다. Wrapper 파일명의 해시 순으로 정렬하지 말고 기존과 같은 사분면 순서를 명시적으로 사용한다.

```text
0 = -X, -Z
1 = -X, +Z
2 = +X, -Z
3 = +X, +Z
```

### 다리 조각 4개

V3에는 다음 다리 Transform이 존재한다.

| 이름 | 현재 로컬 위치 | Renderer | Collider |
|---|---|---:|---:|
| `floor_bridge_001` | `(0.037, -0.409, 12.190)` | 있음 | BoxCollider 있음 |
| `floor_bridge_002` | `(0.037, -0.409, -11.788)` | 있음 | BoxCollider 있음 |
| `floor_MV_bridge_001` | `(0.037, -0.409, 12.410)` | 있음 | BoxCollider 있음 |
| `floor_MV_bridge_002` | `(0.037, -0.409, -12.008)` | 있음 | BoxCollider 있음 |

네 Transform 모두 해당 Level의 `Content` 직속 자식이다. Renderer와 Collider는 이미 `ElevationLevel` 저작 데이터에 등록되어 있다.

추천 초기 해석:

- `floor_bridge_*`: 고정 다리 조각
- `floor_MV_bridge_*`: 실제로 중앙 방향으로 이동하는 다리 조각
- Segment 목록에는 네 조각을 모두 명시적으로 둘 수 있다.
- 고정 조각은 `ClosedLocalPosition == OpenLocalPosition`으로 둔다.
- 이동 조각만 Z축을 따라 중앙 방향으로 열림 위치를 저작한다.

이 해석은 이름과 현재 배치 구조에 근거한 추천이며, 최종 이동 대상은 프리팹 모드에서 시각적으로 확인한 뒤 확정한다.

## 5. 기존 좌표를 그대로 복사할 수 없는 이유

레거시 `ZoneL_typeB` 다리 Segment는 주로 X축을 따라 움직였다.

```text
Env_bridge01 (1): x 14.0 → 9.9
Env_bridge01 (2): x -14.0 → -10.915
Env_bridge02 (1): 고정
Env_bridge02 (2): 고정
```

V3 다리는 중심선이 Z축 방향으로 배치되어 있다. 레거시 `OpenLocalPosition` 숫자를 V3에 그대로 복사하면 다리가 옆으로 이동하거나 다른 부모 좌표계 기준으로 어긋난다.

`ZoneBridgeGate` 런타임 자체는 이동 축을 가정하지 않으므로 재사용할 수 있다. 문제는 기존 `EstimateOpenPositions` 저작 도구가 X축과 레거시 이름을 가정한다는 점이다.

따라서 다음 중 하나가 필요하다.

1. V3 프리팹 모드에서 이동 조각을 손으로 중앙 연결 위치에 맞추고 `Record Bridge OPEN Positions`로 저장한다.
2. V3 전용 추정 로직을 추가해 다리 배치의 주축을 판별하거나 Z축 기준으로 제안값을 만든다.

권장안은 최초 1회는 사람이 위치를 맞추고 기록하는 것이다. 메시 길이와 피벗을 코드로 추정한 값은 최종값으로 신뢰하지 않는다.

## 6. 투명화 시스템과의 연결

V3 다리와 패널은 이미 고도 스택의 `Content` 안에 있다.

- 다리 Renderer와 Collider는 `ElevationLevel` 등록 배열에 포함되어 있다.
- 패널 Wrapper는 독립 `OcclusionSection`을 가진 투명화 대상이다.
- 다리 Transform이 움직여도 등록된 Renderer·Collider 오브젝트 참조 자체는 유지된다.
- `ZoneBridgeGate`를 추가하는 것만으로 고도 그룹 등록이 끊기지는 않는다.

다만 다리·패널 프리팹 구조나 Collider를 변경한 후에는 다음 메뉴를 다시 실행해야 한다.

```text
Tools/Rendering/Wall Occlusion/Register-Wire Selected Prefabs
Tools/Rendering/Wall Occlusion/Validate Selected Prefabs
```

검증 대상:

1. 수정한 독립 다리·패널 프리팹
2. `PF_Zone_L_Type_B_V3.prefab`

새 Collider를 런타임에만 추가하면 `ElevationLevel` 또는 `OcclusionSection`의 등록 배열에 포함되지 않는다. 필요한 Collider는 프리팹 저작 단계에서 만든 뒤 Register-Wire한다.

## 7. 새 세션에서 결정할 설계 분기

다음 결정은 아직 잠그지 않았다. 새 세션에서 하나씩 결정한다.

### 7.1 프리팹 경계

- 다리 4조각을 하나의 `PF_BridgeGate_*` 프리팹으로 묶을지
- 고정 다리와 이동 다리를 분리할지
- 패널 4개를 다리 프리팹이 소유할지, Zone이 각각 독립 Wrapper로 유지할지

추천: 다리 기믹 전체를 하나의 저작 프리팹으로 묶고, 패널은 그 프리팹의 명시적 참조 슬롯으로 등록한다. 단, 기존 V3 고도 그룹과 Prop Wrapper 소유권이 깨지지 않는지 먼저 검증한다.

### 7.2 `ZoneBridgeGate` 소유 위치

- Zone 루트에 유지
- 독립 다리 기믹 프리팹 루트로 이동

현재 `MapContentSpawner.RegisterBridgeGate`는 `zoneGo.GetComponent<ZoneBridgeGate>()`만 호출한다. 독립 자식 프리팹 루트로 옮기려면 다음 중 하나가 필요하다.

- Zone 루트에 얇은 연결 컴포넌트를 유지한다.
- `GetComponentInChildren<ZoneBridgeGate>(true)`로 탐색 계약을 변경한다.
- Zone 루트에서 자식 기믹을 명시적으로 참조한다.

추천: 모듈화하려면 다리 프리팹 루트가 `ZoneBridgeGate`를 소유하고, `MapContentSpawner`는 자식 검색을 사용하도록 변경한다. 한 Zone에 Gate가 여러 개 생길 가능성까지 고려해 단일 검색과 복수 검색 중 무엇을 지원할지 먼저 결정한다.

### 7.3 레거시 저작 도구 호환

- 기존 `ZoneBridgeGateWiring`을 V3 전용으로 변경
- 레거시 메뉴는 유지하고 V3 메뉴를 추가
- 경로·이름 규칙을 데이터화해 한 도구로 통합

추천: 레거시 메뉴를 유지하고 V3 전용 진입점을 추가하되, 공통 구현은 경로와 대상 수집 규칙을 인자로 받도록 통합한다.

### 7.4 패널 순서 규칙

- 파일명 정렬
- 하이어라키 순서
- 사분면 위치 정렬
- Inspector 수동 순서

추천: 패널 인덱스가 네트워크 복제 키이므로 사분면 규칙을 코드로 검증하고, 실제 저장 순서는 Inspector에 명시적으로 직렬화한다. 파일명의 해시는 의미가 없으므로 정렬 기준으로 사용하지 않는다.

### 7.5 다리 열림 위치 저작

- 수동 위치 기록
- 자동 추정 후 수동 보정
- 완전 자동 계산

추천: 자동 추정은 보조 기능으로만 사용하고, 최종 위치는 프리팹 모드에서 수동 확인 후 기록한다.

## 8. 권장 구현 순서

1. 현재 V3 Zone과 새로 배치된 MovingPlatform·Conveyor 프리팹 변경을 먼저 백업하거나 커밋 경계로 분리한다.
2. 다리·패널 프리팹의 소유권과 `ZoneBridgeGate` 위치를 결정한다.
3. `MapContentSpawner`의 Gate 탐색 계약을 결정한다.
4. V3 전용 저작 도구를 추가한다.
5. 패널 4개를 사분면 순서로 연결한다.
6. 다리 4조각을 Segment로 연결하고 닫힘 위치를 기록한다.
7. 이동 다리의 열림 위치를 프리팹 모드에서 맞추고 기록한다.
8. Collider 및 NavMesh carve 범위를 확인한다.
9. 다리·패널 독립 프리팹과 Zone V3를 Register-Wire한다.
10. Wall Occlusion Validate를 실행한다.
11. ZoneLayout과 ZoneLayoutCatalog를 포함한 메인 플로우 연결을 완료한다.
12. Play Mode와 MPPM Host/Client에서 상호작용·복제·다리 이동·투명화·NavMesh를 검증한다.

## 9. 완료 조건

### 프리팹 및 저작

- 패널이 정확히 4개이며 순서가 고정되어 있다.
- 다리 Segment가 의도한 4개 Transform을 가리킨다.
- 이동 다리만 열린 위치에서 중앙 통로를 완성한다.
- 기본 저장 상태는 닫힌 상태다.
- 모든 이동 다리에 Collider가 있다.
- Missing Reference와 Missing Script가 없다.

### 런타임

- Zone 생성 후 `ZoneBridgeGateManager`에 Gate가 정확히 한 번 등록된다.
- 패널별 F 상호작용 거리 판정이 정상이다.
- 호스트가 활성화한 패널 상태가 클라이언트에 동일하게 보인다.
- 다리 개통 진행도가 호스트와 클라이언트에서 일치한다.
- 다리 이동 중 플레이어와 Collider가 비정상적으로 튀지 않는다.
- 닫힘 상태에서 몬스터가 허공을 건너지 않는다.
- 열림 상태에서 NavMeshAgent가 다리를 이용할 수 있다.

### 투명화

- 패널이 SphereCast에 맞았을 때 독립 `OcclusionSection`으로 처리된다.
- 다리가 카메라와 플레이어 사이를 가릴 때 의도한 고도·가림 규칙이 적용된다.
- 다리 이동 후에도 Renderer·Collider 등록 참조가 유효하다.
- 불투명 복귀 시 머티리얼과 Collider 상태가 정상이다.
- Wall Occlusion Validate 결과가 errors=0이다.

### 네트워크

- MPPM Host + Client에서 패널 인덱스와 활성 상태가 동일하다.
- 중도 참가 또는 늦은 상태 적용 시 다리 진행도가 최종 상태로 수렴한다.
- Zone이 로컬 생성된다는 기존 계약을 깨는 `NetworkObject`를 다리·패널 프리팹에 추가하지 않는다.

## 10. 금지 및 주의 사항

- 레거시 X축 열림 좌표를 V3에 그대로 복사하지 않는다.
- 해시가 붙은 패널 Wrapper 이름을 의미 있는 순서로 간주하지 않는다.
- 패널 인덱스를 실행마다 하이어라키 열거 순서로 결정하지 않는다.
- V3 프리팹 YAML의 GUID 또는 fileID를 수동 편집하지 않는다.
- 패키지 `.meta`를 삭제하거나 재생성하지 않는다.
- 다리·패널 프리팹에 `NetworkObject`를 추가하지 않는다.
- 기존 레거시 저작 메뉴를 합의 없이 제거하지 않는다.
- 새 Collider를 만든 뒤 Register-Wire를 생략하지 않는다.
- XZ Area 50개를 자동 생성 도구로 다시 덮어쓰지 않는다.

## 11. 현재 작업 폴더 주의 사항

이 문서 작성 시점에 작업 폴더에는 사용자의 Conveyor·MovingPlatform 작업과 다른 미커밋 변경이 존재한다.

주요 관련 변경:

```text
Assets/0.Scenes/ConveyorBelt.unity
Assets/2.Prefabs/Map/ConveyorBelt/ConvayorTileStraight.prefab
Assets/2.Prefabs/Map/ConveyorBelt/ConveyorTileCorner.prefab
Assets/2.Prefabs/Map/ConveyorBelt/ConveyorTileCorner02.prefab
Assets/2.Prefabs/Map/MovingPlatform.prefab
Assets/2.Prefabs/Map/LevelDeliveryV3/
```

새 세션에서는 위 변경을 사용자 소유 작업으로 취급한다. 삭제·되돌리기·일괄 포맷·프리팹 재생성을 하지 않는다. 다리 개발 변경과 별도 커밋 경계를 유지하는 것이 안전하다.

## 12. 새 세션 시작 시 첫 질문

다리와 패널의 프리팹 경계를 먼저 결정한다.

권장 질문:

> 다리 4조각과 패널 4개를 하나의 기믹 프리팹이 소유하게 할 것인가, 아니면 패널은 기존 V3 Prop Wrapper로 유지하고 다리 기믹 프리팹이 외부 참조로 연결하게 할 것인가?

권장 답변:

> 패널의 V3 Prop Wrapper와 고도 그룹 소유권은 유지하고, 다리 기믹 프리팹 또는 Zone 루트의 `ZoneBridgeGate`가 패널 4개를 명시적으로 참조한다. 프리팹 중첩 참조가 불편하면 Zone 루트가 조립 지점이 된다.

