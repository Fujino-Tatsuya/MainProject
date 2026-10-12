# 로컬라이징 (한/영)

> 게임에 보이는 문구는 `GameData.xlsx` 의 **`Strings` 시트**, 로비·네트워크 오류 문구는 **`ErrorCodes` 시트**가 원본이다. 코드는 키·오류 코드로만 찾는다.
> L1(기반) 기준 문서 — 씬·프리팹 문구 교체(L3), `[DataTableText]` 필드 자동 키 수집(L2)은 아직 없다.
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

> 위 행은 예시다. **실제 오류 코드 목록은 아직 없다**(L4 에서 SessionStartResult 등을 코드화하며 채운다). 지금 시트는 머리글만 있다.

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
| ko·en 의 자리표시자 `{이름}` 집합이 다름 | **오류** — 순서·반복 횟수는 상관없다 |
| `<style=…>` / `</style>` 짝이 안 맞음 | **오류**(언어마다 따로 검사) |
| key 가 빈데 문구가 있음 | **오류** |

- 오류가 있으면 **테이블 모드 Play·빌드는 시작 전에 멈춘다**(수치 테이블 오류와 같은 창·Console).
- **인스펙터 모드 Play** 도 문구는 xlsx 를 읽는다(문구엔 인스펙터 값이 없다). 오류면 막지 않고 Console 오류 + 지난 표로 돈다.
- 키워드 색 `<style=stun>기절</style>` 은 [data-table.md §9](data-table.md#9-프로그래머기획--툴팁-문구와-자리표시자) 와 같은 Style Sheet 를 쓴다.

### Export Template 과의 관계

- `Tools/Data/Export Template` 은 `Strings`·`ErrorCodes` 시트를 **항상** 만든다 — 키가 하나도 없어도 머리글만 있는 시트가 생긴다.
- 기존 행(메모 행·`#` 메모 열 포함)은 **병합·덮어쓰기 어느 쪽이든 그대로** 옮긴다. 코드가 등록한 키 중 시트에 없는 것만 뒤에 붙는다(en 은 빈 칸).
  머리글에 없는 열은 Export 때 사라진다(원본은 Export 백업).
- L1 에는 코드가 등록한 키가 없다(Strings = `StringKeyRegistry`, ErrorCodes = 등록 경로 없음) — 새 키·코드는 시트에 직접 행을 추가한다.
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
| `Loc.Format(key, args)` | `Get` + `{이름}` 치환 — 규칙은 `TutorialGuideText.Fill` 과 같다 |
| `Loc.GetError(code, args = null)` | ErrorCodes 시트 문구. 폴백은 `Get` 과 같다(코드가 없으면 `#code`, en 이 비면 ko). args 가 있으면 `Format` 처럼 치환 |
| `UserLanguageConfig.Language` | 저장값 직접 접근(이벤트 없음). UI 에서는 `Loc.Current` 를 쓴다 |

- 문구는 **로컬 표시 전용**이다. 네트워크로 문구를 보내지 말고 키(또는 키 + 인자)를 보낸다 — 클라마다 언어가 다를 수 있다. 오류도 마찬가지로 코드만 보낸다.
- 오류 로그에는 문구가 아니라 **코드**를 남긴다 — QA·CS 가 ErrorCodes 시트에서 원인·조치를 찾는 열쇠다.
- 키가 화면에 `#UI.…` 로 보이면 시트에 행이 없거나 `#` 로 빠진 것이다.

### 키 규칙

- `영역.화면.이름` 점 구분 PascalCase — `UI.Lobby.Ready`, `UI.Options.Language`, `Tutorial.S3.Dash`, `Skill.Paladin.Q.Name`.
- 한 번 쓴 키의 의미를 바꾸지 않는다 — 문구가 크게 바뀌면 새 키를 만든다(번역이 조용히 어긋나지 않게).
- 같은 단어라도 화면이 다르면 키를 나눈다(언어마다 길이·어순이 달라진다).

### 컴포넌트

| 컴포넌트 | 쓰는 법 |
|---|---|
| `LocalizedText` | TMP_Text 옆에 붙이고 `key` 를 적는다. 켜질 때와 언어가 바뀔 때 텍스트를 바꾼다. 자리표시자가 있는 문구는 이 컴포넌트 대신 코드에서 `Loc.Format` |
| `LocalizedImage` | Image 옆에 붙이고 `ko`·`en` Sprite 를 넣는다. en 이 없으면 ko 를 쓰고 경고를 한 번 남긴다 |

### 표가 런타임에 들어가는 길

- 에디터: `Loc` 가 처음 조회 때 `StringTable.asset` 을 AssetDatabase 로 읽는다.
- 빌드: 빌드 훅(`DataTableBuild`)이 빌드 동안만 `StringTable.asset` 을 **Preloaded Assets** 에 넣고, 빌드 뒤 원래 목록으로 되돌린다. 시작 때 표가 스스로 `Loc` 에 걸린다.
- 생성물 `StringTable.asset` 은 git 에 올린다(문구가 바뀌었을 때만 다시 써진다).

## 4. 파일

| 파일 | 역할 |
|---|---|
| `Assets/1.Scripts/Localization/Loc.cs` | 조회 창구 |
| `Assets/1.Scripts/Localization/UserLanguageConfig.cs` | 언어 저장·기본값 |
| `Assets/1.Scripts/Localization/StringTable.cs` | 런타임 표(SO) — Strings(`Entries`)·ErrorCodes(`Errors`) 두 칸 |
| `Assets/1.Scripts/Localization/LocText.cs` | 언어 코드·자리표시자·style 짝 검사(순수) |
| `Assets/1.Scripts/Localization/LocalizedText.cs` · `LocalizedImage.cs` | 컴포넌트 |
| `Assets/1.Scripts/Localization/Editor/StringsSheet.cs` | 문자열 시트 설정(`StringsSheet.Strings`·`StringsSheet.ErrorCodes` — 시트 이름·키 열·메모 열) · 읽기·Export 시트 · 키 등록(`StringKeyRegistry`) |
| `Assets/1.Scripts/Localization/Editor/StringTableValidator.cs` | 검증(순수) |
| `Assets/1.Scripts/Localization/Editor/StringTableBuild.cs` | 표 에셋 생성·빌드 Preload·수동 갱신 메뉴 |
| `Assets/1.Scripts/UI/Title/LanguageOption.cs` · `Editor/LanguageOptionAuthoring.cs` | 옵션 언어 선택 · 배치 메뉴 `Tools/UI/Title/옵션 Gameplay — 언어 선택 배치` |

- 테스트: `Tools/Tests/데이터 테이블 EditMode 테스트 실행`(픽스처 `StringTableValidatorTests`·`StringsSheetTests`·`ErrorCodesSheetTests`·`LocTests`·`UserLanguageConfigTests`).
