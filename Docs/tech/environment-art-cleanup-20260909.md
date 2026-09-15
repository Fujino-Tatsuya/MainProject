# 환경 아트 통합 분류 완료 — 2026-09-09

> 후속 SVN 복구: 업데이트 과정의 충돌 9건을 CLI로 해결하고 서버 r295의 변경을 새 위치의
> 18개 파일에 병합했다. 현재 충돌·누락·업데이트 필요 항목은 0건이며, 890개의 경로·GUID를
> 재확인했다. 아래 내용 보존 검증은 재분류 완료 시점 기준이고, 이후 서버 설정 변경은
> 아래 SVN 복구 기록과 `Generated/svn-conflict-fix-20260909/verification.json`에 기록했다.

최초 승인 범위의 환경용 모델·텍스처·프리팹·머티리얼 **890개**를 정리했다.
사용 영역 **389개**는 사용자 후속 요청에 따라 출처 이름을 없애고 용도로 다시 분류했다.
**legacy 501개**는 보존했다. 파일명·GUID·기존 사용자 변경과 Git/SVN 관리 경계를 유지했다.
사용자 후속 요청에 따라 Git 변경을 `codex/art-resource-folder-cleanup`에서 게시한다.
열려 있는 타이틀 PR #11의 `fix/Art_title`을 기준으로 폴더 정리 변경을 검토한다.

## 현재 분류 체계

모델·텍스처·프리팹에 다음 공통 용도 이름을 사용한다. 해당 종류가 있는 폴더만 생성했다.

| 폴더 | 용도와 하위 분류 |
|---|---|
| Architecture | Floors, Walls, Doors, Stairs, Backgrounds — 바닥·벽·문·계단·배경 |
| Machinery | Conveyors, Platforms, Cranes, PowerUnits, Industrial, Lasers, Teleporters, Ventilation |
| Props | Containers, Chains, Office, Furniture — 적재물·체인·사무 소품·가구 |
| Pipes | 배관 부품과 배관 묶음 |
| Lighting | Fixtures, Skyboxes, Reflections — 조명·HDRI·반사 텍스처 |
| Layouts | Stages, Zones, Stores — 완성된 스테이지·존·상점 묶음 |
| Colliders / Decals / Markers | 충돌 메시 / 데칼 / 구형 영역 표시용 마커 |
| Shared | Atlases, Surfaces — 여러 사물에 걸쳐 쓰는 아틀라스·기본 표면 |

`Level1`, `LevelDeliveryV3`, `MapGen`, `Title` 출처 폴더는 사용 영역에서 제거했다.
동명 프리팹은 `Parts`/`Assemblies`/`Sections`, 동명 텍스처는 `Surface01`/`Surface02`로 구분했다.
별개 GUID를 가진 파일을 병합하거나 이름을 바꾸지 않았다.

| 관리·내용 | 현재 루트 |
|---|---|
| SVN 모델·텍스처·머티리얼·프리팹 | `Assets/50.Art/Environment/{Models,Textures,Materials,Prefabs}/` |
| Git 프리팹 | `Assets/2.Prefabs/Environment/` |
| Git 머티리얼 | `Assets/3.Materials/Environment/` |
| Git 모델·텍스처 | `Assets/Environment/{Models,Textures}/` |
| SVN legacy | `Assets/50.Art/legacy/Environment/` |
| Git legacy | `Assets/legacy/Environment/` |

예전 탐색기 경로는 폴더가 이동되어 열리지 않을 수 있다. 위 루트의 용도별 폴더 또는
[전체 이동표](environment-art-moves-20260909.json)의 `destination`에서 현재 위치를 찾을 수 있다.

## transparent 맵 머티리얼 분리

독립 `.mat` 파일 57개를 **`UsedInMap` 8개 / `NotUsedInMap` 49개**로 나눈 뒤 용도로 세분했다.
두 머티리얼 루트(SVN/Git)에 동일한 구분을 적용했다. FBX 내장 머티리얼은 모델 내부에 그대로 두었다.

사용 기준은 저장된 `4.MapScene-trensparent.unity`의 실제 Renderer.sharedMaterials와 스카이박스,
실제 생성에 쓰이는 ZoneLayoutCatalog의 프리팹 11개 렌더러, Stage1의 실제 벽 투명화 교체 슬롯이다.
비활성 배치도 포함하며 특정 프레임에 화면에 보인다는 뜻은 아니다.
다른 씬에서 사용하거나 단순 의존성이 있는 머티리얼은 이 맵의 사용으로 계산하지 않았다.

| 사용 머티리얼 | 관리 | 사용 근거 | UsedInMap 하위 위치 |
|---|---|---|---|
| `ConvayorBelt_Corner_Mat.mat` | Git | 생성 존 프리팹 | `Machinery/Conveyors` |
| `ConvayorBelt_Mat.mat` | Git | 생성 존 프리팹 | `Machinery/Conveyors` |
| `Generic_01_A.mat` | SVN | 생성 존 프리팹, 씬 렌더러 | `Shared/Atlases` |
| `Generic_01_A_Occlusion.mat` | Git | Stage1 런타임 교체 | `Shared/Atlases/Occlusion` |
| `MA_prop02.mat` | SVN | 씬 렌더러 | `Shared/Atlases` |
| `PolygonConstruction_01_A.mat` | SVN | 생성 존 프리팹, 씬 렌더러 | `Shared/Atlases` |
| `Skybox_IndustrialFoundry.mat` | Git | 씬 스카이박스 | `Lighting/Skyboxes` |
| `WaterDark.mat` | Git | 씬 렌더러 | `Water` |

`NotUsedInMap` 49개는 타이틀 계열 19개, 실제 배정되지 않은 소스 머티리얼 12개,
구형 마커 5개, 현재 Stage1에서 교체되지 않는 오클루전 변종 13개다.
이 맵에서 사용하지 않는다는 뜻이며 다른 씬/저작 설정에 필요한 파일은 보존한다.

MapGenerator.Catalog의 예전 노드·마커 항목은 현재 생성 코드에서 배치하지 않는다.
WallOcclusionSettings에 등록된 14쌍도 전부 사용으로 세지 않았다. 실제 Stage1 슬롯에 대응하는
`Generic_01_A_Occlusion`만 사용 쪽이다. 맵 또는 런타임 카탈로그가 변경되면 분류를 다시 점검해야 한다.
각 머티리얼의 근거와 최종 경로는 전체 이동표의 `transparent_map_use`에 기록했다.

## 최초 사용/legacy 판단 범위

MainFlow의 `0.BootStrapScene`, `1.TitleScene`, `2.LoadingScene`, `3.LobbyScene`,
`4.MapScene-trensparent`, `5.ResultScene`와 `Assets/0.Scenes/Art/title.unity`의 7개 씬을 기준으로 했다.
`4.MapScene.unity`와 나머지 씬은 시작점에서 제외했다. 중첩 프리팹·비활성 오브젝트·임포터 텍스처까지
Unity 의존성 및 GUID 그래프로 추적했다. 이 넓은 보존 기준과 위의 맵 머티리얼 실제 사용 기준은 다르다.

| 종류 | 사용 영역 | legacy | 합계 |
|---|---:|---:|---:|
| 모델·충돌 메시 | 143 | 151 | 294 |
| 텍스처·HDRI | 47 | 17 | 64 |
| 프리팹 | 142 | 319 | 461 |
| 머티리얼 | 57 | 14 | 71 |
| 합계 | **389** | **501** | **890** |

캐릭터·UI·전투 VFX·오디오·외부 패키지·셰이더 소스·씬·게임플레이 설정 데이터는 이동하지 않았다.
최초 이동은 SVN 382 / Git 508개, 이번 용도 재분류는 SVN 211 / Git 178개다.
빈 폴더는 최초 49개와 이번 48개를 정리했고, 폴더 GUID 참조와 실제 비어 있음을 확인했다.

## 참조 보존 및 저작 도구

- 최초 이동에서 필요한 FBX 68개의 원래 텍스처 GUID remap을 유지했다. 이번 추가 remap은 0개다.
- BossRoomAuthoring, QuestLaserBlockerAuthoring, ZoneBridgeGateWiring, MonsterSceneBossSetup,
  GroundLayerAuthoring, WallOcclusionAuthoring이 현재 환경 경로를 사용한다.
- 벽 투명화 도구는 두 사용 구분을 함께 검색하여 기존 변종 14개를 현재 위치에서 갱신한다.
  마커는 제외하고, 새로 만드는 미배정 변종은 같은 용도의 NotUsedInMap에 둔다.
- SVN은 공개 Subversion API로 원래 파일에서 최종 위치로의 이동 이력을 유지했다.
  SVN DB를 직접 수정하지 않았다. `.meta`는 파일과 함께 이동했다.
- 이번 작업은 열린 씬을 저장하거나 닫지 않았다. 임시 Unity 도구는 검증 후 Assets에서 제거했다.

## 검증

Unity **6000.3.16f1**에서 재임포트 및 실제 에셋 로딩으로 확인했다.

| 확인 | 결과 |
|---|---|
| 전체 890개 파일·meta·GUID·최종 위치 | 통과. 최초 승인된 68개 remap 외 원본 내용 동일 |
| 이번 389개 재분류 내용 변화 | 0건 |
| SVN asset/meta 이동 이력 764개 | 원래 위치에서 최종 위치까지 일치 |
| 전체 에셋의 직접 의존성 | 경로 변환 후 변화 0건 |
| 모델 하위 에셋·머티리얼 14,139개 | 참조 변화 0건; Unity의 미배정 null 프로퍼티 열거 차이 3개만 제외 |
| 대상 7개 씬 오브젝트·메시·머티리얼·스카이박스 | 변화 0건 |
| 대상 씬 Missing Script / 빈 MeshFilter / 빈 Material 슬롯 | 모두 0건 |
| 맵 머티리얼 사용 근거 | 이동 전후 일치 |
| 저작 도구 고정 경로 17개, 소스/변종 14쌍 | 모두 기존 에셋으로 해석 |
| 대상 7개 씬의 legacy 의존 | 0건 |
| 누락 파일·meta / 중복 GUID / 범위 밖 파일 변경 | 0건 |
| 에디터 컴파일 / git diff --check | 통과 |

화면 육안 비교, Play 조작, MPPM 검증은 수행하지 않았다. 게임플레이와 네트워크 동작은 변경하지 않았다.

## 기록과 백업

- [전체 최종 이동표](environment-art-moves-20260909.json): 890개의 원본→최종 경로, 중간 경로, GUID, 용도, 머티리얼 사용 근거.
- `Generated/art-cleanup-20260909/`: 최초 정리의 백업과 검증 기록(당시 경로를 보존한 역사 기록).
- `Generated/art-cleanup-20260909/refinement/`: 이번 백업 `baseline.zip`, 이동표, SVN/Git 실행 로그,
  `verification.json`, `cumulative-verification.json`, `material-usage.json`, 저작 경로 검증과 빈 폴더 meta 백업.

Generated는 로컬 작업 기록이며, 공유용 최종 경로는 이 보고서와 Docs의 이동표를 기준으로 한다.

## 인수 확인용 세 문항

1. NotUsedInMap에 있는 머티리얼을 모두 삭제해도 되는가?
2. 파일 이동 후에도 씬의 연결을 유지하는 식별자는 무엇인가?
3. 오클루전 설정의 14개 변종 중 UsedInMap에는 몇 개가 들어가는가?

정답: 1. 아니다. 다른 씬·프리팹·저작 설정이 참조할 수 있다. 2. `.meta`의 GUID와 하위 에셋 fileID.
3. 현재 Stage1에서 실제 교체되는 Generic_01_A_Occlusion 1개다.

## SVN 업데이트 충돌 복구

정리 후 r279→r295 업데이트가 들어오면서 트리 충돌 8건과 VFX 머티리얼 충돌 1건이 발생했다.
SVN CLI 1.14.5로 기준·로컬·서버 파일을 비교하여 새 위치의 18개 파일에 병합한 뒤 충돌을 해제했다.
노멀맵 메타 10개, 읽기 허용 설정 모델 메타 4개, 구형 래퍼를 제거한 충돌 메시 3개에 서버 수정을 반영했다.
메시 데이터와 fileID, 모델 텍스처 remap과 GUID는 유지했다. OrbWater 머티리얼은 서버의 줄바꿈 변경만
겹친 경우여서 기존 로컬 내용을 보존했다. 서버 r295 대비 충돌·누락·업데이트 필요 항목이 모두 0건이다.
SVN이 로컬 파일 281개의 이동쌍 표시를 해제했지만 원래 경로/리비전에서의 이력 있는 복사와
원본 삭제 기록은 그대로 유지한다. 이 복구에서 서버 커밋이나 Unity 재임포트는 실행하지 않았다.

## Git PR 범위

Git 환경 에셋 508개와 meta의 이동, 폴더 meta, 에디터 저작 도구 6개, 계획과 정리 보고서를 포함한다.
이미 추적 중이던 에셋 내용은 기준 커밋의 내용을 새 경로에 그대로 기록하여 이동만 검토할 수 있게 했다.
이동 대상에 원래 남아 있던 내용 수정과 기타 캐릭터 머티리얼·패키지·복구 파일의 로컬 변경은 보존한다.
SVN 아트 파일 자체는 Git PR에 들어가지 않는다. 팀 환경에서도 SVN의 새 Environment/legacy 경로가
함께 제공되어야 한다. 위 Unity 검증은 작업 사본 기준이며 커밋 대상에는 기존 에셋의 내용 변경이 없다.
