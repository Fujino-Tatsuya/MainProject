# PLAN — 타이틀 시작 연출 단순화 · ESC 메뉴 Press 캡슐 이동 · 설정 탭 모양 (2026-10-26)

> 상태: ✅ 경석 승인(10-26, 「열린 것」 모두 확정) · ✅ 구현(미커밋) · 컴파일 0 · EditMode 3159/4(무관 데칼 4) · ⏳ 경석 Play · 작업자 경석(Claude) · 브랜치 `feature/SettingsDdol` 작업 트리
> 요구 원문(경석 10-26): "설정창 press any button 이거 빠져야하고 이 이미지는 타이틀에 텍스트로 써둔거 대신 나와야함 …
> 설정 버튼 활성화 된 거는 어둡게. 나머지 클릭 가능한건 밝게. 그리고 4개 버튼 다 테두리 … 시작 연출. press any button만 있어 …
> 타이틀에서는 설정이랑 나가기 없앨거임. 어차피 로비에 있으니까."

## 확정(grill 10-26)

| # | 질문 | 경석 답 |
|---|---|---|
| 1 | 설정 탭 4개 모양 | **B안** — 넷 다 같은 알약(테두리 있음) + 흰 글자, **선택된 탭만 색을 곱해 더 어둡게 + 흐린 글자** |
| 2 | 빼는 'Press Any Button' | **ESC 메뉴(계속하기·설정·그만두기 아래 캡슐)** 의 것. 타이틀은 그 캡슐 **이미지**를 'PRESS ANY KEY' 글자 대신 쓴다 |
| 3 | 타이틀 메뉴 | **설정·나가기 삭제**(시작하기 버튼도 없다 — 화면엔 Press 캡슐만). 설정·종료는 로비 ESC 메뉴로 |
| 4 | 시작 연출 | 아무 키 → 카메라가 모니터로 다가감(3초) → 모니터 CRT 꺼짐(1.1초) → 검정 페이드 → 로비 페이드 인. **ESC 건너뛰기 = 바로 검정 페이드 → 로비** |

## 바꾸는 것

### A. 설정 탭(SettingsBody — 타이틀이 빠지므로 로비·인게임 ESC 설정창에 보인다)
- `SettingsBodyView.Refresh()`: 비선택 탭 배경 `Color.clear` → **흰색(원래 알약)**, 선택 탭 배경 → **곱하기 색(시작값 0.55 회색)**.
  글자: 비선택 = 흰색, 선택 = 흐린 회색. 세 색은 인스펙터 필드(`selectedTabTint`·`selectedLabelColor`·`normalLabelColor`)로 — 프리팹 값을 저작 메뉴가 새로 채운다.
- 클래스 주석의 "선택된 탭만 알약" 설명 갱신.
- ⚠️ 탭 Button 의 ColorTint(hover·pressed)는 그대로 둔다 — Image.color 와 따로 곱해진다.

### B. ESC 메뉴에서 Press 캡슐 삭제
- `PauseMenu.prefab` 의 `PressAnyButton` 오브젝트 삭제, `PauseMenuView.pressAnyButton` 필드·바인딩 삭제.
- `PauseMenuAuthoring.cs` 에서 캡슐 생성 줄 삭제(다시 돌려도 안 생기게).
- 문자열 키 `UI.Pause.PressAnyButton` 은 **남겨서 타이틀 캡슐 글자로 재사용**(같은 원문 — xlsx·SVN 변경 없음). 설명 문구만 "타이틀 Press 캡슐"로 바꾼다.

### C. 타이틀 화면 — Press 캡슐 이미지
- **위치 = 1안(경석 10-26 3차): 지금 PRESS ANY KEY 자리(화면 앞 하단, 모니터 밖)**.
- 지금의 CRT 글자 경로(`TitlePressCam` → 소형 RT → `PressAnyKey_Screen` 의 `TitleOverlayCrtImage` + `Title/CRTUI`)는 **그대로 쓰고**,
  Press 캔버스 안의 `PressAnyKey_Text` 만 **알약 Image(`button_pill_neutral`, 9-slice) + 글자 "Press Any Button"(서울알림 Heavy, 흰색)** 캡슐로 바꾼다 — ESC 메뉴 캡슐과 같은 모양.
- 움직임: **지금 CRT 화면 효과의 지직거림 + 깜빡임만**(경석: "그 외 아무것도 안 넣어도 됨") — 글자 뒤섞임(`TextScramble`)은 뺀다. 깜빡임이 약하면 셰이더 밝기 변조 값을 올려 Play 로 맞춘다.
- 클릭 대상 아님(raycast 끔) — 입력은 지금처럼 아무 키·좌클릭·패드 A/Start.
- `TitlePressCam` 은 모니터 UI 카메라와 별개라 남는다.

### D. 타이틀 흐름 단순화(`TitleFlowDirector`)
- 상태: `Idle → Approaching → Starting` 만. `Menu`·`Settings`·`Exiting` 과 메뉴 버튼·설정창 열기·선택 복구 코드 삭제.
- 접근 완료(블렌드 끝) → 곧바로 `Starting`: `TitleMonitorDisplay.PowerOff(1.1초)`(로고가 꺼짐) → `TitleSceneManager.StartGame()`(1.5초 검정 페이드 → 로비, 로비는 진입 시 페이드 인 — 기존 그대로).
- 접근 중 ESC → 블렌드 끊고 **꺼짐 없이 바로 `StartGame()`**(검정 페이드 → 로비).
- 카메라는 꺼짐·페이드 동안 Near 에 그대로(다이브 없음). `TitleStartDive` 는 더 쓰지 않는다 → 스크립트·씬 컴포넌트 삭제. `TitlePowerOff`(전체 화면 꺼짐)는 Exit·폴백 전용이었으므로 씬에서 뺀다 — 🔸 「열린 것」 2.
- 씬 `1.TitleScene`: `Menu_Root`(Start/Option/Exit 버튼) 삭제, `TitleFlowDirector` 의 끊긴 참조 정리. `TitleSceneManager` 의 옛 버튼 참조·ESC 옵션 토글 경로가 null 에 안전한지 확인(지금도 `HasFlowDirector` 면 ESC 무시).
- **모니터 UI 카메라·타이틀용 설정창 경로 삭제(경석 10-26 2차 — 범위에 넣음)**: `TitleUICam`·모니터 UI 캔버스·UI RT,
  `TitleMonitorDisplay.ShowUI`·화면 클릭 변환(`TryScreenToUIPixel`), `TitleScreenRaycaster`·`TitleScreenSlider`·`TitleScreenFeed`(쓰는 곳이 이 경로뿐이면),
  `GameMenuController` 의 Title 모드 설정창(`OpenTitleSettings`·`CloseTitleSettings`·`ApplyTitleSettingsLayout`·`BindTitleCanvas`·`titleRaycaster`),
  `MonitorAwareRaycaster`·`SettingsDropdown` 의 타이틀 모니터 분기. 원칙: **이 경로에서만 쓰이는 것만** 지우고, 한 단계마다 컴파일로 확인.
  ⚠️ `GraphicsQualityConfig`·`TitleCrtFx` 는 다른 일도 하므로 타이틀 모니터 참조 줄만 정리한다.
  ⚠️ 저작 메뉴 `GameMenuAuthoring`·`SettingsPanelAuthoring`·`TitleRigAuthoring` 에 남는 참조도 함께 정리(컴파일 유지).
  10-25 아트의 카메라 3대 AA 문제 중 `TitleUICam` 몫은 카메라가 없어져 사라진다.
- `TitleRigAuthoring`(옛 리그 저작 메뉴)는 이미 구도 상수가 낡았고, 다시 돌리면 메뉴·옛 Press 글자를 되살린다 → 메뉴 실행 시 경고 대화상자 한 줄 추가(실행은 막지 않음).

## 수정 예정 파일
- 코드: `Settings/UI/SettingsBodyView.cs` · `Settings/Editor/SettingsArtSkin.cs`(필요 시) · `UI/Pause/PauseMenuView.cs` · `UI/Pause/Editor/PauseMenuAuthoring.cs` · `UI/Title/TitleFlowDirector.cs` · `Managers/TitleSceneManager.cs`(null 안전만) · `Localization/UiStrings.cs`(설명) · `UI/Title/Editor/TitleRigAuthoring.cs`(경고) · 삭제 `UI/Title/TitleStartDive.cs`
- 에셋: `2.Prefabs/UI/SettingsBody.prefab`(탭 색) · `2.Prefabs/UI/PauseMenu.prefab` · `0.Scenes/MainFlow/1.TitleScene.unity`
- SVN 아트 변경 없음.
- 🔴 손대지 않음: 다른 세션 미커밋 파일(`TwentyThreeBoss.cs`·`No23Controller.controller`·`0.BootStrapScene.unity`·`AddressableAssetSettings.asset`·`handoff-enemy-indicator.md`·`GroupPainter/*`·미추적 `Dev/Editor/*`·`SettingsPreviewCapture.cs`).

## Unity 켜둔 채로 가능한가
씬 1개·프리팹 2개(git 텍스트 에셋) + `.cs` — CLAUDE.md §6 "켜둔 채 OK". 편집은 에디터 안(MCP·저작 메뉴)에서 하고, 끝나면 guid 유지·재직렬화 범위를 diff 로 확인한다. 디렉터리 이동·패키지·SVN 아트 없음.

## 검증
1. 컴파일 error 0.
2. EditMode: 설정·GameMenu·로컬라이제이션 묶음(`GameMenuRulesTests` 등) 통과. 문자열 표 검사(`UiStrings.All` ↔ StringTable)가 키 유지로 깨지지 않는지.
3. 설정창 미리보기 캡처(`Tools/UI/Settings/(확인) 설정창 미리보기 캡처`)로 탭 4개 B안 확인 → 경석에게 이미지 전달.
4. Play(0.BootStrapScene 시작):
   - 타이틀 = Press 캡슐만 보임 · 아무 키 → 3초 접근 → 로고가 CRT 꺼짐 → 검정 페이드 → 로비 페이드 인.
   - 접근 중 ESC → 꺼짐 없이 바로 검정 페이드 → 로비.
   - 로비 ESC 메뉴: 캡슐 없음, 계속하기·설정·그만두기 정상. 설정 → 탭 전환 시 선택 탭만 어둡게.
   - 인게임(맵) ESC 메뉴 같은 확인. 그만두기 → 타이틀 복귀 뒤 다시 시작 정상.
   - 부스 데모 모드(`demo-connection.json`) 시작도 `StartGame` 경로 그대로인지(코드 경로 동일 — 로그로 확인).
5. 경석 Play 확인 → `CONTEXT.md` 갱신. 커밋은 경석 요청 시.

## 열린 것(승인 때 같이 답 주세요 — 기본값으로 진행)
1. ~~타이틀 Press 캡슐 움직임~~ → ✅ 1안(화면 앞 하단) + 지금 CRT 효과의 지직거림·깜빡임(10-26 3차).
2. **타이틀 옛 스크립트 삭제**: 기본 = `TitleStartDive` 만 파일 삭제. `TitlePowerOff`·`TitleOverlayCrtImage` 는 옛 저작 메뉴(`TitleRigAuthoring`)가 참조해 지우면 컴파일이 깨지므로 **파일은 남기고 씬에서만 뺀다**. (대안: 저작 메뉴까지 정리해 셋 다 삭제 — 범위가 커짐)

## 후속(이번 범위 밖)
- (없음 — 모니터 UI RT·설정창 타이틀 배치 정리는 10-26 2차로 본 범위에 넣었다)

## 추가 (10-26 경석 6차) — 설정창 안 컨트롤 반응 통일 + 클릭 소리
Codex 설계 회의(읽기 전용, 결과는 세션 scratchpad `codex_ui_out.txt`) 후 경석 확정.
- **버튼형**(키 변경·초기화·언어·적용·토글·드롭다운 머리·닫기 X·슬라이더 손잡이·스크롤바 손잡이): 고름(마우스 올림·키보드/패드 선택) = 그림 ×0.55 + 버튼 안 글자 회색 / 누름 ×0.45 / 비활성이 최우선(반투명, 회색 hover 와 다르게). 글자 없는 토글·손잡이·X 는 밝기만. **자식 아이콘(✓/✗·X·드롭다운 화살표)도 같이 어둡게.**
- 예외: 키 변경 "입력 대기" 동안 그 줄만 고름 고정(완료·취소·패널 닫기에서 해제) · 드롭다운 머리는 목록이 열린 동안 고름 고정 · 토글 켬/끔은 ✓/✗ 로만(밝기와 무관) · 설정 탭은 `SettingsBodyView` 규칙 그대로(두 번 곱하기 방지).
- **드롭다운 목록 항목**: 올린 줄 = 띠(흰색 0.14, highlighted·selected 둘 다 — 지금 selected 0.08 이라 0.08 로 보이던 것 수정) + **글자 회색**(경석 — Codex 는 흰 글자 권고, 경석이 Claude 안 선택) · 누름 0.24 · 현재 값 ✓.
- **클릭 소리 `UI_Click`**: 컨트롤마다 재생 지점 하나 — Button = onClick · Toggle = onValueChanged(저장값 복원은 SetIsOnWithoutNotify 라 무음) · 드롭다운 = 목록 생성 시 1회 + 항목 클릭/Submit 1회(항목 Toggle·부모 onValueChanged 에는 안 건다) · 슬라이더 = 누르는 순간 1회(드래그·값 변경 무음) · 키 변경 = 입력 대기 시작 1회(대기 중 재클릭 무음) · 스크롤바 무음. AudioManager 없으면 무음.
- 클릭 뒤 고름 표시가 남는 것은 지금처럼 둔다(경석 — 키보드·패드 조작 대상 유지).
- 구현: 런타임에 `GameMenuController` 가 메뉴·설정창 전체를 한 번 훑어 반응·소리 컴포넌트를 붙인다(전체 재저작이 줄을 지웠다 다시 만들어도 살아남게). 저작 `SettingsArtSkin.Tint`·드롭다운 항목 기본값도 같은 값으로.
- 7차(경석 Play): ⚠️ 뒤집음 — 드롭다운 목록 글자는 **흰색 유지**(띠가 있으니 — 결과적으로 Codex 권고안). 토글은 마우스로 누르고 떼면 선택을 풀어, 올려 둔 동안·누르는 동안만 어둡다(클릭 뒤 다른 곳을 누를 때까지 어둡게 남던 것 — 원인은 bool 이 아니라 EventSystem 선택이 남은 것). 키보드·패드 선택은 그대로.
- 8차(경석 Play): 같은 "클릭 뒤 어둡게 남음"을 버튼(언어·적용·초기화·키 변경)·드롭다운 머리·슬라이더·스크롤바까지 — 마우스로 떼면 선택 해제. 드롭다운은 TMP 가 닫으며 머리를 다시 Select() 하므로 마우스로 닫은 프레임 끝에 해제(Esc·Enter 로 닫으면 유지). 키 변경은 입력 완료·취소 때 고정을 풀어 밝은 상태로.
