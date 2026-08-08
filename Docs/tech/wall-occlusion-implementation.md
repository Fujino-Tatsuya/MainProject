# 화면 공간 캡슐 기반 가림 투명화 구현

## 1. 목적과 최종 동작

이 시스템은 각 로컬 게임 카메라에서 카메라와 플레이어 사이를 실제로 가리는 등록 구조물만 선택한다. 선택된 Renderer는 투명 블렌딩이 아니라 디더 컷아웃 셰이더로 교체되며, 화면에 투영된 플레이어 기본 충돌 캡슐의 중심은 완전히 비우고 바깥 테두리만 디더 그라데이션으로 연결한다.

고도 구조에서는 현재 Level보다 높은 Level의 등록 Collider가 SphereCast에 맞으면 그 Level의 `Content` 전체를 같은 마스크로 처리한다. 현재 Level과 아래 Level은 그룹 처리하지 않고 `OcclusionSection` hit만 개별 처리한다.

최종 활성 조건은 다음 하나다.

```text
LevelAboveAndGroupHit || SectionHit
```

이 기능은 로컬 시야 표현이므로 네트워크로 동기화하지 않는다. 각 카메라가 자기 follow target을 기준으로 독립 계산한다.

## 2. 런타임 흐름

`WallOcclusionDriver.LateUpdate()`가 카메라당 매 프레임 한 번 실행된다.

1. `CameraTargetSwitcher`에서 gameplay camera와 현재 follow target을 얻는다.
2. follow target의 기본 non-trigger `CapsuleCollider`를 찾는다.
3. `PlayerGroundingSensor`와 캡슐 발 Y를 이용해 Stack별 현재 Level을 갱신한다.
4. 캡슐을 화면 공간 선분과 반경으로 투영해 전역 셰이더 값으로 전달한다.
5. 카메라에서 캡슐 중심까지 `Physics.SphereCastNonAlloc`을 한 번 실행한다.
6. 맞은 모든 Collider를 `WallOcclusionRegistry`에서 Level 소속과 Section 소유로 각각 조회한다.
7. 위층 Level group hit와 Section hit의 Renderer 합집합을 만든다.
8. `WallOcclusionRendererController`가 머티리얼 변형과 전환 강도를 적용하고 해제 대상을 복원한다.

SphereCast는 고정 배열을 재사용한다. 버퍼가 가득 차면 세션 중 한 번 경고하고 `WallOcclusionSettings.maxCastHits`를 늘리도록 안내한다. Trigger와 플레이어 자신의 Collider는 제외한다.

## 3. 등록 데이터

### ElevationStack

서로 연결된 고도 구조의 Level 목록을 묶는다. Level은 루트 월드 Y로 정렬된다. 같은 Stack에서 같은 기준 Y는 허용하지 않는다. 멀리 떨어진 독립 구조는 별도 Stack으로 나눈다.

### ElevationLevel

다음 데이터만 가진다.

- 직속 `Content` 참조
- Content 하위 Renderer 배열
- Content 하위 Collider 배열
- 데이터 전용 로컬 XZ Area 목록

Content Collider는 위층 group hit 판정과 접지 Level 확인에 사용된다. Content Renderer에는 바닥·벽·프랍이 모두 포함되므로 위층 처리 때 프랍만 떠 보이는 문제가 없다.

### OcclusionSection

현재 층에서 개별 처리할 벽·복도·큰 프랍의 Renderer와 Collider를 소유한다. 하나의 Collider는 Level 소속과 Section 소유를 동시에 가질 수 있다. 두 정보는 중복이 아니라 각각 위층 전체 처리와 현재 층 개별 처리를 담당한다.

같은 Renderer나 Collider를 둘 이상의 Section이 소유하는 것은 오류다. 유효하지 않은 Level 또는 Section은 레지스트리에 들어가지 않고 불투명으로 남으며 원인 경고를 한 번 출력한다.

## 4. 현재 Level 판정

`ElevationStackState`는 Stack별 로컬 상태이며 저장하거나 네트워크로 전송하지 않는다.

- 플레이어 중심이 Stack의 XZ Area 중 하나 안에 있을 때만 현재 Level을 가진다.
- `PlayerGroundingSensor.GroundCollider`가 등록 Level Collider라면 그 Level을 최우선으로 즉시 사용한다.
- 등록된 접지 Level을 얻지 못한 경우 발 Y와 이동 상태를 사용한다.
- 최초 진입은 인접한 아래→위 높이의 20%를 기준으로 초기화한다.
- 접지 상승은 20%에서 위 Level로 전환한다.
- 공중 상승은 점프·넉백으로 보고 현재 Level을 유지한다.
- 하강은 접지 여부와 관계없이 위→아래 진행 60%에서 아래 Level로 전환한다.
- 한 프레임에 여러 Level 경계를 넘으면 연속 평가한다.
- 플레이어가 Stack의 모든 XZ Area 밖에 있으면 그 Stack의 모든 Level을 위층 후보로 취급한다. 실제 투명화에는 여전히 해당 Level Collider의 SphereCast hit가 필요하다.

경사로·계단은 도착하는 높은 Level Content에 포함한다. 별도 전환 볼륨이나 물리 BoxCollider는 사용하지 않는다.

## 5. 화면 공간 캡슐 셰이더

`WallOcclusionGlobals`는 gameplay camera 기준의 다음 값을 전달한다.

- 캡슐 두 끝점의 화면 픽셀 좌표
- 완전 컷아웃 반경과 feather 폭
- gameplay camera view-projection 행렬과 pixel rect
- 플레이어 깊이와 뒤쪽 보호 falloff
- per-renderer 전환 강도 `_WallOcclusionStrength`

`WallOcclusionDither.shader`는 월드 위치를 gameplay camera 화면 좌표로 다시 투영한다. 따라서 Forward, DepthOnly, DepthNormals, ShadowCaster가 같은 마스크를 사용한다. 중심 반경은 완전히 clip하고 바깥 feather에서만 디더 임계값이 변화한다. 플레이어보다 뒤쪽인 픽셀은 깊이 제한으로 불투명하게 보호한다.

기존 바닥 노멀과 플레이어 높이 추정 기반 `Floor Guard`는 제거했다. 셰이더의 역할은 선택된 Renderer 안에서 플레이어 캡슐 주변 어느 픽셀을 버릴지 정하는 것이다. 어떤 Renderer를 선택할지는 SphereCast와 등록 데이터가 담당한다.

## 6. 머티리얼 수명 주기

`WallOcclusionSettings`는 원본 머티리얼과 디더 변형 머티리얼의 명시적 쌍을 가진다. 구형 런타임 자동 바인더와 호환 모드는 없다.

선택된 Renderer는 공유 디더 변형으로 교체하며, 전환 중에는 `MaterialPropertyBlock`으로 강도만 바꾼다. 원래 shared materials와 기존 property block을 저장했다가 완전히 복원한다.

기본 시간은 다음과 같다.

- 활성화: 0.1초
- hit 해제 유예: 0.1초
- 불투명 복원: 0.2초

Renderer의 머티리얼 슬롯 중 하나라도 등록 쌍이 없으면 해당 Level/Section 전체를 안전하게 불투명으로 제외하고 소유자 기준 한 번만 경고한다.

## 7. 제작 도구

에디터 메뉴:

- `Tools/Rendering/Wall Occlusion/Register-Wire Selected Prefabs`
- `Tools/Rendering/Wall Occlusion/Validate Selected Prefabs`
- `Tools/Rendering/Wall Occlusion/Dump Shader Messages`

도구는 선택한 프리팹 에셋 또는 프리팹 인스턴스만 처리한다. Stage/Zone에서는 `Occlusion` 하위 Renderer만 머티리얼 대상으로 본다. 중첩 프리팹 원본은 수정하지 않으며 원본을 먼저 등록하도록 오류를 낸다.

검증 항목은 고정 이름, 필수 노드, 직속 프랍 루트, 중첩 `PF_Prop_*`, 컴포넌트 위치, 중복 Collider 소유, 다중 Level 소속, 기준 Y 중복, transform 제한, 최소 Renderer/Collider, XZ Area, 머티리얼 등록이다. 오류는 프리팹 경로와 전체 하이어라키 경로를 출력하지만 빌드를 막지 않는다.

상세 제작법과 명명법은 다음 문서를 따른다.

- [가림 투명화 프리팹 제작 매뉴얼](occlusion-prefab-authoring-manual.md)
- [가림 투명화 프리팹·하이어라키 작명 규칙](occlusion-prefab-naming-rules.md)

## 8. 주요 파일

| 역할 | 파일 |
|---|---|
| 카메라별 선택과 상태 갱신 | `Assets/1.Scripts/Rendering/WallOcclusionDriver.cs` |
| Stack·Level·Section·XZ Area | `Assets/1.Scripts/Rendering/Occlusion/OcclusionAuthoringComponents.cs` |
| 현재 Level 상태 | `Assets/1.Scripts/Rendering/Occlusion/ElevationStackState.cs` |
| Collider 등록 조회 | `Assets/1.Scripts/Rendering/Occlusion/WallOcclusionRegistry.cs` |
| 머티리얼 전환·복원 | `Assets/1.Scripts/Rendering/Occlusion/WallOcclusionRendererController.cs` |
| 전역 셰이더 데이터 | `Assets/1.Scripts/Rendering/Occlusion/WallOcclusionGlobals.cs` |
| 전역 튜닝 | `Assets/1.Scripts/Rendering/Occlusion/WallOcclusionSettings.cs` |
| 디더 셰이더 | `Assets/3.Materials/Level1_Materials/Occlusion/WallOcclusionDither.shader` |
| 선택 프리팹 등록·검증 | `Assets/1.Scripts/Rendering/Editor/WallOcclusionAuthoring.cs` |
| XZ Area Scene 핸들 | `Assets/1.Scripts/Rendering/Editor/ElevationLevelEditor.cs` |
| EditMode 테스트 | `Assets/Tests/EditMode/Occlusion/WallOcclusionRuntimeTests.cs` |

## 9. 검증 기준

- EditMode 테스트: XZ 회전, Level/Section 이중 등록, 접지 Level 우선, 최초 20%, 상승 20%, 공중 상승 유지, 하강 60%, 영역 이탈, 머티리얼 복원.
- Shader: Forward/DepthOnly/DepthNormals/ShadowCaster 컴파일 오류 없음, Floor Guard 잔존 참조 없음.
- 수동 Play: 1층에서 실제로 가리는 2층 Content 전체가 함께 뚫리고, 2층에서는 OccludableProps만 개별 처리되며 LevelOnlyProps는 불투명인지 확인.
- 수동 Play: 계단 상승, 하강, 2층에서 1층으로 직접 낙하, 점프·넉백, 카메라 전환, Soul/관전 대상을 확인.
- MPPM: Host와 Client가 각자 자기 카메라 기준으로 독립적인 결과를 얻는지 확인.
