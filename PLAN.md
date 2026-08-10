# CURRENT PLAN — DevSceneBooter: 씬 이름 한 줄로 원하는 씬 부팅 (2026-08-10)

> 상태: **승인 대기**. 구현 착수 전.
> 요청자·담당: 은희. `GameManager.cs` 수정 포함 — 2026-08-03 계획서는 이 파일을 팀장 영역이라 봤으나,
> 은희 판단으로 진행한다(추가 4줄·기존 경로 무영향).
> 브랜치 예정: `feature/DevSceneBooter` (base `development`), 레인 `dash`.
> 전신 계획: `git show c98710024:PLAN.md` — "개발 진입점 단일화 + 맵 단독 Play 부팅"(목표 2만 이행됨).

## 목표

**`DevSceneBooter`의 `Scene` 필드에 씬 이름을 적고 Dev_Boot 씬을 Play하면, 그 씬이 정식 흐름과
동일한 상태로 부팅된다** — 호스트 기동, 플레이어 스폰, MainGameReady 발행, 액티브 씬 지정까지.

기존 정식 흐름(BootStrap→Title→Lobby→Loading→MapScene)은 **한 줄도 바뀌지 않는다.**

## 현재 이해 (코드 실측 완료)

| 사실 | 근거 |
|---|---|
| 매니저 4종은 `0.BootStrapScene`에만 있다 | NetworkManager·GameManager·AudioManager·EventSystem 프리팹 인스턴스 |
| `4.MapScene`에 NetworkManager·ForProfile 인스턴스 **0개** (GameManager 참조 1건은 버튼 OnClick이 프리팹 에셋을 가리키는 것) | 씬 GUID 스캔 |
| **강제 타이틀 이동의 정체** = 조건 없는 `LoadScene(titleSceneName)` | [GameManager.cs:59](Assets/1.Scripts/Managers/GameManager.cs:59) |
| ⭐ **로딩 컨트롤러는 `loadingSceneName`이 아니라 `targetSceneName` 기준으로 반응한다** → 로딩씬을 생략해도 스폰·완료 체인이 그대로 돈다 | [:272](Assets/1.Scripts/Loading/NetworkLoadingFlowController.cs:272), [:296](Assets/1.Scripts/Loading/NetworkLoadingFlowController.cs:296), [:338](Assets/1.Scripts/Loading/NetworkLoadingFlowController.cs:338) |
| 타겟 씬 로드 완료 → `SpawnAllPlayersOnce()` → `BroadcastAverageProgress()` → `_phase==LoadingGame && avg>=1` 이면 완료 코루틴 → `NotifyMainGameReady()` | [:376](Assets/1.Scripts/Loading/NetworkLoadingFlowController.cs:376), [:692](Assets/1.Scripts/Loading/NetworkLoadingFlowController.cs:692), [:553](Assets/1.Scripts/Loading/NetworkLoadingFlowController.cs:553) |
| 로비가 씬에 없으면 `CanStartFromLobby()`는 그냥 통과 | [:171](Assets/1.Scripts/Loading/NetworkLoadingFlowController.cs:171) |
| `SpawnAllPlayers`는 public이고 `PlayerObject != null`이면 스킵 → **중복 호출 안전** | [:384](Assets/1.Scripts/Loading/NetworkLoadingFlowController.cs:384), [:420](Assets/1.Scripts/Loading/NetworkLoadingFlowController.cs:420) |
| `NotifyMainGameReady`는 멱등 | [GameManager.cs:81](Assets/1.Scripts/Managers/GameManager.cs:81) |
| 🔴 `MarkMainGameStart()`는 **멱등이 아니다** — 부를 때마다 재스탬프 | [NetworkClock.cs:125](Assets/1.Scripts/Network/NetworkClock.cs:125) |
| `GameManager`는 `4.MapScene`일 때만 자동으로 `MarkMainGameStart` | [GameManager.cs:216](Assets/1.Scripts/Managers/GameManager.cs:216) |
| MainGameReady 실소비자 = 플레이어 **AudioListener 활성화**, InGame BGM | [PlayerAudioListenerActivator.cs:32](Assets/1.Scripts/Player/PlayerAudioListenerActivator.cs:32), [MapSceneManager.cs:58](Assets/1.Scripts/Managers/MapSceneManager.cs:58) |
| 컨트롤러는 **소스 씬 언로드 중에만** `SetActiveScene`을 한다 → 로딩씬 생략 시 `_sourceSceneName`이 비어 액티브 씬이 Dev_Boot에 남는다 | [:961](Assets/1.Scripts/Loading/NetworkLoadingFlowController.cs:961), [:984](Assets/1.Scripts/Loading/NetworkLoadingFlowController.cs:984) |
| 🔴 테스트 씬 6종이 빌드 목록 `enabled: 0` → 런타임 `LoadScene` 불가 | `ProjectSettings/EditorBuildSettings.asset` |
| 🔴 빌드 스크립트는 **EditorBuildSettings의 enabled 씬 전부**를 플레이어에 넣는다 | [BuildWindowsPlayer.cs:48](Assets/1.Scripts/Editor/BuildWindowsPlayer.cs:48) |

## 접근

### 1. 새 씬 `Assets/0.Scenes/Dev/Dev_Boot.unity` (빌드 목록에 넣지 않는다)

**`0.BootStrapScene`을 그대로 복제**하고 `DevSceneBooter` 오브젝트 하나만 추가한다.
BootStrap은 매니저 프리팹 인스턴스 4개가 루트에 있는 373줄짜리 단순 씬이라 복제가 깔끔하다
(NetworkManager / GameManager / AudioManager / EventSystem — AudioManager·EventSystem을 빼면
BGM·UI 입력이 죽는다. `c0d4457d3`에서 부팅 씬 단일 소유로 옮겨졌다).

복제이므로 매니저 구성이 BootStrap과 자동으로 일치한다 — 초안에서 우려했던 "5번째 매니저 추가 시
드리프트"는 최초 구성이 동일해지므로 위험이 줄지만, **향후 BootStrap에 매니저가 추가되면 Dev_Boot에도
수동 반영해야 한다**는 점은 남는다.

### 2. `DevSceneBooter.cs` (신규, `Assets/1.Scripts/Dev/`)

```
[SerializeField] string scene = "4.MapScene";   // ← 여기만 바꾼다
[SerializeField] bool  autoBootOnPlay = true;
[SerializeField] GameObject playerPrefabOverride;   // 비우면 NetworkManager 프리팹 기본값
```

`Awake()`: `FindFirstObjectByType<GameManager>()?.SuppressStartupSceneLoad()`
→ `Instance`가 아니라 `Find`를 쓰는 이유 = 두 Awake의 실행 순서는 보장되지 않지만, 오브젝트 존재는
씬 로드 시점에 보장된다. `Start()`는 모든 `Awake` 뒤라 억제가 반드시 선행된다.

`Start()` → 코루틴:
1. `scene`이 빌드 목록에 있고 enabled인지 `SceneUtility`로 검사 → 아니면 **조치 가능한 에러 로그** 후 중단
2. `flow.SetEditorDefaults("2.LoadingScene", scene, 0f, 0f)`
3. `launcher.StartHost()` → 실패 시 에러 후 중단
4. `IsListening && IsServer && SceneManager != null` 까지 대기
5. `nm.SceneManager.LoadScene(scene, Additive)` — `SceneEventInProgress`면 정식 경로와 동일하게 재시도
6. 씬 `isLoaded` 까지 대기 → **`SceneManager.SetActiveScene(scene)`** (컨트롤러가 안 해주는 유일한 일)
7. 한 프레임 뒤 안전망: 호스트에 `PlayerObject`가 없으면 `flow.SpawnAllPlayers()`,
   `NetworkClock.HasMainGameStarted`가 false면 `MarkMainGameStart()` (재스탬프 방지),
   `GameManager.Instance.NotifyMainGameReady()` (멱등)
   → 6·7은 이벤트 순서에 의존하지 않게 만들기 위한 것이고, 전부 중복 호출 안전이 확인된 API다.

8. **Dev_Boot 씬을 로컬 언로드한다** (정식 흐름이 소스=로비 씬을 언로드하는 것과 대칭).
   반드시 7단계의 `SetActiveScene` **뒤에** 해야 한다(액티브 씬을 먼저 옮기지 않으면 언로드 불가).
   Dev_Boot은 NGO가 아니라 로컬 로드된 씬이므로 `SceneManager.UnloadSceneAsync`를 쓴다
   (컨트롤러의 [UnloadLocalScene](Assets/1.Scripts/Loading/NetworkLoadingFlowController.cs:932) 과 동일한 방식).

   **매니저 생존 실측 완료** — 언로드해도 4종 전부 살아남는다:
   NetworkManager = NGO가 `OnEnable`에서 `DontDestroyOnLoad`(부모 없을 때. `Library/PackageCache/
   com.unity.netcode.gameobjects@aaabf07f/Runtime/Core/NetworkManager.cs:1087`) /
   [GameManager.cs:51](Assets/1.Scripts/Managers/GameManager.cs:51) /
   [AudioManager.cs:42](Assets/1.Scripts/Sound/AudioManager.cs:42) /
   [PersistentEventSystem.cs:28](Assets/1.Scripts/UI/PersistentEventSystem.cs:28).
   → **전제: Dev_Boot에서 매니저 4종은 반드시 루트 오브젝트로 둔다**(NetworkManager는 부모가 있으면
   `DontDestroyOnLoad`가 걸리지 않는다).

### 3. 기존 파일 수정 1건 (**호출되지 않으면 완전히 무영향**)

- `GameManager.cs` — `SuppressStartupSceneLoad()` + `Start()`의 early-return. 4줄.
- `NetworkLoadingFlowController.cs`는 수정하지 않고 기존 `SetEditorDefaults(...)`를 재사용한다.
  Dev 경로는 `StartGameLoading()`을 호출하지 않으므로 로비 준비 게이트는 사용되지 않는다.

### 4. 빌드 씬 목록 — 테스트 씬 6종 `enabled: 1`

`BossScene`·`MonsterScene`·`PlayerScene`·`PlayerBossTest`·`PlayerDashTest`·`CamaraScene`.
런타임 로드의 전제 조건이라 피할 수 없다.

⚠️ **인수인계 사항 — 빌드 스크립트는 은희가 직접 수정한다(2026-08-10 결정).**
[BuildWindowsPlayer.cs:48](Assets/1.Scripts/Editor/BuildWindowsPlayer.cs:48)이 `EditorBuildSettings`의
enabled 씬 전부를 플레이어에 담으므로, **그 수정 전까지는 테스트 씬 6종이 빌드에 실려 나간다.**
이 계획은 `BuildWindowsPlayer.cs`를 건드리지 않는다.

## 리스크

| 리스크 | 대응 |
|---|---|
| 테스트 씬은 NetworkObject 저작·레이어 표준(`f7fba054c`)이 정식 씬과 달라 전투가 깨져 있을 수 있다 | **전투 정상 동작이 완료조건에 포함됨(2026-08-10 결정).** 씬을 NGO `SceneManager.LoadScene`으로 싣기 때문에 씬 배치 NetworkObject는 자동 스폰된다 — 그게 전제 조건은 충족시킨다. 씬별로 실제 타격까지 Play 검증하고, 저작 누락은 고친다. 씬 콘텐츠 자체를 재설계해야 하는 건이 나오면 별건으로 보고한다 |
| MapScene은 `MapGenerator` 스폰 슬롯이 필요 — 없으면 폴백 위치(0,5,0) | 정식 흐름과 동일 동작이라 회귀 아님 |
| 테스트 씬에 인라인 AudioListener/EventSystem이 있으면 중복 | `RuntimeSceneServiceCoordinator`·`PersistentEventSystem`이 이미 정리함 |
| Dev_Boot이 액티브 씬으로 남는 사고 | 6단계 `SetActiveScene`이 유일한 방어선 → 검증 항목에 포함 |

## 검증 방법

1. 컴파일 0 에러.
2. `Dev_Boot` Play + `Scene = 4.MapScene` → 플레이어 스폰·이동, BGM 재생, 액티브 씬이 `4.MapScene`,
   Dev_Boot 언로드됨, `[NetworkClock] MainGame 시작 스탬프` 로그 **1회만**.
3. **전투 검증 (완료조건)** — 대상 씬마다: 좌클릭 기본공격이 몹에 **데미지 적용**(체력 감소·히트플래시·
   플로팅 데미지), 몹이 사망까지 가고, 몹의 반격도 플레이어 체력을 깎는다.
   대상: `4.MapScene` / `BossScene` / `MonsterScene` / `PlayerScene` / `PlayerBossTest` / `PlayerDashTest`.
   씬별 결과를 표로 기록하고, 깨진 건 원인(레이어·Hurtbox·NetworkObject 저작)까지 규명한다.
4. `Scene = 없는이름` → 빌드 목록 안내 에러 후 조용히 중단(예외 없음).
5. **회귀**: `0.BootStrapScene` Play → 타이틀→로비→로딩→맵이 종전과 동일. 로딩화면 대기시간도 5s/2.5s 유지.
6. MPPM 2인으로 Dev_Boot 호스트 + 클라 접속 1회 확인(정식 경로 재사용이라 되어야 정상).

## 진행 상황 (2026-08-10)

**구현 완료 · 컴파일 0에러 0경고**

| 항목 | 상태 |
|---|---|
| `Assets/1.Scripts/Dev/DevSceneBooter.cs` | ✅ 신규 |
| `Assets/1.Scripts/Dev/Editor/DevBuildSceneList.cs` | ✅ 신규 — 빌드 씬 목록 등록을 Unity가 하게 하는 메뉴. Unity가 열려 있을 때 `EditorBuildSettings.asset`을 파일로 고치면 메모리 값에 덮여 조용히 되돌아가므로 필요했다 |
| `GameManager.SuppressStartupSceneLoad()` + `Start()` 가드 | ✅ |
| `NetworkLoadingFlowController.SetEditorDefaults()` 재사용 | ✅ |
| `Assets/0.Scenes/Dev/Dev_Boot.unity` | ✅ BootStrap 복제 + DevSceneBooter (구조 검증: 프리팹 4개·루트 5개·스크립트 guid 일치) |
| 빌드 씬 목록 테스트 씬 6종 활성화 | ✅ 메뉴로 적용, 디스크 반영 확인(12씬 전부 enabled) |
### Play 검증 결과 (`4.MapScene` · `MonsterScene`)

| 확인 항목 | 결과 |
|---|---|
| 호스트 기동 + NGO 씬 로드 | ✅ `4.MapScene`·`MonsterScene` 모두 로드 |
| 액티브 씬 전환 | ✅ `isActive: true`로 타겟 씬이 잡힘 |
| 플레이어 스폰 | ✅ `Paladin(Clone)` **정확히 1개**. PlayerInput·NetworkObject·NetworkTransform·PlayerDefaultAttack·스킬 4종·HitFlash·FloatingDamagePresenter까지 완비 |
| 씬 배치 NetworkObject 스폰 | ✅ MonsterScene 봇 9종이 `Enemy` 레이어 + Hurtbox + MonsterBase로 살아있음 |
| 게임 화면 | ✅ 톱다운 카메라·팔라딘 렌더링 정상(스크린샷 확인) |
| **몹 → 플레이어 전투** | ✅ **작동.** 근접(`MonsterMeleeAttack.Hit`)·투사체(`MonsterProjectile.OnTriggerEnter`)·폭발(`Detonate`) 3경로 모두 피해 적용, 방어력 경감까지(요청 10 → 실제 8, defense 25). 체력 9881 → 9805 연속 감소 |
| **플레이어 → 몹 전투** | ⏸ 합성 입력으로 발동 실패. 아래 참고 |
| 에러·경고 | 관측 구간에서 0건 |

**플레이어 공격을 자동 검증하지 못한 이유(정직하게)**: MCP 합성 마우스 입력으로 좌클릭 공격을 발동시키지
못했다. 격리 시도에서 플레이어를 (40, 40)으로 텔레포트했는데 그곳이 MonsterScene 바닥 밖이라 낙하 상태가 됐고,
낙하 중엔 공격이 거부된다 — 내 테스트가 스스로를 무효화했다. 헛스윙은 로그를 남기지 않아 "발동 안 됨"과
"빗맞음"도 구분되지 않았다.
`attackSpeed: 0`은 조사 결과 **무혐의**다 — 그 값은 `Unit.AttackSpeed`로 들어가 몹·보스 쿨다운
(`MonsterBase.CooldownReady`)에만 쓰이고, 플레이어 공격 주기는 `DevaultAttackController.attackSteps`가 정한다.
**중요**: 플레이어는 정식 흐름과 **동일한 프리팹·동일한 스폰 코드**(`NetworkLoadingFlowController.SpawnAllPlayers`)로
생성된다. 따라서 플레이어 공격 거동이 개발 부팅과 정식 부팅에서 달라질 수 있는 경로가 없다.
→ 사람이 좌클릭 한 번 하는 것으로 5초 만에 확정된다. 은희님 확인 요청.

### 실측으로 부정된 가설 — 씬 배치 플레이어 중복

정적 분석에서는 테스트 씬들에 활성 `Player.prefab` 인스턴스가 있어 Paladin과 **플레이어 2명**이 될 것으로
봤다. Play 실측 결과 `Player` 컴포넌트는 **1개**뿐이었다(`Paladin(Clone)`). 중복이 발생하지 않으므로
대응 코드는 넣지 않았다.

### 🔴 발견된 결함 2건 — 둘 다 수정 완료

**1. 부팅 씬을 `UnloadSceneAsync`로 언로드하면 에디터가 Play 모드를 종료한다.**
대조 실험으로 확정했다 — 언로드 ON이면 Play가 2~3초 만에 죽고, OFF면 계속 유지된다.
정식 흐름이 로비 씬을 언로드해도 괜찮은 것은, 그전에 타이틀 씬을 **Single 로드로 교체**해서
Play 원본 씬이 이미 바뀌어 있기 때문이다.
→ **수정**: 명시 언로드를 버리고 타겟 씬을 **`LoadSceneMode.Single`로 실어 부팅 씬을 대체**한다
(`replaceBootScene`, 기본 켜짐). 결과는 요청하신 대로 부팅 씬이 하이어라키에서 사라지는 것이고,
Play 모드는 유지된다. Single 로드는 이 오브젝트도 파괴하므로 `DontDestroyOnLoad`로 빼두었다가
부팅이 끝나면 스스로 `Destroy`한다.

**2. Play 진입 시 `Dev_Boot`이 빌드 씬 목록에 잡혔다**(`buildIndex: 12`).
단, `ProjectSettings/EditorBuildSettings.asset` **디스크에는 기록되지 않았다** — 에디터가 Play를 위해
임시로 잡은 인메모리 상태였고 영속되지 않는다. 즉 현재 빌드 유출 위험은 없다.
→ 그래도 안전판으로 `Dev/빌드 씬 목록/Dev 부팅 씬을 목록에서 제거` 메뉴를 추가했다.

### 남은 확인 (Unity 재컴파일 대기 중)

- 수정된 Single 로드 경로로 `4.MapScene` 재검증 + Dev_Boot 씬 필드 재저장(`replaceBootScene` 키로 갱신)
- 정식 흐름 회귀(`0.BootStrapScene` Play → 타이틀→로비→로딩→맵)
- MPPM 2인
- 나머지 테스트 씬 4종(`BossScene`·`PlayerScene`·`PlayerBossTest`·`PlayerDashTest`) 부팅

### ⚠️ 별건 — 게임에 AudioListener가 없다 (내 작업과 무관, 기존 결함)

`Paladin.prefab`에는 `AudioListener`도 `PlayerAudioListenerActivator`도 **없다**(`Player.prefab`에는 둘 다 있다).
MainFlow 전체에서 AudioListener를 가진 것은 `2.LoadingScene` 하나뿐이라, **로딩 씬이 언로드되는 순간
게임에 리스너가 0개가 된다** → 소리가 안 나고 Unity가 매 프레임 경고를 뿜는다(콘솔 버퍼를 도배해서
디버깅도 방해한다).
기본 플레이어를 Player → Paladin으로 바꾼 `0bca7a01c`에서 두 컴포넌트가 함께 넘어오지 않은 것으로 보인다.
**정식 흐름에도 그대로 있는 결함이다.** 이 계획의 범위 밖이라 손대지 않았다 — 별건으로 처리할지 은희님 판단.

## 범위 밖

- 테스트 씬 내부 콘텐츠 수리, `ForProfile` 정리, 로비 진입점 추가 변경
- `4.MapScene` 및 정식 MainFlow 씬 수정 (일절 건드리지 않는다)
