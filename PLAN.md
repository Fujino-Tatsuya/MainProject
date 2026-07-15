# PLAN — 맵 콜라이더 + Paladin 스폰 + E2E (feature/map-player-merge, 2026-07-15)

목표: lobby 접속 → 맵 생성 → 3플레이어 전부 **Paladin**으로 스폰 → **이동까지 문제없음**.

## 확정된 사실 (조사 완료)
- 맵 = **시드 기반 결정론적 로컬 생성**(서버·클라 각자 동일 `Instantiate`, NGO 복제 아님, `MapContentSpawner.cs:46`). 존 프리팹에 NetworkObject 없음 → **콜라이더는 프리팹/메시에 baked면 모든 피어 자동 적용**(서버 전용 분기만 피하면 됨).
- 스폰 = `NetworkLoadingFlowController.SpawnPlayerForClient` → `SpawnAsPlayerObject`. 프리팹 = `NetworkManager.prefab`의 `defaultPlayerPrefab` 2곳(NetworkSessionLauncher@124 + NetworkLoadingFlowController@143).
- Paladin.prefab = 완전한 독립 플레이어 루트(NetworkObject+Player.cs+이동/입력/공격). **단 `PlayerDefaultAttack`(3c80e0a7) 누락**(Player.prefab엔 있음).

## 작업 항목

### 1. Paladin 스폰 직결 — ✅ 완료 (커밋 대기)
- `NetworkManager.prefab` `defaultPlayerPrefab` 2곳: CameraTestPlayer(stale) → **Paladin**(`af4a760f…`, fileID 8559504096609571310) 교체 완료. 검증: Paladin참조 2 / CameraTestPlayer 0.

### 2. 바닥/벽 콜라이더 — 방법 재검토 (규모: 존당 floor/wall 메시 ~41개 × 12존 = ~480)
- **(강력 권장) fbx-import baked**: floor(28)+wall(14) fbx `.meta`의 `addColliders: 0→1` + 재임포트. 한 번에 모든 메시에 MeshCollider baked → 전 존 인스턴스·전 피어 자동. non-readable 메시도 import 시 baked라 안전. **자동화 가능(.meta 편집).** fbx=SVN(50.Art)라 SVN 커밋 필요.
- (사용자 최초 선택) 존/벽 프리팹 자식에 직접 MeshCollider: ~480개 수작업, MCP 다운으로 자동화 불안정. 비권장.
- 타입: 정적 맵 = non-convex MeshCollider OK. 플레이어(Rigidbody+CapsuleCollider)가 딛음.

### 3. Paladin PlayerDefaultAttack 누락 — 이동 테스트엔 선택
- Player.prefab의 `PlayerDefaultAttack` 복사 + `DefaultAttackController.playerDefaultAttack` 배선. **공격에 필요, 이동엔 불필요.** ⚠️단 DefaultAttackController가 Awake/Start에서 null 접근하면 NRE로 플레이어 전체 깨질 수 있음 → 스폰 후 콘솔 확인 필수. 필요시 은희와 처리.

### 4. E2E 검증 — MPPM 3클라
- lobby(Temp_LobbyScene) → StartHost + 클라 ready → LoadingScene → MapScene → 3 Paladin 스폰 → 이동. 콘솔 0 에러.
- 지원: curl로 컴파일 상태·플레이 콘솔 로그 확인(MCP node 클라 죽어 curl 경유).

## 순서
2(콜라이더) → 1·2 커밋 → 3(선택) → 4(검증). 통과 후 development 푸시 → feature/map 정리 → 새 브랜치.

## 미결 결정
- 콜라이더 방법: **fbx-import(권장, 내가 자동화)** vs 프리팹 수작업(사용자 Unity). ← 이거 정하면 2 착수.
