# CURRENT PLAN — DevSceneBooter: 씬 이름 한 줄로 원하는 씬 부팅 (2026-08-10)
# CURRENT PLAN — 피격 이펙트 클라이언트 복제 + 런타임 교체 디버그 HUD (2026-08-11)

> 상태: **grill 완료, 승인 대기**. 브랜치 `feature/VFX`.
> 아래 Wall Occlusion(07-28) 항목은 별개 작업 — 이 계획과 무관.

## 배경 — 작업이 둘로 갈렸다

원래 요청은 "키 입력으로 피격 이펙트를 런타임에 바꿔보는 디버그 툴"이었다. 조사 중
**별개의 실 버그**가 드러났다: 몬스터 피격 이펙트가 **호스트에서만 보인다.**

`BaseAttack.TryResolveHit`가 전부 `IsServer` 게이트라(`BaseAttack.cs:132`) `ReceiveAttack`
자체가 서버에서만 불리고, `EffectManager.Play` 호출이 그 안에 있다(`MonsterBase.cs:680`).
리슨 서버에서 호스트는 곧 서버라 호스트 화면에는 보이지만, **순수 클라이언트는 몬스터를
때려도 피격 이펙트가 아예 안 뜬다.** 디버그 편의가 아니라 출하 품질 문제다.

그래서 **A(버그 수정)와 B(디버그 툴)를 커밋 분리**해서 진행한다. B는 A 위에서만 의미가
있으므로 순서는 A → B.

## 확정 사항 (grill 결과)

| # | 결정 | 기각한 대안과 이유 |
|---|---|---|
| 1 | 이펙트 교체는 `EffectManager`의 **전역 오버라이드** | 몬스터 컨테이너 순회 — 레지스트리를 새로 만들어야 하고, 순회 후 스폰된 몹을 놓치며, 각 몹의 인스펙터 원본값을 파괴한다 |
| 2 | 서버가 **`sourcePosition`만** ClientRpc로 전 피어 브로드캐스트 | 이펙트를 NetworkObject로 — `EffectManager`의 풀링·수명·히트스톱 인프라를 통째로 우회하고 복제 트래픽이 RPC보다 훨씬 크다 |
| 3 | 각 피어가 **자기 로컬 콜라이더**로 `Resolve` + `Play` | 서버가 계산한 `Pose` 전송 — 아래 §A-2 참조 |
| 4 | `MonsterBase` / `Enemy` **각각 구현** (2벌 중복 감수) | `Unit`으로 올리기 — 코어는 은희 담당, 사전 합의 필요. 새 컴포넌트 분리 — `Enemy`가 곧 제거될 예정이라 중복 제거 명분이 사라짐 |
| 5 | IMGUI HUD, `Assets/1.Scripts/Dev/`, `F1`~`F5` 선택 + `F6` 해제 | 씬에 Canvas 배치 — `4.MapScene`은 팀 공용이고 Unity 씬 파일 머지 충돌이 지독하다 |
| 6 | `#if UNITY_EDITOR \|\| DEVELOPMENT_BUILD`로 릴리스 제외 | — `ProfilerHUD`와 동일한 관례 |

---

## A. 피격 이펙트 클라이언트 복제 (버그 수정)

### A-1. 구조
서버는 **판정만**, 재생은 각 피어가 로컬로. 이 레포에 이미 정착된 패턴이다 —
`AoeTelegraph`(`AoeTelegraph.cs:12`)와 `GauntletBot.ShowTelegraphClientRpc`가 동일 구조.

- 서버: `ReceiveAttack`에서 `hitContext.sourcePosition`(Vector3)만 ClientRpc로 전 피어에 브로드캐스트
- 각 피어: 수신 → 자기 로컬 `hitVFXCollider` / `hitPointMode` / `hitVFXType`로
  `EffectHitPoint.Resolve` → `EffectManager.Play`
- RPC는 **unreliable** — 순수 연출이라 유실이 상태 발산을 만들지 않는다

### A-2. 왜 `Pose`가 아니라 `sourcePosition`인가 (핵심 근거)

`NetworkManager.prefab`의 `TickRate: 30`, 몬스터 프리팹의 `NetworkTransform`은 `Interpolate: 1`.
클라이언트는 스냅샷 사이를 보간하려고 **의도적으로 과거를 그린다.** 렌더 지연 = 보간 버퍼
(1~2틱, 33~66ms) + 편도 지연. 인터넷 대전이면 100ms 안팎 → 몹이 4m/s로 움직일 때 **0.3~0.4m**,
몸통 반쯤 되는 거리만큼 서버 위치와 어긋난다.

서버가 계산한 `Pose`는 **월드 절대 좌표**라 그 어긋남만큼 이펙트가 몸에서 떨어져 허공에 뜬다.
반면 `SurfacePoint(collider, bounds, origin)`를 수신측이 다시 계산하면 콜라이더가 로컬
오브젝트이므로 **결과가 무조건 그 몹 표면 위**다.

비대칭이 이 설계의 근거다:
- **콜라이더 위치가 틀리면** → 이펙트가 몸에서 떨어진다 (치명적)
- **`origin`이 조금 틀리면** → 표면 위에서 점이 옆으로 미끄러질 뿐 (무해)

`origin`은 "표면의 어느 쪽을 고를지"만 결정하지 이펙트를 몸에서 떼어내지 못한다.
그래서 origin은 서버 값을 그대로 쓰고, 콜라이더는 반드시 로컬 것을 쓴다.

부수 이점: 페이로드 12B (Pose+인덱스 29B 대비 절반 이하).

### A-3. `hitVFXCollider` null 가드 (같이 처리)
현재 `MonsterBase.cs:678`이 `hitVFXCollider.transform`을 무방비로 역참조한다. 프리팹 9개에는
전부 배선돼 있어 당장 안 터지지만, **배선을 잊은 몹이 추가되면 맞을 때마다 예외**를 뿜는다.
그리고 이 줄을 RPC 수신부로 옮기면 **터지는 지점이 1개에서 N개(전 피어)로 늘어난다.**
어차피 만지는 줄이므로 가드를 함께 넣는다 — 없으면 경고 1회 후 조용히 스킵(게임은 정상 진행).

⚠️ 세션 중 논의했던 `fallbackAnchor` 재설계(`hitVFXAnchor` 필드 신설)는 **범위 밖**.
프리팹 9개를 다시 건드려야 한다. 가드만 넣고 넘어간다.

### A-4. 대상
| 프리팹 | 클래스 |
|---|---|
| ChompBot · HumanoidBot · MortarBot · PeekABot · TeslaBot · WallBot | `MonsterBase` |
| GauntletBot · SpinnerBot | `MonsterBase` 하위 (자동 커버) |
| **TwentyThree (No.23 보스)** · ModularRobots_R1 | **`Enemy`** |

`Enemy`는 제거 예정이지만 **7월 마일스톤의 보스가 그 위에 올라가 있어** 빼면 안 된다.

---

## B. 런타임 이펙트 교체 디버그 HUD

### B-1. 오버라이드 저장 위치 — `EffectManager` (SO 아님)
`EffectCatalog`는 `ScriptableObject`다. **여기에 오버라이드를 직렬화 필드로 두면 안 된다** —
SO는 씬 오브젝트와 달리 플레이 모드 중 변경이 에셋에 그대로 눌러앉는다. 플레이를 멈춰도
안 돌아오고, `.asset` 변경으로 git에 잡히고, 최악은 그대로 커밋돼 **팀 전체 기본 이펙트가
바뀐다.**

- 오버라이드는 `EffectManager`(MonoBehaviour 싱글톤)의 **런타임 필드** — 플레이 종료 시 확실히 소멸
- `EffectCatalog`는 순수 데이터로 유지
- 호출부는 `Catalog.GetHitEffect(...)` → `EffectManager.Instance.GetHitEffect(...)`로 변경

> 참고: `EnterPlayModeOptions: 0`(= 아무것도 비활성화 안 함) 확인 — 도메인 리로드는 정상
> 동작하므로 static 필드도 안전하지만, 위 이유로 SO 필드만 피하면 된다.

### B-2. 빌드 격리 제약
HUD가 `#if UNITY_EDITOR || DEVELOPMENT_BUILD`면 **릴리스 빌드엔 클래스가 없다.** 따라서
프로덕션 코드(`MonsterBase`/`Enemy`)가 HUD를 직접 참조하면 릴리스 빌드가 깨진다.
저장소를 `EffectManager`(모든 빌드에 존재)에 두면 자동 해결 — **HUD는 쓰기만, 프로덕션은 읽기만.**

### B-3. 입력 / 표시
프로젝트 전체가 Input System(`Keyboard.current`)을 쓴다.

- `F1`~`F5` → `HitEffect1`~`HitEffect5` 직접 선택 (순환보다 원하는 걸 바로 짚는 게 비교에 유리)
- `F6` → 오버라이드 해제, 각 몹 원래 `hitVFXType`으로 복귀
- 현재 적용 중인 이펙트 이름을 화면에 IMGUI로 표시
- **이미 쓰이는 키(피할 것)**: `F8` ProfilerHUD · `F10` 디버그 부활 · `M` 맵 오버뷰 ·
  `F` 다리 상호작용 · `[` `]` 카메라 전환/미니맵 줌 · `ESC` 씬 전환

### B-4. 오버라이드 범위는 **머신별**
A-1에서 각 피어가 로컬로 `GetHitEffect`를 부르므로, 키를 누른 창만 바뀐다.
디버깅엔 오히려 장점 — MPPM 창 두 개를 나란히 놓고 `HitEffect2` vs `HitEffect4`를
**동시에 비교**할 수 있다. 대신 여럿이 같이 볼 때는 "지금 뭘 보고 있는지"를 말로 맞춰야 한다.

---

## 리스크

- **호스트에서는 이 버그가 안 보인다.** 호스트 = 서버라 보간 어긋남이 0이다. 반드시
  **MPPM 클라이언트 창에서, 몹이 이동 중일 때** 때려서 검증해야 한다. 정지한 몹으로
  테스트하면 잘못된 구현도 통과한다.
- `MonsterBase`/`Enemy` 2벌 중복 — `Enemy` 제거 시 자연 해소되므로 부채로 남기지 않는다.
- `EffectCatalog.asset`이 의도치 않게 변경돼 커밋되지 않는지 `git status` 확인.
- 몹이 디스폰된 직후 RPC가 도착하면 수신측 콜라이더가 없다 → A-3 가드가 흡수(이펙트 하나 누락, 무해).

## 완료 조건

- [ ] MPPM 호스트+클라 2인, **몹이 이동 중일 때** 피격 → **양쪽 화면 모두** 이펙트가 몹 몸에 붙어 재생
- [ ] No.23 보스(`Enemy` 경로)에서도 동일하게 확인
- [ ] `F1`~`F5`로 이펙트 전환, HUD에 현재 이름 표시
- [ ] `F6`으로 각 몹 원래 값 복귀
- [ ] `hitVFXCollider` 미배선 몹에서 예외 대신 경고 1회 + 게임 정상 진행
- [ ] 콘솔 0 에러 — **호스트·클라 양쪽 모두** 확인
- [ ] 릴리스 빌드 컴파일 확인 (HUD 클래스 부재 상태에서 `MonsterBase` 참조 안 깨짐)
- [ ] `EffectCatalog.asset` 무변경 확인

## 커밋 분리

1. `fix(fx): 몬스터 피격 이펙트를 전 피어에 복제` — A
2. `feat(dev): 피격 이펙트 런타임 교체 HUD` — B

A는 리뷰 포인트가 네트워크라 팀장이 따로 볼 항목이다.

---

# CURRENT PLAN — Wall Occlusion per-pixel 재설계 (2026-07-28)

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
