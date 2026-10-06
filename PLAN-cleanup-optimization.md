# PLAN — 전수조사: 파일 정리 · 일회용 코드 제거 · 렌더 피처 · 최적화 (초안, 승인 대기)

> 2026-10-05 · 경석(Claude) · 브랜치 `feature/Boss23`
> 조사 = Claude 서브에이전트 4갈래(파일 / 렌더 / 배칭·물리·빌드 / CPU) + Codex 교차검증 2갈래(성능 / 파일), 전부 읽기 전용.
> 로그(Debug.Log 류)는 빌드에서 빠지므로 범위 밖(팀장 지시).
> **이미 끝난 것**: `MonsterBase.HasParameter` 매 프레임 `animator.parameters` 할당 → 컨트롤러별 해시 캐시(컴파일 확인 · Play 미확인 · 미커밋).

## 0. 원칙

- **측정 먼저, 측정 나중.** `Assets/Settings/Build Profiles/ForProfile.asset`(Development + Profiler) 빌드로 **전후 같은 장면**(MapScene 진입 직후 L존 · 보스전)에서
  Profiler(CPU 메인/렌더 스레드 ms · GC.Alloc/frame) + Frame Debugger(드로우·SetPass·섀도 캐스터)를 찍는다. 숫자 없는 "최적화 완료"는 쓰지 않는다.
- 삭제는 **guid 참조 0 증명 + 종류 분류**(자기 .meta / 생성 캐시 / 주석)를 남긴 것만. 이름이 Legacy·NotUsed 라는 이유로 지우지 않는다(반례 4건 확인됨).
- **Unity 를 꺼야 하는 단계**(디렉터리 삭제·이동, SVN 바이너리, 패키지 핀)는 그 단계 직전에 따로 묻는다. 단일 `.cs`·텍스트 에셋은 켜 둔 채.
- 남의 영역(은희 = 네트워크·코어·HUD, 민경 = VFX, 아트 = SVN)은 **목록만 공유**, 직접 고치지 않는다(승인 시 예외).

## 1. 조사 결론 요약 (사용자 가정 대조)

| 가정 | 실제 |
|---|---|
| Static 없음 | **맞음** — 빌드 씬·환경 프리팹 static 플래그 0. 단 존은 런타임 Instantiate 라 **Static 체크해도 배칭 안 됨** |
| 같은 머티리얼 배칭 없음 | **부분** — SRP Batcher 켜짐(SetPass 는 묶임). 드로우 수는 그대로(L존 렌더러 557~584). 환경은 이미 아틀라스 2장 |
| 그림자 최적화 없음 | **맞음** — 환경 MeshRenderer 1,017 중 Cast On 1,001(바닥 포함 추정) · 거리 35 · 캐스케이드 2 · Soft High |
| GPU 인스턴싱 없음 | 환경은 꺼짐(VFX 44개는 켜짐). **SRP Batcher 가 켜져 있으면 인스턴싱은 무시** — 대신 GPU Resident Drawer |
| Static Collider 없음 | **틀림** — 환경 Rigidbody 0 → 이미 static collider. 예외 `MovingPlatform`(Rigidbody 없이 transform 이동) |
| (추가) 프레임 상한 | **없음** — vSync 0 + `targetFrameRate` 미설정 → 빌드 무제한 |

## 2. 단계

### S1 — 코드 소폭 수정 (Unity 켠 채 · 경석 영역 · 위험 낮음)
1. **프레임 상한** — 부트스트랩에서 `Application.targetFrameRate` (값은 Q3). 
2. `RetroCRTFeature.cs:130` 매 프레임 `new MaterialPropertyBlock()` → 필드 재사용.
3. `PlayerSilhouetteFeature` — 태그된 렌더러 0 이면 패스 등록 안 함(RT 2장 클리어·합성 생략).
4. `FogRendererFeature` CopyBack 블릿 제거(`resourceData.cameraColor = dest`, RetroCRT 와 같은 방식).
5. **Fog LOS**(`FogManager.cs:413,458,494`): 2프레임마다 레이캐스트 360회 × 모든 레이어 → 벽 레이어 마스크 · 플레이어 이동/회전 임계값 넘을 때만 재계산 · 블러 버퍼 재사용 · 변경 시에만 텍스처 업로드.
6. 미니맵: 1초마다 `FindObjectsByType<NetworkObject>` → 등록식, 탐사 비트 패킹은 dirty 일 때만(`MinimapNetworkSync.cs:53`).
7. `Player.cs:386`·`PlayerCorpseController.cs:248` `RaycastAll` → `RaycastNonAlloc` ⚠️ Player 는 은희 영역 — Q4.
8. 버퍼: `TwentyThreeBoss._grabBuffer` 16/8 → 상수 하나(16) · `GrabController` `Collider[3]` → 16.
9. `BossChargeClipLoop.cs:38` `GetCurrentAnimatorClipInfo` → List 오버로드 · `BossPatternTargets.cs:26` foreach → for.
10. `BossElectricFloor` 타일당 Instantiate/Destroy → 기존 `EffectPool`.
11. `SteamVent.cs:113` `r.material.color` → 공용 인스턴스/MPB.

### S2 — 몬스터 서버 FSM 주기 (경석 영역 · 동작 체감 변화 있음)
1. 추격 `NavMesh.SamplePosition`+`SetDestination` 매 프레임 → 0.15~0.25초 주기 또는 목표 0.5m 이상 이동 시. 09-28 가장자리 몰림 대응 로직 보존.
2. 타깃 없는 몬스터 인지 탐색 매 프레임 → 0.15~0.25초, 몹마다 위상 랜덤.
3. `IsAttackable` 의 `GetComponentInParent` ×2 매 프레임 → 타깃 획득 시 캐시.

### S3 — URP 설정 (에디터 UI 에서 · 룩 변화 → 팀장 눈 확인)
1. **SSAO** — Q2(끄기/경량화). 끌 경우 DepthNormals 프리패스가 데칼에 아직 필요한지 Frame Debugger 로 확인.
2. **그림자** — 거리 35 → 실제 카메라(FOV 34, 오프셋 7,17,-7) 최원점 기준으로 재계산 후 설정 · 캐스케이드 2 → 1 · Soft High → Medium.
3. 안 쓰는 기능 지원 끄기(셰이더 변형·빌드 시간): Light Cookies · 반사 프로브 블렌딩/박스 · Screen Space Lens Flare · 추가 라이트 그림자.
4. MapScene 볼륨 `VP_CombatLevel_Target` 에 Vignette·Bloom `skipIterations` **명시**(지금 기본 프로파일 Vignette 0.2 가 새어 들어옴 — 의도 확인 Q5).
5. 타이틀 UI 전용 카메라 2대 → 피처 없는 `UI_Renderer` 지정 + Depth/Opaque Off.
6. **GPU Resident Drawer** 시험: `PC_RPAsset` Instanced Drawing + Graphics `BRG Variants = Keep All`. 런타임 생성 존에 동작하는 유일한 배칭 수단. ForProfile 전후 비교 후 유지/원복 결정.
7. `ToonLit.shader:137` 실제로 안 쓰는 SSAO·혼합 그림자·ShadowMask multi_compile 제거 ⚠️ 은희 셰이더 — Q4.

### S4 — 환경 그림자 캐스터 (프리팹 다수 · git)
바닥·천장·바닥 데칼성 소품 `Cast Shadows = Off`. 대상 목록을 먼저 뽑아 보고 → 에디터 도구로 일괄(멱등) → 섀도 캐스터 수 전후 비교.

### S5 — 파일 정리 (증명된 것만)
**켜 둔 채 가능(단건)**
- 스크립트: `ZoneInteractRing`(+ `BossPatternVisuals.cs:8` cref) · `DevMoveSpeedProbe`(작성자 확인) · `BT/Actions/Event/ReStart`
- 참조 0 스크립트(Codex): `AnimClipUtility` · `IKnockbackSettable` · `AttackElement` · `EnableCollider` · `SpawnPointer` · `OverlapAttack` · `FragmentExploder` · `SceneBgmSwitcher` · `PlayerColorAssigner` — ⚠️ 절반이 은희 영역(Weapon·Player) → Q4. `EffectTestMover` 는 팀장 보존 지시 유지.
- 프리팹·머티리얼: `TempPlayer_Armature` · `Containers/Assemblies` 2종 · `Garen/PlayerAnimatorController`+FBX · Wells/Welz Toon mat 8종 · `MA_AreaZone_Fire` · `MA_CorpsePlaceholder` · `~Others/BaseColor` 5종+`Circle.mat` · `Controller_GauntletBot` · 고아 `AudioManager`/`TitleSceneManager` 프리팹 · `PP.asset`+`PP_Renderer.asset`
- `Assemblies/Level_wall_hallway.prefab`(3.4MB, 참조 0) → `layprefab.prefab` → `QuestLaserBlockerAuthoring.cs` 사슬
- `Legacy_WallOcclusionClip.hlsl` · FlatKit 구형 `RenderFeatures/Resources/` 셰이더 3개(Unity 2021 이하용) · TMP 미사용 `LiberationSans - Outline/Drop Shadow.mat`
- 빌드 목록에서 `Debug/TransparentV3.unity` 제외
- `Resources/NotoSansKR-VariableFont_wght.ttf`(10MB) → `Resources` 밖으로 **이동**(삭제 금지, 아틀라스 재생성용)
- 1회용 에디터 도구: `ZoneMonitorScreenAuthoring` 1·3·4(진단) · `MonsterAttackClipReport` · `FlatKitWaterPatchAuthoring` 진단 0~0g · 로컬 `BossClipMotionProbe`
- 루트: `TalkFile_AGENT.md.md`(git) · 끝난 PLAN 4개 → `Docs/history/PLANS/`(링크 수정 동반) · 로컬 잡동사니(`TempToybox` 497MB · `network.log` · `*.bak` · `McpAudit_*.csproj` · `작성` · `build_log.txt` · `ProfilerCaptures`)

**🔴 Unity 꺼야 함(그 단계 직전에 다시 묻는다)**
- `Assets/_Legacy/Wells&No.23/` · `4.Animations/R1`+`3.Materials/R1` · 빈 폴더 `.meta` 11개 · `INab Studio/Demo Assets`(160MB, git)

**확인 후(남의 판단)**
- `Player/Legacy/Paladin.prefab` 의 `DefaultNetworkPrefabs` 등록 해제 — 빌드 도달 30개 파일 끊김. 플레이어 프리팹 = 은희 영역 + `player-prefabs.md` 갱신.
- `Assets/legacy` 안 `PF_Stage_01_V3.prefab`(16MB, 참조 0) — 상위 폴더는 사용 중.
- 구 물 계통(`WaterBedAuthoring`·`WaterShoreMaskBaker` 경로 상수 낡아 동작 불가 + 메시 2MB) — Flat Kit 물 확정 후.
- SVN: `50.Art/VFX/**/OldVersion`(4MB) · `SurfaceV1` 원본 FBX·PNG(487MB) — 아트 담당.

### 범위 밖 — 목록만 공유
- 네트워크(은희 합의 필요): `_animSpeed` 양자화 · 몬스터 투사체 NGO 풀 + NetworkTransform 제거 · NetworkTransform 38개 회전 X/Z·스케일·임계값·HalfFloat · 보호막 `NetworkList` 전체 재직렬화(`Unit.cs:452`) · 먼 몬스터 관찰자 관리.
- 레이어 충돌 매트릭스(161/190 켜짐) — 회귀 위험 큼, 쿼리 마스크 전수 대조가 선행.
- IL2CPP 전환 — 빌드 시간·NGO 코드젠 회귀 확인 필요. 측정용 1회 비교만 제안.
- HUD 매 프레임 문자열(은희) · `MovingPlatform` kinematic Rigidbody(원본이 SVN).

## 3. 검증

| 단계 | 검증 |
|---|---|
| 매 단계 | Unity 직접 켜서 컴파일 — `Editor.log` `error CS` 0 + DLL 갱신 확인 · 전투 EditMode 테스트 |
| S1·S2 | Play: MapScene 몬스터 추격·어그로·공격 · Fog 시야 경계 · 미니맵 · 23호 잡기/송전기 · **MPPM 2인** |
| S3·S4 | ForProfile 빌드 전후 숫자(렌더 스레드 ms · 드로우 · SetPass · 섀도 캐스터 · GC.Alloc) + 스샷 비교(팀장 눈) |
| S5 | 삭제마다 guid 재검색 · 컴파일 · `.meta` guid 불변 · 빌드 1회(누락 참조 = Missing 경고 0) |

## 4. 결정 (팀장 2026-10-05)
- **Q1 이번 라운드 = S1 + S2.** S3·S4·S5 는 다음 라운드(문서에 남겨 둠).
- Q2 SSAO = **끄고 스샷 비교**(S3 때).
- Q3 프레임 상한 = **`Application.targetFrameRate = 144`** (나중에 옵션 메뉴로 뺄 수 있게 한 곳에서).
- Q4 은희 영역 = **목록만 공유**(CONTEXT.md). 그래서 S1-7(`Player`·`PlayerCorpseController` RaycastAll)은 이번 커밋에서 빠진다.
- Q5 MapScene Vignette 의도 = 미정(S3 때 확인).

### 이번 라운드 작업 목록 (확정)
S1: 1 프레임 상한 · 2 RetroCRT MPB · 3 실루엣 조기 종료 · 4 Fog CopyBack · 5 Fog LOS · 6 미니맵 · 8 버퍼 · 9 보스 소소 할당 · 10 전기 장판 풀 · 11 SteamVent
S2: 1 추격 재경로 주기 · 2 인지 탐색 주기 · 3 타깃 컴포넌트 캐시
+ 이미 끝난 `MonsterBase.HasParameter` 캐시.

### 진행 (10-06) — 구현 ✅ · 컴파일·전투 EditMode 108/2(기존 실패 2) · Play 런타임 에러 0(시작 지점 대기만, 전투 경로 미실행)
| # | 상태 | 비고 |
|---|---|---|
| S1-1 프레임 상한 | ✅ | `Managers/FrameRateCap.cs`(BeforeSceneLoad, 144) |
| S1-2 RetroCRT MPB | ✅ | 단일 캐시 ✗(RenderGraph 기록/실행 분리 → 카메라별 값 덮음) → 프레임별 풀(상한 8) |
| S1-3 실루엣 조기 종료 | ✅ | `PlayerSilhouetteTag.ActiveCount` 0 이면 패스 미등록 |
| S1-4 Fog CopyBack | ✅ | `resourceData.cameraColor = dest` |
| S1-5 Fog LOS | ✅ | 5cm 이동 또는 0.5초마다만 재빌드 · 블러 O(n) 슬라이딩·버퍼 재사용. 레이 마스크(전 레이어)는 씬·룩 변경이라 보류 |
| S1-6 미니맵 | ✅ | 매초 검색 대상 NetworkObject→Player · 로컬 판정 캐시 · 탐사 비트는 `ExploredVersion` 바뀔 때만 패킹 |
| S1-7 Player RaycastAll | ⏭ 은희 | 목록 공유 |
| S1-8 버퍼 | ✅ | 23호 `_grabBuffer` 16 고정 · `GrabController` 3→16 |
| S1-9 보스 할당 | ✅ | ClipInfo 리스트 오버로드 · ConnectedClientsList 인덱스 순회 |
| S1-10 전기 장판 VFX | ✅ | 칸별 인스턴스 재사용(`_viewRoot` 하위, 끄고 켜서 재생) — EffectPool 은 EffectEntry 데이터가 필요해(민경 영역) 안 씀 |
| S1-11 SteamVent | ⏭ 불필요 | 디버그 큐브 생성 1회뿐(1차 조사 과대평가) |
| S2-1 추격 재경로 | ✅ | 0.2초(+지터) 또는 타깃 0.5m 이동·외부 정지/목적지 변경 시 즉시. 실패 재시도도 주기 |
| S2-2 인지 탐색 | ✅ | 실패 후에만 0.2초(+지터) |
| S2-3 타깃 컴포넌트 캐시 | ⏭ 보류 | 공용 `MonsterTargeting` 변경 — 효과 대비 범위 큼 |

### S3 (10-06) — 에디터 측정(MapScene 시작 지점, 진입 8초 뒤 10초 평균, 상한 해제)
| | 기준 | SSAO 끔 + 그림자 28m·1캐스케이드·Soft Medium + 쿠키 끔 |
|---|---|---|
| 렌더 스레드 | 3.74ms | **2.82ms** (−25%) |
| 메인 | 3.08ms | 2.85ms |
| GPU | 2.18ms | 1.91ms |
| 드로우 / 그림자 캐스터 | 639 / 287 | 485 / **139** |
스샷 비교: 거의 동일(컨테이너 모서리 음영만 약간 옅음).
- **유지(룩 영향)**: 추가 라이트 그림자(타이틀 스포트 2개 사용) · 반사 프로브 블렌딩/박스(타이틀 프로브 1) · Screen Space Lens Flare(타이틀 PostFX 켬 → 사용).
- **GPU Resident Drawer — 보류**: 드로우 486→193 이지만 에디터 전체 프레임 6.85→7.77ms, FPS 146→120~129(2회). 시작 방(드로우 ~500)에선 고정비가 이득보다 큼 → **L존 + ForProfile 빌드**에서 재측정 후 결정. ⚠️ `m_BrgStripping` 은 0=Entities 있을 때만 · **1=전부 제거** · 2=모두 유지(1차 조사의 "0→Keep All" 추론 틀림 — 켤 때 2).
- 타이틀 UI 카메라 2대 Depth/Opaque 끄기 — **보류**: 아트가 타이틀 씬 작업 중(충돌 회피), 효과도 타이틀 한정.
- MapScene Vignette 0.2 새어 들어옴(Q5) — 팀장 확인 대기.

### S4 (10-06) — 지면 바닥 Cast Shadows Off ✅
- 도구 `Tools/Map/Authoring/지면 바닥 그림자 끄기 (보고만 / 적용)`(`Map/Editor/FloorShadowAuthoring.cs`, 멱등). 존 9종 **지면 350 Off · 높은 곳 90 유지**.
  판정 = (Ground 레이어 또는 이름 floor/ground/tile) + 두께 &lt; 0.6m − 조명류, 지면 = 존 **최빈** 바닥 윗면 + 0.3m 이하. Quest01 은 최빈이 −2.9(꺼진 구역)라 0m 바닥 17개는 유지(아래로 그림자 보임).
- 방식 = 존 프리팹 **배치별 오버라이드**(git, SVN 원본 무수정) — Codex 교차검증도 같은 권장(프리팹 일괄 ✗: `floor_stone` 이 지면 89·높은 곳 22 혼용 / 런타임 높이 판정 ✗: 다층·이동 플랫폼에 취약).
- 검증: 9개 파일 오버라이드 집합 전후 비교 = `m_CastShadows` +350 외 추가·제거 0(보스룸 1/0.98 스케일 보정은 순서만 바뀜). StageTutorial 20줄은 float 표기(−110.904875→−110.90488).
- 시작 방 측정: 캐스터 139→135(이 방은 큰 타일 몇 장 — 캐스터 대부분이 벽·소품). L존 효과는 이 시야로 안 잡힘 → 빌드 측정 때 같이.

### 묶어 그리기 — Codex 권장(10-06, 미착수)
**에디터 사전 메시 병합**(고정 소품·지면 타일을 재질·공간 단위로 합쳐 git `.asset` 저장, `MeshUtility.AcquireReadOnlyMeshData` 로 SVN Read/Write 변경 없이). 런타임 `StaticBatchingUtility.Combine` 은 Unity 6.3 도 Read/Write 필요 → 불가. GRD 는 빌드 측정 전 OFF 유지.
주의: 병합 단위가 크면 일부만 보여도 통째로 그려진다(삼각형·그림자 GPU 증가 확인) · 이동 플랫폼·파괴 상자·게이트·컨베이어·MPB 대상·벽 투명화 그룹 제외 · 콜라이더·게임플레이 계층 유지.
그 밖 캐스터 후보(중): 가는 파이프 부품 · 저폴리 그림자 프록시(ShadowsOnly). 작은 장식(하).

⏳ **Play 미검증**: 몬스터 추격·어그로 체감(S2) · 전기 장판 VFX 재발동 · 미니맵 탐사 동기화(MPPM) · 타이틀 CRT(카메라 3대) · Fog 시야 경계.
🔎 발견(정리 후보): Visual Scripting `UnitOptions.db` 가 삭제된 타입(Enemy·ChargeController 등) 역직렬화 예외를 Play 마다 ~1500건 쏟는다 — S5 "VS 생성물 재생성/제거"와 같은 뿌리.
