# 디버그 단축키 — 목록과 빌드 포함 규칙

> 할 일 41(2026-10-15). 디버그 단축키를 **릴리스 빌드**(Build/Windows64 Player = `BuildOptions.None`)에서도 쓰게 했다.
> 정식 릴리스 때 심볼 하나만 지우면 다시 빠진다.

## 1. 구조

- 스크립팅 심볼 **`DEV_HOTKEYS`** — `ProjectSettings/ProjectSettings.asset` 의 **Standalone** `scriptingDefineSymbols` 에만 있다
  (`UNITY_POST_PROCESSING_STACK_V2;DEV_HOTKEYS`). 다른 플랫폼에는 없다.
- 디버그 단축키·디버그 기능 코드는 `#if UNITY_EDITOR || DEVELOPMENT_BUILD || DEV_HOTKEYS` 로 감싼다.
  에디터·Development Build 는 심볼과 무관하게 항상 들어간다.
- 새 디버그 단축키를 만들면 같은 게이트를 쓰고 아래 표에 한 줄 추가한다.
  에디터 API(`UnityEditor`·`EditorApplication`)는 이 블록에 넣지 말고 `#if UNITY_EDITOR` 로 따로 감싼다 — 심볼 빌드가 깨진다.

## 2. 끄는 방법 (정식 릴리스)

**Project Settings > Player > Standalone > Other Settings > Scripting Define Symbols** 에서 `DEV_HOTKEYS` 를 삭제하고 Apply.
그러면 §3 표의 "게이트" 열이 `DEV_HOTKEYS` 인 단축키는 클래스(또는 블록)째 빌드에서 빠진다.
"없음" 인 단축키는 심볼과 무관하게 항상 동작한다 — 출시 전에 따로 정리해야 한다.

## 3. 단축키 표

"변경 전 릴리스" = 이번 변경 전 릴리스 빌드(Development Build 끔)에서 동작했는지.
게이트 = `DEV_HOTKEYS` 는 `UNITY_EDITOR || DEVELOPMENT_BUILD || DEV_HOTKEYS`.

| 키 | 기능 | 파일 | 게이트 | 변경 전 릴리스 | 겹침 |
|---|---|---|---|---|---|
| F1 | 피격 이펙트 종류 순환 | `Assets/1.Scripts/Dev/HitVFXDebugHUD.cs` (씬 배치형) | DEV_HOTKEYS | ✗ | ⚠️ F1 — DebugCharacterCycle |
| F1 | 캐릭터 순환 팔라딘 → 어쌔신 → 거너(호스트, 같은 자리 재스폰) — **DebugInterruptLab 씬 전용**. [dash-interrupt.md §10.1](dash-interrupt.md) | `Dev/InterruptLab/DebugCharacterCycle.cs` (씬 배치형) | DEV_HOTKEYS | — (신규, 할 일 115 C6) | ⚠️ F1 — HitVFXDebugHUD(그 HUD 는 이 씬에 없어 실제로는 안 겹침) |
| F2 | 타격점 계산 방식 순환 | `Dev/HitVFXDebugHUD.cs` (씬 배치형) | DEV_HOTKEYS | ✗ | |
| F3 | 23호 다음 행동 = 점프·점프·점프 콤보 예약(호스트) — 3번째 착지 간파 창 확인용 | `Dev/DevBossAttackHotkeys.cs` (자동 생성) | DEV_HOTKEYS | ✗ | |
| F4 | 살아 있는 보스(23호·웰즈) 체력 최대치의 10% 깎기(호스트) — 정상 피해 경로라 페이즈 전환이 실제처럼 난다 | `Dev/DevBossHpCut.cs` (자동 생성) | DEV_HOTKEYS | ✗ | |
| F5 | 보스방 진입 패드로 파티 워프 | `Dev/DevBossEntranceWarp.cs` (자동 생성) | DEV_HOTKEYS | ✗ | |
| F6 | 보스 전투 중(Phase = Combat) 누른 사람만 보스방 지정 지점으로 이동(호스트·클라, 오너 권한) — 지점 = `bossroom.prefab` 의 빈 오브젝트 `[Dev] BossWarpPoint`(옮기면 위치·y축 회전이 바뀜). 연출·낙하 복구 중엔 안 함 | `Dev/DevBossPointWarp.cs` (자동 생성) | DEV_HOTKEYS | — (신규) | |
| F7 | RetroCRT(Cyanilux CRT) 토글 | `Rendering/RetroCRT/RetroCRTController.cs` (맵 씬) | DEV_HOTKEYS | ✗ | ⚠️ F7 — RenderCostAB |
| F7 | 실루엣 ON/OFF 수동 토글(측정기) | `Dev/LookAB/RenderCostAB.cs` (씬 배치형) | DEV_HOTKEYS | ✗ | ⚠️ F7 — RetroCRTController |
| F8 | 프로파일러 HUD 토글 | `Dev/Profiler/ProfilerHUD.cs` (씬 배치형) | DEV_HOTKEYS | ✗ | ⚠️ F8 — ZonePerfRecorder |
| F8 | 존별 성능 한 줄 오버레이 토글 | `Dev/Profiler/ZonePerfRecorder.cs` (씬 배치형) | DEV_HOTKEYS (본문) | ✗ | ⚠️ F8 — ProfilerHUD |
| Home | 존별 성능 CSV 저장 + 콘솔 요약 | `Dev/Profiler/ZonePerfRecorder.cs` | DEV_HOTKEYS (본문) | ✗ | |
| End | 프레임 상한 해제 ↔ 원래 상한(누적 초기화) | `Dev/Profiler/ZonePerfRecorder.cs` | DEV_HOTKEYS (본문) | ✗ | |
| Insert | 존별 성능 누적 초기화 | `Dev/Profiler/ZonePerfRecorder.cs` | DEV_HOTKEYS (본문) | ✗ | |
| F9 / Shift+F9 | 가장 가까운 몬스터 / 전부 즉사 (호스트) | `Utility/DevMonsterKill.cs` (자동 생성) | DEV_HOTKEYS | ✗ |  |
| F10 | 디버그 부활(로컬 오너, 소울 상태일 때) | `Player/Life/PlayerReviveController.cs` (Player 프리팹) | 없음 | ✓ | |
| F12 | 조각 폭발(FragmentExploder 가 붙은 오브젝트 전부) | `fragments/FragmentExploder.cs` | 없음 | ✓ | |
| Alt+1~9, Alt+0 | 튜토리얼 구간 스킵 / Alt+0 판 재시작 (호스트, 튜토리얼 중) | `Map/MapScene/MapSceneSkipController.cs` (+ `MapSceneDirector.Skip.cs`·`MapSceneDebugActions.cs`·`Flow/MapSceneTraitCorridorFlow.cs`) | DEV_HOTKEYS | ✗ | Alt+1~3 — 로비 예외 상황(씬이 달라 실제로는 안 겹침: 이쪽은 맵 씬 튜토리얼 중만) |
| Alt+1 | **로비(3.LobbyScene)** 튜토리얼 클리어 플래그 켜기/끄기 — 누른 사람만(서버 확정·복제). 켜면 훈련 명단·훈련 내부 집결에서 빠지고 출격 집결엔 남는다, 끄면 다음 회차부터 다시 훈련. 규칙 [lobby-training.md](lobby-training.md) "튜토리얼 클리어 플래그" | `Lobby/LobbyRecoveryHotkeys.cs` (자동 생성 — 진행기에 붙음) + `Lobby/LobbyTrainingDirector.Recovery.cs`(RPC) · 표 `Lobby/LobbyRecoveryHotkeyRules.cs` | DEV_HOTKEYS | — (신규, 할 일 112) | Alt+1 — 튜토리얼 스킵(맵 씬만) |
| Alt+2 | **로비** 멈춤·갇힘 탈출 — 누른 사람만. 서버가 낙하 복구·남은 연출 잠금·진행 중 스킬/공격·디버프를 기존 API 로 풀고 안전 지점(훈련 명단 + 훈련 단계면 그 단계 집결 구역 가운데, 아니면 자기 스폰 지점)으로 옮긴다. 브리핑 중엔 풀지 않고 토스트 | 위와 같음 | DEV_HOTKEYS | — (신규, 할 일 112) | Alt+2 — 튜토리얼 스킵(맵 씬만) |
| Alt+3 | **로비** 다시 불러오기 — 호스트만(클라는 '호스트만' 토스트, 서버도 거부). 판 끝 로비 복귀와 같은 경로(`NetworkLoadingFlowController.TryLoadSessionSceneSingle`). 출격·씬 전환 중엔 무시 | 위와 같음 | DEV_HOTKEYS | — (신규, 할 일 112) | Alt+3 — 튜토리얼 스킵(맵 씬만) |
| ~~M~~ | 맵 오버뷰(디버그 맵) 토글 — **꺼짐**(경석 10-22, 쓰지 않음). 인스펙터 `hotkeyEnabled` 로 다시 켠다 | `Map/MapOverviewUI.cs` | 없음 | — | |
| `[` `]` | 미니맵 크기 프리셋 이전/다음 (슬롯 미부착일 때) | `Map/Minimap/MinimapController.cs` | 없음 | ✓ | ⚠️ `[ ]` — CameraTargetSwitcher |
| `[` `]` | 관전 대상 이전/다음 (관전 모드일 때) | `Camera/CameraTargetSwitcher.cs` | 없음 | ✓ | ⚠️ `[ ]` — MinimapController |

파일 경로는 `Assets/1.Scripts/` 기준.

- 23호 예약(F3)은 `TwentyThreeBoss.cs` 의 `DevReserveJumpCombo`·`ConsumeDevReservation` 블록(같은 게이트)을 쓴다. 단타 예약 5종(F3 훅·Shift+F3·F4·F6·F9·F11)은 2026-10-20 경석 요청으로 걷어 냈다 — F11 은 비어 있다(F4 는 10-22 보스 체력 10% 깎기, F6 은 10-26 보스방 지정 위치 이동(할 일 102)으로 씀).
- **겹침은 표시만 했다** — 코드는 고치지 않았다.
  F8·F7 은 둘 다 씬에 붙어 있을 때만 겹친다. `[ ]` 는 관전 모드이고 미니맵이 슬롯 밖일 때만 겹친다.
- 일반 조작 키(ESC·이동·스킬·F 상호작용·Alt+Enter 창 모드 등)는 디버그 단축키가 아니라 제외했다.
- `LookToggle` 은 게이트 대상이지만 단축키가 없다(F9 룩 토글은 09-29 에 뺐다 — 시작 룩 적용만 한다).

## 4. 게이트를 바꾼 파일 (할 일 41)

`Dev/DevBossAttackHotkeys.cs` · `Dev/DevBossEntranceWarp.cs` · `Dev/HitVFXDebugHUD.cs` · `Dev/LookAB/LookToggle.cs` ·
`Dev/LookAB/RenderCostAB.cs` · `Dev/Profiler/ProfilerHUD.cs` · `Dev/Profiler/ZonePerfRecorder.cs` · `Utility/DevMonsterKill.cs` ·
`Map/MapScene/MapSceneSkipController.cs` · `Map/MapScene/MapSceneDebugActions.cs` · `Map/MapScene/MapSceneDirector.Skip.cs` ·
`Map/MapScene/Flow/MapSceneTraitCorridorFlow.cs` · `Monster/Boss/TwentyThreeBoss.cs` · `Rendering/RetroCRT/RetroCRTController.cs`

손대지 않은 것: `[Conditional("UNITY_EDITOR")]` 로그 헬퍼(`Edit.*`·`DashLog`·`PlayerMotor`·`Player`·`PlayerInputReader`) — 로그만 사라지고
단축키 동작에는 영향이 없다. 에디터 전용 부트 도구(`DevBootTarget`·`DevSceneBooter` 의 `#if UNITY_EDITOR`)도 그대로다.

⚠️ `MapSceneDirector.Skip.cs` 는 ServerRpc 를 담고 있다 — 호스트와 클라가 **같은 심볼로 뽑은 빌드**여야 RPC 테이블이 맞는다.
`Lobby/LobbyTrainingDirector.Recovery.cs`(로비 Alt+1~3, 할 일 112)도 같다. 클리어 플래그 복제 목록(NetworkList)은 일부러 게이트 밖 본체에 뒀다 —
NetworkVariable 구성이 빌드마다 달라지면 진행기 오브젝트 동기화 자체가 깨진다.

### 로비 예외 상황 키 추가법 (할 일 112)

1. `LobbyRecoveryHotkeyRules.cs` — `LobbyRecoveryAction` 에 값 하나, `Table` 에 한 줄(숫자·동작·호스트 전용·문구). 결과 문구가 새로 필요하면 `LobbyRecoveryResult`·`Describe`.
2. `LobbyTrainingDirector.Recovery.cs` 의 `RunRecoveryServer` switch 에 한 갈래(서버 실행).
3. 이 표에 한 줄. 키 표 테스트(`LobbyRecoveryHotkeyRulesTests`)가 숫자·동작 중복을 막는다.
