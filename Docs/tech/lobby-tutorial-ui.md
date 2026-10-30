# 로비 튜토리얼 UI (할 일 158)

로비 튜토리얼(명세 `로비_튜토리얼_플로우.md` v1.0 §3·§4)의 **표시 쪽**만 다룬다 — 좌상단 목표 패널, 감독관 대사 패널·삽화,
단계별 바닥 경로 묶음, 집결 구역 민트 테두리, 벽 활용 표식. 상태 흐름·서버 판정·집결 집계는 **할 일 157**(이 문서 범위 밖).

- 표시 컴포넌트는 아래 **입력 API** 만 받는다. 157 상태 파사드(`LobbyTutorialState`) → API 호출은 바인더 `LobbyTutorialUiBinder`(C10, 아래 절) 한 곳.
- 네트워크 없음 — 각 피어가 복제된 상태를 읽어 자기 화면에 그린다. 새 NetworkObject·RPC 없음.
- 로비 씬 연결(C8) 완료 — `3.LobbyScene` 루트에 두 프리팹 인스턴스(아래 "로비 씬 연결").
- 157 의 과도기 어댑터 `LobbyTutorialGuideAdapter.cs`(상단 `TutorialGuideUI` 에 목표·자막을 띄우던 것)는 C10 에서 **삭제**했다(계약 §5). 로비의 `TutorialGuideUI` 인스턴스는 남아 있지만 튜토리얼 목표·대사는 더 이상 거기 뜨지 않는다.

## 입력 API — `LobbyTutorialUI.Instance`

| 호출 | 하는 일 |
|---|---|
| `ShowObjective(LobbyObjectiveView)` | 목표 패널 내용 교체. 대사 중이면 내용만 바꾸고 대사·유예가 끝난 뒤 보인다 |
| `HideObjective()` | 목표 패널 끔 |
| `ShowDialogue(speakerKey, lineKey, background)` | 감독관 대사 한 줄(연속 대사는 다시 부른다). `background` = `None`/`Factory`/`Rebellion` |
| `ShowDialogue(speakerKey, lineKey, backgroundId)` | 같음 — 157 단계표 `backgroundId` 글자(`BRIEF_BG_FACTORY`·`BRIEF_BG_REBELLION`, 빈 값 = 없음) 그대로 |
| `EndDialogue()` | 대사·삽화 닫기. 목표 패널은 유예(기본 0.3초) 뒤 다시 보인다 |
| `SetPath(pathId)` | 그 ID 의 바닥 경로만 켬. null·빈 값 = 전부 끔 |
| `SetWallTarget(wallId, visible)` | 지정 벽 표식 켜고 끔 |
| `SetLockerArea(visible)` | L00 라커룸 영역 바닥 민트 테두리 켜고 끔(173) |
| `SetTutorialActive(active)` | 튜토리얼 진행 중 — 경로 없는 단계(대사·훈련)에도 옛 3스텝 안내(`LobbyGuideSteps`)를 끈다 |
| `ClearAll()` | 씬 종료·회차 초기화 — 유예 없이 전부 끔(옛 안내 억제도 푼다) |

`LobbyObjectiveView` = `ChapterKey`·`ObjectiveKey`·`BodyKeys[]`·`Checklist[(LabelKey, Done)]`·`Progress(Kind, N, Total)`.
키는 전부 Strings 키(`LobbyTutorialUiStrings`). 목록에 없는 키는 Strings 시트에서 찾고, 그래도 없으면 키 글자 그대로 보인다(빈 칸 방지).
157 단계표 칸 값(`LOBBY_OBJ_CENTRAL` 등 접두 없는 id)은 `LobbyTutorialUiStrings.KeyOf(id)` 로 `Tutorial.Guide.<id>` 키를 만들어 넘긴다.
진행 `Kind` = `None`(구분선·진행 줄 끔) / `Gather`(집결 n/N) / `PartyComplete`(훈련 완료 n/N) / `Countdown`(시작까지 n) — 이름·값(0~3) = 157 `LobbyTutorialProgressMode`. 분모 0·카운트 0 이하는 숨김.

### 157 계약 이름 대응 (계약 = 157 `Docs/tech/handoff-lobby-tutorial-contract.md`)

| 158 입력 | 값(= 157 단계표 `LobbyTutorialSteps.asset` 칸) |
|---|---|
| 경로 `LobbyTutorialPathIds` | `PATH_LOCKER`(L00)·`PATH_CENTRAL`(L10·L30 공용)·`PATH_TRAINING_HALL`(L20 — 끝점 = 훈련장 문 앞 입구 `Gather_Entrance`, `Train01Start` 아님)·`PATH_COMBAT_AREA`(T20)·`PATH_COUNTER_AREA`(T30)·`PATH_TRAINING_EXIT`(T50)·`PATH_SORTIE`(L40) |
| 벽 `LobbyTutorialWallIds` | `WALL_TRAIN_WEST`(T34) |
| 삽화 `LobbyTutorialBackgroundIds` | `BRIEF_BG_FACTORY`(L32)·`BRIEF_BG_REBELLION`(L33) |
| 대사 id | `LOBBY_DLG_INTRO`(L11)·`LOBBY_DLG_TRAINING`(L21)·`LOBBY_BRIEF_EVAL`·`LOBBY_BRIEF_FACTORY`·`LOBBY_BRIEF_REBELLION`·`LOBBY_BRIEF_SORTIE`(L31~L34), 화자 `LOBBY_SPEAKER_SUPERVISOR` |
| 문구 id | `LOBBY_TITLE_*` 9·`LOBBY_OBJ_*` 12·`LOBBY_BODY_*` 4·`LOBBY_CHECK_*` 6·`LOBBY_PROGRESS_*` 3 |
| 집결 | 입력 없음 — 테두리는 `PartyGatherZone` 복제값(활성 ∧ 확정 전)만 읽는다. `LobbyGatherPoint` 이름에 의존하지 않는다(L20 활성 구역 = `Entrance` — 확정 뒤 명단 전원 `Train01Start` 로 텔레포트, 내부 재집결 없음. 계약 198d483a) |

표시 컴포넌트(패널·경로 묶음·벽 표식)는 157 코드에 컴파일 의존하지 않는다 — 이름·값만 맞췄다. 157 을 읽는 건 바인더 하나다.

집결 테두리는 입력이 따로 없다 — `PartyGatherZone` 복제값(활성 ∧ 확정 전)을 그대로 읽는다.

## 157 바인더 — `LobbyTutorialUiBinder` (C10)

`LobbyTutorialUI.prefab` 루트 컴포넌트(저작 메뉴가 붙임). 전 피어 로컬, **매 프레임 파사드를 읽어 바뀐 것만** API 로 부른다(폴링 — 늦게 붙은 클라·이벤트 놓침도 같은 결과). StateId 문자열로 분기하지 않고 Kind·키 칸·진행 모드만 읽는다.

| 파사드(`LobbyTutorialState`) | → API |
|---|---|
| `IsAvailable` false | 아무것도 안 그림. 그려 둔 게 있으면 `ClearAll()` 한 번(진행기 디스폰·로비 이탈) |
| `IsAvailable` true 가 된 첫 프레임 | `SetTutorialActive(true)` — 진행 중 내내 옛 3스텝 안내 끔 |
| `ObjectivePanelVisible` ∧ `ObjectiveKey` | `ShowObjective` — 챕터 `TitleKey`·지시문 `ObjectiveKey`(T21 카운트다운 중엔 파사드가 바꿔 줌)·설명 `BodyKeys`·T10 체크(`TryGetLocalChecklist`, 비트 = `LobbyWeaponCheckItem` 순서)·진행 줄(`ProgressMode` + `TryGetProgress`). 아니면 `HideObjective` |
| 집결 n/N | 진행 줄 `집결 {n}/{total}` — 옛 상단 `PartyGatherStatusView`(`Slot_GatherStatus`, 씬에서 꺼 둠) 대체 |
| `IsDialogueActive` + `Dialogue` | (runId, revision) 이 바뀔 때마다 `ShowDialogue(화자, 대사, BackgroundId)` — 연속 대사(L31→L34)는 끄지 않고 다시 부름. 대사 단계를 떠나면 `EndDialogue` |
| `ActivePathId` | 바뀌면 `SetPath` (빈 값 = 전부 끔) |
| `ActiveTargetWallId` | 바뀌면 이전 벽 `SetWallTarget(false)` → 새 벽 `SetWallTarget(true)` |
| `CurrentStep.Kind == LockerVisit`(∧ 출격 확정 전) | 바뀌면 `SetLockerArea` — L00 동안 라커룸 테두리, 전원 방문 → L10 이면 꺼짐(`LobbyTutorialUiBinding.LockerAreaVisible`) |

- 단계표 칸 값(접두 없는 id)은 `LobbyTutorialUiStrings.KeyOf` 로 `Tutorial.Guide.<id>` 키. 화자 칸이 비면 `LOBBY_SPEAKER_SUPERVISOR`.
- 목표 패널은 서명(`LobbyObjectiveSignature` = runId·revision·지시문 id·진행 모드·체크 마스크·n/N)이 같으면 다시 그리지 않는다(매 프레임 할당 없음).
- 같은 프리팹의 `DebugDriver` 가 켜져 있으면 바인더가 손을 뗀다(로비 Play 중 디버그 화면 확인 가능).
- 로컬 플레이어 Transform 을 캐시하지 않는다 — 캐릭터 교체(계약 §6) 때 할 일 없음.
- 순수 매핑 = `LobbyTutorialUiBinding`(EditMode `LobbyTutorialUiBinderTests`).
- **벽 표식(173)**: 씬 `LobbyTraining/Train03Arena/DesignatedWall_West_Marker` 에 `LobbyTutorialWallTarget`(`WALL_TRAIN_WEST`)을 붙이고 아레나 `wallMarkers` 에서 뺐다 —
  표시 = 바인더 `ActiveTargetWallId`(T34 행만 값이 있음) 한 길. 넉백 유효 충돌로 T34 가 끝나는 순간 ID 가 비어 꺼진다(명세 4.3 "성공 즉시 종료"). 옛 `LobbySparringLink.WallMarkerVisible`(T33·T34)은 마커가 비어 이제 아무것도 켜지 않는다(코드는 157 영역이라 그대로).

## 로비 씬 연결 (C8, `Assets/0.Scenes/MainFlow/3.LobbyScene.unity`)

| 변경 | 대상 |
|---|---|
| 추가 | 루트 `LobbyTutorialUI` 인스턴스(바인더 포함, 오버라이드 = 루트 RectTransform·이름만) |
| 추가 | 루트 `LobbyTutorialMarkers` 인스턴스 — `legacyGuideRoots` = 씬 `LobbyGuideSteps` 루트, `GatherBorders.lockerArea` = 씬 `Area_Locker` BoxCollider(173), 경로 7개 점 + 구운 조각 |
| 비활성(삭제 아님) | `UI/Canvas/Panel_PlayerSlots`(상단 파티 슬롯) · `Slot_GatherStatus`(상단 집결 n/N) · `Slot_WeaponCheck`(왼쪽 무기 점검) · `Slot_Train02`(남은 상대) |
| 173 수정 | `Train03Arena/DesignatedWall_West_Marker` — 켬(렌더러는 `LobbyTutorialWallTarget` 이 끄고 켬)·x −19.67 → −19.855(실제 벽면 x −19.89 에 밀착)·아레나 `wallMarkers` 비움 |
| 무수정 | `Gather_*`·`Area_Locker`·루트 `TutorialGuideUI`·`LobbyGuideSteps`·NavMesh |

**경로 점(173 정리)** — 월드 XZ, 바닥 축을 따르는 각진 구간. 로비 쪽은 NavMesh 가 없어(훈련장만 구움) 바닥 Raycast(y 0)와 플레이어 크기 CapsuleCast(반지름 0.25, 높이 0.4–1.5)로
가구·벽에 안 걸리는지 확인했다(`Door_Locker` = 근접 자동문이라 통과로 본다). 158 임시 배치는 라커·중앙·출격 경로가 중앙 콘솔 책상(`Control_R1/R2_*`)을 뚫고 지나갔다 —
책상 서쪽 통로(x −2.8, 111 옛 안내 경로가 쓰던 길)로 돌렸다. 점을 옮기면 자동 다시 굽기(111 과 같음).

| 경로 | 화면 | 점 | 끝 기준 |
|---|---|---|---|
| `PATH_LOCKER` | 01–02 | (4.5,−11.5) 입구 홀 스폰 → (4.5,−5) → (−2.8,−5) → (−2.8,2) → (−6.5,2) | 라커룸 문(−4.5,2) 안 `Area_Locker` |
| `PATH_CENTRAL` | 03–04 · 20–21 | (−6.5,2) → (−2.8,2) → (−2.8,−5.16) → (1.61,−5.16) | `Gather_Brief`(중앙) 중심. L30 은 출구 확정 텔레포트로 이미 중앙이라 같은 경로를 다시 켜도 짧게만 보인다 |
| `PATH_TRAINING_HALL` | 06–07 | (1.61,−6) → (−4.5,−6) → (−10.25,−6) | **`Gather_Entrance`**(훈련장 문 앞) 중심 |
| `PATH_COMBAT_AREA` | 10 | (−14.5,−5.5) → (−16,−5.5) → (−16,0) | `Gather_Train02Start` |
| `PATH_COUNTER_AREA` | 13–14 | (−16,1.5) → (−16,3) → (−16,5) | `Gather_Train03Area` |
| `PATH_TRAINING_EXIT` | 19 | (−16,2.5) → (−16,−6) → (−13.75,−6) | `Gather_TrainEnd`(출구) |
| `PATH_SORTIE` | 26–27 | (1.61,−3.4) → (−2.8,−3.4) → (−2.8,3) → (1.7,3) → (1.7,4.6) | `Gather_Sortie` 남쪽 안, 진단 포드 둘 사이·발사대(`Floor_1`, z 4.93~) 바로 앞 |

- 에디터에서 씬을 열면 레이아웃·TMP 가 인스턴스에 구동 값 오버라이드(`m_TextStyleHashCode`·자식 RectTransform 앵커 0)를 다시 만들 수 있다 — 저장 전 diff 에서 빼면 된다(동작 무관).

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

- 루트 `LobbyTutorialPathSet` — 경로 ID ↔ `LobbyFloorArrowPath` 목록. 자식 `Paths/<경로 ID>` 7개 = 할 일 111 `LobbyFloorArrowPath.prefab` **중첩 인스턴스**
  (모양·셰이더·색 그대로 — 순차 발광 펄스·`_PieceIndex` 같은 셰이더 변경 없음, 승인 결정). 배치 전 구분용으로 2m 씩 벌려 둠 — **경로 점 좌표는 사람이 씬에서 옮긴다**(111 과 같은 굽기).
  - ID = `LobbyTutorialPathIds` = 157 단계표 `pathId`(위 대응표). L10·L30 은 같은 `PATH_CENTRAL`.
  - 켜질 때 전부 끔. 경로를 켠 동안 + 튜토리얼 진행 중(`SetTutorialActive`) `legacyGuideRoots`(씬의 옛 `LobbyGuideSteps` 루트)를 끄고, 둘 다 풀리면 되돌린다 — 옛 3스텝 안내와 동시에 안 켜진다(157 진행기가 없는 로비 = 옛 안내 그대로). 판정 = `LobbyTutorialPathRules.SuppressLegacy`.
- 자식 `GatherBorders` = `LobbyGatherZoneBorders` + 꺼 둔 Quad 풀 8개(`M_LobbyGatherBorder`, 셰이더 `Lobby/GatherZoneBorder`).
  매 프레임 `PartyGatherZone.Spawned` 중 **활성 ∧ 확정 전** 구역의 BoxCollider footprint(XZ)에 테두리를 놓는다(논리 트리거 = 시각 footprint). 폭 0.18m, 띄움 0.02m, 민트(인스펙터).
  L00 동안은 `lockerArea`(씬 `Area_Locker`)도 같은 규칙으로 그린다(`SetLockerArea`).
  씬의 `Gather_*` 는 손대지 않는다. 런타임 오브젝트·메시 생성 없음. 숫자·카운트다운 없음. 배치 규칙 = `LobbyGatherBorderLayout`(EditMode).
  - **높이 = 구역 오브젝트 원점(로컬 y 0)**, 박스 바닥 면이 아니다(173). 씬의 `Gather_*`·`Area_Locker` 는 원점이 바닥(y 0)이고 박스가 center.y 1·size.y 3 이라 바닥 아래 0.5m 까지 내려간다 —
    158 은 박스 바닥 면 + 0.02 = y −0.48 에 놓아 **테두리가 바닥 밑에 묻혀 Play 에서 안 보였다**(풀·셰이더·머티리얼·활성 조건은 정상이었다).

### 벽 활용 표식

- 셰이더 `Lobby/WallTargetMarker` — 판(큐브)의 가장 얇은 축을 두께로 보고 나머지 두 축(m)으로 금색 빛 테두리(약한 맥동) + 낮은 밝기 사선 줄무늬. 텍스처 없음.
- `Assets/3.Materials/Lobby/MA_Train03DesignatedWall.mat` 의 **셰이더·색만 교체**(guid `2325cf83…` 유지).
- 씬 `DesignatedWall_West_Marker` 에 `LobbyTutorialWallTarget`(`WALL_TRAIN_WEST`) — 바인더가 `ActiveTargetWallId`(T34)로 켜고 끈다(173, 위 "벽 표식").
- 위치(173): 판정용 `DesignatedWall_West` 콜라이더 앞면(x −19.70)이 실제 벽 메시 면(x −19.89)보다 0.19m 앞에 있어, 콜라이더에 붙어 있던 마커가 벽에서 떠 보였다 → 마커 뒷면을 벽 메시 면 5mm 앞(x −19.885)으로 옮겼다. 판정 콜라이더는 그대로.

## 디버그 구동 — `LobbyTutorialUIDebugDriver`

`LobbyTutorialUI.prefab` 의 `DebugDriver`(꺼 둠)를 Play 중 켜고 인스펙터 ⋮ 메뉴 `다음 장면`·`이전 장면`·`처음 장면`·`전부 끄기`. 켜져 있는 동안 바인더는 손을 뗀다(끄면 다음 프레임에 157 상태로 전부 다시 그린다).
명세 화면 01–27(21장면)의 목표·대사·삽화·경로 ID·벽 표식(화면 17)·라커룸 테두리(라커 경로 장면)를 순서대로 재현한다. 단축키 없음, `UNITY_EDITOR || DEVELOPMENT_BUILD` 에서만 동작.
`partySize`(기본 3) = 진행 분모, `wallId` = 화면 17 에서 켤 벽 ID(기본 `WALL_TRAIN_WEST`).

## 문구 키 — `LobbyTutorialUiStrings`

`Tutorial.Guide.LOBBY_*`(157 계약과 같은 id — 제목·지시문·설명·체크·진행·화자·대사) + 158 전용 `UI.Lobby.Tutorial.Tag*`(`{tag:ID}` 표기 3개).
`UiStrings.All` 에 들어가 Export Template 이 키를 모은다. Strings 시트 행은 `GameData.xlsx` SVN r570 에 들어갔다(`LOBBY_*` 41행 + `UI.Lobby.Tutorial.Tag*` 3행). 시트에 없는 키는 ko 가 화면 폴백.
157 은 같은 id 를 `TutorialGuideTexts` SO 에도 둔다(지속시간·어댑터용). Export 는 같은 키를 처음 것만 남기므로 행이 둘 생기지 않는다.
단 ko 폴백 문구가 다르다 — 158 은 `LOBBY_OBJ_VULN_CC`·`LOBBY_OBJ_WALL`·`LOBBY_BODY_SUPPRESS` 에 `{tag:Knockback}`·`{tag:Stun}`·`{tag:Suppress}` 자리표시자(색 태그),
157 SO 는 `[넉백]` 같은 평문. 시트 행을 만들 때 `{tag:…}` 쪽으로 맞춰야 색이 나온다.

## 아트 (SVN `Assets/50.Art/UI/Tutorial/`, r560)

| 파일 | 임포트 | 쓰는 곳 |
|---|---|---|
| `TextBox.png` | Sprite Multiple 1장(투명 여백 자른 Rect x38 y183 w2004 h432, Border 64 사방), mip 끔, Max 2048 | 목표·대사 패널 배경 |
| `NameTag.png` | Rect x68 y229 w2036 h265, Border L/R 212·T/B 0 | 이름표 |
| `Operator.png` | Single, mip 끔, Max 2048 | 인물화 |
| `Scene_01.png` / `Scene_02.png` | Single, mip 끔, 알파 없음 | 삽화 Factory / Rebellion |

`.meta` 6개(폴더 포함) 원본은 Work4 에서 만든 것 — 다른 워크트리에서 새로 만들지 말 것(guid 갈림). 프리팹이 이 guid 를 참조한다.

## 검증

- EditMode(맵씬 러너 `Tools/Tests/맵씬 EditMode 테스트 실행`, 픽스처 `^LobbyTutorialUi\w*Tests$`): `LobbyTutorialUiRulesTests`·`LobbyTutorialUiPathSetTests`·`LobbyTutorialUiGatherBorderTests`·`LobbyTutorialUiBinderTests`.
- **사람이 Play 확인(바인더·씬)**: L00 부터 목표 패널 문구·T10 체크/훈련 완료 n/N·집결 n/N·T21 '시작까지 3·2·1'·대사 패널(L11·L21·L31~L34, L32/L33 삽화)·대사 중 목표 숨김·단계별 바닥 경로 하나만·옛 3스텝 화살표가 튜토리얼 중 안 보임·상단 파티 슬롯/집결/왼쪽 칸 안 보임·상단 `TutorialGuideUI` 에 튜토리얼 문구가 안 뜸·캐릭터 선택(라커) 정상·늦은 참가자 목표 숨김. 경로 점 위치는 임시.
- **사람이 Play 확인**: 1920×1080·1280×720·21:9·4:3 Game 뷰에서 목표 패널이 미니맵·특성·체력·스킬·키 가이드와 안 겹치는지, 대사 인물화가 키 가이드·스킬을 안 가리는지,
  디버그 구동으로 화면 01–27 대조(Scene_01/02 매핑 포함), 민트 테두리 = 트리거 박스(Scene 뷰 기즈모와 겹쳐 보기), 벽 표식 밝기·떠 보임, `□`/`■` 글리프 표시.
