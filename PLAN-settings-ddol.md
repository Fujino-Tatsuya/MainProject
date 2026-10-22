# ▶ 계획 = 설정창·ESC 메뉴를 타이틀부터 유지되는 DDOL 프리팹 하나로 (2026-10-22 · grill 확정 · ✅ 승인 · ✅ 구현 · 컴파일 0 · EditMode 2717/0 · ✅ 검토 워크플로 결함 4건 수정 · ✅ 경석 Play · ✅ 커밋 · SVN r500)

> 구현 뒤 추가(경석 10-22 Play): 타이틀 버튼 한국어 기본 · 새 문구 Strings 시트·서울알림 아틀라스 · 입력 기본값(홀드 토글·대시 마우스) · M 키 맵 전체 보기 끔.
> ⚠️ 뒤집음(경석 10-22 Play): 결정 3 의 버튼 구성 → 로비·인게임 모두 **그만두기** 하나, 팝업의 **바탕화면 / 타이틀** 버튼을 누르면 바로 실행
>   (목업 02_ExitConfirm 의 "로비" 자리가 "타이틀", ✗/✓ 는 숨김 — 다시 묻지 않는다, ESC = 메뉴로). 가운데 문구 = 클라 "정말 종료하시겠습니까?" · 호스트 경고.
>   인게임에서도 타이틀로 갈 수 있다(같은 의도적 이탈 경로). 아래 결정 3 표·"인게임 복귀 없음"은 이 줄로 대체.
> 검토(4관점 + 반박 검증)로 고친 것: 로비·인게임 드롭다운 클릭 불가(MonitorAwareRaycaster 가 꺼진 TitleScreenRaycaster 를 모니터로 판정) ·
> 확인 팝업 ✗ 뒤 ESC 헛돎 · 드롭다운 펼친 채 ESC 가 설정창까지 닫음 · 맵 전체 보기가 메뉴 위(→ 기능 끔).

> 담당 경석. 브랜치 `feature/SettingsDdol`(기준 origin/development `035fce61`) 예정.
> 증상: 로비에서 설정창이 안 열린다. 설정창·ESC 메뉴가 씬마다 따로 있다 — 타이틀 = `TitleFlowDirector` 의 씬 인스턴스,
> 맵 = `MapSceneManager` + `4.MapScene` 의 `OptionPanel`, 로비 = 없음(10-19 로비 재구성 이후 붙인 적이 없다).
> 교차검토: Codex 10-22(경석 안을 목표 구조로 추천 — 핵심 근거 4건 Claude 가 코드로 대조 확인).

## 결정 (grill 2026-10-22, 경석)
1. **설정창 + ESC 메뉴 = 프리팹 하나, DDOL 하나.** 평소엔 내용만 꺼 두고 참조를 유지하다가 입력이 오면 켠다.
   설정 값은 이미 정적 Config(`SoundConfig`·`DisplayConfig`·`GraphicsQualityConfig`·`KeyBindingConfig`)가 PlayerPrefs 로 들고 있어 UI 를 영속시켜도 값 문제는 없다.
2. **타이틀 = 모니터(CRT) 안에 그대로 그린다.** 타이틀에 있는 동안만 DDOL 캔버스를 타이틀 UI 카메라(Screen Space-Camera → RT)에 붙이고
   1.4배·Y+60 을 적용한다. 타이틀을 떠나면 Overlay·기본 크기로 되돌린다.
3. **버튼 구성(씬 모드별)**

   | 모드 | ESC 메뉴 | 설정 닫기 |
   |---|---|---|
   | 타이틀 | 없음(옵션 버튼 → 설정만) | 타이틀 메뉴로 |
   | 로비 | 계속 · 설정 · **타이틀로** | 메뉴로 |
   | 인게임(맵) | 계속 · 설정 · **게임 종료** | 메뉴로 |
   | 로딩 · 데모 연결 대기 | 열지 않는다 | — |

   인게임에서 로비·타이틀로 돌아가는 경로는 **없다**(경석: "인게임에서 타이틀로 가는거나 로비로 돌아가는 이런 경우 없음").
   기존 종료 확인 팝업의 바탕화면/로비 선택은 맵에서 **게임 종료 확인 하나**로 줄인다.
4. 나가기 동작의 서버 처리 → 아래 「서버 흐름」 절(조사 결과 반영 후 확정).

## 구조
- 신규 프리팹 `Assets/2.Prefabs/UI/GameMenu.prefab`(이름 가칭)
  - 루트 `GameMenuRoot`(항상 활성 · DDOL · 싱글톤 · Canvas + CanvasScaler 1920×1080 + `GraphicRaycaster` + `TitleScreenRaycaster` — 모드에 따라 한쪽만 켠다)
    - `Content`(켜고 끄는 대상 · `UiModalBlocker` · 전체 Dim)
      - `PauseMenu`(기존 `PauseMenu.prefab` 중첩) · `SettingsBody`(기존 `SettingsBody.prefab` 중첩)
  - 🔴 EventSystem 은 넣지 않는다 — `PersistentEventSystem` 이 다른 EventSystem 의 **GameObject 를 통째로 Destroy** 한다(`PersistentEventSystem.cs:48`). 같은 루트에 두면 메뉴가 같이 지워진다.
- 신규 `GameMenuController`(루트 컴포넌트): 모드(Title/Lobby/InGame/None) · 열림 상태(닫힘/메뉴/설정/종료 확인) · ESC 단일 진입점 · 버튼 → 이벤트(`QuitRequested`, `ToTitleRequested`).
  **서버·씬 전환 처리는 하지 않는다** — 이벤트만 내고 씬 담당(GameManager 등)이 받는다.
- 모드 결정: `SceneManager.activeSceneChanged` + 전환 중 여부로 씬 이름 → 모드. `GameManager.CurrentState` 만으로는 부족하다(DemoConnect 상태가 없다 · `GameManager.cs:11`).
- 생성 보장: `0.BootStrapScene` 에 배치(GameManager 와 같은 경로) + DevBoot 직행 시 없으면 생성. 중복 시 나중 것 파괴(GameManager 패턴 `GameManager.cs:49`).
- 정렬: Overlay 정렬 900 — 캐릭터 선택 190 · 결과 200 위, 로딩 1000 · 연결 안내 30000 아래.

### 타이틀 모드
- `TitleMonitorDisplay.Active` 가 생기면 타이틀 모드: `renderMode = ScreenSpaceCamera`, `worldCamera = TitleMonitorDisplay.UICamera`,
  `TitleScreenRaycaster` 켜고 `GraphicRaycaster` 끔, 배율·오프셋 적용. 사라지면 되돌린다.
- `TitleFlowDirector` 는 `_settingsRoot`(씬 인스턴스) 대신 `GameMenuController.OpenSettings/CloseSettings` 를 부른다.
  배율 필드(`_titleSettingsScale`·`_titleSettingsOffsetY`)는 디렉터에 두고 값만 넘긴다. `InputLocked` 계약 유지(타이틀 ESC 는 디렉터 우선).
- `SettingsBody` 의 `TitleScreenSlider` 는 `TitleMonitorDisplay.Active` 가 없으면 일반 슬라이더처럼 동작 — 그대로 둔다.
- 타이틀 씬의 기존 설정창 인스턴스 · `TitleOptionsPanel` 사용처 제거.

### ESC 우선순위 (진입점 하나 — Update 순서에 기대지 않는다)
1. 전환 중 · 타이틀 연출 잠금 → 무시
2. 키 리바인딩 중 → 리바인딩 취소만
3. 메뉴가 열려 있으면 한 단계 뒤로(드롭다운 → 설정/종료 확인 → 메뉴 → 닫힘)
4. 닫혀 있으면 결과 패널 → 캐릭터 선택(`IsEscapeClaimed` 이미 있음 · `LobbyResultPanel.cs:68`, `CharacterSelectPanel.cs:103`) → 스킬 조준 취소
5. 아무도 안 썼으면 메뉴 열기
- 스킬 조준(`PlayerSkillTargeting.cs:370`)에 같은 프레임 소비 표시를 추가 — 지금은 조준 취소와 메뉴 열기가 한 번에 일어날 수 있다.

### 씬 전환 시 정리
- 전환 시작 때 `Content` 를 실제로 비활성화한다(Canvas 숨김으로 대체 금지) — 해상도 확인 되돌림(`GraphicsSettingsController.cs:52`)·리바인딩 취소(`KeyRebindRow.cs:41`)가 OnDisable 에서 돈다.
- 씬 쪽 구독자는 OnDestroy 에서 반드시 해제(지금 `MapSceneManager.OnDestroy` 는 메뉴 이벤트를 안 푼다 · `MapSceneManager.cs:79`).

## 서버 흐름 (조사 10-22 · 핵심 2건 Claude 코드 대조 확인)
- NetworkManager·`NetworkSessionLauncher`·`DemoSessionDirector`·`NetworkLoadingFlowController` = `0.BootStrapScene` 한 오브젝트, DDOL. GameManager 도 BootStrap · DDOL.
  → **`1.TitleScene` 만 다시 로드하면 중복 없음. `0.BootStrapScene` 재로드는 NetworkManager 2개 — 금지.**
- 정상 흐름: 타이틀 START → `DemoSessionDirector.BeginFromTitle`(`_begun=true`) → 대기 씬 → 호스트 `StartHost` 후 NGO 로 로비 Single 로드 / 클라 `StartClient` 후 씬 동기화.
- **맵에서 호스트 종료** → 클라는 `ReconnectLoop` 가 Connected→Dropped → **6.DemoConnectScene(호스트 대기 중)** 로 가서 3초마다 재접속. 호스트가 다시 켜고 START 하면 전원 로비 자동 재입장. 판 진행은 사라진다(명단이 호스트 메모리).
- **맵에서 클라 종료** → 게임 계속. PlayerObject 디스폰, 같은 playerId 면 재합류 가능.
- 데모 흐름 밖(Dev 부팅·태그 없는 MPPM)에서 호스트가 나가면 클라는 그 씬에 남는다 — 기존 동작, 이번 범위 밖.

### 🔴 "타이틀로"를 그냥 Shutdown + 타이틀 로드로 만들면 깨진다
- 데모 흐름에 **의도적 이탈과 끊김을 가르는 장치가 없다.**
  - 호스트: `OnServerStopped` → 재호스팅 대기 → 3초 뒤 `StartHost` + NGO 로비 Single 로드가 **타이틀을 덮는다.**
  - 클라: Connected→Dropped → 대기 씬이 **타이틀을 덮고** 재접속 반복.
- 타이틀에 돌아와 START 를 눌러도 `_begun=true` 라 `ResolveTitleStart` = `Ignore`(`DemoSessionRules.cs:63`) → **아무 일도 안 일어난다.**

### 그래서 추가하는 것 (은희 영역 — 최소 변경 + handoff)
- `ReconnectLoop.Reset()`: `State=Idle`·시도 횟수·사유 초기화. Idle 이면 `OnStopped` 가 `None` 을 돌려준다(`ReconnectLoop.cs:135`) → 끊김 처리가 자연히 막힌다.
- `DemoSessionDirector.LeaveToTitle()`: 루프 Reset · `_begun=false` · 대기 플래그 해제 · `Time.timeScale=1` · `SessionResult.Clear()` · 오버레이 숨김 · `Shutdown()` →
  **`!IsListening && !ShutdownInProgress` 를 기다린 뒤** `1.TitleScene` 로컬 Single 로드. 데모 흐름이 꺼져 있으면 Shutdown → 대기 → 타이틀 로드만.
- 순수 규칙(Reset 후 OnStopped=None, 재진입 START=BeginDemoFlow)은 EditMode 테스트.
- 메뉴는 `ToTitleRequested` 만 낸다 → GameManager(또는 런처)가 `LeaveToTitle` 을 부른다.

### 결정 (grill 2026-10-22 2차, 경석)
- **로비 "타이틀로"는 호스트도 누를 수 있다.** 호스트에게만 확인 팝업 — "다른 플레이어도 연결이 끊깁니다"(누르면 클라 전원이 "호스트 대기 중"으로 간다 · 끊김과 같은 경로). 클라는 확인 없이 바로.
- **맵 "게임 종료"는 모두 확인 팝업.** 호스트에게는 "다른 플레이어의 게임도 끝납니다" 문구를 더한다. 동작 = 기존 `QuitApplication` 패턴(에디터 `isPlaying=false` / 빌드 `Application.Quit`).
- 새 문구는 Strings 시트 키(ko·en)로 — `GameData.xlsx` 는 SVN, 병합 Export 금지(Map 엘리베이터 열 부활).

### 정리(죽은 경로)
- `4.MapScene` ExitButton(`m_IsActive: 0`)·클라 경고창(`WarnningMessage_Panel`)·`MapSceneManager.ConfirmClientExit/CancelClientExit` — 도달 불가. `GoToResult` 는 서버 경로(전멸·보스 처치)에서 쓰므로 **남긴다.**
- `PauseMenuView.ExitToDesktopRequested/ExitToLobbyRequested` — 구독자 0. 새 메뉴의 버튼 구성으로 교체.

## 작업 순서
1. 프리팹 조립(저작 도구 `GameMenuAuthoring` — 기존 `PauseMenuAuthoring`·`SettingsBodyPrefabAuthoring` 이 옛 구조를 되살리지 않게 함께 수정)
2. `GameMenuController` + 모드 전환 + 부트스트랩 배치
3. 맵 이관: `MapSceneManager` 의 ESC·패널 처리 제거, `4.MapScene` `OptionPanel` 제거
4. 로비 연결: 타이틀로 버튼 + ESC 우선순위 + `DemoSessionDirector.LeaveToTitle`·`ReconnectLoop.Reset`(+테스트)
5. 타이틀 이관: 모니터 모드 + `TitleFlowDirector` 연결 변경, 씬 인스턴스 제거
6. 문서: `Docs/tech/` 설정창 절 갱신 + 로비 씬(은희 영역) 변경을 `Docs/tech/handoff-game-menu.md` 로 공유

## 리스크
- 타이틀 모니터 RT 좌표 변환 — 슬라이더 드래그·드롭다운 목록·바깥 클릭이 어긋날 수 있다.
- 씬 YAML 대량 변경(타이틀·맵·부트스트랩) — 🔴 Unity 를 끄고 작업해야 하는 범위인지 단계마다 판단(CLAUDE.md 6).
- 로비 씬은 은희 영역 — 로비 씬 자체는 건드리지 않는 방향(DDOL 이 덮으므로 씬 배치 불필요).

## 검증
- 컴파일 0 · EditMode 전체 통과 · 모드 판정·ESC 우선순위는 순수 규칙 클래스로 빼서 EditMode 테스트.
- Play(솔로): 타이틀 설정(모니터 안·확대·슬라이더·드롭다운) → 로비 ESC(계속·설정·타이틀로, 캐릭터 선택/결과 패널 열린 채 ESC) → 맵 ESC(계속·설정·게임 종료, 조준 중 ESC) → 로비 → 타이틀 반복 후 메뉴 인스턴스 1개.
- 해상도 확인 중·리바인딩 중 씬 전환 → 되돌림·취소.
- MPPM 2인: 각자 메뉴 독립, 한 명이 메뉴를 열어도 다른 사람·보스 진행 무영향.
