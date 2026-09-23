# PLAN-title-flow.md — 타이틀 연출 (PRESS ANY KEY → 카메라 인 → 모니터 안 메뉴)

> 상태: **승인 대기 (2판)** · 2026-09-21
> 작업자: 경석(Claude) · 브랜치 `feature/Boss23`
> 레퍼런스: VolFx 트레일러 (에셋 구매 없음. 연출 분석 결과는 [PLAN-crt-fx.md](PLAN-crt-fx.md))
> 1판에서 **Codex 교차검증으로 5건이 뒤집혔다.** 뒤집힌 항목은 §9에 남긴다.

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
