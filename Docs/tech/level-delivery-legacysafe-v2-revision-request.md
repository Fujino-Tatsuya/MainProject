# Stage / Zone LegacySafeV2 반려 및 수정 요청서

작성일: 2026-08-08  
판정: **반려 — 수정본 재인계 필요**  
요청 대상: 레벨·아트 프리팹 제작 담당자  
검수 대상 패키지: `LevelDelivery_LegacySafeV2_20260808_134500.zip`  
검수 대상 인계서: `LevelDelivery_LegacySafeV2_20260808_134500_PROGRAMMER_HANDOFF.md`  
검수 패키지 SHA-256: `F38F9EBC26316CD14C28472A91D87CEAE15880D781A46FEE14D1D3A7F38A4886`

## 1. 반려 결론

이번 인계물은 GUID 충돌을 인지하고 기존 메인 에셋을 보호하도록 패키징한 **아트 페이로드**로서는 유효하다. 그러나 현재 MainProject의 가림 투명화 시스템에 등록하여 바로 사용할 수 있는 Stage·Zone 프리팹으로는 완성되지 않았다.

특히 다음 핵심 기능이 현재 상태로는 동작하지 않는다.

1. 같은 층에서 캐릭터를 가리는 벽·복도의 개별 투명화
2. 플레이어가 어느 고도 영역에 있는지 판정하는 XZ Area 기반 층 상태
3. `OccludableProps`에 들어간 중첩 프랍의 자동 등록
4. 엄격한 영문 ASCII 작명 검증
5. Renderer·Collider가 없는 빈 Level의 런타임 등록

따라서 ZIP을 MainProject에 직접 풀거나 기존 Stage1·Zone에 연결하지 말고, 아래 필수 수정 사항을 반영한 새 패키지를 제출해 주기 바란다.

## 2. 정상 확인된 부분

다음 항목은 수정본에서도 유지한다.

- Stage 1개와 Zone 11개가 모두 포함되어 있다.
- 최상위 V2 프리팹 12개와 각 `.meta`가 모두 한 쌍으로 존재한다.
- V2 최상위 프리팹 GUID가 MainProject의 기존 GUID와 충돌하지 않는다.
- ZIP 내부에 중복 GUID, 누락된 `.meta`, 고아 `.meta`가 없다.
- ZIP 항목에 절대 경로 또는 `..` 경로가 없다.
- `Occlusion/ElevationStack_01/Level_*/Content` 기본 뼈대가 존재한다.
- Level 기준 Y값이 인계서에 적힌 값과 일치한다.
- 기존 `Stage1.prefab`, 기존 Zone 프리팹, `ZoneLayout`, Spawn, Node, Slots를 직접 덮어쓰지 않는 정책은 올바르다.
- Git 프리팹과 SVN 아트의 VCS 소유권 분리는 올바르다.
- `Wire Slots + Catalog + Refs`를 실행하지 않는다는 지침은 유지한다.

## 3. 필수 수정 사항

### R-01. 벽·복도 Section 구조 복구 — 최우선

#### 확인된 문제

인계서에는 `WallSection_*` 컨테이너가 남아 있으면 실패라고 적혀 있다. 이 기준은 현재 MainProject 시스템과 반대다.

현재 시스템에서는 플레이어와 같은 층에 있는 벽·복도를 SphereCast가 맞혔을 때 개별 투명화하려면 해당 구간에 `OcclusionSection`이 필요하다. 제작 구조에서는 이를 `WallSection_*`, `HallwaySection_*` 또는 `Section_*` 루트로 표현한다.

정적 검사 결과:

- Zone 11개 모두 Section 루트가 0개다.
- Zone에서 Section 밖에 놓인 벽 계열 인스턴스가 최소 341개다.
- Stage에도 Section 밖의 벽·복도 계열 인스턴스가 약 1,148개다.
- 현재 상태로 등록하면 이 오브젝트들은 같은 층에서 캐릭터를 가려도 개별 투명화되지 않는다.

#### 수정 요청

1. 각 Level의 `Content` 안에서 현재 층의 플레이어를 가릴 수 있는 벽과 복도를 구간별로 묶는다.
2. 벽 구간 루트는 `WallSection_01`, `WallSection_02`처럼 작성한다.
3. 복도 구간 루트는 `HallwaySection_01`, `HallwaySection_02`처럼 작성한다.
4. 기존 Stage의 유효한 `HallwaySection_*` 구조는 유지하고, Section 밖에 남은 벽·복도를 적절한 구간에 포함한다.
5. 바닥 Mesh는 별도 Section 없이 해당 Level의 `Content`에 둘 수 있다.
6. 하나의 Renderer 또는 Collider가 두 Section에 동시에 포함되지 않게 한다.

#### 통과 조건

- 같은 층에서 가림 대상인 모든 벽·복도가 정확히 하나의 `OcclusionSection`에 소속된다.
- 바닥을 제외한 가림 구조물이 Section 밖에 남아 있지 않는다.
- `Validate Selected Prefabs`에서 Section 관련 오류가 0개다.

### R-02. 런타임 컴포넌트와 XZ Areas 작성 — 최우선

#### 확인된 문제

12개 V2 프리팹 모두 다음 컴포넌트가 0개다.

- `ElevationStack`
- `ElevationLevel`
- `OcclusionSection`

총 48개 Level의 XZ Area도 전부 0개다. `Register-Wire`는 Stack·Level·Section 컴포넌트를 연결할 수 있지만 XZ Area의 실제 영역은 자동 생성하지 않는다. XZ Area가 없는 Level은 런타임 유효성 검사에서 제외된다.

#### 수정 요청

1. MainProject의 현재 오클루전 코드와 동일한 GUID를 사용하는 검증 환경에서 작업한다.
2. 모든 실제 `ElevationStack_*`에 `ElevationStack`을 등록한다.
3. 실제로 존재하는 모든 `Level_*`에 `ElevationLevel`을 등록한다.
4. 각 Level의 실제 지형과 낙하 가능 영역을 덮는 데이터 전용 로컬 XZ Area를 최소 1개 작성한다.
5. 굽거나 분리된 영역은 XZ Area 여러 개로 덮는다.
6. XZ Area 용도로 BoxCollider나 Trigger를 만들지 않는다.
7. Section과 `OccludableProps` 프랍에 `OcclusionSection`을 등록한다.

#### 통과 조건

- 모든 실제 Level의 XZ Areas가 1개 이상이다.
- 각 Area가 Scene 뷰에서 실제 플레이 가능 영역과 낙하 영역을 덮는다.
- `ElevationLevel.IsRuntimeValid`에 해당하는 검증 오류가 0개다.
- 제출하는 `validation-report.json`에 12개 최상위 프리팹의 실제 검증 결과가 들어 있다.

### R-03. 존재하지 않는 빈 Level 제거

#### 확인된 문제

실제 내용이 완전히 비어 있는 Level이 12개 존재한다.

- `PF_Zone_L_Type_C_V2`: `Level_B01`, `Level_B02`
- `PF_Zone_M_Type_C_V2`: `Level_L02`, `Level_B01`, `Level_B02`
- `PF_Zone_Quest_01_V2`: `Level_L02`
- `PF_Zone_Quest_02_V2`: `Level_L02`, `Level_B02`
- `PF_Zone_S_Type_A_V2`: `Level_L02`, `Level_B01`
- `PF_Zone_S_Type_Boss_Enter_V2`: `Level_B01`, `Level_B02`

등록 도구는 Stack 직속의 모든 `Level_*`에 `ElevationLevel`을 추가한다. 내용이 없는 Level은 Renderer와 Collider 최소 조건을 충족하지 못해 오류가 된다.

#### 수정 요청

1. 실제 지형이 존재하지 않는 `Level_*` 루트를 제거한다.
2. 실제 지형이 존재하는 층만 Stack 아래에 둔다.
3. 실제 Level 안의 `OccludableProps`와 `LevelOnlyProps`는 비어 있더라도 유지한다.

#### 통과 조건

- 모든 남은 Level의 `Content`에 Renderer가 최소 1개, Collider가 최소 1개 존재한다.
- 내용이 없는 Level이 없다.

### R-04. 중첩 프랍을 V2 Wrapper 구조로 전환

#### 확인된 문제

`OccludableProps`가 참조하는 원본 프리팹은 총 34종이다.

- 34종 모두 `OcclusionSection`이 없다.
- 34종 모두 원본 루트 이름이 `PF_Prop_*`, `PF_Wall_*`, `PF_Hallway_*` 규칙을 만족하지 않는다.
- V2 Zone 인스턴스에서 이름만 `PF_Prop_*`로 Override한 상태다.

현재 등록 도구는 중첩 인스턴스 원본에 `OcclusionSection`이 없으면 상위 Zone을 수정하지 않고 오류를 출력한다. 원본 프리팹 이름도 독립 프랍 규칙에 맞지 않으므로 인계서의 “중첩 프리팹을 먼저 Register-Wire” 절차를 그대로 실행할 수 없다.

#### 수정 요청

레거시 프리팹을 직접 변경하지 않고 다음 구조의 V2 Wrapper를 만든다.

```text
PF_Prop_<Name>_V2 [OcclusionSection]
└─ <LegacyPrefabInstance>
```

1. Wrapper는 새 GUID를 가진 Git 프리팹으로 만든다.
2. Wrapper 루트 이름은 정확히 `PF_Prop_*` 규칙을 사용한다.
3. Wrapper 루트가 Renderer와 Collider 등록 범위 전체를 소유한다.
4. `OccludableProps`에는 Wrapper 프리팹만 직속 자식으로 둔다.
5. `LevelOnlyProps`에는 `OcclusionSection`이 없는 별도 Wrapper를 사용한다.
6. Wrapper 안에 또 다른 `PF_Prop_*` 루트를 만들지 않는다.
7. 원본 레거시 프리팹과 `.meta`는 수정하지 않는다.

#### 통과 조건

- 중첩 원본 등록 오류가 0개다.
- `OccludableProps` 직속 자식마다 루트 `OcclusionSection`이 정확히 1개다.
- `LevelOnlyProps` 하위에는 `OcclusionSection`이 0개다.

### R-05. 영문 ASCII 작명 오류 수정

#### 확인된 문제

11개 최상위 프리팹에서 최소 323개의 이름 Override가 작명 규칙을 위반한다.

대표 예:

- `floor_metal (3)`
- `floor_stone (78)`
- `Wall_Hallway_transparent_006 (1)`

현재 검증은 `Occlusion` 아래 전체 하이어라키를 재귀적으로 검사한다. 공백, 괄호, 하이픈, 한글, Unity 자동 복제 접미사 `(1)`은 모두 오류다.

#### 수정 요청

1. `Occlusion` 아래 모든 GameObject 이름을 영문 ASCII, 숫자, 밑줄만 사용하도록 변경한다.
2. Unity 자동 복제 접미사 `(1)`, `(2)` 등을 `_01`, `_02` 형태로 바꾼다.
3. 공백은 밑줄로 바꾼다.
4. 대소문자를 현재 작명 문서와 정확히 맞춘다.

#### 통과 조건

- 정규식 `^[A-Za-z0-9_]+$`를 위반하는 이름이 0개다.
- `Validate Selected Prefabs`에서 Name 관련 오류가 0개다.

### R-06. Fusebox 분류 수정

#### 확인된 문제

다음 두 프랍이 `LevelOnlyProps`에 들어 있다.

- `PF_Zone_L_Type_A_V2/Occlusion/ElevationStack_01/Level_L01/Content/LevelOnlyProps/PF_Prop_wall_object_fuseboxS`
- `PF_Zone_L_Type_A_V2/Occlusion/ElevationStack_01/Level_L01/Content/LevelOnlyProps/PF_Prop_wall_object_fuseboxL`

인계서의 자체 규칙에서는 Fusebox가 현재 층에서도 투명화되는 `OccludableProps` 대상이다.

#### 수정 요청

두 Fusebox를 `OccludableProps`로 옮기고 V2 Wrapper 루트에 `OcclusionSection`을 등록한다.

#### 통과 조건

- 두 Fusebox가 `OccludableProps`의 직속 자식이다.
- 각각 Renderer와 Collider가 등록된 `OcclusionSection`을 가진다.

### R-07. GUID 충돌 26개를 미해결 상태로 넘기지 않기 — 최우선

#### 확인된 문제

현재 패키지는 다음 충돌을 프로그래머 수동 처리 대상으로 남겨 두었다.

- 동일 GUID·다른 내용 프리팹 25개
- `Generic_01_A.png` 1개

충돌 프리팹 중 18개는 V2 Zone들이 직접 참조한다. 이 상태로 MainProject에 넣으면 기존 메인 프리팹으로 해석되어 소스에서 검수한 외형과 달라질 수 있다.

#### 수정 요청

각 충돌에 대해 다음 중 하나를 확정한다.

1. **메인 기존 에셋 재사용**
   - MainProject의 실제 에셋으로 연결한 상태에서 외형과 Override를 다시 검증한다.
   - `manifest.json`에 `ReuseMain`과 실제 Main 경로를 기록한다.
2. **V2 전용 에셋 사용**
   - 임시 프로젝트의 Unity Project 창에서 복제해 새 GUID를 발급한다.
   - V2 프리팹 참조를 새 GUID 에셋으로 교체한다.
   - Git/SVN 소유권에 맞는 V2 경로에 포함한다.

기존 MainProject 에셋의 GUID나 `.meta`는 변경하지 않는다.

#### 통과 조건

- `manual-review` 또는 `RequiresReferenceSwap: true` 상태가 0개다.
- `DirectImportSafe`가 `true`다.
- 같은 GUID로 내용이 다른 에셋이 0개다.
- 모든 V2 참조가 실제 포함 에셋 또는 Main 재사용 에셋으로 해석된다.

### R-08. Generic_01_A 텍스처·머티리얼 사슬 수정

#### 확인된 문제

`Generic_01_A.png`는 기존 Main 텍스처와 동일 GUID지만 내용이 다르다. 인계서에서는 PNG에 새 GUID를 발급하고 V2 프리팹에서 참조를 교체하라고 되어 있으나, V2 렌더러는 PNG를 직접 참조하지 않는다.

실제 참조 사슬은 다음과 같다.

```text
V2 Renderer
└─ Generic_01_A.mat
   └─ Generic_01_A.png
```

기존 `Generic_01_A.mat`과 기존 디더 머티리얼도 충돌 GUID의 PNG를 참조한다. 새 PNG만 추가하면 사용되지 않는다.

#### 수정 요청

1. 새 GUID의 `Generic_01_A_V2.png`를 만든다.
2. 새 GUID의 `Generic_01_A_V2.mat`을 만든다.
3. V2 머티리얼에서 V2 PNG를 참조한다.
4. V2 전용 디더 머티리얼을 만든다.
5. Source/V2 Dither 머티리얼 쌍을 `WallOcclusionSettings`에 등록할 수 있도록 인계한다.
6. V2 렌더러와 관련 Wrapper가 V2 머티리얼을 참조하게 한다.
7. 기존 Main 머티리얼·텍스처·디더 머티리얼은 수정하지 않는다.

#### 통과 조건

- V2 에셋에서 충돌 GUID `3fc3c11290504854c9e76317f3f97045` 참조가 0개다.
- V2 머티리얼과 디더 머티리얼이 새 V2 텍스처를 사용한다.
- 머티리얼 검증 오류가 0개다.

### R-09. 실제 Unity 검증 보고서 제출

#### 확인된 문제

동봉된 `validation-report.json`은 다음과 같이 비어 있다.

```json
{
  "Reports": []
}
```

따라서 현재 인계물은 패키지 무결성만 검증됐고 MainProject 시스템 검증은 실행되지 않은 것으로 판단한다.

#### 수정 요청

MainProject의 현재 코드가 포함된 깨끗한 검증 브랜치에서 다음 순서로 검증한다.

1. 중첩 V2 Wrapper 프리팹 `Register-Wire`
2. Stage·Zone V3 프리팹 `Register-Wire`
3. Stage·Zone V3 프리팹 `Validate Selected Prefabs`
4. 여러 높이에서 같은 층 벽, 큰 프랍, 위층 전체 투명화 Play Mode 확인
5. 캐릭터가 진입로가 아닌 곳으로 낙하할 때 층 상태 확인
6. MPPM Host/Client에서 동일 동작 확인

#### 통과 조건

- Stage 1개와 Zone 11개 모두 `errors=0`이다.
- 검증 보고서에 프리팹별 Renderer, Collider, XZ Area, Section 수가 기록된다.
- Play Mode 검증 결과와 캡처 또는 영상 경로가 인계서에 기록된다.

## 4. 수정본 권장 하이어라키

실제로 존재하는 층만 만들고, 빈 프랍 분류 컨테이너는 유지한다.

```text
PF_Zone_*
└─ Occlusion
   └─ ElevationStack_01 [ElevationStack]
      ├─ Level_L01 [ElevationLevel + XZ Areas]
      │  └─ Content
      │     ├─ FloorMesh
      │     ├─ WallSection_01 [OcclusionSection]
      │     ├─ WallSection_02 [OcclusionSection]
      │     ├─ OccludableProps
      │     │  ├─ PF_Prop_Container_A_V2 [OcclusionSection]
      │     │  └─ PF_Prop_Fusebox_A_V2 [OcclusionSection]
      │     └─ LevelOnlyProps
      │        ├─ PF_Prop_Railing_A_V2
      │        └─ PF_Prop_Box_Small_A_V2
      └─ Level_L02 [ElevationLevel + XZ Areas]  ← 실제 2층이 있을 때만
         └─ Content
            ├─ FloorMesh
            ├─ WallSection_01 [OcclusionSection]
            ├─ OccludableProps
            └─ LevelOnlyProps
```

Stage도 완전히 동일한 규칙을 사용한다. 기존 `HallwaySection_*`은 유지하되 Section 밖에 있는 가림 벽·복도를 알맞은 Section에 포함한다.

## 5. V3 재인계 패키지 요구사항

수정 패키지는 기존 ZIP과 혼동되지 않도록 새 이름과 새 GUID 정책으로 제출한다.

권장 이름:

`LevelDelivery_LegacySafeV3_<YYYYMMDD_HHMMSS>.zip`

필수 포함 파일:

1. `manifest.json`
2. 실제 결과가 들어 있는 `validation-report.json`
3. `README-PROGRAMMER.txt`
4. `*_PROGRAMMER_HANDOFF.md`
5. Stage V3 프리팹 1개와 `.meta`
6. Zone V3 프리팹 11개와 `.meta`
7. V2/V3 전용 Wrapper 프리팹과 `.meta`
8. 필요한 Git 의존성과 `.meta`
9. 필요한 SVN 아트와 `.meta`
10. 새 GUID의 Generic V3 텍스처·머티리얼·디더 머티리얼
11. Play Mode 검증 증빙 경로
12. 패키지 SHA-256

`manifest.json`에는 다음 상태가 남아 있으면 안 된다.

- `DirectImportSafe: false`
- `RequiresProgrammerReferenceSwap: true`
- `HandoffGroup: manual-review`
- `Status: GuidContentConflict`

## 6. 재검수 체크리스트

- [ ] 실제 존재하는 Level만 포함됨
- [ ] 모든 실제 Level에 XZ Area가 1개 이상 있음
- [ ] 모든 실제 Level의 Content에 Renderer와 Collider가 있음
- [ ] 같은 층 가림 벽·복도가 Section에 포함됨
- [ ] `OccludableProps` 프랍에 루트 `OcclusionSection`이 정확히 1개 있음
- [ ] `LevelOnlyProps` 하위에 `OcclusionSection`이 없음
- [ ] 레거시 원본 대신 규칙에 맞는 V2/V3 Wrapper를 사용함
- [ ] 공백·괄호·한글·하이픈이 들어간 이름이 없음
- [ ] Fusebox가 `OccludableProps`에 있음
- [ ] GUID 충돌 및 미결 Reference Swap이 0개임
- [ ] Generic V3 텍스처·머티리얼·디더 사슬이 독립 GUID를 사용함
- [ ] 기존 Stage1·Zone·Slots·ZoneLayout·Spawn·Nodes를 덮어쓰지 않음
- [ ] Stage·Zone 12개 모두 Register-Wire 후 Validate `errors=0`
- [ ] 같은 층 벽, 큰 프랍, 위층 전체 투명화가 Play Mode에서 동작함
- [ ] MPPM Host/Client 검증을 완료함
- [ ] 검증 결과가 비어 있지 않은 보고서로 동봉됨

## 7. 참고 문서

- `Docs/tech/occlusion-prefab-authoring-manual.md`
- `Docs/tech/occlusion-prefab-naming-rules.md`
- `Docs/tech/stage-zone-prefab-guid-import-manual.md`

