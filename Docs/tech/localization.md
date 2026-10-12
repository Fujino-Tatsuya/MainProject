# 로컬라이징 (한/영)

> 게임에 보이는 문구는 `GameData.xlsx` 의 **`Strings` 시트**, 로비·네트워크 오류 문구는 **`ErrorCodes` 시트**가 원본이다. 코드는 키·오류 코드로만 찾는다.
> L1(기반) + L2(SO 문구 — 스킬 툴팁·캐릭터 이름 자동 키, [§5](#5-l2--so-문구-자동-키)) + L3(씬·프리팹 라벨·HUD 포맷 문구, [§6](#6-l3--씬프리팹-라벨)) + L4a(로비·네트워크 오류 코드, [§7](#7-l4--로비네트워크-오류-코드)) 기준 문서.
> 수치 테이블 쓰는 법은 [data-table.md](data-table.md).

## 1. 한눈에

```
GameData.xlsx › Strings 시트 · ErrorCodes 시트 (SVN)
   │  Play 진입·빌드 때 검증(두 시트 같은 규칙, 오류면 테이블 Play·빌드 중단)
   ▼
Assets/9.ScriptableObject/Localization/StringTable.asset (git, 생성물 — 손으로 고치지 않는다)
   │  Strings → entries 칸, ErrorCodes → errors 칸 (한 에셋)
   ▼
Loc.Get("UI.Lobby.Ready") → 현재 언어 문구   ← LocalizedText / LocalizedImage 가 언어 변경 시 다시 그림
Loc.GetError("LOBBY_FULL") → 현재 언어 오류 문구
```

- 지원 언어: **ko**(기본·필수) · **en**.
- 언어 설정은 타이틀 **옵션 → Gameplay → Language**(한국어 / English). 저장 = PlayerPrefs `Settings.Language`.
  처음 실행(저장값 없음)이면 OS 언어가 한국어면 ko, 아니면 en.

## 2. 기획 — Strings 시트 쓰는 법

| key | #설명 | ko | en |
|---|---|---|---|
| UI.Lobby.Ready | 로비 준비 버튼 | 준비 | Ready |
| UI.Lobby.Waiting | 대기 인원 | {n}명 대기 중 | {n} waiting |
| `#UI.Old.Thing` | (메모 행 — 무시) | | |

- 1행 머리글은 `key | #설명 | ko | en` 그대로 둔다(열 순서는 바뀌어도 이름으로 찾는다). `#설명` 열은 읽지 않는다.
- **키(A열)는 프로그래머가 정한다**. 기획은 ko·en 칸만 고친다. 키 앞에 `#` 를 붙이면 그 행은 빠진다.
- 줄바꿈은 셀 안에서 `Alt+Enter` — 게임에서도 줄바꿈된다.
- 고친 뒤 **Play 를 다시 시작**하면 반영된다(Play 중 수정은 반영 안 됨).
- 수동으로 표만 갱신: `Tools/Data/StringTable 갱신 (Strings·ErrorCodes 시트 → 에셋)`.

### ErrorCodes 시트 — 로비·네트워크 오류 문구

로비 참가 실패·연결 끊김처럼 **플레이어에게 보이는 오류**는 코드 하나에 한 행으로 따로 관리한다.
QA·CS 는 화면의 문구나 로그의 코드로 이 시트를 찾아 원인·조치를 확인한다.

| code | #분류 | #원인(개발자용) | #조치(QA·CS용) | ko | en |
|---|---|---|---|---|---|
| LOBBY_FULL | 로비 | 호스트 인원 상한 도달 | 다른 방 참가 안내. 반복되면 호스트 인원 설정 확인 | 방이 가득 찼습니다 | The lobby is full |
| NET_TIMEOUT | 연결 | 핸드셰이크 {sec}초 안에 응답 없음 | 방화벽·IP 확인 후 재시도 | {sec}초 동안 응답이 없습니다 | No response for {sec}s |

> 위 행은 열 쓰는 법 예시다(`LOBBY_FULL`·`NET_TIMEOUT` 은 코드에 없다). 실제 코드 목록은 [§7](#7-l4--로비네트워크-오류-코드).
> 코드 목록은 `ErrorCatalog`(코드) 한 곳이고, Export 가 시트에 없는 코드만 뒤에 붙인다.

| 열 | 누가 | 뜻 |
|---|---|---|
| `code` | 프로그래머 | 오류 코드. 코드에서 `Loc.GetError(code)` 로 찾는다. `영역_이름` 대문자(`LOBBY_FULL`, `NET_TIMEOUT`). 한 번 정한 코드는 의미를 바꾸지 않는다 |
| `#분류` | 프로그래머 | 로비·연결·Steam 등 묶음 — 필터용 |
| `#원인(개발자용)` | 프로그래머 | 어떤 조건에서 나는지(코드 위치·조건). 번역 대상 아님 |
| `#조치(QA·CS용)` | 프로그래머·QA | 재현·확인 순서, 유저에게 안내할 해결법. 번역 대상 아님 |
| `ko` · `en` | 기획 | 플레이어에게 보이는 문구 — Strings 시트와 같다 |

- `#` 로 시작하는 열은 메모 열이다 — **런타임 표에 들어가지 않는다**(빌드에 개발자용 원인이 새지 않음).
- 검증 규칙·en 폴백·자리표시자·`#` 메모 행은 **Strings 시트와 완전히 같다**(아래 표). 단, 키 공간은 따로라 Strings 키와 같은 문자열이어도 충돌하지 않는다.
- 시트가 없거나 머리글만 있어도 오류가 아니다.

### 검증 규칙

Strings·ErrorCodes 두 시트에 똑같이 적용한다(시트마다 따로 검사 — 중복 판정도 시트 안에서만).

| 상황 | 결과 |
|---|---|
| 같은 키가 두 번(다른 xlsx 포함) | **오류** — 두 번째 행 위치를 알려 준다 |
| ko 빈 칸 | **오류** |
| en 빈 칸 | 경고 + **ko 문구로 표시** |
| ko·en 의 자리표시자 `{이름}` 집합이 다름 | **오류** — 순서·반복 횟수는 상관없다. `{값:%}` 처럼 형식도 이름에 포함 |
| `<style=…>` / `</style>` 짝이 안 맞음, `<style=>`(이름 없음) | **오류**(언어마다 따로 검사) |
| 중괄호 문법 — 짝 없는 `}`, 닫히지 않은 `{`, 빈 `{}` | **오류**(언어마다 따로 검사) |
| key 가 빈데 문구가 있음 | **오류** |

- 오류가 있으면 **테이블 모드 Play·빌드는 시작 전에 멈춘다**(수치 테이블 오류와 같은 창·Console).
- **인스펙터 모드 Play** 도 문구는 xlsx 를 읽는다(문구엔 인스펙터 값이 없다). 오류면 막지 않고 Console 오류 + 지난 표로 돈다.
- 키워드 색 `<style=stun>기절</style>` 은 [data-table.md §9](data-table.md#9-프로그래머기획--툴팁-문구와-자리표시자) 와 같은 Style Sheet 를 쓴다.
- 문법·자리표시자 규칙은 코드 한 곳(`LocText` — `StyleTagError`·`BraceError`·`SamePlaceholders`)이다. 스킬 툴팁 포매터(`SkillTooltipFormatter`)도 같은 함수로 검사하고,
  그 위에 "필드가 실제로 있는가·형식이 맞는가" 만 더 본다.

### Export Template 과의 관계

- `Tools/Data/Export Template` 은 `Strings`·`ErrorCodes` 시트를 **항상** 만든다 — 키가 하나도 없어도 머리글만 있는 시트가 생긴다.
- 기존 행(메모 행·`#` 메모 열 포함)은 **병합·덮어쓰기 어느 쪽이든 그대로** 옮긴다. 코드가 등록한 키 중 시트에 없는 것만 뒤에 붙는다(en 은 빈 칸).
  머리글에 없는 열은 Export 때 사라진다(원본은 Export 백업).
- 코드가 등록하는 Strings 키 = `[DataTableText]` 자동 키 + 캐릭터 이름([§5](#5-l2--so-문구-자동-키)) + L3 UI 문구(`UiStrings`, [§6](#6-l3--씬프리팹-라벨)). 그 밖의 새 Strings 키는 시트에 직접 행을 추가한다.
- ErrorCodes 코드 = `ErrorCatalog.All`([§7](#7-l4--로비네트워크-오류-코드)). 새 코드는 `ko`·`#분류`·`#원인`·`#조치` 가 채워진 채 붙는다(en 빈 칸).
  **시트에만 있고 `ErrorCatalog` 에 없는 행은 경고**(오류 아님, 행은 남긴다) — 안 쓰는 코드면 지우거나 `#` 를 붙인다.
- 🔴 Export 는 xlsx 를 새로 쓴다(서식·열 너비 사라짐) — [data-table.md §4](data-table.md#4-메뉴-toolsdata).

## 3. 프로그래머 — API

```csharp
string ready = Loc.Get("UI.Lobby.Ready");                 // 키가 없으면 "#UI.Lobby.Ready"
string wait  = Loc.Format("UI.Lobby.Waiting",
    new Dictionary<string, string> { ["n"] = count.ToString() });  // 없는 이름의 {…} 는 그대로 남는다
string error = Loc.GetError("NET_TIMEOUT",
    new Dictionary<string, string> { ["sec"] = "10" });     // 코드가 없으면 "#NET_TIMEOUT"

Loc.Current = GameLanguage.En;   // 저장 + Loc.Changed 발생(같은 값이면 아무 일 없음)
Loc.Changed += Redraw;           // OnEnable 에서 구독, OnDisable 에서 꼭 해제
```

| 멤버 | 설명 |
|---|---|
| `Loc.Current` | 현재 언어(`GameLanguage.Ko`/`En`). 설정하면 `UserLanguageConfig` 에 저장되고 `Changed` |
| `Loc.Changed` | 언어가 바뀌거나 표가 새로 걸릴 때 |
| `Loc.Get(key)` | 현재 언어 문구. en 이 비면 ko. 키가 없거나 빈 키면 `#key` |
| `Loc.Get(key, fallback)` | 같지만 키가 없거나(빈 키·표 없음 포함) 시트에 행이 없으면 `fallback` — `#key` 를 내지 않는다. SO 인라인 ko 가 있는 자동 키용 |
| `Loc.Format(key, args)` | `Get` + `{이름}` 치환 — 규칙은 `TutorialGuideText.Fill` 과 같다 |
| `Loc.GetError(code, args = null)` | ErrorCodes 시트 문구. 폴백은 `Get` 과 같다(코드가 없으면 `#code`, en 이 비면 ko). args 가 있으면 `Format` 처럼 치환 |
| `Loc.GetError(code, fallback, args)` | 같지만 시트에 행이 없으면 `#code` 대신 `fallback`(코드의 ko 원문)에 args 를 채운다. `ErrorCode.Message` 가 쓴다 |
| `UserLanguageConfig.Language` | 저장값 직접 접근(이벤트 없음). UI 에서는 `Loc.Current` 를 쓴다 |

- 문구는 **로컬 표시 전용**이다. 네트워크로 문구를 보내지 말고 키(또는 키 + 인자)를 보낸다 — 클라마다 언어가 다를 수 있다. 오류도 마찬가지로 코드만 보낸다.
- 오류 로그에는 문구가 아니라 **코드**를 남긴다 — QA·CS 가 ErrorCodes 시트에서 원인·조치를 찾는 열쇠다.
- 키가 화면에 `#UI.…` 로 보이면 시트에 행이 없거나 `#` 로 빠진 것이다(`Loc.Get(key)` 직접 호출일 때. 폴백이 있는 `LocalizedText`·`UiString`·`Loc.Get(key, fallback)` 은 원문이 보인다).

### 키 규칙

- `영역.화면.이름` 점 구분 PascalCase — `UI.Lobby.Ready`, `UI.Options.Language`, `Tutorial.S3.Dash`, `Skill.Paladin.Q.Name`.
- 한 번 쓴 키의 의미를 바꾸지 않는다 — 문구가 크게 바뀌면 새 키를 만든다(번역이 조용히 어긋나지 않게).
- 같은 단어라도 화면이 다르면 키를 나눈다(언어마다 길이·어순이 달라진다).

### 컴포넌트

| 컴포넌트 | 쓰는 법 |
|---|---|
| `LocalizedText` | TMP_Text 옆에 붙이고 `key` 를 적는다. 켜질 때와 언어가 바뀔 때 텍스트를 바꾼다. 시트에 행이 없으면 **저작된 글자(ko 원문)를 그대로 둔다** — 저작 글자가 비었을 때만 `#key`. 자리표시자가 있는 문구는 이 컴포넌트 대신 코드에서 `Loc.Format` |
| `LocalizedImage` | Image 옆에 붙이고 `ko`·`en` Sprite 를 넣는다. en 이 없으면 ko 를 쓰고 경고를 한 번 남긴다 |

### 표가 런타임에 들어가는 길

- 에디터: `Loc` 가 처음 조회 때 `StringTable.asset` 을 AssetDatabase 로 읽는다.
- 빌드: 빌드 훅(`DataTableBuild`)이 빌드 동안만 `StringTable.asset` 을 **Preloaded Assets** 에 넣고, 빌드 뒤 원래 목록으로 되돌린다. 시작 때 표가 스스로 `Loc` 에 걸린다.
- 생성물 `StringTable.asset` 은 git 에 올린다(문구가 바뀌었을 때만 다시 써진다).

## 4. 파일

| 파일 | 역할 |
|---|---|
| `Assets/1.Scripts/Localization/Loc.cs` | 조회 창구 |
| `Assets/1.Scripts/Localization/LocKey.cs` | 자동 키 규칙(`주인ID/필드경로`) — Export·런타임 공용 |
| `Assets/1.Scripts/Localization/Editor/DataTableTextKeys.cs` | Export 때 `[DataTableText]` 문구를 Strings 키로 수집 |
| `Assets/1.Scripts/Player/Editor/CharacterRosterStringKeys.cs` | Export 때 캐릭터 이름 키 수집(`StringKeyRegistry`) |
| `Assets/1.Scripts/Localization/UserLanguageConfig.cs` | 언어 저장·기본값 |
| `Assets/1.Scripts/Localization/StringTable.cs` | 런타임 표(SO) — Strings(`Entries`)·ErrorCodes(`Errors`) 두 칸 |
| `Assets/1.Scripts/Localization/LocText.cs` | 언어 코드·자리표시자·style 짝 검사(순수) |
| `Assets/1.Scripts/Localization/LocalizedText.cs` · `LocalizedImage.cs` | 컴포넌트 |
| `Assets/1.Scripts/Localization/Editor/StringsSheet.cs` | 문자열 시트 설정(`StringsSheet.Strings`·`StringsSheet.ErrorCodes` — 시트 이름·키 열·메모 열) · 읽기·Export 시트 · 키 등록(`StringKeyRegistry`) |
| `Assets/1.Scripts/Localization/Editor/StringTableValidator.cs` | 검증(순수) |
| `Assets/1.Scripts/Localization/Editor/StringTableBuild.cs` | 표 에셋 생성·빌드 Preload·수동 갱신 메뉴 |
| `Assets/1.Scripts/UI/Title/LanguageOption.cs` · `Editor/LanguageOptionAuthoring.cs` | 옵션 언어 선택 · 배치 메뉴 `Tools/UI/Title/옵션 Gameplay — 언어 선택 배치` |
| `Assets/1.Scripts/Localization/UiStrings.cs` | L3 UI 문구 키 목록(`UiString` = 키 + ko 폴백 + 쓰는 곳) |
| `Assets/1.Scripts/Localization/Editor/UiStringKeys.cs` | Export 때 `UiStrings.All` 을 Strings 키로 등록(`StringKeyRegistry`) |
| `Assets/1.Scripts/Localization/Editor/UiStringsAuthoring.cs` | L3 저작 메뉴 `Tools/Localization/L3 — 씬·프리팹 라벨 키 부착` |
| `Assets/1.Scripts/Localization/ErrorCatalog.cs` | L4 오류 코드 목록(`ErrorCode` = 코드 + 분류·원인·조치 + ko) · 화면 표기(`ErrorDisplay`) |
| `Assets/1.Scripts/Localization/Editor/ErrorCodeKeys.cs` | Export 때 `ErrorCatalog.All` 을 ErrorCodes 시트로 등록(`StringKeyRegistry.RegisterErrorCodes`) |
| `Assets/1.Scripts/Network/Session/SessionStartResult.cs` | `SessionError`(코드 + 인자 + 상세) · `SessionStartResult` |

- 테스트: `Tools/Tests/데이터 테이블 EditMode 테스트 실행`(픽스처 `StringTableValidatorTests`·`StringsSheetTests`·`ErrorCodesSheetTests`·`LocTests`·`UserLanguageConfigTests`,
  L2 `LocKeyTests`·`DataTableTextKeysTests`·`CharacterRosterStringKeysTests`·`LocTextRuleTests`, L3 `UiStringsTests`·`UiStringsFormatTests`·`UiStringsAuthoringTests`,
  L4 `ErrorCatalogTests`·`ErrorDisplayTests`).

## 5. L2 — SO 문구 자동 키

SO·프리팹에 인라인으로 적힌 문구(스킬 툴팁·캐릭터 이름)를 **키 없이 손대지 않고** Strings 시트로 옮기는 단계다.
인라인 값은 지우지 않는다 — ko 원문이자 **폴백**이다.

### 자동 키 규칙 (`[DataTableText]`)

`주인ID/필드경로` — 예 `FirstMeleeMainSkillData/tooltip.description`.

| 주인 | 주인ID | 이유 |
|---|---|---|
| ScriptableObject | **에셋 이름**(`Object.name`) | 빌드에서도 에셋 이름이 그대로 남는다 |
| 프리팹 컴포넌트(예 `FirstMeleePassive`) | **컴포넌트 타입 이름** — `FirstMeleePassive/tooltip.displayName` | 런타임 인스턴스는 프리팹 파일 이름을 모른다(`(Clone)`·자식 오브젝트 이름) |

- 필드경로 = 데이터 테이블 표기(`tooltip.description`, 배열은 `entries[0].x`). 키 함수는 `LocKey.ForField` 하나 — Export 수집과 런타임 getter 가 같이 쓴다.
- 🔴 **에셋 이름·필드 이름을 바꾸면 키가 바뀐다** — 시트의 옛 행은 남고(고아 행) 화면은 인라인 ko 로 돌아간다. 이름을 바꿨으면 시트 key 열도 같이 고친다.
- 같은 컴포넌트 타입이 데이터 테이블 대상 프리팹 둘 이상에 있으면 키가 겹친다 — Export 가 처음 것만 모으고 경고한다(지금은 `FirstMeleePassive` 가 `Player_Paladin` 하나뿐).

### 런타임 — 폴백

- `SkillTooltipText` 의 `DisplayName`·`Subtitle`·`Description` 은 `Loc.Get(키, 인라인 값)` 이다. 시트에 행이 없으면 **`#키` 가 아니라 인라인 ko**.
- 키는 주인이 붙인다: `public SkillTooltipText Tooltip => tooltip.Keyed(this, nameof(tooltip));`(`PlayerSkillData`·`GunnerHeatData`·`AssassinStateData`·`FirstMeleePassive`).
  `Keyed` 없이 struct 를 그대로 읽으면 인라인 값만 나온다.
- 캐릭터 이름 `CharacterRoster.Entry.DisplayName` = `Loc.Get("Character.<플레이어 프리팹 이름>.Name", displayName)`(예 `Character.Player_Paladin.Name`).
  배열 순서가 아니라 프리팹 이름 기준이라 칸을 옮겨도 번역이 따라간다. 프리팹이 비어 있으면 인라인 이름. 결과 화면(`ResultPlayerRowView`) 등 `DisplayName` 을 쓰는 UI 는 그대로 번역된다.
- 툴팁 en 문구도 툴팁 포매터를 거친다 — `{필드}` 는 en·ko 가 같은 집합이어야 한다(위 검증 규칙). 필드가 실제로 있는지는 화면에 띄울 때 포매터가 보고, 틀리면 경고 로그.

### Export 수집

- 대상 = 데이터 테이블 대상(`[DataTableSheet]` SO·프리팹 컴포넌트)의 `[DataTableText]` 문자열 + `StringKeyRegistry`(캐릭터 이름).
- 새 키만 Strings 시트 뒤에 붙는다: **ko = 지금 SO 값, en = 빈 칸, `#설명` = `대상 · 필드경로`**(컴포넌트면 `Player_Paladin › FirstMeleePassive · tooltip.description`).
  이미 있는 키는 건드리지 않는다 — 기획이 고친 ko·en 이 Export 로 덮이지 않는다.
- 빈 값은 모으지 않는다(ko 빈 칸은 시트 오류라서). 런타임도 빈 인라인 값 그대로.

### 수집 제외

- 번역 대상이 아닌 `[DataTableText]` 는 **`[DataTableText(Localize = false)]`** — 수치 시트 문자열 칸으로만 남고 Strings 에 안 간다.
  지금: `SoundEntry.key`(코드 ID)·`SoundEntry.description`(기획 메모).
- 튜토리얼 문구(`TutorialGuideTextTable`·`TutorialTraitTextTable`)는 아직 `[DataTableText]` 가 없어 수집되지 않는다(별도 레인).

### 두 원본 주의

- 툴팁 문구는 L2 뒤에도 수치 시트(`Paladin`·`Gunner` 등)의 `tooltip.*` 칸으로 SO 에 적용된다. **Strings 에 행이 있으면 화면은 Strings 가 이긴다**(ko 포함).
  수치 시트 칸은 Strings 행이 없을 때의 폴백 값이 된다 — 문구는 Strings 시트에서 고친다.
- 인스펙터 모드 Play 에서도 문구는 xlsx Strings 를 읽는다 — 인스펙터에서 고친 툴팁은 Strings 행이 있으면 안 보인다.

## 6. L3 — 씬·프리팹 라벨

씬·프리팹에 박힌 한국어 라벨 중 **필요한 것만** 키로 옮겼다. 영어 메뉴(타이틀·맵 옵션 등)·개발용 로비 라벨은 대상이 아니다.
키·ko 원문은 `UiStrings`(코드) 한 곳에 있고, Export 가 Strings 시트에 **ko = 지금 문구, `#설명` = 쓰는 곳** 으로 새 행을 붙인다(이미 있는 키는 안 건드림).
시트에 행이 생기기 전에도 화면은 ko 원문 그대로다(`#키` 가 안 나온다).

### 정적 라벨 — `LocalizedText`

| 키 | ko | 위치 |
|---|---|---|
| `UI.Result.Character` · `Damage` · `Kills` · `Counters` · `Deaths` | 캐릭터 · 데미지 · 처치 · 간파 · 사망 | `5.ResultScene` › `PlayerRowsHeader/Header_*` |
| `UI.AssassinHUD.EnhancedReady` | E 강화 준비 | `AssassinHUD.prefab` › `EnhancedReady` |
| `UI.Interact.Prompt` | 상호 작용 | `InteractPrompt.prefab` › `InteractText` |
| `UI.Options.Language` | Language | `1.TitleScene` › `LanguageOption/LanguageLabel` |

- 붙이는 법: **`Tools/Localization/L3 — 씬·프리팹 라벨 키 부착`**. 멱등 — 키가 이미 맞으면 저장하지 않는다(두 번 돌려도 diff 없음). 글자·위치·글꼴은 안 건드린다.
  씬·프리팹 글자가 `UiStrings` 의 ko 와 다르면 경고만 남긴다(시트 행이 없으면 저작 글자가 보인다).
- 새로 배치하는 타이틀 Language 행은 `LanguageOptionAuthoring` 이 라벨에 키를 같이 붙인다.
- `UI.Options.Language` 의 ko 는 지금 문구인 `Language` 다 — 타이틀 옵션은 영어 메뉴라 바꾸지 않았다. 한국어로 보일지는 기획이 시트 ko 칸으로 정한다.

### 코드가 쓰는 문구 — 코드 키

런타임에 값을 넣어 매 프레임 쓰는 라벨은 프리팹에 컴포넌트를 붙이지 않고 코드가 `UiString.Format` 으로 만든다(프리팹의 `과열 0단계`·`변신` 글자는 화면에 안 나오는 자리 글자다).
언어가 바뀌면(`Loc.Changed`) 다시 그린다.

| 키 | ko | 자리표시자 | 쓰는 곳 |
|---|---|---|---|
| `UI.GunnerHeat.Stage` | 과열 {stage}단계 | `{stage}` = 0부터 세는 과열 단계 | `GunnerHeatGauge.LabelText` |
| `UI.GunnerHeat.Overheated` | 과열! | | 〃 |
| `UI.AssassinHUD.TransformRemaining` | 변신 {sec}초 | `{sec}` = 남은 초(소수 한 자리) | `AssassinHUD.TransformLabelText` |
| `UI.AssassinHUD.TransformReleasable` | 변신 {sec}초 · R 해제 가능 | 〃 | 〃 |
| `UI.AssassinHUD.TransformEndPending` | 변신 종료 대기 | | 〃 |
| `UI.Overhead.PlayerLabel` | P{n} | `{n}` = 1부터 세는 플레이어 번호 | `PlayerOverheadHealthBarRule.LabelText` |

- 🔴 `UI.Overhead.PlayerLabel` 은 `Player.prefab` 의 직렬화 `labelFormat`(`P{0}`)을 **고치지 않고** 옮겼다 — 시트에 행이 있으면 시트가 이기고, 없으면 `labelFormat` 폴백.
  자리표시자 이름이 다르다(시트 `{n}`, 프리팹 `{0}`). 시트 행이 생기면 `labelFormat` 은 쓰이지 않는다.
- 새 L3 키는 `UiStrings` 에 필드를 만들고 `All` 에 넣는다. 정적 라벨이면 `UiStringsAuthoring` 대상 표에도 넣고 메뉴를 돌린다.

## 7. L4 — 로비·네트워크 오류 코드

로비 연결·세션 시작이 실패하면 문구 대신 **오류 코드**가 오간다. 코드 목록은 `ErrorCatalog`(코드) 한 곳이고,
Export 가 ErrorCodes 시트에 없는 코드를 **ko = 지금 문구, `#분류`·`#원인`·`#조치` = 코드에 적힌 메모, en = 빈 칸** 으로 붙인다(이미 있는 행은 안 건드림).
시트에 행이 생기기 전에도 화면은 ko 원문이다(`#코드` 가 안 나온다).

### 흐름

- `SessionStartResult.Failed(ErrorCode, args, detail)` — 실패 = **코드 + 이름 있는 인자(`{ip}` 등) + 상세**. 프로바이더의 `IsAvailable(out SessionError)` 도 같은 `SessionError` 를 준다.
- **상세(`Detail`)** = 예외 메시지·Relay `reason`/`status`·NGO `DisconnectReason` 같은 기술 정보. **화면에 안 나온다** — 로그(`SessionError.LogText` = `CODE 인자=값 detail='…'`)에만.
- 화면 = `SessionError.DisplayText` = 현재 언어 문구 + 줄바꿈 + 작은 글씨 `[CODE]`(`ErrorDisplay.Compose` — `<size=70%>`). 로비는 `LobbySceneManager.ShowError` 하나로 띄우고 같은 줄을 Console·network.log 에 남긴다.
- 진행 상태 문구(`Relay 방 생성 중...`·`서버 접속 시도 중... (cfg=…)`·`Host 시작됨`·`연결 대상 설정`·조인코드 표시)는 오류가 아니라 **코드화하지 않았다**(개발용 로비 라벨, L3 와 같은 판단).
- 언어를 바꿔도 이미 떠 있는 오류 문구는 다시 그리지 않는다(다음 오류부터 새 언어).

### 코드 목록

| 코드 | 분류 | 원인 | ko |
|---|---|---|---|
| `LOBBY_LAUNCHER_MISSING` | 로비 | NetworkSessionLauncher 배선 없음 | 네트워크 세션 런처를 찾을 수 없습니다. |
| `LOBBY_UI_MISSING` | 로비 | LobbyUIController 배선 없음(준비 버튼) | 로비 UI 컨트롤러를 찾을 수 없습니다. |
| `LOBBY_NOT_ALL_READY` | 로비 | 준비 안 된 플레이어가 있는데 시작 | 모든 플레이어가 준비되지 않았습니다. |
| `NET_IP_EMPTY` | 연결 | IP 입력칸이 빔(로비·IPv4 프로바이더) | IP를 입력하세요. |
| `NET_IP_INVALID` | 연결 | 로비 — IP 파싱 실패 | IP 형식이 올바르지 않습니다: {ip} |
| `NET_IPV4_INVALID` | 연결 | IPv4 프로바이더 — IPv4 아님 | IPv4 형식이 올바르지 않습니다: {ip} |
| `NET_PORT_INVALID` | 연결 | Port 숫자 아님·범위 밖 | Port는 1~65535 범위의 숫자여야 합니다: {port} |
| `NET_PROVIDER_MISSING` | 연결 | 연결 방식 프로바이더 없음(UnityTransport 없음) | {mode} 연결 방식의 프로바이더가 등록되지 않았습니다. |
| `NET_START_CANCELED` | 연결 | 준비 중 취소 | 세션 시작이 취소되었습니다. |
| `NET_PREPARE_FAILED` | 연결 | 프로바이더 준비 중 예상 못 한 예외(상세 = 예외) | 세션 연결 준비에 실패했습니다. |
| `NET_START_EXCEPTION` | 연결 | 세션 시작 중 예외(상세 = 예외) | 세션 시작 중 예외가 발생했습니다. |
| `NET_START_FAILED` | 연결 | 코드 없이 실패가 들어옴(개발 문제 — 기본값) | 세션 시작에 실패했습니다. |
| `NET_HOST_START_FAILED` | 연결 | `StartHost()` false — 대개 포트 점유 | Host 시작에 실패했습니다. 포트가 이미 사용 중인지 확인하세요. |
| `NET_CLIENT_START_FAILED` | 연결 | `StartClient()` false | Client 시작에 실패했습니다. |
| `NET_CONNECT_FAILED` | 연결 | 접속 시도 중 끊김(거절 아님) | 서버 접속에 실패했습니다. IP/Port와 Host 상태를 확인하세요. |
| `NET_REJECTED_BY_HOST` | 연결 | 호스트가 끊음 — 대개 NetworkConfig 해시 불일치 | 호스트가 접속을 거절했습니다. 양쪽 빌드가 같은 버전인지 확인하세요. (내 {cfg}) |
| `NET_DISCONNECTED` | 연결 | 접속 뒤 내 연결이 끊김 | 서버와의 연결이 끊어졌습니다. |
| `NET_PLAYER_DISCONNECTED` | 연결 | 호스트 화면 — 다른 플레이어 끊김 | 플레이어(ClientId={clientId})의 연결이 끊어졌습니다. |
| `NET_TRANSPORT_FAILURE` | 연결 | `OnTransportFailure` | 네트워크 전송 오류가 발생했습니다. |
| `RELAY_TRANSPORT_MISSING` | Relay | UnityTransport 없음 | Relay 연결에 필요한 UnityTransport를 찾을 수 없습니다. |
| `RELAY_JOIN_CODE_EMPTY` | Relay | 조인코드 입력칸이 빔 | Relay 조인코드를 입력하세요. |
| `RELAY_JOIN_CODE_INVALID` | Relay | JoinCodeNotFound·AllocationNotFound·EntityNotFound | Relay 조인코드가 잘못되었거나 만료되었습니다. |
| `RELAY_INACTIVE_PROJECT` | Relay | InactiveProject | Unity Dashboard에서 이 프로젝트의 Relay 서비스를 활성화하세요. |
| `RELAY_UNAUTHORIZED` | Relay | Unauthorized·Forbidden | Relay 인증 또는 프로젝트 접근 권한이 없습니다. |
| `RELAY_PAYMENT_REQUIRED` | Relay | PaymentRequired | Relay 사용 한도 또는 결제 설정을 확인하세요. |
| `RELAY_RATE_LIMITED` | Relay | RateLimited | Relay 요청 한도를 초과했습니다. 잠시 후 다시 시도하세요. |
| `RELAY_UNAVAILABLE` | Relay | NoSuitableRelay·ServiceUnavailable·GatewayTimeout | 사용 가능한 Relay 서버가 없습니다. 잠시 후 다시 시도하세요. |
| `RELAY_JOIN_REJECTED` | Relay | 참가 중 InvalidRequest·Conflict(정원 초과 등) | Relay 방에 참가할 수 없습니다. 조인코드와 방 정원을 확인하세요. |
| `RELAY_HOST_FAILED` | Relay | 방 생성 중 그 밖의 예외 | Relay 방 생성에 실패했습니다. |
| `RELAY_JOIN_FAILED` | Relay | 참가 중 그 밖의 예외 | Relay 방 참가에 실패했습니다. |
| `UGS_PROJECT_NOT_LINKED` | Unity Services | `cloudProjectId` 빔 | Unity Services 프로젝트 연결(cloudProjectId)이 설정되지 않았습니다. |
| `UGS_INIT_FAILED` | Unity Services | 초기화·익명 로그인 예외(상세 = 예외) | Unity Services 초기화 또는 익명 로그인에 실패했습니다. |
| `UGS_NOT_INITIALIZED` | Unity Services | 초기화 안 됨 | Unity Services가 초기화되지 않았습니다. |
| `UGS_INITIALIZING` | Unity Services | 초기화 진행 중 | Unity Services 초기화 중입니다. 잠시 후 다시 시도하세요. |
| `UGS_NOT_SIGNED_IN` | Unity Services | 익명 로그인 안 됨 | Unity Services 익명 로그인이 완료되지 않았습니다. |

- `#조치` 칸 원문은 `ErrorCatalog.cs` 에 있다(Export 가 시트로 옮긴다). 위 표는 요약이다 — 원문은 코드.
- 예전 문구에 붙던 예외 메시지(`세션 연결 준비에 실패했습니다: …`·`Relay 방 참가에 실패했습니다: …`)는 상세로 옮겼다 — 화면엔 문구 + 코드만.
- 새 코드: `ErrorCatalog` 에 필드를 만들고 `All` 에 넣는다(`영역_이름` 대문자, 한 번 정한 코드는 의미를 바꾸지 않는다). 테스트가 `All` 누락·중복·이름 규칙을 잡는다.
