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
  - ⚠️ 정정(11-04, 할 일 218): Press 캡슐은 없어지고 문구(`Text`)만 캔버스 바로 아래에 있다. 대기 중 문구 밖 화면은 블러 + 흑백, 아무 키 → 문구 꺼짐 + 1초 선형 원복 → 3초 접근 … (`TitleFlowDirector` 「Press 대기 블러·흑백 (218)」 칸, 블러 경로는 [panel-backdrop-blur.md §5](panel-backdrop-blur.md)).
  - 11-05(218 후속): 대기 블러·흑백 위에 TV 글리치(가로 찢김·RGB 어긋남·주사선·잡음, 상시 은은 + 가끔 강한 버스트)가 얹힌다. 문구는 선명, 1초 원복 때 블러와 같이 0. 칸 = `TitleFlowDirector` 「Press 대기 TV 글리치 (218 후속)」, 경로 [panel-backdrop-blur.md §5.1](panel-backdrop-blur.md).

## 벽 모니터 존 투어 영상 (11-09, 할 일 254)

- **어디**: `TitleScreenFeed`(1.TitleScene) 「존 투어 영상 (할 일 254)」 칸. 원래 이미지 모드(`_liveFeed` 꺼짐)에서만 동작.
  - `_videoScreens` = 영상 모니터 화면 Renderer — 기본 **TitleOffice 의 `monitor (4)`·`monitor (8)`·`monitor (11)` 의 `monitor_screen`**(게임오브젝트 이름 번호).
    `_wallScreens` 배열 번호가 아니다 — 그 배열은 10-06 오피스 재배치로 13칸 중 5칸이 비었고(`monitor (4)` 는 씬에서 새로 만든 모니터라 배열 밖), 원소 번호로는 4·8·11 을 셀 수 없다.
    대략 위치(TitleOffice 기준): (4) 왼쪽 위 1.8배 · (8) 가운데 맨 뒤 아래쪽 2.83배(가장 큼) · (11) 오른쪽 아래.
  - `_videoClips` = `Assets/50.Art/Video/Title/01~05_*_Zone_Tour.mp4`(SVN r641, 원래 r639 `Lobby/` 에 올라온 것을 옮김).
- **겹침 없음**: `TitleWallVideoPlaylist`(순수 함수, `TitleWallVideoPlaylistTests`) — 시작은 무작위 시작점부터 연속 3개, 끝나면 그 다음 번호부터 순환하며 다른 모니터가 지금 틀지 않는 첫 영상.
- **룩**: 모니터마다 벽 머티리얼 인스턴스 1장(`_MainTex` = 영상 RT 640×360). `_Roll` 0(세로 흐름·동기 띠 없음)·`_SeamBlend` 0.05, 지직(`_WallGlitch`)은 평소 빈도 그대로, CRT(주사선·색수차·비네트)는 다른 벽 모니터와 같다. `TitleCrtFx` 에 등록.
  화면 비율이 16:9 가 아니라 늘어나 보이면 `_videoUv`(배율 xy·오프셋 zw)로 가운데 자르기.
- **임포트**: `TitleWallVideoImport`(AssetPostprocessor) 가 `Video/Title/` 영상을 **절반 해상도 640×360 트랜스코드·소리 끔**으로 강제한다(.meta 는 SVN 이라 손으로 안 박음). 설정을 바꾸면 `Version` 을 올린다.
- **정리**: 씬을 떠나면(OnDisable) VideoPlayer 오브젝트·RT·머티리얼을 지우고 원래 머티리얼로 되돌린다. 중앙 모니터 CRT 꺼짐·페이드 흐름은 건드리지 않는다.
- **빌드 포함**: 영상은 씬 참조로만 들어간다(Addressables·Resources 아님).
