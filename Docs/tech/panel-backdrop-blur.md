# 패널 배경 블러 (할 일 201)

패널(모달)이 열려 있는 동안 **패널만 선명하고 그 뒤는 HUD 까지 전부 실시간으로 흐려진다.**

## 1. 구조

| 조각 | 파일 | 하는 일 |
|---|---|---|
| 공용 진입점 | `Assets/1.Scripts/Rendering/PanelBackdropBlur/PanelBackdropBlur.cs` | `Acquire(owner, panelCanvas)` / `Release(owner)`. 참조 카운트라 패널이 겹쳐도 안전. 세기 `Amount` 0↔1 을 `fadeSeconds`(0.15초, unscaled) 동안 보간. HUD 캔버스 전환(§2). |
| 순수 규칙 | 같은 파일 `PanelBackdropBlurState` | 참조 카운트·가장 위 패널 순서·보간·캔버스 전환 판정. EditMode `PanelBackdropBlurTests`. |
| 패널 컴포넌트 | `Assets/1.Scripts/UI/PanelBackdropBlurTarget.cs` | 패널 콘텐츠 루트에 하나. OnEnable 잡기 / OnDisable 놓기(`UiModalBlocker` 와 같은 수명). |
| 렌더 피처 | `PanelBackdropBlurFeature.cs` + `Shaders/PanelBackdropBlur.shader` | `PC_Renderer` 에만. BeforeRenderingPostProcessing 에서 카메라 컬러 전체를 축소·가로·세로 블러(반복) → 원본과 `Amount` 로 섞고 채도·어둡게도 같은 세기로. `Amount` 0 이면 패스를 넣지 않음(비용 0). Game 카메라만. |
| 설정 SO | `Assets/9.ScriptableObject/UI/PanelBackdropBlurSettings.asset` | 세기 1.42 · 축소 1/4 · 반복 3 · 채도 0.6 · 어둡게 0.25 · 보간 0.15초(옛 특성 창 값 그대로). 피처 칸 `_settings` 로 물려 있다. 데이터 테이블 시트 없음(연출 기술 값). |

붙은 패널: `GameMenu.prefab` `Content`(일시정지·설정·종료 확인) · `LobbyResultPanel.prefab` `Content` · `TraitUI.prefab` `Window`(특성 선택 — 2차 확인창·초기화 팝업은 같은 캔버스라 함께 선명).
저작 메뉴(`GameMenuAuthoring`·`LobbyResultPanelAuthoring`·`TraitUiAuthoring` 전체 생성)도 같이 붙인다.
**제외**: 캐릭터 선택 패널, 데모 연결 대기 화면, 로딩, 튜토리얼 안내·로비 대사, 스킬 툴팁, 디버그 맵 개요(M).

MaskBlur(할 일 203, 맵 비네팅)와 피처·셰이더·설정·정적 상태가 모두 따로다 — 같은 프레임에 함께 돌아도 서로 덮지 않는다.

## 2. HUD 까지 흐리는 방법

Screen Space Overlay UI 는 후처리 뒤에 그려져 렌더 피처가 못 건드린다. 그래서 **블러가 0 보다 큰 동안만**:

- 열린 패널 중 가장 위 패널의 루트 캔버스 `sortingOrder` 보다 **작은** Overlay 루트 캔버스(HUD 0·머리 위 체력바 -10·미니맵 40·피해 숫자 50·튜토리얼 안내 50·컷신 자막 440 등)를
  Screen Space Camera(메인 카메라, 평면 거리 = near clip + 0.05)로 바꾼다 → 투명 패스에서 월드와 같이 그려져 함께 흐려진다.
- 패널 자신과 그보다 위 캔버스(간파 암전 HUD 덮개·섬광 `short.MaxValue` 근처, 데모 연결 30000, 성능 표시 등)는 Overlay 그대로 → 선명.
- 위 패널이 닫히면(예: 결과창 200 위 ESC 메뉴 900 → 닫힘) 다시 판정해 아래 패널 캔버스를 되돌린다.
- 열린 동안 0.5초마다 새 캔버스를 다시 찾는다(피해 숫자 등). 닫히는 보간이 0 이 되면 원래 값(모드·카메라·평면 거리)으로 한 번에 되돌린다.
- HUD 배치 코드(체력바·피해 숫자·상호작용 표시)는 `anchoredPosition = 화면 좌표 / scaleFactor` 방식이라 카메라 모드에서도 같은 자리.

## 3. 알려진 위험·대응

1. **HUD 색 튐** — 카메라 모드인 동안 HUD 에도 후처리(톤매핑 Neutral·블룸·CRT)가 걸린다. 블러·어둡게가 같이 올라가서 대부분 가려지지만, 여는 첫 순간 HUD 색이 살짝 바뀔 수 있다. 크면 `fadeSeconds` 를 줄이거나 어둡게를 올려 가린다.
2. **가까운 물체 가림** — 카메라 모드 UI 는 깊이 판정을 받는다. 평면을 near clip 바로 앞(+0.05)에 둬 탑다운 카메라에선 가릴 물체가 없다.
3. **간파 암전 HUD 덮개** — 덮개·섬광 캔버스는 패널보다 위(최상위)라 전환 대상이 아니다(기존 그대로). 패널이 열린 중 간파 연출이 겹치면 카메라 모드 HUD 는 월드와 같이 DarkAdaptation 패스를 받는다(흐린 배경 일부라 의도와 같음).
4. 렌더러에 피처가 없으면(`Mobile_Renderer`) `PanelBackdropBlur.IsAvailable` = false → 블러·캔버스 전환 없이 패널만 열린다(특성 창은 어둡게 막 0.55 로).

## 4. 비용

열린 동안 매 프레임 1/4 해상도 가로·세로 블러 × 반복 3 + 풀 해상도 합성·복사 1회(추정 0.2~0.5ms, 미실측). 캔버스 전환 순간 캔버스 다시 그리기 1회. 닫혀 있으면 0.
