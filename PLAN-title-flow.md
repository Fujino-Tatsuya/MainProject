# PLAN-title-flow.md — 타이틀 연출 (PRESS ANY KEY → 카메라 인 → 모니터 안 메뉴)

> 상태: **승인 대기 (3판 — §0 전면 교체)** · 2026-09-23 (2판 2026-09-21)
> 작업자: 경석(Claude) · 브랜치 `feature/Boss23`
> 레퍼런스: VolFx 트레일러 (에셋 구매 없음. 연출 분석 결과는 [PLAN-crt-fx.md](PLAN-crt-fx.md))
> 1판에서 **Codex 교차검증으로 5건이 뒤집혔다.** 뒤집힌 항목은 §9에 남긴다.

## 0. 3판 (2026-09-23) — 🔴 **먼저 읽을 것.** §1~§9 와 충돌하면 이 절이 이긴다

> 같은 날 1차 3판 초안("설정 = 오버레이, PRESS ANY KEY = 모니터")은 **팀장 정정으로 폐기**했다. 아래가 최신.
> Codex 설계 검토 2회(09-23) 반영. 🔴 구현 전 **승인 대기**.

### 0.1 왜 바뀌나
아트 오피스를 `TitleOffice.prefab` 인스턴스로 이식(09-23, `Art/title.unity` 무수정)하니 **PRESS ANY KEY·메뉴가 안 보인다.**
월드 캔버스(`CRT_Anchor` z −3.50)가 중앙 화면 메시(피벗 z −3.16, 카메라는 +z) 뒤라 가린 것으로 **추정**(피벗 ≠ 표면, Codex 지적).
흐름 자체(Director 상태머신 · Start → `TitleSceneManager.StartGame` → 페이드 → `GoToLobby`)는 있다.

### 0.2 팀장 확정 요구
| # | 요구 |
|---|---|
| 1 | **PRESS ANY KEY 는 화면 앞 오버레이 UI 텍스트.** Idle 동안 중앙 모니터엔 원래 **Re:C 로고** |
| 2 | 입력 → **Re:C 로고 끔** → 카메라 Far→Near → **중앙 모니터 안에 Start / Setting / Exit** |
| 3 | **Setting 도 모니터 안**, Closeup 유지. 설정창은 모니터에 맞게 **레이아웃 조정 허용** |
| 4 | **Start**: 레퍼런스 0:02~0:04 의 **Flow**(흰 코어 + 마젠타/시안 프린지 혜성 트레일, ~2초 감쇠) — **Start 클릭 때만** |
| 5 | 이어서 0:23~0:25 의 **꺼짐** — 🔴 **화면 전체**: 가로 찢김 1회 → 둥근 배럴 CRT 로 수축(파란 발광) → 가로 띠 → 암전 → **씬 전환** |
| 6 | **Exit 도 같은 꺼짐 연출** 후 종료 |
| 7 | **벽 모니터 13대 = 지금 카메라 화면을 다시 비춘다**(화면 속 화면 반복) |
| 8 | 타이틀 **RetroCRT 끔** — ✅ 완료(`_effectEnabled: 0`, 씬 값만) |
| 9 | **스카이박스가 비치지 않게** — 의도한 오피스 룩 |

### 0.3 구조
```
[Overlay Canvas]  PRESS ANY KEY(BlinkingText) · Fade_Image · 스킵 안내
[TitleUICam → UI_RT]  Screen Space-Camera 캔버스 1개
      ├ MenuRoot     로고 + Start / Setting / Exit
      └ SettingsRoot 기존 Option_Panel(레이아웃 재조정)
[Main Camera]  오피스 + 중앙 모니터(UI_RT 표시) + 벽 13대(피드 RT 표시)
      └ 전체 화면 꺼짐 = 풀스크린 패스(TitleScreenOff), 타이틀 전용 게이트
```

### 0.4 접근
**A. 중앙 모니터 = UI 렌더텍스처**
- `TitleUICam`: 독립 **Base** 카메라, 직교, **전용 `TitleUI` 레이어**(하위까지), `targetTexture = UI_RT`. Main 은 `TitleUI` 제외.
  AudioListener·CinemachineBrain·PP·그림자 없음. Main 보다 먼저 렌더.
- 새 셰이더 `SG_TitleMonitorScreen`(원본 `shader_monitor_screen` 은 로고를 **노드 내장 텍스처**로 박아 둬 바깥에서 못 바꿈):
  `_LogoTex` · `_UITex` · `_FlowTex` · `_ScreenState`(Logo / Black / UI) · `_Brightness`. 기존 그래프의 Lit 구성을 복제해 **Idle 로고 룩 유지**.
- 중앙 렌더러는 **이름 검색이 아니라 명시 참조**(벽 모니터와 같은 이름). 런타임 인스턴스·RT 는 타이틀 종료 때 해제.

**B. 클릭 = 화면 UV → UI 좌표**
- `TitleScreenRaycaster : GraphicRaycaster` — Main 레이 → 중앙 MeshCollider(런타임 추가) → `textureCoord` → UI_RT 픽셀.
  🔴 `eventCamera` 는 **TitleUICam**. 원본 `eventData.position` 은 **보존**(오염 금지). 화면 밖은 hit 없음(Clamp 금지).
- 🔴 **슬라이더**: uGUI Slider 는 드래그 때 `eventData.position` 을 다시 읽는다 → `TitleScreenSlider : Slider` 가 OnPointerDown/OnDrag 동안만 좌표 변환 후 복원.
  `VolumeSlider` 는 `GetComponent<Slider>()` 라 호환. Handle/Fill/Navigation 참조 보존.
- 셰이더에 왜곡을 넣으면 클릭에도 **같은 UV 함수** 적용. 키보드·패드 네비게이션은 이 경로와 무관.
- EventSystem 추가 금지(`PersistentEventSystem` 이 단일화).

**C. Flow (Start 클릭 때만)**
- **Flow 전용 저해상도 핑퐁 RT 2장**(512급, HDR)에만 누적: `Next = Prev(warp(uv)) · exp(−k·dt) + Comet(uv, t)`. UI 는 누적하지 않는다(잔상·과노출 방지).
- 혜성 소스는 셰이더 내부 광점 경로 — 별도 카메라 없음. 채널별 샘플 오프셋으로 마젠타/시안 프린지. 시간은 unscaled.
- 구현은 RenderGraph 외부 RTHandle import 1패스(두 텍스처 read 의존 선언). CustomRenderTexture 도 후보(갱신 시점·복사 비용 확인).

**D. 전체 화면 꺼짐 (Start·Exit 공통)**
- 풀스크린 패스 `TitleScreenOff`(타이틀 전용 static 게이트 — RetroCRT 처럼, 타 씬 영향 없음). 파라미터 `_Tear`·`_Shrink`·`_Band`·`_Glow`.
- 단계: 찢김 1회 → XY 수축 + 둥근 모서리·배럴 재매핑(영역 밖 검정 마스크) → 가로 띠 → **발광까지 0**. 0 나누기 방지.
- 파란 발광은 마스크 기반 글로우로 자체 처리(🔴 Main Camera PP 가 **꺼져 있어** Bloom 에 기대지 않는다).
- 시퀀스는 Director 가 소유: `Starting/Exiting` → 조작 잠금·선택 해제 → (Start 만) Flow → 꺼짐 → **완전 암전 후** `StartGame()`/`Quit()` **1회**.
  기존 `StartGame` 의 페이드는 이미 검은 화면 위라 중복 무해 — 필요 시 즉시 모드.

**E. 벽 모니터 13대 피드** → §0.5 (Codex 2차 반영)

**F. 설정창 in 모니터**
- `SettingsRoot` 는 공유 RT 캔버스 **자식 루트**만 토글(현재 `_settingsRoot` 가 캔버스 자체라 공유 캔버스가 꺼진다 — Codex).
- 가독성: 모니터 크기로 줄이면 18pt → 약 10px · 슬라이더 높이 약 11px(Codex 계산). **기준: 최저 지원 해상도에서 글자 14px 이상** + 슬라이더 판정 영역 확대.
  CanvasScaler reference/match 명시, 필요한 만큼 라벨·탭·슬라이더 재배치.

**G. 스카이박스 반사 제거**
- 원인: 오피스의 **베이크 반사 프로브 1개**와 APV 가 아트 씬 `LightingData.asset` 에만 구워져 있다. 타이틀 씬은 미베이크 →
  프로브가 비고 **Unity 기본 스카이박스 환경 반사**로 떨어진다.
- 해결 1순위: **타이틀 씬에서 라이팅 베이크**(프로브 + APV) — 아트 룩 재현. `1.TitleScene/` 라이팅 폴더가 git 에 생긴다.
- 대안: 환경 반사 소스를 검정/커스텀 큐브맵(굽기 없이 즉시).

**H. 저작 스크립트 동기화** — `TitleRigAuthoring` 이 설정·PRESS ANY KEY 를 월드에 만든다 → 새 구조로 갱신하지 않으면 재실행 시 옛 구조로 돌아간다(Codex).

### 0.5 벽 모니터 피드 (Codex 2차 검토 반영)
- **복사 방식**(두 번째 카메라 없음): `TitleScreenFeedFeature` 가 **`AfterRenderingPostProcessing`** 에서 `activeColorTexture` → 외부 RTHandle 로 `AddBlitPass`.
  🔴 `AfterRendering` 은 FinalBlit 뒤라 백버퍼일 수 있고, **Overlay UI 는 어느 시점에도 안 들어간다**(SDR 순서: …→FinalBlit→AfterRendering→Overlay UI).
- **RT 2장 핑퐁**(480×270, 깊이 없음, Bilinear/Clamp): 프레임 N 은 A 를 읽고 B 에 쓴다 → 다음 프레임 교대. 한 프레임 지연 재귀. 첫 사용 전 검정 초기화.
- 게이트: `TitleScreenFeedController.ActiveController` 활성 **+ 현재 카메라 == 지정된 타이틀 Main** + Game/Base + `targetTexture == null`. 공유 `PC_Renderer` 라 정확한 카메라 비교가 필수.
- 🔴 기존 glitch/non 그래프는 `m_Properties: []` — 텍스처가 **노드 슬롯에 내장(modifiable=false)** 이라 `SetTexture` 로 교체 불가.
  → 새 `SG_TitleWallFeed`(URP Unlit/Opaque): `_FeedTex` · `_FeedGain` · `_Inset` · `_ChromaticPx` · `_GlitchStrength`. **런타임 머티리얼 2개**(glitch 계열 5대 / non 8대 공유), 대상은 원본 머티리얼 참조로 판별(중앙 제외).
- 백화 방지 초기값(추정): `saturate(feed)×0.85` · 여백 2%(축소 배치 + 바깥 검정, UV 자르기 아님) · non 크로마틱 0 / glitch 0.35 texel.
  🔴 원본 non 그래프는 Emission 이 원본의 1.5배 — 그 계수를 피드에 쓰면 백화. 2단계 반복이 안 읽히면 960×540 또는 구도부터.
- 포함 범위: **중앙 모니터 메뉴 포함 / PRESS ANY KEY·Fade 제외**(Overlay 는 구조상 안 들어감 — 권장과 일치).
- 끄는 조건: Start/Exit 확정 후 마지막 RT 유지하고 복사만 중단 · 씬 이탈 시 머티리얼 복원 + RT 해제.
- 비용: RGBA8 2장 ≈ 1 MiB, 장면 추가 렌더 없음. Frame Debugger 로 Opaque Texture 중복 확인.
- ⚠️ 전체 화면 꺼짐 패스(D)는 피드 복사 **뒤**에 둔다 — 꺼지는 화면이 벽 모니터에 한 프레임 비치는 건 무해하지만 순서를 고정해 둔다.

### 0.6 구현 순서 (단계별 완료 기준)
| 단계 | 작업 | 완료 기준 |
|---|---|---|
| 1 | 중앙 화면 식별 · UV 격자 RT 실측 · 스카이박스(베이크) | 격자 방향·crop 확인, 벽 13대 불변, 하늘 반사 없음 |
| 2 | UI RT + 레이캐스터 + **슬라이더 어댑터** | 버튼 hover·클릭, 슬라이더 드래그·화면 밖 release, 키보드·패드 |
| 3 | 설정창 이전·가독성 | 전 탭·볼륨·닫기/ESC, 글자 ≥ 14px |
| 4 | Idle → 로고 끔 → 카메라 → 메뉴 연결 · PRESS ANY KEY 오버레이 | 접근 스킵·중복 입력 정상 |
| 5 | 벽 모니터 피드 | 반복이 읽힘, 백화 없음, 비용 측정 |
| 6 | Flow 단독 | UI 누적 없음, 30/60/144fps 지속시간 일치 |
| 7 | 전체 화면 꺼짐 + 시퀀스 | 찢김·수축·띠·발광 0, 로비 1회 / Exit 종료 |
| 8 | 회귀 · 저작 도구 동기화 | 재진입·RT 누수 없음·맵 RetroCRT 유지, 도구 재실행 무중복 |

### 0.8 ⚠️ 뒤집음 (팀장 09-23 Play 확인 후)
- **Setting 은 줌 없음** — §0.2-3 "Closeup 유지" 철회. 메뉴가 보이던 Near 뷰 그대로 모니터 내용만 설정창으로 바뀐다(설정창은 모니터 폭에 맞춰 0.78배).
- **Flow 제거** — §0.2-4 철회. 무지개빛 혜성 회전이 의도와 달랐다 → Start 도 Exit 처럼 **바로 모니터 꺼짐**. `TitleFlowFx`·`TitleFlowUpdate.shader` 삭제.
- 꺼짐 캡처는 DX12 에서 **위아래 뒤집혀** 들어온다(`TitlePowerOff._flipY = true`, 팀장 확인).
- ✅ 팀장 확인: PRESS ANY KEY 룩 · 모니터 지직거림 · 설정 닫을 때 스크램블 복원 · Start/Exit 꺼짐.

### 0.7 확정 (팀장 09-23)
- 기준 해상도 **1920×1080** — 설정창 가독성 기준(글자 ≥ 14px)은 이 해상도에서 판정
- 꺼짐 마지막 **점 없음** — 가로 띠 → 암전
- 스킵: 카메라 접근은 ESC 스킵 **허용**, Start/Exit 연출은 **스킵 불가**(입력 잠금)

## 1. 목표

`1.TitleScene` 을 평면 UI 에서 **3D 오피스 씬 + 중앙 CRT 안의 메뉴**로 바꾼다.

```
PRESS ANY KEY 깜빡임 (멀리서)
  → 아무 입력 → 카메라가 중앙 CRT 앞으로 (ESC 로 스킵)
  → CRT 안에 로고 + Start / Setting / Exit
  → Setting = 한 단계 더 클로즈업 + CRT 안에 설정창
  → Start = 페이드 → 3.LobbyScene / Exit = 페이드 → 종료
```

## 2. 현재 이해 (레포에서 직접 확인한 사실)

| 항목 | 사실 |
|---|---|
| 진짜 타이틀 | `0.Scenes/MainFlow/1.TitleScene.unity` (빌드세팅 idx 1) |
| 아트 씬 | `0.Scenes/Art/title.unity` — 🔴 **스크립트 12개.** `UnityEngine.Rendering.FreeCamera` 가 **활성** |
| 🔴 FOV | 아트 씬 **26.991467** vs 기존 타이틀 **60**. 좌표만 베끼면 화면 점유율이 재현되지 않는다 |
| 기존 기능 | `TitleSceneManager` 가 Start/Option/Exit·페이드·`GoToLobby`·`Quit`·ESC 토글을 이미 구현 |
| 버튼 결선 | 인스펙터 참조가 **이미 직렬화돼 있다**(`1.TitleScene.unity:2545`). 이름 검색은 **참조가 null 일 때만** 돈다 |
| 🔴 영구 콜백 | 씬에 `OpenOption`(:1450) · `CloseOption`(:1318) · `StartGame`(:3461) UnityEvent 가 박혀 있다 |
| EventSystem | `1.TitleScene` 에 **없다**. BootStrap·Lobby 에 있고 `UI/PersistentEventSystem.cs` 가 런타임 단일화 |
| 설정 UI | `Canvas → Option_Panel → Option_Panel_Background → 탭·콘텐츠·닫기`. 내부는 **고정 좌표** |
| 설정 UI 치수 | 볼륨 라벨 **18pt**(오토사이즈 off) · 탭 24pt · 닫기 56×56 · 슬라이더 500×20 |
| 🔴 마스크 | `Option_Panel` · 배경에 **Mask/RectMask2D 가 없다.** 모니터 경계 밖으로 삐져나가도 안 잘린다 |
| Fade | 🔴 `Fade_Image` 가 **메뉴와 같은 Canvas 자식**. 자동 검색 키는 `Black_Image` 라 유실되면 복구 못 한다 |
| 중앙 CRT | `monitor/monitor_screen` → `MA_monitor_screen` (이 1대 전용) |
| 벽 모니터 | 13대가 `glitch`(5) / `non`(8) **2장을 공유** → 개별 제어 불가 |
| 🔴 CRT warp | `CyaniluxRetroCRT.mat` 에 `CRT_WARP_ON` + `_CRTWarpStrength 0.035`. **화면이 휜다** |
| 패키지 | Cinemachine 3.1.6 · Timeline 1.8.12 · Input System 1.19.0 |

## 3. 접근

### 3.1 상태 머신 — `TitleFlowDirector` (신규)

| 상태 | 카메라 | 월드 캔버스 | 입력 |
|---|---|---|---|
| `Idle` | `VCam_Far` | PRESS ANY KEY 만 | 아무 키/클릭/패드 → `Approaching` |
| `Approaching` | Far → Near 블렌드 | 전부 숨김 | 🔴 전면 차단. ESC = 스킵 |
| `Menu` | `VCam_Near` | 로고 + 버튼 3개 | 마우스 · 방향키+엔터 · 패드 |
| `Settings` | `VCam_Closeup` | 설정창 | 닫기/ESC/패드 Cancel → `Menu` |
| `Starting` / `Exiting` | 고정 | 잠금 | 없음 |

🔴 **`TitleSceneManager` 무수정은 철회한다.** 두 가지 이유로 성립하지 않는다:

1. `TitleSceneManager.Update` 의 ESC 토글은 `Idle`/`Approaching` 을 모른다. `IsTransitioning` 은
   **카메라 상태가 아니라 페이드 잠금**이다 → ESC 스킵과 동시에 옵션창이 열린다.
2. 씬에 **영구 UnityEvent 콜백**이 박혀 있어, Director 콜백을 덧붙이면 기존 경로가 상태 검사를 우회한다.

→ **모든 진입점을 Director 로 통합한다.** `TitleSceneManager` 의 ESC 처리는 제거하고(또는 Director 위임),
씬의 영구 콜백은 Director 메서드로 재지정한다. **버튼 인스턴스와 직렬화 참조는 그대로 유지**한다.

### 3.2 카메라 — Cinemachine vcam 3대

| vcam | 위치 | 회전 | **FOV** | 용도 |
|---|---|---|---|---|
| `VCam_Far` | `(0, 4.26, 13.8)` | `(4.9, 180, 0)` | **26.99** | 시작 |
| `VCam_Near` | `(0, 3.22, 3.47)` | `(4.9, 180, 0)` | **26.99** | 메뉴 |
| `VCam_Closeup` | 실측 | `(4.9, 180, 0)` | 실측 | 설정 |

🔴 **FOV 를 vcam 마다 명시한다.** 기존 타이틀 카메라는 60 이라 좌표만 옮기면 구도가 달라진다.
아트 씬의 `Camera` + `FreeCamera` 는 **이식하지 않는다** — Cinemachine Brain 과 Transform 제어가 충돌한다.

**ESC 스킵** = `brain.ActiveBlend = null` (진행 중 블렌드를 즉시 완료시킨다).
`DefaultBlend` 를 Cut 으로 바꾸는 방식은 **진행 중 블렌드에 소급 적용되지 않는다.**
Brain 이 아직 전환을 만들지 않은 **첫 프레임의 ESC 는 보류**하고, 실제 카메라 갱신 후 메뉴 입력을 연다.

### 3.3 Canvas 3층 구조

1. **Overlay Canvas** (신규 · 전체화면) — `Fade_Image` + 우측 하단 ESC 스킵 안내
   🔴 페이드를 월드로 옮기면 **모니터 영역만 암전**된다. 반드시 Overlay 로 분리하고
   `blackImage` 는 **명시 참조**로 꽂는다(자동 검색 키가 `Black_Image` 라 `Fade_Image` 를 못 찾는다).
2. **World Canvas — Menu** (신규) — 로고 + Start/Option/Exit + PRESS ANY KEY
3. **World Canvas — Settings** (신규) — `Option_Panel` 이하

월드 캔버스 각각에 **`GraphicRaycaster` 를 새로 붙인다**(기존 것은 기존 Canvas 소유).
`worldCamera` 에는 vcam 이 아니라 **Brain 이 구동하는 실제 Camera** 를 지정한다.
(미지정이어도 `Camera.main` 폴백은 있으나 명시한다.) `TrackedDeviceRaycaster` 는 **불필요** —
일반 마우스는 InputModule 의 일반 RaycastAll 경로를 탄다.

RenderTexture 방식은 채택하지 않는다 — 클릭 판정을 UV 로 역산해야 하고 곡면에서 어긋난다.

### 3.4 설정창 — 논리 해상도 유지 + 루트 균일 축소

🔴 **기존 Canvas 의 렌더 모드만 바꾸는 방식은 쓰지 않는다.** World Space 에서는
`CanvasScaler` 의 `Scale With Screen Size` 가 안 돌고 `DynamicPixelsPerUnit` 분기로 빠지며,
`Option_Panel` 내부는 고정 좌표(`Gameplay_Tab (-359,184) 300×100`, `Audio_Content (161,-36) 710×540`)라
그대로 옮기면 수백 단위로 튀어나간다.

**대신:**
- World Canvas 의 RectTransform 을 **1920×1080 논리 크기**로 잡고, **루트 Transform 만 균일 축소**해
  CRT 면에 맞춘다 → 내부 고정 좌표가 전부 보존된다
- `Option_Panel` 루트에 **`RectMask2D` 를 새로 추가**한다 (지금은 마스크가 없어 안 잘린다)
- CRT 는 4:3, UI 는 16:9 → **레터박스 여백 규약을 먼저 정한다**
- 진입 시 `VCam_Closeup` 으로 CRT 를 화면에 꽉 채워 **화면상 글자 크기**를 확보한다

🔴 **폴백 조건 (이걸 넘기면 Overlay 로 되돌린다)** — 아래 중 하나라도 실패하면 설정창만 기존 Overlay 로 간다:
- 클로즈업에서 18pt 볼륨 라벨의 **화면상 높이가 14px 미만**
- CRT warp 를 켠 상태에서 슬라이더 양 끝 / 닫기 버튼의 **표시 위치와 클릭 판정이 어긋남**
- `RectMask2D` 로도 CRT 곡면 경계 밖 삐져나감이 안 잡힘

### 3.5 입력

- `Idle` 의 "아무 키" = `Keyboard.anyKey` + 마우스 좌클릭 + 패드 버튼. **마우스 이동만으로는 발동 안 함**
- `Approaching` 차단 = 월드 캔버스 **GameObject 비활성** (Raycaster 까지 확실히 죽는다)
- `Menu` 진입 시 `EventSystem.SetSelectedGameObject(startButton)`
- 🔴 BootStrap 의 EventSystem 이 **`m_DeselectOnBackgroundClick: 1`** — 빈 곳을 클릭하면 선택이 풀려
  그 뒤 방향키·패드가 먹통이 된다 → **Director 가 선택 복구를 책임진다**
- 🔴 `Button` 은 `ICancelHandler` 를 구현하지 않는다. **패드 Cancel 은 Director 가 직접 처리**한다
- 버튼 Navigation = Explicit 로 Start↔Setting↔Exit 연결

### 3.6 RetroCRT

타이틀에서 **켠 채로 간다**(아트 의도). `RetroCRTController` 를 `1.TitleScene` 에 배치.

**수명**: Title 소유 Controller 이므로 `GoToLobby` 의 Single 로드로 **씬과 함께 자동 해제된다.**
(`OnDisable` 이 `s_active` 를 비우고, Feature 는 `ActiveController == null` 이면 즉시 리턴)
문제가 되는 건 `DontDestroyOnLoad` 로 살리거나 Additive 로 유지하는 경우뿐이다.

명시적 종료가 필요하면 `StartGame()` 의 `FadeThenInvoke` 콜백에서
**완전 암전 후 Controller 의 `enabled = false` → `GoToLobby()`** 순서로 한다.
🔴 `EffectEnabled = false` 로는 **부족하다** — Cyanilux 패스만 꺼지고 픽셀·스캔라인 패스는 별도 조건으로 남는다.
🔴 **공유 Renderer Feature 자체는 끄지 않는다** (다른 씬이 같이 죽는다 — lessons #16).

## 4. 확정된 결정

| # | 결정 | 근거 |
|---|---|---|
| 1 | 아트 오브젝트를 `1.TitleScene` 으로 이식. `Art/title.unity` 는 **백업 보존** | 빌드세팅·GameManager 무수정 |
| 2 | PRESS ANY KEY 는 **중앙 1대만** | 벽 13대는 머티리얼 공유 + 시선 집중 |
| 3 | 카메라 = **Cinemachine vcam 3대** | 패키지 보유 |
| 4 | 스킵 허용, ESC 즉시 이동. 안내는 **우측 하단** | 시선 경로에서 가장 먼 모서리 |
| 5 | 설정도 **모니터 안**. 단 §3.4 폴백 조건을 넘기면 Overlay 로 되돌린다 | 톤 우선, 가독성은 측정으로 판정 |
| 6 | 마우스 · 키보드 · 패드 전부 | |
| 7 | RetroCRT **켠 채로 최종 확인** | 아트 의도 |
| 8 | 연출 중 클릭은 아무 처리도 하지 않는다 | 의도한 동작이 아니다 |

## 5. 수정 예정 파일

| 파일 | 내용 |
|---|---|
| `0.Scenes/MainFlow/1.TitleScene.unity` | 아트 이식(Camera·FreeCamera 제외) · vcam 3대 · Canvas 3층 · RetroCRTController · 영구 콜백 재지정 · `RectMask2D` 추가 |
| `1.Scripts/UI/Title/TitleFlowDirector.cs` (신규) | 상태 머신 · 입력 · vcam · 선택 복구 · Cancel |
| `1.Scripts/UI/Title/BlinkingText.cs` (신규) | PRESS ANY KEY 깜빡임 |
| `1.Scripts/Managers/TitleSceneManager.cs` | 🔴 **수정** — ESC 처리 제거/위임 + Director 상태 가드 + CRT 해제 |
| `0.Scenes/Art/title.unity` | 🔴 **보존. 손대지 않는다** |

**의존**: CRT 파라미터 런타임 전달 경로 → [PLAN-crt-fx.md](PLAN-crt-fx.md)

## 6. 리스크

| # | 항목 | 대응 |
|---|---|---|
| **R1** | 🔴 **CRT warp(0.035) 와 클릭 좌표 불일치.** 보이는 위치와 눌리는 위치가 어긋난다 | **최우선 실측.** 중앙부만 쓰면 무시 가능할 수 있다. 실패 시 (a) UI 구간 warp 0 (b) 설정 Overlay (c) 커스텀 레이캐스터 |
| **R2** | 설정 UI 를 World 로 옮길 때 배치 붕괴 | §3.4 논리 크기 유지 + 루트 축소. 폴백 조건 명시 |
| **R3** | RetroCRT 픽셀/스캔라인이 작은 글자를 뭉갠다 | 클로즈업에서 켠 채 실측 |
| **R4** | 씬 병합 중 컴포넌트·참조 유실 | GUID 집합만으로 부족 — **컴포넌트 개수·소유 오브젝트·직렬화 참조·UnityEvent 대상**까지 비교 |
| **R5** | `1.TitleScene` 단독 재생 시 EventSystem 없음 | **BootStrap 부터 재생** |
| **R6** | CRT 곡면 가장자리 왜곡·가림 | safe-area 여백 |
| **R7** | 영상 효과 재현 범위 | [PLAN-crt-fx.md](PLAN-crt-fx.md) 에서 분리 관리 |

## 7. 범위 밖

- 벽 모니터 13대 개별 연출 (머티리얼 분리 필요)
- VolFx 에셋 구매·도입
- 설정 **항목** 확장(해상도·키바인딩) — 기존 4탭 그대로
- 로비 이후 · 네트워크 (타이틀은 싱글 로컬 구간)

## 8. 검증 (수용 기준)

**전부 `0.BootStrapScene` 부터 Play.** 🔴 육안 판정이 아니라 **상태값·호출 횟수**로 확인한다.

### 흐름
1. 진입 → 중앙 CRT 에 PRESS ANY KEY 깜빡임 + 우측 하단 ESC 안내
2. 아무 키/클릭/패드 → 카메라 이동 시작
3. 🔴 이동 **첫 / 중간 / 마지막 프레임**에서 각각 클릭·Submit·연타 → **액션 호출 횟수 0**
4. 이동 중 ESC → 즉시 Near 로 스냅. **옵션창이 열리지 않을 것**(기존 ESC 우회 확인)
5. `Menu` 진입 시 `EventSystem.currentSelectedGameObject == Start_Button`
6. 🔴 빈 곳 클릭 → 선택 해제 → **방향키가 다시 먹는지**(선택 복구)
7. 마우스 / 방향키+엔터 / 패드 **세 경로 전부** 로 Start·Setting·Exit 도달

### 설정
8. Setting → 클로즈업 완료 **후에** 입력이 열릴 것
9. 🔴 **CRT 켠 상태**에서 슬라이더 **양 끝**·닫기 버튼 **네 모서리**의 표시 위치 = 클릭 판정 위치
10. 18pt 볼륨 라벨의 **화면상 높이 ≥ 14px**
11. 패널이 CRT 경계 밖으로 삐져나가지 않을 것(`RectMask2D`)
12. 패드만으로 탭 이동 → 슬라이더 조작 → 닫기까지 도달
13. Settings ↔ Menu **3회 왕복** 후에도 선택 대상·카메라가 정상

### 이탈
14. Exit → 페이드 후 종료
15. Start → 페이드 후 `3.LobbyScene`
16. 🔴 로비에서 **`RetroCRTController.ActiveController == null`** 그리고 **두 패스 모두 미등록**
    (로비 UI 가 Overlay 라 "CRT 처럼 안 보인다"는 판정 근거가 못 된다)

## 9. 1판에서 뒤집힌 것 (기록)

| 1판 주장 | 실제 | 어디서 틀렸나 |
|---|---|---|
| "아트 씬 스크립트 0개" | **12개. `FreeCamera` 활성** | `Assembly-CSharp::` 만 grep 해 패키지 어셈블리를 통째로 놓쳤다 |
| "`TitleSceneManager` 무수정" | ESC 우회 + 영구 콜백 우회로 **성립 안 함** | `TitleSceneManager.cs:39` · 씬 UnityEvent 3곳 |
| "Event Camera 미지정이면 클릭 불가" | `Camera.main` **폴백 있음** | `GraphicRaycaster.cs:306` |
| "스킵 = DefaultBlend 를 Cut 으로" | **진행 중 블렌드에 소급 안 됨.** `ActiveBlend = null` 이 맞다 | `CameraBlendStack.cs:237,411` |
| "CRT 가 로비까지 따라간다" | 씬 언로드로 **자동 해제됨** | `RetroCRTController.cs:97` |
