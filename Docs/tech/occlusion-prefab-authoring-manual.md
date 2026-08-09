# 가림 투명화 프리팹 제작 매뉴얼

이 문서는 Stage·Zone·벽·복도·프랍을 새 가림 투명화 구조로 제작하거나 전환하는 아트 작업자용 매뉴얼이다. 기존 아트 프리팹은 자동으로 변경하지 않는다. 새로 만들거나 이번 구조로 전환한 프리팹부터 적용한다.

## 1. 결과부터 이해하기

- 카메라와 플레이어 사이를 실제로 가린 등록 오브젝트만 플레이어 주변이 캡슐 모양으로 뚫린다.
- 플레이어가 있는 층보다 높은 층은, 그 층의 Collider가 카메라 경로에 맞았을 때 바닥·벽·등록 프랍을 한 그룹으로 함께 뚫는다.
- 현재 층에서는 벽·복도와 `OccludableProps`의 큰 프랍만 개별 판정한다.
- `LevelOnlyProps`의 난간·작은 상자 등은 현재 층에서 항상 불투명하다. 단, 그 층 자체가 플레이어보다 위층이면 다른 바닥·벽과 함께 처리된다.
- Stage와 Zone은 완전히 같은 규칙을 사용한다.

## 2. 필수 하이어라키

```text
PF_Stage_01 또는 PF_Zone_*
└─ Occlusion
   └─ ElevationStack_01 [ElevationStack]
      ├─ Level_L01 [ElevationLevel]
      │  └─ Content
      │     ├─ FloorMesh 또는 RampMesh
      │     ├─ WallSection_01 [OcclusionSection]
      │     ├─ OccludableProps
      │     │  └─ PF_Prop_Container_A [OcclusionSection on root]
      │     └─ LevelOnlyProps
      │        ├─ PF_Prop_Railing_A
      │        └─ PF_Prop_Box_Small_A
      └─ Level_L02 [ElevationLevel]
         └─ Content
            ├─ FloorMesh
            ├─ HallwaySection_01 [OcclusionSection]
            ├─ OccludableProps
            └─ LevelOnlyProps
```

지하가 있으면 `Level_B01`, 지상이면 `Level_L01`, `Level_L02`처럼 만든다. 실제 층 순서는 이름이 아니라 각 Level 루트의 월드 Y 높이로 결정한다.

한 층뿐이어도 `Occlusion/ElevationStack_01/Level_*/Content` 구조를 생략하지 않는다. `Occlusion`, `Content`, `OccludableProps`, `LevelOnlyProps`도 비어 있더라도 반드시 둔다. 빈 분류 노드를 유지해야 잘못된 프랍 배치를 줄일 수 있다.

## 3. 층별 배치 규칙

### 바닥·경사로

- 바닥 Mesh와 Collider는 해당 Level의 `Content` 바로 아래 또는 그 하위에 둔다.
- 계단·경사로·진입 다리는 도착하는 높은 층의 `Content`에 둔다.
- 바닥은 별도의 `OcclusionSection`이 없어도 된다. 위층 전체 판정에는 Level의 Content 등록 정보가 사용된다.

### 벽·복도

- 현재 층에서도 플레이어를 가릴 수 있는 벽과 복도는 구간별 루트에 `OcclusionSection`을 둔다.
- 연속 외벽은 코너나 복도 접합부를 기준으로 `WallSection_01`, `WallSection_02`처럼 나눈다.
- 복도 프리팹은 `PF_Hallway_*` 루트가 자기 `OcclusionSection`을 소유한다.
- 하나의 Renderer나 Collider를 두 Section이 함께 소유하면 안 된다.

### 프랍

- 현재 층에서도 가려지면 뚫어야 하는 큰 프랍은 `OccludableProps`에 둔다.
- 현재 층에서는 뚫지 않을 프랍은 `LevelOnlyProps`에 둔다.
- 두 컨테이너 아래에는 `PF_Prop_*` 프리팹 루트만 직속 자식으로 둔다. 중간 분류 폴더를 만들지 않는다.
- `OccludableProps`의 프랍 루트에는 `OcclusionSection`이 정확히 하나 있어야 한다.
- `LevelOnlyProps`의 프랍 루트와 모든 자식에는 `OcclusionSection`이 없어야 한다.
- `PF_Prop_*` 안에 또 다른 `PF_Prop_*`를 넣지 않는다. 여러 프랍은 형제 루트로 배치한다.

## 4. Level 높이와 데이터 전용 XZ 박스 만들기

`ElevationLevel`의 루트 Y가 그 층의 기준 높이다. 같은 Stack 안에서 같은 Y를 쓰는 Level은 허용하지 않는다.

1. `Level_L01` 같은 Level 루트에 `ElevationLevel` 컴포넌트를 붙인다.
2. Inspector의 `XZ Areas` 목록에 항목을 추가한다.
3. `Label`, 로컬 `Center`, `Size`, `Rotation Degrees`를 입력한다.
4. Scene 뷰에서 Level을 선택해 청록색 핸들로 중심, Y 회전, XZ 크기를 조절한다.
5. 박스가 해당 고도 구조와 그 아래로 떨어질 수 있는 영역까지 포함하도록 평면 범위를 잡는다.

이 박스는 물리 오브젝트가 아니라 컴포넌트 안에 저장되는 데이터다. 이 용도로 `BoxCollider`나 Trigger를 새로 만들지 않는다. Y 크기는 없으며 Level 로컬 XZ 평면만 검사한다. 굽은 영역은 XZ Area를 여러 개 추가해 덮는다.

하나의 Stack은 연결된 하나의 고도 구조만 표현한다. 멀리 떨어진 건물이나 독립 발판은 별도 `ElevationStack_02`로 나눈다. Stack과 Level 루트는 위치 이동과 Y축 회전만 허용하며 Scale은 `(1,1,1)`로 유지한다.

## 5. Collider와 머티리얼 최소 조건

- 각 Level의 `Content` 전체에 Renderer가 최소 1개, Collider가 최소 1개 필요하다.
- 각 `OcclusionSection`에는 Renderer가 최소 1개, Collider가 최소 1개 필요하다.
- 모든 Renderer마다 Collider를 요구하지는 않는다.
- `LevelOnlyProps`의 개별 프랍마다 Collider를 강제하지 않는다.
- 투명화 대상 Renderer의 모든 원본 머티리얼은 `_WallOcclusionStrength` 속성을 지원해야 한다. 현재 V3 표준 지원 그래프는 `Generic_Standard`, `Generic_Basic`, `ConvayorBelt_Graph`, `ConvayorBelt_Corner_Graph`다.
- 도구는 별도 `_Occlusion` 변형 머티리얼을 만들지 않는다. 다른 Shader Graph가 필요하면 그 원본 그래프에 공통 `WallOcclusionClip.hlsl`을 통합한 뒤 사용한다.
- 잘못 등록된 대상은 런타임에서 안전하게 불투명으로 남고 경고는 한 번만 출력된다.

## 6. 등록·검증 순서

Project 창에서 프리팹 에셋 또는 Scene의 프리팹 인스턴스를 선택한다.

1. 중첩해서 사용할 `PF_Prop_*`, `PF_Wall_*`, `PF_Hallway_*` 원본을 먼저 선택한다.
2. `Tools > Rendering > Wall Occlusion > Register-Wire Selected Prefabs`를 실행한다.
3. Stage 또는 Zone 원본을 선택해 같은 메뉴를 실행한다.
4. `Tools > Rendering > Wall Occlusion > Validate Selected Prefabs`를 실행한다.
5. Console의 프리팹 경로, 오브젝트 경로, 수정 안내를 확인해 오류를 0개로 만든다.

도구는 선택한 프리팹만 처리한다. 하이어라키를 임의로 재배치하지 않고, 중첩 프리팹 원본을 몰래 수정하지 않는다. 중첩 원본이 미등록이면 그 원본을 직접 선택해 먼저 등록하라는 오류를 낸다. 검증 오류는 빌드를 막지 않지만 해당 런타임 대상은 불투명으로 제외될 수 있다.

## 7. 작업 완료 체크리스트

- Stage/Zone 루트 아래 `Occlusion`이 정확히 하나인가?
- 모든 투명화 대상이 Stack → Level → Content 안에 있는가?
- 각 Level에 두 프랍 컨테이너가 비어 있어도 존재하는가?
- 큰 가림 프랍과 현재 층 보호 프랍을 올바른 컨테이너에 넣었는가?
- 프랍 루트가 컨테이너의 직속 자식이고 중첩 `PF_Prop_*`가 없는가?
- Level 루트 Y와 XZ Areas가 실제 플레이 공간을 덮는가?
- Scale이 `(1,1,1)`이고 X/Z 회전이 없는가?
- 등록 후 Validate 결과가 오류 0개인가?
