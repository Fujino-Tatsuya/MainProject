# 로비 튜토리얼 — 흐름·서버 상태 (할 일 157)

> 로비(`3.LobbyScene`)에서 하는 튜토리얼 전체: 라커 방문 → 중앙 집결 → 훈련장(기본 조작·전투·간파·제압·자유 연습) → 임무 브리핑 → 출격.
> 명세: 로비 튜토리얼 플로우 v1.0(2026-10-27, `_lane-artifacts/common/157-lobby-tutorial/로비_튜토리얼_플로우.md`). 계획·결정: 레인 산출물 `157-PLAN.md` §10.
> **표시(목표/대사 패널·바닥 경로·집결 테두리·벽 표식)는 158** — 읽기 계약 = [handoff-lobby-tutorial-contract.md](handoff-lobby-tutorial-contract.md). 이 문서는 진행·판정(서버)만 적는다.
> 부스 운영·접속은 [lobby-demo-flow.md](lobby-demo-flow.md). 기준 2026-10-30 · 커밋 ⑩(`3cc5f105`). 문서와 코드가 다르면 코드가 맞다.

## 0. 요약

- **단계표 SO 가 진행 원본이다.** `Assets/9.ScriptableObject/Lobby/LobbyTutorialSteps.asset`(`LobbyTutorialStepTable`) 22행, 행 순서 = 진행 순서. 연결 = `LobbyRules.asset` 의 `stepTable`.
  문구 키·집결 지점·유지 시간·경로/벽/대사/배경 ID 가 행에 있다 — 코드 switch 에 수치·문구를 두지 않는다.
- **판정은 서버, 표시는 전 피어.** 서버 진행기 `LobbyTrainingDirector`(씬 상주 NetworkObject `LobbyTraining/LobbyTrainingDirector`)가 단계를 정하고
  스냅샷 1개(`LobbyTutorialSnapshot` NetworkVariable)를 복제한다. 모든 피어는 같은 SO 에서 `stepIndex` 행을 읽고 파사드 `LobbyTutorialState` 에 Publish 한다.
- **앞으로만.** 역행 없음, 한 프레임 한 전이, 지난 단계·회차의 보고는 revision·runId 로 버린다.
- **캐릭터 잠금 없음**(Q1). 어느 단계든 라커에서 교체 가능 — 기록은 clientId 기준이라 교체로 깨지지 않는다(§7). 남은 교체 거부 = 출격 집결 카운트다운 중·출격 확정 뒤.
- **매 회차 L00 부터**(Q3). 판 끝 로비 복귀 = 새 진행기 = runId +1. 훈련 완료 자동 클리어 플래그는 없앴다.
- 훈련장 문은 막혀 있고 출입은 텔레포트(Q4 B — L20 입구 확정 → 안, T50 출구 확정 → 로비 중앙).

## 1. 단계표 (22행)

명세 상태 23개 중 `T21_CombatCountdown` 은 행이 아니라 **T20 의 표시 상태**(활성 구역 카운트다운 중 — `DisplayStateId`).
유지 = 집결 행 `holdSeconds`(기본 0.3초, 표시 안 함 / T20 만 3초 = 카운트다운). 집결 지점 이름은 `LobbyGatherPoint`(중앙 = `Brief` — `Central` 개명은 미룸).

| # | StateId | Kind / Activity | 완료 조건(서버) | 지점·키 |
|---|---|---|---|---|
| 0 | L00_LockerVisit | LockerVisit | 스폰 완료 참여자 전원이 라커룸 `Area_Locker` 에 한 번씩(누적) | 경로 `PATH_LOCKER` |
| 1 | L10_CentralGather | Gather | 중앙 전원 집결 0.3초 → **훈련 명단 고정** | `Brief`(`Gather_Brief`) |
| 2 | L11_CentralIntro | Dialogue | 대사 끝 시각 | `LOBBY_DLG_INTRO` |
| 3 | L20_TrainingGather | Gather | 입구 전원 집결 → 명단 전원 훈련장 안으로 텔레포트 → 바로 L21 | `Entrance`(`Gather_Entrance`) |
| 4 | L21_TrainingIntro | Dialogue | 대사 끝 시각(표적·체크 미시작) | `LOBBY_DLG_TRAINING` |
| 5 | T10_Basic | Activity BasicCheck | 명단 전원 6항목 완료 | 진행 = PartyComplete |
| 6 | T20_CombatGather | Gather | 전원 집결 3초(카운트다운, 이탈 = 취소) | `Train02Start` |
| 7 | T22_Combat | Activity Combat | 생성 상대 6마리 전원 사망 | 남은 수 표시 없음 |
| 8 | T30_CounterGather | Gather | 전원 집결 0.3초 | `Train03Area` |
| 9 | T31_Counter | Activity Counter | 간파 성공 순간 | |
| 10 | T32_VulnerableCC | Activity VulnerableCc | 새 시전 넉백/기절 실제 적용 1회 | 보조 2줄 |
| 11 | T33_ResetPosition | Activity ResetPosition | 상대 위치 복귀 실행(자동) | |
| 12 | T34_Wall | Activity Wall | 지정 벽 유효 넉백 충돌 | 벽 `WALL_TRAIN_WEST` |
| 13 | T40_SuppressKill | Activity SuppressKill | 제압 중 처치 | 보조 1줄 |
| 14 | T50_FreePractice | Gather + FreePractice | 출구 전원 집결 0.3초 → 종료 텔레포트(중앙) | `TrainEnd` |
| 15 | L30_BriefGather | Gather | 중앙 전원 집결 0.3초 | `Brief` |
| 16~19 | L31~L34 | Dialogue ×4 | 행마다 대사 끝 시각 | `LOBBY_BRIEF_EVAL`·`_FACTORY`·`_REBELLION`·`_SORTIE`, 배경 L32·L33 |
| 20 | L40_SortieGather | Gather | **접속자 전원** 집결 0.3초 → 출격 확정 1회 | `Sortie` |
| 21 | L41_Loading | Loading | 기존 로딩 흐름 | |

- 행 칸 `trainingProfile` = Training 인 행(L21·T10~T50) 동안 명단 체력 하한 1(§7). 칸 전체 목록은 계약 문서 §1·§2.
- 표 검증 = EditMode `LobbyTutorialStepTableTests`(22행·StateId 유일·Kind 별 필수 칸).
- `enforceRequiredSkillTags`(표 전체, 기본 **꺼짐**) — 스킬 `tooltip.tags` 가 지금 전부 비어 있어 T32·T34 태그 검사는 끔(Q12a). 태그 데이터가 채워지면 켠다.

## 2. 서버 구조

| 파일(`Assets/1.Scripts/Lobby/`) | 역할 |
|---|---|
| `LobbyTutorialStepTable.cs` | enum `LobbyTutorialStepKind`·`LobbyTutorialActivity`·`LobbyTutorialProgressMode`, 행 `LobbyTutorialStep`, SO + `Validate` |
| `LobbyTutorialFlow.cs` | 순수 모델 — 행 번호 전이·revision·runId·참여자/명단 고정·라커 Set·한 프레임 한 전이·사건 신선도·출격 래치(`TryCommitLaunch`) |
| `LobbyTutorialRules.cs` | 순수 규칙 — 캐릭터 교체 거부(`CharacterChangeBlock`)·대사 길이(`DialogueSeconds`)·`LobbyTutorialDialogueClock`·텔레포트 재시도 범위 |
| `LobbyTutorialSnapshot.cs` | 복제 구조체 { runId, revision, stepIndex, enteredAt, timerStart, timerEnd, launchCommitted } |
| `LobbyTutorialState.cs` | 158 읽기 파사드(정적) — 계약 문서 §2·§3 |
| `LobbyTrainingDirector.cs` (+`.Recovery.cs`) | NGO 진행기 — 스냅샷·명단·클리어 플래그 복제, 집결 구역 켜고 끄기, 대사 스케줄러, 텔레포트, 출격, 연결/재접속 |
| 내용 컴포넌트(같은 오브젝트) | `LobbyTrainingWeaponCheck`(T10)·`LobbyTrainingCombat`(T22)·`LobbyTrainingSparring`(T31~T40·T50 허수아비)·`LobbyBriefing`(L31~L34 잠금·카메라) |
| `PartyGatherZone` / `PartyGatherRules` | 집결 구역(서버 폴링·쓰러짐 제외·확정 래치·카운트다운 서버 시각 복제) |
| `LobbyAreaVolume` | 비네트워크 박스 볼륨 — 라커룸 `Area_Locker`(L00 + 캐릭터 선택 영역, 161) |

### 2.1 진행·보고 규칙

- **Activity + revision**: 내용 컴포넌트는 단계 시작 때 그 revision 을 들고 있다가 완료 때 `ServerReportActivityCompleted(Activity, revision)`. 지금 행·revision 이 아니면 경고 후 무시.
  판정 콜백 안이 아니라 **다음 프레임**에 보고한다(피해·사망 처리 도중 단계를 넘기지 않게).
- **한 프레임 한 전이**: 같은 프레임 두 번째 전이는 다음 프레임으로 미룬다. 각 Activity 는 진입 프레임 **이후** 사건만 인정 — T31 성공이 같은 프레임에 T32 를 끝내지 않는다.
- **집결 행**은 구역 확정(`PartyGatherZone.ServerConfirmed`)으로만, **대사 행**은 스케줄러로만 넘어간다.
- 참여자: 명단 고정 전(L00·L10) = 스폰 완료(PlayerObject 있음)한 접속자 중 클리어 플래그 끈 사람, 고정 뒤 = 명단, L40 = 접속자 전원. N=0 이면 전이 금지.

### 2.2 대사 스케줄러 (L11·L21·L31~L34)

- 진입 때 서버가 길이를 정해 스냅샷 `timerStart`/`timerEnd`(서버 시각)에 싣고, 끝 시각에 다음 행으로 1회. 클라 보고를 기다리지 않는다.
- 길이 = 행 `dialogueSeconds` > 0 → 문구 표(`TutorialGuideTexts`) `durationSeconds` → 글자 수 규칙(2초 + 공백 제외 글자당 0.08초, 최소 3초) + 끝 여유 0.5초. 수치 = `LobbyRules`(`dialogue*`). 음성 없음.
- L11·L21 은 이동 허용. L31~L34 는 묶음 입력 잠금(§5). 연속 대사(L31→L34)는 행이 바뀌어도 목표 패널이 다시 켜지지 않는다(계약 §3).
- 옛 무전(`LobbyRadio`)의 단계 진입 대사·사건 무전(TRAIN_03_*)·BRIEF_01~04 는 **모두 재생하지 않는다**(Q6 — 감독관 대사와 중복 금지). 키·문구 행은 남겨 둠. 아래 "로비 무전" 절.

## 3. 로비 구간 — L00 ~ L21

### 라커 방문 (L00)과 캐릭터 선택 영역 (할 일 161)

- 라커룸 내부 박스 `Area_Locker`(`LobbyAreaVolume`)를 서버가 매 프레임 폴링 — 본체 기준점·높이 범위·쓰러짐 규칙은 집결 구역과 같다. 다른 층은 불인정.
- 누적 방문 Set(clientId) — 시작 때 이미 안에 있으면 인정, 재진입은 1회. 스폰 완료 참여자 전원 방문 → L10. 볼륨 칸이 비면 오류 로그 1회 후 자동 통과.
- **캐릭터 선택도 같은 영역**이다(161): 라커룸 박스 안이면 F 프롬프트 → 선택 패널(표식 `CharacterSelectStation` 은 라커룸 가운데). EditMode `LobbyCharacterSelectAreaTests`·`LobbyLockerVisitTests`.

### 중앙 집결 (L10) → 소개 대사 (L11)

- 중앙 `Gather_Brief` 0.3초 전원 집결 = 확정 순간 **훈련 명단 고정**. 이후 접속 = 늦은 참가자(로비 대기, 출격 인원엔 포함). 캐릭터는 잠그지 않는다.

### 훈련장 집결 (L20) → 훈련 소개 대사 (L21)

- 입구 `Gather_Entrance` 집결 확정 → 명단 전원을 `Gather_Train01Start` 박스 안으로 텔레포트 → 재집결 없이 L21. 명세 L20 '문 통과' = 이 텔레포트.
- 명단 밖(늦은 참가자)은 옮기지 않는다. 실패한 명단 사람(PlayerObject 없음·거부)은 다음 집결 행(T20)까지 0.25초마다 재시도.
- L21 동안 표적·T10 체크는 시작하지 않는다(Activity None).

### 텔레포트 공통 (할 일 111 유지)

- 실행 = `LobbyTrainingDirector.TeleportRosterInto(대상 구역, 확정 지점)` — 입구(L20) → `Train01Start`, 출구(T50) → `Brief`(중앙).
- 자리 = `LobbyTrainingEndPlacement`(박스 로컬 XZ, 가장자리 여백 1m, 간격 1.5m 격자, 줄·묶음 가운데 정렬, 자리보다 많으면 순환). 높이 = 구역 원점 바닥 + 0.5m. EditMode `LobbyTrainingEndPlacementTests`.
- 이동 = `PlayerFallRecovery.RelocateServer`(서버 → 오너 RPC → `TeleportAuthoritative`). 바라보는 방향은 바꾸지 않는다. 구역 칸이 비면 경고 1회 후 생략.
- 원격 클라는 위치 복제가 돌아온 뒤에 안으로 세므로 왕복 지연만큼 늦게 찬다(L30 중앙 집결).

### 훈련장 문 — 막힘

- `Door_Training` 하나만 막혀 있다 — `PF_Lobby_Props_v003.prefab` 안 인스턴스 오버라이드 3개(`LobbyProximityDoor` 컴포넌트 제거·문짝 MeshCollider `m_Enabled: 0`·추가 BoxCollider(트리거 아님, 레이어 `Wall`)).
  나머지 문 3개(`Door_Administrator`·`Door_Locker`·`Door_Operations`)는 근접 자동 문 그대로. 런타임에 문을 찾아 끄는 코드는 없다.

## 4. 훈련장 — T10 ~ T50

### T10 기본 조작 — 6항목

개인 bitmask(`LobbyWeaponCheckEntry`, clientId 행) 복제, 표적 3개(`LobbyTrainingTargetSpots`). 순서 자유, 완료자는 연습 계속, **명단 전원 완료** 전엔 다음 단계 없음.
행동한 본인만·항목별 최초 1회. 거부된 요청·조준 취소는 체크되지 않는다. 패시브·WASD 이동은 항목이 아니다. 체크 키 `LOBBY_CHECK_*` 6개 = `LobbyWeaponCheckItem` 값 순서.
**체크 시점은 승인 시점**(사용자 확정 2026-10-22) — 명세의 '실제 효과 시점'으로 옮기지 않는다.

| 항목 | 팔라딘(가붕이) | 거너 | 암살자(일반 / 변신) | 훅 · 시점 |
|---|---|---|---|---|
| 기본 공격 | 콤보 한 타 시작 | 한 발 발사 | 한 타 시작(강타 포함) | `Player.ServerBasicAttackStarted` — 적중 무관 |
| Q | `FirstMeleeMainSkill` 승인 | `GunnerChargeLaserSkill` **발사** | `AssassinDashStrikeSkill` 승인 | 거너 = `GunnerChargeLaserSkill.ServerFired`, 나머지 = `PlayerSkillController.ServerSkillUsed` |
| E | `FirstMeleeSubSkill` 승인(방어 창 전) | `GunnerCoolBackstepSkill` 승인 | `AssassinEnhanceSkill` / 변신 `AssassinCircleStrikeSkill` 승인 | `ServerSkillUsed` |
| R | `FirstMeleeUltimateSkill` 승인 | `GunnerTrackingLaserSkill` 대상 확정 승인 | `AssassinTransformSkill` 승인 | `ServerSkillUsed` |
| 우클릭 | `FirstMeleeInterruptSkill` 승인 | `GunnerInterruptSkill` 승인 | `AssassinInterruptSkill` 승인 | `ServerSkillUsed` |
| 대시 | 대시 승인 | 대시 승인 | 대시 승인 | `PlayerDashController.ServerDashApproved` |

- 변신 중 Q/E 는 키별 한 항목(같은 슬롯). 발사 신호로 세는 스킬은 `GunnerChargeLaserSkill` 하나뿐(`TryGetApprovalItem(countsOnFire)`). EditMode `LobbyWeaponCheckTests`.

### T20 전투 집결(카운트다운) → T22 전투

- T20 = `Gather_Train02Start` 유지 3초 = 서버 카운트다운(`countdownEndServerTime` 복제). 그동안 표시 상태 T21(목표 `LOBBY_OBJ_COMBAT`·진행 '시작까지 {n}'). 한 명이라도 나가면 취소 → T20, 다시 모이면 처음부터.
- 카운트다운 동안 상대 없음. 확정 → T22 에서 로비 전용 `LobbyCombatSpawnSet.asset` **6마리** 스폰(`LobbyRules.combatSpawnSet`, 기준점 씬 `Train02Spawns` — 비면 옛 존2 9마리) + 훈련 리셋 `CombatStart`.
- 완료 = 생성 집합 전원 사망 1회(`MonsterDeathEvents.ServerMonsterDied` → `LobbyCombatLedger`, 마지막 사망 중복 무시). 끼임 복구 = 0.5초마다 훈련실 경계 밖·오래 멈춤 → 초기 위치 Warp.
- '남은 상대' 수는 계약에 없다(옛 `LobbyTrainingCombat` 남은 수 복제는 158 이 끄는 옛 뷰 전용).

### T30 ~ T40 간파 훈련 — 훈련 상대 `TrainingSparringBot`

- **프리팹** `Assets/2.Prefabs/Lobby/TrainingSparringBot.prefab` = `SpinnerBot.prefab` Variant(`SpinnerBot` 컴포넌트 → `TrainingSparringBot`). 규칙 값 = 23호 데이터 `No23_A.asset`(`counterRules` — 그로기·취약·넉백·칸 감소 등).
  23호 코드(`TwentyThreeBoss`)를 읽어 같은 규칙을 이 상대 안에 다시 썼다 — 23호·공용 코드는 고치지 않는다(23호 규칙이 바뀌면 같이 맞춘다). 이동 없음, 사거리(`SpinnerBotData.attackRange`) 안이면 간파 창 → 공격.
- **상태** = `TrainingSparringModel`(순수, 진행기 Activity 를 따라감) · 연결 = `LobbyTrainingSparring` + `LobbySparringLink` · "새 시전" 판정 = `LobbyCastWindow`.
- **스폰** = T30 집결 확정(T31 시작) 순간 `Train03Arena` A/B 위치에 — 집결 대기 중엔 없다. 간파 훈련이 아닌 단계로 넘어가거나 진행기 디스폰 때 정리.

| 단계 | 상대 동작 | 완료 |
|---|---|---|
| T31 간파 | 처치 불가, 간파 창(보라빛 예고, 훈련 최소 2초로 연장)을 단 같은 공격 반복. 실패 = 반복 | 실제 간파 성공 순간(그로기 끝을 기다리지 않음). 성공 뒤 **취약 무기한 유지**(T34 끝까지)·자발 행동 멈춤 |
| T32 취약 중 CC | 취약 유지, 자발 행동 없음. 넉백(간파 스킬·넉백 세기 > 0) = 밀림, 밀림 뒤 경직 = 기절, 뇌진탕 = 밀림이 끝나면 기절. **넉백 없는 경직만 = 반응·인정 없음** | 단계 진입 뒤 **새 시전**의 넉백/기절 **실제 적용** 1회(누구 것이든) |
| T33 위치 복귀 | 앞 CC 반응이 끝나면 상대만 `PointC` 로, 체력 복구(취약·칸 보존). 플레이어는 `PracticeReset`(쿨·대시) 회차당 1회 | 복귀 실행 = 자동 완료 |
| T34 벽 | 그대로 취약. 지정 벽 `DesignatedWall_West` = `WALL_TRAIN_WEST` | 지정 벽 유효 넉백 충돌 자체(새 시전). 칸 감소는 실제 규칙대로(조건 아님). 빗나간 넉백 = 위치 복구 후 재시도 |
| T40 제압·처치 | 칸 보존. 칸 0 이면 즉시 제압(보통 T34 벽이 건 제압을 이어받음), 남았으면 취약 보정 해제 → 간파 창·패턴 재개 → 간파/벽으로 소진 → 제압. **제압은 처치할 때까지 무기한**(상대가 그로기 타이머를 매 틱 연장). 제압 전 처치 방지(체력 하한 1) | 제압 중 사망(마지막 타격 귀속 없음) |

- **시전 기준선(`LobbyCastWindow`)**: T32·T34 진입 순간 접속자별 (Player 인스턴스, `Player.ServerCastSeq`) 저장 → 그보다 큰 시전 ID 만 새 시전. 캐릭터 교체로 생긴 새 인스턴스·진입 때 없던 사람 = 기준선 0.
  CC·벽 사건엔 castId 가 없어 **같은 서버 프레임의 `Player.ServerAttackLanded`(대상 = 이 상대)** 와 짝짓는다 — 짝 없는 CC(지연 효과 등)·면역(적용 안 됨)·다른 벽은 불인정.
  간파 자동 넉백은 따로 거르지 않는다 — 간파 성공은 T31 에서만 나고 그 시전 ID 는 T32 기준선 이하라 걸러진다. EditMode `LobbyCastWindowTests`·`TrainingSparringModelTests`·`TrainingSparringBotTests`.
- 제압 중 쿨타임 가속은 보류(Q13 A — 실전 규칙이 없어 문구만, 훈련이 임의 버프를 만들지 않는다).
- **칸 UI**: 머리 위 중간보스 바(`IOverheadHealthBarRule`) — 이름 자리 `칸 n/m`, 제압 중 '제압된 상대를 공격하세요'(남은 초 없음). 옛 '마무리 체력 60·마무리하세요' 단계는 없앴다.
- **씬 배치(임시 좌표 — 최종 위치는 사용자)** `LobbyTraining/Train03Arena`(`LobbyTrainingSparringArena`):
  - `SpawnAB` (-16, 0, 4) — `Gather_Train03Area`(중심 (-16, 5), 7×5) 안쪽 중심 1m(사거리 2m 라 구역 밖이면 공격·간파 창이 안 나온다 — 10-22 결정).
  - `PointC` (-17.5, 0, 5), 방향 +x — 지정 벽 면까지 2.2m, 간파 넉백 3m 로 닿는다.
  - `DesignatedWall_West` — Wall 레이어 BoxCollider(메시 없음), 중심 (-19.8, 1.1, 5) 크기 (0.2, 2.2, 5.5). 서쪽 아트 벽(`PF_Lobby_Architecture_v015` `SB_WallBay_004/005_body`) 바로 앞에 겹쳐 스윕이 먼저 맞힌다.
    아트 벽 콜라이더를 직접 넣지 않은 이유: SVN 아트가 바뀌면 참조가 조용히 끊긴다. 옮길 땐 아트 벽 면에 붙이고 NavMesh 걷는 면을 침범하지 않게.
  - `DesignatedWall_West_Marker` = 임시 주황 띠(`MA_Train03DesignatedWall.mat`, 콜라이더 없음) — T33·T34 동안만 켜진다(`LobbySparringLink.WallMarkerVisible`). 158 벽 표식이 대체할 임시 비주얼.

### T50 자유 연습 + 출구 집결

- 상대 사망 뒤 허수아비 1개(`LobbyRules.freePracticeDummyPrefab` = `TrainingDummy_Tutorial` — 사망 없음, 자리 = A/B 위치). 체력 하한은 행 `trainingProfile` = Training.
- 출구 `Gather_TrainEnd` 0.3초 전원 집결 확정 → 허수아비·훈련 보정 정리 → 종료 텔레포트(중앙 `Gather_Brief` 안) → L30. 이후 행엔 훈련 내용이 없어 재입장해도 재시작 없음.

## 5. 브리핑 — L30 ~ L34

- L30 = 중앙 0.3초 전원 집결(종료 텔레포트로 이미 안이면 구역이 켜지자마자 센다).
- L31 평가(`LOBBY_BRIEF_EVAL`) → L32 생산 시설(`_FACTORY`, 배경 `BRIEF_BG_FACTORY`) → L33 반란(`_REBELLION`, `BRIEF_BG_REBELLION`) → L34 출격 안내(`_SORTIE`, 배경 없음) — 대사 스케줄러가 행마다 1회, L34 끝 → L40.
- **`LobbyBriefing` = 묶음 입력 잠금 + 카메라만** (시간은 재지 않는다):
  - 잠금 = L31 진입 때 `LobbyRadio.ServerClear()` → **접속자 전원**(늦은 참가자 포함) `PlayerEncounterLock.BeginCinematicServer()`(보스 등장 연출 잠금 재사용 — FSM Cinematic·대시·입력·무적).
    상호작용 F 도 막힌다(`LobbyInteractor.IsBlocked` 가 `PlayerLifeInputPolicy.BlocksForCinematic` 을 본다). 브리핑 중 새로 스폰된 플레이어도 0.25초 주기로 잠근다.
  - **내가 건 것만 푼다**: 장부 `LobbyBriefingLockLedger`(+`LobbyBriefingLockSession`). 다른 계통이 이미 잠근 사람은 건드리지 않는다.
  - 해제 경로: L34 끝(→ L40)·단계 이탈(회차 초기화)·디스폰(판 끝·씬 언로드·호스트 셧다운) 모두 장부 해제. 끊긴 사람은 장부에서만 뺀다. EditMode `LobbyBriefingTests`.
  - 카메라 = `CameraTargetSwitcher.SetTemporaryFollow(cameraFocus)`(= `Gather_Brief`) 묶음 동안, 떠나면 `ClearTemporaryFollow`. `cameraFocus` 가 비면 카메라 무변경.
- 목표 패널은 대사 동안 숨는다(파사드). 옛 '작전 설명 중'(`LOBBY_OBJ_BRIEFING`)·`LOBBY_OBJ_SORTIE` 직접 표시는 없앴다(키 보존 — L40 목표 = `LOBBY_OBJ_SORTIE_GATHER`).
- 실전 초기화(명세 §10)는 하지 않는다 — 로비 플레이어는 메인 진입 때 디스폰되고 MapScene 이 새로 스폰한다(새 오브젝트 기본값 = 실전 초기값).

## 6. 출격 — L40 · L41

- L40 = `Gather_Sortie` 0.3초, 참여자 = **접속자 전원**(늦은 참가자 포함). 출격 카운트다운 표시는 없다(Q8 A).
- 확정 1회 = 출격 래치(`TryCommitLaunch`) → 스냅샷 `launchCommitted` true → 집결 구역 전부 끔, 이후 로딩 행 말고 전이·집결·대사·완료 보고 무시 → L41.
- L41 = 기존 흐름 그대로 — 같은 오브젝트에 `LobbyPodLaunch` 가 있으면 포드 연출(입력 잠금·페이드) 끝에, 없으면 바로 `NetworkSessionLauncher.StartGameLoading` 정확히 1회.
- 접속 승인: 출격 집결 카운트다운 중·출격 확정 뒤엔 거절(`LobbyCountdownActiveProvider`). EditMode `LobbySortieRunTests`.
- **시연 즉시 출격**: `LobbyRules.allowInstantSortie`(기본 **꺼짐**, Q2 B) — 켜면 명단 고정 전(L00·L10)에도 출격 구역이 켜지고 확정하면 훈련을 건너뛴다. DEV_HOTKEYS 와 별개.

## 7. 참여자·연결·회차

| 경우 | 처리 |
|---|---|
| 늦은 참가(명단 고정 뒤 접속) | 로비 대기 — 목표 패널 없음(`IsLocalWaitingLateJoiner`), 훈련 집결 인원 아님, 브리핑 잠금은 받음, L40 인원엔 포함 |
| 캐릭터 교체 | 같은 clientId 의 PlayerObject 디스폰·같은 자리 재스폰. 명단·라커 Set·T10 마스크·집결 참여자 모두 clientId 라 유지, 집결 인원 N 에서 빼지 않는다(같은 프레임 재스폰이라 T20 카운트다운도 취소 안 됨). 훈련 보정(체력 하한)은 교체 완료(`LobbyPlayerSpawner.ServerCharacterSwapped`) 때 다시 건다 |
| 연결 종료 | 모델·구역 명단에서 빼고 감소한 N 으로 재평가. N=0 이면 전이 금지 |
| 재접속(같은 playerId, 새 clientId) | 승인 때 묶인 playerId(`PlayerIdRegistry`)로 명단 소속·라커 방문·T10 마스크(`ServerClientRebound`)를 새 clientId 로 이어 감. 명단이 훈련장 안인 단계면 훈련장 안으로 텔레포트. 매핑 없음 = 늦은 참가자. 클리어 플래그는 잇지 않음 |
| 쓰러짐 | 집결 불인정. 훈련 행 체력 하한 1 |
| 새 회차(판 끝 로비 복귀·Alt+3 리로드) | 새 진행기 = runId +1, L00 부터 — 라커·T10 체크·명단 모두 새로. 지난 회차의 늦은 보고는 runId·revision 으로 걸러진다 |

## 튜토리얼 클리어 플래그 (할 일 112, 로비 Alt+1)

QA·부스 운영용 예외 키(키 표·게이트 = [debug-hotkeys.md](debug-hotkeys.md) §3). 진행기 복제 목록(`IsTrainingCleared`).

- **누른 사람만**, 서버 확정. 결과는 누른 사람에게만 토스트.
- **켜면** 참여자·훈련 명단·훈련 집결에서 빠진다(= 늦은 참가자와 같은 로비 대기). **출격 집결 인원엔 남고** 브리핑 잠금도 받는다.
  훈련 중에 켜면 즉시 빠져 진행 중 집결·체력 하한·T10 체크가 그 사람을 기다리지 않는다. **훈련할 사람이 아무도 없으면 출격 집결(L40)로 건너뛴다.**
- **끄면** 플래그만 지운다 — 이미 고정된 이번 명단엔 다시 넣지 않고 다음 회차부터 포함. 명단 고정 전에 끄면 이번 회차에 포함.
- 서버 세션 정적 장부로 회차를 넘겨 잇고, 연결 종료·호스트 셧다운 때 지운다. **훈련 완료 자동 부여는 없다**(Q3 — 옛 할 일 111 의 종료 집결 자동 부여 폐지).

## 로비 무전 (T6b)

`LobbyRadio`(NGO)·`LobbyRadioRules`(순수) 는 남아 있지만 **157 이후 재생하는 대사가 없다** — 단계 진입 대사 표(Q6)·간파 훈련 사건 무전 `TRAIN_03_COUNTER/WALL/COMBAT`·브리핑 `BRIEF_01~04` 모두 껐다.
남은 역할: 단계가 바뀔 때 미재생 줄 폐기 번호 맞추기, 브리핑 시작 때 `ServerClear()`. API `ServerPlay(id, oncePerRound)`·`ServerHasPlayed`·`ServerClear` 는 그대로(사건 무전이 다시 필요하면 이걸로).
옛 키·문구 행(`LOBBY_TRAIN_ROUTE`·`TRAIN_*`·`BRIEF_*`)은 지우지 않았다. EditMode `LobbyRadioTests`.

## 8. 문구 — 문자열 handoff

- 키 = `Tutorial.Guide.<id>` — `TutorialGuideTextSource` 가 Strings 시트 우선, 없으면 `TutorialGuideTexts.asset` SO 폴백([tutorial-guide-ui.md](tutorial-guide-ui.md#문구-출처--strings-시트-우선-so-폴백-할-일-86-t6a)).
  157 은 SO 에 ko 폴백 41행(`LOBBY_TITLE_*`·`LOBBY_OBJ_*`·`LOBBY_BODY_*`·`LOBBY_CHECK_*`·`LOBBY_PROGRESS_*`·`LOBBY_SPEAKER_SUPERVISOR`·`LOBBY_DLG_*`·`LOBBY_BRIEF_*`)을 넣었다.
- **xlsx Strings 행은 아직 없다** — 157·158 병합 뒤 메인이 최신 development 에서 `GameData.xlsx`(SVN)에 넣는다. 목록(44행 = `Tutorial.Guide.LOBBY_*` 41 + 158 전용 `UI.Lobby.Tutorial.Tag*` 3)은
  레인 산출물 `C:/UnityProject/_lane-artifacts/MainProject-Worktree/157/157-xlsx-strings.md`(레포에 복사하지 않음).
- `LOBBY_OBJ_VULN_CC`·`LOBBY_OBJ_WALL`·`LOBBY_BODY_SUPPRESS` 3행은 **`{tag:ID}` 표기**(`{tag:Knockback}`·`{tag:Stun}`·`{tag:Suppress}`) — xlsx 도 이 표기로 써야 158 이 색을 입힌다. 과도기 어댑터만 평문 `[넉백]` 등으로 바꿔 띄운다.

### 과도기 표시 어댑터 — 158 병합 때 삭제

`LobbyTutorialGuideAdapter.cs` 파일 하나(씬·코드 참조 없음, 숨은 DDOL 오브젝트로 스스로 생김) — 파사드를 읽어 기존 상단 `TutorialGuideUI` 에 목표(`SetObjective`)·대사(`ShowSubtitle`)만 띄운다. 158 패널과 겹치지 않게 병합 때 지운다(계약 §5).

## 9. 알려진 한계

- **날아가던 투사체 castId**: T32·T34 진입 전에 쏜 투사체가 진입 뒤 맞으면 시전 ID 가 기준선 이하라 불인정 — 의도된 동작이지만 '맞혔는데 안 됨'으로 보일 수 있다. 같은 프레임 짝이 없는 지연 CC 도 불인정.
- **로딩 타임아웃 정책은 기존 유지**: `NetworkLoadingFlowController` 는 NGO 씬 로드 타임아웃 뒤 시간초과 클라를 빼고 완료한 클라만으로 진행한다(그 사람은 판 도중 재합류로 들어옴). L41 은 이 흐름을 바꾸지 않았다.
- **브리핑 삽화 리소스 없음**: 157 은 `BackgroundId`(`BRIEF_BG_FACTORY`·`BRIEF_BG_REBELLION`)만 낸다. 이미지 납품 전까지 빈 배경(Q14 A).
- **T32 기절 인정 범위 — 결정: 실제 기절(뇌진탕 포함)만 인정**(사용자 결정 A). 넉백 없이 경직(`staggerDuration`)만 있는 적중은 기절이 아니다 — 상대가 반응하지 않고 T32 도 인정하지 않는다. 판정 `TrainingSparringRules.VulnerableHitCrowdControl`·`StunsOnPushEnd`.
- **xlsx 미반영**: Strings 시트 행이 들어가기 전엔 SO 폴백 ko 문구가 보인다(en 없음). `{tag:ID}` 3행은 158 이 없으면 표기 그대로/어댑터 평문.
- 경로 키 `PATH_*`·벽 키·삽화 키는 157 이 정한 임시 이름(158 과 맞추면 SO 만 고친다). 중앙 지점 enum 이름은 아직 `Brief`.
- `Gather_Brief`(중앙)가 스폰 줄 `SpawnPoint_*` 와 겹친다 — L10/L30 중 스폰된 늦은 참가자는 명단 밖이라 무관.
- 제압 중 쿨타임 가속 없음(Q13 A). 옛 안내 스텝(`LobbyGuideSteps`)은 158 경로가 대체할 때까지 과도기로 남아 있다(아래).

## 10. Play 확인 (사람 — 솔로 호스트 + MPPM 2인)

`0.BootStrapScene` Play → START → 로비. 로그 태그 `[LobbyTraining]`. 회귀 기준 EditMode = Lobby 테스트 24파일(`Assets/Tests/EditMode/Lobby/Editor/`).

- [ ] L00: 라커룸에 들어가면 방문(2인이면 둘 다 들어가야 L10). 라커 안에서 F → 캐릭터 교체 가능(이후 어느 단계든 가능)
- [ ] L10→L11: 중앙 집결 즉시(0.3초) 확정 → 감독관 소개 대사 → 끝나면 L20
- [ ] L20→L21: 입구 집결 → 훈련장 안으로 텔레포트(재집결 없음) → 훈련 대사 동안 표적 없음
- [ ] T10: 6항목(기본 공격·Q·E·R·우클릭·대시) 각자 체크, 둘 다 끝나야 다음
- [ ] T20→T22: 카운트다운 3→2→1, 한 명이 나가면 취소·재시작 → 상대 6마리 전멸
- [ ] T31: 간파 성공 순간 완료, 상대가 취약 상태로 멈춤
- [ ] T32: 새 넉백/기절 스킬 적중으로 완료(간파 자동 넉백·진입 전 투사체는 불인정)
- [ ] T33→T34: 상대만 C 위치 복귀(취약 유지) → 서쪽 지정 벽으로 넉백하면 완료, 빗나가면 위치 복구 후 재시도
- [ ] T40: 칸 소진 → 제압이 끝나지 않고 유지, 제압 중 처치로 완료(제압 전엔 안 죽음)
- [ ] T50: 허수아비 1개, 출구 집결 → 허수아비 사라지고 중앙으로 텔레포트
- [ ] L30~L34: 바로 집결 → 브리핑 대사 4줄, 그동안 전원(늦은 참가자 포함) 이동·공격·F 잠김·카메라 중앙, 끝나면 풀림
- [ ] L40→L41: 출격 구역 접속자 전원 0.3초(카운트다운 표시 없음) → 포드 연출 → 메인 1회
- [ ] 판 끝 로비 복귀 = L00 부터 다시. 훈련 중 접속 = 로비 대기·출격엔 포함. 훈련 중 끊고 같은 playerId 로 재접속 = 명단·체크 유지
- [ ] 단계마다 캐릭터 교체 뒤 진행 기록·집결 인원 유지

## 이력 (할 일 86 / 111)

옛 10단계 흐름(`LobbyTrainingStage/Phase` — Lobby → Train01~03A~D → TrainEnd → Brief → Sortie, 무전 단계 진입 대사, 훈련 선택·즉시 출격 동시 활성, 집결 유지 3초 공통,
훈련 완료 자동 클리어 플래그, 브리핑 BRIEF_01~04 한 번에 재생·'작전 설명 중', D 마무리 체력 60)은 157 커밋 ②~⑩에서 단계표 모델로 교체·폐지됐다.
남은 것: 텔레포트·문 막힘(111), 클리어 플래그(112 Alt+1), 브리핑 잠금 장부·카메라(86 T6c), T10 훅·체크 시점(86), 훈련 상대 프리팹·23호 규칙 복제(86 T5b), 아래 바닥 화살표·NavMesh. 상세는 git 이력(이 문서의 157 이전 판).

## 바닥 안내 화살표 (할 일 111)

다음 갈 곳을 바닥에 흐르는 화살표로 알려 준다. **씬 배치는 사용자가 직접**(프리팹만 제공). 158 이 단계별 `PathId` 경로로 대체할 예정 — 그 전까지 아래 프리팹이 쓰인다.

### 경로형 — `LobbyFloorArrowPath.prefab` (점선 + 삼각형, 꺾은선)

- **프리팹**: `Assets/2.Prefabs/Lobby/FloorArrow/LobbyFloorArrowPath.prefab` — 루트 `LobbyFloorArrowPath`, 자식 `Start`·`Waypoint1`·`End` = 경로 점, `Pieces` = 구운 조각(내장 Quad + 공유 머티리얼 `M_LobbyFloorArrowPath`).
  셰이더 `Lobby/FloorArrowPath`(URP Unlit 반투명), 텍스처 `50.Art/UI/Navigate/ArrowLineSet.png` **참조만**.
- **세팅**: 점 자식을 바닥 위로 옮긴다(루트 직속 자식 권장). 꺾이는 점이 더 필요하면 빈 자식을 `waypoints` 에 순서대로.
- **굽기**: 값·점을 바꾸면 자동으로 다시 굽는다(인스펙터 `다시 굽기` 버튼도). 기즈모 선이 빨간색이면 낡음. 씬 인스턴스에서 조각이 줄면 프리팹 조각은 꺼 둔다 — 깨끗이 하려면 프리팹 모드에서. `Pieces` 아래에 다른 오브젝트를 넣지 말 것.
- **런타임 비용**: Play·빌드에선 생성 없음 — 조각마다 `MaterialPropertyBlock` 만. 흐름 = 셰이더가 조각별 경로 거리 + `_Time`.
- **배치 규칙**(`LobbyFloorArrowPathLayout`, EditMode `LobbyFloorArrowPathLayoutTests`): 구간마다 등간격, 꺾임 여백 = max(`spacing`/2, (최장 조각 길이 + 최대 폭)/2), 짧은 구간·길이 0 구간은 건너뜀, 큰 삼각형 중심 = 도착점, 작은 삼각형 = `triangleEvery` 번째마다.
- **SVN 슬라이스 의존 없음**: `dashUv`·`triangleUv`(UV 0~1)를 컴포넌트가 들고 있다 — 텍스처 그림이 바뀌면 이 두 값만 고친다.

| 필드 | 기본 | 뜻 |
|---|---|---|
| `start` / `waypoints` / `end` | 프리팹 자식 | 경로 점 |
| `material` | `M_LobbyFloorArrowPath` | 공유 |
| `spacing` | 0.8 | 조각 중심 간격(m) |
| `dashLength` / `dashWidth` | 0.45 / 0.2 | 점선 조각(m) |
| `triangleEvery` | 4 | 몇 조각마다 작은 삼각형(0 = 없음) |
| `smallTriangleSize` / `endTriangleSize` | 0.35 / 0.9 | 삼각형 폭(m) |
| `heightOffset` | 0.02 | 바닥에서 띄우는 높이 |
| `color` | 노랑 α0.9 (HDR) | 색·투명도 |
| `flowSpeed` / `flowWavelength` / `flowMinAlpha` | 2 / 3 / 0.35 | 물결 속도(m/초)·주기(m)·골 불투명도 |
| `dashUv` / `triangleUv` | ArrowLineSet 영역 | 아틀라스 영역 |
| `piecesRoot` | `Pieces` | 손대지 말 것 |

혼자 놓으면 늘 켜져 있다. 스텝에 따라 켜고 끄려면 `LobbyGuideSteps.prefab` 자식으로 쓴다.

### 직선 띠 — `LobbyFloorArrow.prefab`

- `Assets/2.Prefabs/Lobby/FloorArrow/LobbyFloorArrow.prefab` — 루트 `LobbyFloorArrowStrip`, 자식 `Visual`(Quad + `M_LobbyFloorArrow`). 셰이더 `Lobby/FloorArrow` 가 `_Time` 으로 UV 를 민다. 늘 켜져 있다.
- 루트 위치 = 띠 시작점, +Z = 진행 방향. 필드 `length` 4 · `width` 1 · `arrowSpacing` 1 · `heightOffset` 0.02 · `color` 하늘색 · `flowSpeed` 1.5 · `endFade` 0.15. `Visual` 트랜스폼은 손으로 고치지 말 것(값이 덮인다).

### 안내 스텝 — `LobbyGuideSteps.prefab` (과도기)

- 자식 경로 3개 `Step1_CharacterSelect`/`Step2_Tutorial`/`Step3_Sortie` 중 지금 것 하나만 켠다. 전 피어 로컬·내 화면만, 복제된 진행기와 내 위치를 매 프레임 읽는다. 규칙 = `LobbyGuideStepRules`(EditMode `LobbyGuideStepRulesTests`).
- 157 ②부터 입력이 단계표 기준이다(위에서 처음 맞는 줄): 출격 확정·출격 구역 안 = 없음 → 명단으로 훈련 중 = 없음 → 명단 밖 + 캐릭터 래치 없음 + 교체 가능 = ① →
  명단으로 L20 입구 집결 중 = ②(구역 안이면 없음) → 명단 고정 전(L00·L10, 클리어 플래그 꺼짐) = 없음(라커·중앙 경로는 158 몫) → 나머지(클리어 플래그·늦은 참가자·출격 집결) = ③.
  ① 캐릭터 = 라커룸 `Area_Locker` 박스 진입 세션 래치. 158 이 단계별 `PathId` 경로로 대체하면 이 프리팹·규칙을 퇴역한다(시점은 158 과 합의).
- 편집 모드에선 세 경로가 다 보이고, 인스펙터 `editorPreview` 로 한 스텝만 볼 수 있다(씬 보기 숨김 — 저장 안 됨).

## NavMesh 사전 베이크

T22 전투 몬스터(NavMeshAgent 타입 `Humanoid`)가 걸을 NavMesh 는 **에디터에서 미리 구워 git 에 넣는다.** 로비엔 런타임 베이크 코드가 없다(MapScene 의 `MapNavMeshBaker` 와 별개).

- **오브젝트**: `3.LobbyScene` › `LobbyTraining/TrainingNavMesh` 의 `NavMeshSurface`.
- **범위**: Collect Objects = **Volume**(훈련실만) — 중심 (-16.15, 0.75, 0), 크기 (8.3, 2.5, 16.6). Agent Type = Humanoid(0), Use Geometry = Physics Colliders, Include Layers = **Ground + Wall**
  (Default 는 뺐다 — 집결 구역 트리거가 바닥처럼 구워지지 않게).
- **결과**(2026-10-22): 걷는 면 x -19.33~-12.67, z -7.33~7.33. 이 볼륨은 끼임 복구의 훈련실 경계로도 쓴다(`LobbyTrainingCombat.roomNavMesh`).
- **결과 에셋**: `Assets/0.Scenes/MainFlow/3.LobbyScene/NavMesh-TrainingNavMesh.asset`(+`.meta`) — 바이너리라 **git 에 커밋해야** 다른 PC·빌드에 들어간다. 없으면 몹이 서 있기만 한다.

### 다시 구워야 할 때

훈련실 바닥·벽·소품 배치가 바뀌거나 로비 전체가 회전·이동되면(SVN 아트 교체 포함). 볼륨은 월드 좌표 고정이라 그때 중심·크기도 같이 맞춘다.

1. Play 모드가 아닌 상태에서 `3.LobbyScene` 을 연다.
2. `LobbyTraining/TrainingNavMesh` 선택 → `NavMeshSurface` 의 **Bake**(같은 경로에 다시 만들어진다).
3. Scene 뷰에서 파란 NavMesh 가 훈련실 바닥을 덮는지, `Train02Spawns` Gizmo 상자가 그 위에 있는지 본다.
4. 씬 저장 → `NavMesh-TrainingNavMesh.asset` 과 `3.LobbyScene.unity` 를 함께 커밋.
