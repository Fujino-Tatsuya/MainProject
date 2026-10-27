# 인수인계 — 타이틀 화면 카메라 구도·라이팅·심도 (2026-10-25, 아트)

타이틀(1.TitleScene) 연출 값만 바꿨다. 코드·프리팹 원본 변경 없음.

## 바뀐 것

| 파일 | 내용 |
|---|---|
| `Assets/0.Scenes/MainFlow/1.TitleScene.unity` | VCam_Far (0, 4.25, 13.8) · 숙임 5.1° / VCam_Near (0, 2.77, 3.47) · 숙임 0° · 화각 24.89 / TitleOffice 씬 오버라이드: `Geometry/Spot Light` 세기 55·4200K, `Point Light (1)` 세기 2.4·범위 4.8·따뜻한 색·위치 (−4.17, 2.8, −0.8), `Point Light (2)` 세기 2·범위 3·위치 (−4.17, 5.4, −0.5), `Lighting/Reflection Probe` 세기 0.3 |
| `Assets/0.Scenes/MainFlow/1.TitleScene/LightingData.asset`, `ReflectionProbe-0.exr` | 라이팅 재베이크(`Tools/Title/Authoring/타이틀 라이팅 베이크`) |
| `Assets/0.Scenes/Art/title/GlobalVolumeProfile.asset` | Depth Of Field: 초점 20 m · F4 · 65 mm (아트 씬 `title.unity` 도 같은 프로필) |

- 반사 프로브 세기 0.3: 재베이크한 프로브가 조명 켜진 방을 밝게 찍어 광택 소품이 회색으로 떠서 낮췄다. 프로브를 다시 구우면 다시 확인할 것.
- `TitleRigAuthoring` 의 상수(`FarPos`·`NearPos`·`CamEuler`)는 옛 구도다. 재실행하면 이번 구도가 덮인다.
  - ⚠️ 정정(10-26 경석 Claude): `TitleRigAuthoring` 는 삭제했다 — 타이틀 메뉴·모니터 UI 카메라(`TitleUICam`)·`VCam_Closeup` 이 없어져 다시 돌리면 옛 리그가 되살아난다. Press 캡슐만 `Tools/Title/Authoring/Press 캡슐 배치 (멱등)`. 계획 [PLAN-title-start-settings-tabs.md](../../PLAN-title-start-settings-tabs.md).

## 알려진 문제 (아트 쪽에서는 못 고침)

1. **Play 뒤 에디트 모드에서 카메라 안티에일리어싱이 바뀐다.** 에디터에서 Play 를 하고 끈 뒤 타이틀 씬을 저장하면 Main Camera·TitleUICam·TitlePressCam 의 Anti-aliasing 이 None → SMAA 로 저장된다(`GraphicsQualityConfig` 카메라 렌더 훅이 Play 종료 뒤에도 남음, 스크립트 재컴파일 시 사라짐). 이번 커밋은 None 으로 되돌린 상태.
   - ⚠️ 정정(10-26): `TitleUICam` 은 삭제됐다 — 대상 카메라는 Main Camera·TitlePressCam 2대. 10-26 저장분은 둘 다 None 확인.
2. **중앙 모니터 화면이 깊이 텍스처에 안 써진다.** DoF 가 화면 픽셀을 뒤 벽 깊이(메뉴 구도에서 약 17~20 m)로 계산해, 초점을 실제 화면 거리(약 5 m)에 맞추면 메뉴 글씨가 흐려진다. 이번 DoF 초점 20 m 는 **이 상태에 맞춘 값**이다 — 화면이 깊이에 써지게 바뀌면 DoF 값을 다시 정해야 한다.

## 확인

- 0.BootStrapScene 에서 Play: 대기 → 3초 접근 → 메뉴(START/OPTION/EXIT 가독성) → 설정창 → START 다이브.
  - ⚠️ 정정(10-26): 메뉴·설정창·다이브는 없어졌다. 지금 = Press 캡슐 → 아무 키 → 3초 접근 → 모니터 CRT 꺼짐 → 검정 페이드 → 로비(접근 중 ESC = 바로 페이드).
