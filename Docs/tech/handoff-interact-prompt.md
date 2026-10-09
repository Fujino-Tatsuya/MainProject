# Handoff — 상호작용 F 키 프롬프트 (2026-10-09, 은희 → 경석)

상호작용할 수 있는 대상 위에 F 키 아이콘을 띄우는 **로컬 전용** 서비스를 추가했다. 첫 소비처가 존 다리 게이트 패널이라
경석 영역인 `ZoneBridgeGateManager.cs` 를 몇 줄 고쳤다(PR 없음 규칙 — 이 문서로 공유).

## 게이트 매니저 변경점 (`Assets/1.Scripts/Map/ZoneBridgeGateManager.cs`)
판정·RPC·외곽선 로직은 그대로다. 추가된 것만:

- 직렬화 필드 `promptHeightOffset`(기본 1.8m) — 패널 위 아이콘 높이.
- `UpdateHighlight()` 끝: 외곽선 선택이 바뀔 때 `InteractPrompt.Show(this, 패널 Transform, 위로 promptHeightOffset)`,
  선택이 없어지면 `InteractPrompt.Hide(this)`. 프롬프트는 외곽선과 **같은 선택**을 따른다(별도 거리 판정 없음) —
  사망·유령·연출 잠금·범위 밖·이미 활성은 이미 거기서 걸러진다.
- 정리: `OnDisable` · `OnDestroy` · `OnNetworkDespawn` · `UnregisterGate`(외곽선 게이트가 빠질 때)에서 `Hide(this)`.

## 새 API (`Assets/1.Scripts/UI/Interact/`)
```csharp
InteractPrompt.Show(owner, targetTransform, worldOffset); // 띄우기(마지막 Show 우선, 동시에 1개)
InteractPrompt.Hide(owner);                               // owner 가 띄운 것만 끈다
InteractPrompt.IsShownBy(owner);
```
- **로컬 전용·네트워크 동기화 없음.** 로컬 플레이어 기준 "지금 F 를 누르면 실행될 대상"이 바뀔 때만 부른다.
- owner 는 보통 `this`. Hide 는 소유자 기준이라 다른 소스(부활·상자 등)가 켠 프롬프트를 끄지 않는다.
- 파괴·비활성 시 반드시 `Hide(this)`. (안 불러도 owner/대상이 파괴되면 다음 프레임에 자동으로 비워지지만, 의존하지 말 것.)
- 표시: DontDestroyOnLoad Screen Space - Overlay Canvas(`InteractPromptCanvas`, sortingOrder -5 = 머리 위 체력바 위·CombatHUD 아래).
  배치는 머리 위 체력바와 같은 `OverheadHealthBarScreenPlacement` — 카메라 뒤·화면 밖이면 숨김. 크기는 세로 1080 기준 픽셀로 일정.
- 외형: `Assets/Resources/InteractPromptSettings.asset`(아이콘 스프라이트·크기 56×56·화면 오프셋).
  아이콘은 `Assets/50.Art/UI/HUD/F_KeyIcon.png`(SVN, Sprite 임포트). 에셋이 없거나 아이콘이 비면 TMP 텍스트 "F".
  키 글자는 이미지에 박혀 있어 리바인딩은 반영하지 않는다.

## 테스트
- EditMode `InteractPromptSlotTests` — 소유권(마지막 Show 우선·소유자만 Hide·null/파괴 처리).
