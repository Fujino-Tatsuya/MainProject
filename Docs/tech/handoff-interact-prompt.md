# Handoff — 상호작용 F 키 프롬프트 (2026-10-09, 은희 → 경석)

상호작용할 수 있는 대상 위에 F 키 아이콘을 띄우는 **로컬 전용** 서비스를 추가했다. 첫 소비처가 존 다리 게이트 패널이라
경석 영역인 `ZoneBridgeGate.cs` · `ZoneBridgeGateManager.cs` · `ZoneL_typeB.prefab` 을 고쳤다(PR 없음 규칙 — 이 문서로 공유).

## 프롬프트 UI 위치·편집 (`ZoneL_typeB.prefab`)
F 아이콘 UI 는 **존 프리팹 안에 있다** — 코드로 Canvas 를 만들지 않는다.

```
ZoneL_typeB (ZoneBridgeGate — promptView · promptHeightOffset)
└─ InteractPromptCanvas   Canvas(Screen Space - Overlay, sortingOrder -5) + CanvasScaler(1920×1080, match height 1) + InteractPromptView
   └─ InteractKeyIcon     RectTransform 56×56(앵커 왼쪽 아래) + Image(F_KeyIcon, preserveAspect, raycast off) + CanvasGroup(alpha 0)
```

- 아이콘 이미지·크기·색: `InteractKeyIcon` 의 Image / RectTransform `sizeDelta` 를 프리팹에서 직접 고친다.
  크기는 Canvas 단위(세로 1080 기준 픽셀)라 카메라 거리와 무관하게 일정하다. 앵커는 왼쪽 아래로 둘 것(위치 계산 기준).
- Canvas 정렬·스케일: `InteractPromptCanvas` 의 Canvas / CanvasScaler. -5 = 머리 위 체력바(-10) 위·CombatHUD(0) 아래.
- 화면 오프셋: `InteractPromptView.screenOffset`(Canvas 단위, 위 = +y). 월드 높이: `ZoneBridgeGate.promptHeightOffset`(기본 1.8m).
- `CanvasGroup.alpha` 는 0 으로 저장해 둔다 — 런타임에 뷰가 켜고 끈다.
- 아이콘 원본 `Assets/50.Art/UI/HUD/F_KeyIcon.png` 는 SVN. 키 글자는 이미지에 박혀 있어 리바인딩은 반영하지 않는다.

## 게이트 변경점
판정·RPC·외곽선 로직은 그대로다.

- `ZoneBridgeGate`: 직렬화 필드 `promptView`(InteractPromptView) · `promptHeightOffset`(1.8m) + 읽기 프로퍼티.
- `ZoneBridgeGateManager.UpdateHighlight()` 끝: 외곽선 선택이 바뀔 때
  `InteractPrompt.Show(this, gate.PromptView, 패널 Transform, 위로 gate.PromptHeightOffset)`, 선택이 없어지면 `Hide(this)`.
  프롬프트는 외곽선과 **같은 선택**을 따른다(별도 거리 판정 없음) — 사망·유령·연출 잠금·범위 밖·이미 활성은 이미 거기서 걸러진다.
  매니저의 `promptHeightOffset` 필드는 없앴다(게이트로 이동).
- 정리: `OnDisable` · `OnDestroy` · `OnNetworkDespawn` · `UnregisterGate`(외곽선 게이트가 빠질 때)에서 `Hide(this)`.

## API (`Assets/1.Scripts/UI/Interact/`)
```csharp
InteractPrompt.Show(owner, view, targetTransform, worldOffset); // 띄우기(마지막 Show 우선, 동시에 1개)
InteractPrompt.Hide(owner);                                     // owner 가 띄운 것만 끈다
InteractPrompt.IsShownBy(owner);
```
- **로컬 전용·네트워크 동기화 없음.** 로컬 플레이어 기준 "지금 F 를 누르면 실행될 대상"이 바뀔 때만 부른다.
- **뷰는 소유자가 넘긴다.** 새 소비처(부활·상자 등)는 자기 프리팹에 `InteractPromptCanvas` 를 같은 구조로 두고 배선한다.
  다른 뷰로 Show 가 오면 이전 뷰는 숨는다. `view` 가 null 이면 경고 1회 후 표시하지 않는다(그 owner 의 기존 프롬프트도 끈다).
- owner 는 보통 `this`. Hide 는 소유자 기준이라 다른 소스가 켠 프롬프트를 끄지 않는다.
- 파괴·비활성 시 반드시 `Hide(this)`. (안 불러도 owner/대상이 파괴되면 다음 프레임에 자동으로 비워지지만, 의존하지 말 것.)
- 배치: 각 `InteractPromptView` 가 `Canvas.willRenderCanvases` 에서 자기가 표시 뷰일 때만 머리 위 체력바와 같은
  `OverheadHealthBarScreenPlacement` 로 위치를 잡는다 — 카메라 뒤·화면 밖·표시 뷰 아님이면 alpha 0.
- 예전 `Resources/InteractPromptSettings` 와 코드 생성 Canvas 경로는 제거했다(이중 경로 없음).

## 테스트
- EditMode `InteractPromptSlotTests`(메뉴 `Tools/Tests/플레이어 EditMode 테스트 실행`) — 소유권(마지막 Show 우선·소유자만 Hide·
  다른 뷰로 Show 시 이전 뷰 숨김·null 뷰/대상·파괴 처리).
