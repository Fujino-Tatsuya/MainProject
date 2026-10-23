# 맵 라이팅 — 카툰풍 던전 초안 (`VP_CombatLevel_Toon`)

작성일: 2026-10-13 · 브랜치: `art/MapLightingDraft` · 상태: **초안, Play 미확인**

이전 기준값과 Zone 로컬 조명 리그는 [map-rendering-lighting-handoff.md](map-rendering-lighting-handoff.md)를 본다
(그 문서의 "정본 = `4.MapScene-trensparent`"는 낡았다 — 현재 Bootstrap 은 `4.MapScene` 으로 들어간다).

## 1. 방향

- **차갑고 어두운 바탕 + 따뜻한 국소 조명**으로 색 대비. 바탕(환경광·주광 그림자 쪽)은 청회색으로 낮추고,
  밝기는 Zone 로컬 조명(따뜻한 Spot/Point)이 맡는다.
- 카툰이라 그림자는 **Hard 유지**(가장자리 또렷), 대신 strength 를 낮춰 그림자 속이 새카맣게 뭉개지지 않게.
- 톤매핑은 ACES → **Neutral** — ACES 의 채도 이동·하이라이트 뭉개짐을 피해 원색을 살린다.
- 원본 에셋은 덮어쓰지 않았다. Volume 은 새 프로파일, 씬 값은 아래 표로 되돌린다.

## 2. 이번에 안 건드린 것

Zone 로컬 조명(`Lighting_Target_CombatLevel`), `FogProfile.asset`, URP 에셋, 플레이어 머티리얼,
`LightingData.asset`(재베이크 안 함), 스카이박스 머티리얼.

## 3. Volume — `Global Volume` (PostProcessing 그룹)

`sharedProfile`: `VP_CombatLevel_Target.asset` → **`VP_CombatLevel_Toon.asset`** (같은 폴더, Target 복제본)

| 항목 | Target (원본, 그대로 둠) | Toon (새) |
|---|---|---|
| Tonemapping | ACES | **Neutral** |
| Color Adjustments · Post Exposure | 0.2 | **-0.15** |
| · Contrast | -8 | **12** |
| · Saturation | -10 | **8** |
| · Color Filter | 흰색 | 흰색 |
| White Balance · Temperature | 10 | **-6** |
| · Tint | 0 | 0 |
| Bloom · Intensity | 0.12 | **0.35** |
| · Threshold | 1.25 | **1.1** |
| · Scatter | 0.55 | **0.6** |
| Vignette | 꺼짐 (0.15 / 0.72) | **켜짐 · Intensity 0.22 · Smoothness 0.45 · 검정** |
| Chromatic Aberration | 0.015 | 0.015 |
| Depth Of Field | 꺼짐 | 꺼짐 |

## 4. 씬 값 — `4.MapScene.unity`

| 항목 | 전 (HEAD) | 후 |
|---|---|---|
| Environment Lighting Source | Skybox (`Skybox_IndustrialFoundry`) | **Gradient (Trilight)** |
| Ambient Sky | (0.471, 0.604, 0.710) | **(0.26, 0.30, 0.40)** |
| Ambient Equator | (0.314, 0.420, 0.514) | **(0.16, 0.18, 0.24)** |
| Ambient Ground | (0.149, 0.216, 0.275) | **(0.07, 0.07, 0.10)** |
| 2차(환경광 ×1.4) · Sky / Equator / Ground | (0.26, 0.30, 0.40) / (0.16, 0.18, 0.24) / (0.07, 0.07, 0.10) | **(0.36, 0.42, 0.56) / (0.22, 0.25, 0.34) / (0.10, 0.10, 0.14)** |
| 3차(10-23 더 밝게) · 환경광 ×1.25 | (0.36, 0.42, 0.56) / (0.22, 0.25, 0.34) / (0.10, 0.10, 0.14) | **(0.45, 0.52, 0.70) / (0.28, 0.31, 0.42) / (0.13, 0.13, 0.18)** |
| 3차 · 주광 강도 / 노출(Toon 프로필) | 0.55 / −0.15 | **0.75 / +0.25** |
| 3차 · 벽에 붙은 존 스포트라이트 | 벽에 붙음 | 벽에서 수평 3m 이상 — `Tools/Map/Authoring/벽에 붙은 조명 안쪽으로` |
| Ambient Intensity | 1.15 | **1** (Gradient 에선 쓰이지 않음) |
| Reflection Intensity | 0.6 | **0.4** |
| Directional Light 색 | (0.991, 0.885, 0.780) | **(1.0, 0.86, 0.68)** |
| Directional Light Intensity | 0.3 | **0.55** |
| Directional 그림자 | Hard · Strength 1 | Hard · **Strength 0.65** |
| Directional 회전 (euler) | (70.4, 210, 0) | **(50, 210, 0)** — 70° 는 거의 수직이라 그림자가 짧아 형태가 안 읽힘 |

스카이박스 머티리얼 참조는 그대로다(배경 표시용). Environment Lighting 만 Gradient 로 바꿨다.

## 5. 되돌리기

1. `PostProcessing/Global Volume` 의 Profile 을 `VP_CombatLevel_Target.asset` 으로 되돌린다.
2. Lighting 창(Environment) 과 `Environment/Directional Light` 를 §4 "전" 열 값으로 되돌린다.
3. 또는 git 으로 `4.MapScene.unity` 만 이 커밋 이전으로 되돌린다. `VP_CombatLevel_Toon.asset` 은 참조만 끊기면 남아 있어도 무해하다.

## 6. 알려진 리스크 · Play 확인 필요

- **많이 어두워질 수 있다.** 환경광 SH(L0)가 Skybox 0.283 → Gradient 0.025 로 약 1/11 이 된다.
  에디터(Play 아님) 렌더에서 튜토리얼 시작 복도 평균 밝기가 HEAD 0.0089 → 초안 0.0017. 그 복도는 에디터에서
  Zone 로컬 조명이 없는 구간이라 둘 다 어둡고, 실제 밝기는 Play 에서 Zone 조명이 깔린 상태로 판단해야 한다.
  너무 어두우면 먼저 Ambient Sky/Equator 를 1.5~2배로 올린다(Gradient 에서는 Intensity 가 안 먹는다).
- **`LightingData.asset` 이 있다**(`Assets/0.Scenes/MainFlow/4.MapScene/LightingData.asset`, Lighting Settings 에셋은 없음).
  베이크된 환경 SH 가 로드 시 우선하면 Play 에서 환경광 변경이 안 보일 수 있다. 그때는 Lighting 창 Generate Lighting
  (또는 Environment 만 재계산)이 필요하다 — 이번엔 재베이크하지 않았다.
- 작업 직전 에디터 메모리의 Ambient Intensity 가 저장 안 된 **1.57** 이었다(디스크 1.15). 누군가 라이브 튜닝 중이던 값일 수 있다.
- 캡처(에디터 렌더, Play 아님): `C:\UnityProject\_lane-artifacts\MainProject-Worktree\lighting_before.png`(HEAD 값) /
  `lighting_after.png`(초안). 같은 시점·같은 씬 상태에서 값만 바꿔 렌더했다.

## 로비씬(3.LobbyScene) — 첫 초안 값 그대로 (2026-10-23)
맵의 1차 초안 값(환경광 ×1.4·3차 밝게 이전)을 그대로 적용. 볼륨은 맵과 따로 `3.LobbyScene/VP_Lobby_Toon`(VP_CombatLevel_Toon 복제, 노출 −0.15)을 쓴다 — 맵 쪽 노출을 바꿔도 로비는 그대로.

| 항목 | 전 | 후 |
|---|---|---|
| 볼륨 프로필 | VP_CombatLevel_Target | **VP_Lobby_Toon** (Neutral · 노출 −0.15 · 대비 +12 · 채도 +8 · WB −6 · 블룸 0.35 → **0.8**(10-23, 형광등 번짐) · 비네트 0.22) |
| 환경광 | 스카이박스 | **그라데이션** (0.26, 0.30, 0.40) / (0.16, 0.18, 0.24) / (0.07, 0.07, 0.10) |
| 반사 강도 | 1 | **0.4** |
| 주광 | 강도 1, 색 (1, 0.957, 0.839), 그림자 강도 1, 각도 50° | **강도 0.55, 색 (1, 0.86, 0.68), 그림자 강도 0.65**, 각도 50° 그대로 |

### 로비 바깥 검정 바닥
`Environment/Env_Black_Outside` — MapScene 의 `env_black_tutorial`(튜토리얼 방·통로 모양대로 구멍 뚫린 큰 판 + `Black.mat`)과 같은 방식.
**로비 자리는 뚫려 있고 바깥만 가린다. 구멍 = 로비 건축물 외곽선과 오차 없이 일치**(사용자 요청 10-23).
- 높이: **y 2.165 = 벽 윗면(2.2) 바로 아래**(사용자가 직접 맞춘 값). 보행면 아래(−0.5)에 두면 게임 카메라(61°)에서 바깥벽 밑
  (벽이 바닥판보다 0.11m 돌출 + 바닥판 밑 −0.3 아래가 빔)으로 빈 공간 띠가 약 20px 보인다. 도구는 기존 오브젝트의 높이를 그대로 쓴다.
- 만드는 법: `Tools/Map/Authoring/로비 바깥 검정 바닥 만들기`(3.LobbyScene 활성, Play 정지) → 씬 저장.
  `PF_Lobby_Architecture_v015` 메시 중 보행면 기준 −1~+3m 에 걸치고 위에서 봐서 넓이가 있는 삼각형(바닥·벽 윗면)을 모아,
  그 꼭짓점 X·Z 좌표(mm)를 그대로 격자선으로 쓰는 불균등 격자에 찍는다(벽 면 = 격자선) → 테두리에서 채운 칸 = 바깥, 나머지 = 구멍.
  사각형끼리·벽 아래로 5mm 겹쳐 이음새 틈을 막는다. 메시는 `3.LobbyScene/Env_Black_Outside_Mesh.asset`(같은 에셋을 고쳐 써 guid 유지).
- 실행할 때마다 콜라이더 레이캐스트로 구멍 테두리 전체(0.5m 간격, 테두리 ±1cm)를 실측해 콘솔에 오차를 남긴다.
- 2026-10-23 결과: 구멍 670.4㎡, 사각형 17장(정점 68), 테두리 428곳 실측 **오차 0cm**(빈 공간 0곳·구멍 작음 0곳).
  테두리 X −20.11·−4.61·−0.61·7.61·12.11·16.11 / Z −16.11·−8.11·−0.11·8.11·12.11 = 벽 바깥면, 남쪽 정문은 문틀 −16.155·문턱 −16.2 까지.
- 로비 레이아웃이 바뀌면 다시 실행하고 씬을 저장한다. 콜라이더 없음·그림자 드리우기 끔.

## 로비 형광등 (2026-10-23)
URP 에는 실시간 튜브 라이트가 없어서 **바닥 빛 줄기(일자형 모양) + 포인트 라이트 1개(주변 밝힘)**로 형광등 느낌을 낸다.
**막대(관·마개) 메시는 두지 않는다** — 탑다운이라 천장 요소가 필요 없고, 공중의 막대가 바닥에 놓인 것처럼 보였다(사용자 10-23).
- 형광등 한 개 = 이름이 `_Fluorescent` / `_Fluorescent (n)` 인 묶음. 묶음의 위치·회전 = 형광등 자리·방향(로컬 X). 묶음을 복제해 배치한다(현재 `Environment/Fluorescent_South` 아래 18개).
- 도구(`Assets/1.Scripts/Map/Editor/LobbyFluorescentAuthoring.cs`, `Tools/Map/Authoring/`):
  - `형광등 — 선택 위치에 놓기`: 빈 오브젝트 → 그 아래 묶음 생성(있으면 다시 조립). 포인트 라이트 → 옆에 `Fluorescent_<이름>` + 원래 라이트 끔.
  - `형광등 — 씬 전체 다시 조립`: 활성 씬의 모든 묶음을 지금 설정으로 다시 만든다(위치·회전 유지, 예전 막대 메시도 지움).
  - `형광등 — 바닥 빛 줄기 사용`(체크 메뉴, EditorPrefs, **기본 켬 — 사용자 결정 10-23 C안**): 묶음 바로 아래 바닥(y 0.012)에 길쭉한 가산 빛(2.6×0.75m, 밝기 0.18 — 10-23 0.24 에서 살짝 옅게) — `M_FluorescentStreak.mat`(URP Unlit 가산) + `T_FluorescentStreak.png`(생성 텍스처).
  - `형광등 — 라이트 수/0개 (빛 줄기만)·1개·3개`(체크 메뉴, EditorPrefs, **기본 1개 — 사용자 10-23**). 1개 = 세기 2.6·범위 3.8, 3개 = 0.6m 간격·세기 1.2·범위 3.5. 그림자 끔, 색 (0.94, 0.98, 1).
  - 비교(10-23, 홀): 1개는 3개와 밝기 거의 같음(화면 평균 63.8 → 63.5), 0개는 27% 어두워지고 소품·캐릭터가 형광등 빛을 못 받아 빛 줄기가 '칠한 무늬'처럼 보임 → **1개 채택**.
  - 연산: PC 렌더러 Forward+(화면당 추가 라이트 한도 256, 물체당 4개 제한 없음). 형광등 36개 기준 3개였을 때 라이트 108개·한 화면 평균 48/최대 82개 → 1개로 36개. 모바일 렌더러는 Forward(물체당 4개)라 겹침이 많으면 빠질 수 있다.
- 캡처: `조명 캡처 (게임 카메라 각도)` = PlayerFollowCamera(오프셋 (7,17,-7)·59.8°/-45°·FOV 34) + 후처리 → `Temp/LightingCapture/`.
  ⚠ 오프스크린 렌더는 **HDR 텍스처에 렌더해야** 블룸이 나온다(8비트 대상이면 URP 가 HDR 을 꺼 1.0 에서 잘린다).
- 로비 블룸 세기 0.35 → **0.8**(10-23 사용자 결정).
- 비교(10-23, 홀 가운데): 막대 제거 후 라이트만이면 바닥 빛이 둥근 얼룩 → 일자형 느낌이 사라진다. 바닥 빛 줄기를 켜면 유지된다 → **C안(라이트 + 바닥 빛 줄기) 확정**.
- 예전 막대용 `M_Fluorescent_Tube.mat`·`M_Fluorescent_Housing.mat` 은 참조가 0 이 된 뒤 10-23 삭제했다.
