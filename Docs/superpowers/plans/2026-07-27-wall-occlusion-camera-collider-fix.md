# Wall Occlusion Camera and Collider Fix Implementation Plan

> **⚠️ 폐기됨 (2026-07-28).** 이 계획이 만든 구조(WallOcclusionUnit / Manager /
> Collider 기반 판정)는 per-pixel 월드공간 페이드로 전면 교체되어 전부 삭제됐다.
> 이 문서는 이력 보존용이며 현재 코드와 일치하지 않는다.
> 현행 설계: [Docs/tech/wall-occlusion-implementation.md](../../tech/wall-occlusion-implementation.md)
>
> 특히 Task 2~3의 "Collider.ClosestPoint 정밀 판정"은 실제로는 동작하지 않았다.
> 해당 API가 non-convex MeshCollider를 지원하지 않아 AABB로 폴백하는데 맵의 벽
> 콜라이더는 전부 non-convex였고, 테스트는 BoxCollider로만 검증해 이를 놓쳤다.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Additive 로딩 중 잘못된 `Camera.main`을 잡는 문제와 `Renderer.bounds` 기반 벽 판정 오차를 제거해, 현재 아트 프리팹으로도 사용자가 MapScene Play Mode 검증을 할 수 있게 한다.

**Architecture:** `CameraTargetSwitcher`가 직접 생성한 렌더링 카메라를 소유하고 `WallOcclusionProjectBridge`가 이를 `WallOcclusionManager`에 명시적으로 전달한다. 벽 후보의 넓은 범위 필터에는 `Renderer.bounds`를 유지하지만, 실제 시야 차폐는 등록된 기존 Collider에 대한 단일 NonAlloc SphereCast로, 플레이어/장판 근접 거리는 `Collider.ClosestPoint`로 판정한다.

**Tech Stack:** Unity 6000.3.16f1, C#, Unity Physics NonAlloc queries, Cinemachine, NUnit EditMode tests

## Global Constraints

- 작업 브랜치는 `feature/map-player-merge`를 유지한다.
- 신규 프록시 GameObject, 신규 Collider, 신규 레이어를 런타임에 생성하지 않는다.
- 아트에서 이미 저작된 `MeshCollider`만 재사용한다.
- `Assets/50.Art`, `Assets/51.Audio` 및 SVN 관리 `.meta`는 수정하지 않는다.
- `Camera.main` 및 씬 전체 카메라 검색을 가림 처리 경로에서 사용하지 않는다.
- Play Mode와 MPPM 실플레이는 사용자가 수행한다.

---

### Task 1: 정밀 판정 계약을 EditMode 테스트로 고정

**Files:**
- Create: `Assets/Tests/EditMode/Occlusion/WallOcclusionRuntimeTests.cs`
- Create: `Assets/1.Scripts/Rendering/Occlusion/AssemblyInfo.cs`
- Create: `Assets/1.Scripts/Rendering/Editor/WallOcclusionTestRunner.cs`
- Modify: `Assets/Tests/EditMode/Occlusion/WallOcclusionCoreTests.cs`

**Interfaces:**
- Consumes: `WallOcclusionUnit.Configure(...)`, `WallOcclusionManager.SetCamera(Camera)`
- Produces: `WallOcclusionUnit.TryGetClosestPoint(Vector3, out Vector3, out float)`, `WallOcclusionManager.TryGetCamera(out Camera)`

- [x] **Step 1: L자형 Collider의 빈 AABB 영역을 표면 거리로 처리하는 실패 테스트 작성**

```csharp
[Test]
public void TryGetClosestPoint_UsesColliderSurfaceInsideCombinedRendererBounds()
{
    // 두 BoxCollider로 L자를 만들고 AABB 내부 빈 공간의 점을 조회한다.
    // 기대값은 Bounds.ClosestPoint의 0이 아니라 실제 Collider 표면까지의 거리 2.5다.
}
```

- [x] **Step 2: 명시적 카메라가 없을 때 활성 `MainCamera`를 사용하지 않는 실패 테스트 작성**

```csharp
[Test]
public void TryGetCamera_DoesNotFallbackToTaggedMainCamera()
{
    // MainCamera 태그 카메라가 있어도 manager.SetCamera 호출 전에는 false여야 한다.
}
```

- [x] **Step 3: EditMode 테스트를 실행해 새 API 부재로 RED 확인**

Run: Unity Test Runner의 `VeyTrace.Rendering.Occlusion.EditModeTests`

Expected: `TryGetClosestPoint` 및 접근 가능한 `TryGetCamera` 계약이 없어 컴파일 또는 테스트 실패

테스트 실행은 `Tools/Rendering/Wall Occlusion/Run EditMode Tests` 메뉴가 해당 테스트 어셈블리만 실행하고 콘솔에 PASS/FAIL 집계를 출력하게 한다.

- [x] **Step 4: 더 이상 사용하지 않을 AABB 직접 차폐 테스트 제거**

`SegmentIntersectsExpandedAabb_*` 두 테스트는 실제 런타임 경로가 Collider SphereCast로 교체되므로 삭제한다.

### Task 2: 기존 Collider를 WallOcclusionUnit과 관리자에 등록

**Files:**
- Modify: `Assets/1.Scripts/Rendering/Occlusion/WallOcclusionUnit.cs`
- Modify: `Assets/1.Scripts/Rendering/Occlusion/WallOcclusionRuntimeBinder.cs`
- Modify: `Assets/1.Scripts/Rendering/Occlusion/WallOcclusionManager.cs`
- Modify: `Assets/1.Scripts/Rendering/WallOcclusionProjectBridge.cs`

**Interfaces:**
- Consumes: 벽 Renderer와 같은 GameObject에 이미 존재하는 `Collider[]`
- Produces: `WallOcclusionUnit.Colliders`, `WallOcclusionBindingReport`, Collider→Unit 등록 맵

- [x] **Step 1: Unit에 Collider 배열과 실제 표면 최근접점 계산 구현**

```csharp
public void Configure(
    WallOcclusionProxy newProxy,
    RendererBinding[] newBindings,
    Collider[] newColliders);

public bool TryGetClosestPoint(
    Vector3 center,
    out Vector3 closestPoint,
    out float distance);
```

활성 Collider만 순회하고 `Collider.ClosestPoint(center)` 중 최소 제곱거리를 선택한다.

- [x] **Step 2: Binder가 기존 Collider만 수집하고 누락 수를 보고하도록 구현**

```csharp
public readonly struct WallOcclusionBindingReport
{
    public int BoundUnits { get; }
    public int ColliderUnits { get; }
    public int MissingColliderUnits { get; }
}
```

`renderer.GetComponents<Collider>()` 결과를 Unit에 전달하며 Collider가 없는 벽은 정밀 판정에서 제외하고 보고한다.

- [x] **Step 3: 관리자가 Unit 등록 시 Collider→Unit 맵을 구성하고 Clear 시 함께 비우도록 구현**

중복 Collider는 최초 등록 Unit을 유지하고 한 번만 경고한다.

- [x] **Step 4: Bridge 로그에 `units`, `colliderUnits`, `missingColliders`를 출력**

최종 아트 프리팹 적용 후 `missingColliders=0` 여부를 사용자가 즉시 확인할 수 있게 한다.

- [x] **Step 5: 새 EditMode 테스트를 실행해 GREEN 확인**

Expected: L자형 Collider 빈 공간의 표면 거리가 2.5이고 Collider가 비활성이면 조회가 실패한다.

### Task 3: 직접 차폐와 근접 판정을 Collider 기반으로 교체

**Files:**
- Modify: `Assets/1.Scripts/Rendering/Occlusion/WallOcclusionManager.cs`
- Modify: `Assets/1.Scripts/Rendering/Occlusion/WallOcclusionSettings.cs`
- Modify: `Assets/99.Settings/WallOcclusionSettings.asset`

**Interfaces:**
- Consumes: Collider→Unit 맵, `WallOcclusionUnit.TryGetClosestPoint`
- Produces: 카메라→플레이어 단일 `Physics.SphereCastNonAlloc`, 실제 Collider 표면 기반 근접 투명도

- [x] **Step 1: 재사용 RaycastHit 버퍼와 용량 설정 추가**

```csharp
[Range(8, 256)] public int directHitBufferSize = 64;
```

버퍼는 설정 변경 시에만 재할당하고 프레임별 GC를 만들지 않는다.

- [x] **Step 2: 감지 주기당 SphereCastNonAlloc 1회로 직접 차폐 Unit 제출**

카메라에서 플레이어 방향으로 `directCastRadius`의 SphereCast를 수행하고, Collider→Unit 맵에 등록된 hit만 `Direct` 채널로 제출한다. 버퍼가 가득 찬 경우 한 번만 경고한다.

- [x] **Step 3: 근접 판정의 정밀 단계에서 Collider.ClosestPoint 사용**

`Renderer.bounds.SqrDistance(center)`는 `outerRadius + hysteresis` 범위의 broad phase에만 쓰고, 최종 거리와 카메라 방향 dot은 Unit의 실제 최근접 Collider 표면점을 사용한다.

- [x] **Step 4: 기존 AABB 직접 교차 런타임 경로 제거**

`IntersectsDirectSightLine`과 그 호출을 삭제하고, 직접 차폐는 물리 쿼리 결과만 신뢰한다.

- [x] **Step 5: 전체 Occlusion EditMode 테스트 실행**

Expected: 모든 테스트 통과, 새 경로에서 프레임별 관리 배열 할당 없음

### Task 4: 게임 카메라를 생성 지점에서 명시적으로 전달

**Files:**
- Modify: `Assets/1.Scripts/Camera/CameraTargetSwitcher.cs`
- Modify: `Assets/1.Scripts/Rendering/WallOcclusionProjectBridge.cs`
- Modify: `Assets/1.Scripts/Rendering/Occlusion/WallOcclusionManager.cs`

**Interfaces:**
- Produces: `CameraTargetSwitcher.GameplayCamera`
- Consumes: `WallOcclusionManager.SetCamera(Camera)`

- [x] **Step 1: EnsureCameraRig에서 mainCameraPrefab 인스턴스의 Camera 캐시**

```csharp
public Camera GameplayCamera => gameplayCamera;
```

`Instantiate(mainCameraPrefab, transform)` 반환 인스턴스의 자식까지 검색해 실제 렌더링 `Camera`를 저장하고, 없으면 경고한다.

- [x] **Step 2: Bridge가 매 LateUpdate에 Switcher의 카메라와 타깃을 함께 전달**

Switcher가 없거나 카메라가 파괴되면 `SetCamera(null)`로 만들어 잘못된 씬 카메라 사용을 막는다.

- [x] **Step 3: Manager의 Camera.main 폴백 제거**

명시적으로 받은 카메라가 활성 상태일 때만 감지하고, 그 외에는 기존 투명 상태를 복원한다.

- [x] **Step 4: 카메라 EditMode 테스트를 실행해 GREEN 확인**

Expected: 태그된 MainCamera만 존재할 때 false, `SetCamera(explicitCamera)` 후 true, 비활성 카메라는 false

### Task 5: 문서·컴파일·사용자 Play 검증 인계

**Files:**
- Modify: `Docs/tech/wall-occlusion-runtime-bounds.md`
- Modify: `PLAN.md`

**Interfaces:**
- Produces: 최종 아트 재적용 절차와 Play Mode 확인 체크리스트

- [x] **Step 1: 설계 문서를 승인 구조로 갱신**

카메라 소유권, broad/precise phase, Collider 누락 정책, 성능 특성을 기록한다.

- [x] **Step 2: Unity 스크립트 재컴파일**

Expected: C# compile error 0

- [x] **Step 3: Occlusion EditMode 테스트 전체 실행**

Expected: 전체 통과

- [x] **Step 4: 콘솔에서 신규 오류 확인**

Expected: WallOcclusion 관련 Error/Exception 0. 기존 RuntimeNavMeshBuilder non-readable mesh 오류는 별도 이슈로 명시한다.

- [x] **Step 5: 사용자 Play Mode 확인 항목 전달**

MapScene 생성 후 binding 로그의 `missingColliders`, 벽 뒤 플레이어 가시성, L/ㄱ자 코너의 과투명 여부, 장판 범위 가시성, 카메라 전환 후 정상 복원을 확인한다. 최종 아트를 받으면 Collider authoring 도구를 다시 실행하고 동일 체크를 반복한다.
