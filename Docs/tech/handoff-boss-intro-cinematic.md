# handoff — 보스 등장 시네마틱 (2026-10-23 · 경석(Claude))

> 계획·결정·구현 표 = [PLAN-boss-encounter-cutscene.md](../../PLAN-boss-encounter-cutscene.md) 맨 위 "★ 결정·정정" 절. **Play 미확인**(컴파일 0 · 전투 EditMode 291/0).
> 경석 영역(보스·보스방) 작업이지만 아래 **다른 영역 파일에 작은 추가**가 있다 — 동작을 바꾼 것은 HP바·튜토리얼 팁 두 곳뿐이다.

| 파일(담당 영역) | 추가 | 영향 |
|---|---|---|
| `Player/Player.cs` | `public Animator CharacterAnimator => animator;` | 읽기 전용 접근자. 등장 연출 밀림 리액션이 상태를 고른다 |
| `Camera/CameraTargetSwitcher.cs` | `EnterCinematic(cam, blend)` / `ExitCinematic(cam, blend)` / `IsInCinematic` · 우선순위 200 | 연출 vcam 을 플레이어·낙하 vcam(100) 위로 올렸다 내린다. 기존 전환(낙하·관전·임시 추적)은 그대로 |
| `Camera/Feedback/CameraFeedback.cs` | `PlayImpactShake(amplitude, duration)` | 세 번째 임펄스 소스(지연 생성). 설정창 흔들림 배율 적용 |
| `Tutorial/Guide/TutorialGuideUI.cs` | `SubtitleFont` · `TryResolveText(id, out text)` | 자막을 띄우지 않고 문구만 찾는다(독백·스킵 안내) |
| `9.ScriptableObject/Tutorial/Guide/TutorialGuideTexts.asset` | 행 `BOSS_INTRO_WellsCommand` · `BOSS_INTRO_SkipHint` | 문구 표(로컬라이징 Export 대상이면 같이 옮길 것) |
| `UI/Combat/BossHealthHUD.cs` · `DelayedHealthBar.cs` · 새 `BossHealthIntroFill.cs` | 디렉터 `HidesBossHud` 면 숨김 + 처음 보일 때 1/4 → 최대 0.8초 차오름(잔상 `ForceFill`) | 🔴 동작 변경 — 등장 연출 내내 보스 HP바가 안 보이고 **전투 시작 순간** 차오르며 뜬다(10-23 아트 요청). 연출을 끈 판에서도 처음 뜰 때 차오른다(`introFillEnabled` 로 끔) |
| `Map/Tutorial/Flow/TutorialZone4Flow.cs` | 연출 중이면 정면 팁 보류 | 🔴 동작 변경 — `TUT_B_BOSS_FRONT_TIP` 이 독백과 안 겹치게 전투 시작 뒤에 나온다 |
| `Map/BossTeleportManager.cs` | 보스방 도착 자리 = 디렉터 `TryGetIntroStartPose`(워크인 출발 대열) | 도착 위치가 `PlayerArrivalPoints` 삼각형 대신 화면 아래 대열로 바뀐다(`introEnabled` 끄면 예전 그대로) |

- 🔴 **연출 중 화면 HUD 를 전부 끈다**(경석 10-23) — `BossIntroPresentation` 이 Screen Space 루트 캔버스의 `Canvas.enabled` 를 끄고 연출이 끝나면 되돌린다. 새 화면 UI 를 만들 때 연출 중에도 보여야 하면 `BossIntroPresentation.KeepsDuringIntro` 에 넣을 것(지금: 게임 메뉴·접속 오버레이·로딩 화면).
- `CameraTargetSwitcher.TryGetGameplayViewRotation` 추가 — 연출 중엔 렌더 카메라 transform 이 연출 vcam 을 따르므로 "화면 방향"은 이 값으로 읽는다.
- 스킵 입력은 `Keyboard.current.spaceKey` 를 직접 본다(연출 잠금 중이라 대시 액션은 막혀 있다). 게임 메뉴가 열려 있으면 무시.
- 연출 끄기: 씬 `BossEncounterDirector` 인스펙터 `introEnabled` = false → 예전 흐름(도착 즉시 강하).
- ⚠️ 뒤집음(2026-10-26 · 경석): 스킵 **투표자 = 접속자 전원(관전자·영구 사망자 포함)** — 관전자도 관전 화면으로 연출을 보고 동의한다. 분모 = 새 복제 값 `SkipVoterCount`(예전 `EligibleCount`). 잠금·워크인·보스 인원 보정 대상(`_eligibleClientIds`)은 그대로. 화면 부품(레터박스·비네팅·스킵 위젯·HUD 끄기·vcam)은 `Map/Cinematic/CinematicOverlay.cs` 로 옮겼다(4구역 소개 연출과 공유, 동작 무변경) — [handoff-zone4-intro-cinematic.md](handoff-zone4-intro-cinematic.md).
