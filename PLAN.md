# PLAN — 맵 시스템 (v11 최소화: 슬롯 저장식 셔플 배치, 2026-07-14)

## ★ 다음 세션 시작점 (이것부터)
자동배치·보정·진단툴을 **전부 걷어내고** 최소 구조로 재편한다.
런타임 = **프리팹 셔플 → 슬롯에 저장된 위치·회전으로 생성**. 저작 = **Save 툴 하나**.

## 핵심 모델 (승인됨 2026-07-14)
- **슬롯 골격 = ZoneSlot 10개(씬).** ZoneSlot이 이미 SlotID·Size·후보플래그(Quest/Boss/Spawn)를 가짐.
  여기에 **① baseline 위치 = `slot.transform.position`(손으로 맞춘 값)** + **② 프리팹별 회전 = `List<(Prefab, YawSteps 0~3)>`** 를 저장한다.
- **좌표 = 슬롯당 1개(공유).** 처음에 현재 `GeneratedMap`(스냅으로 맞춰둔 10개)에서 한 번 저장, 이후 안 바꿈.
- **회전 = (슬롯 × 프리팹) 조합별.** 90° 4택(0/90/180/270). 조합 **34개**(L 3×3=9 / M 5×4=20 / S 5) = 사용자가 노가다로 채움.
- **생성:** `LayoutPlacer`가 프리팹 셔플만 → `MapContentSpawner`가 (슬롯, 뽑힌 프리팹)로 YawSteps 조회 → `slot.position` + yaw로 Instantiate. 조회 실패 = 에러+스킵(조용한 폴백 금지).
- **프리팹 풀 = `ZoneLayoutCatalogSO` 유지**(Size×Role 셔플 소스).

## 삭제 (전부)
- 파일 통삭제: `Editor/BakeZones.cs`, `Editor/HallwayDebug.cs`, `Editor/MapZoneImporter.cs`, `Editor/AllMeshImporter.cs`.
- 메뉴 삭제: 근접 Bake, Y단차보정 2종, Hallway ASCII/FloorY/SnapSlots, Normalize, ForceReload, `Test Generate(seed 99)`.
- `ZonePlacement.ExtraYawSteps` 죽은 필드 → 슬롯 YawSteps 조회로 대체(의미 재정의).
- **`ZoneVolume`/`ZoneDefinitionSO` 제거**: 슬롯이 자립하면 불필요. 단 씬에 컴포넌트 10개 + `ZoneDef_*` 에셋 10개가 살아있어 **씬 수술 필요**(구현 순서 6). ZoneVolume의 `Tier1/2/3Count`(옛 SpawnPoint 분산툴용, 죽은 값)도 함께 소멸.

## 유지 (건드리지 말 것)
- **`SpawnPoint.cs` 유지** — 어비스 물 리스폰(`WaterRespawnTrigger`)이 씬 `SpawnPoint`를 참조. (ZoneVolume의 Tier카운트와 무관한 별개 시스템 — 혼동 주의.)
- `ZoneLayoutCatalogSO`, `ZoneSlot`(확장), `LayoutPlacer`(셔플), `MapGenerator`, `MapContentSpawner`(수정), 미니맵.

## 툴 (최종 = 3개)
1. `Test Generate (random)` — 셔플 생성.
2. `Test Generate (seed 12345)` — 재현/디버그.
3. `Save Placements` — 현재 `GeneratedMap` 클론들의 `(SlotID, 프리팹) → (slot 위치, YawSteps)`를 슬롯에 저장. `GeneratedZoneIdentity`로 정확 식별(근접매칭 아님). 저장 후 34개 중 **미저작 조합 목록**을 로그.
   - 슬롯 골격/카탈로그 초기 셋업은 기존 `Wire`를 1회성으로만 쓰고, 자립 후 손유지.

## 구현 순서
1. `GeneratedZoneIdentity`(SlotID + 소스 프리팹) 컴포넌트 신설, 스폰 시 부착.
2. `ZoneSlot`에 `List<RotationEntry{ GameObject Prefab; int YawSteps }>` + `TryGetYaw(prefab, out steps)` 추가.
3. `MapContentSpawner`: `slot.position` + 조회 yaw로 스폰(실패 시 에러+스킵). `LayoutPlacer.ExtraYawSteps` 경로 정리.
4. `Save Placements` 에디터 툴 신설(위치+회전 저장 + 커버리지 로그).
5. 삭제 목록 파일/메뉴 제거 → **컴파일 0에러** 확인.
6. (씬 수술 — 사용자와 함께) `ZoneVolume`/`ZoneDef` 제거 — 슬롯 자립 확인 후. missing script 정리 + 에셋/`.meta` 삭제.

## 검증
- 컴파일 0에러 → `Save Placements`로 baseline 저장 → 회전 34개 채움(커버리지 34/34) → 랜덤 seed 여러 번 생성해 전 존 통로 연결 육안 → 같은 seed 재현 동일 → (여력) MPPM Host/Client 일치.

## 함정 (유지)
- 클론(`GeneratedMap/*`) = `DontSaveInEditor` → 조정 후 **Save 먼저, 리컴파일/재생성은 그 다음**.
- MCP `set_transform`은 `{x,y,z}` position+rotation **함께** 줘야 함(부분지정 시 원점 리셋 버그).
- 좌표는 슬롯 공유 — 특정 프리팹 문이 어긋나면 **그 프리팹 자체를 고치지, 슬롯 위치를 옮기지 않는다**(옮기면 그 슬롯의 다른 조합이 틀어짐).

---

# (이전) PLAN — 맵 시스템 (v10 리셋 핸드오프, 2026-07-14)

## ★ 다음 세션 시작점 (이것부터 읽기)
**자동 배치(v8 stage1_example 중심추출 + v9 회전프리셋)는 통로에 안 붙어 폐기·전부 롤백됨.**
다음 세션 = **수동 정렬 + Bake 방식**으로 진행한다 (사용자가 각 존을 통로에 직접 맞추고 그 값을 저장).

### 왜 자동배치를 접었나 (핵심 교훈)
- 존 프리팹들이 **표준화돼 있지 않다** — 같은 크기라도 `typeA/B/C`의 문·연결부 위치가 제각각.
  → "슬롯 중심에 셔플 프리팹을 놓고 회전만" 방식으로는 문이 통로 끝점과 못 맞아 **갭 수 미터**로 벌어진다.
  회전 프리셋(v9)만으론 위치 오프셋을 못 흡수. 바닥 bounds 중심 계산(Task 0)까지 해봐도 근본적으로 부족.
- 결론: **셔플 배치는 프리팹이 규격 모듈일 때만 가능.** 현재 아트는 아니므로 자동배치 폐기.

### 확정 사실 (재사용 — 수동 방식에도 유효)
- `stage1_example`(MapScene 내, 루트 (0,0,0)) = 디자이너 정답 레이아웃. **정답 레퍼런스로 보존.**
- 존 10개 = **L3** `ZoneL_RightDown/MiddleTop/LeftMiddle` · **M4** `ZoneM_LeftDown/MiddleOfMiddle/LessLeftDown/RightMiddle` · **S3** `ZoneS_MiddleDown/RightTop/LeftTop`.
- ZoneDef_1~10(SO) + ZoneVolume 10개 = ID·역할후보 플래그 보유(1~3 대형전투 / 4·5·9·10 중형퀘 / 6·8 소형Spawn+Boss / 7 소형전투).
- 통로 = `Stage1/Level_wall_hallway` 고정(생성 대상 아님, 재사용). Y단차 보정 = `Level Zones To Hallway Floor`.

### 다음 세션 워크플로 (아래 'v6' 절이 상세 — 그대로 사용)
1. `Tools/MapGen/Wire Slots + Catalog + Refs` — ZoneVolume → 슬롯.
2. `Tools/MapGen/Test Generate` — 존 스폰(GeneratedMap 클론).
3. **각 클론을 씬에서 통로에 직접 맞춤(위치 + 90° 회전).** 케이스별로 눈으로 정렬.
4. `Tools/MapGen/Bake GeneratedMap -> ZoneVolumes` — 맞춘 위치+yaw를 ZoneVolume에 되받아 저장.
5. `Wire` 재현 확인 → **씬 저장** → 커밋(팀장).

## 현재 코드 상태 (2026-07-14 롤백 직후)
- **오늘 세션 자동배치 산출물 전부 삭제**: `BuildZoneVolumes.cs`, `ZoneFootprintAuthoring.cs`, `Core/`(ZoneSize이전분·ZoneFootprintSource·ZoneFloorBoundsUtility·ZoneRotationPresetCatalogSO·asmdef), `Assets/Tests/EditMode`. `MapEnums`(ZoneSize) 원복, `ZoneWiring` 편집 되돌림, 프리팹 8개 원복, 씬/프리팹 missing script 제거. **컴파일 0에러.**
- v6/v8 툴(`ZoneWiring.Wire`, `BakeZones`, `Test Generate`, `MapZoneImporter`, `HallwayDebug`)은 그대로 정상.
- ⚠️ 잔재: `ZoneL_typeA`·`ZoneM_typeQuest01/02` 프리팹 pivot 0.2~0.5m 이동(미커밋/미추적이라 못 되돌림 → **아트/SVN 재수령 시 덮임**). 씬 ZoneVolume 위치는 위 3~4단계 Bake로 재정렬 예정이라 무해.
- 전부 미커밋(feature/map). stash: `stash@{0}`(꼬임백업 7/10), `stash@{1}`(map-v2-wip 6/30).

---

## (이전) v6 — ZoneVolume yaw + Bake 루프
Status: ZoneVolume yaw 단일소스 + Bake 되받기. seed 12345 생성 검증됨(대형 Z3 -90 자동 회전 포함).
아래는 참고용 — 다음 세션은 위 stage1_example 기반으로 진행.

## v6 확정 워크플로
1. `Tools/MapGen/Wire Slots + Catalog + Refs` — ZoneVolume(위치+Size+yaw) → 슬롯 생성.
2. `Tools/MapGen/Test Generate (seed 12345, 재현용)` — 존 프리팹 스폰.
3. **미세조정(핵심 루프)** — 씬에서 실제 존 클론(`GeneratedMap/*`)을 눈으로 보며 **회전(월드 Y) + 위치(다리 미세정렬)** 조정.
4. `Tools/MapGen/Bake GeneratedMap -> ZoneVolumes` — 클론 위치+yaw를 가장 가까운 ZoneVolume에 되받아 영구화.
5. `Wire` 재실행 → 슬롯 재현 확인 → **씬 저장** → 커밋(팀장 리뷰).

## 회전/위치 규칙 (ZoneWiring.Wire)
- 슬롯 회전 = **ZoneVolume 트랜스폼 월드 yaw(90° 스냅) 단일 소스.** 대형·중형·소형 전부 동일 규칙.
  - 구 `sizeYaw`(Size 비율→90° 자동유도) **제거** — 정사각(대/소)은 유도 불가, 명시적 회전이 WYSIWYG로 정확.
- 슬롯 위치 = ZoneVolume 위치 그대로(Y 포함, 과거 Y=0 클램프 제거).
- 방향 저작은 "실제 존 돌리고 Bake" (사용자가 클론을 보며 맞추는 게 가장 정확 + 다리 미세정렬 동시 해결).
- ✅검증: Z3(대형)=270(-90), Z9·Z10(가로중형)=90 → 슬롯·클론 재현됨.

## Y 단차 자동 보정 (BakeZones.cs)
- 문제: 일부 프리팹 바닥이 통로 바닥면보다 +0.5 떠있음(ZoneS_typeBossEnter·ZoneS_typeStart·ZoneL_typeB 3종 — 크기 무관, 프리팹별).
- `Tools/MapGen/DEBUG Report Zone Floor Deltas` — 통로·존의 **바닥 메시(이름에 floor 포함)만** 골라 상면 Y 중앙값 측정 → 존별 Δ 출력(측정만).
  (구 HallwayDebug 전체렌더러 최빈 max.y는 벽/두꺼운 조각 잡아 통로=3.0 오측 → floor 필터로 해결.)
- `Tools/MapGen/Level Zones To Hallway Floor (write ZoneVolume Y)` — Δ를 ZoneVolume Y에 적용(영구, Wire로 재현).
- ✅검증: 적용 후 재측정 시 10개 전부 Δ=0.00(통로 바닥면 Y=0에 정렬).

## ZoneVolume 기즈모
- 선택 시 트랜스폼 **회전까지 반영해 박스 + forward(+Z) 노란 화살표** 표시 → 볼륨 회전이 눈에 보임(ZoneVolume.cs).

## 코드 변경 (이번 세션, 미커밋)
- `Editor/ZoneWiring.cs` — 슬롯 회전=ZoneVolume yaw 단일소스(sizeYaw 제거), 위치 Y클램프 제거.
- `Editor/BakeZones.cs` 신규 — `Bake GeneratedMap → ZoneVolumes`(근접 매칭, yaw 90° 스냅).
- `ZoneVolume.cs` — 기즈모 회전 반영 + forward 화살표.
- (경위) 세션 초반 앵커 자동캡처 툴(AnchorTool.cs)을 만들었다가 ZoneVolume 직접 방식 확정으로 **제거**함. 씬 더미 ZoneAnchors도 삭제.
- 컴파일 0에러 / Wire·Generate 정상 검증.

## ⚠️ 주의 / 함정
- 클론(`GeneratedMap/*`) = **DontSaveInEditor** → 리컴파일/재생성 시 소실. **클론 조정 후 반드시 Bake 먼저, 그 다음 리컴파일/재생성.**
- MCP `unity_set_transform`은 **오브젝트 포맷 `{x,y,z}` + position·rotation 함께** 줘야 함. 배열이나 부분지정 시 **위치가 원점(0,0,0)으로 리셋**되는 버그 있음(이번에 겪음).
- 리컴파일 시 MCP 브릿지(포트 3000)가 도메인 리로드로 끊길 수 있음 → **에디터 창 포커스**로 재바인딩. 씬 트랜스폼 값은 리로드로 안 날아감(미저장분만 주의).
- 맵 밖 **앵커 템플릿 3개(L/M/S_Anchor)**: 크기 참고용으로 아트가 만든 것, 이제 자동화엔 안 씀. 씬 저장 전 삭제할지 결정.

## 확정 규약 (유지)
- **슬롯 좌표 = 존 중심 (WYSIWYG).** 위치 재정렬 없음(CenterOnSlot 제거됨). 회전은 v9 구현 후 `slot.rotation * ExtraYawSteps` 규약을 따른다.
- **Slots 부모 = (0,0,0)**, 숨은 오프셋 금지.
- 카탈로그: 대형3 / 중형3+퀘스트2(2중1) / 소형(typeA·BossEnter·Start). 배치 소스오브트루스 = 씬 ZoneVolume 10개.
- 역할존 후보/시드 매핑 = ZoneVolume 플래그(Quest/Spawn/BossEnter). Clone↔슬롯·존 매칭은 v9 구현 후 근접 방식이 아니라 `GeneratedZoneIdentity.SlotID`를 사용한다.
- 디버그 툴 `HallwayDebug.cs`(ASCII 덤프/Y리포트) 유지. SnapSlots(통로 실측)는 폐기.
- 백업 stash: `stash@{1}`(구 all_mesh), `stash@{2}`(map-v2-wip).

---

# v9 추가 계획 — 슬롯×프리팹 사전 회전 프리셋 (2026-07-14 승인 → ❌폐기)

> ❌ **폐기됨 (2026-07-14).** 프리팹이 표준화돼 있지 않아(같은 크기라도 문 위치 제각각) 회전 프리셋만으론
> 통로에 안 붙음. 구현했다 전부 롤백. 상단 v10 핸드오프(수동 정렬+Bake) 참조. 아래는 이력 보존용.
>
> (원안) 이 절은 위 v8의 수동 yaw/Bake 루프 중 **회전 결정과 Clone 매칭 규약을 대체**하려 했다.

## 목표

- `Test Generate`가 존을 셔플한 뒤 런타임에서 출입구를 탐색하거나 최적 회전을 계산하지 않는다.
- 고정 Stage1 슬롯과 셔플된 Zone 프리팹의 조합으로 미리 저장된 90° 회전값을 조회하여 통로에 정확히 연결한다.
- `stage1_example`은 슬롯 중심/통로 접속 위치의 아트 정답 레이아웃으로 유지한다.
- 같은 seed는 호스트와 클라이언트에서 동일한 프리팹·역할·회전을 선택한다.

## 좌표 오차의 확인된 원인과 수정 원칙

### 확인된 원인

- 현재 `BuildZoneVolumes.TryOwnBounds`는 Zone 자식의 **모든 Renderer 월드 bounds**를 합산한다. 따라서 바닥뿐 아니라 Zone 프리팹에 일부 포함된 벽·몰딩·장식 돌출까지 중심과 크기 계산에 들어간다.
- 실제 로그에서도 논리 규격 `20/40m`가 아니라 S `20.8×20.8`, M `20.8×40.8` 또는 `41.6×20.8`, L `40.8×40.8` 또는 `41.6×40.8`로 측정됐다. 이 값은 부동소수점 공차가 아니라 벽 두께와 비대칭 시각물의 크기다.
- `BuildZoneVolumes.ApplyToVolume`은 이 시각 bounds의 `center`를 ZoneVolume 위치로, `size`를 ZoneVolume 크기로 복사한다. 이후 `ZoneWiring`이 이를 ZoneSlot으로 만들고, `MapContentSpawner`는 그 슬롯 월드 위치에 **프리팹 루트 Pivot**을 맞춘다.
- 즉 기존 구현은 `전체 Renderer bounds 중심 = 논리 바닥 중심 = 프리팹 루트 Pivot`이라고 가정했지만 실제 에셋은 이 세 기준이 일치하지 않는다.
- 로컬 좌표와 월드 좌표의 변환 자체가 주원인은 아니다. 서로 다른 기준점(bounds center, 논리 center, root pivot)을 같은 좌표라고 취급한 것이 주원인이다.
- 논리 중심에서 벗어난 Pivot으로 90° 회전하면 기존 위치 오차도 함께 회전하므로, 셔플된 프리팹에 따라 통로 접합부의 틈/겹침 방향이 달라진다.

### 확정 수정 원칙

- **논리 풋프린트는 벽·장식·몰딩을 제외한 바닥 전용 bounds**로 계산한다. 기대 크기는 S `20×20`, M `20×40` 또는 `40×20`, L `40×40`이다.
- 바닥 Renderer 판별을 오브젝트 이름 추측으로 구현하지 않는다. 각 Zone 원본/프리팹 루트에 `ZoneFootprintSource`를 붙이고, 직렬화된 `FloorRenderers` 목록에 바닥 Renderer만 명시적으로 저장한다. 목록이 비었거나 크기가 규격과 다르면 전체 Renderer bounds로 폴백하지 말고 검증 실패로 중단한다.
- 바닥 전용 bounds는 `stage1_example`에서 **10개 ZoneSlot의 논리 중심을 한 번 보정하고 검증하는 에디터 저작 데이터**로만 사용한다. `Test Generate` 또는 실제 플레이 중에는 bounds를 다시 계산하지 않는다.
- `Level_wall_hallway`의 위치·형태·벽은 Stage1 전체에서 고정이며 런타임 생성 대상이 아니다. 보정 완료 후 10개 ZoneSlot의 위치/기본 회전/풋프린트를 저장하고 이를 유일한 배치 기준으로 사용한다.
- 각 Zone 프리팹 루트 Pivot의 XZ가 바닥 전용 bounds 중심과 일치해야 한다. 허용 오차를 넘으면 프리팹 자식 전체를 같은 양만큼 이동해 Pivot을 논리 중심으로 정규화하거나 명시적 `ZoneOrigin`을 도입한다. 슬롯별 임의 위치 보정값으로 숨기지 않는다.
- 회전 정규화 후에는 벽 포함 Renderer bounds가 프리팹마다 다른 것을 정상적인 아트 차이로 허용한다. 이 시각 bounds는 위치, 회전, 슬롯 크기 판정에 사용하지 않는다.
- 고정 hallway와 Zone 양쪽에 벽이 있으므로 접합부의 실제 출입구는 별도 소켓/마커로 검증한다. 벽 bounds가 서로 닿는지 여부로 출입구 연결을 판정하지 않는다.

## 확정 아키텍처

### 회전 키

- 단순 `SlotID → yaw`가 아니라 **`(SlotID, LayoutPrefab) → ExtraYawSteps`**를 사용한다.
- 이유: 같은 L/M/S 크기라도 프리팹마다 로컬 출입구 방향이 다를 수 있으므로 슬롯 yaw 하나만 저장하면 셔플 후 접속이 깨진다.
- `ExtraYawSteps` 허용값은 `0..3`이며 각각 `0°/90°/180°/270°`다.
- 최종 회전은 아래 한 규칙만 사용한다.

```csharp
Quaternion finalRotation = slot.transform.rotation
    * Quaternion.Euler(0f, preset.ExtraYawSteps * 90f, 0f);
```

- 실행 중 Renderer bounds, 물리 쿼리, 출입구 자동감지로 회전을 다시 계산하지 않는다.
- 프리셋 누락/중복/범위 오류는 임의의 0° 폴백으로 숨기지 말고 명시적 오류로 노출한다.

### 프리셋 예상 개수

- Large: 전투 프리팹 3종 × L 슬롯 3곳 = **9개**.
- Medium:
  - 전투 프리팹 3종 × M 슬롯 4곳 = 12개.
  - 퀘스트 프리팹 2종 × M 슬롯 4곳 = 8개.
  - 합계 **20개**.
- Small:
  - `ZoneS_typeA`는 고정 전투 슬롯 1곳 = 1개.
  - `ZoneS_typeBossEnter`는 후보 2곳 = 2개.
  - `ZoneS_typeStart`는 후보 2곳 = 2개.
  - 합계 **5개**.
- 현재 카탈로그 기준 총 기대값은 **34개**다. 카탈로그/후보 규칙 변경 시 검증기가 기대 조합을 다시 계산해야 한다.

## 변경 대상과 책임

### 신규 좌표 저작 데이터

- [ ] `Assets/1.Scripts/Map/Core/ZoneFootprintSource.cs`를 생성한다. (Core asmdef — ZoneSize를 참조하므로 ZoneSize도 Core로 분리)
- [ ] 컴포넌트는 `ZoneSize ExpectedSize`와 `List<Renderer> FloorRenderers`만 가진다. 컴포넌트가 붙은 Transform을 해당 Zone의 기준 공간으로 사용한다.
- [ ] `stage1_example`의 10개 Zone 그룹과 생성 후보 Zone 프리팹 전부에 컴포넌트를 붙이고 바닥 Renderer 참조를 저장한다.
- [ ] `Assets/1.Scripts/Map/Core/ZoneFloorBoundsUtility.cs`를 생성하고 `TryCalculateLocalBounds(Transform reference, IReadOnlyList<Renderer> floorRenderers, out Bounds bounds, out string error)`를 제공한다. (순수 계산 — EditMode 테스트 위해 Editor가 아닌 Core asmdef에 둠)
- [ ] 유틸리티는 각 Renderer의 로컬 bounds 8개 모서리를 `renderer.localToWorldMatrix`로 월드에 옮긴 뒤 `reference.worldToLocalMatrix`로 기준 공간에 변환해 합산한다. `Renderer.bounds` 월드 AABB를 다시 로컬로 변환하는 방식은 회전 시 AABB가 부풀 수 있으므로 사용하지 않는다.

### 신규 런타임 데이터

- [ ] `Assets/1.Scripts/Map/Core/ZoneRotationPresetCatalogSO.cs` 생성.
- [ ] 직렬화 엔트리는 `int SlotID`, `GameObject LayoutPrefab`, `int ExtraYawSteps`만 가진다.
- [ ] `TryGetExtraYawSteps(int slotId, GameObject prefab, out int steps)`를 제공한다.
- [ ] 조회 키는 프리팹 이름 문자열이 아니라 에셋 참조를 사용한다.
- [ ] 동일 `(SlotID, LayoutPrefab)` 엔트리가 둘 이상이면 검증 실패로 처리한다.

권장 인터페이스:

```csharp
[CreateAssetMenu(fileName = "ZoneRotationPresetCatalog",
    menuName = "VeyTrace/Zone Rotation Preset Catalog")]
public sealed class ZoneRotationPresetCatalogSO : ScriptableObject
{
    [System.Serializable]
    public struct Entry
    {
        public int SlotID;
        public GameObject LayoutPrefab;
        [Range(0, 3)] public int ExtraYawSteps;
    }

    public List<Entry> Entries = new();
    public bool TryGetExtraYawSteps(int slotId, GameObject prefab, out int steps);
}
```

### 배치 선택

- [ ] `Assets/1.Scripts/Map/LayoutPlacer.cs`에서 이미 존재하는 `ZonePlacement.ExtraYawSteps`를 실제로 채운다.
- [ ] `SelectLayouts`가 프리팹을 최종 선택한 다음 프리셋 카탈로그를 조회한다.
- [ ] 역할 프리팹과 전투 프리팹 모두 동일한 조회 경로를 사용한다.
- [ ] 프리셋이 없으면 해당 배치를 생성하지 않고 SlotID/프리팹/seed를 포함한 오류를 남긴다.
- [ ] `System.Random` 소비 순서를 회전 조회가 바꾸지 않도록 한다. 프리셋 조회에는 RNG를 사용하지 않는다.

### 실제 생성

- [ ] `Assets/1.Scripts/Map/MapContentSpawner.cs`에서 최종 회전을 `slot.rotation * ExtraYawSteps`로 적용한다.
- [ ] 위치는 계속 `slot.transform.position`을 그대로 사용한다.
- [ ] 위 직접 배치는 해당 프리팹의 루트 Pivot이 바닥 논리 중심과 일치한다는 에디터 검증을 통과한 경우에만 허용한다.
- [ ] 런타임 `CenterOnSlot` 또는 Renderer bounds 기반 재정렬을 다시 넣지 않는다.
- [ ] 생성된 각 존 루트에 원본 `SlotID`를 보존하는 식별 컴포넌트를 붙인다.

권장 식별 컴포넌트:

```csharp
public sealed class GeneratedZoneIdentity : MonoBehaviour
{
    public int SlotID;
}
```

### MapGenerator 참조

- [ ] `Assets/1.Scripts/Map/MapGenerator.cs`에 `ZoneRotationPresetCatalogSO RotationPresetCatalog` 참조를 추가한다.
- [ ] 카탈로그가 없으면 생성 시작 전에 즉시 오류를 내고 중단한다.
- [ ] `ZoneWiring`에서 프리셋 에셋 참조를 자동 연결하되 기존 엔트리를 임의로 초기화하지 않는다.

### Bake 안전성

- [ ] `Assets/1.Scripts/Map/Editor/BakeZones.cs`의 "가장 가까운 ZoneVolume" 매칭을 제거한다.
- [ ] `GeneratedZoneIdentity.SlotID`로 정확한 `ZoneSlot`과 원본 `ZoneVolume`을 찾는다.
- [ ] 한 SlotID가 두 번 Bake되거나 대응 볼륨이 없으면 전체 Bake를 중단한다.
- [ ] 이 Bake는 슬롯의 기준 위치/yaw를 보정하는 용도이며, 셔플된 특정 프리팹의 `ExtraYawSteps`를 ZoneVolume yaw에 섞어 굽지 않는다.

## 프리셋 저작 도구

- [ ] `Assets/1.Scripts/Map/Editor/ZoneRotationPresetEditor.cs` 생성.
- [ ] 선택한 GeneratedZone을 90° 단위로 회전해 통로에 맞춘 뒤 `(SlotID, 원본 프리팹, ExtraYawSteps)`를 저장하는 메뉴를 제공한다.
- [ ] 저장 시 현재 회전이 슬롯 기준 정확한 90° 배수인지 검사한다(허용 오차 0.1°).
- [ ] 같은 키가 이미 있으면 새 엔트리를 추가하지 않고 기존 값을 갱신한다.
- [ ] `Validate Rotation Presets` 메뉴에서 현재 카탈로그와 슬롯 후보 규칙으로 가능한 조합 전체를 열거한다.
- [ ] 현재 구성에서는 34개 조합이 모두 존재해야 성공한다.
- [ ] 검증 로그는 누락 조합, 중복 조합, 잘못된 step, 크기 불일치를 각각 구분해서 출력한다.

## 좌표/피벗 수정 작업

- [ ] `Assets/1.Scripts/Map/Editor/BuildZoneVolumes.cs`의 전체 Renderer 합산 경로를 제거하고, 명시적으로 지정된 바닥 Renderer만 합산하는 `TryFloorBounds` 경로로 교체한다.
- [ ] `TryFloorBounds`는 `ZoneFootprintSource.FloorRenderers`와 `ZoneFloorBoundsUtility.TryCalculateLocalBounds`만 사용한다. 규격 검사가 실패하면 해당 Zone 이름, 측정 크기, 입력된 모든 Renderer 경로를 출력해 벽/장식 오등록을 바로 찾을 수 있게 한다.
- [ ] 바닥 bounds 크기는 S `20×20`, M `20×40`/`40×20`, L `40×40`을 기준으로 검사한다. 초기 아트 측정 허용 오차는 축별 `0.05m`이며, 초과하면 슬롯을 갱신하지 않는다.
- [ ] 슬롯 중심은 기준 공간의 바닥 local bounds 중심을 `ZoneFootprintSource.transform.TransformPoint`로 월드에 변환해 사용하고 Y는 기존 floor 높이를 유지한다. 이를 Stage1/ZoneSlots 부모의 로컬 좌표로 저장하며, 런타임에서는 다시 bounds를 읽지 않는다.
- [ ] 프리팹 이름/Renderer 이름 문자열에 의존하지 않는다. 이름 검색은 기존 에셋의 최초 목록 작성을 돕는 에디터 메뉴에서만 허용하고, 저장된 Renderer 참조를 사람이 검토한 뒤 사용한다.
- [ ] 모든 Zone 프리팹에 대해 `root pivot ↔ floor-only bounds center`의 XZ 거리를 검사한다. 직접 슬롯 배치 방식의 통과 기준은 `0.05m` 이하다.
- [ ] Pivot 검증 실패 프리팹은 먼저 에디터에서 정규화하고 다시 검사한다. `MapContentSpawner`에 프리팹별/슬롯별 임시 position offset을 추가하지 않는다.
- [ ] Medium 프리팹은 90°/270°에서 논리 풋프린트가 `20×40 → 40×20`으로 정확히 교환되는지 검사한다.
- [ ] 고정 hallway 접합부와 Zone 출입구의 위치/방향은 소켓 기준으로 검사한다. 시각 Renderer bounds의 접촉/중첩은 연결 성공 조건이 아니다.

## 테스트 우선 구현 순서

### Task 0 — 바닥 논리 bounds와 Pivot 기준 복구

- [ ] `Assets/Tests/EditMode/Map/ZoneFloorBoundsTests.cs`와 EditMode 테스트 asmdef를 생성한다.
- [ ] `BuildZoneVolumes`가 전체 Renderer를 읽을 때 S `20.8×20.8`처럼 실패하고, `ZoneFootprintSource.FloorRenderers`만 읽을 때 `20×20`으로 통과하는 회귀 테스트를 먼저 작성한다.
- [ ] 비대칭 벽을 한쪽에 추가해도 바닥 논리 중심과 ZoneSlot 위치가 변하지 않는 테스트를 작성한다.
- [ ] 같은 Zone을 0°/90°/180°/270°로 돌려도 루트 Pivot과 논리 중심의 거리가 허용 오차 이내인지 검사한다.
- [ ] `ZoneFootprintSource`/`FloorRenderers` 누락, 규격 초과, Pivot 불일치 시 기존 슬롯 값을 일부만 변경하지 않고 사전 검증 단계에서 중단되는지 확인한다.
- [ ] 10개 슬롯을 `stage1_example`에서 다시 보정한 뒤 씬에 저장하고, 이후 `Test Generate`가 해당 고정 슬롯 값만 소비하는지 확인한다.

### Task 1 — 프리셋 조회 데이터

- [ ] EditMode 테스트 어셈블리와 `ZoneRotationPresetCatalogSOTests`를 만든다.
- [ ] 정상 조회, 누락 조회, 동일 키 중복, `ExtraYawSteps` 범위 오류 테스트를 먼저 실패시킨다.
- [ ] 최소 구현 후 테스트를 통과시킨다.

### Task 2 — LayoutPlacer 회전 전달

- [ ] 고정 슬롯/프리팹/프리셋으로 `ZonePlacement.ExtraYawSteps`가 예상값인지 테스트한다.
- [ ] 같은 seed에서 프리셋 추가 전후의 프리팹 선택 결과가 같아 RNG 소비 순서가 유지되는지 테스트한다.
- [ ] 역할 프리팹(Quest/Start/Boss)과 Combat 프리팹을 각각 검증한다.

### Task 3 — 생성 회전과 SlotID 보존

- [ ] `slot yaw=90°`, `ExtraYawSteps=2`일 때 생성 루트 최종 yaw가 270°인지 검증한다.
- [ ] 생성 루트 위치가 슬롯 위치와 동일하고 `GeneratedZoneIdentity.SlotID`가 보존되는지 검증한다.

### Task 4 — Bake 매칭 교체

- [ ] 서로 가까운 두 존을 의도적으로 배치해도 거리와 관계없이 SlotID 기준으로 올바른 볼륨에 매칭되는 EditMode 테스트를 작성한다.
- [ ] 중복/누락 SlotID가 씬 값을 일부만 변경하지 않고 사전 검증 단계에서 중단되는지 확인한다.

### Task 5 — 34개 조합 저작 및 검증

- [ ] L 9개, M 20개, S 5개 프리셋을 실제 통로에 맞춰 저장한다.
- [ ] 프리셋 검증기 결과가 `34/34`, 중복 0, 범위 오류 0인지 확인한다.
- [ ] 프리팹 피벗 및 Medium 회전 풋프린트 검증을 통과시킨다.

### Task 6 — 재현/랜덤 생성 검증

- [ ] `Wire` 후 seed `99`, `12345`를 각각 생성하여 모든 존이 통로와 연결되는지 확인한다.
- [ ] 랜덤 seed를 최소 20회 생성하여 가능한 L/M/S 셔플 조합에서 프리셋 누락 오류가 없는지 확인한다.
- [ ] 같은 seed를 두 번 실행해 프리팹, 역할, SlotID, 최종 yaw가 모두 동일한지 로그로 비교한다.
- [ ] MPPM Host + Client에서 동일 seed의 10개 배치 결과가 일치하는지 확인한다.

## 완료 조건

- [ ] 10개 ZoneSlot이 벽 제외 바닥 bounds 기준의 정확한 논리 중심과 S/M/L 풋프린트를 가진다.
- [ ] 벽/장식 Renderer를 추가·제거해도 이미 보정된 슬롯 위치와 런타임 생성 위치가 변하지 않는다.
- [ ] 모든 생성 대상 Zone 프리팹의 루트 Pivot과 바닥 논리 중심이 XZ `0.05m` 이내로 일치한다.
- [ ] 런타임 회전 계산 없이 모든 가능한 슬롯×프리팹 조합이 사전 프리셋으로 해결된다.
- [ ] `Test Generate` 반복 시 BossEnter, Quest, PlayerSpawn 셔플이 유지되면서 모든 통로가 열린 위치에 맞는다.
- [ ] 생성 존의 중심 좌표가 슬롯 중심에서 XZ 0.05m 이상 벗어나지 않는다.
- [ ] 프리셋 누락 시 잘못된 맵을 조용히 생성하지 않는다.
- [ ] Bake가 거리 기반 매칭을 사용하지 않는다.
- [ ] EditMode 테스트, 컴파일, seed 99/12345, 랜덤 20회, MPPM Host/Client 검증 결과를 최종 보고에 남긴다.

## 범위 밖

- Stage1 통로 자체의 절차 생성.
- 런타임 Renderer bounds를 이용한 위치/회전 재보정.
- 런타임 소켓 탐색/최적화 알고리즘.
- Stage2 이후 레이아웃 일반화.
- 새 Zone 디자인 또는 새 역할 추가.
