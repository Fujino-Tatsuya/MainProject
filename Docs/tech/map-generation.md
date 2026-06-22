# 맵 생성 (Map Generation) — v2: 사전 디자인 존 + 위치 셔플

> 담당: **경석**. 코드 위치: `Assets/1.Scripts/Map/`.
> 네트워크 권한 모델 [networking.md](networking.md), 데이터주도 원칙 [architecture.md](architecture.md), 레벨/난이도 설계 [design/level-system.md](../design/level-system.md).
> **상태: v2 코드 완료(워크트리 — 미커밋·미컴파일), Unity 씬/프리팹 작업 대기.** 구 절차생성 모델은 **폐기**(2026-06-19 재작성). 옛 설계는 git 이력 참조.

## 0. 스코프 / 결정 로그 ★ (v2)
- **Stage1 = 스켈레톤.** 외곽 프레임 + 존↔존 **연결 다리/통로(공유·고정)** + **슬롯 앵커(`ZoneSlot`)** 만. **바닥·벽·테마는 각 존 프리팹으로 이관**(테마별로 다름).
- **존 = 미리 레벨디자인한 프리팹(`ZoneLayout`).** 바닥·벽·테마·노드(1/2/3티어)·몬스터 스폰위치·몬스터 그룹을 전부 포함한 완성 디자인.
- **생성 = 위치 셔플.** 같은 크기(대/중/소) 전투 존끼리 디자인을 **위치만 랜덤 배치**(고정 세트, 풀에서 뽑는 게 아님). 역할 존(보스 / 스폰=상점 / 퀘스트)은 **단일 고정 디자인**.
- **장애물 폐기.** 절차적 노드/장애물 배치 없음 — 전부 디자인에 포함.
- **카운트 데이터 주도.** 슬롯 수·디자인 수를 코드에 안 박음(Stage1 `ZoneSlot` + 카탈로그에서 유도). 기획이 디자인에서 조절.
- **난이도 = 디자인 세트 선택.** 서버 소유 `DifficultyLevel`(Ascension+Stage)로 (Size, Difficulty) 전투 풀을 고름. 마릿수 스케일 폐기(몬스터 디자인 내장).
- **NGO:** 시드/난이도 **NetworkVariable** 복제 → 서버/클라 동일 결정 생성. 존 비주얼=양쪽 로컬(같은 시드), 몬스터=서버 `NetworkObject` Spawn→복제. 데이터 핸드오프 없음.

## 1. 목적
Stage1 지오메트리는 고정, 매 런마다 **어떤 디자인 존이 어느 슬롯에 오는지만** 셔플. "위치만 달라지고 컨셉/디자인은 고정."

## 2. 데이터 모델 (v2)
| 타입 | 역할 |
|------|------|
| `ZoneSlot` (MB) | Stage1 슬롯 앵커 — `SlotID`·`Size`·`Footprint`·역할후보 플래그·런타임 `AssignedRole`/`IsFilled` |
| `ZoneLayout` (MB) | 존 프리팹 루트 — `Size`/`Role`/`Difficulty` 태그 + `MonsterGroupID`·몬스터 스폰 마커·소켓 |
| `ZoneLayoutCatalogSO` | (Size×Difficulty) 전투 풀 + 역할 고정 디자인 조회(`GetCombatPool`/`GetRoleLayout`) |
| `LayoutPlacer` (MB) | 역할존 고정 + 전투존 크기별 풀 셔플 1:1 → `ZonePlacement[]` |
| `MapGenerator` (MB) | 슬롯 수집 → 역할 배정 → `LayoutPlacer` → 스폰. `Generate(seed, int level)` |
| `MapContentSpawner` (MB) | 존 비주얼(양쪽 로컬) + 몬스터(서버 Spawn) |
| `MapNetworkSync` (NB) | 서버 시드/난이도 결정 → `NetworkVariable` 복제 → 양쪽 `Generate` |
| `MapOverviewUI` (MB) | M키 오버뷰 (슬롯 사각형 by 역할 + 아이콘) |
| `MapGenConfigSO` | 몬스터 그룹 풀 + (구 스캐터용) 배제반경 |

**유지(에디터 호환·런타임 미사용):** `ZoneVolume`/`SpawnPoint`/`ZoneDefinitionSO`/`GeneratedNodeData`(구 절차모델), `MapPrefabCatalogSO`(역할 아이콘·지오메트리 빌드), `MapCorridors`/`MapGeometryBuilder`/`MapSpawnPointScatter`(Stage1 지오메트리 저작 툴).
**삭제:** `NodePlacer`·`ObstaclePlacer`·`DifficultyTableSO`.

## 3. 존 모델
| 크기 | 정체 | 역할 |
|------|------|------|
| 대형 | 1티어 노드 포함 | 전투 |
| 중형 | 2칸(가로2/세로2) | 퀘스트 후보 → 1 퀘스트, 나머지 전투 |
| 소형 | — | 보스방 / 스폰(=상점) 후보 |
- 역할 후보 = `ZoneSlot`의 `IsQuestCandidate`/`IsBossCandidate`/`IsSpawnCandidate`. 개수는 씬 배치가 결정.
- 예시 디자인(기획 조절): 중형 = 2티어 1 + 3티어 5, 소형 = 2티어 1 + 3티어 3.

## 4. 파이프라인 — `Generate(seed, level)`
1. **슬롯 수집**: 씬 `ZoneSlot` → `SlotID`(동률 시 위치 2차 키) 정렬 = 결정성. 중복 SlotID는 `LogError`.
2. **역할 배정**: 퀘스트 → 보스 → 스폰 (후보 중 1, 단일 RNG).
3. **레이아웃 선택**(`LayoutPlacer`): 역할존 = 고정 디자인 / 전투존 = 크기별 (Size, level) 풀 시드 셔플 → 같은 크기 슬롯에 1:1.
4. **스폰**(`MapContentSpawner`): 존 비주얼 양쪽 로컬 + 몬스터 서버 Spawn(마커 위치).
> 결정성(NGO 디싱크 방지): 슬롯 정렬 + 크기 고정 순회(L→M→S) + 단일 주입 RNG.

## 5. 난이도 ([design/level-system.md](../design/level-system.md) §3)
- `DifficultyLevel = Ascension + StageIndex*StageStep` (서버, `MapNetworkSync`에서 합성).
- catalog의 (Size, Difficulty) 전투 풀을 골라 셔플 → **난이도 = 디자인 세트 선택**. 마릿수 스케일·`DifficultyTableSO`는 **폐기**(몬스터는 디자인 내장).
- 초기엔 단일 난이도(Difficulty=0) 세트로 시작, 밴드는 카탈로그에 디자인 추가로 확장.

## 6. 런타임 / 네트워킹
1. NGO가 MapScene 로드 → `MapNetworkSync.OnNetworkSpawn`.
2. **서버**: 시드 결정 + 난이도 합성 → `NetworkVariable(_seed/_difficulty/_ready)` 기록 + 로컬 `Generate`.
3. **클라**: `_ready` 복제 시 동일 시드로 `Generate` — **동시 시작·레이트 조인 모두 대응**.
4. 존 비주얼 = 양쪽 로컬(같은 시드 → 동일), 몬스터 `NetworkObject` = 서버 Spawn → 복제.
> 노드/존 클리어 상태 동기화는 클리어 시스템 붙을 때(§8).

## 7. 오버뷰 UI (M키)
슬롯 사각형(역할별 색: 전투=회청/보스=빨강/스폰=초록/퀘스트=노랑, `Footprint` 크기) + 역할 아이콘(카탈로그). `MapGenerator.Slots`/`GetRoleSlot` 기반, 열 때 재드로우(서버/클라 동일 시드라 동일).

## 8. 상태 / 남은 일

### v2 코드 (완료 — 워크트리, 미커밋·미컴파일)
- 신규 `ZoneSlot`·`ZoneLayout`·`ZoneLayoutCatalogSO`·`LayoutPlacer`(+`ZonePlacement`), `ZoneSize` enum.
- 재작성 `MapGenerator`·`MapContentSpawner`·`MapOverviewUI`·`MapNetworkSync`(NetworkVariable), 슬림 `MapGenConfigSO`, 수정 `MapSceneSetup`·`MapDevTools`. 삭제 `NodePlacer`·`ObstaclePlacer`·`DifficultyTableSO`.
- 정적 검증(워크플로 18 에이전트) → 결함 5건 수정: `.cs.meta` 4종 GUID 핀, 몬스터 재생성 누수, SlotID 안정 정렬+중복검출, NetworkVariable 레이트조인, mapGenerator null 가드.

### Unity 작업 (경석, 메인 체크아웃 — 이 브랜치 체크아웃 후)
1. **`ZoneSlot` 배치** — 보조: `VeyTrace/Map/v2 ① Create ZoneSlots from ZoneVolumes`(기존 ZoneVolume에서 자동 생성) → 각 슬롯 **Size(대/중/소) 보정** + 역할후보·`Footprint`·앵커 방향 + **고유 `SlotID`** 확인. (수동: 빈 GameObject + `ZoneSlot` 컴포넌트)
2. **`ZoneLayout` 프리팹 제작**: 루트에 `ZoneLayout` + 바닥/벽/테마/노드 + 몬스터 스폰 마커(`MonsterSpawnPoints`)/소켓(`Sockets`) + Size·Role·Difficulty·`MonsterGroupID` 태그. **같은 크기 = 통일 풋프린트·출입구 좌표**(다리 자동 정렬).
3. **`ZoneLayoutCatalog.asset`** — 보조: `VeyTrace/Map/v2 ② Build ZoneLayout Catalog from Prefabs`(프리팹 태그 기준 자동 등록·재실행 멱등). (수동: `Create → VeyTrace/Zone Layout Catalog` + Entries)
4. `VeyTrace/Map/Setup Scene Generator`(LayoutPlacer·카탈로그 배선) → **`MapGenerator` 우클릭 `▶ Test Generate`** 검증(콘솔 로그·M키 오버뷰) → 씬 저장.

### 미정
- 표준 소켓 규약 실좌표(기존 Stage1 다리 위치 기준), 클리어 상태 네트워크 동기화, 난이도 밴드 확장, 몬스터 에셋/그룹 실데이터.
