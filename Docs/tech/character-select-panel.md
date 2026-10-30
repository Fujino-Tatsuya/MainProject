# 로비 캐릭터 선택 패널 — 붙이는 법 (할 일 48.2)

> PLAN-48 v2 §4.6 "선택 패널" 의 UI 만 먼저 만든 것. 이 문서는 **패널을 로비에 붙이는 쪽(48 C12)** 이 볼 것만 적는다.
> 패널은 **네트워크를 모른다**(NetworkBehaviour·NetworkManager 참조 없음). 서버 요청·준비 상태 연결은 C12 몫이다.
> ✅ **C12 연결 완료(2026-10-19)** — 연결 코드: `Assets/1.Scripts/Lobby/LobbyInteractor.cs`(F → `Show`·`SetLocked`·`CharacterChosen` → `LobbySession.RequestCharacter`), 서버 검증·교체는 `LobbySession.RequestCharacterServerRpc` → `LobbyPlayerSpawner.TrySwapCharacter`.
> 만든 방식은 [lobby-result-panel.md](lobby-result-panel.md) 와 같다 — 자체 Canvas·`UiModalBlocker`·ESC 소유·순수 규칙 분리·저작/재생성/캡처 메뉴.

## 파일
| 무엇 | 경로 |
|---|---|
| 패널 프리팹 | `Assets/2.Prefabs/UI/CharacterSelect/CharacterSelectPanel.prefab` |
| 카드 프리팹 | `Assets/2.Prefabs/UI/CharacterSelect/CharacterSelectCard.prefab` (패널 안 인스턴스 `Card_Template` 의 원본) |
| **가운데 카드 모양은 어디서 고치나** | **`CharacterSelectPanel.prefab` > `Content/Cards/Card_Template`**(가운데 자리에 놓인 카드 프리팹 인스턴스, 패널 `cardTemplate`). 초상화 `Clip/Image_Portrait` 크기·회전, 금색 테두리 `Ring_Focus` 크기(RectTransform)·색·스프라이트(Image)를 여기서 고치면 런타임 카드가 이걸 로스터 수만큼 복제한다(옆 카드도 같은 원본, `sideScale` 로 줄 뿐). Play 에서 원본 자신은 꺼진다. 자리·배율·테두리 알파는 여전히 패널(`focusedSlot`·`focusScale`·연출)이 정한다 |
| 뷰 | `Assets/1.Scripts/UI/CharacterSelect/CharacterSelectPanel.cs` · `CharacterSelectCard.cs` |
| 규칙(순수) | `Assets/1.Scripts/UI/CharacterSelect/CharacterSelectPanelRules.cs` — 테스트 `Editor/CharacterSelectPanelRulesTests.cs` |
| 카드 이동 연출 규칙(순수, 할 일 60) | `Assets/1.Scripts/UI/CharacterSelect/CharacterSelectCardMotion.cs` — 같은 테스트 파일 |
| 이동 키 길게 누르기 규칙(순수, 할 일 60) | `Assets/1.Scripts/UI/CharacterSelect/CharacterSelectKeyRepeat.cs` — 같은 테스트 파일 |
| 배경 흐름 | `Assets/1.Scripts/UI/CharacterSelect/CharacterSelectBackgroundScroll.cs` |
| SELECT 셰브론 삼각형 클릭 영역(할 일 109) | `Assets/1.Scripts/UI/UiTriangleRaycastFilter.cs` — 테스트 `Assets/1.Scripts/UI/Editor/UiTriangleRaycastFilterTests.cs` |
| 문구 키 | `Assets/1.Scripts/UI/CharacterSelect/CharacterSelectUiStrings.cs` (`UiStrings.All` 에 포함) |
| 저작·미리보기 | `Assets/1.Scripts/UI/CharacterSelect/Editor/CharacterSelectPanelAuthoring.cs` |
| 가운데 금색 테두리 0.7배 굽기(할 일 162) | `Assets/1.Scripts/UI/CharacterSelect/Editor/CharacterSelectFocusRingBaker.cs` → `Assets/2.Prefabs/UI/Textures/CharacterSelectFocusRing.png` |
| UI 수정 테스트(할 일 162, 실제 프리팹) | `Assets/1.Scripts/UI/CharacterSelect/Editor/CharacterSelectUiPolishTests.cs` |

## 붙이는 법
1. 로비 씬 아무 곳에 `CharacterSelectPanel.prefab` 을 둔다. 루트에 자체 Canvas(Screen Space Overlay, **sortingOrder 190** — 결과 패널 200 아래)와
   CanvasScaler(1920×1080, match 0.5)가 있어 부모 Canvas 가 필요 없다. 씬에 **EventSystem** 이 있어야 한다(뒤로가기 버튼·카드 클릭).
2. 로스터는 프리팹에 `CharacterRoster.asset` 이 연결돼 있다. 카드는 `Show` 때 로스터 엔트리 수만큼 만든다(하드코딩 없음, 늘면 카드도 는다).
3. 준비 오브젝트 상호작용(F)에서 연다:
   ```csharp
   panel.Show(myCharacterId, isReady);
   panel.CharacterChosen += id => lobbySession.RequestCharacterServerRpc(id); // 🔴 서버 요청은 C12 가
   ```
4. 준비 상태가 열린 채로 바뀌면 `panel.SetLocked(isReady)` 를 부른다(카운트다운 시작·Ready 토글 등).
5. 서버가 교체를 거부해도 패널은 이미 닫혀 있다. 거부 피드백이 필요하면 C12 가 따로 띄운다.

## API (`CharacterSelectPanel`)
| 멤버 | 동작 |
|---|---|
| `void Show(int currentCharacterId, bool isLocked)` | 연다. 포커스 = 지금 캐릭터(없거나 범위 밖이면 0번). 이미 열려 있으면 값만 바꿔 다시 그린다 |
| `void Hide()` | 닫는다(`Closed(Api)`). 닫혀 있으면 아무것도 안 한다 |
| `void SetLocked(bool)` | 열린 채로 준비 상태 갱신 — 잠금 문구·확정 가능 여부. 닫혀 있으면 무시(다음 `Show` 값이 이긴다) |
| `bool IsOpen` / `bool IsLocked` / `int FocusedCharacterId` | 상태 |
| `event Action<int> CharacterChosen` | **다른** 캐릭터를 골랐다(id = 로스터 index). 곧이어 `Closed(Chosen)` |
| `event Action<CharacterSelectCloseReason> Closed` | 닫힐 때 한 번 — `Api`(호출 측 `Hide`)·`Button`(그냥 닫기 — 뒤로가기 또는 지금 캐릭터 재선택)·`Escape`·`Chosen` |
| `static bool IsEscapeClaimed` | 열려 있거나 이 프레임 ESC 로 닫혔다 — 아래 ESC 참고 |
| `void MoveFocus(int)` / `CharacterSelectConfirmResult Confirm()` | 키 입력과 같은 길(테스트·게임패드 연결용) |

### 선택 확정 규칙(`CharacterSelectPanelRules.Evaluate`, 이 순서)
| 경우 | 결과 |
|---|---|
| 준비 중(잠금) | 거부 — 이벤트 없음, 잠금 문구가 0.6초 붉게 커졌다 돌아온다. 패널은 열린 채 |
| 로스터 `Available=false` | 거부 — 이벤트 없음. 카드는 회색 + "선택 불가" |
| 지금 캐릭터(`SameAsCurrent`) | `CharacterChosen` 없이 그냥 닫힘 — 뒤로가기와 같은 `Closed(Button)`(전용 사유 없음, 10-19 결정) |
| 그 밖 | `CharacterChosen(id)` 한 번 → `Closed(Chosen)` |

## 화면
- 오른쪽 비스듬한 세로 스택: **위 = 이전, 가운데(초록 큰 카드) = 포커스, 아래 = 다음**. 끝에서 순환한다.
  카드가 2장이면 다른 카드는 아래, 4장 이상이면 포커스 ±1 만 보인다. 위·아래 카드는 목업처럼 화면 밖으로 일부 잘린다.
- 카드 = 초상화(카드 모양으로 잘림) + 이름. **지금 내 캐릭터 = "현재" 알약 표식만**(포커스 표시인 초록·크기와 따로 보인다).
  **금색 테두리(카드의 `focusRing`/`Ring_Focus`) = 가운데 자리 강조**(할 일 60, 사용자 결정 (a)) — 지금 캐릭터와 상관없다.
  - **두께 0.7배(할 일 162)**: 스프라이트 = 구운 PNG `Assets/2.Prefabs/UI/Textures/CharacterSelectFocusRing.png`(git, 694×551 = `Ring_Focus` rect 1:1, PPU 100). SVN 원본 `card_selected_ring.png` 는 그대로.
    원본은 테두리가 아니라 채워진 카드 실루엣이고, 카드 뒤에 크게(694×551) 깔려 카드 검은 테두리(`card_selected_gap` 636×501) 밖 띠만 보인다(옆 ≈23px·위아래 ≈25px).
    굽기 = 원본 링 그림을 가운데 기준 가로 ≈0.967·세로 ≈0.972 배로 줄여 그대로 다시 그린 것 — 가운데 가로·세로줄의 보이는 띠가 0.7배(23 → 16px)가 되는 배율, 모양·모서리·안티에일리어싱은 원본.
    rect 크기·위치·색(1, 0.8, 0.25)은 그대로. 메뉴 `Tools/UI/Authoring/Character Select Focus Ring (0.7배 굽기·연결)`(`CharacterSelectFocusRingBaker`, 배율 상수 `ThicknessScale`) —
    카드 프리팹 `Ring_Focus` 에 연결하고 패널 `Card_Template` 이 덮어쓰고 있으면 같이 맞춘다. 원본 파일 바이트를 직접 읽는다(.meta 무수정). 같은 입력 → 같은 픽셀(EditMode 테스트가 다시 구워 대조).
  잠금 중에는 포커스 카드 초록이 바래고, 왼쪽 아래에 "준비를 해제해야 바꿀 수 있습니다".

### 카드 이동·확대 연출(할 일 60)
값은 모두 패널 인스펙터 `Card Motion`(UI 연출 값 — SO·xlsx 아님). 시간 = `Time.unscaledTime`(슬로모션 무관). 트윈 패키지 없음 — 이징은 `CharacterSelectCardMotion.EaseOutCubic`.

| 필드 | 기본 | 뜻 |
|---|---|---|
| `moveSeconds` | 0.18 | 포커스가 바뀌면 카드가 지금 자리에서 새 자리(`focusedSlot`/`previousSlot`/`nextSlot`·`sideScale`)로 미끄러짐, EaseOutCubic |
| `ringFadeSeconds` | 0.1 | 금색 테두리 — 이동 시작에 꺼지고(지금 알파에서 선형), 이동이 끝나면 새 가운데 카드에서 켜짐 |
| `focusScaleSeconds` | 0.12 | 이동이 끝난 뒤 가운데 카드 확대, EaseOutCubic |
| `focusScale` | 1.08 | 다 선 가운데 카드 배율. 가운데에서 벗어나는 카드는 이동하면서 옆 크기(`sideScale`)로 돌아간다 |

- 단계 = `Idle → Moving(이동·테두리 페이드 아웃) → Settling(확대·테두리 페이드 인, 둘 중 긴 쪽) → Idle`.
- **이동 중 새 입력은 큐잉하지 않는다** — 키·휠·옆 카드 클릭·`MoveFocus` 가 오면 모든 카드가 **지금 보이는 자리·크기**에서 새 목표로 다시 출발(연타해도 끊김·점프 없음).
  테두리는 이어서 꺼지고(정착 중이었다면 켜지던 알파에서), 정착은 마지막 이동이 끝난 뒤 한 번만.
- 논리 포커스(`FocusedCharacterId`)·이름판·카드 모양(초록/옆 카드)은 입력 즉시 바뀐다. **확정(Enter/Space/가운데 클릭)은 이동 중에도 받고 대상은 논리 가운데**다.
  가운데 클릭 판정도 index == 논리 포커스(그 카드가 아직 미끄러지는 중이어도).
- 화면 밖 카드: 자리 offset ±2 = 위·아래 자리에서 같은 간격만큼 더 나간 곳(위 바깥 (1035, 888) · 아래 바깥 (-13, -1028), 둘 다 화면 밖).
  - 보이지 않던 카드가 보이게 되면 **도착하는 쪽 바깥**(위 자리면 위 바깥, 아래 자리면 아래 바깥)에서 들어온다. 큰 점프로 바로 가운데에 오면 이동 방향 쪽(포커스 앞으로 = 아래 바깥).
  - 보이던 카드가 화면 밖이 되면 **흘러가는 쪽 바깥**(포커스 앞으로 = 카드가 올라가므로 위 바깥, 뒤로 = 아래 바깥)으로 빠지고 이동이 끝나면 꺼진다.
    (자리 offset 은 4장일 때 ±2 가 +2 로 정해지므로 offset 부호가 아니라 이동 방향을 쓴다.)
  - 2·3장 순환에서 위↔아래로 바뀌는 카드는 사라졌다 나타나지 않고 **가운데 카드 뒤로 가로질러** 미끄러진다(가운데 카드가 맨 위에 그려진다).
- `Show`(열 때)·`SetLocked` 는 애니메이션 없이 바로 정렬 — 테두리 켜짐·확대 상태.

- 왼쪽 아래 이름판(`Content/Nameplate` — 원형 초상화·이름)은 **할 일 162 로 꺼 뒀다**(삭제 아님). 패널 `nameplatePortrait`·`nameplateName` 칸은 연결된 채라
  꺼진 동안에도 포커스 캐릭터 초상화·이름을 넣는다(오류 없음) — 다시 보이려면 프리팹에서 `Nameplate` 만 켠다. `(재생성)` 메뉴도 꺼진 채 만든다.
  왼쪽 위 = 뒤로가기 + 조작 안내. 맨 오른쪽 = SELECT 셰브론 띠 — 누르면 선택 확정(할 일 109).
- 배경 = 단색(`bg_color`, 불투명 — 뒤 클릭 막기) + 타이포 루프(`bg_typography_loop`)를 26° 돌려 줄무늬를 따라 **오른쪽으로 40px/s**(unscaled) 흐름(방향 = 10-19 결정).
  - 아트 텍스처가 Wrap=Clamp 라(아트 .meta 는 SVN 소관) uvRect 대신 타일 4장을 움직인다. 방향·속도는 `Bg_Rotate/Bg_Scroll` 의 인스펙터.
  - 일반판 vs `_gamma` 판: 캡처 비교(왼쪽 영역 평균 밝기, 0~255) 목업 30.2 · 일반판 34.1 · `_gamma` 43.2 → **일반판** 사용(프로젝트 = Linear).
- 목업의 "기본/코어" 탭(스킬 정보 자리)은 데이터가 없어 **배치하지 않았다**(`tab_*` 아트는 그대로 남아 있음).
- 폰트 = `NotoSansKR-VariableFont_wght SDF`(목업 폰트 없음) — 목업보다 얇다. 초상화는 로스터 `Portrait`(지금 HUD 가로 초상화·어쌔신은 임시 jpg).

## 조작
| 입력 | 동작 |
|---|---|
| W/S · ↑/↓ · A/D · ←/→ · 마우스 휠 | 이전(위·왼쪽·휠 위)/다음(아래·오른쪽·휠 아래) 카드로 포커스 — 잠금 중에도 구경 가능 |
| 이동 키 길게 누르기 | 누른 순간 1칸 → 0.25초 뒤부터 반복, 간격이 0.25초에서 선형으로 줄어 홀드 1초에 0.1초 도달 후 유지(아래 "길게 누르기"). 휠·카드 클릭은 반복 없음 |
| Enter · 숫자패드 Enter · Space · 가운데 카드 클릭 | 선택 확정(위 규칙) |
| SELECT 셰브론 클릭(`Content/Chevron` 의 Button, 할 일 109) | Space 와 같은 `Confirm()` — 잠금이면 잠금 문구 강조, 지금 캐릭터면 그냥 닫힘, 이동 중이면 논리 가운데 |
| 위·아래 카드 클릭 | 그 카드로 포커스 이동 |
| 뒤로가기 버튼 · ESC | 닫기(`Button`·`Escape`) |

- 키는 `Keyboard.current`·`Mouse.current` 를 직접 읽는다. 열려 있는 동안 `Content` 의 `UiModalBlocker` 가 로컬 플레이어 입력(이동·공격·F 등)을 막는다.
- 카드 클릭은 `Button` 이 아니라 `IPointerClickHandler` — EventSystem 선택이 남아 Enter 가 두 번 먹는 일을 막는다. 뒤로가기·셰브론 Button 은 Navigation None
  (None 이면 클릭해도 선택되지 않는다). 셰브론은 혹시 선택이 남아 있으면 클릭 때 지우고, 키 입력과 같은 조건(열림·`LobbyResultPanel.IsEscapeClaimed` 아님)에서만 `Confirm()` 을 부른다.
  같은 프레임에 클릭과 Space 가 겹쳐도 확정·닫힘이면 두 번째는 닫힌 패널이라 무시되고, 잠금이면 같은 강조가 다시 걸릴 뿐이다.
- 셰브론 버튼 = 사용자가 에디터에서 `Chevron`(chevron.png 이미지, 'SELECT' 글자는 형제 `SelectBadge`)에 Button 을 직접 붙인 것. 패널 `confirmButton` 칸이 이 Button 을 가리킨다.
  Image Raycast Target 켬, 색 전이 = Button 기본 Color Tint. 글자·문구 키 없음.
- **클릭 영역 = 삼각형**(`UiTriangleRaycastFilter`, `ICanvasRaycastFilter`) — rect 의 왼쪽 위·아래 빈 곳은 클릭이 지나간다.
  아트 텍스처가 Read/Write 꺼짐이라(아트 .meta 는 SVN) `Image.alphaHitTestMinimumThreshold` 를 쓰지 않는다.
  꼭짓점 3개 = 인스펙터(rect 정규화 좌표, 0,0 = 왼쪽 아래 — rect 크기·pivot 이 바뀌어도 맞음, rect 밖 값 가능). 기본 (-0.04, 0.5)·(1, 1)·(1, 0):
  chevron.png 빗변 두 줄을 늘인 교점(끝이 둥글어 rect 왼쪽 7.5px 바깥)과 오른쪽 모서리 — 그림 픽셀이 전부 안에 들고 끝 둥근 곳 바깥 98픽셀만 더 잡힌다.
  셰브론을 고른 채 씬 뷰에서 하늘색 삼각형 기즈모로 보인다. 판정 = 순수 `UiTriangleRaycastFilter.Contains`(변·꼭짓점 위 = 안).
- **hover 흰 아웃라인**(할 일 109 후속): 셰브론의 `UiHoverOutline` 이 Pointer Enter/Exit(삼각형 필터를 거친 레이캐스트 — hover 판정은 계속 삼각형)로
  자식 `Outline_Hover` 를 켜고 끈다. Button 이 interactable 일 때만(`onlyWhenInteractable`), 비활성화 때 끔.
  - **모양 = chevron.png 실제 알파 실루엣**(왼쪽 둥근 끝 포함)을 두르는 흰 링. `Outline_Hover` = `Image`(스프라이트 `Assets/2.Prefabs/UI/Textures/CharacterSelectChevronHoverRing.png`,
    흰색, Raycast Target 끔, 기본 꺼짐) — rect 는 셰브론 rect 를 링 여백(11px)만큼 사방으로 늘린 것(앵커 stretch, offset ±11). 링은 셰브론 픽셀과 겹치지 않아 자식으로 위에 그려도 셰브론을 가리지 않는다.
  - **굽기** = 메뉴 `Tools/UI/Authoring/Character Select Chevron Hover Ring (굽기·연결)`(`CharacterSelectPanelAuthoring.BakeAndApplyChevronHoverRing`) —
    `UiSilhouetteRingBaker.Bake(원본 png, 출력 png, 기준 표시 크기, 두께, 간격)` 로 PNG 를 다시 굽고 패널 프리팹 `Outline_Hover` 를 맞춘다. `(재생성)` 메뉴도 같은 구성(링 PNG 가 있으면 그대로 쓰고 없으면 굽는다).
    원본은 `chevron.png` 파일 바이트를 직접 읽는다(SVN 아트 Read/Write 꺼짐 — .meta 무수정). 런타임 생성 없음. 같은 입력 → 같은 픽셀(EditMode 테스트가 다시 구워 비교).
  - 값 = `CharacterSelectPanelAuthoring.ChevronRingThickness` **7px**(할 일 162 에서 9 → 7)·`ChevronRingGap` **3px** — 셰브론 **표시 크기 기준 화면 px**(1920×1080 기준 캔버스, 기준 표시 크기 = 셰브론 rect 190×659).
    텍스처 px = 화면 px × (원본 텍스처 폭 / 표시 폭) — 지금은 1:1 이라 링 PNG 212×681(원본 190×659 + 사방 11), PPU 100. 실루엣 = 알파 ≥ 128, 링 가장자리 1px 안티에일리어싱.
    두께·간격을 바꾸려면 상수를 고치고 굽기 메뉴를 다시 돌린다(테스트가 PNG 와 상수를 대조한다).
  - **잠금(준비 중) 동안은 켜지 않는다** — 패널 `Redraw`(Show·SetLocked 경로)가 `UiHoverOutline.Blocked = Rules.IsLocked` 를 넣는다. hover 중에 잠기면 바로 꺼지고, 풀리면 바로 켜진다.
    Button 은 interactable 그대로라 잠금 중 클릭 = `Confirm()` → Locked → 잠금 문구 강조.
- **hover 동안 셰브론 본체 알파 1 → 0.8**(할 일 162): 셰브론 Button 의 Color Tint `Highlighted` 알파 0.8(RGB 0.961 그대로)·`Fade Duration` 0.08초 — 인스펙터에서 바로 고친다.
  Color Tint 대상 = 셰브론 Image 뿐이라 자식 흰 링(`Outline_Hover`)·형제 `SelectBadge` 는 알파 1 그대로. 잠금 규칙은 그대로(잠금 중엔 흰 링만 안 켜지고, Button 색 전이는 interactable 이라 평소처럼 돈다).
  새로 Button 을 붙일 때(재생성) 값 = `CharacterSelectPanelAuthoring.ChevronHoverAlpha`·`ChevronHoverFadeSeconds`(이미 붙어 있으면 손댄 값 유지).

### 길게 누르기(키 반복, 할 일 60·60.1)
판단 = `CharacterSelectKeyRepeat`(순수). 패널은 매 프레임 "뒤로(W·↑·A·←) / 앞으로(S·↓·D·→) 중 하나라도 눌려 있는가"(`isPressed`)와 `Time.unscaledTime` 을 넘기고, 돌려받은 칸(-1·0·+1)만큼 `MoveFocus` 한다.
값은 패널 인스펙터 `Key Repeat`(UI 값 — SO·xlsx 아님).

| 필드 | 기본 | 뜻 |
|---|---|---|
| `keyRepeatDelaySeconds` | 0.25 | 처음 1칸 뒤 첫 반복까지 대기 |
| `keyRepeatStartIntervalSeconds` | 0.25 | 첫 반복 직후 간격(가속 시작값) |
| `keyRepeatMinIntervalSeconds` | 0.1 | 가장 짧은 간격 — 램프가 끝나면 유지(옛 `keyRepeatIntervalSeconds`, `FormerlySerializedAs` 로 값 승계) |
| `keyRepeatRampSeconds` | 0.75 | 간격이 시작값에서 최소값까지 줄어드는 시간(첫 반복 시점부터) |

- **가속(할 일 60.1)**: 반복 칸을 낸 시점의 홀드 시간 h(그 방향 반복이 시작된 시각 기준)로 다음 간격 = `Lerp(시작 간격, 최소 간격, clamp01((h - 대기) / 램프))`.
  기본값이면 칸 시각 ≈ 0 · 0.25 · 0.50 · 0.70 · 0.86 · 0.99 · 1.09 · 1.19 … (홀드 1초에 0.1초 도달, 그 뒤 0.1초 유지).
  칸 시각은 예정 시각 기준이라 프레임 속도와 무관하다(프레임이 밀린 경우만 그 프레임 기준).
- 같은 방향 키 두 개(W 와 ↑ 등)는 하나로 친다 — 하나를 떼도 다른 쪽이 눌려 있으면 반복이 이어진다.
- 반대 방향을 새로 누르면 **즉시 그쪽 1칸 + 대기·가속부터 다시**. 둘 다 누르고 있으면 **나중에 누른 쪽**이 이긴다. 같은 프레임에 둘 다 누르면 뒤로(기존 키 검사 순서).
- 이기던 키를 떼고 반대쪽이 아직 눌려 있으면 그 방향으로 **대기·가속부터 다시(즉시 1칸 없음)**. 둘 다 떼면 멈춘다. 떼고 다시 누를 때도 가속은 처음부터.
- **한 프레임 최대 1칸** — 프레임이 길어 간격이 여러 번 지나도 1칸만 가고 밀린 칸은 버린다(다음 칸 = 그 프레임 + 그 시점 간격).
- 반복 칸마다 카드 이동은 지금처럼 재목표 — 처음 몇 칸(0.25·0.2초 간격)은 이동 0.18초보다 길어 칸 사이에 잠깐 멈춰 보일 수 있고, 간격이 0.18초 아래로 줄면 카드는 계속 미끄러진다. 누르고 있는 동안 금색 테두리는 꺼진 채, 떼고 마지막 이동이 끝나면 켜진다.
- `Show`·닫힘(`Hide`·ESC·확정·뒤로가기)·`LobbyResultPanel.IsEscapeClaimed` 인 프레임에는 반복 상태를 비운다. **그때 눌려 있던 이동 키는 뗐다가 새로 누를 때까지 무시**(열자마자 미끄러지지 않게).
- 잠금 중에도 반복된다(확정만 막힘). Enter·Space·ESC 가 눌린 프레임에는 이동 키를 보지 않는다 — 같은 프레임에 누른 이동 키는 다음 프레임에 1칸으로 들어간다.

## ESC
- 🔴 `LobbyResultPanel.IsEscapeClaimed` 가 true 면(결과 패널이 위에 열려 있거나 이 프레임 ESC 로 닫힘) 선택 패널은 **ESC 와 키·휠·카드 클릭을 모두 무시**한다 — 같은 키로 둘이 같이 닫히지 않는다.
- 반대로 로비에 ESC 를 쓰는 다른 메뉴(일시정지·설정 등)를 붙이면 그쪽은 `CharacterSelectPanel.IsEscapeClaimed` 도 확인해야 한다.

## 문구 키(`UI.CharacterSelect.*`) — xlsx Strings 시트 반영은 병합 뒤 메인
| 키 | ko |
|---|---|
| `UI.CharacterSelect.LockedNotice` | 준비를 해제해야 바꿀 수 있습니다 |
| `UI.CharacterSelect.CurrentTag` | 현재 |
| `UI.CharacterSelect.Unavailable` | 선택 불가 |
| `UI.CharacterSelect.Hint` | W/S·방향키 이동   Enter 선택   Esc 뒤로 |

캐릭터 이름은 로스터 `Character.<프리팹>.Name`(기존 키).

## 다시 만들기·미리보기
- `Tools/UI/Authoring/Character Select Panel` — 없는 프리팹만 만든다(있으면 손대지 않음).
- `Tools/UI/Authoring/Character Select Panel (재생성)` — 두 프리팹을 처음부터 덮어쓴다(GUID 유지, 내부 fileID 는 바뀜). 자리·색은 스크립트 상단 상수와 패널·카드 인스펙터.
  가운데 카드 원본 `Card_Template` 도 `CharacterSelectPanelAuthoring.EnsureCardTemplate`(가운데 자리·`focusScale`, 미리보기 = 초록·테두리 켬·로스터 0번 초상화, 이미 있으면 연결만)로 다시 놓인다.
  셰브론도 `CharacterSelectPanelAuthoring.MakeChevronButton`(Raycast Target·Button Navigation None·삼각형 필터, 이미 붙어 있으면 그대로 쓰고 Navigation·대상 그래픽만 맞춤)으로 선택 버튼이 되고 `confirmButton` 에 연결된다.
- `Tools/UI/Preview/Character Select Panel 캡처` — 프리뷰 씬에서 1920×1080 PNG 를 `C:/UnityProject/_lane-artifacts/MainProject-Worktree4/48.2-char-select/` 에 저장(열린 씬 무수정):
  `CharacterSelect_unlocked.png`(지금 0번·포커스 1번) · `CharacterSelect_locked.png`(지금 1번·포커스 2번·잠금) · `CharacterSelect_vs_mockup.png`(왼쪽 목업) · `CharacterSelect_bg_gamma.png`(배경 _gamma 판 비교용), 콘솔에 배경 밝기 비교.
  이동 연출은 시계를 넘겨 다 선 상태로 찍는다.
- `Tools/UI/Preview/Character Select Panel 이동 연출 캡처` — 같은 방식으로 `C:/UnityProject/_lane-artifacts/MainProject-Worktree4/60-anim/` 에 저장. 지금 0번·포커스 0번에서 `MoveFocus(1)` 후 패널 시계를 직접 넣어 샘플링:
  `CharacterSelect_anim_0_idle.png`(열 때) · `_1_moving_pos50.png`(위치 50% = 시간 ≈ 21%, 테두리 반쯤) · `_2_moving_ring_off.png`(시간 ≈ 56%, 테두리 꺼짐) · `_3_settled.png`(정착 — 테두리 켜짐·확대).
  시계 교체는 에디터 전용 `CharacterSelectPanel.SamplePreviewMotion`(`#if UNITY_EDITOR`).
- `Tools/UI/Preview/Character Select Panel 길게 누르기 캡처` — 같은 폴더에 앞으로 키를 시각 0 부터 누른 것으로 치고 60fps 로 `CharacterSelectKeyRepeat` 를 돌려
  `CharacterSelect_hold_0_0.35s.png` · `_1_0.45s.png` · `_2_0.55s.png`(반복 칸 재목표 직후 미끄러짐)를 저장. 콘솔에 대기·간격·램프 값과 1.5초까지의 칸 이동 시각(가속 확인용).
- 테스트: `Tools/Tests/캐릭터 선택 패널 EditMode 테스트 실행`(`CharacterSelectPanelRulesTests` + 셰브론 버튼 `CharacterSelectConfirmButtonTests` — 실제 프리팹을 프리뷰 씬에 올려 클릭 + 삼각형 판정 `UiTriangleRaycastFilterTests` + 가운데 카드 원본 `CharacterSelectCardTemplateTests` — `Card_Template` 의 초상화 크기·회전·테두리 크기·색·스프라이트가 런타임 카드에 들어가고 원본은 꺼지는지).

## ⏳ 사람 Play 확인 (C12 연결 뒤)
- 열린 동안 이동·공격·F 가 막히고 닫으면 풀리는가. 뒤 화면 클릭이 안 먹는가.
- 키(W/S/A/D/방향키)·휠·카드 클릭으로 포커스가 순환하는가(이름판은 할 일 162 로 꺼짐 — 왼쪽 아래에 보이지 않아야 한다). Enter/Space 한 번에 한 번만 확정되는가.
- 다른 캐릭터 확정 → `RequestCharacterServerRpc` 가 한 번 나가고 패널이 닫히는가. 지금 캐릭터 확정 → 요청 없이 닫히는가.
- 준비 중 열기 → 구경은 되고 확정하면 잠금 문구가 강조되는가. 열린 채 준비 해제(`SetLocked(false)`) → 바로 고를 수 있는가.
- 결과 패널이 위에 열려 있을 때 ESC 한 번에 결과 패널만 닫히는가(선택 패널은 그대로).
- (할 일 109) SELECT 셰브론 클릭 한 번 = 확정 한 번(다른 캐릭터 → 요청 1회·닫힘, 지금 캐릭터 → 닫힘, 잠금 → 잠금 문구 강조)인가. 잠금 중 클릭한 뒤 Enter/Space 를 눌러도 강조만 한 번 더 될 뿐 두 번 확정되지 않는가. 이동 중 클릭이 새 가운데로 확정되는가. 셰브론이 마우스 올림·눌림 색으로 바뀌는가.
  삼각형 밖(셰브론 rect 의 왼쪽 위·아래 빈 곳 — 아래 카드가 겹치는 자리 포함)을 누르면 확정되지 않고 그 뒤 카드 클릭이 먹는가. 'SELECT' 글자 위를 눌러도 확정되는가.
- (할 일 109 후속) `Card_Template` 에서 초상화 크기·회전, `Ring_Focus` 크기·색을 바꾸고 Play → 가운데 카드에 그대로 나오는가. 패널을 열었을 때 카드가 겹쳐(원본 + 복제본) 보이지 않는가. 옆 카드는 줄어든 채 같은 초상화 모양인가.
- (할 일 60) 카드가 미끄러지듯 움직이고 연타해도 점프 없이 새 목표로 가는가. 테두리가 이동 중 꺼졌다가 가운데 카드에서 켜지고 살짝 커지는가. 이동 중 Enter 가 새 가운데로 확정되는가.
- (할 일 60·60.1 길게 누르기) 이동 키를 누르고 있으면 0.25초 뒤부터 처음엔 천천히(0.25초 간격) 넘어가다 점점 빨라져 1초쯤부터 0.1초마다 넘어가고, 떼면 멈추는가. 가속이 자연스럽고 빨라진 뒤 미끄러짐이 끊겨 보이지 않는가.
  누른 채 반대 키 → 즉시 반대로 1칸 후 대기·느린 반복부터, 반대 키만 떼면 대기 뒤 원래 방향으로 느린 반복부터 이어지는가. 떼고 다시 누르면 다시 느리게 시작하는가. W+↑ 같이 눌러도 2칸씩 가지 않는가.
  키를 누른 채 패널을 열면(F) 미끄러지지 않고, 새로 눌러야 움직이는가. 잠금 중에도 반복되는가. 결과 패널이 위에 있으면 반복이 멈추는가.
- 배경이 슬로모션과 무관하게 천천히 흐르고 가장자리가 비지 않는가(16:9 외 비율 포함).
