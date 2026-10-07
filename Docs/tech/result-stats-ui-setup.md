# 결과 화면 플레이어별 통계 — UI 배선 지시서 (2단계용)

> 계획 = [PLAN-result-stats.md](../../PLAN-result-stats.md) 2단계. 1단계(코드)는 `feature/ResultStats` 에 들어가 있다.
> 이 문서는 **`5.ResultScene.unity` 와 행 프리팹을 어떻게 만들지**만 적는다. 코드는 이미 이 이름·필드를 기대한다.

## 현재 씬 (2026-10-07 실측, `Assets/0.Scenes/MainFlow/5.ResultScene.unity`)
`Canvas` 아래 `ResultStats`(= `ResultStatsView` 가 붙은 오브젝트로 추정 — 저작 메뉴에서 컴포넌트로 찾을 것)와
`Text_Outcome` · `Text_Survival` · `Text_Kills` · `Button_GotoLobby` 가 있다.

## 코드가 기대하는 것

### `ResultStatsView` (`Assets/1.Scripts/UI/ResultStatsView.cs`)
| 직렬화 필드 | 타입 | 비었을 때 이름으로 찾는 자식 | 비고 |
|---|---|---|---|
| `outcomeText` | `TMP_Text` | `Text_Outcome` | 기존. 라벨 = `clearedLabel`/`failedLabel`(전멸)/`abortedLabel`(중도 종료, 기본 `ABORTED`) |
| `survivalText` | `TMP_Text` | `Text_Survival` | 기존. `survivalPrefix` + mm:ss (플레이 시간) |
| `killsText` | `TMP_Text` | `Text_Kills` | **런타임에 숨긴다**(팀 처치 합은 표시하지 않음). 씬에서 지워도 된다 |
| `playerRowContainer` | `Transform` | `PlayerRows` | 🆕 행이 붙을 부모 |
| `playerRowPrefab` | `ResultPlayerRowView` | — (자동 탐색 없음) | 🆕 **반드시 배선** |
| `characterRoster` | `CharacterRoster` | — (자동 탐색 없음) | 🆕 `Assets/9.ScriptableObject/Player/CharacterRoster.asset` 배선 |

- 행 프리팹·컨테이너가 비어 있으면 행을 그리지 않고 `LogWarning` 한 줄만 남긴다.
- 행은 `SessionResult.Players` 순서(clientId 오름차순, P1 = 호스트)로 `Start` 에서 한 번 복제된다.
  이름은 `PlayerRow_P1`, `PlayerRow_P2`, …. 🔴 **`PlayerRows` 아래에 디자인용 더미 행을 두지 말 것** — 코드는 자기가
  만든 행만 지우므로 더미가 그대로 남는다.

### `ResultPlayerRowView` (`Assets/1.Scripts/UI/ResultPlayerRowView.cs`) — 행 프리팹 루트에 붙인다
| 직렬화 필드 | 타입 | 이름으로 찾는 자식 | 내용 |
|---|---|---|---|
| `slotText` | `TMP_Text` | `Text_Slot` | `slotFormat`(기본 `P{0}`) — P1, P2, … |
| `characterText` | `TMP_Text` | `Text_Character` | 로스터 `DisplayName`, 모르면 `unknownCharacterLabel`(`-`) |
| `portraitImage` | `Image` | `Image_Portrait` | 로스터 `Portrait`. 없으면 Image 를 끈다 |
| `damageText` | `TMP_Text` | `Text_Damage` | 가한 데미지(정수) |
| `killsText` | `TMP_Text` | `Text_Kills` | 처치 수 |
| `countersText` | `TMP_Text` | `Text_Counters` | 간파 성공 |
| `deathsText` | `TMP_Text` | `Text_Deaths` | 사망 횟수 |
| `localMarker` | `GameObject` | `Marker_Local` | 로컬 플레이어 행만 활성("나" 강조 — 테두리/배경/"나" 라벨 등) |

- 숫자 텍스트에는 값만 들어간다. 열 제목(데미지·처치·간파·사망)은 아래 헤더 행에 둔다.
- 참조를 직렬화로 꽂아 두는 것을 권장(이름 탐색은 폴백).

## 만들 것

### 1. 행 프리팹 `Assets/2.Prefabs/UI/Result/ResultPlayerRow.prefab`
```
ResultPlayerRow            RectTransform · ResultPlayerRowView · HorizontalLayoutGroup(권장) · LayoutElement(높이 고정)
├─ Marker_Local            Image(강조 배경) — 기본 비활성 · 레이아웃 무시(LayoutElement.ignoreLayout) 또는 맨 뒤 배경
├─ Text_Slot               TMP_Text
├─ Image_Portrait          Image (preserveAspect)
├─ Text_Character          TMP_Text
├─ Text_Damage             TMP_Text
├─ Text_Kills              TMP_Text
├─ Text_Counters           TMP_Text
└─ Text_Deaths             TMP_Text
```
- 폰트·크기는 기존 `Text_Outcome`/`Text_Survival` 의 TMP 폰트 에셋을 그대로 쓴다.

### 2. `5.ResultScene.unity`
```
Canvas
└─ ResultStats (ResultStatsView)
   ├─ Text_Outcome          (기존)
   ├─ Text_Survival         (기존)
   ├─ Text_Kills            (기존 — 지우거나 그대로 두면 런타임에 숨김)
   ├─ PlayerRowsHeader      🆕 열 제목 행(Text: 슬롯·캐릭터·데미지·처치·간파·사망) — 행 프리팹과 같은 열 폭
   └─ PlayerRows            🆕 RectTransform · VerticalLayoutGroup · (ContentSizeFitter 선택) — 자식 비움
```
- `ResultStatsView` 에 `playerRowContainer = PlayerRows`, `playerRowPrefab = ResultPlayerRow.prefab`,
  `characterRoster = CharacterRoster.asset` 를 꽂는다.
- 최대 행 수 = `SessionResultPayload.MaxPlayers`(8). 실사용 3인 기준으로 배치하되 넘쳐도 깨지지 않게(레이아웃 그룹).

## 저작 메뉴 요구사항 (멱등)
- 메뉴 예: `Tools/UI/Authoring/Result Stats Rows`. 에디터 스크립트는 `Assets/1.Scripts/UI/Editor/`.
- **여러 번 눌러도 결과가 같아야 한다**: 이미 있는 오브젝트·프리팹은 이름으로 찾아 재사용하고 값만 맞춘다(중복 생성 금지).
- 프리팹은 `PrefabUtility.SaveAsPrefabAsset`, 씬은 `EditorSceneManager.MarkSceneDirty` + `SaveScene`.
- 씬 안 `ResultStatsView` 는 `FindObjectsByType<ResultStatsView>` 로 찾고, 0 개/2 개 이상이면 에러로 멈춘다.
- 끝에 무엇을 만들었는지/재사용했는지 콘솔에 한 줄씩 남긴다.
- CLAUDE.md 규칙: 씬·프리팹은 텍스트 에셋 한두 개라 에디터를 켠 채로 해도 되지만, **Play 중이면 먼저 정지**.
  저장 뒤 `.meta` guid 가 바뀌지 않았는지 확인.

## 확인
- `Tools/Tests/결과 통계 EditMode 테스트 실행` 통과 수.
- ⏳ 사람 Play(MPPM 3인): 클리어/전멸/ExitButton 3경로에서 호스트·클라 결과 화면 값이 같은지, "나" 강조가 각 피어에서 자기 행인지.
