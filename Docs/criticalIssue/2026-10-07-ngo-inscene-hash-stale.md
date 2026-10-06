# 에디터 호스트 + 빌드 클라에서 MapScene 진입 시 플레이어 미스폰 — 씬 배치 NetworkObject hash 불일치

| 항목 | 내용 |
|---|---|
| 발견 | 2026-10-06 ~ 07, MPPM 로컬 빌드 인스턴스 테스트 중 |
| 상태 | **원인 확정 · 미수정** (MapScene 재저장 대기) |
| 영향 | 로비 → 로딩 → `4.MapScene` 진입 시 **아무도 스폰되지 않음** → 게임 진행 불가 |
| 재현 조건 | **에디터 호스트 + 빌드 클라** 조합에서만. 에디터끼리(MPPM 가상 플레이어)는 재현 안 됨 |
| 확인 환경 | Unity 6000.3.16f1, NGO `com.unity.netcode.gameobjects` 2.12.0, 브랜치 `fix/PeekABotSpawnIssue`(HEAD `99ffc63a`) |
| 관련 문서 | [scene-flow-audit.md](../tech/scene-flow-audit.md) — 같은 장애에서 드러난 씬 흐름 결함 전수조사 |

## 1. 증상

- 호스트(에디터): Game 뷰에 **"No cameras rendering"**.
- 빌드 클라: 깜빡이는 화면만 렌더링.
- 로딩 화면은 정상적으로 100% → 완료까지 진행된 뒤 사라진다. 그래서 "로딩은 됐는데 화면이 없다"처럼 보인다.

## 2. 원인 (확정)

`4.MapScene` 의 `bossroom` 안 송전탑(`Env_Mv_bosscharger_upper`, 23호 차징 패턴용 `BossChargingPylon`) 4개에 붙은
**씬 배치 NetworkObject 의 `GlobalObjectIdHash` 가 씬 파일에 낡은 값으로 저장돼 있었다.**

| 송전탑 | 씬 파일 저장값 (에디터 Play 가 사용) | 다시 계산한 값 (빌드가 사용) |
|---|---|---|
| #1 | 2407976892 | 722896760 |
| #2 | 1449384102 | 907036865 |
| #3 | 796511384 | 1909650729 |
| #4 | 297737433 | 818016615 |

나머지 씬 배치 NetworkObject 6개(BossEncounterDirector, BossTeleportManager, GameRule, MapNetworkSync, Minimap, ZoneBridgeGateManager)는
저장값 = 재계산값으로 정상이었다.

### 왜 에디터와 빌드가 다른 값을 쓰나 (NGO 2.12 소스)

- hash 는 에디터의 `NetworkObject.OnValidate()` 에서 `GlobalObjectId.GetGlobalObjectIdSlow(this).ToString().Hash32()` 로 계산해
  씬에 저장한다 (`NetworkObject.cs:254-310`).
- **Play 모드 중에는 다시 계산하지 않는다** — `if (EditorApplication.isPlaying && !string.IsNullOrEmpty(gameObject.scene.name)) return;` (`NetworkObject.cs:262`).
  → 에디터 호스트는 **씬 파일에 저장된 값**을 쓴다.
- **빌드는 빌드 시점에 다시 계산한 값**을 쓴다. 빌드 클라 로그의 hash 가 에디터에서 다시 계산한 값과 4개 모두 정확히 일치했다(§4).
- `bossroom.prefab` 은 10-02(`91cad8a7` 존 원본 0.98 축소), 10-06(`b2582266` 그림자 끄기)에 내부가 바뀌었다.
  송전탑은 "씬 → bossroom 프리팹 인스턴스 → 중첩 프리팹 인스턴스 → 그 위에 덧붙인 NetworkObject" 구조라 GlobalObjectId 가 바뀌었는데,
  그 뒤 MapScene 을 에디터에서 열고 **저장한 사람이 없어** 새 hash 가 파일에 기록되지 않았다.
  (어느 커밋에서 정확히 바뀌었는지는 미확인. OnValidate 는 `RecordPrefabInstancePropertyModifications` 만 하고 씬을 dirty 로 만들지 않아,
  씬을 열기만 해서는 저장되지 않는다.)

### 왜 플레이어가 아무도 안 나왔나 (연쇄)

1. 서버(에디터 호스트)가 MapScene 씬 배치 오브젝트 목록을 **낡은 hash** 로 클라에 보낸다.
2. 빌드 클라는 자기 씬에서 그 hash 를 찾지 못한다 → `NonAuthorityLocalSpawn` 실패 → NGO 가 실패 경고를 찍으려다 null 인
   `networkObject.name` 을 읽어 **NRE** (`NetworkObject.cs:3348~` `Deserialize`).
   ```
   NullReferenceException
     at Unity.Netcode.NetworkObject.Deserialize (...) [0x000a6]
     at Unity.Netcode.SceneEventData.DeserializeScenePlacedObjects ()
     at Unity.Netcode.NetworkSceneManager.OnClientLoadedScene (...)
   ```
3. 예외 때문에 클라가 MapScene `LoadComplete` 를 보내지 못한다 → 서버에 MapScene `LoadEventCompleted` 가 오지 않는다.
4. 플레이어 스폰(`NetworkLoadingFlowController.SpawnAllPlayersOnce`)은 **그 이벤트 안에서만** 호출된다 (`NetworkLoadingFlowController.cs:431`) → 스폰 0.
5. 그런데 로딩 흐름은 NGO 동기화 성공이 아니라 씬 로드 진행률 100% 만 보고 `Completed` 로 넘어가 LoadingScene 을 내린다 → 카메라 0개.

3~5 의 구조적 약점(실패해도 완료로 넘어감, 스폰 트리거 단일 이벤트)은 이 hash 문제와 별개로 남아 있다 — [scene-flow-audit.md](../tech/scene-flow-audit.md) §7·§8.

## 3. 왜 찾기 어려웠나

- **에디터끼리 테스트하면 절대 재현되지 않는다.** 양쪽 다 낡은 저장값을 써서 일치한다.
- **전원 빌드도 재현되지 않을 가능성이 높다** — 양쪽 다 새로 계산한 값을 쓴다(아래 §6, 미검증).
- 어긋나는 건 **에디터 호스트 + 빌드 클라** 조합뿐이다. MPPM 을 "로컬 빌드 인스턴스"로 바꾼 뒤에야 드러났다.
- NGO 가 원래 남겨야 할 "어느 hash 를 못 찾았다" 경고가 2차 NRE 로 가려져 로그에 hash 가 없었다.
- 처음엔 "존 비네트워크 규약 위반(bossroom 안 NetworkObject)", "런타임 사본과 hash 중복", "빌드 버전 불일치"를 의심했으나 셋 다 아니었다.
  송전탑 NetworkObject 는 **의도된 서버 권한 기믹**(23호 차징 패턴)이다 — 지우면 안 된다.

## 4. 확정 근거

| 근거 | 내용 |
|---|---|
| 빌드 클라 로그 | MapScene 로드 직후(NGO 처리 전) 클라 씬의 송전탑 hash = 722896760 / 907036865 / 1909650729 / 818016615 |
| 호스트 로그 | 서버 `ScenePlacedObjects`(클라로 보내는 원천)의 송전탑 hash = 2407976892 / 1449384102 / 796511384 / 297737433 |
| 에디터 재계산 | MapScene 을 에디터에서 열고 NGO 와 같은 방식으로 재계산 → 빌드 클라 값과 4개 모두 일치, 나머지 6개는 저장값 = 재계산값 |
| 저장소 검색 | 빌드 쪽 hash 4개는 저장소 어떤 씬·프리팹에도 없다 = 빌드 때 새로 계산된 값 |
| 빌드 신선도 | `Builds/PlayModeScenarios/Windows` 는 같은 코드로 22:49 에 새로 빌드됨 → 빌드 버전 불일치 가설 기각 |

사용한 진단 도구(임시, 커밋하지 않음):
- `Assets/1.Scripts/Dev/DiagNetObjInventory.cs` — 서버·클라 MapScene 씬 배치 NetworkObject 인벤토리(`[DiagNO]`), 빌드에서도 찍힘
- `Assets/1.Scripts/Dev/Editor/DiagNOHashCheck.cs` — 메뉴 `Tools/Diag/NetworkObject Hash Check`, 열린 씬의 저장 hash 와 재계산 hash 비교

## 5. 로그 위치 (같은 문제를 다시 조사할 때)

| 대상 | 경로 |
|---|---|
| 에디터 호스트 | 저장소 루트 `network.log` (프로젝트 로거), `%LOCALAPPDATA%\Unity\Editor\Editor.log` |
| MPPM 로컬 빌드 인스턴스 | `Temp/com.unity.multiplayer.playmode/ScenariosLogs/<Instance>-Run_*.log` (Play 마다 새로 생김, Temp 라 사라질 수 있음) |
| MPPM 빌드 결과물 | `Builds/PlayModeScenarios/Windows/Windows.exe` |
| 단독 실행 빌드 | `%USERPROFILE%\AppData\LocalLow\DefaultCompany\MainProject (Client)\Player.log` |

⚠️ `Editor.log` 는 PC 전체에서 하나다. 다른 Unity 에디터를 켜면 덮어쓰인다 — 분석 전에 먼저 복사할 것.

## 6. 수정 방향

1. **MapScene 재저장** — 에디터에서 `4.MapScene` 을 열어 새 hash 를 파일에 기록한다(씬을 dirty 로 만든 뒤 저장).
   바뀌는 줄이 송전탑 hash 4개뿐인지 diff 로 확인한다. 송전탑·23호 차징 패턴은 그대로 유지된다.
2. **같은 구성의 다른 씬 점검** — `Assets/0.Scenes/Debug/PlayerBossTest.unity` 에도 송전탑 4개가 있다. 같은 메뉴로 검사한다.
3. **재발 방지** — "씬 배치 NetworkObject 저장 hash = 재계산 hash" 검사를 EditMode 테스트 또는 빌드 전 검사로 둔다.
   bossroom·송전탑 프리팹을 고친 뒤 MapScene 을 저장하지 않으면 다시 터진다.

### 미검증 가설

- **전원 빌드(호스트도 빌드)면 이 문제는 나지 않는다.** 근거: 빌드는 매번 hash 를 새로 계산하고, 빌드 클라 값이 재계산값과 일치했다.
  확인 방법: 같은 `Windows.exe` 를 두 개 띄워 하나는 호스트, 하나는 클라로 MapScene 까지 진입 → 클라 로그에 `Deserialize` NRE 가 없고 플레이어가 스폰되면 확인.
  확인되더라도 1번(재저장)은 필요하다 — 에디터 호스트 디버깅 워크플로가 막히기 때문이다.

## 7. 경과

| 시각 | 내용 |
|---|---|
| 10-06 23:01 | 에디터 호스트 + 빌드 클라 2 로 첫 재현. 클라 2개 모두 같은 위치에서 NRE |
| 10-07 00:07 | Codex 씬 흐름 전수조사 보고서 작성(완료 보고 전 워크스페이스 크레딧 소진으로 중단) |
| 10-07 00:30 | claude-alt 조사: 송전탑 NetworkObject 는 23호 차징 패턴용 의도된 오브젝트 — 제거 보류 |
| 10-07 02:31 | `[DiagNO]` 인벤토리로 송전탑 4개만 호스트·클라 hash 불일치 확인 |
| 10-07 02:37 | 에디터 재계산으로 "씬 파일의 낡은 저장값" 확정 |
