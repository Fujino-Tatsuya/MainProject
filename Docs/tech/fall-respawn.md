# 낙하 리스폰 설계 (보류 — 추후 작업)

> 2026-06-24. 플레이어/몬스터 작업 중이라 **코드 미적용**. HP 시스템 연동 이후 구현.
> 본 문서는 합의된 설계 + 1차 구현(원복됨) 스냅샷이라 그대로 다시 넣으면 됨.

## 요구사항 (경석 확정)
- 바닥 구멍/물에 빠지면 **사망 아님** → **최대 체력의 15% 데미지** 후 **위치 복구**.
- 리스폰은 **떨어진 그 존 내부**로 제한 (가장 가까운 땅 계산 금지 → 보스방 등 오스폰 방지).
- "스폰 가능 레이어"(바닥 'Ground' 레이어)를 탐색해 그 존 안의 안전 지점으로 복귀.

## 설계 (탐색 근거)
- 플레이어 이동 = **owner-authoritative** NetworkTransform(`Player.prefab` AuthorityMode:1) → **텔레포트는 오너 클라가** transform/rb.position 세팅(자동 동기화). NetworkTransform.Teleport() 사용 권장(보간 끊김 방지).
- 데미지 = **서버권한** (`Unit.cs` `TakeDamage(int)` server-only, `TakeDamageRpc(int)` owner→server). 15% = `_health.MaxHp * 0.15`.
- 현재 존 추적 = 존마다 **영역 트리거**(ZoneTrigger, ZoneSlot.Footprint 크기) + 플레이어 OnTriggerEnter/Exit.
- 안전 지점 = ① 현재 존 XZ 안에서 마지막 grounded 위치(`ContainsXZ`로 존 구속) → ② 폴백: 존 중심 down-raycast(GroundMask).

## 구성요소 (원복된 1차 구현)
1. `Map/ZoneTrigger.cs` (신규): `[RequireComponent(BoxCollider)]`, `Zone`/`Footprint`, `Init(zone, footprint, height=60)`, `ContainsXZ(worldPos)`. MapContentSpawner가 존 인스턴스에 부착.
2. `Player/FallRespawn.cs` (신규, NetworkBehaviour): `FallYBelowZone`/`GroundMask`/`DamageRatio=0.15`/`RespawnCooldown`. Update에서 owner만 낙하 판정→`Respawn()`(텔레포트+`Unit.FallDamageRpc`), OnTriggerEnter/Exit로 `_currentZone` 추적, 쿨다운으로 RPC 연타 방지.
3. `Unit.cs`에 `[Rpc(SendTo.Server)] FallDamageRpc(float ratio)` → `TakeDamage(RoundToInt(_health.MaxHp*ratio))`. (⚠️Unit.cs는 CP949 — 편집 시 바이트 보존)
4. `MapContentSpawner.SpawnPlacements`에서 각 존에 `ZoneTrigger.AddComponent`+`Init(layout, slot.Footprint)`.

## 적용 전 선행조건 / 주의 (리뷰 지적)
- ⚠️ **HP 미연동**: `Unit.Initialize(...)`가 아직 호출 안 됨 → `_health` null → 낙하 데미지 no-op. **플레이어 OnNetworkSpawn(서버)에서 HP 주입** 후라야 데미지 작동.
- `FallRespawn`를 `Player.prefab`(NetworkObject 루트)에 부착 + `GroundMask`(바닥 'Ground' 레이어) 지정. 바닥 메시를 그 레이어로.
- 비키네마틱 RB 텔레포트: `rb.position`만 세팅 + `NetworkTransform.Teleport()` 권장. CollisionDetection을 ContinuousDynamic으로 올려 관통 방지.
- ZoneTrigger 큰 박스가 몬스터/노드 트리거와 간섭 가능 → 전용 자식 GO + 전용 레이어(Layer Collision Matrix) 권장.
- 인접 존 겹침 시 `_currentZone` tie-break: `ContainsXZ` + OnTriggerExit로 처리(원복본 반영).

## 현재 상태
- 위 코드는 **원복(삭제)됨**. `NodeMarker`/`ZoneLayout.Nodes`/노드 스폰/`WaterScroll`(물 비주얼)은 유지.
- 재구현 시 본 문서의 구성요소를 그대로 추가하면 됨.
