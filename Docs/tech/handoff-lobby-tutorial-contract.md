# handoff — 로비 튜토리얼 157 ↔ 158 계약

> 할 일 157(진행·판정, 서버) ↔ 158(표시: 목표/대사 패널·바닥 경로·집결 테두리·벽 표식, Work4) 사이의 **읽기 계약**.
> 명세: 로비 튜토리얼 플로우 v1.0(2026-10-27) §2·§3·§4·§6. 157 계획: `157-PLAN.md` §2·§3·§10(레인 산출물).
> 작성 2026-10-30 · 157 커밋 ①. 이 문서가 157 PLAN §3 초안보다 우선한다.

## 0. 요약

- 158 은 **읽기만** 한다 — 진입점은 정적 파사드 **`LobbyTutorialState`**(`Assets/1.Scripts/Lobby/LobbyTutorialState.cs`) 하나.
- 서버가 단계를 정하고 **스냅샷 1개**(`LobbyTutorialSnapshot` = runId·revision·stepIndex·진입 시각·대사 시각·출격 확정)를 복제한다.
  문구 키·집결 지점·경로·벽·대사 ID 는 모든 피어가 같은 단계표 SO(`LobbyTutorialSteps.asset`)에서 `stepIndex` 로 읽는다.
- 모든 값은 전 피어에서 복제값으로 계산된다 — 늦게 붙은 클라도 같은 결과(이벤트를 놓쳐도 다음 폴링/이벤트 뒤 재조회로 맞는다).
- **캐릭터 잠금 없음**(사용자 결정 10-30) — 어느 단계든 라커에서 교체 가능. 158 은 교체 때문에 따로 할 일이 없다(파사드 값은 clientId 기준이라 그대로).
  (커밋 ③에서 옛 훈련 명단 잠금 `TrainingLocked` 제거 완료 — 남은 교체 거부는 출격 집결 카운트다운·출격 확정 뒤뿐.)
- **커밋 ②부터 진행기(`LobbyTrainingDirector`)가 `Publish`·`SetLiveSource` 한다** → 로비에 진행기가 있으면 `IsAvailable == true`. 옛 10단계 모델(`LobbyTrainingFlow`)은 삭제됐다.

## 1. 단계표 (22행, 행 순서 = 진행 순서)

명세 §2 상태 23개 중 `T21_CombatCountdown` 은 행이 아니라 **T20 의 표시 상태**(활성 구역 카운트다운 중).

| # | StateId | Kind | 집결 지점 | 비고 |
|---|---|---|---|---|
| 0 | L00_LockerVisit | LockerVisit | — | 라커 누적 방문(라커룸 내부 볼륨 `Area_Locker` = `LobbyAreaVolume`, 비네트워크 — 158 은 경로 끝점으로 참조 가능). 경로 `PATH_LOCKER` |
| 1 | L10_CentralGather | Gather | `Brief`(중앙 — 이름 유지. `Central` 개명은 미룸 — 158 코드 호환) | 확정 = 훈련 명단 고정 |
| 2 | L11_CentralIntro | Dialogue | — | `LOBBY_DLG_INTRO` |
| 3 | L20_TrainingGather | Gather | **`Entrance`**(문 앞 입구 영역) | **확정: A**(157 PLAN §10.3) — 입구 집결 확정 → 명단 전원 `Gather_Train01Start` 안으로 텔레포트 → 바로 L21(내부 재집결 없음). 활성 집결 테두리 = `Gather_Entrance`. **명세 §5 L20 의 '문 통과' 문구는 이 텔레포트로 대체**(`Door_Training` 막힘 유지 — Q4 B). `Train01Start` 는 도착지일 뿐 활성 구역이 되지 않는다 |
| 4 | L21_TrainingIntro | Dialogue | — | `LOBBY_DLG_TRAINING` |
| 5 | T10_Basic | Activity(BasicCheck) | — | 개인 6항목, 진행 = PartyComplete |
| 6 | T20_CombatGather | Gather | `Train02Start` | 유지 3초 = 카운트다운 → 표시 상태 T21 |
| 7 | T22_Combat | Activity(Combat) | — | 남은 상대 표시 없음 |
| 8 | T30_CounterGather | Gather | `Train03Area` | |
| 9 | T31_Counter | Activity(Counter) | — | |
| 10 | T32_VulnerableCC | Activity(VulnerableCc) | — | 보조 설명 2줄 |
| 11 | T33_ResetPosition | Activity(ResetPosition) | — | 자동 전환(입력 대기 없음) |
| 12 | T34_Wall | Activity(Wall) | — | 벽 `WALL_TRAIN_WEST` |
| 13 | T40_SuppressKill | Activity(SuppressKill) | — | 보조 설명 1줄 |
| 14 | T50_FreePractice | Gather(+FreePractice) | `TrainEnd` | 출구 집결 + 자유 연습 병행 |
| 15 | L30_BriefGather | Gather | `Brief`(중앙) | |
| 16~19 | L31_BriefIntro · L32_BriefFactory · L33_BriefRebellion · L34_SortieIntro | Dialogue | — | 입력 잠금 묶음, 배경 `BRIEF_BG_FACTORY`·`BRIEF_BG_REBELLION`(L32·L33) |
| 20 | L40_SortieGather | Gather | `Sortie` | 참여자 = 접속자 전원 |
| 21 | L41_Loading | Loading | — | 기존 로딩 흐름 |

- 행 칸 `trainingProfile`(None/Training) = 서버 훈련 보정(체력 하한) 적용 행 — L21·T10~T40 = Training. 158 은 읽지 않아도 된다.
- 158 은 **StateId 문자열로 분기하지 말고** Kind·키 칸을 읽는다(행 추가·순서 조정에 안전). 꼭 필요하면 `DisplayStateId` 비교.
- 경로 키 `PATH_*`·벽 키 `WALL_TRAIN_WEST`·삽화 키 `BRIEF_BG_*` 는 **157 이 정한 임시 이름**이다. 158 이 다른 이름을 원하면 단계표 SO 만 고치면 된다(코드 무관) — 브릿지로 알려 주면 157 이 반영.

## 2. 상태 읽기 API (`LobbyTutorialState`)

| API | 타입 | 뜻 |
|---|---|---|
| `IsAvailable` | bool | 진행기 Publish 있음·단계표 있음. false 면 158 은 아무것도 그리지 않는다 |
| `RunId` / `Revision` | int | 바뀌면 표식·경로를 다시 맞춘다 |
| `CurrentStep` | `LobbyTutorialStepView` | 행 사본: StateId·Kind·Activity·TitleKey·ObjectiveKey·BodyKeys·ChecklistKeys·ProgressMode·ProgressKey·GatherPointId·PathId·TargetWallId·DialogueId·SpeakerKey·BackgroundId·LocksInput |
| `DisplayStateId` | string | 보통 = StateId, T20 카운트다운 중 = `T21_CombatCountdown` |
| `TitleKey` / `ObjectiveKey` / `BodyKeys` | string / string / IReadOnlyList<string> | 카운트다운 중엔 ObjectiveKey = `LOBBY_OBJ_COMBAT` |
| `ObjectivePanelVisible` | bool | false = 진행기 없음·대사 단계·로딩·출격 확정 뒤·**내가 명단 밖 늦은 참가자** |
| `ProgressMode` | `LobbyTutorialProgressMode` | None·Gather·PartyComplete·Countdown(T20 카운트다운 중) |
| `TryGetProgress(out n, out total)` | bool | Gather = 활성 구역 모인 수/참여자, PartyComplete = 6항목 완료 인원/명단, Countdown = 남은 정수 초(올림). None·값 없음이면 false → 하단 줄 숨김 |
| `ProgressKey` | string | `LOBBY_PROGRESS_GATHER`(`{n}/{total}`) · `LOBBY_PROGRESS_PARTY_DONE` · `LOBBY_PROGRESS_COUNTDOWN`(`{n}`) |
| `TryGetLocalChecklist(out mask, out itemKeys)` | bool | T10 만 true. 비트 = `LobbyWeaponCheckItem` 값(`LobbyWeaponCheckRules.Has`), 키 순서 = 같은 값 순서 |
| `ActiveGatherPointId` | `LobbyGatherPoint` | None = 집결 없음 |
| `ActiveGatherZone` | `PartyGatherZone` | 테두리 footprint = 이 구역 BoxCollider(트리거 = 테두리 공유). `GatheredCount`·`ParticipantCount`·`IsCountdownActive`·`IsConfirmed` |
| `ActivePathId` | string | 비면 경로 끔. 같은 revision 동안 바뀌지 않는다 |
| `ActiveTargetWallId` | string | T34 만 값. 완료(다음 revision) 즉시 빈 값 |
| `CountdownRemainingSeconds` | double | T20 카운트다운 중만, 그 밖 0(유지 0.3초는 표시하지 않음) |
| `Dialogue` / `IsDialogueActive` | `LobbyTutorialDialogueView` / bool | DialogueId·SpeakerKey·BackgroundId·StartServerTime·EndServerTime·Revision. 대사 중엔 목표 패널 숨김 |
| `IsInputLockedForBriefing` | bool | L31~L34(표시 참고용 — 실제 잠금은 서버) |
| `LaunchCommitted` | bool | true 면 목표·경로·집결 표식 모두 정리 |
| `TrainingTarget` | `TrainingSparringBot`(null 가능) | 취약·제압·칸 표시용(기존 복제값) |

## 3. 이벤트 (전 피어, C# static event)

| 이벤트 | 언제 | 158 용도 |
|---|---|---|
| `StepChanged(prev, next, revision)` | revision·회차 변경(회차 첫 Publish 포함 — 그땐 `prev.IsValid == false`) | 패널 문구·경로·테두리·벽 교체 |
| `DialogueStarted(view)` / `DialogueEnded(dialogueId, revision)` | 대사 단계 진입/이탈 | 대사 패널 |
| `ProgressChanged()` | 구역 n/N·체크 마스크·카운트다운 정수 변경 | 하단 진행 줄 |
| `TargetWallCleared(wallId)` | 벽 키가 있던 단계를 떠날 때 | 벽 표식 즉시 끔 |

- 한 번의 전이 안 호출 순서: `DialogueEnded`(이전이 대사면) → `StepChanged` → `DialogueStarted`(다음이 대사면) → `TargetWallCleared`.
  연속 대사(L31→L34)는 Ended 직후 Started 가 같은 호출에서 온다 — **그 사이에 목표 패널을 켜지 말 것**(`ObjectivePanelVisible` 은 계속 false).
- 같은 revision 재Publish(대사 시각 갱신 등)는 이벤트 없이 값만 바뀐다 — 대사 끝 시각은 `Dialogue.EndServerTime` 을 매번 읽는다.
- 진행기 디스폰 때 `Clear()` — 대사 중이면 `DialogueEnded` 가 오고 `IsAvailable` 이 false 가 된다.

## 4. 문구 키

- 전부 `Tutorial.Guide.<id>` — 158 은 `TutorialGuideTextSource` 로 해석(시트 우선, SO 폴백, 기존 경로).
- id 목록·ko 문구 = 157 PLAN §6(제목 `LOBBY_TITLE_*` 9 · 지시문 `LOBBY_OBJ_*` 12 · 보조 `LOBBY_BODY_*` 4 · 체크 `LOBBY_CHECK_*` 6 · 진행 `LOBBY_PROGRESS_*` 3 · 화자/대사 `LOBBY_SPEAKER_SUPERVISOR`·`LOBBY_DLG_*` 2·`LOBBY_BRIEF_*` 4).
  커밋 ③: `TutorialGuideTexts.asset` 에 §6 ko 폴백 41행 추가(durationSeconds 0). xlsx Strings 행 추가는 메인/사람 작업 — 시트 행이 없으면 SO 폴백 문구가 보인다.
- **문구 안 `{tag:ID}` 표기**(`{tag:Knockback}`·`{tag:Stun}`·`{tag:Suppress}` — `LOBBY_OBJ_VULN_CC`·`LOBBY_OBJ_WALL`·`LOBBY_BODY_SUPPRESS`) — 158 이 색 처리, xlsx Strings 행도 이 표기. 과도기 어댑터는 평문 `[넉백]`·`[기절]`·`[제압]` 으로만 바꿔 띄운다(커밋 ⑥b).

## 5. 경계 — 157 이 하지 않는 것

- 패널/대사 프리팹·좌상단 배치·상단 파티 UI 제거·경로/테두리/벽 표식 렌더링·`PartyGatherStatusView` 대체 = **158**.
- 옛 왼쪽 안내 칸(`Slot_WeaponCheck`·`Slot_Train02` — `LobbyWeaponCheckView`·`LobbyTrainingCombatView`)은 158 이 이미 끈다. 157 은 이 뷰에 의존하지 않는다.
- **과도기 어댑터 — 158 병합 시 삭제**: `Assets/1.Scripts/Lobby/LobbyTutorialGuideAdapter.cs` 파일 하나(커밋 ③, 씬·다른 코드 참조 없음 — 첫 씬 로드 때 숨은 DDOL 오브젝트로 스스로 생김). `LobbyTutorialState` 를 읽어 기존 상단 가이드 UI 에만 띄운다: 목표 = `TutorialGuideUI.SetObjective`(지시문 키·T10 체크·진행 n/N, T21 카운트다운 = '시작까지 {n}' 체크 줄 1개 — 커밋 ⑥), 대사(L11·L21) = `ShowSubtitle` → 대사 단계 끝에 `ClearSubtitles`. 브리핑(L31~L34)은 옛 `LobbyBriefing` 이 띄우므로 건드리지 않는다. 158 병합 때 이 파일을 지우면 끝(158 패널과 상단 가이드가 겹치지 않게).
- 씬(`3.LobbyScene.unity`) 수정은 157·158 이 같은 날 겹치지 않게 브릿지로 순서를 맞춘다(157 은 커밋 ③에 라커 볼륨 1개만).

## 6. 캐릭터 교체와 158

- 교체 = 같은 clientId 의 PlayerObject 디스폰·재스폰(같은 위치). 진행 기록(명단·라커 방문·T10 마스크·집결 참여자)은 clientId 기준이라 그대로이고, 파사드 값도 바뀌지 않는다.
- 158 이 로컬 플레이어 Transform 을 캐시해 경로를 그린다면 교체 뒤 새 PlayerObject 로 다시 잡아야 한다(`NetworkManager.LocalClient.PlayerObject` 재조회).

## 7. 변경 이력

- 2026-10-30 커밋 ① — 최초. 파사드 골격·단계표 22행. 진행기 미연결(`IsAvailable == false`).
- 2026-10-30 커밋 ③ — L00 = 라커 볼륨 `Area_Locker`(서버 폴링, 스폰 완료 참여자 전원 누적 방문). L11·L21 대사 = 서버 스케줄러: 스냅샷 `TimerStartServerTime`/`TimerEndServerTime` 이 실제 값(길이 = 행 dialogueSeconds → 문구 표 durationSeconds → 글자 수 규칙, + 끝 여유 — 수치 `LobbyRules`), 끝 시각에 다음 행 1회. 캐릭터 잠금 제거. 과도기 어댑터 추가(§5).
- 2026-10-30 커밋 ⑥ — T20~T22. T20 = `Train02Start` 집결, 유지 3초 = 서버 카운트다운 → 그동안 표시 상태 T21(`DisplayStateId`·`ObjectiveKey`=`LOBBY_OBJ_COMBAT`·`ProgressMode` Countdown·`LOBBY_PROGRESS_COUNTDOWN` n = 남은 초 올림 3→2→1). 한 명이라도 이탈 = 구역이 카운트다운 취소 → 표시 T20 복귀, 다시 모이면 처음부터 3초. 판정은 순수 함수 `LobbyTutorialCountdownDisplay`(파사드가 씀, API 무변경). 카운트다운 동안 상대 없음, 확정 → T22 에서 스폰. T22 상대 = 로비 전용 `LobbyCombatSpawnSet.asset` **6마리**(Q9, `LobbyRules.combatSpawnSet` 로 연결 — 씬 무수정, 기준점은 기존 `Train02Spawns`). 완료 = 생성 상대 전원 사망 1회(마지막 사망 중복 수신 무시). **'남은 상대' 수는 계약에 없음**(옛 `LobbyTrainingCombat.Remaining/Total` 복제값은 158 이 끄는 옛 뷰 전용으로만 남김). 과도기 어댑터가 카운트다운 줄을 체크 줄 1개로 표시. 158 표시 계약 API 변화 없음.
- 2026-10-30 커밋 ⑥b — 문구 3행(`LOBBY_OBJ_VULN_CC`·`LOBBY_OBJ_WALL`·`LOBBY_BODY_SUPPRESS`) SO 기본값을 `{tag:ID}` 표기로(§4). 과도기 어댑터만 평문 치환. API 변화 없음.
- 2026-10-30 커밋 ⑦ — T30~T34. T31 = 실제 간파 성공 순간 완료(예고 창만 훈련 최소 2초로 연장), 성공 뒤 취약 T34 끝까지 유지. T32 = 단계 진입 뒤 새 시전(castId 기준선, Player 인스턴스별)의 넉백/기절 실제 적용 1회(간파 자동 넉백·이전 시전·면역 불인정, `LobbyCastWindow`). T33 = 상대만 PointC 복귀·체력 복구(취약·칸 보존). T34 = 지정 벽(`WALL_TRAIN_WEST`) 유효 넉백 충돌 = 완료 → 다음 revision 에서 `ActiveTargetWallId` 빈 값·`TargetWallCleared`, 빗나가면 위치 복구 후 재시도. 옛 벽 표식(`WallMarkerVisible`)은 T33·T34 에서만. 단계 안 무전(TRAIN_03_*) 끔 — 문구 키 보존. **은희 영역 1줄**: `Player.ServerCastSeq` getter(읽기 전용) 추가. 158 표시 계약 API 변화 없음.
- 2026-10-30 커밋 ⑤ — T10 판정 무변경(기존 `LobbyWeaponCheck` 가 명세 §5 T10 과 일치) — 테스트로만 고정: 순서 자유·완료자 연습 계속·전원 완료 전 다음 단계 없음·변신 Q/E 키별 한 항목·패시브/WASD 없음·캐릭터 교체 뒤 마스크 유지·체크 키 `LOBBY_CHECK_*` 6개가 `LobbyWeaponCheckItem` 값 순서. 158 표시 계약 변화 없음.
- 2026-10-30 커밋 ④ — L20/L21 마무리(§10.3 A). 입구 확정 → 명단 전원 텔레포트 1회 → L21 대사 → 대사 끝 T10. 명세 L20 '문 통과' = 텔레포트(문 막힘·종료 텔레포트 그대로). 명단 밖(늦은 참가자)은 옮기지 않음(로비 대기). 텔레포트 실패(플레이어 오브젝트 없음·거부)한 명단 사람은 다음 집결 행(T20)까지 0.25초마다 서버가 재시도. L21 동안 표적·T10 체크 미시작(Activity None). 158 표시 계약 변화 없음.
- 2026-10-30 커밋 ② — 진행기 연결(스냅샷 NetworkVariable → `Publish`). L20 집결 지점 `Train01Start` → **`Entrance`**(§10.3 A 확정 — ②는 기존 입구 텔레포트(`TeleportRosterInto`)를 그대로 태워 동작, 마무리(대사 중 표적 미스폰 등)는 커밋 ④). 행 칸 `trainingProfile` 추가. `Brief`→`Central` 개명은 미룸. 과도기 어댑터(상단 가이드)는 새 `LOBBY_*` 키가 없어 아직 없음.
