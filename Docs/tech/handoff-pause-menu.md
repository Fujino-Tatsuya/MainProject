# handoff — 인게임 ESC 일시정지 메뉴 (경석 → 은희, 2026-10-20)

> 계획: [PLAN-pause-menu.md](../history/PLANS/PLAN-pause-menu.md) · 아트 SVN r466 `UI/UI_Panel`(03_PauseMenu · 02_ExitConfirm) · 문구 SVN r477(Strings 시트)

## 요약 — 은희가 할 일은 "나가기 두 개 연결"뿐
ESC 흐름과 UI 는 끝나 있다. **바탕화면 / 로비 의 실제 처리(호스트·클라 예외 포함)는 비워 뒀다** — 만들고 아래 이벤트에 연결하면 된다.

```
ESC ─▶ 일시정지 메뉴 ─ 계속하기 ─▶ 닫힘
                    ├ 설정 ─▶ 설정창 ─(X·ESC)─▶ 메뉴
                    └ 그만두기 ─▶ 종료 확인 ─ ✗·ESC ─▶ 메뉴
                                          └ [바탕화면|로비] 고르고 ✓ ─▶ 이벤트(← 은희 연결)
```

## 연결 지점 — `PauseMenuView`(`Assets/1.Scripts/UI/Pause/PauseMenuView.cs`)
- 위치: `4.MapScene` › `UI/Canvas/OptionPanel/PauseMenu`(프리팹 `Assets/2.Prefabs/UI/PauseMenu.prefab`).
- 코드 이벤트: `ExitToDesktopRequested` · `ExitToLobbyRequested` (`event Action`).
- 인스펙터: `onExitToDesktop` · `onExitToLobby` (UnityEvent — 코드 이벤트와 같이 불린다). 어느 쪽이든 하나만 연결하면 된다.
- 아무것도 연결 안 된 채 ✓ 를 누르면 `[PauseMenu] '로비' 나가기가 아직 연결되지 않았다` 경고만 뜬다(크래시 없음).

```csharp
// 예: MapSceneManager(또는 은희 쪽 퇴장 담당)에서
PauseMenuView pause = optionPanel.GetComponentInChildren<PauseMenuView>(true);
pause.ExitToLobbyRequested += LeaveToLobby;     // 호스트 = 전원 로비 복귀 / 클라 = 혼자 세션 구성 화면
pause.ExitToDesktopRequested += QuitToDesktop;  // 게임 종료(호스트가 끄면 클라 처리 포함)
```

## 바뀐 것 / 지금 끊긴 것
- `MapSceneManager`: ESC = 한 단계 뒤로(위 그림). `HandleEscape` · `ShowPauseMenu` 추가. 설정창 X 는 메뉴로.
- **설정창의 나가기 버튼(`OptionPanel/ExitButton`)은 꺼 뒀다** — 그만두기가 대신한다. 그래서 지금은
  **호스트 중도 종료 → 결과 화면(`GoToResult`) 경로가 UI 에서 빠져 있다.** 코드(`GoToResult` · 클라 경고창 · `ConfirmClientExit`)는 그대로 있으니 필요하면 재사용.
- 문구 키(Strings 시트, r477): `UI.Pause.Title/Continue/Settings/Quit/PressAnyButton` · `UI.Exit.Title/Message/Desktop/Lobby` · `UI.Settings.Title` — ko·en 다 있음.
- ⚠️ xlsx 는 **병합 Export 를 쓰지 않고** Strings 행만 직접 넣었다. 병합 Export 를 해 보니 Map 시트 엘리베이터 16행에 r470 에서 지운 "중첩 전용 Elevator 열"(D열)이 되살아났다 — **다음에 누가 병합 Export 할 때 같은 일이 생긴다.** 코드 쪽에 그 대상이 아직 남아 있는지 확인 필요(은희 r470 작업).
- 참고: `UI.StatusEffect.Grabbed`(r469) en 칸이 비어 ko 로 나온다(이번 작업 무관).

## 다시 만들 때
- `Tools/UI/Pause/일시정지 메뉴 만들기 + ESC 적용 (멱등)` — 프리팹을 처음부터 다시 만들고 맵에 없으면 넣는다. 배치·색은 `PauseMenuAuthoring.cs` 에서 고친다(프리팹 직접 수정은 다음 실행 때 덮인다).
- 선택 테두리 그림 `Assets/2.Prefabs/UI/Textures/PauseSelectRing.png` 는 이 메뉴가 생성한다.
