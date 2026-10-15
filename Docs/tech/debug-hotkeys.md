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
| F1 | 피격 이펙트 종류 순환 | `Assets/1.Scripts/Dev/HitVFXDebugHUD.cs` (씬 배치형) | DEV_HOTKEYS | ✗ | |
| F2 | 타격점 계산 방식 순환 | `Dev/HitVFXDebugHUD.cs` (씬 배치형) | DEV_HOTKEYS | ✗ | |
| F3 / Shift+F3 | 23호 다음 공격 예약 — 훅(좌↔우) / 예약 대기열 비우기 (호스트) | `Dev/DevBossAttackHotkeys.cs` (자동 생성) | DEV_HOTKEYS | ✗ | |
| F4 | 23호 어퍼컷 예약 (호스트) | `Dev/DevBossAttackHotkeys.cs` | DEV_HOTKEYS | ✗ | |
| F5 | 보스방 진입 패드로 파티 워프 | `Dev/DevBossEntranceWarp.cs` (자동 생성) | DEV_HOTKEYS | ✗ | |
| F6 | 23호 점프어택 예약 (호스트) | `Dev/DevBossAttackHotkeys.cs` | DEV_HOTKEYS | ✗ | |
| F7 | RetroCRT(Cyanilux CRT) 토글 | `Rendering/RetroCRT/RetroCRTController.cs` (맵 씬) | DEV_HOTKEYS | ✗ | ⚠️ F7 — RenderCostAB |
| F7 | 실루엣 ON/OFF 수동 토글(측정기) | `Dev/LookAB/RenderCostAB.cs` (씬 배치형) | DEV_HOTKEYS | ✗ | ⚠️ F7 — RetroCRTController |
| F8 | 프로파일러 HUD 토글 | `Dev/Profiler/ProfilerHUD.cs` (씬 배치형) | DEV_HOTKEYS | ✗ | ⚠️ F8 — ZonePerfRecorder |
| F8 | 존별 성능 한 줄 오버레이 토글 | `Dev/Profiler/ZonePerfRecorder.cs` (씬 배치형) | DEV_HOTKEYS (본문) | ✗ | ⚠️ F8 — ProfilerHUD |
| Home | 존별 성능 CSV 저장 + 콘솔 요약 | `Dev/Profiler/ZonePerfRecorder.cs` | DEV_HOTKEYS (본문) | ✗ | |
| End | 프레임 상한 해제 ↔ 원래 상한(누적 초기화) | `Dev/Profiler/ZonePerfRecorder.cs` | DEV_HOTKEYS (본문) | ✗ | |
| Insert | 존별 성능 누적 초기화 | `Dev/Profiler/ZonePerfRecorder.cs` | DEV_HOTKEYS (본문) | ✗ | |
| F9 | 23호 잡기 예약 (호스트) | `Dev/DevBossAttackHotkeys.cs` | DEV_HOTKEYS | ✗ | ⚠️ F9 — DevMonsterKill |
| F9 / Shift+F9 | 가장 가까운 몬스터 / 전부 즉사 (호스트) | `Utility/DevMonsterKill.cs` (자동 생성) | DEV_HOTKEYS | ✗ | ⚠️ F9 — DevBossAttackHotkeys |
| F10 | 디버그 부활(로컬 오너, 소울 상태일 때) | `Player/Life/PlayerReviveController.cs` (Player 프리팹) | 없음 | ✓ | |
| F11 | 23호 돌진 예약 (호스트) | `Dev/DevBossAttackHotkeys.cs` | DEV_HOTKEYS | ✗ | |
| F12 | 조각 폭발(FragmentExploder 가 붙은 오브젝트 전부) | `fragments/FragmentExploder.cs` | 없음 | ✓ | |
| Alt+1~9, Alt+0 | 튜토리얼 구간 스킵 / Alt+0 판 재시작 (호스트, 튜토리얼 중) | `Map/Tutorial/TutorialSkipController.cs` (+ `TutorialDirector.Skip.cs`·`TutorialDebugActions.cs`·`Flow/TutorialCorridorFlow.cs`) | DEV_HOTKEYS | ✗ | |
| M | 맵 오버뷰(디버그 맵) 토글 | `Map/MapOverviewUI.cs` | 없음 | ✓ | |
| `[` `]` | 미니맵 크기 프리셋 이전/다음 (슬롯 미부착일 때) | `Map/Minimap/MinimapController.cs` | 없음 | ✓ | ⚠️ `[ ]` — CameraTargetSwitcher |
| `[` `]` | 관전 대상 이전/다음 (관전 모드일 때) | `Camera/CameraTargetSwitcher.cs` | 없음 | ✓ | ⚠️ `[ ]` — MinimapController |

파일 경로는 `Assets/1.Scripts/` 기준.

- 23호 예약(F3·F4·F6·F9·F11)은 `TwentyThreeBoss.cs` 의 `DevReserveNextAttack`·`ConsumeDevReservation` 블록(같은 게이트)을 쓴다.
- **겹침은 표시만 했다** — 코드는 고치지 않았다. F9 는 23호 잡기 예약과 몬스터 즉사가 동시에 일어난다.
  F8·F7 은 둘 다 씬에 붙어 있을 때만 겹친다. `[ ]` 는 관전 모드이고 미니맵이 슬롯 밖일 때만 겹친다.
- 일반 조작 키(ESC·이동·스킬·F 상호작용·Alt+Enter 창 모드 등)는 디버그 단축키가 아니라 제외했다.
- `LookToggle` 은 게이트 대상이지만 단축키가 없다(F9 룩 토글은 09-29 에 뺐다 — 시작 룩 적용만 한다).

## 4. 게이트를 바꾼 파일 (할 일 41)

`Dev/DevBossAttackHotkeys.cs` · `Dev/DevBossEntranceWarp.cs` · `Dev/HitVFXDebugHUD.cs` · `Dev/LookAB/LookToggle.cs` ·
`Dev/LookAB/RenderCostAB.cs` · `Dev/Profiler/ProfilerHUD.cs` · `Dev/Profiler/ZonePerfRecorder.cs` · `Utility/DevMonsterKill.cs` ·
`Map/Tutorial/TutorialSkipController.cs` · `Map/Tutorial/TutorialDebugActions.cs` · `Map/Tutorial/TutorialDirector.Skip.cs` ·
`Map/Tutorial/Flow/TutorialCorridorFlow.cs` · `Monster/Boss/TwentyThreeBoss.cs` · `Rendering/RetroCRT/RetroCRTController.cs`

손대지 않은 것: `[Conditional("UNITY_EDITOR")]` 로그 헬퍼(`Edit.*`·`DashLog`·`PlayerMotor`·`Player`·`PlayerInputReader`) — 로그만 사라지고
단축키 동작에는 영향이 없다. 에디터 전용 부트 도구(`DevBootTarget`·`DevSceneBooter` 의 `#if UNITY_EDITOR`)도 그대로다.

⚠️ `TutorialDirector.Skip.cs` 는 ServerRpc 를 담고 있다 — 호스트와 클라가 **같은 심볼로 뽑은 빌드**여야 RPC 테이블이 맞는다.
