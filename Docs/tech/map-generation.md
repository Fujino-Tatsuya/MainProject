# 맵 생성 (Procedural Map Generation)

> 담당: **경석**. 마감 1차: **2026-06-17**. 코드 위치: `Assets/1.Scripts/Map/`.
> 네트워크 권한 모델은 [networking.md](networking.md), 데이터주도 원칙은 [architecture.md](architecture.md) 참조.
> **상태: 설계안 — 리뷰 요청 중(코딩 대기).**

## 0. 스코프 / 결정 로그 ★
- **기본 맵 = 단일 프리팹(`Stage1`)으로 미리 제작·저장.** 고정 지형(외벽·바닥·방 구조) + `SpawnPoint` 마커 + 몬스터 스폰 위치를 **전부 프리팹에 포함**. 생성기는 이 맵의 **내부 콘텐츠(노드/장애물/몬스터/역할)만** 매번 바꾼다.
- **데이터 핸드오프 없음.** NGO는 **서버에서 `NetworkObject` 스폰 시 자동 동기화** → 별도 데이터 복제 프로토콜 불필요. **경석이 맵 시스템 전체를 구현**: ① 생성 로직, ② 서버 스폰, ③ 디버그/오버뷰 맵 UI.
- **통합:** 로딩씬(**은희, 오늘 전달 예정**)에서 내 맵을 spawn → 끝나는 구조.
- **전부 직렬화:** 수치·위치·후보 전부 `[SerializeField]`/SO로 노출 → 인스펙터 실시간 튜닝.
- **몬스터 = 지금은 데이터·스폰위치만.** 실제 몬스터 프리팹/스폰은 몬스터 기획·에셋 확정 후(추후). 단 스폰 자체는 서버에서 하면 NGO 자동 동기화.

---

## 1. 목적 / 산출물
전체 맵 크기·레이아웃은 **고정(Stage1 프리팹)**, 매 시작마다 **내부만 달라진다**: 퀘스트/보스방/스폰(+상점) 위치, 각 노드(1/2/3티어)의 위치·노드vs장애물·몬스터 구성.

**경석 산출물 (이번에 다 만듦):**
| # | 산출물 | 내용 |
|---|--------|------|
| A | **기본 맵 프리팹 `Stage1`** | 고정 지형 + SpawnPoint + 몬스터 스폰위치 |
| B | **내부 생성 로직** | 영역 역할 배정 + 노드/장애물/몬스터 데이터 결정(시드 기반) |
| C | **서버 스폰** | 결정된 콘텐츠를 서버에서 `NetworkObject`로 스폰(NGO 자동 동기화) |
| D | **디버그/오버뷰 맵 UI** | 전체 노드 표시, 호스트·클라 동일, 클리어 시 실시간 갱신(§7) |
| E | (통합) | 로딩씬(은희)이 A~D를 spawn |

---

## 2. 데이터 모델
| 타입 | 역할 | 비고 |
|------|------|------|
| `MapGenConfigSO` | 전역 수치 — 전부 인스펙터 노출 | `Resources/MapGen/MapGenConfig.asset` |
| `MapPrefabCatalogSO` *(신규)* | 카테고리별 콘텐츠 프리팹 목록(§3) | 노드/장애물용 |
| `ZoneDefinitionSO` | 영역 정의(등급/후보플래그/SpawnPoint) | ZoneDef_1~9 |
| `SpawnPoint` (MonoBehaviour) | 노드 후보 위치(`AllowedTier`, `ParentZone`) + 몬스터 스폰 위치(자식 transform) | 씬 배치. **`ParentZone`으로 ZoneDef에 연결** |
| `ZoneVolume` (MonoBehaviour) *(신규)* | 영역 경계(bounds) + 티어별 스폰포인트 개수 | 스캐터 툴이 읽어 SpawnPoint 생성 |
| `GeneratedNodeData` (struct) | 생성 결과 1건(내부용) | 서버가 스폰 시 참조 |
| `MonsterGroupData` (struct) | 몬스터 그룹 풀(데이터만, 추후) | Config 보유 |

### 2.1 영역(Zone) — 9개, 역할 후보
| 역할 | 후보 영역 | 확정 규칙 |
|------|-----------|-----------|
| **퀘스트** | 중앙 세로로 긴 영역, 좌하단 세로로 긴 영역 (2곳) | 1곳 선택 → 퀘스트 전용(노드 X) |
| **보스방** | 좌최상단, 우최상단, 중앙최하단 (작은 정사각형 3곳) | 1곳 선택 → 노드 X |
| **플레이어 스폰** | 좌하단 가로로 긴 영역, 우중앙 가로로 긴 영역 (2곳) | 1곳 선택 → 상점 동반 |
| **전투** | 나머지 전 영역 | 전부 Combat 고정(추후 변경 가능) |

> ⚠️ ZoneDef_1~9 ↔ 스크린샷 영역 매핑은 인스펙터 인증 필요. 보스후보 툴팁 "2곳"→"3곳".

---

## 3. 프리팹 카탈로그 (`MapPrefabCatalogSO`) — 내부 콘텐츠만
생성기가 시드 기반으로 변형(variant)을 선택 → 서버가 해당 프리팹을 스폰. **벽·바닥은 Stage1 프리팹에 이미 포함된 고정 지형이라 여기 없음.**

| 카테고리 | 프리팹 | 용도 |
|----------|--------|------|
| **1티어 노드(대형)** | `node_factory` · `node_hospitalroom` · `node_operationroom` | 대형 노드 3종 (A등급 영역 3곳, 1곳당 1개) |
| **2티어(중형)** | `SM_Prop_Pallet_03` · `SM_Bld_ConcreteFrame_Pillar_03` · `SM_Prop_Shipping_Container_01` | 중형 노드/장애물 |
| **3티어(소형)** | `SM_Prop_BarrelStack_01` · `SM_Prop_Brick_Stack_04` · `SM_Prop_ConcreteBag_Stack_03` | 소형 장애물 *(회복/순간이동/버프 오브젝트 프리팹은 추후)* |
| **스폰포인트 마커** | `node_spownpoint` | 노드 후보 위치(`SpawnPoint`) — Stage1 프리팹에 포함 |

**고정 지형(Stage1 프리팹에 포함, 생성 무관):**
- 벽(Fence) `SM_Prop_Fence_Concrete_01`/`MetalSheet_02`/`MetalSheet_03`/`Wire_01` — 영역 외벽, **영역별 한 텍스처로 통일**(영역 구분), 문으로 통로 연결.
- 바닥(Floor) `SM_Bld_Concrete_Floor_01`~`04` — 전 영역 바닥.

> 가정(확정 전): 2티어 노드/장애물 = 같은 프리팹 풀 + `Content` 플래그 구분. 3티어 제공 3종 = 장애물 풀, 회복/순간이동/버프는 별도 프리팹 추후.

---

## 4. 영역 역할 배정 순서
1. 퀘스트(후보2→1) → 2. 보스방(후보3→1) → 3. 스폰(후보2→1, +상점) → 4. 나머지=전투 고정 → 5. 전투 영역에만 노드 배치(§5).

> 구현 메모: `AssignQuestZone()`이 공유 SO(`DefaultGrade`) 런타임 변경 → 금지(런타임 역할 상태 분리). `AssignPlayerSpawn()`/`AssignBossGate()` 현재 빈 함수.

---

## 5. 노드 배치 규칙
씬(=Stage1 프리팹)의 `SpawnPoint`(영역별, `AllowedTier`)를 후보로 시드 기반 선택. 수치 전부 Config/인스펙터.

> **Min/Max 의미 = "맵 전체 총량"** (존별 아님 — 2026-06-11 확정). 존별 적용은 슬롯 수에 클램프되어 분포가 고정되는 문제(예: 3티어 전부 회복)가 있어 전역 풀 배분으로 변경·검증 완료.

- **1티어(대형)**: 3곳(A등급 영역 3개). 영역 중앙 또는 끝(후보 중 랜덤). 카탈로그 1티어 3종 배정.
- **2티어(중형)**: ① 전투영역마다 **노드 1개 무조건 보장** → ② 나머지 후보를 **맵 전체 풀**로 모아 `Tier2Obstacle_Min/Max`(전역 총량)만큼 장애물, 나머지는 노드.
- **3티어(소형)**: 전투영역 후보 전체를 **맵 전체 풀**로 모아 `Recovery/Teleport/Buff_Min/Max`(전역 총량) 순서로 배정, 나머지는 장애물. (`Buff` enum 추가 완료)
- **공통**: 배제반경 겹침 방지. 할당 여부는 명시적 `IsAssigned` 플래그.

### 5.1 몬스터 (데이터 + 스폰위치만)
- 노드 결과에 `MonsterGroupID` + `MonsterCount`(난이도별 `MonstersPerNode_*`) 기록.
- 스폰 위치 = `SpawnPoint` 자식 transform(인스펙터 인증).
- 실제 몬스터 프리팹/스폰 = 추후. 스폰은 서버에서 → NGO 자동 동기화.

---

## 6. 런타임 구조 (서버 스폰 + 자동 동기화)
> 데이터 핸드오프 없음. NGO 권한 모델은 [networking.md](networking.md) §권한.

1. **기본 맵 `Stage1` 준비** — 고정 지형/SpawnPoint/몬스터 스폰위치 포함 프리팹.
2. **로딩씬(은희, 오늘 전달)** 에서 서버가 맵을 spawn → 생성 엔트리 호출.
3. **서버: `Generate(seed)`** → 영역 역할 + 내부 콘텐츠(노드/장애물/몬스터 데이터) 결정.
4. **서버: 결정된 콘텐츠를 `NetworkObject`로 스폰** → NGO가 클라에 **자동 동기화**. (정적 지형은 프리팹에 이미 동일하게 포함)
5. **노드 클리어 상태 = 네트워크 변수로 동기화** → 디버그 UI(§7) 실시간 갱신.

> 메모: 고정 지형은 모든 클라 동일하므로 네트워크 동기화 대상은 **동적 콘텐츠(노드/몬스터/클리어 상태)** 뿐. 클리어 상태 동기화 방식(노드별 `NetworkVariable` vs `NetworkList`)은 §8.

---

## 7. 디버그 / 오버뷰 맵 UI (산출물 D)
레이븐스워치 "Act Spawn Locations" 류 전체 맵 오버뷰. **생성 검증 + 디버깅 + 인게임 미니맵 겸용.**
- **전체 노드 위치 + 타입 아이콘** 표시. 레전드(우리 게임 매핑):
  - 보스게이트 / 전투노드(=Objective) / 3티어 회복·순간이동·버프 / 퀘스트 / 스폰·상점 / 장애물.
- **호스트·클라 전원 동일 화면** (동기화된 노드 상태 기반 — §6.5).
- **클리어 시 실시간 갱신**(해당 노드 제거/표시 변경).
- 맵 토글 키(예: M)로 열기. 좌표 → UI 매핑(미니맵 스케일).

---

## 8. 합의 / 미정 / TODO
### 합의 (은희)
- 로딩씬 ↔ 맵 시스템 **호출 인터페이스**(언제/무엇을 spawn, 생성 엔트리 시그니처).
- 노드 클리어 상태 동기화 방식, 콘텐츠 NetworkPrefab 등록.

### 확인 필요
- 2티어 노드/장애물 프리팹 공용 여부, 3티어 회복/순간이동/버프 프리팹 확정 시점.

### 수치 TBD
스폰 안전반경, 배제반경, 몬스터 그룹 풀 실데이터(추후), 난이도 결정 주체.

### 코드 작업 목록
- ✅ 1. `MapEnums`: `NodeContentType`에 `Buff` 추가(끝에 — 직렬화 값 보존) + `ZoneRole` enum 신규.
- ✅ 2. `MapPrefabCatalogSO` 신규 + **`MapPrefabCatalog.asset` 자동 생성·인증 완료**(9종 프리팹 연결, `MapCatalogPopulator` 메뉴).
- ✅ 3. `MapGenerator`: 역할 배정 **SO 비변경=런타임 dict**, `Generate`→`List<GeneratedNodeData>`. 단일 RNG. **씬 SpawnPoint를 `ParentZone`별 수집**(`GatherSpawnPoints`).
- ✅ 4. `NodePlacer`: per-zone `List<SpawnPoint>` 처리, `IsAssigned` 버그 수정, Tier2 노드≥1 보장+장애물 Min/Max, Tier3 회복/순간이동/버프/장애물 Min/Max, variant 선택.
- ✅ 5. `ZoneDefinitionSO`: 보스 툴팁 3곳. **`SpawnPoints` List 제거** — SO는 씬 참조 직렬화 불가 → `SpawnPoint.ParentZone` 역참조로 전환(**설계 결함 수정**). `ZoneType` 필드 불필요(역할 런타임).
- ✅ 6. `SpawnPoint`: `ParentZone`/`AllowedTier`/`MonsterSpawnPoints` + `IsAssigned`/`ResetRuntime`.
- ✅ 보조: `ZoneVolume`(경계+티어별 개수) + `MapSpawnPointScatter`(스폰포인트 자동 분산) + `MapSceneSetup`(MapGenerator 배선) + `Test Generate` 우클릭.
- ✅ 7. **콘텐츠 스폰(`MapContentSpawner`)** — 생성 결과→프리팹 인스턴스화. **NetworkObject 프리팹=서버만 `Spawn()`**(NGO 복제, 클라 생성 스킵), 비네트워크 시각물=양쪽 로컬 생성(같은 시드). 1티어·스폰 구조물 **×100 스케일**(카탈로그 `Tier1Scale`/`SpawnStructureScale`), 스폰 존 중앙에 `node_spownpoint` 배치. `Generate()` 끝에 자동 호출. *(남음: 클리어 상태 네트워크 동기화 — 디버그 UI와 함께)*
- ✅ 8. **디버그/오버뷰 맵 UI**(`MapOverviewUI`) — **M키 토글**(또는 `VeyTrace/Map/Toggle Overview UI`). 존 사각형(역할별 색: 전투=회청/보스=빨강/스폰=초록/퀘스트=노랑) + 역할 아이콘(Boss/Spawn/Quest.png) + 노드 점(티어별 크기, 내용별 색: 전투=주황/장애물=진회/회복=초록/이동=하늘/버프=보라). 열 때마다 현재 데이터로 재드로우 → 서버/클라 동일(같은 시드). *(남음: 노드 클리어 시 `RefreshOverview()` 호출 연결 — 클리어 시스템 붙을 때)*
- ✅ 추가: 역할 영역에 아이콘 Quad 마커(1티어급 크기 20, `AreaMarkerSize`) + 3티어 2.5배(`Tier3Scale`).
- ✅ 9. **기본 맵 `Stage1` 프리팹화** — `Assets/2.Prefabs/Map/Stage1.prefab` (MapGeometry 고정지형 + ZoneVolumes/SpawnPoints 포함, 씬 인스턴스 연결). 빌드 규칙: 바닥/벽 **정확 맞춤 스케일 보정**(빈틈 없음), **통로 화이트리스트 13쌍**만 뚫린 길로 연결(문 없음), 나머지 인접부 통벽. `MapGeometryBuilder.CorridorPairs`에서 쌍 수정.
- 🔶 10. 데이터/씬 셋업: 카탈로그✅, ZoneDef_1~10 후보플래그✅(ID/이름 수정 완료). **남음: 씬에 ZoneVolume 배치 → Scatter 실행**(또는 SpawnPoint 수동 배치).

### 에디터 셋업 순서 (Unity 메뉴) — Unity 재연결 후
1. `VeyTrace/Map/Populate Prefab Catalog` — 카탈로그 채우기 *(완료)*.
2. `VeyTrace/Map/Setup Scene Generator` — 씬에 MapGenerator 생성·배선.
3. 각 영역 위에 **`ZoneVolume`** 배치 → `Zone` 지정 + 크기/티어 개수. (A등급 1·2·3번은 `Tier1Count`≥1)
4. `VeyTrace/Map/Scatter Spawn Points` — 볼륨 안에 SpawnPoint 자동 생성.
5. **MapGenerator 우클릭 → `▶ Test Generate`** — 콘솔에서 매번 다른 결과 확인.
6. 씬 저장.

### 현재 씬(MapScene)
블록아웃 + `node_factory`/`node_hospitalroom`/`node_operationroom`/`node_spownpoint` 마커. → Stage1 프리팹화 대상.
