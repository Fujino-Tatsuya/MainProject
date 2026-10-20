# 로비 캐릭터 선택 패널 — 붙이는 법 (할 일 48.2)

> PLAN-48 v2 §4.6 "선택 패널" 의 UI 만 먼저 만든 것. 이 문서는 **패널을 로비에 붙이는 쪽(48 C12)** 이 볼 것만 적는다.
> 패널은 **네트워크를 모른다**(NetworkBehaviour·NetworkManager 참조 없음). 서버 요청·준비 상태 연결은 C12 몫이다.
> ✅ **C12 연결 완료(2026-10-19)** — 연결 코드: `Assets/1.Scripts/Lobby/LobbyInteractor.cs`(F → `Show`·`SetLocked`·`CharacterChosen` → `LobbySession.RequestCharacter`), 서버 검증·교체는 `LobbySession.RequestCharacterServerRpc` → `LobbyPlayerSpawner.TrySwapCharacter`.
> 만든 방식은 [lobby-result-panel.md](lobby-result-panel.md) 와 같다 — 자체 Canvas·`UiModalBlocker`·ESC 소유·순수 규칙 분리·저작/재생성/캡처 메뉴.

## 파일
| 무엇 | 경로 |
|---|---|
| 패널 프리팹 | `Assets/2.Prefabs/UI/CharacterSelect/CharacterSelectPanel.prefab` |
| 카드 프리팹 | `Assets/2.Prefabs/UI/CharacterSelect/CharacterSelectCard.prefab` (런타임에 로스터 수만큼 복제) |
| 뷰 | `Assets/1.Scripts/UI/CharacterSelect/CharacterSelectPanel.cs` · `CharacterSelectCard.cs` |
| 규칙(순수) | `Assets/1.Scripts/UI/CharacterSelect/CharacterSelectPanelRules.cs` — 테스트 `Editor/CharacterSelectPanelRulesTests.cs` |
| 배경 흐름 | `Assets/1.Scripts/UI/CharacterSelect/CharacterSelectBackgroundScroll.cs` |
| 문구 키 | `Assets/1.Scripts/UI/CharacterSelect/CharacterSelectUiStrings.cs` (`UiStrings.All` 에 포함) |
| 저작·미리보기 | `Assets/1.Scripts/UI/CharacterSelect/Editor/CharacterSelectPanelAuthoring.cs` |

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
- 카드 = 초상화(카드 모양으로 잘림) + 이름. **지금 내 캐릭터 = 금색 테두리 + "현재" 표식**(포커스 표시인 초록·크기와 따로 보인다).
  잠금 중에는 포커스 카드 초록이 바래고, 왼쪽 아래에 "준비를 해제해야 바꿀 수 있습니다".
- 왼쪽 아래 이름판 = 포커스된 캐릭터의 원형 초상화·이름. 왼쪽 위 = 뒤로가기 + 조작 안내. 맨 오른쪽 = SELECT 셰브론 띠.
- 배경 = 단색(`bg_color`, 불투명 — 뒤 클릭 막기) + 타이포 루프(`bg_typography_loop`)를 26° 돌려 줄무늬를 따라 **오른쪽으로 40px/s**(unscaled) 흐름(방향 = 10-19 결정).
  - 아트 텍스처가 Wrap=Clamp 라(아트 .meta 는 SVN 소관) uvRect 대신 타일 4장을 움직인다. 방향·속도는 `Bg_Rotate/Bg_Scroll` 의 인스펙터.
  - 일반판 vs `_gamma` 판: 캡처 비교(왼쪽 영역 평균 밝기, 0~255) 목업 30.2 · 일반판 34.1 · `_gamma` 43.2 → **일반판** 사용(프로젝트 = Linear).
- 목업의 "기본/코어" 탭(스킬 정보 자리)은 데이터가 없어 **배치하지 않았다**(`tab_*` 아트는 그대로 남아 있음).
- 폰트 = `NotoSansKR-VariableFont_wght SDF`(목업 폰트 없음) — 목업보다 얇다. 초상화는 로스터 `Portrait`(지금 HUD 가로 초상화·어쌔신은 임시 jpg).

## 조작
| 입력 | 동작 |
|---|---|
| W/S · ↑/↓ · A/D · ←/→ · 마우스 휠 | 이전(위·왼쪽·휠 위)/다음(아래·오른쪽·휠 아래) 카드로 포커스 — 잠금 중에도 구경 가능 |
| Enter · 숫자패드 Enter · Space · 가운데 카드 클릭 | 선택 확정(위 규칙) |
| 위·아래 카드 클릭 | 그 카드로 포커스 이동 |
| 뒤로가기 버튼 · ESC | 닫기(`Button`·`Escape`) |

- 키는 `Keyboard.current`·`Mouse.current` 를 직접 읽는다. 열려 있는 동안 `Content` 의 `UiModalBlocker` 가 로컬 플레이어 입력(이동·공격·F 등)을 막는다.
- 카드 클릭은 `Button` 이 아니라 `IPointerClickHandler` — EventSystem 선택이 남아 Enter 가 두 번 먹는 일을 막는다. 뒤로가기 버튼은 Navigation None.

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
- `Tools/UI/Preview/Character Select Panel 캡처` — 프리뷰 씬에서 1920×1080 PNG 를 `C:/UnityProject/_lane-artifacts/MainProject-Worktree4/48.2-char-select/` 에 저장(열린 씬 무수정):
  `CharacterSelect_unlocked.png`(지금 0번·포커스 1번) · `CharacterSelect_locked.png`(지금 1번·포커스 2번·잠금) · `CharacterSelect_vs_mockup.png`(왼쪽 목업) · `CharacterSelect_bg_gamma.png`(배경 _gamma 판 비교용), 콘솔에 배경 밝기 비교.
- 테스트: `Tools/Tests/캐릭터 선택 패널 EditMode 테스트 실행`(`CharacterSelectPanelRulesTests`).

## ⏳ 사람 Play 확인 (C12 연결 뒤)
- 열린 동안 이동·공격·F 가 막히고 닫으면 풀리는가. 뒤 화면 클릭이 안 먹는가.
- 키(W/S/A/D/방향키)·휠·카드 클릭으로 포커스가 순환하고, 이름판이 따라 바뀌는가. Enter/Space 한 번에 한 번만 확정되는가.
- 다른 캐릭터 확정 → `RequestCharacterServerRpc` 가 한 번 나가고 패널이 닫히는가. 지금 캐릭터 확정 → 요청 없이 닫히는가.
- 준비 중 열기 → 구경은 되고 확정하면 잠금 문구가 강조되는가. 열린 채 준비 해제(`SetLocked(false)`) → 바로 고를 수 있는가.
- 결과 패널이 위에 열려 있을 때 ESC 한 번에 결과 패널만 닫히는가(선택 패널은 그대로).
- 배경이 슬로모션과 무관하게 천천히 흐르고 가장자리가 비지 않는가(16:9 외 비율 포함).
