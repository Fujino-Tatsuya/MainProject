# 핸드오프 — 진격의 충격(PAL_4_Q_BLAST) 부채꼴 충격 VFX

> 2026-11-03 · 민경(Claude) · 브랜치 `feature/VFX` · 계획 [PLAN.md](../../PLAN.md) 맨 위(이 작업이 최신일 때).
> 판정·피해 코드는 무변경 — 이미 나가던 특성 연출 사건을 받아 재생만 한다.

## ⚠️ 은희 님께 — 기획서와 판정 데이터가 다르다
- 기획서(`Re_C_특성_기획서_20261016`): "종료 순간 **전방 1m 지점 중심 반경 2m**"(원).
- 데이터 `9.ScriptableObject/Traits/Paladin/PAL_4_Q_BLAST.asset` 파생 hitArea: **Q 끝 지점에서 앞으로 90° 부채꼴, range 2m, forwardOffset 0**.
- 연출은 **실제 판정(부채꼴)** 에 맞췄다. 데이터를 기획대로 원으로 바꾸면 연출 모양도 다시 맞춰야 한다(민경에게 알려 주세요).

## 동작
- Q 가 정상 종료·홀드 해제·최대 시간 도달로 끝나면 0.05초 뒤 서버 판정 순간 `TraitFxTrait.PaladinAdvanceImpact` **Activated** 가 전 피어에 난다(`FirstMeleeMainSkill.RunTraitBlastServer`, 맞은 적 없어도 1번). 돌파의 방패 종료도 같다. 강제 중단·사망이면 없다.
- `PaladinSkillView` 가 받아 사건 도형의 부채꼴(`AttackSectorShape`) 포즈(`ShapePose` — forward/side/yaw 오프셋 반영)에 `FX_QBlast_Entry` 를 재생. **배율 = range**(부채꼴 길이 1 저작), 방향 = 마지막 진행 방향. 각도 90° 는 저작 고정.
- 맞은 적마다의 **Hit** 사건은 연출 없음.
- 구성: 앞쪽 금빛 섬광·별·발밑 빛 고임·불티 / 바닥을 앞으로 쓸고 나가는 금빛 충격파(약 0.15초에 끝)·지나간 자리·낮은 벽 / 흙먼지·돌 파편 / 공기 일렁임. 전체 약 0.65초. (바닥 균열 레이어는 민경 확인 후 뺐다.)

## 바뀐 파일
| 파일 | 내용 |
|---|---|
| `1.Scripts/Player/Skill/PaladinSkillView.cs` | 칸 `qBlast` · `HandleTraitFx` 에 `PaladinAdvanceImpact` Activated 분기 → `PlayQBlast`(부채꼴 없으면 생략) |
| `1.Scripts/Effects/EffectCatalog.cs` · `9.ScriptableObject/Effects/EffectCatalog.asset` | `QBlast` 등록(프리웜) |
| `2.Prefabs/Player/Paladin/Player_Paladin.prefab` | `PaladinSkillView.qBlast` 연결(1줄) |
| SVN `50.Art/VFX/Common/Players/Player1/QSkill/QBlast/` | `FX_QBlast`(+`_Entry`) · 재질 10 · 텍스처 9 · 메시 6 |

## 원본 제작(MyProject)
- 빌드 메뉴 `Tools/VFX/Q Blast (팔라딘 진격의 충격) 빌드` → `Export/Player1/QSkill/QBlast/` · 미리보기 `Scenes/QBlastPreview.unity`(`QBlastTester` — 정면 팔라딘 + 2m·90° 부채꼴 가이드 + 허수아비 안 2·밖 1).
- 균열 레이어는 빌더에서도 뺐다. 🔴 빌더를 다시 돌리면 프리팹의 손 수정은 덮어써진다.

## 확인
- DebugTraitLab(Play): `PAL_4_Q_BLAST` 넣고 Q → 최대 시간 종료 → 끝 지점·진행 방향에 `FX_QBlast` 1개(배율 2) — 로그로 확인.
- [ ] 민경 Play: 화면에서 크기·방향·타이밍 · 돌파의 방패(PAL_2_DASH) 종료 · MPPM 클라 화면.

## SVN / git
- SVN: `50.Art/VFX/Common/Players/Player1/QSkill/QBlast/` 폴더 add.
- git: 위 코드·SO·프리팹 + 문서.

## 후속 방패 레이어 (2026-11-05 · 민경 확정·Codex 파일 구현)
- `Meshes/SM_QBlast_Shield.asset` + 새 `.meta`: `SM_GuardianWill_ShieldPanel`의 정점만 ×0.5 + (0, 0.425, 1)로 변환. 34정점·96인덱스, 법선·UV·색·삼각형·방향 유지. AABB center (0, 0.424999990, 0.965000004), extent (0.275000006, 0.337499999, 0.034999996).
- `FX_QBlast` 자식 `B_Shield`·`B_ShieldGlint`: 원점 (0,0,0), 앞 띠와 동일한 Initial/Emission/Size 모듈, 수명 0.3초·Hierarchy 배율. 방패 면이 원점에서 커지며 띠 바깥 끝을 따라 나간다. 곡선 최종값은 원본 그대로 1.02(1.0으로 보정하지 않음).
- 흰색 ColorOverLifetime: 방패 알파 0 → 1(정규화 0.1, 약 0.03초) → 1(0.8) → 0(1). 반짝임 알파 0(0·0.35) → 1(0.5) → 0(0.7·1). 재질은 각각 기존 `M_GuardianWill_ShieldProj`·`M_GuardianWill_WallGlint`.
- `B_ShieldShatter`: 기존 `M_GuardianWill_Mote`, Billboard, World 공간, Shape 배율. 지연 0.27초 뒤 16개 단발, 수명 0.3~0.45초·크기 0.04~0.08·속도 1.5~3.5·중력 배율 -0.2·알파 1→0. Box 중심 (0,0.425,1), 크기 (0.55,0.675,0.05), 방향 무작위 1. 최장 종료 0.72초, 엔트리 `computedDuration` 0.65→0.75초.
- 기존 36개 YAML 객체는 루트 Transform의 자식 참조 3개 추가 외 동일. 추가 12개 fileID 고유·내부 참조 정상, 루트 자식 8→11. 기존 프리팹·엔트리·참조 메시·재질의 `.meta` SHA256 동일.
- Unity 에디터 없이 제작. 프로젝트 MCP 브리지의 Unity YAML 부분집합 파서로 프리팹 48개 객체·메시·엔트리 해석 확인(미해석 0·잘림 없음), 크기 곡선 전체와 파티클 설정 수치 검증 통과. 실제 Unity import·셰이더 표시·Play·MPPM은 민경 확인 필요.
- SVN 대상: `FX_QBlast.prefab`, `FX_QBlast_Entry.asset`, 새 메시와 `.meta`(4개). Git 문서: `PLAN.md`, 본 문서. 앞선 텍스처 보정은 이번 작업에서 추가 변경하지 않음.

## 2차 — 안개·바람·팔라딘 방패 (2026-11-05 · 민경 확정·Codex 파일 구현)

### 레이어 설정

시간은 초, 지연은 프리팹 재생 시작 기준이다. UberFXSG GUID는 `7f11fe28133f1a74594785107b98f2fe`. 실제 메인 텍스처 속성은 `_MainTex`이며 `_MainTexture`만 바꾸면 연결되지 않는다.

| 레이어 | 셰이더 / 재질 | 메인 텍스처 | 지연 | 수명 |
|---|---|---|---:|---:|
| B_Wave_Front | 기존 IntegratedEffect / M_QBlast_Front (보존) | T_QBlast_FrontBand (보존) | 0 | 0.30 |
| B_Wave_Wall | UberFXSG / M_QBlast_Wall (FuzzAdd_Soft_Default 복제) | 새 T_QBlast_WallFlame | 0.02 | 0.36 |
| B_Wave_Fill | UberFXSG / M_QBlast_Fill (FuzzAdd_Soft_Default 복제) | 새 T_QBlast_FillRipple | 0.04 | 0.44 |
| B_Wave_Fog | UberFXSG / 새 M_QBlast_Fog (FuzzAlpha_Soft 복제) | 새 T_QBlast_FogNoise (메인+디테일) | 0.08 | 0.60 |
| C_WindStreaks | UberFXSG / 원본 Wind_Stuff_something_idk_1_add_Soft 1 | 원본 MainTex 8c59664e… + AlphaOverride aaf863c8… | 0 | 0.28 |
| C_WindRing | UberFXSG / 원본 Wind_floor_Ring_accent_add_Soft | 원본 MainTex 2ab49b65… + AlphaOverride c9c8818f… | 0 | 0.30 |
| B_Shield | 기존 IntegratedEffect / 공용 M_GuardianWill_ShieldProj | 새 T_Paladin_ShieldEmblem | 0 | 0.30 |
| B_ShieldGlint | 기존 IntegratedEffect / M_GuardianWill_WallGlint (보존) | 기존 텍스처 보존 | 0 | 0.30 |
| B_ShieldShatter | 기존 IntegratedEffect / M_GuardianWill_Mote (보존) | 기존 텍스처 보존 | 0.27 | 0.30~0.45 |
| A_ImpactFlash / A_ImpactStar / A_Sparks / C_Dust | 기존 셰이더·재질·텍스처·객체 보존 | 보존 | 0 | 0.20 / 0.20 / 0.35 / 0.65 |

- 앞 띠 4개 YAML 객체와 재질·PNG 해시 동일. 방패 3개 레이어 객체도 그대로이며 공용 방패 재질의 텍스처만 교체했다. 작업 시작 파일에 `D_Distortion` 자식은 없었으며 이번에 복구하거나 추가하지 않았다. `M_QBlast_Distortion.mat` 등 기존 관련 에셋은 보존했다.
- 벽·채움의 기존 Size 모듈 전체는 보존했다. 벽은 기존 X/Z 성장·Y 고정(Separate Axes), 채움은 기존 균일 성장이다. 새 안개·바람·링은 앞 띠의 Size 모듈을 그대로 복제했다. 수명과 지연 차이로 박자를 나눴다.
- 새/변경 레이어의 ColorOverLifetime RGB는 끝까지 흰색으로 유지하고 알파만 낮춘다. 벽·안개·바람의 StartColor는 밝은 금 `(1,0.96,0.68,1)`. 채움은 메시 색을 원점 `(1,1,0.94)`→바깥 `(1,0.94,0.61)`로 보간하고 알파 0.04→0.75로 원점을 옅게 한다.
- 안개는 y=0.04, 90° 부채꼴 12개 반지름 구간을 가진 메시로 양옆·안팎 경계를 부드럽게 페이드한다. 메인 패닝 `(0.08,-0.18)`, 디테일 패닝 `(-0.12,0.09)`, DetailMultiplyChannel R 활성. 벽/채움/안개에 IntegratedEffect 미사용. 세 재질은 `_DISABLEEROSION` 키워드·수치를 함께 켜 UV Custom Data 기반 침식 대신 텍스처 알파와 ColorOverLifetime으로 페이드한다.
- 바람은 새 메시의 8개 가느다란 곡선 스트립으로 표현한다(메시 입자 1개 안에 8획, 각도 -36°~+36°). 원본 Piloto 재질은 바꾸지 않았다. 링도 평면 UV를 가진 **90° 부채꼴 메시**로 잘라 원형 전체가 나오지 않는다. 링 y=0.03, 획 y=0.055.
- 엔트리 computedDuration 0.80초. 안개 종료 0.68초, 방패 흩어짐 최장 종료 0.72초.

### UV·PNG·방패 검증

- 원본 Fill: 98정점, UV `(U,V)=(각도 인덱스/48, 반지름)`, 원점 V=0·바깥 V=1. 가로 물결의 일정 V 선이 동심 호가 된다. 새 FillRipple 메시도 원본 위치·UV·인덱스를 유지하고 공간 색·알파만 추가한다. Fog도 같은 극좌표 UV다.
- 원본 Wall: 바닥 y=0에서 V=0, 상단 y=0.14에서 V=1. ShaderGraph의 TilingAndOffset Offset = Panning × Time(양의 오프셋은 무늬를 음의 UV 방향으로 이동) 연결을 확인했다. `_MainTexturePanning.y=-0.65`는 위쪽, Fill `-0.75`는 바깥쪽 이동이다. Wall은 U Repeat/V Clamp, Fill·Fog는 U/V Repeat.
- ImageGen 내장 도구로 생성하고 RGBA PNG로 저장했다. Wall/Fill/Fog는 512×512, 방패는 1024×1024. 리사이즈 후 경계 32px를 대칭 크로스페이드해 Wall 가로 경계, Fill·Fog 양축 경계 RGBA 불일치 **0**을 확인했다. 앞 띠 PNG는 손대지 않았다.
- 방패: 위가 넓고 아래가 뾰족한 히터형, 두꺼운 마디 베벨 바깥 금 테두리 + 얇은 안쪽 선, 끝이 넓어지는 금색 십자, 사선 반사광 한 줄. 면은 위가 밝고 아래가 더 투명하다. 최종 PNG 샘플 알파: 면 상단 (0.34,0.25)=208, 하단 (0.34,0.60)=105, (0.40,0.72)=104, 십자 중앙=252. 좌표는 이미지 좌상단 기준 정규화 좌표.
- 원본 ShieldPanel UV 범위 U=[0,1], V=[0,1]이며 수직 y 증가에 V 증가(메시 전체에 올바른 방향으로 맵핑). Q의 구운 메시도 같은 UV를 유지한다. E `FX_GuardianWill_Barrier/B_Shield`가 동일 공용 재질 GUID `6d0100c2335de1842b60a9dd6e5a06a6`을 참조함을 확인했으므로 E도 새 방패가 표시된다. E 프리팹·원본 메시 자체는 변경하지 않았다.

### 파일 목록 (이번 2차 작업)

아래 아트 경로는 `Assets/50.Art/VFX/Common/Players/Player1/QSkill/QBlast/` 기준. 기존 `.meta`는 모두 보존했다. 새 아트 파일은 각각 새 GUID의 `.meta`를 함께 추가했다.

- 변경: `FX_QBlast.prefab`, `FX_QBlast_Entry.asset`, `Materials/M_QBlast_Wall.mat`, `Materials/M_QBlast_Fill.mat`.
- 변경(공용): `Assets/50.Art/VFX/_Materials/Player1/M_GuardianWill_ShieldProj.mat`.
- 새 PNG(+각 `.meta`): `Textures/T_QBlast_WallFlame.png`, `Textures/T_QBlast_FillRipple.png`, `Textures/T_QBlast_FogNoise.png`, `Textures/T_Paladin_ShieldEmblem.png`.
- 새 재질(+`.meta`): `Materials/M_QBlast_Fog.mat`.
- 새 메시(+각 `.meta`): `Meshes/SM_QBlast_FillRipple.asset`, `Meshes/SM_QBlast_Fog.asset`, `Meshes/SM_QBlast_WindStreaks.asset`, `Meshes/SM_QBlast_WindRing.asset`.
- 문서: `PLAN.md`, 본 핸드오프. 아트 5개 변경·18개 신규(메타 포함), 문서 2개 갱신. 기존 메시 파일은 전부 보존.
- 임시 스크립트·원본 해시·정적 검증 보고서는 OS Temp의 `qblast-v2-codex/`에만 저장했다. ImageGen 원본은 Codex generated_images에 있으며, 최종 PNG는 위 프로젝트 경로에 저장했다.

### 검증 범위·후속 확인

- Unity YAML 부분집합 파서로 프리팹 **56개 객체**·변경/신규 메시·재질·엔트리를 해석(미해석 0·잘림 없음). 새 fileID 12개 고유, 내부 참조 정상, 루트 자식 **10→13**. 기존 44개 객체 중 변경은 벽/채움 PS·Renderer와 루트 자식 목록뿐이며 나머지 객체 39개는 동일(검증 허용 집합 7개 외 37개도 원문 비교 통과).
- 실제 변경 파일 해시 비교로 기존 `.meta` 변경 0, 앞 띠 PNG·재질·객체 보존, 방패 레이어 객체 보존, 원본 메시·Piloto 원본 재질 보존을 확인했다. 모든 메시 인덱스 범위·버퍼 길이·90° 영역·반지름 UV 수치 검증 통과.
- 에디터 없이 파일 제작한 결과다. Unity import·실제 셰이더 표시·블룸·소프트 파티클 깊이 페이드·탑다운 가독성·Play·MPPM은 미실행. 민경은 밝기·안개 양·벽 패닝·방패 크기/방향과 Q/E 양쪽을 Play에서 확인할 것.

### 이미지 생성 프롬프트 요약

ImageGen 내장 도구 사용(외부 API/CLI 미사용). 참조는 `Paladin_Shield_BaseColor.png`이며 아틀라스를 복사하지 않고 한 장의 정면 방패로 재구성했다.

- Wall: square transparent achromatic VFX mask, dense white base at bottom, shredded flame-like tips fading into transparent upper 20%, vertical wind strands, seamless horizontal edges, no UI rectangle.
- Fill: transparent white UV mask, 7–10 broken horizontal ripple bands with mottled turbulence, U=angle/V=radius polar mapping supplies concentric arcs, tile both axes, no painted circles.
- Fog: opaque grayscale seamless two-axis soft cloud density noise, broad wispy curls and smaller turbulence, no sky/perspective/vignette.
- Shield: one upright heater shield, thick segmented brilliant gold outer bevel + thin gold inner contour, flared gold knightly cross, translucent pale face fading downward, one diagonal reflection, transparent exterior. 추가 편집: interior membrane more transparent toward bottom while cross and both rims stay sharply luminous.

## 2차-2 — 기하 무늬·고정 띠·채움 림·가산 안개 (2026-11-05 · Codex)

민경이 확정한 후속 요청에 따라 에디터 없이 파일로 구현했다. E 방어 `T_GuardianWill_Wall.png`(256×128)·`T_GuardianWill_Ground.png`(256×256)를 직접 열고, 흰색 RGB·알파 무늬가 보이도록 어두운 배경에 합성해 확인했다. 새 PNG는 그 기하 구성을 기준으로 코드로 만든 흰색 선·단색 면·알파 마스크다(이 후속 작업에는 ImageGen 미사용).

### 레이어별 흐름

아래 레이어는 모두 **UberFXSG 가산(SrcAlpha/One)**. RGB는 ColorOverLifetime에서 어두워지지 않고 알파만 사라진다. 고정은 UV 패닝이 없다는 뜻이며 크기 성장은 각 원본 레이어를 따른다.

| 레이어 | 재질 / 텍스처 | 흐름 | 지연 / 수명(초) |
|---|---|---|---:|
| B_Wave_Wall | M_QBlast_Wall / 새 T_QBlast_WallLattice | 마름모 격자·교차 마디가 위로(V=-2) | 0.02 / 0.30 |
| B_Wave_WallBaseBand (새) | 새 M_QBlast_WallBaseBand / 새 T_QBlast_WallBaseBand | 하단 밝은 단색 띠 + 위 가는 선 고정 | 0.02 / 0.30 |
| B_Wave_Fill | M_QBlast_Fill / 새 T_QBlast_FillWaveLines | 가로선 5주기 → 동심 호가 바깥으로(V=-0.75) | 0.04 / 0.44 |
| B_Wave_FillRunes (새) | 새 M_QBlast_FillRunes / 새 T_QBlast_FillRunes | 눈금 32주기·가는 선 2줄·반투명 그라데이션 고정 | 0.04 / 0.44 |
| B_Wave_FillRim (새) | 새 M_QBlast_FillRim / 내장 흰색 | 바깥 호 + 좌우 직선 외곽선 고정 | 0.04 / 0.44 |
| B_Wave_Fog | M_QBlast_Fog / 기존 T_QBlast_FogNoise (메인+디테일) | (0.08,-0.18) / (-0.12,0.09) 두 방향 패닝 | 0.08 / 0.60 |

- 벽 패닝은 작업 시작 파일에서 이미 V=-2로 튜닝되어 있었으므로 유지했다. 반면 벽 startDelay는 0초여서 이번 요청에 명시된 **0.02초**로 벽과 하단 띠를 맞췄다. 벽 수명은 현재 파일 값 **0.30초**를 유지했다. 나머지 기존 수명·크기 곡선·박자는 보존했다.
- 벽 격자는 가로 6·세로 3주기, 교차점에 알파 강조. 단색 띠는 별도 같은 벽 메시 레이어여서 격자와 함께 스크롤되지 않는다. 새 벽 PNG는 512×256 RGBA, 가로 경계 RGBA 불일치 0. 격자는 U/V Repeat, 고정 띠는 U Repeat/V Clamp.
- 채움은 기존 `SM_QBlast_FillRipple`(U=각도, V=반지름)을 유지한다. 물결 PNG만 패닝하고 눈금/바탕은 별도 레이어로 고정했다. 물결·눈금 PNG는 512×512 RGBA, 가로 경계 RGBA 불일치 0. 눈금 띠는 V≈0.79~0.88, 양옆 가는 선은 V=0.775/0.90.
- 림 새 메시 `SM_QBlast_FillRim`: **90° 바깥 호 전체 + 원점까지 이어진 양쪽 직선**. 길이 1 기준 본체 0.035(3.5%)와 안쪽 번짐 0.02, 원점 근처 폭만 자연스럽게 모인다. 바깥 테두리가 가장 밝고 안쪽으로 알파가 낮아진다. 460정점·2016인덱스, 모든 정점이 부채꼴 안쪽이며 radius≤1. range 2 재생 시 본체 폭 약 0.07m. Fill과 Size 모듈·startDelay·startLifetime 동일.
- 안개는 FuzzAdd_Soft_Default 기반으로 교체했다. StartColor **(1,0.97,0.88,1)**, 알파 곡선 최대 0.38 유지(공간 알파·두 노이즈가 추가로 약하게 하므로 앞 띠보다 약한 안개). MainAlphaChannel R, DetailMultiplyChannel R, 두 텍스처 슬롯 모두 FogNoise. 가산 템플릿 복제 후 노이즈·패닝 슬롯을 명시적으로 지정해 기본 흰색 텍스처/패닝 0으로 남지 않도록 검증했다.

### 변경 파일

경로는 `Assets/50.Art/VFX/Common/Players/Player1/QSkill/QBlast/` 기준.

- 변경 4개: `FX_QBlast.prefab`, `Materials/M_QBlast_Wall.mat`, `Materials/M_QBlast_Fill.mat`, `Materials/M_QBlast_Fog.mat`.
- 새 PNG 4개(+각 `.meta`): `Textures/T_QBlast_WallLattice.png`, `Textures/T_QBlast_WallBaseBand.png`, `Textures/T_QBlast_FillWaveLines.png`, `Textures/T_QBlast_FillRunes.png`.
- 새 재질 3개(+각 `.meta`): `Materials/M_QBlast_WallBaseBand.mat`, `Materials/M_QBlast_FillRunes.mat`, `Materials/M_QBlast_FillRim.mat`.
- 새 메시 1개(+`.meta`): `Meshes/SM_QBlast_FillRim.asset`.
- 문서: `PLAN.md`, 본 문서. 기존 Flame/Ripple PNG는 삭제하거나 덮어쓰지 않았다. 엔트리는 **0.80초 유지**(신규 레이어 최장 종료 0.48초, 전체 최장 종료 0.72초).

### 검증

- YAML **68객체**, 미해석·잘림·fileID 충돌 0. 새 fileID 12개, 루트 자식 **13→16**. 모든 내부 참조·부모/자식·GO/Component 역참조 정상.
- 기존 56객체 중 루트 자식 목록·안개 StartColor·벽 startDelay를 제외한 **53객체 원본과 동일**. 앞 띠(재질·PNG 포함)·방패 3개·바람 2개·A_*·C_Dust를 보존했다. 시작 프리팹에 D_Distortion 자식은 없었으며 관련 기존 재질/텍스처도 변경하지 않았다.
- 작업 범위의 기존 파일 전체 해시 비교: 실제 변경은 위 4개뿐이며 기존 `.meta` 변경 0. 모든 신규 GUID 고유. 메시 버퍼 길이·인덱스 범위·90° 호·양쪽 직선·반지름 1 이내를 수치 검증했다. 새 레이어의 크기 곡선·수명·지연 일치, 가산 블렌드와 패닝/텍스처 참조를 검증했다.
- 임시 스크립트·기준 해시·검증 JSON·어두운 배경 합성/림 메시 미리보기는 **OS Temp/qblast-v2-2-codex/**에만 있다. 실제 Unity import·Play·MPPM은 미실행. 밝기·소프트 파티클 깊이 페이드·겹침은 민경 Play 확인 필요.
