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
