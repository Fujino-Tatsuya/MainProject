# 로비 튜토리얼 UI (할 일 158)

로비 튜토리얼(명세 `로비_튜토리얼_플로우.md` v1.0 §3·§4)의 **표시 쪽**만 다룬다 — 좌상단 목표 패널, 감독관 대사 패널·삽화,
단계별 바닥 경로 묶음, 집결 구역 민트 테두리, 벽 활용 표식. 상태 흐름·서버 판정·집결 집계는 **할 일 157**(이 문서 범위 밖).

- 표시 컴포넌트는 아래 **입력 API** 만 받는다. 157 상태 스냅샷 → API 호출 바인더(`LobbyTutorialUiBinder`, C10)는 157 계약이 온 뒤 붙인다.
- 네트워크 없음 — 각 피어가 복제된 상태를 읽어 자기 화면에 그린다. 새 NetworkObject·RPC 없음.
- 로비 씬 연결(C8)은 157 이 development 에 병합된 뒤 별도 커밋. 그전엔 프리팹을 빈 씬에 놓고 디버그 구동으로 본다.

## 입력 API — `LobbyTutorialUI.Instance`

| 호출 | 하는 일 |
|---|---|
| `ShowObjective(LobbyObjectiveView)` | 목표 패널 내용 교체. 대사 중이면 내용만 바꾸고 대사·유예가 끝난 뒤 보인다 |
| `HideObjective()` | 목표 패널 끔 |
| `ShowDialogue(speakerKey, lineKey, background)` | 감독관 대사 한 줄(연속 대사는 다시 부른다). `background` = `None`/`Factory`/`Rebellion` |
| `EndDialogue()` | 대사·삽화 닫기. 목표 패널은 유예(기본 0.3초) 뒤 다시 보인다 |
| `SetPath(pathId)` | 그 ID 의 바닥 경로만 켬. null·빈 값 = 전부 끔 |
| `SetWallTarget(wallId, visible)` | 지정 벽 표식 켜고 끔 |
| `ClearAll()` | 씬 종료·회차 초기화 — 유예 없이 전부 끔 |

`LobbyObjectiveView` = `ChapterKey`·`ObjectiveKey`·`BodyKeys[]`·`Checklist[(LabelKey, Done)]`·`Progress(Kind, N, Total)`.
키는 전부 Strings 키(`LobbyTutorialUiStrings`). 목록에 없는 키는 Strings 시트에서 찾고, 그래도 없으면 키 글자 그대로 보인다(빈 칸 방지).
진행 `Kind` = `None`(구분선·진행 줄 끔) / `TrainingDone`(훈련 완료 n/N) / `Gather`(집결 n/N) / `Countdown`(시작까지 n). 분모 0·카운트 0 이하는 숨김.

집결 테두리는 입력이 따로 없다 — `PartyGatherZone` 복제값(활성 ∧ 확정 전)을 그대로 읽는다.

## 프리팹 · 저작 메뉴

| 프리팹 | 메뉴(멱등 — 다시 돌리면 기본값으로 맞춤) |
|---|---|
| `Assets/2.Prefabs/UI/Lobby/Tutorial/LobbyTutorialUI.prefab` | `Tools/UI/로비 튜토리얼 UI 구성` |
| `Assets/2.Prefabs/Lobby/Tutorial/LobbyTutorialMarkers.prefab` (+ `M_LobbyGatherBorder.mat`) | `Tools/Lobby/로비 튜토리얼 마커 구성` |

### `LobbyTutorialUI.prefab`

- 별도 **Overlay 캔버스**(sortingOrder 50, CanvasScaler 1920×1080 match 0.5). **GraphicRaycaster 없음** + 모든 Graphic `raycastTarget` 끔 — 클릭을 막지 않는다.
  CombatHUD·맵씬 공용 `TutorialGuideUI` 와 별개(둘 다 손대지 않았다). 글꼴 = `SeoulAlrimTTF-Medium SDF`(TutorialGuideUI 와 같음).
- `Illustration`(맨 뒤, 전면) → `SafeArea`(`LobbySafeAreaFitter` = `Screen.safeArea`) › `ObjectivePanel` · `DialoguePanel`, `DebugDriver`(꺼 둠).
- **목표 패널** `LobbyObjectivePanelView` — 좌상단 앵커 (0,1), 여백 좌 44·위 40(Safe Area 안). 폭 560, 설명 줄이 있으면 680. 높이는 줄바꿈으로 늘어남(ContentSizeFitter 세로).
  패딩 24, 순서 = 챕터(23, `#F0C870`)·지시문(32 굵게, 흰색)·설명(24)·체크(24)·구분선(2px `#E3B95A`)·진행(24, 숫자만 `#F0C870`). 글자 자동 축소 끔.
  배경 = `50.Art/UI/Tutorial/TextBox.png` Sliced(픽셀 배율 1.5, 색 알파 0.8 — 금테도 같이 옅어진다). 색·폭·알파는 인스펙터.
  - 체크 완료 행 = 체크 글자색을 `doneDarken`(0.5)만큼 어둡게 + 취소선, 글리프 `□`/`■`(승인 결정 — 금색 체크 안 씀).
  - `{tag:Knockback}`·`{tag:Stun}` = `[넉백]`·`[기절]`, 색 = `SkillTooltipTagStyles.asset`(스킬 툴팁과 같은 원본). `{tag:Suppress}` = `[제압]`, 색 = 인스펙터.
- **대사 패널** `LobbyDialoguePanelView` — 가로 20–80%, 세로(위에서) 64–84%. 배경 TextBox(α0.92), 이름표 `NameTag.png`(왼쪽 위 테두리에 걸침, 이름 `감독관`),
  인물화 `Operator.png`(오른쪽 끝에 겹쳐 서고 위로 솟음, 본문은 오른쪽 여백 330 으로 피함). 입력 버튼·진행 아이콘 없음.
  삽화 `Scene_01`(Factory, 생산 시설)·`Scene_02`(Rebellion, 반란) = 전면 Preserve Aspect(16:9 외 비율은 검정 레터박스), 0.4초 디졸브. 패널 페이드 0.15초.
- **대사 중 목표 숨김** — `LobbyDialogueObjectiveGate`: 대사 중 + 끝난 뒤 `dialogueGraceSeconds`(0.3) 동안 숨김. 유예 안에 다음 대사가 오면 계속 숨김(연속 대사 사이 깜빡임 방지).

### `LobbyTutorialMarkers.prefab`

- 루트 `LobbyTutorialPathSet` — 경로 ID ↔ `LobbyFloorArrowPath` 목록. 자식 `Paths/Path_<ID>` 8개 = 할 일 111 `LobbyFloorArrowPath.prefab` **중첩 인스턴스**
  (모양·셰이더·색 그대로 — 순차 발광 펄스·`_PieceIndex` 같은 셰이더 변경 없음, 승인 결정). 배치 전 구분용으로 2m 씩 벌려 둠 — **경로 점 좌표는 사람이 씬에서 옮긴다**(111 과 같은 굽기).
  - ID = `LobbyTutorialPathIds`: `Locker`·`Central`·`TrainingInside`·`Combat`·`Counter`·`Exit`·`BriefCentral`·`Sortie`(157 계약이 다른 이름을 정하면 그쪽에 맞춘다).
  - 켜질 때 전부 끔. 경로를 켠 동안 `legacyGuideRoots`(씬의 옛 `LobbyGuideSteps` 루트를 넣는다)를 끄고, 빈 ID 면 되돌린다 — 옛 3스텝 안내와 동시에 안 켜진다.
- 자식 `GatherBorders` = `LobbyGatherZoneBorders` + 꺼 둔 Quad 풀 8개(`M_LobbyGatherBorder`, 셰이더 `Lobby/GatherZoneBorder`).
  매 프레임 `PartyGatherZone.Spawned` 중 **활성 ∧ 확정 전** 구역의 BoxCollider 바닥 면에 테두리를 놓는다(논리 트리거 = 시각 footprint). 폭 0.18m, 띄움 0.02m, 민트(인스펙터).
  씬의 `Gather_*` 는 손대지 않는다. 런타임 오브젝트·메시 생성 없음. 숫자·카운트다운 없음. 배치 규칙 = `LobbyGatherBorderLayout`(EditMode).

### 벽 활용 표식

- 셰이더 `Lobby/WallTargetMarker` — 판(큐브)의 가장 얇은 축을 두께로 보고 나머지 두 축(m)으로 금색 빛 테두리(약한 맥동) + 낮은 밝기 사선 줄무늬. 텍스처 없음.
- `Assets/3.Materials/Lobby/MA_Train03DesignatedWall.mat` 의 **셰이더·색만 교체**(guid `2325cf83…` 유지) — 씬의 `DesignatedWall_West_Marker` 는 그대로, 표시 조건도 기존 `LobbySparringLink.WallMarkerVisible`.
- `LobbyTutorialWallTarget`(TargetWall ID 로 렌더러 켜고 끔)은 157 계약(TargetWallImpact) 연결 때(C10) 마커에 붙인다. 지금은 어디에도 안 붙어 있다.
- 큐브가 벽에서 떠 보이면 위치·두께 조정은 씬(사용자/157).

## 디버그 구동 — `LobbyTutorialUIDebugDriver`

`LobbyTutorialUI.prefab` 의 `DebugDriver`(꺼 둠)를 Play 중 켜고 인스펙터 ⋮ 메뉴 `다음 장면`·`이전 장면`·`처음 장면`·`전부 끄기`.
명세 화면 01–27(21장면)의 목표·대사·삽화·경로 ID·벽 표식(화면 17)을 순서대로 재현한다. 단축키 없음, `UNITY_EDITOR || DEVELOPMENT_BUILD` 에서만 동작.
`partySize`(기본 3) = 진행 분모, `wallId` = 화면 17 에서 켤 벽 ID.

## 문구 키 — `LobbyTutorialUiStrings`

`UI.Lobby.Tutorial.*`(챕터·지시문·설명·체크·진행·태그·이름표) + `Tutorial.Guide.LOBBY_DLG_*`(감독관 대사 6줄). `UiStrings.All` 에 들어가 Export Template 이 키를 모은다.
**Strings 시트 행은 아직 없다**(ko 가 화면 폴백) — xlsx 행 추가는 SVN 쪽. 대사 키를 TutorialGuideTexts SO 에는 넣지 않았다(지속시간은 157 서버가 정한다).
157 이 대사 SO 행을 같은 `LOBBY_DLG_*` id 로 만들면 Export 에서 키가 겹칠 수 있다 — 그때 한쪽으로 합친다.

## 아트 (SVN `Assets/50.Art/UI/Tutorial/`, r560)

| 파일 | 임포트 | 쓰는 곳 |
|---|---|---|
| `TextBox.png` | Sprite Multiple 1장(투명 여백 자른 Rect x38 y183 w2004 h432, Border 64 사방), mip 끔, Max 2048 | 목표·대사 패널 배경 |
| `NameTag.png` | Rect x68 y229 w2036 h265, Border L/R 212·T/B 0 | 이름표 |
| `Operator.png` | Single, mip 끔, Max 2048 | 인물화 |
| `Scene_01.png` / `Scene_02.png` | Single, mip 끔, 알파 없음 | 삽화 Factory / Rebellion |

`.meta` 6개(폴더 포함) 원본은 Work4 에서 만든 것 — 다른 워크트리에서 새로 만들지 말 것(guid 갈림). 프리팹이 이 guid 를 참조한다.

## 검증

- EditMode(맵씬 러너 `Tools/Tests/맵씬 EditMode 테스트 실행`, 픽스처 `^LobbyTutorialUi\w*Tests$`): `LobbyTutorialUiRulesTests`·`LobbyTutorialUiPathSetTests`·`LobbyTutorialUiGatherBorderTests`.
- **사람이 Play 확인**: 1920×1080·1280×720·21:9·4:3 Game 뷰에서 목표 패널이 미니맵·특성·체력·스킬·키 가이드와 안 겹치는지, 대사 인물화가 키 가이드·스킬을 안 가리는지,
  디버그 구동으로 화면 01–27 대조(Scene_01/02 매핑 포함), 민트 테두리 = 트리거 박스(Scene 뷰 기즈모와 겹쳐 보기), 벽 표식 밝기·떠 보임, `□`/`■` 글리프 표시.
