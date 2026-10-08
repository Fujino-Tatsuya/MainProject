# Scene Flow 전수조사 보고서

- 조사일: 2026-10-06~07
- 대상: `fix/PeekABotSpawnIssue`, 조사 시작 HEAD `99ffc63a`
- 범위: Title → Lobby → Loading → Map → Boss/Result → Lobby, NGO 씬 이벤트, 플레이어 스폰, 실패/재접속/재시작, 런타임 생성 `NetworkObject`
- 방식: 코드·씬·프리팹·NGO 2.12 패키지 소스와 기존 로그의 읽기 전용 정적 감사
- 제한: Unity/MPPM을 재실행하지 않았고 코드·씬·프리팹은 수정하지 않았다. 따라서 기존 로그에 기록되지 않은 “실패한 정확한 `GlobalObjectIdHash`”는 새 계측 없이 복원할 수 없다.

## 1. 판정 기준

- **[확인]** 코드/직렬화 파일/로그가 직접 증명한다.
- **[고신뢰 추론]** 여러 독립 증거가 같은 결론을 가리키지만, 금지된 재실행 또는 추가 계측 없이는 마지막 인과 한 단계를 관찰할 수 없다.
- **[미확인]** 가능한 후보이나 현재 증거로 우선순위를 확정할 수 없다.
- 심각도는 **must-fix / consider / note** 세 단계로 표기한다.

## 2. 요약 결론

| 심각도 | 판정 | 결론 |
|---|---|---|
| must-fix | [확인] | 이번 무스폰의 직접 실패는 클라이언트가 서버가 보낸 MapScene 씬 배치 `NetworkObject` 하나를 로컬 씬에서 찾지 못한 것이다. `NetworkObject.Deserialize`가 실패한 뒤 NGO 자체 경고 코드가 `null.name`을 읽어 2차 NRE를 냈고, 클라이언트는 `LoadComplete`를 보내지 못했다. |
| must-fix | [고신뢰 추론] | 자산 단위 원인은 `bossroom.prefab`에 섞인 중첩 `NetworkObject` 4개다. 중요한 보정은 이 프리팹이 런타임 생성될 뿐 아니라 `4.MapScene`에도 직접 배치되어 있다는 점이다. 따라서 네 오브젝트는 실제 NGO 씬 배치 페이로드에 들어간다. 네 개 중 정확한 실패 해시는 NGO 2차 NRE 때문에 기존 로그에서 소실됐다. |
| must-fix | [확인] | 로딩 완료 조건이 NGO 씬 동기화 성공과 분리돼 있다. 각 피어의 Unity `AsyncOperation.progress`만 0.9에 도달해도 자체 메시지로 100%를 보내고, 서버는 target `LoadEventCompleted` 없이 `Completed`로 진입해 LoadingScene을 내린다. |
| must-fix | [확인] | 플레이어 스폰은 target `LoadEventCompleted` 한 곳에만 묶여 있다. 위 예외로 이벤트가 오지 않아 세 명 모두 스폰 0이 됐다. 카메라 없음/깜빡임은 이 결과이지 별도 카메라 원인이 아니다. |
| must-fix | [확인] | 타임아웃·연결 끊김·로드 요청 실패에 대한 abort/recovery 상태가 없다. 타임아웃 클라이언트를 평균에서 제외한 뒤에도 `SpawnAllPlayers()`는 모든 연결 클라이언트를 순회한다. |
| must-fix | [확인] | MapScene 릴리스 빌드가 씬에 직렬화된 개발 전용 타입을 통째로 컴파일 제외해 missing script/serialization layout 경고를 낸다. 이번 NGO 실패의 직접 원인으로 확정되지는 않았지만 빌드 씬 일관성을 깨므로 별도로 제거해야 한다. |
| must-fix | [확인] | Title/Lobby 시작 NRE는 `AudioManager.Instance` 무조건 역참조이며 호스트와 두 빌드에서 반복된다. Result도 같은 형태다. |
| consider | [확인] | Map→Result→Lobby는 NGO `NetworkSceneManager`가 아니라 로컬 `SceneManager.LoadScene(Single)`과 named message를 섞는다. 세션은 유지한 채 NGO가 추적하던 MapScene을 로컬로 없애므로 재시작/로비 복귀 계약이 없다. |
| consider | [확인] | MapScene은 additive 로드 중 Awake에서 active scene이 LoadingScene인데, 공통 탐색기가 active scene만 검색한다. Map UI 참조를 잘못 찾을 수 있다. |

## 3. 이번 장애의 실제 연쇄

### 3.1 확인된 타임라인

1. 호스트가 LoadingScene을 NGO additive로 로드하고, 그 `LoadEventCompleted` 뒤 MapScene NGO additive 로드를 시작한다. 코드: `NetworkLoadingFlowController.cs:165-207`, `:373-390`, `:555-589`.
2. NGO는 MapScene 로드 완료 직후 씬의 `NetworkObject`를 수집한다. 패키지: `NetworkSceneManager.cs:1783`, `:2742-2779`.
3. 호스트는 수집된 씬 배치 오브젝트를 `AuthorityLocalSpawn`한다. 그 루프 안에서 `MapNetworkSync.OnNetworkSpawn()`이 실행되고 맵 생성까지 재진입한다. 패키지: `NetworkSceneManager.cs:1821-1875`; 프로젝트: `MapNetworkSync.cs:35-56`. 실제 호스트 스택도 `MapNetworkSync.cs:51 → AuthorityLocalSpawn → OnSessionOwnerLoadedScene:1832`를 보인다(`network.log:403-412`).
4. 두 클라이언트는 MapScene의 Unity 로드는 끝냈지만 씬 배치 오브젝트 역직렬화 중 같은 위치에서 실패했다. `network.log:579-583`, `:602-606`; 원본 클라이언트 로그 `Instance-Run_UxbA1cuR.log:569-575`, `Instance(1)-Run_TsFItBju.log:1073-1079`.
5. NGO 클라이언트 경로는 `DeserializeScenePlacedObjects()`가 끝나야 `LoadComplete`를 전송한다. 패키지: `NetworkSceneManager.cs:1882-1920`, 특히 `:1885` 이후 `LoadComplete` 구성 `:1887`. 예외가 먼저 탈출했으므로 target MapScene의 클라이언트 `LoadComplete` 및 서버 `LoadEventCompleted`가 없다.
6. 그런데 별도 로딩 진행률 코루틴은 NGO 성공 여부가 아니라 `AsyncOperation.progress >= 0.9`만 보고 100%를 보낸다. `NetworkLoadingFlowController.cs:669-697`.
7. 서버는 평균 100%만으로 완료 코루틴을 시작한다. `NetworkLoadingFlowController.cs:751-767`. 로그상 23:01:14.865에 average=1과 완료 코루틴 시작(`network.log:624-625`), 이후 Ready/Activating/Completed(`:628-630`), LoadingScene 언로드(`:631` 이후)가 이어진다.
8. 플레이어 스폰은 target `LoadEventCompleted` 처리 끝의 `SpawnAllPlayersOnce()`에서만 호출된다. `NetworkLoadingFlowController.cs:373-437`, 특히 `:431`. 해당 이벤트가 없으므로 `Spawned player` 로그도 한 줄도 없고 스폰은 0이다.
9. LoadingScene 카메라까지 내려간 뒤 MapScene에는 소유 플레이어/플레이어 카메라가 없으므로 호스트는 “No cameras rendering”, 클라이언트는 불완전한 화면만 보인다.

### 3.2 NGO NRE가 의미하는 것

**[확인, must-fix]** 현재 NGO 패키지는 `com.unity.netcode.gameobjects@aaabf07f880c`다.

- `NetworkObject.Deserialize`는 `NetworkSpawnManager.NonAuthorityLocalSpawn(...)`을 호출한다(`NetworkObject.cs:3364`).
- 씬 오브젝트이면 `CreateLocalNetworkObject`가 `(GlobalObjectIdHash, scene handle)`로 `GetSceneRelativeInSceneNetworkObject`를 조회한다(`NetworkSpawnManager.cs:960-985`, `:977`). 찾지 못하면 `networkObject`가 null인 실패 경로다.
- 그 뒤 reader 위치가 예상과 다르면 패키지 경고가 `networkObject.name`을 읽는다(`NetworkObject.cs:3375`). 실패 객체가 null이면 원래 “어느 해시를 못 찾았는지”보다 이 2차 `NullReferenceException`만 남는다.
- 따라서 현재 스택은 “씬 배치 `NetworkObject` 로컬 해석 실패 + 동기화 reader 불일치”를 확정하지만, 실패 객체의 이름/해시는 기존 로그에서 복원할 수 없다.

수정 방향:

1. 우선 오염 자산을 제거한다(다음 절).
2. 재현 검증 전 서버/클라이언트가 `GlobalObjectIdHash`, scene handle, 이름, `NetworkBehaviour` 목록을 덤프하도록 일시 계측한다.
3. NGO 패키지 업데이트에 동일 null-deref 수정이 있는지 확인하고, 없으면 패키지를 임베드한 뒤 경고를 null-safe하게 만들며 실패 hash를 기록한다. `Library/PackageCache` 직접 수정은 커밋 대상이 아니다.

## 4. `bossroom` 가설 검증

### 4.1 규약 위반은 확정

**[확인, must-fix]** `MapContentSpawner`의 계약은 존 프리팹을 모든 피어가 로컬 `Instantiate`하고, 존 내부에는 `NetworkObject`를 두지 않는 것이다(`MapContentSpawner.cs:5-8`, `:62`, `:75-77`). 에디터 테스트도 같은 규약을 검사하지만 `bossroom`만 명시적으로 제외한다(`ZonePrefabNetworkRulesTests.cs:8-18`, `:21-34`).

`bossroom.prefab`에는 중첩 모델 오브젝트에 추가된 `NetworkObject`와 `NetworkTransform` 네 쌍이 있다.

| 프리팹 hash | 증거 |
|---:|---|
| 4130406446 | `bossroom.prefab:2730-2754` |
| 3863298080 | `bossroom.prefab:3102-3126` |
| 3199288331 | `bossroom.prefab:7873-7897` |
| 3620266727 | `bossroom.prefab:10388-10412` |

`Assets/2.Prefabs/Environment/Layouts/Zones/` 전체에서 이 규약을 위반하는 프리팹은 현재 `bossroom` 하나다.

### 4.2 “런타임 생성물이라 씬 배치 페이로드에 섞였다”는 설명은 그대로는 부정확

**[확인]** NGO는 MapScene 로드 콜백에서 먼저 `PopulateScenePlacedObjects(nextScene)`를 실행한 뒤(`NetworkSceneManager.cs:1783`) 호스트 씬 배치 스폰 루프를 돈다(`:1821-1835`). `MapNetworkSync.OnNetworkSpawn()`과 `MapGenerator.Generate()`는 그 루프 안에서 나중에 실행된다. 따라서 그 순간 `MapContentSpawner`가 새로 만든 런타임 `bossroom` 복제본은 이미 끝난 최초 수집에 소급해 들어가지 않는다. 또한 존 경로는 네 오브젝트에 `Spawn()`을 호출하지 않는다.

즉, **런타임 복제본만 있었다면** 이번 최초 MapScene 씬 배치 페이로드의 직접 원인이라고 할 수 없다. 다만 그 복제본에는 spawn되지 않은 `NetworkObject` 4개가 매번 생기므로 여전히 심각한 잠재 결함이다.

### 4.3 실제로는 동일 프리팹이 MapScene에 직접 배치돼 있다

**[확인]** `4.MapScene.unity:1137-1261`에 guid `09f37616c8805a244ae999c00477d2ab`인 `bossroom.prefab` 인스턴스가 직접 배치돼 있다. 이 인스턴스는 네 `NetworkObject`에 다음 씬 전용 hash override를 가진다.

| 프리팹 hash | MapScene 인스턴스 hash | 씬 증거 |
|---:|---:|---|
| 3199288331 | 297737433 | `4.MapScene.unity:1161-1164` |
| 3620266727 | 2407976892 | `4.MapScene.unity:1177-1180` |
| 4130406446 | 1449384102 | `4.MapScene.unity:1229-1232` |
| 3863298080 | 796511384 | `4.MapScene.unity:1245-1248` |

따라서 이 네 개는 런타임 생성 시점과 무관하게 `PopulateScenePlacedObjects`의 실제 대상이며 서버가 클라이언트에 보내는 씬 배치 페이로드에 포함된다.

### 4.4 자산 단위 근본 원인 판정

> **⚠️ 정정 (2026-10-07, 후속 실측)** — 아래 판정은 일부 틀렸다. 송전탑 NetworkObject 4개는 "실수로 추가된" 것이 아니라
> 23호 차징 패턴(`BossChargingPylon`)용 **의도된 서버 권한 오브젝트**이고, 씬 배치 자체도 문제가 아니었다.
> 실제 원인은 **MapScene 에 저장된 이 4개의 `GlobalObjectIdHash` 가 낡은 값**이라 에디터 호스트(저장값)와 빌드 클라(빌드 시 재계산값)가
> 서로 다른 hash 를 쓴 것이다. 확정 근거·수정 방향은 [criticalIssue/2026-10-07-ngo-inscene-hash-stale.md](../criticalIssue/2026-10-07-ngo-inscene-hash-stale.md).
> 아래 "수정 방향"의 NetworkObject 제거는 하지 말 것.

**[고신뢰 추론, must-fix]** 실패 자산은 `bossroom`의 네 중첩 `NetworkObject` 중 하나로 판정한다.

근거:

- NGO 스택이 씬 배치 오브젝트 조회 실패를 직접 증명한다.
- MapScene의 정상적인 네트워크 상태 소유자는 명시적인 루트 오브젝트인데, `bossroom`의 네 개만 로컬 전용 시각 존의 중첩 모델에 실수로 추가된 `NetworkObject`다.
- 해당 프리팹은 MapScene 직접 배치와 런타임 로컬 생성 양쪽에서 사용되어 두 생성 계약이 충돌한다.
- 클라이언트 두 대가 같은 위치에서 동일하게 실패한다.
- 클라이언트의 마지막 맵 생성 로그가 `bossroom` 처리(`Instance-Run_UxbA1cuR.log:568`, 다른 클라이언트 `:1072`)이고 바로 다음 줄이 NRE다. 이 한 줄만으로 인과를 증명하지는 않지만 위 정적 증거와 일치한다.

남은 식별 한계:

- NGO 2차 NRE가 serialized hash를 기록하지 않아 네 개 중 어느 hash가 최초 실패했는지는 **미확인**이다.
- 재실행 금지 조건 때문에 “네 개 제거 전/후” 대조 실험은 이번 감사에서 하지 않았다.
- 따라서 보고서는 실패 클래스는 **확정**, `bossroom` 자산은 **고신뢰 원인 판정**, 네 개 중 정확한 한 컴포넌트는 **미확인**으로 구분한다.

수정 방향:

1. `bossroom.prefab`의 네 `NetworkObject`/`NetworkTransform`을 제거한다. 시각 장치 상태가 필요하면 씬 상주 단일 manager가 순수 로컬 컴포넌트를 구동하는 기존 `ZoneBridgeGateManager` 패턴을 쓴다.
2. MapScene에 배치한 고정 보스룸과 런타임 존이 같은 프리팹을 공유해야 한다면, 공용 visual prefab은 완전 비네트워크로 유지하고 네트워크 상태 소유자는 MapScene 루트에 별도 배치한다.
3. `ZonePrefabNetworkRulesTests`의 `bossroom` 예외를 제거한다.
4. `MapContentSpawner`도 instantiate 전에 `GetComponentsInChildren<NetworkObject>(true)`를 검사해 0이 아니면 hash/경로와 함께 즉시 실패시킨다. 에디터 테스트만으로는 새 예외나 빌드 자산 오염을 막지 못한다.

## 5. MapScene 씬 배치 `NetworkObject` 목록

YAML 기준 MapScene에는 총 10개의 씬 배치 `NetworkObject`가 존재한다.

| 이름/출처 | scene hash | 비고 |
|---|---:|---|
| BossEncounterDirector | 1554909941 | 직접 배치, `4.MapScene.unity:857-899` |
| BossTeleportManager | 1139285423 | 직접 배치, `:1920-1960`; 같은 오브젝트에 BossTimerManager도 있음 |
| ZoneBridgeGateManager | 3343419440 | 직접 배치, `:2311-2350` |
| MapNetworkSync | 2213399674 | 직접 배치, `:2384-2460` |
| MinimapNetworkSync | 2776485797 | 직접 배치, `:2506-2605` |
| GameRule prefab instance | 2080339570 | 정상적인 씬 프리팹 인스턴스 형태. source hash 3954788080, `:6429-6439` |
| bossroom nested #1~#4 | 297737433 / 2407976892 / 1449384102 / 796511384 | 로컬 존 규약 위반, 위 4.3 절 |

`BossEncounterDirector`, `BossTeleportManager`, `BossTimerManager`, `ZoneBridgeGateManager`, `MapNetworkSync`, `MinimapNetworkSync`에는 전체 타입/직렬 필드를 빌드 구성에 따라 제거하는 `#if UNITY_EDITOR || DEVELOPMENT_BUILD`가 없다. 이 사실만으로 이들을 완전히 배제할 수는 없지만, 현재 정적 증거에서는 `bossroom` 네 개가 유일한 구조적 이상치다.

## 6. 정상 의도 기준 전체 씬 흐름

| 단계 | 현재 트리거와 소유자 | 실제 전환 방식 | 감사 결과 |
|---|---|---|---|
| Bootstrap → Title | `GameManager.Start` | 로컬 `SceneManager.LoadScene(Single)` (`GameManager.cs:66-77`) | 세션 시작 전이므로 방식은 타당. |
| Title → Lobby | Title 버튼 → `GameManager.GoToLobby` | 로컬 Single (`GameManager.cs:171-177`) | 세션 시작 전에는 타당. |
| Lobby 네트워크 시작 | `LobbySceneManager` → `NetworkSessionLauncher` | Host/Client 시작, 로딩 콜백 등록 | 연결 실패 UI는 Lobby가 살아 있을 때만 동작. |
| Lobby → Loading | 호스트 `LobbySceneManager.StartGameLoading` | NGO additive (`NetworkLoadingFlowController.cs:165-207`) | 시작 버튼을 먼저 비활성화하며 실패 복구가 없음 (`LobbySceneManager.cs:252-276`). |
| Loading → Map | LoadingScene `LoadEventCompleted` 뒤 호스트가 target NGO additive | `NetworkLoadingFlowController.cs:373-390`, `:555-589` | 올바른 NGO 경로이나 성공 barrier가 깨져 있음. |
| Map 생성 | 씬 배치 `MapNetworkSync.OnNetworkSpawn` | 같은 seed로 각 피어 로컬 존 생성, 서버만 몬스터 spawn (`MapNetworkSync.cs:35-56`, `MapContentSpawner.cs:16-110`) | 생성이 NGO 씬 배치 spawn 루프 안에서 재진입한다. 생성 오류를 로딩 실패로 승격하지 않음. |
| 플레이어 생성 | target `LoadEventCompleted` | 서버가 client별 prefab instantiate 후 `SpawnAsPlayerObject` (`NetworkLoadingFlowController.cs:439-514`) | 트리거가 단일 이벤트에 결박됨. late join 경로 없음. |
| Map → Boss | 별도 씬 전환 없음 | MapScene 내부 `BossTeleportManager`, `BossEncounterDirector` | 보스는 동일 씬의 상태 전환이다. |
| Boss/전멸 → Result | 서버 `MapSceneManager.GoToResult` | named message 송신 후 각 피어 로컬 Single (`MapSceneManager.cs:82-98`, `:140-150`, `:207-268`) | NGO가 관리하던 MapScene을 NGO 밖에서 제거한다. |
| Result → Lobby | Result 버튼 → `GameManager.GoToLobby` | 각 피어 로컬 Single (`ResultSceneManager.cs:27-...`, `GameManager.cs:171-177`) | 세션 종료/유지 정책과 동기화 barrier가 없다. |

## 7. 로딩 성공/실패 판정 결함

### 7.1 표시 진행률이 성공 barrier를 대신한다

**[확인, must-fix]** `ReportLocalProgress()`는 Unity 비동기 로드가 0.9에 도달하면 1을 제출한다(`NetworkLoadingFlowController.cs:669-697`). 서버는 평균이 1이면 target `LoadEventCompleted`, 씬 오브젝트 동기화, 플레이어 스폰 여부와 무관하게 완료한다(`:751-767`, `:591-630`). 이번 로그가 이 경로를 그대로 증명한다.

수정 방향:

- 표시 progress와 상태 barrier를 분리한다. 표시값은 0.99 이하에서 멈춰도 된다.
- 서버의 성공 조건은 최소한 다음을 모두 만족해야 한다.
  1. target NGO `LoadEventCompleted` 수신
  2. 필요한 참가자가 `ClientsThatCompleted`에 존재
  3. 서버 MapScene 생성/검증 성공
  4. 각 참가자의 `PlayerObject` 생성 성공
  5. 필요하면 클라이언트의 local player/camera ready ACK
- 그 전에는 `NotifyMainGameReady`, LoadingScene 언로드, `Completed` 송신을 금지한다.

### 7.2 명시적인 실패 상태와 롤백이 없다

**[확인, must-fix]** `NetworkLoadingPhase`는 Idle부터 Completed까지만 있고 Failed/Aborting이 없다(`NetworkLoadingPhase.cs:1-9`).

- LoadingScene 최초 Load 요청 실패는 warning만 남기고 이미 바뀐 phase/UI를 복구하지 않는다(`NetworkLoadingFlowController.cs:187-205`).
- target Load는 `SceneEventInProgress`만 30프레임 재시도하며, 그 밖의 실패는 warning만 남긴다(`:569-585`).
- unload는 `UnloadEventCompleted`를 무기한 기다린다(`:970-1002`).
- Map 생성 중 예외/오류를 flow 실패로 전달하는 계약이 없다. 현재 호스트도 bridge open 위치 누락 오류를 냈지만(`network.log:399-412`) 로딩은 계속됐다.

수정 방향:

- `Failed`/`Aborting` 상태, 전체 deadline, 오류 코드와 사용자 메시지를 추가한다.
- 실패 시 새 네트워크 scene load를 막고, 진행 중 scene event를 정리한 뒤 정책에 따라 전원 Lobby 복귀 또는 문제 클라이언트 disconnect를 수행한다.
- 모든 coroutine 대기는 timeout/cancellation token 또는 flow generation id로 취소 가능해야 한다.
- `MapGenerator.Generate`는 bool/result 또는 예외 경계를 통해 치명적 저작 오류를 로딩 controller에 반환해야 한다.

### 7.3 타임아웃 처리 후에도 잘못된 클라이언트를 스폰한다

**[확인, must-fix]** target `LoadEventCompleted`에서 timed-out client는 tracking/progress에서 제거한다(`NetworkLoadingFlowController.cs:415-420`). 그러나 바로 뒤 `SpawnAllPlayers()`는 `_networkManager.ConnectedClientsIds` 전체를 순회한다(`:439-454`). 아직 연결은 살아 있지만 MapScene 동기화에 실패한 클라이언트에도 PlayerObject를 spawn할 수 있다.

수정 방향:

- 스폰 대상은 `ClientsThatCompleted`와 현재 연결 집합의 교집합으로 고정한다.
- timeout 정책을 “kick 후 나머지 진행” 또는 “전원 abort” 중 하나로 명시한다. timeout 참가자를 조용히 제외하고 성공 처리하지 않는다.

### 7.4 연결 끊김 복구가 Lobby 수명에 종속된다

**[확인, must-fix]** Lobby의 상세 disconnect/transport failure UI는 `LobbySceneManager`에 있고(`LobbySceneManager.cs:428-483`), Lobby 언로드 시 콜백을 해제한다(`:626-637`). 영속 `NetworkSessionLauncher`의 stopped/disconnect 핸들러는 캐릭터 선택 저장소만 비운다(`NetworkSessionLauncher.cs:470-478`). `NetworkLoadingFlowController`도 disconnect callback을 구독하지 않는다.

결과:

- Loading/Map에서 호스트 연결이 끊겨도 오류 화면이나 안전한 씬 복귀 주체가 없다.
- 로딩 중 끊긴 client가 tracking 집합에 남아 NGO timeout까지 기다리거나, 별도 progress와 엇갈릴 수 있다.

수정 방향:

- 영속 session coordinator가 connect/disconnect/server stopped/transport failure를 소유한다.
- loading controller는 disconnect 즉시 참가자 집합과 barrier를 갱신하고 정해진 정책을 실행한다.
- UI scene manager는 표시만 맡고 세션 생명주기 판단을 소유하지 않는다.

## 8. 플레이어 스폰, 늦은 합류, 재시작

### 8.1 단일 이벤트 결박

**[확인, must-fix]** 초기 플레이어 생성의 유일한 호출점은 target `LoadEventCompleted`의 `SpawnAllPlayersOnce()`다(`NetworkLoadingFlowController.cs:431`, `:457-465`). 이벤트 핸들러가 한 번 예외로 끊기면 재시도/화해(reconciliation)가 없다.

수정 방향:

- “client가 target scene ready이고 PlayerObject가 없으면 spawn”이라는 idempotent 서버 메서드를 만든다.
- target `LoadComplete`, `LoadEventCompleted`, client connect/reconnect 후 scene sync 완료에서 동일 메서드를 호출할 수 있게 한다.
- 성공 조건 검사 때도 참가자별 `PlayerObject` 존재를 재검증한다.

### 8.2 늦은 합류는 현재 지원되지 않는다

**[확인, must-fix 또는 정책 명시]** 로딩 controller의 `HandleClientConnected`는 phase가 Idle 또는 Completed면 즉시 반환한다(`NetworkLoadingFlowController.cs:276-287`). 완료 후 `ResetFlow()`는 Completed를 그대로 유지한다(`:911-940`). 따라서 Map 플레이 중 새 연결에는 로딩 상태도, 플레이어 스폰도 보내지 않는다.

반면 `MapNetworkSync`는 `_ready`가 이미 true인 늦은 합류 클라이언트가 seed/difficulty로 맵을 생성하도록 작성돼 있다(`MapNetworkSync.cs:43-56`). 맵 상태는 late join을 일부 지원하지만 player lifecycle은 지원하지 않는 불완전한 조합이다.

수정 방향:

- late join 비지원이면 connection approval에서 게임 시작 후 접속을 명시적으로 거절하고 사유를 보낸다.
- 지원이면 NGO scene synchronization 완료를 client별로 추적한 뒤 해당 client만 spawn하고, 선택 캐릭터/게임 진행 상태/보스 상태/파괴물 상태까지 동기화한다.

### 8.3 Result/Lobby 복귀와 재시작 계약이 없다

**[확인, consider]** Map→Result는 서버가 named message를 보내고 모든 피어가 로컬 `SceneManager.LoadScene(Single)`을 호출한다. Result→Lobby도 로컬 Single이다. `NetworkSessionLauncher`의 명시적 shutdown은 `OnApplicationQuit`뿐이다(`NetworkSessionLauncher.cs:443-458`).

위험:

- NGO의 scene tracking과 실제 로컬 씬이 달라진다.
- PlayerObject/동적 오브젝트/session selection/loading phase의 다음 판 초기화 순서가 정의되지 않는다.
- named message는 유실/연결 끊김/느린 피어에 대한 완료 barrier가 없다.
- 호스트만 다음 게임을 시작해도 각 피어가 같은 source scene/active scene에 있다는 보장이 없다.

수정 방향은 둘 중 하나를 선택한다.

1. **세션 종료형:** 서버가 전환을 공지 → 모든 피어 shutdown 완료 ACK → 로컬 Result/Lobby로 이동 → 새 세션 생성.
2. **세션 유지형:** Map unload와 다음 network scene 전환을 NGO `NetworkSceneManager`로 수행하고, Result/Lobby UI만 필요하면 로컬 additive overlay로 분리한다.

## 9. 런타임 생성 `NetworkObject` 전수 결과

| 생성 지점 | 현재 방식 | 판정 |
|---|---|---|
| 존 layout (`MapContentSpawner.cs:62`) | 모든 피어 로컬 instantiate, `Spawn()` 안 함 | 규약 자체는 타당. `bossroom` 네 중첩 NO 때문에 위반. 런타임 복제마다 orphan NO 4개 생성. must-fix. |
| 일반 몬스터 (`MapContentSpawner.cs:337-343`) | 서버 instantiate → root `NetworkObject.Spawn()` → 추적/despawn | 정상적인 서버 권한 경로. |
| 보스 (`BossEncounterDirector.cs:329-352`) | 서버 instantiate → `NetworkObject.Spawn()` | 정상적인 서버 권한 경로. |
| 플레이어 (`NetworkLoadingFlowController.cs:468-514`) | 서버 instantiate → target scene 이동 → `SpawnAsPlayerObject` | 생성 코드는 타당하나 trigger/barrier/late join이 결함. |
| 존 bridge/panel | 로컬 순수 컴포넌트, 씬 상주 `ZoneBridgeGateManager`가 상태 소유 | 로컬 존 규약에 맞는 권장 패턴. |
| breakable crate | crate는 로컬, 상위 broadcaster/MapNetworkSync가 상태 전달 | 오브젝트마다 NO를 두지 않는 권장 패턴. |

추가 안전장치:

- zone prefab뿐 아니라 `MapContentSpawner`가 참조하는 모든 layout prefab을 recursive 검사한다.
- 서버 spawn 대상 prefab은 root `NetworkObject` 유무, 등록된 network prefab 여부, child `NetworkObject` 정책을 빌드 전 검증한다.
- MapScene의 씬 배치 NO 목록/해시를 CI에서 스냅샷 비교하면 의도치 않은 nested NO 추가를 바로 잡을 수 있다.

## 10. Scene manager 및 빌드 일관성 문제

### 10.1 Title/Lobby/Result 시작 NRE

**[확인, must-fix]** Title은 `AudioManager.Instance.PlayBGM(AudioManager.Instance.Catalog.TitleBGM)`을 무조건 호출한다(`TitleSceneManager.cs:39-54`, 특히 `:53`). Lobby도 동일하다(`LobbySceneManager.cs:84-96`, 특히 `:95`). Result는 `AudioManager.Instance.StopBGM()`을 무조건 호출한다(`ResultSceneManager.cs:20-25`).

로그:

- 호스트 Title NRE: `network.log:29-30` (`TitleSceneManager.cs:53`)
- 호스트 Lobby NRE: `network.log:121-122` (`LobbySceneManager.cs:95`)
- 두 빌드 로그도 시작부 `:58`, `:91`에 같은 NRE가 반복된다.

수정 방향:

- Bootstrap에서 `AudioManager` 존재를 필수 검증해 없으면 명시적 오류를 내고, 각 scene manager는 null-safe 호출로 2차 장애를 막는다.
- 오디오가 필수 시스템이면 “없어도 진행”과 “부팅 실패” 중 정책을 정하고 한 곳에서 처리한다. 현재처럼 매 씬 `Start`에서 NRE를 내는 방식은 금지한다.

### 10.2 MapScene additive Awake가 LoadingScene을 검색한다

**[확인, consider]** 로그에서 `MapSceneManager.Awake activeScene=2.LoadingScene`이다(`network.log:573-576` 부근, 클라이언트 원본 `:562-563`). `MapSceneManager.Awake`는 곧바로 `ResolveSceneReferences()`를 호출한다(`MapSceneManager.cs:28-37`). 공통 `NemoSceneManager.FindInActiveScene`는 `SceneManager.GetActiveScene()`의 root만 검색한다(`NemoSceneManager.cs:150-165`).

따라서 MapScene 자기 UI가 직렬화 배선되지 않은 경우 LoadingScene을 검색하고 누락될 수 있다. fade가 LoadingScene의 overlay를 잘못 잡거나 null로 동작할 가능성도 있다.

수정 방향:

- manager 자신의 `gameObject.scene`을 기준으로 검색한다.
- 가능하면 이름 탐색을 없애고 씬 직렬화 참조 + `OnValidate` 검증으로 고정한다.
- active scene 변경은 MapScene 완전 준비 후 한 주체가 명시적으로 수행한다.

### 10.3 GameState가 NGO 성공보다 먼저 MainGame이 된다

**[확인, consider]** `GameManager.HandleSceneLoaded`는 Unity `sceneLoaded`에서 MapScene 이름만 보고 `MainGame`으로 바꾸고 서버 시계를 시작한다(`GameManager.cs:224-240`). 이번 클라이언트 로그에서도 NRE 직전에 `Loading -> MainGame`이 이미 발생했다(`network.log:576-578`, `:599-601`).

수정 방향:

- Unity scene load는 “파일 로드됨”, NGO barrier 완료는 “게임 준비됨”으로 별도 상태를 둔다.
- `MainGame`/공유 시계 시작은 플레이어 spawn 및 ready barrier 뒤 단 한 번 수행한다.

### 10.4 릴리스 빌드의 missing script/serialization layout

**[확인, must-fix]** MapScene에는 다음 개발 컴포넌트가 직렬화돼 있지만 클래스 전체가 `#if UNITY_EDITOR || DEVELOPMENT_BUILD`로 감싸져 있다.

| 타입 | 클래스 조건부 컴파일 | MapScene 증거 | 클라이언트 로그 |
|---|---|---|---|
| LookToggle | `LookToggle.cs:27` | `4.MapScene.unity:3236` | `Instance-Run_UxbA1cuR.log:425-426` |
| ProfilerHUD | `ProfilerHUD.cs:17-419` | `4.MapScene.unity:3255-3272` | `:427-429` |
| RenderCostAB | `RenderCostAB.cs:27` | `4.MapScene.unity:4650` | `:430-432` |
| HitVFXDebugHUD | `HitVFXDebugHUD.cs:22` | prefab/scene instance `4.MapScene.unity:6278` | `:423-424`, `:433-435` |

두 번째 빌드도 동일 경고를 낸다(`Instance(1)-Run_TsFItBju.log:1053-1065`). `FogManager`에도 missing Behaviour가 하나 있다(`Instance-Run_UxbA1cuR.log:424`).

이번 NRE는 NGO 씬 오브젝트 조회에서 났고 위 타입들은 확인된 씬 배치 NetworkBehaviour가 아니므로, 이 경고를 이번 직접 원인으로 단정하지 않는다. 하지만 에디터 호스트와 릴리스 플레이어가 서로 다른 씬 컴포넌트 구성을 갖는 것은 확정이며 제거해야 한다.

수정 방향:

- `ZonePerfRecorder`처럼 타입과 직렬화 layout은 모든 빌드에서 유지하고 실행 코드만 조건부 컴파일한다.
- 또는 빌드 전처리에서 개발 컴포넌트를 씬/프리팹에서 명시적으로 제거하고, 에디터/릴리스 양쪽의 씬 배치 NO hash/NetworkBehaviour 목록을 검증한다.
- release build smoke test에서 missing script와 “different serialization layout”을 실패 조건으로 둔다.

## 11. 권장 수정 순서

1. **P0 — 자산 오염 제거:** `bossroom`의 네 `NetworkObject`/`NetworkTransform` 제거, 테스트 예외 제거, runtime fail-fast 추가.
2. **P0 — 증거 보강 후 MPPM 검증:** MapScene load 직전/직후 씬 배치 NO inventory를 서버·클라이언트에 기록하고 누락 hash를 확인한다. 기존 제약상 이 감사에서는 실행하지 않았다.
3. **P0 — 로딩 barrier 교체:** progress 100%와 성공을 분리하고 target `LoadEventCompleted` + 참가자별 PlayerObject/ready를 완료 조건으로 만든다.
4. **P0 — 실패/timeout/disconnect:** Failed/Aborting, deadline, kick-or-abort 정책, Lobby 복귀를 구현한다.
5. **P0 — 플레이어 lifecycle:** 참가자별 idempotent spawn/reconcile와 late join 지원 또는 명시적 거절을 구현한다.
6. **P1 — 세션 전환 정책:** Result/Lobby를 세션 종료형 또는 NGO 유지형 중 하나로 통일한다.
7. **P1 — 씬 manager 정리:** AudioManager NRE, MapScene active-scene 검색, 너무 이른 MainGame 상태 전환을 수정한다.
8. **P1 — 빌드 일관성:** 개발 전용 컴포넌트의 missing script/layout 경고를 0으로 만든다.
9. **P2 — 저작 오류:** bridge open 위치 누락, NavMeshAgent 생성 경고, 음수 scale collider 경고를 빌드 전 검사로 이동한다. 이번 무스폰의 직접 원인은 아니지만 Map 생성 성공 판정을 신뢰하려면 필요하다.

## 12. 수정 후 필수 검증 매트릭스

| 시나리오 | 합격 조건 |
|---|---|
| 에디터 호스트 + 빌드 클라 2, 정상 시작 | 세 피어 target `LoadComplete`; 서버 target `LoadEventCompleted`; 참가자별 PlayerObject 1개; LoadingScene은 그 뒤 언로드 |
| 클라이언트 한 대 MapScene 역직렬화 실패 강제 | progress가 100이어도 Completed 금지; 오류 표시; 정책대로 전원 abort 또는 실패 client kick |
| 클라이언트 로드 timeout | timed-out client에 PlayerObject를 만들지 않음; 참가자 집합/정책 로그가 명확함 |
| 로딩 중 disconnect/host 종료 | 무한 대기 없음; 살아 있는 피어가 정해진 씬/UI로 복귀 |
| 늦은 합류 | 비지원이면 승인 단계에서 명시적 거절; 지원이면 scene ready 뒤 그 client PlayerObject 1개 |
| Boss clear/party wipe → Result | 모든 피어가 같은 결과를 표시하고 MapScene/NGO tracking이 일치 |
| Result → Lobby → 두 번째 게임 | stale PlayerObject/scene event/selection/loading phase 없이 첫 게임과 동일하게 시작 |
| 릴리스 빌드 | missing script, serialization layout mismatch, scene placed NO soft-sync 오류 0건 |

## 13. 최종 답변

- **왜 아무도 스폰되지 않았나:** 클라이언트 MapScene 씬 배치 `NetworkObject` 역직렬화가 예외로 중단되어 target `LoadEventCompleted`가 발생하지 않았고, 플레이어 스폰이 그 이벤트 하나에만 묶여 있었기 때문이다. **[확인]**
- **왜 로딩은 끝났나:** 별도 progress 채널이 Unity `AsyncOperation`만 보고 100%를 보내며, 서버가 NGO 성공 barrier 없이 `Completed`와 LoadingScene unload를 실행했기 때문이다. **[확인]**
- **왜 카메라가 없나:** 플레이어/플레이어 카메라가 0인데 LoadingScene 카메라까지 내려갔기 때문이다. **[확인된 연쇄 결과]**
- **`bossroom`이 원인인가:** 네 `NetworkObject`는 규약 위반이며, 런타임 복제본뿐 아니라 MapScene 직접 배치 인스턴스에도 존재해 실제 씬 배치 페이로드에 들어간다. 현재 자산 중 유일한 비정상 중첩 집합이므로 자산 단위 원인으로 판정한다. 단, NGO의 2차 NRE가 누락 hash를 가려 네 개 중 정확한 하나는 기존 로그만으로 식별할 수 없다. **[고신뢰 원인 판정 + 명시적 식별 한계]**
