# handoff — 설정창·ESC 메뉴 DDOL 통합 (경석 10-22 → 은희)

> 계획: [PLAN-settings-ddol.md](../../PLAN-settings-ddol.md). 브랜치 `feature/SettingsDdol`.
> 은희 영역(네트워크 세션·플레이어 입력)을 건드린 부분만 적는다. 리뷰 대신 이 문서로 공유한다(workflow.md).

## 1. 무엇이 바뀌었나
- 설정창 + ESC 메뉴 = `Assets/2.Prefabs/UI/GameMenu.prefab` 하나. `0.BootStrapScene`·`Dev_Boot` 에 있고 DDOL 로 끝까지 산다(`GameMenuController`).
  - 맵 `OptionPanel`(PauseMenu·SettingsBody·클라 경고창·ExitButton 포함)과 타이틀 `Settings_Root` 는 **지웠다**.
  - 씬 모드: 타이틀 = 설정만(모니터 RT 안) / 로비·인게임 = 계속·설정·**그만두기** / 로딩·데모 연결 대기 = 안 열림.
  - 그만두기 팝업 = **바탕화면 / 타이틀** 두 버튼 — 누르면 **바로** 실행(다시 묻지 않음, ✗/✓ 숨김). 취소는 ESC → 메뉴. 로비·인게임 같다.
    타이틀 = 세션을 떠나 타이틀로, 바탕화면 = 게임 종료.
    가운데 문구: 클라 "정말 종료하시겠습니까?" / 호스트는 경고(로비 "다른 플레이어도 연결이 끊깁니다" · 인게임 "다른 플레이어의 게임도 끝납니다").
- 로비 씬 자체는 건드리지 않았다(DDOL 이 덮는다).

## 2. 은희 영역 변경
### 2-1. 그만두기 → "타이틀" = 의도적 이탈 (`DemoSessionDirector`·`ReconnectLoop`) — 로비·인게임 둘 다
데모 흐름에는 의도적 이탈과 끊김을 가르는 장치가 없었다 — 그냥 Shutdown + 타이틀 로드를 하면 호스트는 3초 뒤 재호스팅해 NGO 가 로비로 타이틀을 덮고,
클라는 대기 씬으로 끌려가 재접속하며, 타이틀에 돌아와도 `_begun=true` 라 START 가 `Ignore` 로 먹통이었다.
- `ReconnectLoop.Reset()` — Idle 로. Idle 에서 `OnStopped` 는 None 을 돌려주므로 뒤따르는 서버/클라 정지가 끊김으로 처리되지 않는다.
- `DemoSessionDirector.PrepareLeaveToTitle()` — 루프 Reset · `_begun=false` · 대기 플래그 해제 · 오버레이 숨김. **Shutdown 전에** 부른다.
- `GameManager.LeaveSessionToTitle()` — 위를 부르고 `NetworkManager.Shutdown()` → `!IsListening && !ShutdownInProgress` 까지 기다린 뒤 `1.TitleScene` 로컬 Single 로드.
  🔴 `0.BootStrapScene` 은 다시 로드하지 않는다(NetworkManager 중복).
- 호스트가 나가면 클라 전원은 **끊김 경로**("호스트 대기 중")로 간다 — 기존 동작 그대로. 인게임에서 클라가 나가면 판은 계속(PlayerObject 디스폰, 같은 playerId 면 재합류).
- EditMode: `ReconnectLoopTests` 이탈 3건.

### 2-2. 그만두기 → "바탕화면"
`GameManager.QuitGame()`(에디터 Play 정지 / 빌드 종료). 판 진행 정리는 기존 `OnApplicationQuit` 들 그대로.
`PauseMenuView` 이벤트: `ExitToTitleRequested`(옛 `ExitToLobbyRequested` 자리 — 오른쪽 버튼은 이름이 `Lobby` 지만 글자는 "타이틀") · `ExitToDesktopRequested`.
두 갈래 선택 상태(`ExitChoice`)·✓ 확인 단계와 인스펙터 UnityEvent(`onExitToDesktop`·`onExitToLobby`)는 지웠다 — 연결된 곳이 없었다.

### 2-3. 입력 설정 기본값 (`UserInputConfig`)
처음 실행(저장값 없음) 기본값을 **홀드 스킬 토글 = 켬 · 대시 방향 = 마우스(Cursor)** 로 바꿨다(경석 10-22). 이미 저장된 PC 는 그대로. enum 값은 안 바꿨다(저장 호환).

### 2-4. 스킬 조준 ESC (`PlayerSkillTargeting`)
조준 취소·자동 접근 취소에 쓴 ESC 를 `PlayerSkillTargeting.IsEscapeClaimed`(이 프레임)로 남긴다 — ESC 메뉴가 같은 키로 같이 열리지 않게.

## 3. 알아 둘 것
- ESC 우선순위는 `GameMenuController.LateUpdate` 한 곳: 다른 창(로비 결과·캐릭터 선택·조준)이 쓴 ESC · 키 변경 중 · 드롭다운 펼침이면 무시.
- 새 ESC 소비자를 만들면 `IsEscapeClaimed` 같은 "이 프레임 썼다" 표시를 두고 `GameMenuController` 의 `claimed` 에 더할 것.
- M 키 맵 전체 보기(`MapOverviewUI`)는 꺼 두었다(`hotkeyEnabled`, 경석 10-22 — 안 씀).
- 새 문구 6개(UI.Exit.ToTitle · UI.Exit.HostQuitWarning·HostToTitleWarning · UI.Title.Start·Setting·Exit)는 Strings 시트(ko·en) + 서울알림 Heavy 아틀라스에 넣었다(SVN).
- 타이틀 버튼 글자 연출 `TextScramble` 이 Awake 때 글자를 저장해 두고 켜질 때마다 그걸로 덮어써 언어를 바꿔도 한국어로 남았다 —
  같은 오브젝트의 `LocalizedText` 키로 지금 언어 문구를 목표로 쓰게 고쳤다(연출 중 언어가 바뀌면 다시 재생).

## 4. 후속(이번 범위 밖)
- `EnemyC3PrewarmTests` 2건이 **에디터에서 Play 를 한 번 한 뒤에** 실패한다 — `WellsDroneVisualPool` 이 `HideAndDontSave` 라 Play 가 끝나도 에디터 메모리에 남아
  `Single(…"WellsDroneVisualPool")` 이 2개를 본다. Play 전 실행은 통과. 57.3(`ddc0ee61`) 쪽 — Play 종료 때 풀을 지우거나 테스트가 이전 풀을 무시하게.
