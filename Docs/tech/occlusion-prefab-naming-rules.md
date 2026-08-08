# 가림 투명화 프리팹·하이어라키 작명 규칙

이름은 제작 자동화와 검증에만 사용한다. 런타임은 이름을 읽지 않고 `ElevationStack`, `ElevationLevel`, `OcclusionSection`에 등록된 참조만 사용한다.

새로 만들거나 이번 구조로 전환한 프리팹부터 아래 규칙을 엄격히 적용한다. 영문 ASCII, 숫자, 밑줄만 허용하며 대소문자까지 정확히 일치해야 한다. 한글, 공백, 하이픈, 괄호, Unity 자동 복제 접미사 `(1)`은 허용하지 않는다.

## 1. 고정 이름과 패턴

| 대상 | 규칙 | 예시 |
|---|---|---|
| Stage 루트 | `PF_Stage_*` | `PF_Stage_01` |
| Zone 루트 | `PF_Zone_*` | `PF_Zone_L_Combat_A` |
| 독립 프랍 루트 | `PF_Prop_*` | `PF_Prop_Machine_Generator_A` |
| 독립 벽 루트 | `PF_Wall_*` | `PF_Wall_Outer_Straight_A` |
| 독립 복도 루트 | `PF_Hallway_*` | `PF_Hallway_Corner_A` |
| 공통 작업 루트 | 정확히 `Occlusion` | `Occlusion` |
| 고도 스택 | `ElevationStack_` + 두 자리 숫자 | `ElevationStack_01` |
| 지하 Level | `Level_B` + 두 자리 숫자 | `Level_B01` |
| 지상 Level | `Level_L` + 두 자리 숫자 | `Level_L01`, `Level_L02` |
| Level 내용 루트 | 정확히 `Content` | `Content` |
| 개별 투명화 프랍 분류 | 정확히 `OccludableProps` | `OccludableProps` |
| 현재 층 불투명 프랍 분류 | 정확히 `LevelOnlyProps` | `LevelOnlyProps` |
| 일반 섹션 | `Section_*` | `Section_01` |
| 벽 섹션 | `WallSection_*` | `WallSection_01` |
| 복도 섹션 | `HallwaySection_*` | `HallwaySection_01` |

`B01`, `L01`, `L02`는 사람이 알아보기 위한 층 표기다. 런타임 층 순서는 Level 루트의 실제 월드 Y로 정렬한다.

## 2. 하이어라키 강제 규칙

- Stage/Zone의 `Occlusion`은 루트의 직속 자식이며 정확히 하나다.
- `Occlusion`의 직속 자식은 `ElevationStack_*`뿐이다.
- Level은 Stack의 직속 자식이다.
- `Content`는 Level의 직속 자식이며 정확히 하나다.
- `OccludableProps`와 `LevelOnlyProps`는 Content의 직속 자식이며 각각 정확히 하나다.
- 두 프랍 컨테이너의 직속 자식은 `PF_Prop_*` 루트만 허용한다.
- `PF_Prop_*` 하위에 또 다른 `PF_Prop_*`가 있으면 오류다.
- `OccludableProps/PF_Prop_*` 루트의 `OcclusionSection`은 정확히 하나다. 자식에 붙이면 오류다.
- `LevelOnlyProps/PF_Prop_*` 자신과 하위에는 `OcclusionSection`을 허용하지 않는다.
- `OcclusionSection`끼리 Renderer 또는 Collider를 중복 소유할 수 없다.
- 하나의 오브젝트가 둘 이상의 Level Content에 속할 수 없다.

## 3. 검증 결과 해석

| 검증 상황 | 결과 |
|---|---|
| 필수 이름·노드 누락 | 오류 로그, 빌드는 계속 가능 |
| Content 또는 Section의 Collider/Renderer 최소치 미달 | 오류 로그 |
| 같은 Stack의 Level 기준 Y 중복 | 오류 로그 |
| Stack/Level Scale이 `(1,1,1)`이 아님 | 오류 로그 |
| Stack/Level에 X 또는 Z 회전이 있음 | 오류 로그 |
| 미등록·지원 불가 머티리얼 | 오류 로그, 런타임 불투명 유지 |
| 중첩 프리팹 원본 미등록 | 원본 프리팹을 먼저 등록하라는 오류 |
| 올바르게 등록된 대상 | 이름과 무관하게 컴포넌트 참조로 런타임 동작 |

## 4. AI 프리팹 전환 요청 템플릿

```text
선택한 프리팹만 가림 투명화 구조로 등록·검증해라.

준수 문서:
- Docs/tech/occlusion-prefab-authoring-manual.md
- Docs/tech/occlusion-prefab-naming-rules.md

요구사항:
1. 기존 아트·게임플레이 하이어라키는 보존한다.
2. Stage와 Zone은 동일한 ElevationStack/Level/Content 규칙을 쓴다.
3. Level 루트 Y와 XZ Areas는 제공된 층 범위에 맞춘다.
4. 프랍은 OccludableProps 또는 LevelOnlyProps 중 하나로만 분류한다.
5. 중첩 프리팹 원본은 몰래 수정하지 말고 원본 등록 필요 오류를 보고한다.
6. BoxCollider 기반 데이터 볼륨을 새로 만들지 않는다.
7. Validate 결과와 남은 오류를 프리팹 경로별로 보고한다.
```

AI에게는 각 Level의 기준 Y, XZ 범위, 프랍 분류를 명시해 주는 것이 안전하다. 분류가 불명확한 프랍을 AI가 임의로 결정하게 하지 않는다.

## 5. AI 완료 보고 형식

```text
처리한 프리팹:
- Assets/.../PF_Zone_Example.prefab

등록 결과:
- Stack 1
- Level 3
- Section 7
- Renderer 24
- Collider 12
- XZ Area 4

검증:
- 오류 0 / 경고 0

수동 확인 필요:
- Level_L02 XZ Area가 실제 2층 전체를 덮는지 Scene 뷰 확인
- PF_Prop_Container_A가 OccludableProps 분류가 맞는지 플레이테스트 확인
```

AI가 해서는 안 되는 작업은 기존 아트 프리팹 일괄 변경, 이름만 보고 런타임 등록을 생략하는 것, 중첩 프리팹 원본의 암묵적 수정, 임의 프랍 분류, 물리 BoxCollider를 XZ Area 대신 추가하는 것이다.

