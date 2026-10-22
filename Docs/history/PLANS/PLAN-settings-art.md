# PLAN-settings-art.md — 설정창 UI 를 SVN 새 아트(UI_Panel r466)로 교체

> 상태: **✅ 구현 · 경석 타이틀 Play 확인(10-20)** · ⏳ ESC(맵) Play · 미커밋 · 2026-10-20 · 작업자: 경석(Claude) · 브랜치 `feature/Boss23`
> 선행: [PLAN-settings.md](PLAN-settings.md)(설정창 기능 — 10-12~13 완료). 이 계획은 **겉모습만** 바꾼다. 저장·적용 계층(`*Config`)과 컨트롤러 로직은 건드리지 않는다.
> 아트: SVN `4_Resources/Art/UI/UI_Panel/`(이지원 r466, 목업 `Mockups~` 는 r467). 납품 README·`asset_manifest.json` 이 배치 원본.

## 0. 현재 상태 (10-20 확인)
- SVN: 로컬 **r457** · 원격 **r474** · 핀 457 · 로컬 수정 0 · 잠금 0(이 사본). UI_Panel 은 로컬에 없다 → **update 필요(Unity 종료 후)**.
- 설정창 = `Assets/2.Prefabs/UI/SettingsBody.prefab` — 탭 4개(게임플레이·그래픽·조작·오디오) + 내용 4개(710×540). **틀이 없다** — 틀·제목·닫기는 씬 쪽(`4.MapScene` `OptionPanel`, 타이틀 `TitleOptionsPanel`)에 있다.
- 지금 스킨 = `SettingsDarkTheme`(어두운 단색, 저작 메뉴 끝에서 자동 적용).
- 타이틀은 설정창을 RT 에 그려 3D 모니터에 다시 찍는다(`TitleFlowDirector.ScaleSettingsBody` 1.4배, CRT 감소율 적용).

## 1. 결정 (경석 10-20 grill)
| # | 결정 |
|---|---|
| A1 | **틀까지 공용 프리팹에 넣는다** — `panel_settings`·닫기 버튼·제목을 `SettingsBody` 안에. 타이틀·ESC 가 같은 모습. 씬의 옛 배경·제목·닫기는 끈다(삭제 아님). ESC 의 **나가기 버튼·경고창은 그대로** |
| A2 | 행이 많은 탭은 **스크롤** — 목업 크기 행(높이 57) 유지, 내용 영역 = ScrollRect + 새 `scrollbar_track/handle` |
| A3 | 토글(VSync·백그라운드 음소거·블룸·Hold·대시 방향) = `dropdown_button_base` 위에 `icon_check_green`(켬) / `icon_cross_red`(끔) |
| A4 | 폰트 = 아트 원본 **SeoulAlrimTTF-Heavy** 로 TMP 폰트 에셋을 만들어 쓴다. ⚠️ **SVN·레포 어디에도 폰트 파일이 없다**(SVN 7,430개 전수 확인, README 도 "미포함") → 파일 확보 방법은 §4 질문 |

## 2. 아트 ↔ UI 대응
| 아트(Sprites/…) | 붙일 곳 | 이미지 타입 |
|---|---|---|
| `Panels/panel_settings` | `SettingsBody` 배경(새 `Frame`) — 탭 4칸·내용 영역이 그림에 포함 | Simple(늘리지 않음) |
| `Buttons/button_close_base` + `Icons/icon_cross_red` | 새 닫기 버튼(우상단) | Simple |
| 제목 자리 | "설정 / Settings" TMP(Loc 키 추가) | — |
| `Buttons/button_pill_neutral` | **선택된 탭** 강조. 비선택 3칸은 그림 위 글자만 | Sliced |
| `Settings/settings_label_capsule` | 각 행 왼쪽 라벨 | Sliced |
| `Settings/dropdown_capsule` + `dropdown_button_base` + `Icons/icon_chevron_down` | `SettingsDropdown` 닫힘(펼치면 화살표 Z 180°) | Sliced / Simple |
| `Settings/dropdown_panel_open` + `Controls/scrollbar_*` | 드롭다운 펼친 목록 | Sliced |
| `Controls/slider_track/fill/handle` + 값 TMP("100%") | 볼륨 3·흔들림·비네트 2 | Sliced / Filled(Horizontal) / Simple |
| `Buttons/button_pill_neutral` | 해상도 적용·키 초기화·키 변경 칸 | Sliced |
| `Buttons/button_pill_green` + `icon_check_green` / `button_pill_red` + `icon_cross_red` | 해상도 확인 창 유지 / 되돌리기 | Sliced |
| `Controls/scrollbar_*` | 내용 영역 스크롤(A2) | Sliced |
- 크기: 납품 기준 1920×1080 · Reference PPU 100 · 스프라이트 PPU 200 → **RectTransform = 매니페스트 rect(논리 크기)**. 위치도 매니페스트 rect 를 패널 기준 좌표로 환산.
- hover/pressed 아트 없음 → Button ColorTint(살짝 밝게/어둡게)로 대신.
- 쓰지 않는 납품물: Popup·ExitConfirm·PauseMenu·BattleResults 패널(다른 화면 — 범위 밖).

## 3. 구현
1. **SVN update → r474**(Unity 종료 상태), 충돌 0 확인, 새 스프라이트 `.meta` guid = 매니페스트 guid 대조. `art-svn.json` 핀 457 → 474.
2. **런타임** `SettingsBodyView`(신규, 작은 컴포넌트): 닫기 버튼 → `CloseRequested` 이벤트, 탭 선택 강조 이동. 타이틀(`TitleFlowDirector`)·ESC(`MapSceneManager.CloseOptionPanel`)가 구독. 토글 아이콘 전환(`SettingsToggleIcon` — Toggle.isOn → 체크/X).
3. **저작 메뉴** `Tools/UI/Settings/설정창 새 아트 스킨 적용 (멱등)` — 프리팹을 `LoadPrefabContents` 로 열어 틀·탭·행·컨트롤 스프라이트와 크기·위치를 매니페스트 값으로 배치, 내용 영역에 ScrollRect. 기존 항목 배치 메뉴 끝의 `SettingsDarkTheme` 호출은 새 스킨으로 바꾼다(다시 돌려도 새 아트 유지).
4. **씬**(2개, 텍스트 에셋 한두 개 수준): 맵 `OptionPanel` 의 옛 배경 Image·`Options_Title`·`OptionPanel_Close` 끔, 타이틀 `TitleOptionsPanel` 의 옛 틀 끔. 타이틀 배율 1.4 는 새 틀(1469×957)이 모니터 RT(1536×1080)에 들어가도록 다시 잡는다(Play 확인 후 확정 — 인스펙터 값).
5. **폰트**(§4 답에 따라): TTF → TMP Font Asset(Dynamic, 한글 포함) → 설정창 TMP 전부 교체. 폰트가 늦어지면 1~4 를 NotoSansKR 로 먼저 끝내고 폰트만 나중에 바꾼다.
6. 문서: `Docs/tech/` 설정창 문서(또는 이 PLAN 진행란)·`CONTEXT.md`.

## 4. 열린 질문 (승인 때 같이)
- **폰트 파일 확보**: SeoulAlrim(서울알림체)은 서울시 무료 배포 폰트(상업 이용 가능으로 알려짐 — 라이선스 원문 확인 필요). ① 경석/이지원 님이 TTF 를 SVN 에 올려 준다 ② 내가 서울시 공식 배포처에서 받는다(다운로드 승인 필요) 중 선택.
- 폰트 파일 VCS: TTF·Font Asset 은 크기가 커서 **SVN(아트)** 이 맞아 보인다. Font Asset(.asset)은 git 이 참조하므로 guid 를 SVN 에 둘지 git 에 둘지 — 기본안 = TTF·Font Asset 모두 SVN `UI/Fonts/`.

## 5. 리스크
- 타이틀 3D 모니터·CRT: 새 아트의 밝은 회색 내용 영역(C8C6C2)이 CRT 글로우에서 번질 수 있다(10-13 흰 컨트롤 번짐과 같은 뿌리). 타이틀 Play 로 확인, 번지면 감소율로 조절.
- 그래픽 12줄 스크롤 + 드롭다운 펼침: 펼친 목록이 ScrollRect 마스크에 잘리지 않는지(TMP_Dropdown 목록은 별도 캔버스라 보통 안 잘림) 확인.
- `MonitorAwareRaycaster`: 새 스크롤·닫기 버튼도 타이틀 RT 좌표에서 클릭되는지.
- r458 캐릭터 선택 아트 이동(옛 스프라이트 → `legacy/`): guid 가 같이 옮겨졌는지 update 후 캐릭터 선택 씬 참조 깨짐 검사.
- 은희 님 영역 겹침 없음(설정창은 경석 10-12 작업). `4.MapScene` 은 공용 씬 — 변경 = OptionPanel 하위 활성 플래그 3개만.

## 6. 검증
- 컴파일 `error CS` 0 · 설정 EditMode(`Tools/Tests/설정창 EditMode 테스트 실행`) 전부 통과 · `PlayerAnimationEventRequirementTests`(r462 반영으로 통과 기대).
- Play: 타이틀 설정창·ESC 설정창 양쪽 — 탭 전환 강조, 드롭다운 펼침·선택, 슬라이더·% 표시, 토글 아이콘, 스크롤, 닫기 버튼, 해상도 확인 창. 스크린샷으로 목업과 대조.
- SVN: update 후 `svn status` 충돌 0, 스프라이트 guid = 매니페스트, `python Docs/tech/check-environment.py`.

## 진행
### 10-20 — 승인(경석) · 폰트 확보 · 스크립트 ✅ · 컴파일 error CS 0 · ⏳ Unity 종료 후 SVN update
- 폰트: 서울시 공식 배포처 `seoul.go.kr/upload/seoul/font/seoul_font3.zip`(50MB, 서울서체 3종)에서 `SeoulAlrimTTF-Heavy.ttf`(2.5MB)만 꺼냄.
  이용 조건(배포 페이지 원문 요지) = **누구나 무료·자유 사용, 판매·유상양도·무단배포 금지, 공공누리 제1유형(출처 표시)** → 게임 내장 가능, **크레딧에 출처 표기 필요**. 팀 SVN 보관은 내부 공유로 판단.
- 런타임 신규: `SettingsBodyView`(선택 탭 알약·닫기 버튼) · `SettingsToggleIcon`(체크/X — `SetIsOnWithoutNotify` 때문에 매 프레임 비교) · `SettingsSliderPercent`(볼륨 % 표시).
- 연결: `TitleOptionsPanel.ShowOnly` → `SettingsBodyView.Refresh` · `TitleFlowDirector`·`MapSceneManager` 가 틀 닫기 버튼에 각자 닫기 연결.
- 저작: `SettingsArtSkin`(틀·탭·내용 카드·스크롤·컨트롤 그림·글꼴, guid 로 스프라이트 로드 — 없으면 아무것도 안 바꿈) · `SettingsPanelAuthoring` 행 크기 목업 기준(라벨 278·컨트롤 557·행 61·간격 37), 기존 볼륨·토글 행도 새 행 모양으로 · `SettingsFontAssetAuthoring`(정적 에셋 + NotoSansKR 폴백).
- `SettingsDarkTheme` 삭제(새 아트가 대신 — 호출부 1곳뿐이었음). 문구 키 `UI.Settings.Title`(설정/Settings) 추가 → 테스트 57→58. **Strings 시트 행은 아직 없음**(ko 폴백으로 보임 — xlsx 는 다른 사본이 잠금 중).

### 10-20 — SVN update · 적용 ✅ · 경석 타이틀 Play 확인 · EditMode 설정 26/0 · 플레이어 508/0
- SVN r457 → **r475**(update 중 r475 기획 문서가 새로 올라옴), 충돌 0. UI_Panel 스프라이트 27개 guid = 매니페스트 전부 일치. r458 캐릭터 선택 옛 스프라이트는 `legacy/` 로 guid 그대로 이동(참조 안전). 핀 457 → 475 · metaFiles 3304. `check-environment.py` 전부 통과.
- ⚠️ 은희 r465 `UI/HUD/temp_stunIcon.png` 는 `.meta` 없이 올라옴 → 받는 사람마다 guid 가 다르게 생성된다(이번 작업 무관, 손대지 않음).
- 폰트 에셋 `Assets/50.Art/UI/Fonts/SeoulAlrimTTF-Heavy SDF.asset`: 요청 245자 전부 서체에 있음, 아틀라스 1장, 폴백 NotoSansKR. → **SVN r476 커밋**(10-20), 핀 476.
- `설정 패널 항목 배치` 메뉴 → 프리팹 저장(경고 0). 오프스크린 캡처(`(확인) 설정창 미리보기 캡처`, 로컬 도구 `SettingsPreviewCapture.cs`)로 탭 4개·스크롤 끝 확인 — 목업과 일치.
- 씬: 신규 메뉴 `설정창 옛 틀 끄기 — 타이틀·ESC (멱등)`(`SettingsHostSceneAuthoring`). 맵 = OptionPanel 배경 투명(클릭 차단 유지)·옛 제목/"Option"/X 끔·**나가기 = 제목줄 오른쪽 빨간 알약**(경석 10-20 선택, 버려진 안: ESC 만 0.85배 축소 후 틀 아래 / 현 위치 유지). 타이틀 = 옛 Close_Button·Options_Title 끔·배율 1.4 → 1.0.
- 경석 타이틀 Play(10-20): "제대로 잘 나옴" — 탭 4개·드롭다운 값·토글 체크/X·키 이름·볼륨 % 확인.
- 남은 것: ESC(맵) Play 확인 · Strings 시트에 `UI.Settings.Title` 행(xlsx 잠금 풀린 뒤) · 크레딧에 서울알림체 출처 표기 · ✅ 커밋(SVN r476 · git).
