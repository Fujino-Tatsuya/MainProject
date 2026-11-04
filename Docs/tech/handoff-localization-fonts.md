# 핸드오프 — 현지화 누락 연결 · 글꼴 서울알림체 통일 (2026-11-02, 경석(Claude))

> 다른 담당 영역(특성·튜토리얼·현지화·HUD 프리팹)을 건드려서 남긴다. 규칙 원본은 [localization.md](localization.md)(§8 키 목록 · §10 글꼴 규칙).
> 시작점: 전수 점검(한↔영 전환 시 안 바뀌는 문구·서체가 섞이는 곳) → 경석 지시 "Medium·Heavy 로 통일, 한국어·영어 전부 연결".

## 무엇이 바뀌었나

| 영역 | 변경 | 파일 |
|---|---|---|
| 특성 선택 창 | 고정 문구 8개(제목·인트로·마지막 안내·획득 토스트·초기화 팝업)와 마감 문구 2개를 `UI.Trait.*` 키로. 화면 = 시트 행 → `TraitTexts.asset` 행 → `TraitTexts` 상수 | `Traits/Content/TraitStringKeys.cs`(`TraitUiStrings`) · `Traits/TraitTextTable.cs`(`Resolve`) · `Traits/TraitDeadlineModel.cs`(`SecondsFormat`·`DoneFormat` 상수 삭제) |
| 캐릭터 선택 | 정보 팝업 `미구현` 에 `LocalizedText`(`UI.CharacterSelect.InfoNotImplemented`) | `CharacterSelectPanel.prefab`(컴포넌트 1개 추가) · `CharacterSelectUiStrings.cs` |
| 보스 처치 연출 | 타이포를 `UI.BossVictory.Typo`(보스 격파 / BOSS DOWN)로. 행이 없으면 `BossVictoryConfig.typoText` | `BossVictoryPresentation.cs` · `UiStrings.cs` |
| 타이틀 Press | 코드 ko 를 `아무 버튼이나 누르세요` 로(Heavy 글꼴 글자 집합이 코드 ko 에서 온다) | `UiStrings.cs` |
| 테스트 | `UiStringsTests.Keys_AreUnique_AndFollowAreaScreenNameRule` — `Tutorial.Guide.<행 id>`(UPPER_SNAKE) 예외. 158 로비 튜토리얼 키가 들어오며 깨졌던 것 | `Tests/EditMode/Localization/Editor/LocalizationL3Tests.cs` |
| xlsx(SVN, 미커밋) | r576 받은 뒤 `Strings` 만: en 52칸 · 새 행 18 · ko 21칸(영어였던 로딩·결과·패시브·Press·상태이상 8개 → 한국어). 다른 시트 변경 0 | `50.Art/DataTable~/GameData.xlsx` · 초안·근거 [localization-en-draft.md](localization-en-draft.md) "누락 점검 후속" |
| 문자열 표 | 갱신 — 키 553 · 오류 코드 35 · 오류 0 | `9.ScriptableObject/Localization/StringTable.asset` |
| 글꼴 | LiberationSans·NotoSansKR 직접 지정 45곳 → 서울알림 Medium. 외곽선 숫자용 머티리얼 2개 새로 | `CombatHUD`·`InteractPrompt`·`OverheadHealthBar`·`MonsterHealthBar_Normal`·`TrainingDummy`·`Slot_`·`ResultPlayerRow`·`CharacterSelectPanel` 프리팹 · `5.ResultScene` · `2.Prefabs/UI/SeoulAlrimTTF-Medium SDF - Cooldown Outline.mat`·`- Overhead Outline.mat` |
| Heavy 글꼴(SVN, 미커밋) | `설정창 글꼴 에셋 만들기` 메뉴로 `튼` 추가(요청 424자 · 서체에 없는 글자 0) | `50.Art/UI/Fonts/SeoulAlrimTTF-Heavy SDF.asset` |

## 알아 둘 것

- 🔸 **en·ko 새 문구는 AI 초안**(기획 검수 전). 확인이 필요한 두 군데: `Tutorial.Guide.LOBBY_BODY_SUPPRESS` "캐릭터의 행동이 빨라집니다" 의 주어 · `UI.Trait.LastNotice` 의 "게이지".
- 🔸 **외곽선 두께가 달라 보일 수 있다** — 서울알림 Medium 아틀라스 `_GradientScale` 4(LiberationSans 10·Overhead 21). 같은 `_OutlineWidth`·`_FaceDilate` 값을 옮겼지만 화면 두께는 Play 로 확인해야 한다(스킬 쿨타임 숫자·머리 위 이름표).
- 🔸 **글자 폭이 바뀌었다** — 같은 `fontSize` 라도 서울알림이 LiberationSans 보다 넓다. 스킬 키 라벨(`RMB`·`Space`)·HP 숫자·머리 위 이름표 넘침 여부 Play 확인.
- ⚠️ `AssassinShadowReturnSkillData`·`AssassinTransformedShadowReturnSkillData` 자동 키 6행은 갱신 때마다 "주인을 데이터 테이블 대상에서 찾지 못해 `{필드}` 를 검사하지 못했다" 경고가 난다(두 SO 가 `[DataTableSheet]` 대상이 아님). 설명에 `{필드}` 가 없어 무해하고, 런타임은 `PlayerSkillData.Tooltip`(`Keyed`)로 번역된다.
- 특성 테스트 3개(`TraitPickConfirmTests`·`TraitDeadlineModelTests`·`TraitWindowModelTests`)는 손대지 않았다(작성자 코드 — 경석 결정). 실제 시트를 읽는 테스트라 시트 행이 생기면 결과가 바뀐다:
  `TraitPickConfirmTests.Message_UsesRegisteredText_ThenDefault` 는 원래부터 실패(시트의 `Trait.ASN_2_RESTRAINT.Conflict` 가 테스트 등록 문구를 이김),
  `TraitWindowModelTests.TextTable_DefaultsMatchConstants_AndFallsBackWithoutAssetOrRow` 의 "에셋 값 우선" 은 이제 시트 `UI.Trait.Title` 이 이긴다(설계 변경: 시트 > SO).

## Play 확인 체크리스트

1. 설정 → 언어 English: 로비 튜토리얼 목표·체크리스트·감독관 대사, 스킬 툴팁 태그 칩, 특성 창(제목·인트로·마감·토스트·초기화 팝업), 캐릭터 선택 정보 팝업, 보스 처치 타이포가 영어.
2. 한국어: 로딩 상태 문구·결과 패널 `클리어`·패시브 배지·타이틀 `아무 버튼이나 누르세요`·상태이상 HUD 이름이 한국어.
3. 전투 HUD: 상태이상 글자에 서체가 섞이지 않는지(이전엔 한글만 NotoSansKR), 쿨타임 숫자 외곽선, 스킬 키 라벨 폭.
4. 머리 위 이름표·몬스터 이름표·허수아비 체력·상호작용 프롬프트 글꼴·외곽선.
5. 타이틀 Press 캡슐 — 글자가 길어져(16자 → 11자 한글) 알약 안에 들어가는지.
