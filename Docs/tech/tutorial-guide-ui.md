# 튜토리얼 안내 UI (할 일 15 · S6)

> 클라 로컬 안내 UI 모듈 — **목표 패널 · 안내 자막 · 강조 표시**. 네트워크 없음.
> 서버 튜토리얼 상태를 읽어 이 API 를 부르는 연결은 S7~S10 흐름 컴포넌트([tutorial-director.md](tutorial-director.md) §10.1 단계별 문서).
> 씬 배치는 **S7 에서 끝났다**(`4.MapScene` 루트 `TutorialGuideUI` 인스턴스 1개, 추가만). 서버 자막은 디렉터 `ShowSubtitleServer` → ClientRpc → `ShowSubtitle` 하나로 온다.

## 파일

| 무엇 | 경로 |
|---|---|
| 파사드(MonoBehaviour) | `Assets/1.Scripts/Tutorial/Guide/TutorialGuideUI.cs` |
| 순수 로직 | `TutorialGuideText.cs`(변형·치환·mm:ss) · `TutorialSubtitleQueue.cs` · `TutorialGuideModel.cs`(상태·RebuildFromState) |
| 문구 테이블 타입 / ID 상수 | `TutorialGuideTextTable.cs` / `TutorialGuideIds.cs` |
| 문구 출처(SO + Strings) / Export 키 등록 | `TutorialGuideTextSource.cs` / `Editor/TutorialGuideStringKeys.cs` — [아래](#문구-출처--strings-시트-우선-so-폴백-할-일-86-t6a) |
| 로컬 플레이어 읽기 | `TutorialLocalPlayerInfo.cs`(캐릭터 식별 · {Q}{E}{P}) · `TutorialCharacterLookup.cs`(로스터 역조회 순수 판정) |
| HUD 강조 대상 | `TutorialHudTarget.cs`(enum + CombatHUD 경로) · `TutorialHighlightMarker.cs` |
| 저작 메뉴 | `Editor/TutorialGuideUIAuthoring.cs` — **Tools/UI/튜토리얼 안내 UI 구성** (멱등) |
| 테스트 | `Editor/TutorialGuideTextTests.cs` · `Editor/TutorialGuideTextSourceTests.cs` · `Editor/TutorialGuideModelTests.cs` · `Editor/TutorialCharacterLookupTests.cs` |
| 프리팹 | `Assets/2.Prefabs/UI/Tutorial/TutorialGuideUI.prefab` |
| 문구 SO | `Assets/9.ScriptableObject/Tutorial/Guide/TutorialGuideTexts.asset` |

테스트는 `TutorialEditModeTestRunner`(`Tools/Tests/튜토리얼 EditMode 테스트 실행`)의 `Fixtures` 에 있다(`^TutorialGuide(Text|TextSource|SubtitleQueue|Model)Tests$`, I0 에서 플레이어 러너에서 이동).

## 접근 방식

`TutorialGuideUI.Instance` — 씬에 놓인 첫 인스턴스(Awake 에서 등록, OnDestroy 에서 해제). 둘 이상이면 경고 후 첫 것만 쓴다.
DontDestroyOnLoad 하지 않는다 — 튜토리얼 씬에 속한다. 부르는 쪽은 `TutorialGuideUI.Instance?.…` 로 null 을 견딘다.

## API

| 호출 | 하는 일 |
|---|---|
| `ShowSubtitle(id, args)` | 자막 큐에 넣는다. 겹치면 순서대로, 문구별 지속시간(0 이하 = 6초). 행이 없거나 문구가 비면 **아무것도 안 띄우고 경고만.** |
| `SetObjective(id, args, checklist[], progress)` | 지금 할 일 1줄 + 체크리스트(□/■) + 진행도(`TutorialProgress(0, 9)` → "0/9", Total 0 = 숨김). 목표 id 가 바뀌면 `stepChangeClip` 재생. |
| `SetWaitParty(n, total, secondsLeft)` / `ClearWait()` | TUT_WAIT_PARTY 줄. 초는 로컬에서 unscaled 로 줄이고, 다시 부르면 그 값으로 맞춘다. |
| `Highlight(Transform, style)` | RectTransform = 그 UI 사각형, 그 밖 Transform = 월드 위치를 `Camera.main` 으로 투영해 따라감. 파괴되면 자동 제거. |
| `Highlight(TutorialHudTarget, style)` | CombatHUD 칸(P·Q·E·R·RMB·보스 타이머·미니맵·미니맵 보스 아이콘)을 **런타임에 경로로 찾아** 오버레이. 못 찾으면 경고 1회 + 1초마다 재시도. |
| `ClearHighlight(target)` / `ClearHighlight()` | 하나 / 전부. |
| `RebuildFromState(TutorialGuideState)` | 늦게 로드된 클라 — 목표·체크리스트·진행도·대기 줄·강조만 복원. **자막은 비운다**(지나간 자막 생략, 효과음 없음). |
| `CaptureState()` | 현재 상태 스냅샷(복원 대조용). |
| `Clear()` | 보스방 도착 시 전부 정리. |

`args` = `IReadOnlyDictionary<string, string>`. 체크리스트 `Label` 은 테이블 id 면 그 문구, 아니면 글자 그대로(같은 args 로 치환).
`style` = `Pulse`(테두리 맥동) · `Arrow`(위쪽 ▼) · `PulseAndArrow`(기본).

## 문구 규칙

- **변형**: `id + _PALADIN/_GUNNER/_ASSASSIN` 행이 있고 **문구가 비어 있지 않으면** 그 행, 아니면 기본 id.
  빈 변형 행은 "아직 안 채운 자리" 로 보고 기본으로 내려간다.
- **캐릭터 식별**(15 후속): 스폰된 Player 의 `NetworkObject.PrefabIdHash` 를 스폰 로스터 항목 `PlayerPrefab` 의 해시와
  역조회해 로스터 id 를 얻고, 그 항목의 `tutorialCharacter` 필드(`CharacterRoster.asset`, 데이터)로 변형을 고른다.
  로스터는 스폰과 같은 것 — `NetworkManager` 의 `NetworkLoadingFlowController.CharacterRoster`. 씬 참조 추가 없음.
  - 해시는 Variant 마다 다르다(Player_Paladin 913233600 · Player_Gunner 816596077 · Player_Assassin 3304217410, 2026-10-12 프리팹 확인)
    `Instantiate` 가 값을 복사하고 클라는 같은 해시의 프리팹으로 만든다(NetworkPrefab 오버라이드 없음) → **모든 피어가 같은 결과, 복제 불필요.**
  - 서버 `TutorialDirector.GetClientsOfCharacterServer` 와 클라 `TutorialGuideUI` 문구 변형이 같은 `ResolveCharacter` 를 쓴다.
  - 역조회 실패(로스터 없음 · 해시 불일치 · `tutorialCharacter` = None)면 경고 1회(상태·해시별) 후 고유 컴포넌트로 폴백 —
    `GunnerHeat` = 거너, `AssassinState` = 어쌔신, `FirstMeleePassive` = 팔라딘.
  - 순수 판정 `TutorialCharacterLookup`(테스트 `Editor/TutorialCharacterLookupTests.cs`). 로스터 해시 표는 로스터가 같으면 캐시한다.
  - 새 캐릭터: `TutorialCharacter` 에 값 추가 + 로스터 항목 `tutorialCharacter` 지정(+ 변형 접미사). 코드 분기 추가 없음.
- **치환**: `{Q}` `{E}` = 슬롯 스킬 Data 의 툴팁 DisplayName, `{P}` = 패시브 툴팁(IPlayerPassive → IPassiveTooltipProvider, PassiveHUD 와 같은 경로).
  그 밖 `{n}` `{total}` `{mm:ss}` `{키}` `{스킬명}` … 은 호출자 args. 호출자 args 가 {Q}{E}{P} 보다 우선.
  **모르는 자리표시자는 그대로 남기고 경고 없이 통과**(테스트로 고정).
- **mm:ss**: 음수·NaN = 00:00, 소수는 올림(1.2초 = 00:02), 60초 = 01:00.
- 자막·대기 시간은 `Time.unscaledDeltaTime` — 슬로우모션에 늘어나지 않는다.

## 화면 배치 (1920×1080 기준)

- 별도 Screen Space Overlay 캔버스, **sortingOrder 50** (CombatHUD 0 · 캐릭터 HUD 10 위). **GraphicRaycaster 없음** — 클릭을 막지 않는다.
- CombatHUD 생명주기 레이어(`PlayerCombatUiLifecyclePolicy`/`CombatHudLayerRules`) 대상이 아니다 → **사망·관전 중에도 보인다.**
  단 HUD 칸 강조는 대상 칸이 비활성이면 숨는다(CombatHUD 는 로컬 Player 프리팹 자식).
- 목표 패널: 상단 중앙(y −24, 폭 640) — 튜토리얼엔 보스 HP 바가 없어 빈 자리.
- 자막: **상단 앵커 y −350**(피벗 위, 폭 1100, 최대 2줄 — 1080p 에서 y 730~633, 1줄이면 730~667) — 할 일 79(2026-10-22) 에서 옮겼다.
  - 이전: 중앙 앵커 +120(1080p 에서 y 612~709). 16:9 에서는 HUD 와 안 겹쳤지만, 목표 패널은 상단 앵커·자막은 중앙 앵커라
    **화면이 16:9 보다 넓으면(21:9 2560×1080, MPPM·에디터 Game 뷰를 낮고 넓게 쓸 때) 목표 패널(체크리스트 8줄 + 대기 줄 = 높이 283)과 겹쳤다**
    (21:9 = 캔버스 2217×935 — 2줄 자막 위 끝 636 > 패널 바닥 628 로 8px 겹침, 32:9 = 2715×764 는 약 90px 겹침).
  - 지금: 패널과 같은 상단 앵커라 비율이 바뀌어도 간격이 고정이다. −350 = 패널 y −24 + 패널 최대 높이 약 314(체크리스트 8줄·대기 줄·목표 2줄) + 여유 12.
    목표가 3줄이면(실제 목표 문구엔 없음) 343 이라 닿는다. 화면 중앙(플레이어)에서는 자막 아래 끝까지 약 93px(이전 72px) 위.
  - 높이는 `ContentSizeFitter` 가 정하고 아래로 자란다(피벗 위). 튜닝은 프리팹 `Subtitle` RectTransform(저작 메뉴도 같은 값).
  - 그대로 남은 겹침: 특성 선택 창(`TutorialTraitUI`, sortingOrder 60, 화면 중앙)이 열려 있으면 그 아래 자막이 가려진다(창이 모달이라 의도로 본다).
    스킬바 툴팁(최대 y ~412)·어쌔신 HUD(y 170~234)·거너 과열 바(y 270~284)·우측 하단(타이머·미니맵·키가이드)과는 어느 비율에서도 안 겹친다.
- 우측 하단(타이머·미니맵·키가이드)은 쓰지 않는다.
- 폰트 = `Assets/Resources/NotoSansKR-VariableFont_wght SDF.asset`(스킬 툴팁과 같음). 정적 아틀라스에 ☐/☑ 가 없어 **□/■** 를 쓴다(인스펙터 `uncheckedMark`/`checkedMark`).

## 배치 방법 (1·3 은 완료 — S7·I0)

1. ✅ 튜토리얼 씬에 `TutorialGuideUI.prefab` 을 하나 놓는다(씬 상주, 네트워크 오브젝트 아님) — S7 에서 `4.MapScene` 에 놓았다.
2. 필요하면 인스펙터 `stepChangeClip`(단계 전환 효과음)을 채운다. 행별 효과음은 문구 SO 의 `sfx`. (아직 비어 있음 — 사운드 결정 대기)
3. ✅ 테스트 픽스처를 튜토리얼 러너로 옮긴다 — I0.
4. 프리팹 구조를 바꾸려면 `TutorialGuideUIAuthoring` 을 고치고 메뉴를 다시 돌린다(프리팹은 통째로 다시 만든다, 문구 SO 는 빠진 행만 추가).

## 문구 출처 — Strings 시트 우선, SO 폴백 (할 일 86 T6a)

화면 문구(자막·목표·대기 줄·체크리스트 id)는 `TutorialGuideTextSource` = **SO + Strings(`Loc`)** 하나에서 읽는다. 23호 레이드 `BOSS_RAID_*` 자막도 같은 길이다(호출 id 그대로).
- 키 = **`Tutorial.Guide.<행 id>`** 일반 키(예: `Tutorial.Guide.TUT_Z1_Q`, `Tutorial.Guide.TUT_Z1_Q_GUNNER`, `Tutorial.Guide.BOSS_RAID_…`). 툴팁 자동 키(`주인ID/필드경로`)가 아니라서
  검증기는 ko·en 자리표시자 집합이 같은지만 본다 — `{Q}`·`{E}`·`{P}`·`{mm:ss}`·`{n}/{total}` 표기를 바꿀 필요가 없다.
- 시트에 키가 있으면 그 문구(현재 언어), 없으면 **SO 문구**(`#키` 를 내지 않는다). 지금 xlsx 에 행이 없으므로 화면은 그대로 SO 문구다.
- 지속시간·효과음은 계속 **SO** 가 원본이다. SO 에 없고 시트에만 있는 id 도 찾는다(지속시간 0 = 큐 기본값, 효과음 없음) — 새 로비 대사(T6b)가 쓸 수 있다.
  시트에만 있는 캐릭터 변형(`id_GUNNER` 등)은 SO 기본 id 행의 지속시간·효과음을 물려받는다.
- 변형 선택: 변형 행이 SO·시트 어느 쪽에 있든 문구가 비어 있지 않으면 변형을 쓴다(SO 변형이 빈 칸이어도 시트 변형이 있으면 그것).
- 언어가 바뀌면(`Loc.Changed`) 목표 패널·대기 줄은 다시 그린다. 이미 뜬 자막은 그대로, 다음 줄부터 새 언어.
- Export Template: `Editor/TutorialGuideStringKeys.cs` 가 SO 의 문구 있는 행 전부를 Strings 키로 등록한다(ko = SO 문구, `#설명` = `TutorialGuideTexts · 튜토리얼 안내` / `· 23호 레이드 자막`, en 빈 칸). 빈 문구 행은 뺀다.
  Export 는 병합 뒤 메인이 한다. 시트 행이 생긴 뒤 문구를 고칠 곳은 xlsx 다(SO 문구는 폴백·새 행 ko 원문으로만 남는다).
