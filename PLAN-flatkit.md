# PLAN — Flat Kit(URP) 전환: 몬스터 · 플레이어 · 23호 · Wells · 물

> 상태: **진행 중 — 1~7단계 완료, 8 아트 컨펌(인게임) 대기 → 9 물** (승인 09-30) · 작업자: 경석(Claude) · 브랜치 `feature/Boss23` · 작성 2026-09-30
> Codex 교차검증 1회(읽기 전용, 09-30) 반영.

## 목표
캐릭터 룩과 물을 에셋 스토어 **Flat Kit(URP)**(`Assets/FlatKit/`, 팀장 09-30 임포트)로 통일한다.
대상 = 몬스터 8종(Chomp·Gauntlet·Humanoid·Mortar·PeekABot·Spinner·Tesla·Wall) · 플레이어(`Paladin.prefab` — 실제 스폰) · 23호 · Wells · 맵 물.

## 확정 (팀장 09-30)
| # | 결정 |
|---|---|
| F1 | 자체 툰 `Project/ToonLit` 의 "맵이 어두워도 캐릭터 밝기 유지(_CharKey*)" 는 **잃어도 된다** |
| F2 | "카메라 거리 무관 외곽선 두께" 도 필요 없다 — 카메라 줌 없음 |
| F3 | 물: `WaterDark.shader`·물가 마스크·바닥 경사는 전부 Flat Kit 물을 흉내 내려던 것 → **Flat Kit 물로 교체**. 프리셋 = **`PoolScene_Water_09`** (`Demos/[Water] Pool`) |
| F4 | Mobile 품질에서 물이 안 나와도 된다(Mobile_RPAsset 은 Depth·Opaque 꺼짐) — PC 빌드만 |
| F5 | 순서: 몬스터 1종 시범 → 룩 확인·조정 → 몬스터 전체 → PC_Renderer 정리(최적화 포함) → 플레이어 → 23호·Wells → 아트 컨펌 → 물 → Play |

## 접근
- **머티리얼은 git `Assets/3.Materials/FlatKit/` 에 새로 만든다.** SVN 원본(`50.Art/Char/Monster/**/Materials`)은 건드리지 않고
  프리팹(git)에서 렌더러 머티리얼을 오버라이드한다 — 기존 `3.Materials/Toon/` 과 같은 방식. 이름이 같은 원본 두 장
  (`M_PeekABot_01` Pack01·Pack02, GUID 다름 — Codex 정정)을 고쳐 다른 몹이 딸려 바뀌는 일도 없다.
- 셰이더 = `FlatKit/Stylized Surface` 하나. `_BaseMap`·`_BaseColor` 를 원본에서 옮긴다
  → `DissolveDeath`·`HitFlash`·사망 틴트가 그대로 동작(프로퍼티 이름 동일, Codex 확인).
  `With Outline`·`Terrain` 변형은 안 쓴다.
- 외곽선 = **`ObjectOutlineRendererFeature`**(머티리얼의 `Outline` 패스를 그린다). 화면 전체용 `FlatKitOutline` 이 아님(Codex 정정).
- **git 에 넣을 Flat Kit 범위**: `Shaders/` · `[Render Pipeline] URP/` · `Utils/` · `PresetsShared/` + 물 프리셋 3개 파일
  (`PoolScene_Water_09.mat` · `PoolScene_Water_09_ColorGradient.png` · `PoolScene_Water_08_ColorGradient.png` — 08 은 `_NoiseMap` 참조).
  프리셋은 `3.Materials/FlatKit/Water/` 로 **복사**(guid 새로)해서 쓴다. `Demos/`(137MB)·`*.unitypackage`(150MB)·PDF 는 `.gitignore`.
- 물: 새 머티리얼을 `4.MapScene` 의 물 슬롯 2곳에 교체. `WaterDark.mat`·셰이더·베이커는 **지우지 않는다**(되돌리기용).
- PC_Renderer 정리: 현재 렌더러 피처를 전수 조사해 **안 쓰는 것(구 잔재)은 끄거나 뺀다** — 무엇을 뺄지는 조사 후 목록으로 확인받는다.

## 단계 · 검증
| # | 단계 | 검증 |
|---|---|---|
| 1 | git 범위 확정(.gitignore) · 물 프리셋 복사 | 복사본 guid 의존성 전부 해석 · `git status` 에 Demos/unitypackage 없음 |
| 2 | `Stylized Surface`·`Water` 재임포트 | 콘솔·Shader 인스펙터 오류 0 |
| 3 | **ChompBot 시범** Flat Kit 머티리얼 + 외곽선 피처 임시 적용 | 스샷 → **팀장 룩 확인·값 조정** |
| 4 | 몬스터 나머지 7종 | 종마다 HitFlash · 사망 틴트 · 디졸브 · 인터럽트 표시(`DissolveOverlay` 슬롯 번호 고정 — 슬롯 순서 유지) |
| 5 | PC_Renderer: 외곽선 피처 확정 + 안 쓰는 피처 정리 | 뺄 목록 확인받기 · Render Graph 경고 없음 · 프레임 비교 |
| 6 | 플레이어(Paladin — 본체·검·방패) | 벽 뒤 실루엣(`UniversalForwardOnly` 태그) · 로컬/원격 |
| 7 | 23호 · Wells(단독 `Wells.prefab` + 23호 탑승분 둘 다, `SK_welz.fbx` 내장 머티리얼 → 오버라이드) | 사망 연출 · 간파 표시 |
| 8 | **아트 컨펌** | 팀장/아트 스샷 확인 |
| 9 | 물 → `PoolScene_Water_09` 복사본 | 물가·경사·수면 색 PC 화면 |
| 10 | 통합 Play + MPPM 호스트/클라 | 누락 머티리얼·셰이더 오류 0 |

## 리스크
- `Stylized Surface With Outline`·`Terrain` 임포트 경고 원인 미확정(순서 문제로 단정 불가 — Codex). 쓰지 않는 변형이지만 2단계에서 재확인.
- 데모 물 룩은 풀 씬의 조명·후처리·지형 경사까지 합친 결과다 — 머티리얼만 옮기면 다르게 보일 수 있다(9단계 조정).
- 몬스터 FBX 가 중첩 프리팹 안에 있어 렌더러 슬롯마다 오버라이드가 필요하다(Codex).
- 기존 `ToonLit` 머티리얼(`3.Materials/Toon/`)은 전환 후 참조가 0 이 되면 정리 후보 — 이번엔 지우지 않는다.

## 진행 기록 (09-30)
- 1·2단계 ✅ `.gitignore`(Demos·PDF) · 물 프리셋 복사 `3.Materials/FlatKit/Water/FK_Water_Pool09.mat`(+그라데이션 2장, guid 새로) · 셰이더 컴파일 ok.
- 🔴 **Flat Kit 데모 씬을 열면 `AutoLoadPipelineAsset` 이 Graphics·Quality 의 URP 에셋을 데모용으로 바꾼다.** 되돌릴 값을 직렬화하지 않아 도메인 리로드 한 번이면 복구 못 하고 묶인다(실측). → `Tools/Rendering/Flat Kit/Restore Project Pipeline`.
- 🔴 받은 Flat Kit 이 **6000.4 빌드**(에디터 스크립트 버전 상수만 다름 — 셰이더는 6000.3 과 동일, 1019개 대조). 폴더 안 `[Render Pipeline] Universal (URP).unitypackage`(6000.3) 재임포트로 해결.
- 3단계 ✅ ChompBot 시범 → 팀장 확정값: Color Shaded 0.6 · Light Contribution 1 · Light Falloff 1 · Rim a 0.23/size 0.12 · Outline 4(Screen). 값은 `Assets/1.Scripts/Rendering/Editor/FlatKitCharacterLook.cs` 한 곳.
  - 하얗게 뜸 원인 = `_LightFalloffSize` 기본 0(포인트 라이트 범위 안 = 거리 무관 최대 세기). MCP `create_material` 은 `_BaseColor` 를 (0,0,0,0) 으로 만든다(검정).
  - 에디터가 어두운 건 `FogManager`([ExecuteAlways]) 디밍 — 룩 문제 아님.
- 4·7단계 ✅ `FlatKitCharacterConvert.cs` — 몬스터 8종 · Wells · 23호 · 23호 Solo, 슬롯 23 · FK 머티리얼 17(`3.Materials/FlatKit/Monster·Boss`). 투명 원본(PeekABot 안테나 · SpinnerBot 날개 · Wells 보호유리)은 **투명 Flat Kit**, 외곽선 끔. 남은 비-FK = `Interrupt` 겹(의도). `Verify Characters` 메뉴로 실제 슬롯 확인.
  - 23호가 보스방에서 보라색 = 조명 색을 받는 결과(구 ToonLit 은 맵 조명 무시).
- 5단계(렌더러) ✅ 적용: MaskBlurFeature 끔(컨트롤러 오브젝트 비활성 — 게임에서 안 돎) · LOD Cross Fade 끔 · Terrain Holes 끔 · 그림자 캐스케이드 4→**2** / 거리 50→**35** / split **0.6** · SSAO 유지 + Radius 0.15 · Intensity 0.3 · Direct 0.1(Codex 교차검증).
  - ⚠️ 정정: SSAO `Samples: 1`=Medium(8), `BlurQuality: 0`=**High** — ToonLit 주석의 '저품질' 기록은 enum 오독(URP `ScreenSpaceAmbientOcclusion.cs:41-59`).
  - 유지: 보조 조명 그림자(팀장 — 맵 조명 재작업으로 조명 많아짐) · Adaptive Performance(보류) · 불투명 텍스처 다운샘플은 **9단계 물 전환 때 비교**.
  - ✅ 데이터 기반 렌즈 플레어 끔(전 브랜치·아트 씬·SVN 사용 0) · **화면공간 렌즈 플레어는 유지** — 아트 `title` 볼륨(`Assets/0.Scenes/Art/title/GlobalVolumeProfile.asset:136` intensity 1)이 쓴다. 메인 `1.TitleScene` 카메라 3개가 후처리 꺼짐(:3483·4251·7186)이라 게임엔 안 나오지만 아트 씬(`title.unity:1184` PP 켜짐)에서 쓰고 브랜치 전반에 있음 → 팀장 기준(아트 씬·다른 브랜치 미사용일 때만 정리)에 걸림.
  - ✅ 데칼 최대 거리 1000 → **50**. URP17: 컷 거리 = min(프로젝터 1000, 전역) + 바운딩 반경, 카메라 위치 기준, fadeScale 0.9 → 약 45m 부터 페이드. 게임 카메라 화면 끝 바닥 ≈ 28~30m, 시네마틱 카메라 없음(서브에이전트·Codex 일치). 줌아웃/원거리 연출이 생기면 재검토.
- 6단계 ✅ 플레이어 — `Paladin`(실제 스폰, NetworkManager defaultPlayerPrefab) · `Paladin_VFX`(이펙트 작업용 복제본) · `Player`(역할 프리팹). 슬롯 11 · FK 8장 추가(`3.Materials/FlatKit/Player`). Paladin 검·방패는 원본부터 색 전용(텍스처 없음, 색 복사 확인). `Paladin_VFX` 의 HexOverlay·검/방패 글로우 겹은 그대로.
  - 🔴 도중에 팀장이 임시로 넣은 `Assets/Magic Pig Games (Infinity PBR)`(Projectile Factory ↔ Realistic Effects Pack 4 연동 애드온)이 **컴파일 에러 6개로 프로젝트 전체 컴파일을 막았다** — 본체 팩·Projectile Factory 둘 다 없어 쓸 수 있는 이펙트 0. 팀장 지시로 폴더 삭제(git 미추적).
- 외곽선 두께(인게임 조정): 4 → 1.6 → 0.5 → 0.25 → **0.4**. 1920×1080 기준 폭 = Width × 2.7px(`StylizedSurface.shader:325-329`, CameraDistanceImpact 0 → 화면 비율 고정). 0.4 = 1.08px = 끊기지 않는 최소 1px.
  - ✅ **플레이어만 Smooth Normals 적용**(팀장 09-30 — 몬스터는 0.4 에서 가시 안 보임, Paladin 은 AI 생성 tripo 메시라 가시가 보임. 거너·어쌔신도 같은 경로):
    - 복제 셰이더 `Assets/1.Scripts/Rendering/Toon/Shaders/FlatKitStylizedSurfaceSkinnedOutline.shader`(`Project/FlatKit Stylized Surface (Skinned Outline)`) — Outline 패스만 변경: TEXCOORD3 의 **탄젠트 공간** 법선을 스키닝된 N·T 로 복원(뼈와 같이 돈다). TEXCOORD3 가 비면 원래 법선. Flat Kit 원본 무수정.
    - 굽기 `Assets/1.Scripts/Rendering/Editor/FlatKitSmoothNormalBaker.cs`(AssetPostprocessor) — 허용 목록 FBX 6개(Paladin 몸 · PlayerBaseModel · 검/방패 + _MaskUV). UV3 사용(UV2 는 _MaskUV 이펙트와 겹칠 수 있어). SVN FBX·.meta 무수정 — 임포트 산출물에만. 런타임 계산 0, 정점당 12B. 새 캐릭터 = 목록에 경로 추가 + `Tools/Rendering/Flat Kit/Reimport Smooth Normal Targets`.
    - 룩 도구가 `3.Materials/FlatKit/Player/` 를 복제 셰이더 + `DR_OUTLINE_SMOOTH_NORMALS` 로 바꾼다. 플레이어 외곽선 두께는 별도 `PlayerOutlineWidth`(0.25).
  - (기록) 몬스터용 설계 메모 — Smooth Normals 보류 — 이 두께에서 가시가 안 보임. 다시 굵게 가면: Flat Kit 기본(uv2 에 오브젝트 공간 법선)은 **스킨드 메시에서 뼈와 같이 안 돈다** → Codex 권장 = 탄젠트 공간 인코딩 + 외곽선 패스에서 스키닝된 normal/tangent 로 복원(프로젝트 소유 복제 셰이더) + git `AssetPostprocessor.OnPostprocessModel` 로 허용 목록 FBX 만 임포트 때 굽기(SVN FBX·.meta 무수정). 가시의 다른 원인 후보: `normalize(clipNormal.xy)` 가 카메라를 향한 윗면 법선에서 불안정. 시범은 ChompBot 1종.
- 외곽선 최종: 플레이어·몬스터·보스 모두 **0.4**(플레이어는 Smooth Normals 로 가시 제거 후 맞춤, 팀장 09-30 인게임 이상 없음).
- 9단계 물 🔄 `4.MapScene` 의 `AbyssWater` 2곳(쿼드 평면, y −19 · 200×200 보스방 쪽 / 330×330 본맵)을 `WaterDark.mat` → `FK_Water_Pool09.mat` 교체. 코드 의존 0. `WaterDark` 머티리얼·셰이더·베이커는 되돌리기용으로 유지.
  - ⚠️ 쿼드(정점 4개)라 Flat Kit 파도(`_WaveMode 2`) 정점 변위는 안 보인다 — 색·거품·굴절만. 구 WaterDark 의 안개(어비스) 노이즈 연동은 없다.
  - 보스방 파도 조각 `WaterWaves_BossRoom`(45m = 바닥 30×30 ×1.5, 0.4m 격자 12,996 정점) + 파도 f 25.59→2.5(파장 2.5m)·a 0.25. 보스방 쪽 큰 Quad 끔. 도구 `Assets/1.Scripts/Rendering/Editor/FlatKitWaterPatchAuthoring.cs`.
  - 🔴 첫 시도가 **전부 검정** — Flat Kit 물 색은 (불투명 씬 깊이 − 수면 깊이)로 그라데이션을 읽는데 물 밑에 아무것도 없어 깊이 무한 → Pool09 그라데이션 끝(21,21,22). 데모는 1~3m 밑 바닥 + 벽 관통(깊이≈0 → 밝은 띠·거품). → 경사 바닥 `WaterBed_BossRoom`(방 벽 아래 0.35m → 조각 가장자리 10m, 체비셰프 거리, `MA_WaterBed` 불투명 Unlit). 예전 `WaterBedAuthoring`(WaterDark 용)은 경로가 낡아 현재 씬에 바닥 0개였다.
  - 🔄 09-30 재설계(팀장: 흐르는 느낌 없음·안→밖으로 흐르는 듯·깊이 부자연스러움): ① 물을 **벤트 창살 바로 밑 y 0.2**로(바닥 판 y 0~0.5 두께 안 → 판이 가리고 벤트 틈으로만 보임, 판 옆면이 수면 관통 → 가장자리 거품. y −19 는 19m 시차로 벤트 옆으로 밀려 보였다). ② **데모 조건 복제** — 데모 `Pool_Water.fbx` 실측 27.57m·UV 0~1·정점 2809(간격 0.53m). 데모도 파장 0.25m 파도를 0.53m 격자로 그려 **불규칙 일렁임**이 된다 → 파도 값 원복(f 25.59·a 0.2)·간격 0.53·UV 27.57m/1. ③ 바닥 = 펄린 노이즈 0.8~4.5m(사각 링 폐기).
  - ⚠️ 뒤집음(10-01): 데모 파도 복제는 벤트 틈(수 cm)으로 보면 단색 → 흐름이 읽히게 재조정: 격자 0.25m · f 7(파장 0.9m) · a 0.05 · v 1.2 · 마루 0.3/0.7 · 거품 양 0.25·선명 0.9·Y 3배·**색 흰 청록 a 0.6**(Pool09 거품은 짙은 청록 a 0.25 음영이었다) · 바닥 1.5~7m.
    - 🔴 이 Flat Kit 버전은 파도 계산 전체가 `_WAVEMODE_GRID` 안(Water.shader 254~271) — Round/Pointy 는 파도 0. 한 방향 물결이 필요하면 복제 셰이더에서 고쳐야 함.
    - 벽 투명화(디더) 때 바닥 판(옆면 없음) 밑의 물이 옆으로 비쳐 '벽에 물결'처럼 보였다 → 바닥 메시 가장자리를 바닥 윗면까지 세운 불투명 테두리.
  - 10-01 팀장 확정 방향: 수면 **y −0.12**(0.2 는 물이 벽 위로 비침 — 팀장 Play 확인) · 짙은 청록 어둡게(바닥 5~9m) · 파장 0.63m(f 10, 격자 0.16m, 35,721 정점) · 마루 0.45 · 거품 0.4. 도구가 이미 열린 `4.MapScene` 은 닫지 않게 수정.
  - 10-01 존 11종 물(`2. Zone Patches`) — 존 프리팹 안 `Water`(**`ZoneWater` 컴포넌트 = 수면 높이 한 칸**) └ `WaterWaves`·`WaterBed`. 크기 = 존 바닥 실측, 높이 = 창살 아랫면 −0.56(없으면 주 바닥 −0.62; 최댓값 기준은 3m 단 때문에 폐기). **재실행해도 존별 높이 유지**, 머티리얼 값은 덮어쓰지 않음(초기값은 `3. Reset Water Look`). 보스방도 `Water_BossRoom` 같은 구조. `ZoneS_typeA` 수면 −4.43(팀장 Play 조절값, 하이어라키 순서로 추정).
    - `Assets/1.Scripts/Rendering/Water/ZoneWater.cs` — 에디터에선 이 값이 기준(Transform Y 를 끌어도 되돌림). Play 중 (Clone) 조절은 종료 때 사라짐 → Copy/Paste Component 로 프리팹에.
  - ⏳ 인게임 확인 + 불투명 텍스처 다운샘플(`PC_RPAsset m_OpaqueDownsampling: 1` = 2배 축소) 끄기 비교.
- ⏳ 인게임 확인은 팀장이 직접(09-30).
