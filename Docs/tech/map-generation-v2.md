# 맵 생성 v2 — 구현 문서 (as-built)

> 2026-07-02 기준, `feature/map` 브랜치 (커밋 964ba80 → 76c8d78).
> 구 절차 조립(v1: ZoneAssembler/MapGeometryBuilder+ZoneVolume) 폐기, **사전 디자인 통짜 존 + 고정 스켈레톤 + 셔플 배치**로 전면 교체됨.
> 설계 원본: [map-generation.md](map-generation.md)(NGO 결정), 마스터 프레임 = `Downloads/맵레벨디자인및Zone숫자.png`.

## 1. 개요

- 존 10개(대3/중4/소3)를 **고정 슬롯 스켈레톤**에 매 generate마다 셔플 배치.
- 존 = 아티스트가 블렌더에서 통짜 제작한 FBX → 임포터가 프리팹화.
- 다리/둘레벽 = 슬롯 기준 절차 생성(정적, 프리팹) — 존 내용물만 매판 바뀜.
- 멀티: 서버가 시드 결정(MapNetworkSync) → 양쪽 로컬 생성. 모든 랜덤은 주입 시드 단일 RNG, 순회는 SlotID 정렬 — 결정적.

## 2. 에셋 규격 (실측 확정)

| 항목 | 값 |
|---|---|
| 존 크기 | 대 41×41m / 중 21×41m(5×10타일) / 소 21×21m — 크기별 완전 동일 |
| 존 개방변 | **로컬 N(+Z)·W(−X) 완전 개방**, S/E는 벽(문 장식 포함) — 9종 공통 |
| 벽의 문 | **장식(통로 아님)**. 실제 통로 = 지오메트리가 뚫린 곳만 |
| 보행면 높이 | y=0.50m (바닥 렌더러 top 최빈값 — 빌더가 자동 실측) |
| 프리팹 피벗 | 임포터가 렌더러 바운즈 XZ 중심으로 자동 센터링(Y는 저작값 유지) |
| 텍스처 | `Assets/50.Art/texture/Tex_zone/` 개별 텍스처+노멀맵 (구 Env_Factory 아틀라스 폐기) |
| FBX | `Assets/50.Art/mesh/Mesh_zone/zone_{L,M}_type{A,B,C}, zone_S_{typeA,typeBoss,typeStart}` (git엔 meta만 — 50.Art 바이너리 정책) |

## 3. 배치 규칙 (팀장 확정)

- **대형 3**: 3디자인 ↔ 3슬롯 매판 랜덤 순열(재사용 없음).
- **중형 4슬롯(전부 퀘스트 후보)**: 퀘스트 위치 1곳 랜덤 → `zone_M_typeQuest`(전용, 현재 임시 코드 생성본) 배치, 나머지 3슬롯 = M_typeA/B/C 랜덤.
- **소형 3**: 우상단(S_TR) = 고정 전투(S_typeA). **좌상(S_TL)·좌하(S_BL) = 스폰 후보 겸 보스입구 후보** — 한쪽이 스폰(typeStart)이면 다른쪽이 보스입구(typeBoss).
- **회전**: 결정적(슬롯별 고정) — 개방변이 다리를 최대한 많이 받는 90° 회전(다리 개수 가중, 동점=최소 회전). 대/소=4방향, 중형=0/180(풋프린트 축 유지). 배치 다양성은 "어떤 디자인이 오는가"로 확보.
- **다리가 벽 변으로 갈 수밖에 없는 곳(5곳) = 벽 밀기**: 스폰 직후 다리 입구 자리 벽 조각 삭제(§5).

## 4. 시스템 구성

| 파일 | 역할 |
|---|---|
| `1.Scripts/Map/Editor/MapZoneImporter.cs` | FBX→프리팹: 블렌더 머티리얼 **이름** 기반 슬롯 매핑(conv*→컨베이어, floor*→바닥, window→창벽, prop→prop, 그외→기본벽), MeshCollider, 피벗 센터링, 파일명 기반 Size/Role 태깅(typeBoss/typeStart/typeQuest), **출입구 자동 감지**(벽 계열 커버리지 4m+ 빈틈) |
| `1.Scripts/Map/Editor/ZoneWiring.cs` | 슬롯 스켈레톤(좌표 하드코딩) + 연결 그래프 13쌍(SlotPairs) + 카탈로그 + 참조 배선 + **정적 지오메트리 빌더** + 문 위치 리포트 + 임시 퀘스트존 빌더 + 테스트 생성 메뉴 |
| `1.Scripts/Map/MapGenerator.cs` | 시드 생성 진입점. 슬롯 수집(SlotID 정렬)→역할 배정(퀘스트/보스/스폰 — 후보 중 랜덤) |
| `1.Scripts/Map/LayoutPlacer.cs` | 크기별 전투 풀 셔플 1:1 배정(퀘스트는 전용 디자인, 없으면 풀 폴백) + **회전 매칭(PickYaw)** |
| `1.Scripts/Map/MapContentSpawner.cs` | 존 인스턴스화(슬롯 위치·회전+ExtraYaw) + **벽 컷(CutWallsForSlot)** + 몬스터 서버 스폰(마커 기반, 현재 마커 0) + 에디터 생성물 DontSaveInEditor |
| `1.Scripts/Map/ZoneSlot.cs` | 슬롯 앵커: Size/역할후보/ConnN·E·S·W(다리 개수)/**WallCuts**(벽 컷 지점) |
| `1.Scripts/Map/ZoneLayout.cs` | 존 프리팹 태그: Size/Role/OpenN·E·S·W(출입구)/노드·몬스터 마커(미배치) |
| `50.Art/MapGen/MapObj/ZoneLayout/ZoneLayoutCatalog.asset` | (Size×Role) 프리팹 카탈로그 10엔트리 |
| `50.Art/MapGen/MapObj/MapGeometryV2.prefab` | 정적 지오메트리(다리 13 + 개방변 둘레벽 20변) — 씬 인스턴스 연결 |

**LayoutPlacer.PickYaw ↔ ZoneWiring.FinalStepsFor는 동일 규칙의 중복 구현** — 한쪽을 바꾸면 반드시 다른쪽도 (런타임 배치와 정적 다리가 같은 회전을 전제).

## 5. 정적 지오메트리 & 벽 컷

`Tools/MapGen/Build Static Geometry V2` 1회 실행으로:
1. 구 산출물 삭제 → 슬롯 기준 **다리 13개**(폭 6m, 바닥 top=보행면 0.5, 측벽 3m) 생성. 벽 변으로 붙는 다리는 해당 슬롯 `WallCuts`에 입구 기록(현재 5지점).
2. 존 **개방변(N/W) 둘레벽** 20변 — 다리 입구 위치만 트고 채움(떨어지는 길 차단). 벽 변은 존 자체 벽이 담당.
3. 전부 Tex_zone 머티리얼(프리미티브 세그먼트 4m — 임시, 추후 아트 다리 메시로 교체 예정) → `MapGeometryV2.prefab` 저장.

**벽 컷**: 존 스폰 직후 `WallCuts` 지점의 7×3m 박스와 겹치는 벽/코너/문 조각을 `SetActive(false)` — 결정적(동일 시드→동일 결과). 아트가 개구부 미리 뚫린 존 변형을 주면 컷 자동 0. 컷 단면은 조각 단위라 미관 보정은 추후 아트 몫.

## 6. 에디터 워크플로 (메뉴)

| 메뉴 (Tools/MapGen/) | 용도 |
|---|---|
| Import All Zone FBX (Mesh_zone) | FBX 재임포트 → 프리팹 갱신 (아트 교체 시 이것만) |
| Build Temp Quest Zone Prefab | 임시 퀘스트존 재생성 (정식 FBX 오면 불필요) |
| Wire Slots + Catalog + Refs | 슬롯/그래프/카탈로그/참조 재배선 (슬롯 좌표·그래프 수정 후) |
| Build Static Geometry V2 | 다리+둘레벽+컷지점 재생성 → 프리팹 (Wire 후) |
| Test Generate (random seed) | 매클릭 새 배치 (씬 저장에 안 섞임) |
| Test Generate (seed 12345/99) | 재현/디버그용 고정 시드 |
| Report Zone Door Positions | 존별 문(장식) 위치 리포트 |

순서 의존: Import → (Wire) → Build Static Geometry → Generate.

## 7. 검증 상태

- ✅ 셔플: 대형 순열/퀘스트 4중1/스폰↔보스입구 스왑 — 시드별 상이 확인
- ✅ 다리 13/13 + 전 존 연결(고립 없음), Y 무단차(보행면 0.5 정렬)
- ✅ 벽 컷 5지점 38조각, 개방변 둘레벽 폐합(다리 입구 외 낙하 불가)
- ✅ 씬 박제 방지(생성→저장 시 씬 크기 불변)
- ⏳ 미검증: 실제 네트워크 세션(서버 시드 동기화) 생성·통행, 낙하→복귀 실플레이

## 8. 남은 작업 (다음 세션 후보)

1. **노드/몬스터 스폰 마커** — 기획 회의 결과 대기(존당 노드 수/티어/위치 규칙). 그전까지 빈 상태 유지(팀장 지시). 몬스터 프리팹도 미확정(MapGenConfig.MonsterGroups 2종 정의만 있음).
2. **타입2 몬스터(노드 상호작용 시 순간 소환)** — 서버 Instantiate만 해두고 상호작용 이벤트에 NetworkObject.Spawn() 하는 지연 스폰. 기획 확정 후.
3. 아트 인계: 다리 정식 메시 / zone_M_typeQuest 정식 FBX(같은 이름 임포트=교체) / TEMP_QT.002 임시 머티리얼 정리 / (선택) 컷 위치 개구부 변형.
4. 실플레이 검증(네트워크 생성/통행/낙하복귀) 후: 씬의 구 ZoneVolumes(비활성)·Env_Factory_* 머티리얼 삭제.
5. FoW·물: [fog-system.md](fog-system.md) / WaterDark(어비스 물) 완료 — 물결 흐름·FoW는 별도 트랙.
