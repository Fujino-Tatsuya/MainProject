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

---

## S6 — 존별 실시간 측정 (10-06 2차 · 🟡 승인 대기)

### 결정 (팀장 10-06)
- **예산 = 이 PC(i9-13900KF · RTX 4060 Ti) 200fps(5.0ms), 개발 빌드·상한 해제 기준.** HUD 색·초과 비율 모두 이 기준(인스펙터 값, 기본 200).
  처음 144(6.94ms)로 잡았다가 팀장 판단으로 상향(10-06). 근거: 에디터 시작 방 실측이 렌더 2.82·메인 2.85·GPU 1.91ms(§S3)라 빌드 기준 200 은 여유가 있어야 정상.
  ⚠️ 측정 예산(200)과 출시 프레임 상한(`FrameRateCap` 144)은 별개 — 상한은 이번에 안 바꾼다.
- **결정은 개발 빌드 숫자로**, 에디터는 빠른 확인용(GRD 에디터 숫자가 거꾸로 나온 전례 — §S3).
- **이번 범위 = 측정 도구까지.** 존별 표를 보고 다음(GRD·메시 병합·그림자)을 고른다.
- 🔴 **존 진입·이탈 때 렌더 설정을 바꾸지 않는다(채택 안 함).** 근거:
  - GRD 모드 실행 중 변경 = 내부 구조 폐기·재생성 + 전 렌더러 재등록 → 경계마다 렉.
  - 그림자 거리·캐스케이드 변경은 싸지만 경계에서 그림자가 튄다. 존은 복도로 이어져 두 존이 한 화면에 보인다.
  - 에디터 Play 중 URP 에셋 값 변경은 **에셋 파일에 저장된다**(git diff).
  - 존은 맵 시작 때 전부 생성·상시 활성(`MapContentSpawner`) → 존별 비용 차이 = 카메라에 보이는 콘텐츠 차이.
    → 존별 숫자는 **콘텐츠 수정(캐스터·병합)의 근거**로 쓰고, 전역 설정은 가장 무거운 존에 맞춰 하나로.
    콘텐츠로 안 내려가는 존이 숫자로 확인될 때만 재검토.

### 구현
1. **`ZonePerfRecorder`** 신규(`Dev/Profiler/`, 본문 `#if UNITY_EDITOR || DEVELOPMENT_BUILD` — 클래스는 항상 존재해 릴리스 빌드 Missing Script 경고 없음).
   - 존 판정: `GeneratedZoneIdentity` 마다 렌더러 합친 XZ 범위를 1회 계산(존 수 바뀌면 재계산) → **로컬 플레이어**(`LocalClient.PlayerObject`, 없으면 메인 카메라) 위치가 든 존. 없으면 `복도/기타`.
     키 = 원본 프리팹 이름(+ 역할 BossRoom 표시). 맵이 시드마다 달라도 **존 종류별로 실행 간 비교 가능.** 슬롯 ID 는 보조 열.
   - 매 프레임 누적(존별): `FrameTiming` CPU 메인·**렌더 스레드**·GPU·전체 + `ProfilerRecorder` 드로우·SetPass·삼각형·**그림자 캐스터**·GC 할당.
     평균·최대·p95(0.25ms 히스토그램, 할당 0)·**5.0ms 초과 프레임 비율**(+ 6.94ms 초과 = 144 상한에서 실제로 프레임이 떨어지는 비율)·체류 초.
   - 화면: 현재 존 이름 + 그 존 누적 평균/초과 비율 한 줄(IMGUI, F8 HUD 아래).
   - **F9** = CSV 저장 + 콘솔 요약 표 · 종료 시 자동 저장. 에디터 `Logs/ZonePerf/`, 빌드 = exe 옆 `ZonePerf/`.
     CSV 머리에 조건 기록: 에디터/빌드 · GRD 켜짐 · 그림자 거리 · 프레임 상한 · 해상도 · 맵 시드(가능하면).
   - **F10** = 프레임 상한 144 ↔ 해제 토글(측정은 상한 해제가 기본 — 상한 대기 시간이 CPU 값에 섞임). 바꾸면 누적 초기화.
2. **`ProfilerHUD`**: 렌더 스레드 ms·그림자 캐스터 수 표시 추가(같은 숫자를 HUD 에서도).
3. **빌드 메뉴** `Build/Windows64 Player (MainFlow · 측정용 Development)` — `BuildOptions.Development` 만(프로파일러 자동 연결·딥 프로파일 ✗ = 오버헤드), 출력 `../MainProjectBuilds/WindowsDev/`. 릴리스 메뉴는 그대로.
4. `4.MapScene` 의 `ProfilerHUD` 오브젝트에 `ZonePerfRecorder` 부착(씬 1개 텍스트 변경 — Unity 켠 채 가능).

### 리스크 / 확인할 것
- `FrameTiming` 렌더 스레드 값·`Shadow Casters Count` 카운터가 이 플랫폼(D3D)·빌드에서 실제로 나오는지 → 안 나오면 `N/A` 로 표시(0 으로 찍지 않는다 — 0 은 거짓 신호).
- 빌드는 데이터 테이블 훅으로 **xlsx 값**이 들어간다 → 몬스터 속도가 옛 값(남은 일 3). 렌더 측정엔 영향 작음, CSV 메모로만.
- 존 경계 판정은 렌더러 범위 기준이라 복도·존 겹침 구간이 한쪽으로 들어갈 수 있다 — 체류 초가 짧은 존은 판단에서 뺀다.

### 검증
- 컴파일(`Editor.log` `error CS` 0) · 전투 EditMode(기존 실패 2 외 0).
- 에디터 Play(Host) MapScene: 존 2개 이상 걸어서 → F9 CSV 에 존별 행, 체류 초 > 0, 렌더 스레드·캐스터가 N/A 가 아닌지. HUD 숫자와 Unity Profiler 같은 프레임 대략 일치.
- 개발 빌드 1회: HUD·CSV 생성 확인. 릴리스 빌드 경로는 컴파일만(Missing Script 없음).
- 측정 절차(도구 완료 후 팀장): 개발 빌드, 상한 해제, 시작 방 → L·M·S·퀘스트 존 → 보스룸을 각 20초 이상 체류 → F9.

### S6 진행 (10-06) — 도구 ✅ · 에디터 검증 ✅ · 개발 빌드 ⏳
- 구현: `Dev/Profiler/ZonePerfRecorder.cs`(신규) · `ProfilerHUD` 렌더 스레드·그림자 캐스터 · `Build/Windows64 Player (MainFlow · 측정용 Development)` · `4.MapScene` 부착(+ `ProfilerHUD.targetFps` 120→200).
- 키 = `Home` 저장 / `End` 상한 토글 / `Insert` 초기화 (F1~F12 는 전부 다른 디버그 키가 사용 중). Play 종료 시 자동 저장.
- 🔴 실행은 **Dev Boot**(`Dev_Boot.unity` Play) 로만 — `4.MapScene` 직접 Play 는 검은 화면.
- 첫 실행에서 고친 것: `FrameTiming.gpuFrameTime` 이 NaN 을 줘 히스토그램 인덱스가 음수 → 유한하지 않은 값·10초 초과 버림.
- ⚠️ 발견(기존 문제): `Build/` 메뉴 빌드(릴리스 포함)는 `BuildPipeline.BuildPlayer` 직접 호출이라 데이터 테이블 훅(`DataTableBuild`, Build 버튼 전용)을 안 거친다 → **xlsx 가 아니라 인스펙터 값으로 빌드**. 측정엔 무해, 출시 빌드 경로는 은희와 정리 필요.
- ⚠️ MCP `simulate_key` 로 넣은 `Home` 은 게임에 안 닿았다(에디터 포커스 없음 추정) — 사람이 누르는 키는 미확인. 자동 저장 경로는 확인.
- ✅ 개발 빌드(10-06): `../MainProjectBuilds/WindowsDev/MainProject.exe` · 2분 52초 · 725MB · 오류 0. 빌드 DLL 에 측정기 본문(저장 로그 문자열·`SaveCsv`) 포함 확인. CSV = exe 옆 `ZonePerf/`.
  ⚠️ 빌드할 때마다 부산물이 생긴다 — `PC_RPAsset` 셰이더 프리필터(SSAO 칸) · URP Global Settings rid 2개 · `ProjectSettings.preloadedAssets` 에 Behavior App UI Settings · `ScriptableBuildPipeline.json` · `AddressableAssetsData/link.xml`. **커밋하지 말고 되돌릴 것**(이번에도 되돌림, 재오염 없음 확인).

**에디터 첫 실측**(시드 307900784, 상한 해제, 1920×1080, 81초, 팀장 직접 플레이) — 판단용 아님(에디터 오버헤드):
| 존 | 초 | Frame avg/p95 | Main | Render | GPU | 드로우 | 그림자 캐스터 |
|---|---:|---|---:|---:|---:|---:|---:|
| ZoneS_typeA | 4.8 | 6.79 / 7.75 | 2.87 | 1.36 | 1.22 | 656 | 155 |
| 복도/기타 | 29.5 | 6.38 / 8.50 | 2.89 | 1.45 | 1.27 | 347 | 70 |
| ZoneM_typeA | 8.5 | 6.34 / 8.00 | 2.83 | 1.26 | 0.99 | 449 | 118 |
| ZoneS_typeStart | 11.1 | 6.29 / 8.25 | 2.75 | 2.08 | 1.15 | 535 | 131 |
| ZoneL_typeB | 24.0 | 5.99 / 7.50 | 2.69 | 1.22 | 0.95 | 549 | 137 |
| ZoneS_typeBossEnter [BossRoom] | 3.5 | 5.51 / 7.25 | 2.44 | 1.16 | 0.92 | 333 | 88 |
읽는 법: Frame(≈6ms) 이 Main+Render+GPU 어느 것보다 훨씬 크다 → **에디터 고정비가 프레임을 지배**. 게임 자체 비용(Main ≈2.8ms, GPU ≈1ms)은 존마다 차이가 작다. 존 판단은 개발 빌드 숫자로(§S6 결정 그대로).

**빌드 1차 측정 유실(10-06)** — 측정기가 `OnApplicationQuit` 에서만 저장 → 맵→결과 화면 전환으로 MapScene 이 내려가며 데이터 소실. `OnDisable` 저장으로 수정(씬 언로드·게임 종료 공통), 에디터에서 자동 저장 확인 후 재빌드(증분 18초, DLL 반영 확인).

**에디터 2차**(시드 30349763, 게임 뷰 최대화, 61초): ZoneL_typeB Frame 3.50/4.50 · Main 2.46 · Render 2.83 · **GPU 3.11** · ZoneM 3.83 · ZoneS_typeA 3.65 · 복도 3.81 · 시작 존 5.25(시작 직후 로딩 끊김 6회 섞임).
→ 실제 화면 크기에선 **GPU 가 1순위, 렌더 스레드 2순위.** 1차의 GPU ≈1ms 는 게임 뷰가 작아서였다(같은 1920×1080 표기라도 뷰 크기에 따라 다름 — 에디터 GPU 숫자 신뢰 낮음). 삼각형 최대 ≈1천만(평균 300~500만) 구간 존재 — 원인 미확인.
팀장 체감: 솔로 빌드 160~200fps → 멀티 호스트는 더 낮을 것 → 현 상태는 문제로 판단(10-06).

**🔴 개발 빌드 측정(10-06 16:03, 시드 966343229, 100초, 상한 해제, 해상도 3840×2160)** — 판단 기준 숫자
| 존 | 초 | Frame avg/p95 | Main | Render | GPU | 5ms 초과 |
|---|---:|---|---:|---:|---:|---:|
| ZoneS_typeStart | 36.6 | **13.50** / 15.00 | 10.60 | 10.93 | **13.36** | 99.9% |
| ZoneM_typeA | 10.4 | 5.33 / 6.25 | 0.42 | 4.42 | 5.12 | 71.8% |
| 복도/기타 | 23.4 | 5.13 / 6.00 | 1.15 | 4.19 | 4.92 | 46.8% |
| ZoneS_typeA | 5.1 | 5.07 / 6.00 | 0.66 | 4.21 | 4.88 | 52.3% |
| ZoneL_typeB | 19.8 | 5.01 / 6.00 | 0.44 | 4.22 | 4.81 | 50.5% |
| BossEnter | 5.0 | 4.92 / 5.75 | 0.53 | 4.25 | 4.80 | 35.2% |
결론: **GPU 병목(4K).** 빌드 메인 스레드 0.4~1.2ms → CPU 여유 큼. 렌더 스레드 ≈ GPU 는 GPU 대기 포함으로 추정(미확정).
→ 메시 병합·GRD(CPU 제출 비용 절감)는 **우선순위 뒤로**. 멀티 부담도 CPU 보다 GPU(플레이어·이펙트 추가 렌더)가 관건.
→ 다음 후보(팀장 선택 대기): ① 4K GPU 패스 분해(풀스크린 패스: 안개·블러·CRT·실루엣) ② 렌더 스케일/업스케일 민감도 ③ 시작 존 GPU 13ms 원인.
⚠️ 에디터(1080p) 숫자와 비교하지 말 것 — 해상도가 4배 다르다.

**해상도 비교(10-06 개발 빌드, 상한 해제, 거너·붕괴 사망 포함 빌드)** — GPU 평균 ms
| 구간 | 1080p | 1440p | 4K |
|---|---:|---:|---:|
| 일반 존(L·M·S·복도·보스룸) | 2.2~2.5 | 2.7~3.3 | 4.7~5.0 |
| 시작 존 | 4.2 | 6.5 | 13.1 |
| 로딩 화면 중 | 5.1 | 7.8 | 14.9 |
| 일반 존 5ms 초과 | 0% | 0~1% | 31~62% |
결정(팀장 10-06): **기본 해상도 1920×1080**(defaultIsNativeResolution 0) — `6df20e8c`. Codex 교차검증도 "해상도가 텍스처 품질 옵션보다 우선" 동의.
텍스처 품질(globalTextureMipmapLimit·Mipmap Limit Group·streaming)은 VRAM·대역폭 수단이라 이 병목엔 효과 작음 → 옵션 메뉴 만들 때 항목으로.
다음: 시작 존·로딩 화면 GPU 2~3배 원인 · 패스별 GPU 기록 0 문제 · 붕괴 사망 순간 부하.
