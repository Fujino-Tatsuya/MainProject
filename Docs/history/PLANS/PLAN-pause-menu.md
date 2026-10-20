# PLAN-pause-menu.md — 인게임 ESC = 일시정지 메뉴 → 설정 / 그만두기 → 종료 확인 (SVN r466 아트)

> 상태: **✅ 승인(경석 10-20, 문구 초안 그대로) · ✅ 구현 · ✅ 경석 Play** · 커밋(git · SVN r477) · 2026-10-20 · 작업자: 경석(Claude) · 브랜치 `feature/Boss23`
> 선행: [PLAN-settings-art.md](PLAN-settings-art.md)(설정창 새 아트 — 71c8599a · SVN r476). 아트 = SVN `UI/UI_Panel` 의 03_PauseMenu · 02_ExitConfirm.

## 0. 현재 (10-20 확인)
- 맵 ESC(`MapSceneManager.Update`) = `OptionPanel` 토글. 그 안에 설정창(`SettingsBody`) + 나가기 버튼(71c8599a 에서 제목줄 빨간 알약으로 옮김) + 클라 경고창.
- 나가기 = `GameManager.GoToResultButton` → `MapSceneManager.GoToResult`: 호스트 = 전원 결과 화면(중도 종료 집계), 클라 = 경고창 → 게임 종료.

## 1. 결정 (경석 10-20)
| # | 결정 |
|---|---|
| P1 | ESC → **일시정지 메뉴**(메뉴 / 계속하기 · 설정 · 그만두기 + 아래 "Press Any Button" 캡슐 — 목업 그대로) |
| P2 | 계속하기 = 메뉴 창만 닫기(게임 계속) · 설정 = 설정창 열기, **설정창을 닫으면(X·ESC) 메뉴로 복귀** · 그만두기 = 종료 확인 팝업 |
| P3 | 종료 팝업 = "바탕화면 / 로비" 중 하나를 고르고 ✓ · ✗ = 메뉴로 복귀. 고른 쪽 = **흰 글자 + 테두리**, 처음엔 아무것도 안 고름 → ✓ 비활성 |
| P4 | 🔴 **로비로 나가기·게임 종료의 실제 처리와 예외(호스트/클라)는 범위 밖** — 은희가 만들어 연결한다. 이 작업은 버튼과 연결 지점(이벤트)만 둔다. (참고: 호스트 = 전원 로비 복귀, 클라 = 혼자 나가 세션 구성 화면) |
| P5 | 문구는 전부 **Strings 시트 키**로(번역 연결) — 키 등록 + 병합 Export + **en 칸까지** 채움 |
| P6 | 범위 = 인게임(맵)만. 로비 씬 ESC 는 이번에 안 건드림 |

## 2. 아트 ↔ UI
| 아트 | 붙일 곳 |
|---|---|
| `Panels/panel_pause_menu`(364×696) | 메뉴 패널. 제목 "메뉴" + 버튼 칸 3개는 그림에 포함 |
| `Buttons/button_pill_neutral`(Sliced) ×3 | 계속하기 · 설정 · 그만두기(목업 위치, 글자 회색 C0C4C4) |
| `Buttons/button_pill_neutral`(Sliced, 473 폭) | 아래 "Press Any Button" 캡슐(목업 그대로, 누르면 계속하기와 같음) |
| `Panels/panel_exit_confirm`(842×574) | 종료 팝업. 제목줄 + 밝은 회색 메시지 칸 + 아래 버튼 홈 2칸 포함 |
| `button_pill_neutral` ×2 | 바탕화면 · 로비(선택 토글) |
| `button_pill_red` + `icon_cross_red` / `button_pill_green` + `icon_check_green` | 팝업 밖 아래 ✗ / ✓ |
- 좌표 = 매니페스트 03·02 배치(설정창과 같은 방식 — 패널 중심 기준, +4 여백). 글꼴 = 서울알림체(r476).

## 3. 문구 (Strings 시트 키 — ko / en 초안, 승인 때 고쳐 주세요)
| 키 | ko | en |
|---|---|---|
| `UI.Pause.Title` | 메뉴 | Menu |
| `UI.Pause.Continue` | 계속하기 | Continue |
| `UI.Pause.Settings` | 설정 | Settings |
| `UI.Pause.Quit` | 그만두기 | Quit |
| `UI.Pause.PressAnyButton` | Press Any Button | Press Any Button |
| `UI.Exit.Title` | 게임 종료 | Quit Game |
| `UI.Exit.Message` | 정말 종료하시겠습니까? | Are you sure you want to quit? |
| `UI.Exit.Desktop` | 바탕화면 | Desktop |
| `UI.Exit.Lobby` | 로비 | Lobby |
| `UI.Settings.Title`(10-20 추가, 시트 행 없음) | 설정 | Settings |
- 목업 문구 "종료 팝업"·"진짜로 종료함?"은 자리표시로 보고 위처럼 바꿨다.

## 4. 구현
1. **런타임** `PauseMenuView`(신규): 메뉴·팝업 버튼 연결, 선택 토글 상태, 공개 이벤트 `ExitToDesktopRequested` · `ExitToLobbyRequested`(+ 인스펙터용 UnityEvent 2개 — 은희가 코드나 인스펙터로 연결). 아무것도 연결 안 됐으면 경고 로그만(크래시 없음).
2. **`MapSceneManager`** ESC 상태: 닫힘 → 메뉴 → (설정 | 종료 팝업). ESC = 한 단계 뒤로(설정·팝업 → 메뉴, 메뉴 → 닫힘). 키 변경 중 ESC 무시는 유지. 설정창 X → 메뉴(71c8599a 의 "옵션 닫기" 연결을 바꿈).
3. **프리팹** `Assets/2.Prefabs/UI/PauseMenu.prefab` — 저작 메뉴 `Tools/UI/Pause/일시정지 메뉴 만들기 (멱등)` 로 생성, 맵 `OptionPanel` 에 인스턴스. 71c8599a 에서 제목줄로 옮긴 **나가기 버튼은 끈다**(그만두기가 대신). 클라 경고창·`GoToResult` 코드는 그대로 둔다(은희가 재사용할 수 있게, UI 에서만 안 씀).
4. **문구**: `UiStrings` 에 위 키 + `LocalizedText` 부착 → Unity `Export Template` **병합** → xlsx en 칸 입력 → `StringTable 갱신` → SVN(update → 충돌 0 → 커밋) + git `StringTable.asset`. Export 는 xlsx 서식을 지운다(문서상 정상 동작).
5. **인수인계** `Docs/tech/handoff-pause-menu.md` — 은희용: 이벤트 이름·연결 예시·지금 끊긴 것(호스트 결과 화면 이동이 UI 에서 빠짐).

## 5. 리스크
- **호스트 중도 종료 → 결과 화면** 경로가 UI 에서 빠진다(P4 로 은희가 로비 복귀를 연결할 때까지 그만두기 ✓ 는 아무 일도 안 함). 그 사이 Play 테스트로 게임을 끝낼 방법 = 에디터 정지.
- 병합 Export 는 xlsx 전체를 다시 쓴다 — 다른 사람이 xlsx 를 잠그고 고치는 중이면 충돌. 실행 직전 `svn status -u`·잠금 확인(지금은 잠금 없음).
- UiModalBlocker(입력 막기)가 메뉴·팝업 동안에도 켜져 있는지 — OptionPanel 이 부모라 유지된다.

## 6. 검증
- 컴파일 0 · 설정 EditMode · 데이터 테이블 EditMode(Strings 검증 포함).
- 미리보기 캡처(로컬 도구 확장)로 메뉴·팝업을 목업과 대조.
- Play(경석, 맵): ESC → 메뉴 / 계속하기 / 설정 → X → 메뉴 / 그만두기 → 팝업 → ✗ → 메뉴 / 선택 전 ✓ 비활성 / ESC 단계별 뒤로 / 언어 바꾸면 영어.

## 진행
### 10-20 — 구현 ✅ · 컴파일 0 · 데이터 테이블 239/0 · 로컬라이제이션 150/0 · 설정 26/0 · SVN r477 · ⏳ 맵 Play
- 런타임 `UI/Pause/PauseMenuView`(버튼 → 이벤트, 팝업 선택 상태, 나가기 2개 = 코드 이벤트 + UnityEvent, 미연결이면 경고만) · `MapSceneManager` ESC 단계 전환.
- 저작 `UI/Pause/Editor/PauseMenuAuthoring` → `PauseMenu.prefab` + 맵 OptionPanel 인스턴스 + 나가기 버튼 끔. `SettingsHostSceneAuthoring` 도 나가기 버튼을 옮기지 않고 끄도록 맞춤(오전 결정 ⚠️ 뒤집음).
- 미리보기 캡처로 메뉴·팝업 = 목업 일치 확인. 두 가지 고침:
  - 글자 일부("뉴·속·하")가 가는 폴백으로 나옴 → 서울알림체 에셋이 설정창 글자만 구워져 있었다. 생성 메뉴가 `UiStrings.All` 전체 글자를 굽도록 + 기존 에셋에 **글자만 더하는** 방식으로(guid 유지) → 245 → 288자.
  - 선택 테두리 안 보임 → 어두운 알약을 흰색으로 "칠해" 깔았는데 UI 색은 곱하기라 흰색이 안 된다. 흰 캡슐 테두리 PNG 를 생성(`Textures/PauseSelectRing.png`)해 버튼 위에.
- 🔴 문구: **병합 Export 결과를 버렸다** — Strings 10행은 맞았지만 Map 시트 엘리베이터 16행에 r470(은희)에서 지운 "중첩 전용 Elevator 열"이 되살아남. xlsx 원본 복원 후 Strings 시트 XML 에 10행(ko·en)만 직접 추가 → 다른 시트 변경 0(행 비교로 확인) → StringTable 갱신(키 167). SVN **r477**(xlsx + 폰트 에셋), 핀 477.
- 인수인계 [handoff-pause-menu.md](Docs/tech/handoff-pause-menu.md).
### 10-20 — 경석 Play 후 수정 ✅ · 컴파일 0 · ⏳ 경석 재확인
- 경석 Play: 동작 문제 없음. ① 인게임 ESC 가 게임 화면을 덮지 않고 HUD(스킬 아이콘)가 메뉴 위에 그려짐 ② 타이틀 설정창이 모니터 가운데보다 내려가 보임.
- ① `OptionPanel` 을 화면 전체로 펴고 배경 = 반투명 검정(알파 0.5) + 자체 캔버스 정렬 **1000**(CombatHUD 0 · 튜토리얼 50~60 · 맵 전체 보기 999 위, 드롭다운 목록 30000·성능 표시 아래) + GraphicRaycaster. 원인 = OptionPanel 이 기본 캔버스(정렬 0) 안이라 별도 캔버스 CombatHUD(정렬 0)와 순서가 엉킴.
  - ⚠️ `Canvas.overrideSorting` setter 가 비활성 OptionPanel 에서 저장되지 않음(정렬 1000 만 저장, override 0) → 직렬화 필드로 넣음.
- ② `TitleFlowDirector._titleSettingsOffsetY`(기본 **+60**, 인스펙터 -200~200, Play 중 반영). 값은 스크린샷 비율로 잡은 추정 — 경석 눈으로 확정.
- 경석 재확인(10-20): ESC 반투명 배경·HUD 위 정렬 OK · 타이틀 설정창 위치 OK.
- 추가(경석): 타이틀 모니터 메뉴(START·SETTING·EXIT)가 아래로 쏠림 → 세 버튼 +170(한 칸) 올려 **SETTING = 예전 START 자리**(y 110). `1.TitleScene` 3줄 + 저작 `TitleRigAuthoring.LayoutMonitorMenu` 오프셋 −60 → +110(다시 조립해도 유지). ⏳ 경석 눈 확인.
