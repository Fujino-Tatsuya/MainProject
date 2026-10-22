# handoff — 설정창 언어 → 로컬라이징(Loc) 통합 (할 일 38)

> 대상: 경석(설정창 담당). 작업: 은희 승인, 2026-10-13. 브랜치 `feature/SettingsLocalization`(기준 `feature/Boss23` 633c0c20 + `feature/LocalizationContent` 병합).
> 상세 규칙은 [localization.md §9](localization.md#9-설정창-통합). 설정창 설계는 [PLAN-settings.md](../history/PLANS/PLAN-settings.md) S15·S16.

## 1. 왜

설정창 언어(S15·S16)와 28번 로컬라이징(Loc·Strings 시트)이 따로 있어 합치면 깨졌다.

| 충돌 | 내용 |
|---|---|
| 같은 이름 전역 타입 | `GameLanguage`·`LocalizedText` 가 양쪽에 있어 CS0101 |
| enum 순서 반대 | 설정창 `English=0/Korean=1`, Loc `Ko=0/En=1` |
| 같은 PlayerPrefs 키 | `Settings.Language` 를 설정창은 정수, Loc 은 문자열(`ko`/`en`)로 저장 |
| 언어 UI 두 개 | 28번이 옛 타이틀 옵션 Gameplay 탭에 넣은 `LanguageOption` + 설정창 게임플레이 탭 언어 버튼 |

S15 의 "나중에 버튼 하나로 SO/xlsx 로 옮긴다"를 지금 Loc 으로 한 것이다 — 문구 원본이 GameData.xlsx **Strings 시트**가 됐다.

## 2. 무엇을 바꿨나

| 전 | 후 |
|---|---|
| `Settings/Localization/SettingsStrings.cs`(키·영·한 57개) | `Settings/SettingsUiStrings.cs` — 키 `UI.Settings.*` 57개, `UiString`(키 + ko 폴백) + 영어 원문(`AuthoredEnglish`). 화면 문구 원본 = Strings 시트 |
| `LanguageConfig`(Current/Changed/Set/Next/NativeName) | `Loc.Current`·`Loc.Changed`·`LocText.Next`·`LocText.NativeName`(언어 이름 비번역 규칙 그대로) |
| `GameLanguage.English/Korean` | `GameLanguage.Ko/En`(Loc 쪽) |
| 설정 전용 `LocalizedText`(키만) | Loc `LocalizedText`(키 + 저작 글자 = ko 폴백). `SettingsBody.prefab` 39개는 스크립트 guid 만 바꿔 같은 오브젝트에 그대로(필드 `key` 동일) |
| 라벨 저작 글자 = 영어 | 라벨 저작 글자 = **ko**(시트 행이 없을 때 보이는 폴백) |
| `SettingsLocalizationLinker` — 영어 원문 역조회 | 같은 메뉴, 영어 원문 → ko 순 역조회 + 글자를 ko 로 맞춤(멱등) |
| PlayerPrefs 정수 0/1 | 문자열 `en`/`ko`. 정수로 저장된 값(팀장 PC)은 처음 읽을 때 0→en, 1→ko 로 한 번 옮겨 다시 저장 |
| `SettingsStrings.Format("…", a, b)`(`{0}`) | `UiString.Format(이름 있는 인자)` — `{width}`·`{height}`·`{action}`, 확인 창 초는 공용 `UI.Unit.Seconds`(`{time}`) |
| 타이틀 옵션 `LanguageOption`·`LanguageOptionAuthoring`·키 `UI.Options.Language` | **삭제** — 언어 선택은 설정창 게임플레이 탭 버튼 하나 |

- 고친 코드: `GameplaySettingsController`·`GraphicsSettingsController`·`GraphicsQualityController`·`KeyRebindPanel`·`KeyRebindRow`(API 만 바꿈, 동작 동일).
- 테스트: `SettingsRulesTests` 의 언어 3개 → Loc API 기준으로 다시 씀(+ 57키·라벨 역조회·액션 이름). 로컬라이징 테스트에 확인 창·Linker·PlayerPrefs 이관 추가.
- 첫 실행 기본(OS 한국어면 한국어, 그 밖 English)은 두 시스템 규칙이 같아 그대로.

## 3. 경석이 받는 법

이 브랜치는 `feature/Boss23` 위에 28번(`feature/LocalizationContent`)을 병합한 것이다. PR 없이 직접 병합한다([workflow.md](workflow.md)).

1. Unity 를 닫는다(여러 에셋을 한 번에 다시 쓰는 병합 — CLAUDE.md 6).
2. `feature/Boss23` 에서 `git fetch` 후 `git merge origin/feature/SettingsLocalization`(fast-forward 또는 병합 커밋).
   - Boss23 에 그 뒤 새 커밋이 있으면 충돌 후보는 `SettingsBody.prefab`·`Settings/UI/*` 컨트롤러·`1.TitleScene.unity`.
     설정창 라벨을 새로 배치했다면 병합 뒤 **`Tools/UI/Settings/설정 패널 문구 키 연결 (멱등)`** 을 한 번 돌리면 키·ko 가 맞춰진다.
3. Unity 를 켜고 컴파일 0 확인 → `Tools/Tests/데이터 테이블 EditMode 테스트 실행`·설정 EditMode 테스트.
4. GameData.xlsx(SVN)의 Strings 시트에 `UI.Settings.*` 행이 있어야 en 이 나온다. 없으면 화면은 ko 폴백이다 — `Tools/Data/Export Template` 로 행을 붙이고 en 칸을 채운다([data-table.md](data-table.md)).
   - 2026-10-13: Export 병합 + en 채움을 마친 xlsx(Strings 156 · ErrorCodes 35, en 빈 칸 0)가 은희 로컬에 있다 — **SVN 커밋 전**. 커밋되면 `svn update` 만 하면 된다.
     내용·재번역 29행: [localization-en-draft.md › xlsx 반영](localization-en-draft.md#xlsx-반영-2026-10-13-할-일-38-③). 런타임 표 `StringTable.asset` 은 git 에 같이 갱신돼 있다.
5. Play: 설정창 → 게임플레이 → 언어 버튼으로 한국어 ↔ English, 라벨·드롭다운·확인 창·키 맞바꿈 알림이 바뀌는지.
   팀장 PC 는 옛 정수 저장값이 있으면 그 언어로 시작해야 한다(자동 이관 — Windows 에디터 PlayerPrefs 로 0→en·1→ko 재저장까지 확인함, 2026-10-13).

## 4. 앞으로 설정창 문구를 더할 때

1. `SettingsUiStrings` 에 `S("영역.이름", ko, 영어 원문, 쓰는 곳)` 한 줄 — 키는 `UI.Settings.` + 이름(점마다 대문자 시작, 숫자로 시작 금지).
2. 정적 라벨: 배치 메뉴에서 영어 원문 글자로 라벨을 만든다 → 키 연결 메뉴가 `LocalizedText` 를 붙이고 글자를 ko 로 바꾼다.
   코드 문구: `SettingsUiStrings.X.Text`, 자리표시자는 `X.Format(new Dictionary<string,string>{["이름"]=값})`. 초는 `UiStrings.Seconds` 로 만든 뒤 `{time}`.
   언어가 바뀌면 다시 채우도록 `Loc.Changed` 를 OnEnable/OnDisable 에서 구독·해제.
3. Export Template → Strings 시트에 새 행 → **en 칸은 기획이 채운다**(영어 원문을 옮기면 된다). `AuthoredEnglish` 만 고쳐서는 화면 en 이 안 바뀐다.
4. 언어 이름은 번역하지 않는다(`LocText.NativeName`).
