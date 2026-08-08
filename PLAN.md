# CURRENT PLAN — 선택형 화면 공간 캡슐 오클루전 재설계 (2026-08-06)

> 상태: **구현 및 자동 검증 완료 / 사용자 프리팹 적용 후 Play 검증 대기**
> 범위: 런타임 시스템, 선택 프리팹 등록·검증 도구, 자동 테스트, 문서 2개 및 기존 기술 문서 갱신.
> 제외: 기존 Stage1·복도·Zone·프랍 아트 프리팹의 하이어라키 변환. 사용자가 새 구조로 변환한 뒤 실제 맵 Play 검증을 별도로 수행한다.

## 목표

현재처럼 재질이 매핑된 Stage1·Zone 전체를 대상으로 카메라–플레이어 선분 주변을 일괄 디더 처리하지 않는다.
로컬 카메라에서 플레이어의 기본 충돌 캡슐을 향해 실제 물리 가림 후보를 선택하고, 명시적으로 등록된 구조물 중
실제로 가리는 섹션만 플레이어의 **화면 공간 캡슐 실루엣 주변**에서 디더 컷아웃한다.

## 잠긴 설계 결정

### 1. 로컬 가림 선택

- 각 클라이언트가 자기 게임플레이 카메라와 현재 카메라 추적 대상을 기준으로 독립 계산한다. 네트워크 동기화하지 않는다.
- `LateUpdate`마다 `Physics.SphereCastNonAlloc`을 1회 실행한다. 재사용 버퍼를 사용해 프레임별 GC를 만들지 않는다.
- 캐스트 대상은 카메라 추적 대상의 **기본 non-trigger CapsuleCollider** 중심이다. 무기·망토·VFX·애니메이션 Renderer bounds는 사용하지 않는다.
- 캐스트 반경은 카메라 방향에 투영된 기본 캡슐 실루엣과 전역 패딩으로 동적 계산한다.
- 카메라와 캡슐 사이에서 맞은 등록 대상은 가장 가까운 하나가 아니라 모두 처리한다. 트리거, 플레이어 자신, 구간 뒤쪽, 미등록 대상은 제외한다.
- `Collider.ClosestPoint`와 기존 전 맵 렌더러/재질 스캔은 사용하지 않는다.
- 추락 중 캐릭터와 Soul도 실제 기본 캡슐을 사용한다. 관전 시 현재 관전 대상을 사용하며, 유효 대상이 없으면 판정을 멈추고 활성 대상을 복원한다.

### 2. 투명화 표현

- 표현 방식은 URP 불투명 큐의 **디더 컷아웃**을 유지한다. 일반 알파 블렌딩으로 바꾸지 않는다.
- 기존 월드 공간 카메라–플레이어 원통 마스크를 제거한다.
- 기본 캡슐의 위·아래와 반지름을 카메라 화면에 투영한 화면 공간 캡슐 마스크를 사용한다.
- 캡슐 중심부는 완전히 비우고, 외곽 링에서만 디더 그라데이션을 적용한다.
- 색상, DepthOnly, DepthNormals, ShadowCaster가 동일한 카메라 화면 마스크를 사용한다. 잘려 보이지 않는 부분은 깊이·SSAO·그림자도 함께 제거한다.
- 구멍 크기·외곽 폭은 전역 설정 하나를 사용한다. 섹션별 크기 오버라이드는 이번 범위에 넣지 않는다.
- 같은 섹션에서 한 콜라이더가 맞으면 그 섹션의 등록 렌더러 전체를 활성화하되 실제 픽셀 제거는 화면 공간 캡슐 주변에만 일어난다.
- 선택 진입 약 0.1초, 해제 유예 약 0.1초, 불투명 복원 약 0.2초의 부드러운 전환을 유지한다.
- 전환 중인 소수 렌더러에만 MPB 전환값을 사용하고, 안정 상태에서는 공유 원본/디더 변형 재질과 빈 MPB로 복귀시켜 SRP Batcher 손실을 제한한다.
- 노멀과 플레이어 높이로 바닥을 추측하던 `Floor Guard`와 관련 설정·테스트·문서는 제거한다. 같은 그룹에서 플레이어 뒤쪽 픽셀을 보호하는 깊이 제한은 유지한다.

### 3. 명시적 런타임 등록

- 런타임은 이름이나 재질명을 판정 근거로 사용하지 않는다. 컴포넌트에 직렬화된 Renderer·Collider·역할 참조만 사용한다.
- `OcclusionSection`은 평면 복도 한 인스턴스, 연속 외벽 한 구간, 또는 등록된 프랍 한 개의 기본 소유 단위다.
- `ElevationStack`은 같은 위치에 수직으로 쌓인 여러 `ElevationLevel`의 현재 층 상태를 독립적으로 관리한다. 별도 런타임 층 번호 필드는 두지 않고 Level 루트의 월드 Y로 정렬한다.
- 컴포넌트는 활성화/비활성화 시 레지스트리에 스스로 등록·해제한다. 맵 전체를 매 프레임 탐색하지 않는다.
- 하나의 Renderer 또는 Collider는 `OcclusionSection` 소유자를 최대 하나만 갖는다. 중첩 섹션의 중복 소유는 검증 오류다.
- `OcclusionSection`은 `Occlusion` 아래 어느 깊이에나 둘 수 있다. 가장 가까운 자기 컴포넌트가 하위를 소유하며 다른 `OcclusionSection` 경계에서 부모 자동 수집을 멈춘다.
- Level의 `Content` 소속과 `OcclusionSection` 소유는 의도적으로 함께 가질 수 있다. Level 소속은 상위 층 전체 처리를, Section 소유는 현재 층 개별 처리를 담당하며 최종 활성 조건은 OR로 결합한다.
- 하나의 대상이 둘 이상의 `ElevationLevel/Content`에 소속되는 것은 허용하지 않는다.
- 역할이 다른 벽·바닥·난간·프랍을 하나의 메시, Renderer 또는 Collider에 합치는 것은 허용하지 않는다.
- 잘못 등록된 대상은 불투명 상태로 제외하고 원인 경고를 한 번만 출력한다. 정상 섹션은 계속 동작한다.
- 각 `ElevationLevel/Content` 전체에는 상위 층 그룹 hit 판정용 Collider가 최소 하나 필요하고, 각 `OcclusionSection`에는 현재 층 개별 hit 판정용 Collider가 최소 하나 필요하다. 개별 Renderer나 `LevelOnlyProps` 프랍별 Collider는 강제하지 않는다.
- 구형 재질 기반 자동 바인딩 호환 모드는 제거한다. 새 컴포넌트가 없는 레거시 프리팹은 불투명하게 유지한다.

### 4. Stage·Zone 공통 하이어라키

#### 4-A. 공통 필수 구조

```text
PF_Stage_01 또는 PF_Zone_*
├─ (기존 아트·게임플레이 하이어라키: 자유, 항상 불투명)
└─ Occlusion                                      ← 유일한 공통 투명화 작업 범위
   └─ ElevationStack_01 [ElevationStack]
      ├─ Level_B01 [ElevationLevel: 루트 Y + XZ Areas]
      │  └─ Content
      │     ├─ FloorMesh
      │     ├─ WallSection_01 [OcclusionSection]
      │     ├─ OccludableProps                    ← 비어 있어도 필수
      │     │  └─ PF_Prop_Container_A [OcclusionSection]
      │     └─ LevelOnlyProps                     ← 비어 있어도 필수
      ├─ Level_L01 [ElevationLevel: 루트 Y + XZ Areas]
      │  └─ Content
      │     ├─ FloorMesh
      │     ├─ HallwaySection_01 [OcclusionSection]
      │     ├─ OccludableProps
      │     └─ LevelOnlyProps
      └─ Level_L02 [ElevationLevel: 루트 Y + XZ Areas]
         └─ Content
            ├─ FloorMesh
            ├─ WallSection_01 [OcclusionSection]
            ├─ OccludableProps
            │  └─ PF_Prop_Machine_A [OcclusionSection]
            └─ LevelOnlyProps
               ├─ PF_Prop_Railing_A
               └─ PF_Prop_Box_Small_A
```

- `Occlusion` 아래에는 `ElevationStack`만 허용한다. 독립 `OcclusionSection`을 직접 두는 것은 검증 오류다.
- 단층 Stage·Zone도 `ElevationStack` 하나와 `ElevationLevel` 하나를 둔다. 모든 투명화 대상은 반드시 하나의 `Level/Content`에 속한다.
- `Occlusion`, 각 Level의 `Content`, `OccludableProps`, `LevelOnlyProps`는 비어 있어도 항상 존재하는 필수 역할 노드다.
- 바닥과 벽을 위한 별도 고정 컨테이너는 추가하지 않는다. 바닥은 `Content` 바로 아래에 두고, 현재 층에서도 개별 처리할 벽·복도는 `OcclusionSection`으로 표시한다.
- `OccludableProps`와 `LevelOnlyProps` 컨테이너 자체에는 컴포넌트를 붙이지 않는다. `OccludableProps` 아래 각 프랍 프리팹 루트가 자기 `OcclusionSection`을 소유한다.
- 두 프랍 컨테이너 아래에는 프랍 프리팹 루트만 직속 자식으로 배치한다. 중간 분류 노드는 허용하지 않으며 각 프랍 내부의 메시·Collider 하이어라키는 자유다.
- `OccludableProps` 직속 `PF_Prop_*` 루트에는 `OcclusionSection`이 정확히 하나 있어야 한다. `LevelOnlyProps` 직속 프랍의 자신과 하위에는 `OcclusionSection`이 없어야 하며, 컴포넌트를 프랍 루트가 아닌 자식 메시 등에 붙이는 것도 검증 오류다.
- `OcclusionSection` 아래에 `Occluders`·`NeverOccluders`를 두지 않는다. 컴포넌트가 자기 하위를 직접 소유한다.
- `LevelOnlyProps`의 프랍은 현재 층에서 개별 처리하지 않지만, 그 Level이 플레이어보다 위에 있으면 다른 `Content`와 함께 처리된다.
- `OccludableProps`의 프랍과 Level 안의 벽·복도 섹션은 상위 층 전체 처리에도 포함되고, 현재 층에서는 자기 SphereCast hit에 따라 독립 처리된다.

#### 4-B. Stage·Zone 소유 예시

- Stage와 Zone은 완전히 같은 등록 규칙을 사용한다. 런타임과 등록 도구는 프리팹 종류를 구분하지 않는다.
- Stage1 외벽·공용 복도도 Stage의 알맞은 `ElevationLevel/Content` 안에 넣는다.
- Stage의 특정 층에 속한 바닥·벽·프랍은 Stage의 해당 `ElevationLevel/Content`가 소유한다.
- Zone의 특정 층에 속한 바닥·벽·프랍은 Zone의 해당 `ElevationLevel/Content`가 소유한다.
- 외벽은 코너·복도 접합부를 경계로 한 연속 구간별 `OcclusionSection`으로 나누고, 복도 프리팹 인스턴스는 자기 섹션을 소유한다.
- 투명화 시스템은 `Slots`, `Runtime`, `StaticGeometry`, `Hallways`, `OuterWalls` 같은 외부 이름을 예외 규칙으로 알지 않는다. `Occlusion` 밖은 이름과 관계없이 수집하지 않는다.

### 5. 최소 고도 스택 규칙

고도 시스템의 런타임 규칙은 아래 여섯 단계만 둔다.

1. 기존 `PlayerGroundingSensor`에서 접지 여부와 상승·하강 상태를 읽고 기본 CapsuleCollider의 발 Y를 사용한다. 별도 지면 캐스트나 GroundCollider→Level 매핑은 만들지 않는다.
2. 각 `ElevationLevel`은 루트 Transform Y 하나와 컴포넌트 내부의 데이터 전용 로컬 `XZ Areas` 목록만 가진다. 물리 `BoxCollider`, `ActivationVolumes`, 별도 층 번호 데이터는 두지 않는다.
3. 플레이어가 들어 있는 XZ Area를 가진 Level들을 Y로 정렬해 Stack의 현재 층을 정한다. 상태가 없을 때는 인접 높이 구간의 상승 20% 기준으로 초기화한다.
4. 접지 상승은 다음 높은 Level까지 20%에서 전환하고, 공중 상승은 점프·넉백으로 보고 현재 층을 유지한다. 진입로 및 공중 낙하는 다음 낮은 Level까지 60%에서 전환한다. 한 프레임에 여러 경계를 넘으면 연속 평가한다.
5. 현재 층보다 높은 Level만 상위 층 전체 처리 후보로 허용한다. 상위 Level의 `Content` 안 Collider 중 하나라도 카메라→기본 캡슐 SphereCast에 맞으면 그 `Content`의 모든 Renderer를 함께 활성화한다. 아무 Collider도 맞지 않으면 활성화하지 않는다.
6. 현재 층과 아래층의 Level 전체 처리는 끈다. 다만 `Content` 안의 벽·복도·`OccludableProps` 섹션은 자기 Collider가 실제로 맞으면 독립 활성화한다. 최종 조건은 `LevelAboveAndGroupHit || SectionHit`이며 실제 픽셀 제거는 화면 공간 캡슐 주변에서만 일어난다.

- `StableSurfaces`, `TransitionSurfaces`, `AlwaysOccluders`, `NeverOccluders`, `LowerHeight`, `UpperHeight`, `HeightMarker`와 층 간 연결 데이터는 사용하지 않는다.
- Stack과 Level 루트는 위치 이동과 Y축 회전만 허용하고 스케일은 `(1,1,1)`로 고정한다. X/Z 기울기 회전은 검증 오류다.
- XZ Area는 `ElevationLevel` 인스펙터 목록에서 Center·Size·Y Rotation으로 만들고 Scene 핸들로 편집한다. 제작 방법을 프리팹 매뉴얼에 명시한다.
- Stack의 모든 XZ Area 밖이면 활성 층이 없으므로 Level `Content`는 SphereCast hit 시 전체 처리할 수 있다. 겹친 Stack은 각각 독립 판정한다.
- 하나의 Stack 안에서 사실상 같은 기준 Y를 가진 Level은 허용하지 않는다. 같은 높이의 분리 구간은 한 Level의 여러 XZ Area로 합치고, 독립 판정이 필요하면 Stack을 나눈다. 중복 높이는 검증 오류다.
- 한 Stack의 여러 XZ Area는 하나의 이어진 고도 구조의 형태를 표현할 때만 사용한다. 서로 멀리 떨어진 건물·발판·독립 구조는 각각 별도 Stack으로 나누며, 하나의 Level Content Collider hit가 관계없는 구조 전체를 활성화하지 않게 한다.
- 계단·경사로·진입 다리는 별도 역할이나 컴포넌트를 만들지 않고 도착하는 높은 층의 `Content`에 넣는다. 상승 20%·하강 60% 전환으로 현재 층 보호와 상위 층 전체 처리를 전환한다.

### 6. 선택 프리팹 등록·검증 도구

- 선택한 프리팹만 대상으로 `Register/Wire Selected`와 `Validate Selected`를 제공한다.
- 고정 `Occlusion`, `Content`, `OccludableProps`, `LevelOnlyProps` 역할 노드와 등록 컴포넌트를 읽어 Renderer·Collider 참조와 Level 소속을 직렬화한다. XZ Area는 `ElevationLevel` 컴포넌트 내부 목록을 편집한다.
- 하이어라키를 임의로 이동하거나 재구성하지 않으며, 중첩 프리팹 원본을 몰래 수정하지 않는다.
- 지원되는 일반 재질은 선택 프리팹에서 실제 사용한 것만 공유 디더 변형을 결정적 경로와 이름으로 생성·재사용한다. 원본 아트 재질은 수정하지 않는다.
- 에미션·특수 Shader Graph 등 지원하지 않는 재질은 외형을 추측하지 않고 아트가 제공한 변형을 요구한다.
- 이름, 필수 역할 노드, 프랍 직속 자식 규칙, 중첩 `PF_Prop_*`, 프랍 분류와 Section 컴포넌트 불일치, `Occlusion` 직속 섹션, Level 미소속 대상, 역할 혼합, 콜라이더/렌더러 누락, XZ Area, Level Y 정렬·중복 높이, 루트 transform, 중복 Section 소유, 다중 Level 소속, 미지원 재질을 검사한다.
- Content 또는 Section의 Collider 최소치가 충족되지 않으면 선택 프리팹 검증에서 프리팹·오브젝트 경로·수정 방법을 오류 로그로 출력하되 빌드는 막지 않는다. 런타임에서는 해당 대상만 불투명 상태로 제외하고 원인 경고를 한 번만 출력한다.
- 자동으로 구조의 공간적 독립성을 단정하지 않는다. 검증 결과에 Stack·Level별 Renderer·Collider·XZ Area 수를 표시해 잘못 크게 묶인 그룹을 사람이 확인할 수 있게 한다.
- 검증 오류는 빌드를 막지 않는다. 프리팹 경로·오브젝트·수정 방법과 전체 오류 수를 로그로 알린다.
- 등록 컴포넌트가 없는 레거시 프리팹은 프로젝트 전체 오류로 취급하지 않는다.
- Scene/Play 기즈모로 Level 루트 높이, XZ Area, 상승 20%, 하강 60%, 캐스트 경로, hit 섹션과 현재 층을 표시한다. 일반 빌드에서는 기본 비활성화한다.

### 7. 작명 규칙

- 새로 만들거나 이번 구조로 전환한 프리팹부터 엄격 적용한다. 기존 레거시 파일은 이름 변경하지 않는다.
- 역할 노드는 영문 ASCII 고정 이름이며 대소문자까지 일치해야 한다. 한글, 공백, `(1)` 형태 자동 복제 접미사를 금지한다.
- 새 프리팹 파일은 종류별 접두사와 `_` 구분을 사용한다.
  - `PF_Zone_L_Combat_A`
  - `PF_Hallway_Straight_A`
  - `PF_Prop_Machine_Generator_A`
  - `PF_Wall_Outer_Straight_A`
- 프랍 등록 단위는 `OccludableProps` 또는 `LevelOnlyProps`의 직속 자식이면서 이름이 `PF_Prop_*` 규칙을 만족해야 한다. `PF_Prop_*` 안에 다른 `PF_Prop_*`을 중첩하면 검증 오류다. Stage·Zone 프리팹이 프랍 프리팹을 소유하는 정상 중첩은 허용한다.
- 메시·콜라이더·섹션·고도 스택·Level의 세부 접두사, 필수/선택 노드, 정규식, 올바른/잘못된 예시를 별도 문서에 고정한다.

## 구현 순서

1. 기존 오클루전 테스트를 새 계약 기준으로 갱신하고 등록/선택/상태 전이 순수 로직 테스트를 먼저 추가한다.
2. `OcclusionSection`, `ElevationStack`, `ElevationLevel`, 컴포넌트 내부 XZ Area 목록과 런타임 레지스트리를 구현한다.
3. `WallOcclusionDriver`를 로컬 카메라·기본 캡슐·매 프레임 NonAlloc SphereCast 기반으로 교체한다.
4. 화면 공간 캡슐 마스크와 전환 강도를 셰이더의 Forward/Depth/Shadow 패스에 동일 적용한다. Floor Guard를 제거한다.
5. 공유 재질 변형 캐시와 전환 중에만 MPB를 사용하는 상태 머신을 구현한다.
6. 선택 프리팹 등록·검증 메뉴와 Scene/Play 기즈모를 구현한다.
7. 아래 두 신규 문서를 작성하고 기존 `Docs/tech/wall-occlusion-implementation.md`를 새 구조로 갱신한다.
   - `Docs/tech/occlusion-prefab-authoring-manual.md` — 아트 작업자용 하이어라키·XZ Area·프랍 분류·검증 메뉴얼
   - `Docs/tech/occlusion-prefab-naming-rules.md`
8. 작명 규칙 문서에 AI용 프리팹 변환 작업 지시 템플릿과 완료 보고 형식을 포함한다.

## 검증

### 이번 구현에서 수행

- EditMode: 모든 SphereCast hit 수집·중복 제거·미등록 제외·잘못된 대상 fail-opaque·경고 1회.
- EditMode: 상위 Level의 Content Collider 하나 hit 시 Content 전체 활성, 현재 층 Section 개별 활성, `LevelAboveAndGroupHit || SectionHit`, LevelOnlyProps 현재 층 보호, 중첩 섹션 소유권, 겹친 Stack 독립 판정, Level Y 자동 정렬.
- EditMode: 기존 접지 센서 재사용, 초기 20%, 접지 상승 20%, 공중 상승 무시, 진입로/낙하 하강 60%, XZ 이탈, 다층 연속 통과.
- EditMode: 소스/디더 재질 전환, 해제 유예와 복원, 비활성화/씬 해제 시 원상 복구.
- Editor: 선택 프리팹 자동 등록의 멱등성, 필수 Content/프랍 노드, 모든 대상의 Level 소속, 금지된 Occlusion 직속 Section, 중복/누락/미지원 재질 검증.
- Shader: Forward/DepthOnly/DepthNormals/ShadowCaster 컴파일과 Floor Guard 잔존 참조 0건.
- C# 컴파일 및 기존 관련 EditMode 테스트 회귀.

### 사용자 프리팹 반영 후 후속 검증

- Stage1 복도와 연속 외벽의 Level 소속, 1층/2층 `OccludableProps`와 `LevelOnlyProps` 분류.
- 1층에서 2층 Content가 실제로 가릴 때 바닥·벽·두 프랍 그룹이 함께 화면 공간 캡슐 주변에서 처리되어 떠 보이는 오브젝트가 없는지 확인.
- 2층에서는 벽·OccludableProps만 개별 처리되고 LevelOnlyProps는 불투명하게 유지되는지 확인.
- 진입로 상승 20%, 하강 60%, 2층에서 직접 낙하, 점프·넉백 오인 방지.
- 여러 겹 벽 전부 처리, 카메라 전환, Soul, 추락, 관전.
- MPPM host+client에서 각 카메라별 독립 결과 및 콘솔 오류 없음.

## 리스크와 대응

- **아트 전환 전에는 투명화가 없음:** 구형 호환 모드를 의도적으로 제거한다. 사용자 프리팹 등록 후 실제 맵 검증을 진행한다.
- **대형 Level Content 비용:** Stage1 전체를 Level 하나로 뭉치지 않고 실제 고도와 지역 소유 범위로 Stack/Level을 나눈다. Level별 Renderer·Collider 수를 검증 로그에 표시하고 상태 변경은 hit/전환 대상에만 제한한다.
- **SphereCast 버퍼 포화:** 고정 재사용 버퍼 포화를 감지해 한 번 경고하고 설정 가능한 상한을 둔다. 누락 여부를 테스트/기즈모로 확인한다.
- **특수 재질 외형 손실:** 자동 추측을 금지하고 지원 재질만 생성하며 나머지는 명시 변형을 요구한다.
- **카메라/그림자 좌표 불일치:** 게임플레이 카메라의 화면 변환 데이터를 모든 셰이더 패스에 명시적으로 전달하고 테스트한다.
- **사용자 작업 보호:** 기존 아트 프리팹과 현재 워크트리의 관련 없는 변경은 건드리지 않는다.

---

# CURRENT PLAN — 지연 체력바(잔상 바) — 플레이어/보스 HUD (2026-08-05)

> 상태: **승인 대기**. 브랜치 `feature/DelayedHealthBar` (base `Convayor-V2`), 레인 `dash`.
> 구현 위임: Codex(코드만) / 프리팹 배선: 은희(Unity 에디터).
> grill 완료 — 아래는 확정된 결정만 담는다.

## 목표

피격 시 **앞쪽 HP 바는 즉시 줄고, 뒤쪽 잔상 바가 옛 HP에 잠시 머문 뒤 따라 내려온다**.
잃은 양을 눈으로 읽히게 하는 순수 로컬 표현(길티기어/LoL 방식).

## 스코프

- **In**: 로컬 플레이어 HUD(`PlayerHealthHUD`), 보스 HUD(`BossHealthHUD`).
- **Out**: 원격 플레이어 머리 위 바(`UnitOverheadHealthBar`) — 월드스페이스 소형 바라 효과 낮고
  `Player.prefab` 수정이 필요. 로직은 재사용 가능한 형태로 두어 나중에 붙일 수 있게 한다.
- **Out**: 실드 바 — 실드는 상한(MaxShield) 개념이 없어(현재도 MaxHp 대비로 그림) 잔상 의미가 모호.
- **Out**: 지연 *피해* 게이지(예약 피해 후 적용)류 게임플레이 변경 — UI 작업이 아님.

## 알고리즘 (확정)

```
state: Queue<int> held      // 아직 잔상으로 붙잡고 있는 피해 조각
       float displayed      // 잔상 바가 그리는 값 (비율 아님, HP 절대값)
       float holdTimer

이벤트 Unit.ClientHpChanged(prev, next):
  if next < prev:
      held.Enqueue(prev - next)
      holdTimer 시작  // resetHoldOnDamage=true면 매번 갱신, false면 held가 비어있었을 때만
      if held.Count > maxHeldHits: held.Dequeue()   // 가장 먼저 들어온 조각부터 놓아줌
  else:
      held.Clear(); displayed = next                // 회복 → 즉시 스냅
  if next <= 0: held.Clear(); displayed = 0         // 사망 → 0 스냅

매 프레임(Tick):
  if holdTimer 만료: held.Clear()
  target    = hp + held.Sum()
  displayed = MoveTowards(displayed, target, maxHp * drainRatePerSecond * dt)
  delayedFill.fillAmount = maxHp > 0 ? clamp01(displayed / maxHp) : 0
```

### 파라미터 (전부 `SerializeField`, 인스펙터 튜닝)

| 필드 | 기본값 | 의미 |
|---|---|---|
| `holdSeconds` | 0.4 | 피격 후 잔상이 멈춰 있는 시간 |
| `drainRatePerSecond` | 0.8 | 초당 감소량 = 최대HP의 80% (**고정 속도** → 큰 피해는 오래, 작은 피해는 짧게 흐름) |
| `maxHeldHits` | 5 | 홀드로 붙잡을 최대 피격 횟수. 초과하면 가장 오래된 조각을 pop |
| `resetHoldOnDamage` | 플레이어 `true` / 보스 `false` | 피격마다 홀드 타이머를 갱신할지 |

**왜 큐인가**: 보스 HUD는 3인이 거의 매 프레임 때리고, 플레이어도 Vent 장판/도트를 맞는다.
"피격마다 홀드 리셋"만 있으면 리셋이 무한 갱신돼 **잔상이 전투 시작 HP에 영구 고착**한다.
큐 상한(`maxHeldHits`)이 그 고착을 끊고, 보스는 리셋 자체를 끈다.

**왜 고정 속도인가**: 고정 시간은 피해 크기 정보를 지우고, `Lerp` 감쇠는 끝이 안 닿아 잔상이
미세하게 남고 프레임레이트에 의존한다.

**pop = 즉시 차감이 아니다** — `target`을 낮출 뿐이고 실제 감소는 위 고정 속도로 흐른다.

### 즉시 리셋(스냅) 케이스 — 이 4개만

1. 대상 교체 (`Bind` 재호출 / 보스 참조 변경)
2. 회복 (HP 증가)
3. 사망 (HP 0)
4. `PlayerHealthHUD.SetDisplayOverrideZero(true)` (Soul 표현 — HUD를 0으로 덮을 때 잔상도 0)

`FinalMaxHp` 변동은 리셋 불필요 — `displayed`를 HP 절대값으로 보관하므로 비율이 자동으로 맞는다.

## 피해 감지 — 기존 복제 이벤트 확장

[Unit.cs:474](Assets/1.Scripts/Unit/Unit.cs:474)의 `OnHpReplicated(prev, next)`가 이미 있고
`next < prev`만 걸러 `ClientDamaged`(파라미터 없음)를 쏜다. 여기에 **증감 전부를 흘리는 이벤트를
2줄 추가**한다. 데미지 파이프라인·서버 권한 경로는 건드리지 않는다.

```csharp
public event System.Action<int, int> ClientHpChanged;   // (previous, next)

void OnHpReplicated(int previous, int next)
{
    ClientHpChanged?.Invoke(previous, next);            // 추가
    if (next < previous) ClientDamaged?.Invoke();       // 기존 유지 (HitFlash 구독)
}
```

- `NetworkVariable` 복제 기반이라 RPC 불필요, 모든 피어에서 발동.
- 회복도 쏘는 이유: 회복 시 잔상 스냅 신호가 필요하고, 소스가 이벤트 하나로 단일화된다.
- 기존 `ClientDamaged`와 `HitFlash`는 **무수정**(실드 감소도 쏘므로 HP 델타 의미가 오염됨 → 통합 안 함).
- 한계: 리모트 클라는 네트워크 틱에서 여러 피격이 합산될 수 있어 큐 카운트가 근사치다.
  호스트에서는 1건당 1회 보장. 잔상 연출에는 무해.

## 구독 수명주기

| 지점 | 처리 |
|---|---|
| `PlayerHealthHUD.Bind(player)` | 이전 대상 구독 해제 → 새 대상 구독, `displayed = CurrentHealth` 스냅. `OnDisable`에서 해제 |
| `BossHealthHUD` | 매 프레임 `FindBoss()` 결과가 이전 참조와 다르면 구독 교체 + 잔상 리셋 (보스1→보스2 잔상 누출 방지) |
| 늦은 바인딩 | 구독 직후 현재 HP로 스냅. 놓친 구간은 잔상 없이 시작 |

## 변경 파일 (정확히 4개)

| 파일 | 변경 |
|---|---|
| `Assets/1.Scripts/UI/Combat/DelayedHealthBar.cs` | **신규** — `[Serializable]` 순수 C# 클래스. `Image delayedFill` + 파라미터 + `Queue<int>`. API: `Bind(int hp)` / `OnHpChanged(int prev, int next)` / `Tick(float dt, int hp, int maxHp)` |
| `Assets/1.Scripts/Unit/Unit.cs` | `ClientHpChanged` 이벤트 + `Invoke` (2줄) |
| `Assets/1.Scripts/UI/Combat/PlayerHealthHUD.cs` | `[SerializeField] DelayedHealthBar delayed;` + 구독/Tick + `SetDisplayOverrideZero` 연동 |
| `Assets/1.Scripts/UI/Combat/BossHealthHUD.cs` | 동일 + 대상 교체 감지 |

기존 `hpFill` / `hpText` / 실드 / `displayOverrideZero`의 **매 프레임 폴링은 그대로 유지**한다
(이벤트로 옮기면 리팩터 범위가 커진다). 잔상만 이벤트 기반.

## 프리팹 배선 (사용자 몫 — Codex 범위 밖)

Codex는 `.cs` 4개만 건드리고 **프리팹/씬/`.meta`는 수정하지 않는다**. 이후 은희가 Unity에서:

1. [CombatHUD.prefab](Assets/2.Prefabs/UI/CombatHUD.prefab) — HP 바에 Image 추가, `hpFill`과 동일한
   RectTransform/`fillMethod`, **형제 순서를 `hpFill`보다 앞**(= 뒤에 그려짐) → `delayed.delayedFill`에 연결
2. [BossHealthHUD.prefab](Assets/2.Prefabs/UI/BossHealthHUD.prefab) — 동일
3. 색/투명도는 프리팹에서 결정(코드는 `fillAmount`만 건드림), `resetHoldOnDamage`는 보스만 해제

## 완료 조건

1. 변경 파일 정확히 4개. **프리팹/씬/`.meta` 무수정**
2. 신규 `.cs`는 **UTF-8(BOM)** — 레포 인코딩 규칙
3. 기존 폴링 동작(HP 텍스트·앞바·실드·`displayOverrideZero`) 회귀 없음
4. 단일 커밋 + `work_completed`에 커밋 해시 + 인스펙터 배선 안내
5. (사용자) Unity 컴파일 0 에러 → 프리팹 배선 → MapScene Play에서 피격 시 잔상 확인

## 리스크

- 컴파일 검증을 Codex가 못 한다(Unity 미실행) → 에러 시 `/CoopAgent_Reload` 후 Claude가 수정.
- 리모트 클라의 큐 카운트는 네트워크 틱 합산으로 근사(위 참조).
- `Unit.cs`는 코어(은희 담당 영역)지만 이벤트 추가만이라 기존 경로 무영향.

---

# CURRENT PLAN — 개발 진입점 단일화 + 맵 단독 Play 부팅 (2026-08-03)

> 상태: **승인 대기**. 구현 착수 전.
> 요청자: 은희 (Network 관할). 담당 코드 중 `GameManager.cs`는 팀장(경석) 담당 영역 —
> 부팅 흐름을 건드리므로 머지 전 합의 필요.

## 목표

1. **맵을 단독으로 Play해도 게임이 돈다.** `ForProfile`의 `Start Host` GUI 버튼으로 호스트를 띄우면
   `NetworkClock`과 `GameManager`가 정상 구동해야 한다. 현재는 셋 다 성립하지 않는다.
2. **로비 진입점을 하나로 줄인다.** 개발 기간 동안 로비가 둘(`3.LobbyScene`, `3.BeaverLobby`)이라
   어디가 진짜인지 매번 헷갈린다. Steamworks 로비가 들어오면 어차피 교체될 영역이므로
   지금은 "하나만 남긴다"까지만 한다.

## 현재 이해 (조사 완료)

| 사실 | 근거 |
|---|---|
| `4.MapScene`에 `NetworkManager`·`ForProfile`이 **없다** | 씬 GUID 스캔. `ForProfile`은 `PlayerBossTest`/`PlayerDashTest`에만 존재 |
| `4.MapScene`의 GameManager 참조 1건은 인스턴스가 아니라 **버튼 OnClick이 프리팹 에셋을 직접 가리키는 것** | `4.MapScene.unity:2057` |
| 매니저는 `0.BootStrapScene`에만 있다 | GameManager.prefab(15참조) + NetworkManager.prefab(13참조) |
| `NetworkClock`·`NetworkSessionLauncher`·`NetworkLoadingFlowController`는 `NetworkManager.prefab`의 컴포넌트 | 프리팹 GUID 스캔 |
| **강제 이동의 정체는 BootStrap이 아니라 `GameManager.Start()`** | `GameManager.cs:59` — 조건 없이 `LoadScene(titleSceneName)` |
| 그 결과 `CurrentState`가 `Title`로 남아 `NotifyMainGameReady()`도 안 나간다 | `ForProfile.cs:86`이 `CurrentState == MainGame`을 요구 |
| 정식 MapScene 로딩은 `NetworkManager.SceneManager.LoadScene(..., Additive)` → `SetActiveScene` | `NetworkLoadingFlowController.cs:497,979` |
| `3.LobbyScene`은 빌드 목록에 **없다**. MPPM PlayMode 설정 2개가 초기 씬으로 참조 | `EditorBuildSettings`, `LobbySceneTest.asset:25`, `PlayerTest.asset:25` |
| `3.LobbyScene`은 팀원 IP 하드코딩 버튼 5개(`172.33.1.x`), `3.BeaverLobby`는 IP/Port 입력 필드 | 씬 GameObject diff, 두 매니저 클래스 |

## 접근

### A. 개발용 부팅 씬을 분리한다 (목표 1)

새 씬 **`Assets/0.Scenes/Dev/Dev_MapScene.unity`** 를 만든다. 구성:

- `NetworkManager.prefab` 인스턴스 (→ `NetworkClock`·`NetworkSessionLauncher`·`NetworkLoadingFlowController` 동반)
- `GameManager.prefab` 인스턴스
- `ForProfile`을 든 GameObject 하나 (`Start Host` OnGUI 버튼)
- `DevSceneBooter`(신규, 소형): `Start()`에서 `SceneManager.LoadScene("4.MapScene", Additive)` 후 `SetActiveScene`

**`4.MapScene`은 수정하지 않는다.** 이게 "씬 파일 분리"의 핵심 이득이다:

- 정식 흐름(`BootStrap → … → MapScene`)에서 `NetworkManager`가 중복되지 않는다.
  NGO `NetworkManager`는 싱글톤이라 중복 인스턴스가 세션을 깨뜨릴 수 있다.
- `4.MapScene.unity`를 건드리지 않으므로 맵 저작과 **씬 머지 충돌이 발생하지 않는다**.
- Dev 씬은 `EditorBuildSettings`에 넣지 않는다 → 빌드 산출물에 영향 0.
  (`4.MapScene`은 이미 목록에 있으므로 에디터 Play에서 이름으로 additive 로드가 된다.)

정식 흐름과 동일하게 additive + `SetActiveScene` 형태를 유지해, Dev 경로에서만 성립하는
씬 구성 차이를 최소화한다.

### B. `GameManager`를 부팅 씬 인식형으로 바꾼다 (목표 1의 실제 차단 지점)

`GameManager.cs`:

1. 직렬화 필드 `bootstrapSceneName = "0.BootStrapScene"` 추가.
2. `Start()`에서 활성 씬이 `bootstrapSceneName`일 때만 `LoadScene(titleSceneName)`.
   그 외(Dev 씬 등)에서는 **자동 진행을 생략**한다.
3. 자동 진행을 생략한 경우, 이미 로드된 씬을 1회 스캔해 `mainGameSceneName`이 있으면
   `SetState(GameState.MainGame)`.
   - additive 로드는 `HandleSceneLoaded`가 잡지만, GameManager가 먼저 깨어 있으면
     그 경로로도 들어온다. Start 스캔은 순서가 뒤집힐 때의 보험.

이 변경으로 `CurrentState == MainGame`이 성립하고, `ForProfile.HandleServerStarted`의
`NotifyMainGameReady()`가 나간다 → `MapSceneManager`의 인게임 BGM 등 `OnMainGameReady`
구독자가 정상 동작한다.

`NetworkClock`은 **기존 코드로 이미 해결된다** — `ForProfile.HandleServerStarted`가
`HasMainGameStarted`가 false면 `MarkMainGameStart()`를 호출한다(`ForProfile.cs:70-74`).
새로 만들 것 없음.

> ⚠️ `GameManager.cs`는 팀장 담당 영역(부팅 씬)이다. 변경은 기본값 유지·기존 경로 무영향
> 이 되도록 설계했으나, PR 전에 팀장 확인을 받는다.

### C. 로비를 하나로 줄인다 (목표 2)

`3.BeaverLobby`를 남기고 구 로비를 제거한다. 이름은 그대로 둔다 —
Steamworks 로비로 교체될 영역에 rename 비용을 쓰지 않는다.

**삭제**
- `Assets/0.Scenes/MainFlow/3.LobbyScene.unity` (+`.meta`)
- `Assets/1.Scripts/Managers/LobbySceneManager.cs` (+`.meta`)
- `Assets/2.Prefabs/Managers/LobbySceneManager.prefab` (+`.meta`)
- `NetworkSessionLauncher.OnSetConnectionData(string ip)` 1인자 오버로드
  — 유일 호출자가 `LobbySceneManager`. 포트를 조용히 7777로 고정하는 오버로드라
  남겨두면 나중에 함정이 된다.

**재지정**
- `Assets/Settings/PlayMode/LobbySceneTest.asset` → 초기 씬 `3.BeaverLobby`
- `Assets/Settings/PlayMode/PlayerTest.asset` → 초기 씬 `3.BeaverLobby`

빌드 목록엔 없던 씬이므로 빌드 영향 0.

## 완료 조건

1. `Dev_MapScene`을 열고 Play → 좌상단 `Start Host` → 아래가 모두 성립:
   - `[NetworkClock] MainGame 시작 스탬프 = …` 로그 1회
   - `NetworkClock.MainGameElapsed`가 증가 (`IsRunning == true`)
   - `MainGameElapsed`에 의존하는 결정론 모션(MovingPlatform, Vent)이 실제로 움직인다
   - 인게임 BGM 재생 (= `NotifyMainGameReady()` 발행 확인)
2. **정식 흐름 회귀 없음**: `0.BootStrap → 1.Title → 3.BeaverLobby → 2.Loading → 4.MapScene`
   `[SceneFlow]` 로그 시퀀스가 변경 전과 동일.
3. C# 컴파일 0 에러 / 0 경고. `3.LobbyScene`·`LobbySceneManager` 잔존 참조 0건 (GUID 스캔).
4. MPPM 2인 검증(BeaverLobby 경유 정식 흐름) 정상.

## 리스크 / 한계

- **Dev 씬은 솔로 전용이다.** 일반 `SceneManager`로 MapScene을 먼저 얹고 나중에 호스트를
  시작하는 순서라, 클라이언트 씬 동기화 경로가 정식과 다르다. 호스트 시작 시점에 이미
  로드된 씬의 `NetworkObject`는 서버가 스폰하므로 솔로는 성립하지만, **Dev 씬에서 2인
  접속은 보장하지 않는다.** 멀티 검증은 정식 흐름/MPPM으로 한다. 이 제약은 씬 안 텍스트와
  `DevSceneBooter` 주석에 남긴다.
- `ForProfile`은 `#if UNITY_EDITOR`가 아니다. Dev 씬을 빌드 목록에 넣지 않으므로 실무상
  무해하나, 누군가 목록에 추가하면 릴리즈에 GUI 버튼이 남는다.
- `GameManager.cs` 변경이 팀장 담당 영역과 겹친다(위 B 참고).

## 미확정 (승인 시 함께 확정)

- Dev 씬 이름/경로: `Assets/0.Scenes/Dev/Dev_MapScene.unity` 로 제안
- MPPM 초기 씬을 `3.BeaverLobby`로 제안 (대안: `0.BootStrapScene` = 정식 흐름 전체 검증)
- 커밋 분할: (A+B 부팅) / (C 로비 정리) 2개로 제안

# CURRENT PLAN — Wall Occlusion per-pixel 재설계 (2026-07-28)

> 상태: **완료·push됨 (`0314d4c`)**. Play Mode 검증 통과. SVN 최신화 완료(r235).
> 다음은 MapScene Play 검증 → 머지.
> 자동 검증: C# 컴파일 0 에러 / 0 경고, 셰이더 컴파일 정상, EditMode 15 passed / 0 failed,
> Apply All 매핑 5쌍, Validate errors=0
> 설계 문서: [Docs/tech/wall-occlusion-implementation.md](Docs/tech/wall-occlusion-implementation.md)

## 왜 재설계했나

이전 구조는 벽 오브젝트당 스칼라 불투명도 하나를 CPU가 계산해 MPB로 밀어 넣었다.
그래서 벽은 통째로 사라지거나 통째로 남거나 둘 중 하나였고, 사용자가 요구한
**벽 표면 위의 그라데이션**(ㅡ자 벽에서 시선축 쪽 끝은 투명, 반대쪽 끝은 불투명)이
원천적으로 불가능했다.

추가로 이전 구조의 "정밀 판정"은 실제로는 동작하지 않았다. `Collider.ClosestPoint`가
non-convex MeshCollider를 지원하지 않아 AABB로 폴백하는데, 맵의 벽 콜라이더는 전부
non-convex였다. 즉 제거했다던 코너 오판이 그대로 남아 있었고, EditMode 테스트는
BoxCollider로만 검증해서 초록이었다.

## 무엇으로 바꿨나

불투명도를 프래그먼트의 월드 좌표로 셰이더가 직접 계산한다. C#은 전역 유니폼
네 개만 갱신한다.

- 물리 쿼리 0회, MaterialPropertyBlock 0회 (SRP Batcher 복귀)
- `WallOcclusionUnit` / `Proxy` / `Manager` / `VisibilityContributor` 삭제
- 페이드 타이밍·히스테리시스 삭제 — 공간 그라데이션에는 on/off 이벤트가 없다
- 렌더러 이름 문자열 필터 삭제 — 벽/바닥은 셰이더가 노멀(`1-|normalWS.y|`)로 가른다
- 정적 스테이지(`Stage1`)도 `OnEnable`에서 바인딩 (이전엔 영원히 누락됐다)

## 완료 조건

- [x] 셰이더 4개 패스 전부 per-pixel 클립 + Forward+ 키워드(`_CLUSTER_LIGHT_LOOP`) 추가
- [x] `WallOcclusionDriver`가 명시 카메라/타깃으로 전역 유니폼 갱신 (`Camera.main` 금지 유지)
- [x] 머티리얼 바인더 멱등화, 미매핑 머티리얼 리포트
- [x] 저작툴에서 Shader Graph 프로퍼티 이름 하드코딩 제거 (아트 재저장에 안 깨지도록)
- [x] MapScene 컴포넌트를 Driver 하나로 교체
- [x] 구 아키텍처 삭제 + 테스트 교체
- [x] **사용자 Play Mode 검증** — 그라데이션·자주색 해소 확인
- [x] `Assets/level` 아트팩 145개 재배치 (GUID 145/145 보존) + 오클루전 매핑 5쌍 → 14쌍
- [x] **SVN 최신화** — r235 커밋(신규 242·수정 104). r234의 GUID 재발급 역머지 + 콜라이더
      플래그 39개 복구 포함. 경위 = `Docs/_local/lessons.md` #14
- [x] MapScene **Play 검증** — 경사로 낙하 발견 → 경사로·계단 17개 MeshCollider 부착
      (`caaef90`). `MapColliderAuthoring` 이름 필터에 slope/stair 추가.
- [x] **`feature/dash-soul` 머지** (`7a5db51`, 59커밋) — 씬 구조 재편 충돌 9건 해소.
      MapScene은 UnityYAMLMerge 3-way로 충돌 0. 컴파일 error CS 0.
- [ ] 머지본 **Play 검증** (맵 생성·경사로·dash/soul) → push
- [ ] 이후 `development` 머지 — 기준 209 커밋 앞, fast-forward 가능

## 남은 과제

- 스킬 장판(Telegraph) 채널 미구현 — 전역 벡터 배열로 복원 가능
- 변종 머티리얼은 원본 Shader Graph의 근사치. 룩을 정확히 보존하려면 원본 그래프에
  Custom Function + Dither 노드를 넣어야 하나 SVN 아트 수정 합의 필요
- 다른 커스텀 셰이더(ToonLit, WaterDark)의 Forward+ 키워드 누락 점검

## 별개 이슈 — 맵 콜라이더 전멸

2026-07-28 아트 교체로 맵 프리팹 12개의 콜라이더가 전부 사라졌다(렌더러 1,823 / 콜라이더 0).
재설계 후 벽 투명화는 콜라이더를 쓰지 않아 이 기능을 막지는 않지만, 플레이어 충돌·
NavMesh·낙하 방지에 필요하다. `Tools > Map > Authoring > Add Floor+Wall MeshColliders`로 복구.

---

# CURRENT PLAN — 보스룸 진입·No.23 등장·전투 전환 (2026-07-24)

> 상태: 설계 승인, 구현 대기
> 설계: [Docs/superpowers/specs/2026-07-24-boss-encounter-intro-design.md](Docs/superpowers/specs/2026-07-24-boss-encounter-intro-design.md)
> 구현 계획: [Docs/superpowers/plans/2026-07-24-boss-encounter-intro.md](Docs/superpowers/plans/2026-07-24-boss-encounter-intro.md)

`BossEncounterDirector`가 서버 권한으로 텔레포트 완료 ACK, 플레이어 상태 초기화·입력 잠금, No.23 낙하/착지 연출, 페이지 대사, 만장일치 ESC 스킵, 동시 전투 시작을 조율한다. 충전기 4개 재사용, 보스룸 투명 경계와 NavMesh 포함까지 같은 수직 슬라이스에서 MPPM 1·2·3인으로 검증한다.

---

# PLAN — 몬스터 FSM 프레임워크 + 일반몹 5 + 중간보스 3 (2026-07-16)

> 직전 PLAN(맵 콜라이더+Paladin 스폰, 커밋 bd10c98/01ceb71)은 완료. E2E 검증만 미완(git+메모리 보존).
> 보스(웰즈&23호) 재작성은 **다음 스펙** — 의도는 메모리 `project-boss-bt-intent-analysis`에 확정.

## 목표
`Unit`(=UnitBase) 상속 · **코드 FSM(BT 미사용)** · 데이터 주도(ScriptableObject) 몬스터 프레임워크를 만들고,
SRG 로봇으로 **일반몹 5종 + 중간보스 3종**을 찍어내 MonsterScene에서 서버권한 협동 전투를 검증한다.

## 스코프
- **In**: FSM 프레임워크 · 일반몹 5(ChompBot·HumanoidBot·PeekABot·TeslaBot·MortarBot) · 중간보스 3(GauntletBot·SpinnerBot·WallBot) · 스포너 · 상태복제 · 그로기/슈퍼아머 · 상태적용 파사드 · 사망(Death애니+디졸브) · 드롭 훅 · MonsterScene 검증.
- **Out(다음 스펙)**: 보스(웰즈&23) 재작성 · 맵생성 연동(함수 호출만 후속) · 성장/드롭 실제 값(훅만) · 풀 상태이상 시스템 · 밸런싱.
- **불변식**: 팀원 BT 자산(`Enemy.cs`·`Assets/1.Scripts/Enemy/Boss/*`·`8.BehaviorTreeGraph/*`)은 **손대지 않음**. 신규 코드는 별도 폴더/네임스페이스로 완전 분리(제안: `Assets/1.Scripts/Monster/`).

## 현재 이해 (조사 완료)
- `Unit`: 서버권한 스탯/체력/쉴드/방어/`StatusEffectType`(enum·저장만) + `IKnockbackable`. 지속시간·만료·CC적용 로직은 없음.
- `AttackInfo`: "기본/스킬 공격 여부 + 그로기 여부"만 담음 → **데미지와 상태이상은 별도 경로**로 처리해야 함.
- 기존 몹 골격은 전부 BT 기반(`BehaviorGraphAgent`) — 재사용 안 함. 이동은 `NavMeshAgent` 서버전용(`RunningOnlyOnServer` 패턴만 개념 차용).
- SRG 8종 전부 **Humanoid 릭** → Mixamo 리타게팅 호환. Death 애니는 GauntletBot(Defeat)만 존재.
- 전용 몹 스포너 없음. 투사체 프리팹 없음.

## 접근
### 1) FSM 프레임워크 (`MonsterBase : Unit`)
- 상태 = `MonsterState` enum + 상태별 Enter/Tick/Exit를 **한 클래스에서** 처리(BT↔Animator desync 제거).
- 상태 진입 시 **직접 Animator 재생**. 공격 판정은 **애니이벤트 프레임**에 훅.
- **상태 복제(척추)**: `NetworkVariable<MonsterState>`(Server write) → 클라 `OnValueChanged`에서 상태→애니 매핑으로 Animator 재생. 판정은 서버만.
- 이동: `NavMeshAgent` **서버 전용**(클라는 NetworkTransform 복제).

### 2) 아키타입 3종 (공유 로직 + SO 데이터)
- **Melee**(Chomp·Humanoid, +중간보스 3): 추격→사거리→애니이벤트 Overlap 히트박스.
- **RangedTurret**(PeekABot·TeslaBot): 정지, Alert→Reveal→Shoot, 발사 프레임에 서버 투사체 스폰.
- **RangedMobile**(MortarBot): 재배치 후 포격.
- `MonsterDataSO`: 스탯·AnimatorController·공격파라미터·탐지/리쉬 거리·그로기/슈퍼아머 설정·아키타입.

### 3) 타게팅/어그로/전투 루프 (일반 게임식 어그로 관리)
- 모든 몹은 **스폰 포인트 저장**. 인지범위 내 **Player 태그/레이어 발견 → target**.
- 공격범위 내 → 공격, 아니면 추격. 범위 안에 계속 있으면 **계속 공격 반복**.
- **공격 시도 중 피격 → 공격 취소 + Hit 처리**(일반 몹).
- **리쉬**: 몹마다 개별 리쉬 범위. 플레이어가 그 밖으로 벗어나면 **스폰포인트 복귀 + 상태 초기화**(버프/디버프 전부 제거 + 체력 최대 회복).
- 플레이어 상호작용 로직 참고처: **git `feature/playerskill` 브랜치**(스킬/공격이 Unit에 어떻게 데미지·상태를 적용하는지).

### 4) 중간보스 차별 (= Melee + config)
- **강화** 스탯. **그로기**: `GroggyCount/MaxGroggyCount` 누적 피격→Groggy(취약·지속) — 애니는 GauntletBot `Defeat` 공용(SpinnerBot은 Dizzy).
- **슈퍼아머**: 특정 공격 windup~active 동안 들어오는 CC/넉백/행동취소 무시. 표현 = **노란 외곽선**(슈퍼아머 상태 인지) + 피격 시 **빨강 틴트 잠깐**(데미지는 들어갔음을 표시, 단 행동취소 아님). 임시 틴트로 시작, 전용 VFX는 추후.
- **CC/버프/디버프**: 상태적용 **파사드**(`IStatusEffectFacade` 등) 경유 — 본체는 은희가 나중에 정의(지금은 스텁). 슈퍼아머는 이 파사드 위에서 "무시" 판정.

### 5) 공격 판정
- 멜리: 애니이벤트 프레임 서버 `OverlapBox/Sphere` → 데미지+상태(파사드).
- 원거리: 발사 프레임 **서버 투사체 프리팹**(임시 제작, 추후 교체) + INab VFX 재사용.

### 6) 사망 / 드롭
- HP 0 → 서버가 **Death 애니 재생 → 디졸브 소멸 → 디스폰**(맨 디졸브 금지). Death 없는 7종은 **Mixamo Death** 취득.
- 드롭은 `virtual OnDeath(killerContext)` **확장 훅만** 노출(은희 성장시스템이 값/호출 연결). 사망 지점 단일화.

### 7) 애니 취득 매핑(Humanoid 공용)
- **Hit** = WallBot Hit 공용(자기 Hit 있으면 자기 것). **Groggy** = GauntletBot Defeat 공용(중간보스 3). **Death** = Mixamo(전 로봇). 어색한 놈만 개별 Mixamo 교체(추후).

### 8) 스폰
- Spawner 컴포넌트 + 씬 배치 스폰지점 → 서버 NGO 스폰 후 복제. 이후 맵생성 연동은 **동일 스폰 함수 호출**만.

## 전투 계약 통합 (feature/PlayerSkill 분석 확정 — 계약은 현재 브랜치에도 존재)
- `AttackInfo{damage, attackType(None/Default/Q/E/R), isGroggyAttack}` — 데미지·상태 분리. 방향/위치=`AttackHitContext`.
- **몹 피격**: `Enemy:Unit`(Unit이 IAttackReceiver 구현) + Collider + `Hurtbox`(ownerUnit 지정). `OnNetworkSpawn` 서버에서 `Initialize(...)` 필수(안 하면 _health null NRE). 그로기 = `TakeDamage(AttackInfo)` override에서 `isGroggyAttack` 처리.
- **몹 공격**: `BaseAttack` 계열 재사용 — 근접 `ColliderBasicAttack`+`AttackTriggerRelay`, 스윕 `OverlapAttack`(애니이벤트 `Hit()`), 원거리 `DefaultAttackProjectile.Launch`. `targetLayer`=플레이어. `InitializeAttackInfo()` 보장.
- **넉백**: 루트에 `Rigidbody`+`NavMeshAgent`+`LinearKnockback` → `Unit.Knockback(dir,strength)`(AttackInfo 무관).
- **시간제 상태이상 = 어디에도 없음** → 신규 최소 서버권한 컴포넌트(`MonsterStatusEffect`: NetworkVariable 플래그 + 타이머 + FSM 차단조회) + 상태적용 파사드 신설. `StatusEffectController`(플레이어 전용·무타이머 조회)는 몹에 미사용.
- **금지**: 오너→서버 직접 데미지 RPC(`TakeDamageRpc`) 의존(PlayerSkill 제거됨). 데미지는 `BaseAttack→ReceiveAttack` 서버 경로만. 밸런스는 경감률 `dmg×100/(100+def)` 기준.

## 네트워크 권한 가정
- 스폰·FSM·이동목표·데미지·상태·사망·드롭 = **전부 서버(호스트) 권한**. 클라 = 상태/트랜스폼 복제로 애니만 재생.

## 리스크 / 의존성
- MonsterScene에 **NavMesh Bake 선행 필요**(맵 콜라이더/네브메시 미비 — 사용자 처리).
- **투사체 프리팹 임시 제작 필요**(추후 교체).
- **디졸브 셰이더/머티리얼 필요**(없으면 임시 페이드 폴백).
- **SRG 팩(224파일) = SVN 처리**(git 금지, 사용자). `.meta` 동반.
- Mixamo Death/Hit/Groggy 클립 취득(사용자) — 없으면 임시 폴백으로 배선만 완성.
- 파사드 스텁 상태에선 CC 실제 효과(이동/입력차단)는 은희 구현 전까지 no-op.

## 미결 / 확인 필요
- 신규 폴더/네임스페이스 = `Assets/1.Scripts/Monster/` 로 확정?
- 슈퍼아머 텔레그래프 표현(머티리얼 틴트 vs 전용 VFX) — 아트 의존, 임시 틴트로 시작?

## 완료 조건 (Acceptance)
- 스포너가 서버에서 몹 스폰 → 전 클라 복제(위치·애니 동기).
- 일반몹 5종: 탐지→추격→공격(서버판정)→피격(Hit)→사망(Death+디졸브)→`OnDeath` 훅 발화.
- 중간보스 3종: 위 + 그로기(누적피격 발동, 취약) + 슈퍼아머(텔레그래프 표시 + CC/넉백 무시).
- 원거리 3종: 서버 투사체 발사·명중 판정 복제.
- 플레이어 CC가 파사드로 라우팅(스텁이라도 호출 확인), 슈퍼아머 중 무시.
- 콘솔 0 에러(서버·클라).

## 검증 계획 (MPPM host+client)
MonsterScene(NavMesh baked) → host+1클라 → 스폰 → 각 아키타입 1종씩 교전 → 중간보스 그로기·슈퍼아머 → 사망 연출·훅 로그 확인. 서버판정/클라애니 동기 육안 + 콘솔 확인.

---

# PLAN 추가 — 공격 이동잠금 + 지속넉백/경직(Q 스킬 대응) + HumanoidBot (2026-07-20)

> 위 프레임워크(2026-07-16)는 가동 중. 이번 작업은 그 위에 **버그 2건 + 코어 전투 확장 1건**.
> 팀장 그릴 확정 사항: ①넉백=지속밀림 ②구조=지속넉백 먼저→끝나면 Stunned 경직(~0.2s, 0.1~1s 조절) ③공격종료=attackDuration 튜닝(지금)+애니 End 이벤트(이후).

## A. 공격 중 이동/슬라이드 근본 해결
- **완료**: `StopAgent()`에 `agent.velocity=Vector3.zero` — 공격 진입 시 감속 글라이딩 제거(MonsterBase/BossBase).
- **잔여 원인**: `attackDuration`(예: Humanoid 0.9s)이 실제 공격 클립보다 짧아 FSM이 클립 끝나기 전에 Attack을 빠져나가 재추격 → 클립 꼬리에 재이동("공격 끝나기 전 이동").
- **지금**: 각 몹 데이터 `attackDuration`을 실제 공격 클립 길이에 맞게 튜닝(클립 길이 조회 후 반영).
- **이후(견고)**: MonsterBase에 애니 이벤트 수신 훅(`OnAttackAnimEnd`) + 릴레이 → 클립 End 이벤트가 있으면 그걸로 Attack 종료, 없으면 attackDuration 타이머 폴백. (플레이어 DefaultAttack 방식과 동형.)

## B. HumanoidBot 안 걸음
- 확인됨: `Controller_HumanoidBot` 파라미터 Attack/Hit/RunBlend = 데이터와 일치(배선 정상).
- 원인 후보: 컨트롤러 내부 **RunBlend 블렌드트리에 걷기/달리기 클립 미연결**(유력) 또는 물리적 미이동. → 컨트롤러 열어 블렌드트리 확인 후 걷기 클립 배선(에셋 작업). 다른 몹이 걸으면 전자 확정.

## C. 지속넉백 + 경직 (플레이어 Q 근접스킬 대응) — 코어 전투 확장
**목표**: 어떤 공격이든(특히 Q) 맞은 몹을 **지속적으로 밀어내고(넉백) → 끝나면 짧게 경직(Stunned)** 시킬 수 있게, 값 **조절 가능**하게. 스킬 본체는 스킬 담당에게 **넘길 수 있도록 인프라 + 수신측**을 제공.

### C-1. 전송: `AttackInfo` 확장(하위호환)
- 필드 추가: `float knockbackStrength`(0=넉백없음), `float knockbackDuration`(지속밀림 초), `float staggerDuration`(넉백 종료 후 Stunned 초).
- 생성자에 **옵션 파라미터(기본 0)** 추가 → 기존 `new AttackInfo(dmg,type,groggy)` 호출부 전부 무영향.

### C-2. 수신: MonsterBase 반응 시퀀스
- `ReceiveAttack(info, hitContext)` override로 **넉백 방향 확보**(dir = 몹 - attacker, 수평). 데미지는 기존 경로 유지.
- `knockbackStrength>0`이면: **MonsterState.Knockback 신설** 진입 → agent off, 서버가 knockbackDuration 동안 **매 틱 dir로 지속 밀기**(NetworkTransform 복제) → 종료 시 `MonsterStatusEffect.ApplyStatus(Stunned, staggerDuration)` → 경직 유지 후 `DecideNextAfterAction`.
- **슈퍼아머(BlocksInterrupt) 중이면 넉백·경직 무시**(기존 CC 무시 규칙 일관). 사망/그로기/복귀 중 진입 가드.
- 지속밀림 구현은 `LinearKnockback` 확장(sustained 모드) 또는 MonsterBase 서버틱 직접 이동 중 택1 — 물리 vs NetworkTransform 충돌 최소인 쪽으로(구현 시 결정, 기본안=서버틱 직접 이동 + agent off).

### C-3. 조절 가능 + 핸드오프 + 검증
- 값은 **공격(스킬) 측 AttackInfo가 지정** → Q 스킬은 자기 SO/SerializeField로 세팅만 하면 됨(핸드오프 지점).
- 검증: `MonsterTestBootstrap.DoDebugAttack`에 knockback/stagger 파라미터 추가 → 좌클릭으로 몹에 지속넉백→경직 재현.

## 스코프 경계
- **In**: A(튜닝+이벤트훅), B(진단+걷기배선), C(AttackInfo 확장·MonsterBase 수신·지속넉백·경직·디버그검증).
- **Out/하드오프**: 플레이어 **Q 스킬 본체**(입력→히트박스→AttackInfo 세팅)는 스킬 담당. 나는 그쪽이 값만 채우면 되도록 인프라+수신만.
- **불변식**: BT 자산·`Enemy/*` 미변경. AttackInfo 확장은 하위호환(기존 호출부 무수정).

## 리스크
- 지속넉백이 NavMeshAgent/NetworkTransform와 충돌 가능 → agent off + 서버 권한 이동으로 격리.
- attackDuration 튜닝엔 실제 클립 길이 필요(조회 후 반영).
- B는 애니메이터 에셋 작업 — 걷기 클립 자체가 없으면 취득 필요(사용자).

## 완료 조건
- 공격 중 몹이 제자리 고정(진입 글라이딩 0 + 클립 끝까지 재이동 없음).
- 디버그 공격으로 몹이 지속적으로 밀린 뒤 ~0.2s 경직 → 이후 정상 복귀. 값 조절 반영됨.
- 콘솔 0 에러. 기존 몹/보스/플레이어 공격 계약 회귀 없음(AttackInfo 하위호환).

---

# PLAN 추가 — MapScene 몬스터 통합 (2026-07-21)

> 전제: C(지속넉백+Stunned 경직) 구현 완료(AttackInfo 확장·MonsterState.Knockback·Q 배선, 컴파일 0).
> 팀장 확정: ①존 스폰 = **전존 정석 저작**(ZoneLayout+마커) ②바닥 = **fbx addColliders 지금 확정**(구 미결 해결책 A).

## 목표
MapScene(생성맵)에서 몬스터 실스폰 + FSM 사이클(탐지→추격→공격→피격/넉백→사망/리쉬복귀) E2E 검증. 완료 후 git push.

## 작업
1. **fbx 콜라이더**: `50.Art/MapGen/MapObj/mesh/{floor,wall}` 임포터 콜라이더 활성(.meta) → 재임포트 → 바닥/벽 MeshCollider 생성. ⚠️ 아트 .meta=SVN 관할 — 로컬 적용 후 **팀장 TortoiseSVN 커밋** 필요. (meta에 addCollider 키 부재 확인됨 — 임포터 버전별 키 확인 후 적용)
2. **ZoneLayout 전존 저작**: 에디터 일회성 스크립트(`1.Scripts/Map/Editor/`)로 존 프리팹 12개에 ZoneLayout 부착. Size/Role/Difficulty = ZoneLayoutCatalog Entries와 일치. Combat존 MonsterGroupID 배정 + 렌더 바운즈 기반 스폰 마커 자식 자동 생성(L=4/M=3/S=2, Start·BossEnter·bossroom=0). 위치는 러프 — 테스트 후 프리팹에서 수동 조정.
3. **MonsterGroups 등록**: `MapGenConfig.asset`(50.Art=SVN)에 그룹 풀 등록 — 1=ChompBot 2=HumanoidBot 3=TeslaBot 4=MortarBot 5=GauntletBot(중간보스 존용).
4. **NavMesh 런타임 베이크**: `MapNavMeshBaker` 신설 — MapGenerator.OnGenerated 구독 → NavMeshSurface.BuildNavMesh() → 이미 스폰된 몹 agent 재부착(SamplePosition+Warp, 스폰이 베이크보다 먼저라 필수). MapScene에 NavMeshSurface 배치.
5. **테스트 하네스**: NetworkManager+MonsterTestBootstrap(Player 스폰, 디버그공격 off)을 **MapScene에 직접 추가**(팀장 확정). ⚠️ MapScene은 빌드 플로우(index 3) 소속 — **push 전 하네스 오브젝트 제거 필수**(잔존 시 정식 로딩 플로우 NetworkManager 중복).

## 리스크
- 몬스터 스폰(SpawnPlacements 내부) < 베이크(OnGenerated) 순서 → 4의 재부착으로 해소.
- 마커 자동 위치가 소품 위/통행불가 지점에 떨어질 수 있음(테스트에서 조정).
- 50.Art 에셋 변경분(meta·Config)은 git에 안 잡힘 — SVN 커밋 별도 안내.

## 완료 조건
호스트 Play → 맵 생성 → 존별 몬스터 스폰 → NavMesh 이동/전투/넉백/리쉬 사이클 정상 → 콘솔 0 에러 → git push + SVN 커밋 목록 안내.

## 진행 상태 (2026-07-21 세션 마감)
- ✅ §1~5 구현·1차 검증 완료(컴파일 0): 콜라이더/NavMesh 런타임 베이크, 전존 ZoneLayout+MonsterGroups 저작, 하네스, CC(넉백/경직)+Q, 플레이어 버그 2건(벽관통·조준폴백).
- ✅ §6 보스 입장: 패드 표시+진입 색전환+이탈 취소+생존자 전원 텔레포트+페이드, 튜닝값 인스펙터화까지 완료.
- ⏳ 남은 작업·조사·멀티검증·커밋가이드 = **Docs/tech/map-monster-boss-handoff.md**로 이관(Codex 인수). 핵심: 패드 y 가림 / MPPM 텔레포트 검증 / 터렛 스폰 재확인 / MortarBot 복귀후 Idle 회귀 조사 / push 전 하네스 제거.
- ⛔ 캐릭터 누워있는 이슈 = 타 팀원 확인 중, 건드리지 말 것.

## 추가 §6 — 보스 Enter 카운트다운+전원 텔레포트 (2026-07-21 팀장 확정)
스펙(2026-07-21 2차 개정 — 로아식): 생존 플레이어 **1명이라도** BossEnter 존 진입 → 서버가 3·2·1 카운트다운(전 피어 표시) → 만료 시 **생존자 전원**을 보스룸으로 이동. **카운트다운 중 존이 비면(전원 이탈/전멸) 취소·리셋**(재진입 시 재시작). 존 범위는 바닥 테두리 라인으로 상시 표시 — 대기 시안/진입·카운트다운 초록(BossEnterZoneVisual, 전 피어 로컬). 몹 스폰은 마커→바닥 레이캐스트 스냅(허공 마커는 존 중심 폴백+경고 — 터렛 부유 방지). 보스룸 = bossroom.prefab을 **맵 밖 고정 좌표(x≈+500)에 씬 배치**. 보스 스폰/전투 시작은 스코프 외.

구현:
- `BossEnterTrigger`(일반 MonoBehaviour): 서버 전용 판정, 생존 Player 진입 1회 감지 → 매니저 호출. 존 프리팹은 비네트워크 규약이므로 **MapContentSpawner가 BossRoom 역할 존 스폰 시 서버에서 동적 부착**(트리거 박스 = 존 바운즈).
- `BossTeleportManager`(씬 상주 NetworkObject): `NetworkVariable<double>`(서버시간 만료각)로 카운트다운 복제 → 전 피어 OnGUI 임시 표시(UI 담당 교체 전제). 만료 시 서버가 ConnectedClients 중 CurrentHealth>0만 산개 텔레포트 — 서버권한 NT는 `NetworkTransform.Teleport`, 오너권한 대비 대상 오너에게 ClientRpc 로컬 이동 병행. **매니저 GO 위치 = 텔레포트 지점**(참조 배선 없음).
- 주의: FoW/미니맵이 보스룸(맵 밖 좌표)을 어떻게 다루는지는 후속 확인.

---

# PLAN 추가 — 중간보스 3종 완성 (2026-07-20)

> grill 확정. 페이즈 없음(**그로기만**). 고유 기믹까지 구현. C(넉백/경직)는 은희 인터페이스 대기(병렬).
> 애니 상태 존재 확인 완료. 데미지=애니이벤트(OnAttackHit)+windup 타이머 폴백(A②로 이미 구축).

## 공통 신규 시스템
### (S1) AoE 텔레그래프 (신규 — 기존 시스템 없음)
- 바닥에 **빨간 장판**(임시 데칼/평면 + 반투명 빨강 머티리얼)을 windup 동안 표시 → 히트 시 사라짐.
- 서버 권한 표시 + 전 피어 복제(NetworkVariable 또는 ClientRpc). 크기/지속=파라미터.
- 재사용형 컴포넌트(`AoeTelegraph`)로 — 이후 보스 장판에도 재사용.

### (S2) 코드 주도 공격 선택 + CrossFade
- 진입은 단일 `Attack`(FSM). 서버가 **가중치 랜덤으로 1종 선택** → 선택값 ClientRpc 브로드캐스트 → 전 피어가 해당 상태로 **CrossFade**(orphan 상태여도 CrossFade 가능 → 진입전이 배선 불필요). 종료 시 ResetToLocomotion(구축됨)로 이동 복귀.

## GauntletBot (콤보 스크립트 → 7종 가중치 선택기로 재작성)
- **7 공격**: `Smash` + `Gauntlet_Punch01_L/R`, `02_L/R`, `03_L/R`. (상태 존재 확인)
- **선택 가중치**: 01/02 高, **03(어퍼컷) 最低**. `Smash`는 **범위 내 player 수(1/2/3+)에 따라 확률 상향**(1명 싱글도 동작). 매 cadence(1/attackSpeed)마다 선택.
- **Smash**: windup 동안 (S1) 빨간 장판 표시 → 히트 프레임에 **AoE 오버랩 데미지**(반경=장판). 강력.
- **어퍼컷(03)**: **데미지만 지금**. player airborne CC = **연기(훅만)** — C/상태이상 인터페이스 이후.
- **01/02**: 일반 단타 데미지.
- L/R = 시각 다양성(번갈아/랜덤). 데미지는 OnAttackHit 이벤트(없으면 windup 폴백).
- 기존 콤보(exit-time/RPC 콤보) 로직 제거.

## SpinnerBot (신규 스크립트 `SpinnerBot : MonsterBase`)
- **기본 = Whip 근접**(`Attack Whip L/R`), **특수 = 스핀 대시**. 평소 Whip, 조건/확률로 스핀 대시(B안 확정).
- **스핀 대시**: `Spin Attack Start`(제자리 회전 windup, **최소 1초 회피시간 보장**) → `Spin Attack Loop` 유지하며 **직선 질주**(**공격 시작 시 방향 고정**, windup 중 재조준 X → 옆으로 회피 가능) → 경로상 적 **1틱** 데미지(hit-once). **windup시간/거리/속도/지속 전부 인스펙터 노출**(플레이테스트로 튜닝).
- **낙하 안전(★)**: 대시는 **NavMeshAgent + `NavMesh.Raycast`로 navmesh 경계 클램프** → 낭떠러지(navmesh 밖) 절대 안 감, 가장자리에서 정지. raw transform 이동 금지.
- **스핀 종료 → Dizzy**: 그로기/취약 창 진입(SpinnerBot 그로기 애니 = Dizzy). 스핀 후 자기 유발 빈틈. 종료 후 로코모션 복귀.

## WallBot (방어형 — 이미 단발, HP만 상향)
- `attackFinishTrigger`(AttackEnd)는 **다단 히트가 아니라 단일 스윙의 2단 애니(Start→Strike) 진행용**. MonsterBase는 히트 1회 + 이 트리거로 애니만 진행 → **이미 단발 공격**. 그대로 유지(제거하면 애니가 안 끝남).
- **매우 높은 HP로 상향**(350→600, 튜닝). 역할=탱커. 커스텀 스크립트 불필요.

## 리스크 / 의존성
- (S1) 텔레그래프 = 신규 에셋(데칼/머티리얼) 제작 필요.
- OnAttackHit 애니이벤트를 각 공격 클립에 삽입해야 정밀 타이밍(미삽입 시 windup 폴백 동작).
- GauntletBot 각 공격의 Anticipation→본동작 전이 확인(없으면 본동작으로 직접 CrossFade).
- 어퍼컷 airborne = 상태이상 인터페이스(은희) 이후.
- SpinnerBot 스핀대시 vs NavMesh/NetworkTransform — 서버 권한 이동으로 격리.

## 완료 조건 (중간보스)
- GauntletBot: 7종 가중치 선택 발동, Smash 장판→AoE 데미지, 어퍼컷 데미지(airborne 훅), 그로기, 사망.
- SpinnerBot: 스핀대시 발동·경로 1틱 데미지, 그로기, 사망.
- WallBot: 단발 멜리 + 고HP 탱킹, 그로기, 사망.
- MonsterScene E2E(스폰→교전→고유공격→그로기→사망) + 콘솔 0 에러.
