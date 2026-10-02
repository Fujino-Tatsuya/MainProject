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
  - 10-01 2차(팀장 지적 3건): ① 크기 2m 축소는 ⚠️ 뒤집음 — 구덩이 안에 물 가장자리가 드러나 아래 쿼드와 겹쳐 보였다. 벽 끝 비침의 원인은 수면이 높았던 것(−0.62)이라 **모든 존 −4.43**(팀장 Play 값, 존 구덩이 벽은 −10.5~−11 까지)으로 내리고 크기는 원복. ② 거품·굴절이 조각마다 UV 0 부터라 이웃 물이 안 이어짐 → 복제 셰이더 `Assets/1.Scripts/Rendering/Water/FlatKitWaterWorldUV.shader`(`o.uv` 한 줄 = 월드 XZ/27.57). ③ 메뉴 `4. Set All Zones to Default Height (overwrites)`.
    - ⚠️ 맵 330m 큰 쿼드(`Abyss/AbyssWater`)가 씬에서 삭제된 채 저장됨(10-01 09:55, 도구의 SaveScene 이 열린 씬의 미저장 삭제를 함께 저장한 것으로 추정) — 의도 확인 필요.
  - 10-01 3차: 물을 **뚫린 곳(구덩이·벤트)에만** — 칸마다 위에 주 바닥 판(창살 제외)이나 구덩이 벽(wall_*, 바닥 아래로 내려감)이 있으면 막힘, 없으면 물(`CreateMaskedGridMesh`, 존별 `WaterHoles_<존>.asset`). 존 전체 사각형은 구덩이 벽 뒤에도 물이 있어 벽 투명화 때 '벽에 물이 흐르는' 것처럼 보였다(팀장 '벽 안쪽 기준'). S_A 는 존 가장자리에 벽이 없고 구덩이 벽만 있어(±6.8) '가장자리 벽' 기준은 실패. 보스입구 존은 뚫린 곳 0 → 물 없음. 맵 큰 쿼드는 팀장이 삭제(의도).
  - ⏳ 인게임 확인 + 불투명 텍스처 다운샘플(`PC_RPAsset m_OpaqueDownsampling: 1` = 2배 축소) 끄기 비교.
- ⏳ 인게임 확인은 팀장이 직접(09-30).

## 9-b 물 데모 룩 맞추기 (10-01 조사 · Codex 교차검증 1회 · **승인 대기**)
팀장 지적: 데모와 비주얼 차이가 크다 · 거품이 압도적으로 많다 · 물결·깊이가 안 보인다 · 탑다운에서 어색한 장면.

**조사 결론**
- 흰색 = **거품**(양 0.4 / 흰 a0.6, 데모 0.08 / 짙은 청록 a0.25) + **물결 마루**(크기 0.45·경계 0.7 / 흰 a0.9, 데모 0.102·0). 그라데이션 PNG 는 데모와 바이트 동일(Codex SHA-256).
- 깊이 색 = 수면→물 밑 불투명 바닥까지의 **시선 방향 거리**. 물 메시는 평평해도 되고 **바닥이 벽 쪽 얕음 → 가운데 깊음으로 경사져야** 한다. 지금 바닥은 테두리 한 줄만 수면 위, 바로 안쪽이 5~9m 절벽이라 중간 색이 없다. 물결(굴절)도 이 깊이 경계를 흔드는 것이라 경사가 있어야 보인다. 셰이더 수정은 필수 아님.
- 탑다운 어색함 = **맵 바깥 허공에도 물**. 마스크가 "바닥 판 없음 = 물"이라 존 경계까지 깔리고 4m 타일 계단 경계가 그대로 드러난다(마스크 PNG 확인). 회전 조각은 AABB 크기를 그대로 써 부정확(Codex).
- ⚠️ 정정: 포인트 라이트가 물을 하얗게 만든다는 가설은 틀림 — Forward+ 에선 `GetAdditionalLightsCount()` 0.

**확정(팀장 10-01)**: 맵 바깥 물은 없앤다 — **둘러싸인 구덩이·벤트만**. (버림: 바깥 전체를 바다로 복원 — 바깥 지형 높이 맞춤 작업 추가)

**작업**
1. 머티리얼(도구 상수 포함): 거품 0.08 · 짙은 청록 a0.25 · Sharpness 0.737 · Speed 0.5 · StretchY 1 · 마루 0.102/0. 물결 값은 흰색 제거 후 따로.
2. 마스크: 존 경계에서 뚫린 칸을 따라 채우기(flood fill) → **경계와 이어진 칸 = 바깥 → 제외**. 막힌 칸 판정은 8모서리 변환으로 정확히. 그 뒤 1~2칸(≈0.3m) 바깥으로 넓혀 경계를 벽·판 속에 묻는다.
3. 바닥: **막힌 칸까지의 거리**(격자 거리 변환)로 수심 — 벽 쪽 얕음(≈0.5m) → 약 3m 안쪽부터 깊음(≈7m). 실제 게임 카메라에서 그라데이션 전 구간이 보이게 조정. 바닥은 물 칸 밑에만.
4. 보스방 물도 같은 규칙인지 확인.

**검증**: 마스크 PNG 재출력(바깥 물 0 · 존별 남은 칸 수 로그) · 구덩이가 존 경계에 닿아 사라진 곳이 없는지 존별 확인 · Play 스샷(데모 대비 색 단계·거품량) · 벽 투명화 때 벽 뒤 물 비침 없음.
**리스크**: 존 경계에 걸친 구덩이(다음 존으로 이어지는 도랑)는 바깥으로 판정돼 사라진다 → 존별 로그로 확인 후 예외 처리. 좁은 도랑은 바닥보다 맞은편 벽이 먼저 시선에 걸려 밝게만 나올 수 있다.
- 9-b 진행(10-01): 1~3 구현 · 머티리얼 데모 값 적용 · 존 물 재생성. 결과 — 물 있음: L_B(가운데 십자 해자 전체) · M_A · M_B · M_C · S_A(4m 구덩이 4개) · Start / **물 없음**: L_A · L_C · Quest01 · Quest02(빈 곳이 전부 존 가장자리와 이어짐) · BossEnter. 진단 메뉴 `0d. Snapshot Zones` → `Temp/ZoneShots/`.
  - ⚠️ L_C 는 가운데 빈 곳으로 **배수 파이프가 내려가는** 구조(L_B 해자와 같은 모티프)인데 바깥 링에 4m 틈이 있어 물이 0 이 됐다 → 틈 닫기 폭을 넓힐지 팀장 판단 대기.
  - 보스방은 이번에 안 바꿈(머티리얼만 공유).
- 10-01 팀장 Play 지적 2건 → **같은 원인**: 도구의 `SaveMesh` 가 기존 메시 에셋에 `EditorUtility.CopySerialized` 로 덮어써 **GPU 정점 버퍼가 이전 메시로 남았다** — 새 인덱스 + 낡은 정점이 섞여 물이 없어야 할 곳(M_A·M_B 구석 구덩이 = 이전 마스크 블록 자리)에 바닥 없는 물이 그라데이션 끝(거의 검정)으로 그려지고(사진 1), 엉뚱한 작은 조각이 보였다(사진 2). → 메시 API(Clear·SetVertices…)로 채우게 수정, 재생성 후 스냅샷에서 사라짐 확인.
  - 팀장 의도: 모든 빈 곳이 물이 아니라 **어떤 곳은 안개, 어떤 곳은 물** — 지금 규칙(둘러싸인 구덩이만 물)과 맞음. 추가 요청 없음 · 보스방 유지.
  - 존별 조절 범위: **수면 높이만 존별**(`ZoneWater`). 색·거품·파도는 머티리얼 1장(`FK_Water_Pool09`) 공유 = 전 존 공통, 바닥 수심 값은 도구 상수(전 존 공통).
- 10-01 팀장 Play: 물 가장자리가 점무늬로 지직거림 → 구덩이 벽(`Generic_01_A`, SVN)의 바닥 아래 디더 투명화(`_WallOccZoneBaseY −13 · FadeHeight 13`, 수면 −4.43 에서 불투명도 0.66) 구멍으로 **벽 속으로 밀어 넣은 물(EdgeTuck 0.32m)**이 비친 것. ⚠️ 뒤집음 — 물은 벽 안쪽 면에서 멈추고(막힌 칸 제외), 바닥 테두리는 수면 +0.62 → **수면 −0.05**(테두리 경사가 벽 앞 수면 위로 튀어나오지 않게). 게임 카메라 스냅샷(`0e`)에서 벽 구멍 뒤 물 비침 사라짐 확인. 벽 자체 디더(벽 경계 1~2px)는 아트 연출이라 유지.
- 10-01 팀장 Play: 파이프·벽 아래 물 위 점무늬 → ① **깊이 텍스처의 디더 구멍** — 수면 밑 벽·파이프가 깊이에 구멍 난 채로 기록돼 물 색이 픽셀마다 얕음/아주 깊음으로 갈림. 복제 셰이더 `FlatKitWaterWorldUV.shader` 의 DepthFade 깊이 = **3×3 중 가장 가까운 값**(Flat Kit 원본 무수정 · 보스방도 같은 머티리얼이라 함께 적용). ② 그 뒤 드러난 벽 면-물 사이 검은 띠 = 벽 AABB 가 기둥·돌출부 때문에 벽돌 면보다 두꺼워 물이 상자 끝에서 멈춤 → 물 범위를 **수면 높이에서 자른 벽 메시 단면**까지 넓힘(벽 상자 안에서만, 최대 1m). 4m 격자 벽이 칸 경계 위에 놓여 단면이 한쪽 칸에만 찍히던 누수는 경계 양쪽 표시로 해결.
  - ⚠️ 뒤집음: 바닥 판 밑 3m 넓히기는 디더 벽 구멍으로 물이 청록 점으로 비쳐 폐기(벽 구멍 = 검은 심연 연출).
  - 남음: 벽-물 맞닿는 선 1~2px 점무늬(벽 자체 디더) · 파이프 하단 디더(아트 연출).
- 10-01 팀장: 물 앞 벽·파이프 디더 '무조건 잡아야 함' → Codex 교차검증(읽기 전용) 일치안 채택: **머티리얼 변형** `Assets/3.Materials/FlatKit/Water/Generic_01_A_Wet.mat`(부모 = SVN `Generic_01_A`, `_WallOccZoneFadeHeight` 만 0 = 디더 끔)을 만들고, 물 칸(±0.5m)에 걸치며 바닥 아래로 내려가는 렌더러 슬롯만 **존 프리팹에서** 교체(도구 `2. Zone Patches` 가 매번 원복 후 재적용). L_B 328 · S_A 96 · Start 32 슬롯, M_A·B·C 는 물이 창살 밑뿐이라 0. 원본 무수정 확인. 물 없는 구덩이(안개)는 심연 디더 유지.
  - 버림: MPB(SRP Batcher 이탈 + WallTransparencyGroup 이 같은 속성을 인스턴스에 써 경쟁 — Codex) · 원본 값 변경(맵 전체·SVN) · 물도 디더(수면 전체가 구멍).
  - 검증 대기: 팀장 Play · Frame Debugger 배칭 · MPPM 호스트/클라 동일 머티리얼.
- 10-01 팀장: **벤트 밑은 '구멍으로만 흐르게 보이는' 처리를 따로** 함 → 벤트 밑 물 제거 · 시작 존 물 없음 · 두 번째(S_A) 유지 · 세 번째는 구덩이에만.
  - 도구: 창살(`floor_metal_trenchcover`)도 막힌 칸 · 더 높은 단(3m 블록) 바닥도 막힌 칸(다리·이동발판·크레인·조명 제외 — 막으면 L_B 다리 밑이 계단 모양으로 검게 뚫렸다) · `NoWaterZones` = Start · L_C(안개 구역) · **위에서 안 보이는 물 덩어리 제거**(연결 칸이 전부 수면 위 MeshRenderer AABB 에 덮이면 삭제 — M_A·M_B 솟은 단 안의 숨은 물).
  - 결과: 물 = **L_B(해자) · S_A(구덩이 4개)** 뿐. 나머지 존 물 없음.
- 10-01 팀장: **L_B 물 없앰** · **중간 크기 존 두 개(M_A·M_B)의 구석 큰 구덩이에 물** · 안 쓰는 메시 삭제.
  - 구석 구덩이는 존 가장자리로 뚫려 '바깥' 판정이라 빠져 있었다 → `EdgePitZones`(M_A·M_B)는 바깥 판정 무시. 그 대신 존 가장자리 판 사이 틈이 물 띠로 남아 **폭 ≈1m 미만 띠 제거(열기 3칸)** 추가.
  - 결과: 물 = **M_A 구석 2곳 · M_B 구석 2곳 · S_A 구덩이 4곳**. 디더 끈 슬롯 M_A 32 · M_B 46 · S_A 96.
  - 메시 16개 삭제(참조 0 확인 — git 12 `git rm` · 미추적 생성물 4). 남은 것 = 위 3존 + 보스방(`WaterGrid_30x30m_188x188`·`WaterBed_30x30m`).
  - ⚠️ 구석 구덩이의 존 경계 쪽 물 가장자리는 이웃 존/허공과 맞닿는 직선 — 인게임에서 떠 보이면 다음 조정 대상.
- 10-01 팀장 Play: M_A·M_B '구석 구덩이' = 실은 **ㄱ자 존의 빈 모서리**(벽은 안쪽 두 면뿐) → 열린 두 면의 물 가장자리가 카메라 쪽이라 비스듬히 보면 벽 바깥으로 떠 보임(직교에선 바닥 모서리와 겹쳐 안 보임). 팀장: 문제는 **큰 존 입구 직전 오른쪽 모서리 한 곳** — 크기·위치를 직접 조절하겠다.
  - → **물 잘라내기 상자** `Assets/1.Scripts/Rendering/Water/WaterTrimBox.cs`(XZ 상자 안 물 칸 삭제, 기즈모만·런타임 동작 없음) + 메뉴 `GameObject/Flat Kit Water/Trim Box` + 도구가 존 프리팹의 상자를 읽어 반영(로그에 제거 칸 수). 상자는 `Water` **밖**에 둘 것(Water 는 재생성 때 지워짐).
  - 원인 실측: 물 범위 기준(바닥 판 경계)이 **바깥 벽보다 ≈0.45m 밖** → M_A 모서리 물이 벽 선 밖으로 띠처럼 나옴. 카메라 회전 고정이라 기하로 해결 가능 → **수면 높이 벽 단면의 최소·최대 XZ(바깥 벽 바깥 면)에서 물을 자른다**(M_A 300칸) + 물 밑 바닥의 여유 블록도 그 선에서 빼고 정점을 선 안으로 당김(물 없는 여유 블록이 허공에 어두운 띠로 매달렸다).
  - 남는 한계: 벽이 아예 없는 열린 면은 물 가장자리를 가릴 것이 없다(고정 카메라에서도 카메라를 향한 면은 못 가린다) → 필요하면 잘라내기 상자로 직접 다듬기.
  - ⚠️ 뒤집음(팀장 10-01 '벽 안쪽으로 계산해야 벽이 투명해져도 이질감 없다'): 기준선을 바깥 벽 **안쪽 면**으로 — 바깥 벽이 수면까지 안 내려오는 존(M_A)이 있어 **바닥 바로 아래(top−0.3) 단면**도 읽고, 변마다 가장 바깥 선 = 바깥 면 / 1.5m 안의 다음 선 = 안쪽 면. 거기에 **시차 보정**: 카메라 회전 고정(오프셋 7,17.5,−7)이라 수면이 바닥보다 4.43m 아래면 화면에서 카메라 쪽(+x·−z)으로 1.77m 밀려 보임 → 카메라를 향한 변만 1.77m 더 당김. M_A·M_B 물 한계 x −9.60~7.83 · z −17.63~19.80.
  - 열린 면 있는 존(`EdgePitZones`)에만 적용 — S_A 에 적용했더니 맨 바깥 벽 = 구덩이 벽이라 구덩이 물이 973칸 깎였다(되돌림).
  - ⚠️ 뒤집음(팀장 '너무 줄였다'): M_A 오른쪽 변은 **존 프리팹 안에 바깥 벽이 없다**(바닥 판 x 6~10·펜스뿐 — 사진의 벽돌 벽은 프리팹 밖) → 벽 아랫단을 못 찾아 바닥 기준 1.77m 를 전부 당겼다. 변의 벽 아랫단 기준 깊이로 바꾸고(벽 없으면 바닥), 비율 상수 `OpenEdgeParallaxScale` = **0.5**(≈0.89m). 물 한계 x −9.60~8.71 · z −18.51~19.80. 화면 보고 이 상수만 조절.
  - ⚠️ 뒤집음(팀장 '처음처럼 꽉 채우고 맵 밖 비주얼만 없애라'): 네 변을 다 벽 안쪽 면으로 잘라 화면 위쪽(먼 쪽 변)이 덜 찼다. 맵 밖으로 비치는 건 **카메라를 향한 변(+x·−z)**뿐 → 먼 쪽 변은 자르지 않음(처음 범위). M_A 4,808칸 · M_B 4,604칸.
  - 🔴 **진짜 원인(팀장 '두 문제만 합쳤다')**: 같은 존 프리팹이 슬롯마다 **0·90·180·270° 회전 배치**(ZoneSlot.Rotations — Stage1 M_A 0·90°, M_B 0·90·270°, 튜토리얼 M_A 180°). 프리팹 기준 +x·−z 를 카메라 쪽으로 잘라, 180° 배치에선 그 변이 먼 쪽(덜 참)·안 자른 변이 카메라 쪽(띠)이 됐다.
    → 도구가 열린 면 존마다 **회전 4가지 메시**(물·바닥, 0° = 기존 이름, `_r1~_r3`)를 존 로컬 카메라 방향으로 각각 잘라 만들고, `ZoneWater` 가 OnEnable 에서 존의 실제 회전(`Euler(0, 90·YawSteps, 0)`)으로 고른다. 시차 비율 다시 1.0. 프리팹 편집 화면엔 0° 메시.
    - 시도·폐기: 열린 면 바닥을 2.8m 안쪽에서 끝내기 → 가장자리 물 픽셀의 시선이 바닥에 안 닿아 물 안 검은 얼룩.
    - 촬영 메뉴 `0e` 가 열린 면 존을 4회전으로 찍는다(`<존>_y<각>_cam<n>.png`).
  - ⚠️ 뒤집음(팀장 10-01 '자른 부분만 다시 채워라'): 열린 면 자르기 전부 끔(`ClipOpenEdges = false`, 코드 보존) → M_A 5,820칸 · M_B 5,356칸(처음 꽉 찬 상태). 회전별 메시 12개 삭제(참조 0).
- 10-01 팀장: '존 안 물이 꼭 하나여야 하나? 왼쪽·오른쪽 따로 조절하면 된다' → **떨어진 물 덩어리마다 오브젝트 하나**: `Water`(ZoneWater) └ `WaterPart_N` └ `WaterWaves`·`WaterBed`. 원점 = 덩어리 범위 중심 → Scale X/Z 로 그 중심 쪽으로 줄어든다(물·거품은 월드 UV 라 무늬 안 늘어남). 이름은 중심 (z, x) 순 — **재생성해도 같은 이름 조각의 위치·크기 유지**. M_A 2 · M_B 2 · S_A 4. 이전 통짜 메시는 삭제(참조 0).
