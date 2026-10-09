# Handoff — 상호작용 F 키 프롬프트 (2026-10-09, 은희 → 경석)

상호작용할 수 있는 대상 위에 F 키 아이콘을 띄우는 **로컬 전용** 서비스를 추가했다. 첫 소비처가 존 다리 게이트 패널이라
경석 영역인 `ZoneBridgeGate.cs` · `ZoneBridgeGateManager.cs` · `ZoneL_typeB.prefab` 을 고쳤다(PR 없음 규칙 — 이 문서로 공유).

## 어디를 고치나
| 바꿀 것 | 편집 위치 |
|---|---|
| 아이콘 이미지·크기·색, Canvas 정렬·스케일, 화면 오프셋 | `Assets/2.Prefabs/UI/InteractPrompt.prefab` (모든 대상에 반영) |
| 패널 위 높이 | `ZoneGatePanel.prefab` 의 `InteractPrompt` **로컬 위치** (모든 게이트 패널에 반영) |

## 게이트 패널 프리팹 (`Assets/2.Prefabs/Environment/Machinery/PowerUnits/ZoneGatePanel.prefab`)
- **게이트 패널 = `ZoneGatePanel.prefab`** — `object_panel.prefab` 의 Prefab Variant + 자식 `InteractPrompt` 중첩 인스턴스(로컬 (0, 1.8, 0)).
- **새 게이트 존은 이 프리팹을 배치**하고 `ZoneBridgeGate.panels` 에 넣는다. 원본 `object_panel.prefab` 은 `ZoneL_typeC`·`Zone_typeQuest01`·
  `all_mesh.unity` 에서 장식으로도 쓰므로 거기에 프롬프트를 붙이지 말 것.
- `ZoneL_typeB` 의 `PF_Prop_object_panel_001~004` 는 이 Variant 의 인스턴스다(이름·Transform 은 인스턴스 오버라이드).

## 프롬프트 프리팹 (`Assets/2.Prefabs/UI/InteractPrompt.prefab`)
F 아이콘 UI 는 프리팹 하나이고, **상호작용 대상 오브젝트의 자식으로 중첩 프리팹 인스턴스**를 놓아 쓴다. 코드는 만들지 않고 고르기만 한다.

```
InteractPrompt          일반 Transform(= 월드 기준점) + InteractPromptView
└─ Canvas               Canvas(Screen Space - Overlay, sortingOrder -5) + CanvasScaler(1920×1080, match height 1)
   └─ InteractKeyIcon   RectTransform 56×56(앵커 왼쪽 아래) + Image(F_KeyIcon, preserveAspect, raycast off) + CanvasGroup(alpha 0)
```

- 기준점이 Canvas 위 일반 Transform 인 이유: Overlay Canvas 루트의 Transform 은 Unity 가 화면에 맞춰 몰아서 위치 기준으로 못 쓴다.
  뷰는 매 프레임 **루트 `transform.position`** 을 화면 좌표로 바꿔 아이콘을 그 위에 놓는다 — 크기는 화면 기준이라 카메라 거리와 무관하게 일정하다.
- 아이콘 이미지·크기·색: `InteractKeyIcon` 의 Image / RectTransform `sizeDelta`. 앵커는 왼쪽 아래로 둘 것(위치 계산 기준).
- Canvas 정렬·스케일: `Canvas` 자식의 Canvas / CanvasScaler. -5 = 머리 위 체력바(-10) 위·CombatHUD(0) 아래.
- 화면 오프셋: 루트 `InteractPromptView.screenOffset`(Canvas 단위, 위 = +y).
- `CanvasGroup.alpha` 는 0 으로 저장해 둔다 — 런타임에 뷰가 켜고 끈다.
- 아이콘 원본 `Assets/50.Art/UI/HUD/F_KeyIcon.png` 는 SVN. 키 글자는 이미지에 박혀 있어 리바인딩은 반영하지 않는다.

## 게이트 변경점
판정·RPC·외곽선 로직은 그대로다.

- `ZoneL_typeB.prefab`: `PF_Prop_object_panel_001~004` 가 `ZoneGatePanel` 인스턴스라 각각 자식 `InteractPrompt` 를 가진다(로컬 위치 (0, 1.8, 0) — 패널 스케일 1·Y 회전만이라 월드로도 패널 위 1.8m).
- `ZoneBridgeGate.TryGetPromptView(panelIndex, out view)`: 패널 Transform 의 `GetComponentInChildren<InteractPromptView>(true)` 를 처음 한 번 캐시한다
  (별도 직렬화 리스트는 `panels` 와 순서를 맞춰야 해서 두지 않았다). 구 `promptPrefab`·`promptHeightOffset` 필드는 없다.
- `ZoneBridgeGateManager.UpdateHighlight()` 끝: 외곽선 선택이 바뀔 때 `InteractPrompt.Show(this, 그 패널의 뷰)`, 선택이 없어지면 `Hide(this)`.
  프롬프트는 외곽선과 **같은 선택**을 따른다(별도 거리 판정 없음) — 사망·유령·연출 잠금·범위 밖·이미 활성은 이미 거기서 걸러진다.
- 정리: `OnDisable` · `OnDestroy` · `OnNetworkDespawn` · `UnregisterGate`(외곽선 게이트가 빠질 때)에서 `Hide(this)`.

## 새 상호작용 대상 붙이는 법
1. 대상 프리팹 안, 프롬프트를 띄울 오브젝트의 자식으로 `InteractPrompt.prefab` 을 끌어다 놓고 로컬 위치로 높이를 맞춘다.
2. 대상 컴포넌트가 그 뷰를 찾아(`GetComponentInChildren<InteractPromptView>(true)` 또는 직렬화 참조) 들고 있는다.
3. "지금 F 를 누르면 실행될 대상"이 바뀔 때 `InteractPrompt.Show(this, view)`, 없어지면 `Hide(this)`.
4. 파괴·비활성(`OnDisable`·`OnDestroy`, 네트워크면 `OnNetworkDespawn`)에서 `Hide(this)`.

## API (`Assets/1.Scripts/UI/Interact/`)
```csharp
InteractPrompt.Show(owner, view); // 띄우기(마지막 Show 우선, 동시에 1개)
InteractPrompt.Hide(owner);       // owner 가 띄운 것만 끈다
InteractPrompt.IsShownBy(owner);
```
- **로컬 전용·네트워크 동기화 없음.** 로컬 플레이어 기준 "지금 F 를 누르면 실행될 대상"이 바뀔 때만 부른다.
- 넘기는 것은 대상 자식의 **씬 인스턴스 뷰**다. 다른 뷰로 Show 가 오면 이전 뷰는 숨는다.
  `view` 가 null 이면 경고 1회 후 표시하지 않는다(그 owner 의 기존 프롬프트도 끈다).
- owner 는 보통 `this`. Hide 는 소유자 기준이라 다른 소스가 켠 프롬프트를 끄지 않는다.
- 파괴·비활성 시 반드시 `Hide(this)`. (안 불러도 owner/뷰가 파괴되면 다음 프레임에 자동으로 비워지지만, 의존하지 말 것.)
- 배치: 각 `InteractPromptView` 가 `Canvas.willRenderCanvases` 에서 자기가 표시 뷰일 때만 머리 위 체력바와 같은
  `OverheadHealthBarScreenPlacement` 로 위치를 잡는다 — 카메라 뒤·화면 밖·표시 뷰 아님이면 alpha 0.
- 이력: `Resources/InteractPromptSettings` + 코드 생성 Canvas → `ZoneL_typeB` 안 `InteractPromptCanvas`(2a4976af)
  → 독립 프리팹 + 런타임 Instantiate(ca2e965c) → 패널 자식 중첩 인스턴스(213b08c0) → 게이트 패널 Variant `ZoneGatePanel`. 런타임 생성 경로 없음.

## 테스트
- EditMode `InteractPromptSlotTests`(메뉴 `Tools/Tests/플레이어 EditMode 테스트 실행`, 9건) — 소유권(마지막 Show 우선·소유자만 Hide)·
  다른 뷰로 Show 시 이전 뷰 숨김·null 뷰/소유자·파괴된 뷰/소유자 정리.
