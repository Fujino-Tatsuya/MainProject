# 죽은 코드 · 1회용 코드 · 무참조 코드 전수조사 (2026-09-09)

> 팀장 지시. **①읽기전용 조사 → ②이 보고서 → ③승인 → ④삭제** 로 끊어 진행한다.
> 교훈 #67 — *파괴적 단계를 선행 단계와 같은 실행에 두려면 선행 단계를 산출물로 검증한 뒤에 실행한다.*
> 이 문서가 그 「산출물」이다. **실제로 무엇을 지웠는지는 맨 아래 §실행 이력을 볼 것** —
> 본문(조사 결과)은 조사 시점 그대로 두고, 적용은 이력에만 누적한다.

## 방법 — 참조원 4종을 각각 셌다

`grep 0건 = 삭제` 로 가면 반드시 뭔가를 조용히 죽인다(교훈 표 「부분 확인으로 없음을 단정」 사례 8건).
특히 #66 — *이 엔진에서는 배선의 절반이 프리팹에 있다.* 그래서 네 방향에서 따로 셌다.

| 축 | 무엇 | 왜 필요한가 |
|---|---|---|
| A | 다른 `.cs` 에서 타입명이 등장하는 **파일 수**(자기 파일 제외) | 코드 참조 |
| B | 직렬화 파일에서 그 스크립트 **guid** 등장 횟수 | 프리팹·씬 배선. 이름이 아니라 guid 로 센다(교훈 #91) |
| C | 직렬화 파일에서 **타입명 문자열** 등장 횟수 | UnityEvent·리플렉션·BehaviorGraph(`type: {class: X}`) |
| D | `[MenuItem]` 진입점 보유 여부 | 참조 0이어도 살아 있는 도구 |

대상: `Assets/1.Scripts`(439) + `9.ScriptableObject`(3) + `Tests`(2) + `50.Art`(1) = **445개 `.cs`**.
BroAudio(276) · INab Studio(21) 은 서드파티라 제외.

```
선언 타입 574종 / 코드 참조쌍 2266 / 직렬화 등장 guid 2004종 / 직렬화 등장 타입명 225종
→ A=0 · B=0 · C=0 파일 : 76개   (그중 D 보유 29개 / 진입점 없음 47개)
```

## 🔴 방법의 사각 — 삭제 금지로 확정한 거짓 후보 42건

조사가 끝나기 전에 이미 42건이 거짓 후보로 판정됐다. **이 42건은 A=B=C=0 이지만 살아 있다.**

| 종류 | 건수 | 왜 참조가 0인가 |
|---|---|---|
| NUnit 테스트 | 8 | 테스트 러너가 리플렉션으로 잡는다. 참조가 있을 수 없다 |
| `[CustomEditor]` / PropertyDrawer | 5 | Unity 가 어트리뷰트로 리플렉션 연결 |
| `[MenuItem]` 도구 | 29 | **진입점이 곧 참조다.** 참조 수로는 판단 불가 — 목적으로 판단해야 한다 |

테스트 8건: `DashChargeLedgerTests` · `DashRuntimeConfigTests` · `DashSnapshotHistoryTests` ·
`DashValidationPolicyTests` · `GameManagerMainGameReadyTests` · `MonsterPerceptionPolicyTests` ·
`WallOcclusionRuntimeTests` · `RuntimeSafetyTests`

CustomEditor 5건: `EffectDurationProbeEditor` · `EffectEntryEditor` · `MovingPlatformEditor` ·
`VentEditor` · `FogVolumeEditor`

## ✅ 증거가 확정된 삭제 후보

### 1. 구 `Wells&No.23` 보스 계통 — 스폰 경로가 없다

살아 있는 보스는 **`Assets/2.Prefabs/Monster/Boss/TwentyThree.prefab`** 이다
(`4.MapScene.unity` 의 `BossEncounterDirector.bossPrefab` = guid `f3f87bba…`).

| 대상 | 증거 |
|---|---|
| `Assets/2.Prefabs/Wells&No.23/Wells.prefab` | **참조 0.** 어떤 씬·프리팹·에셋도 guid `79860f5b…` 를 참조하지 않고 `DefaultNetworkPrefabs.asset` 에도 없다 → **스폰될 경로가 없다** |
| `Assets/8.BehaviorTreeGraph/Boss/Wells&No.23/No.23.asset` | **참조 0**(guid `132421a8…`). CONTEXT.md 의 "구 `TwentyThree.prefab` 이 transparentV3 에서 삭제됨" 기록과 일치 |
| BT 노드 `.cs` 21개 | 프로젝트에 BehaviorGraph 는 **3개뿐**이고 전부 이 구 보스용. 그 3개가 참조하는 `Assembly-CSharp` 클래스는 **34종**인데 이 21개는 거기에 **하나도 없다** |

⚠️ **BT 계통 전체(60개 파일 2829줄)를 지울지는 별 판단이다.** 그래프 3개를 함께 폐기하면 60개가 모두
죽지만, `Wells.prefab` + 그래프를 보존 자산으로 남길지는 팀장 결정 사항이다.

### 2. 🔴 `Enemy/Boss/` 삭제는 **아직 막혀 있다** — 착수 금지

`Monster/Editor/BossGrabSocketAuthoring.cs` 가 직접 적고 있다:

> `PlayerStateController.cs:760` 이 시전자에서 **레거시 구체 타입** `GrabController` 를 [본다].
> 그런데 `GrabController` 는 신형 보스에서 **제거된 레거시**라 부착 0곳 → … **한시적 조치**다.
> 은희 님 인터페이스가 들어오면 … `Enemy/Boss/` 레거시 폴더 삭제를 마무리한다.
> **그때까지 `GrabController.cs` 는 [남긴다].**

→ `Enemy/`(31파일 3955줄) 는 **은희 인터페이스 대기 중**이다. 지금 지우면 `PlayerStateController` 가 깨진다.
`Enemy/` 전체가 무참조로 보이는 것은 맞지만 **`GrabController` 만은 예외**이고, 그 하나 때문에 폴더가 남아 있다.

> ⚠️ **정정 (3차 · Codex)** — 마지막 문장이 **틀렸다.** `Enemy/` 31개 중 **대다수가 참조된다.**
> `GrabController` 는 예외가 아니라 여러 생존 파일 중 하나이고, 신 보스 프리팹
> (`Monster/Boss/TwentyThree.prefab:706`)에 **실제로 부착돼 있다.** §3차 표 참조.
> 순서도 거꾸로였다 — 구 보스 **그래프를 폐기한 뒤** 비로소 다수가 죽는다.

### 3. `Assets` 안의 에이전트 도구 상태 파일 — 미추적 로컬 잔여물

⚠️ **정정**: 초안에서 "커밋된"이라고 적었으나 **git 미추적**이다(`ls-files --error-unmatch` 5건 전부 실패).
커밋 이력에 남은 것이 아니라 로컬 워킹트리 잔여물이다. Unity 가 임포트한다는 점은 그대로 문제다.

```
Assets/8.BehaviorTreeGraph/Boss/.omc/state/sessions/…/pre-tool-advisory-throttle.json
Assets/8.BehaviorTreeGraph/Boss/Wells&No.23/.omc/state/agent-replay-….jsonl
Assets/8.BehaviorTreeGraph/Boss/Wells&No.23/.omc/state/idle-notif-cooldown.json
Assets/8.BehaviorTreeGraph/Boss/Wells&No.23/.omc/state/sessions/…/subagent-tracking-state.json
```
Unity 가 `.json`/`.jsonl` 을 TextAsset 으로 임포트한다. 게임 자산이 아니다.

### 4. 자기 파일이 "지워도 된다"고 적은 도구

- `Map/Editor/QuestLaserBlockerAuthoring.cs`(46줄) — *"걷어내고 나면 이 파일도 지워도 된다."*
  선행 조건: `layprefab.prefab` 의 `LaserBlockWall` 제거(실측 1건, 메뉴 실행으로 멱등 처리).

### 5. 루트의 stale `.csproj`

`VeyTrace.Map.Core.csproj` · `VeyTrace.Map.Rotation.csproj` · `VeyTrace.Map.EditModeTests.csproj` —
대응하는 `.asmdef` 가 프로젝트에 **없다**(asmdef 12개 전수 확인). Unity 가 재생성하는 파일이므로
삭제해도 무해하지만, 없는 어셈블리의 csproj 가 남아 IDE 를 혼란시킨다.

## ⚠️ 판단 필요 — 개별 확인이 끝나지 않은 13건

A=B=C=0 이고 진입점도 없다. 다만 **"자기 파일 안에서만 쓰이는 내부 타입"** 이거나
**"아직 배선 전인 신규 기능"** 일 수 있어 개별 확인이 필요하다.

> ⚠️ **정정 (3차 · Codex)** — 이 13개 중 **4개는 살아 있다.** `DevTelegraphProbe` ·
> `BossArenaDecalReceiverInstaller`(둘 다 `[RuntimeInitializeOnLoadMethod]` 로 **자기 인스턴스를 만든다**) ·
> `DistanceState` · `TrashMobState`(둘 다 `[BlackboardEnum]` 으로 **Behavior 에디터가 자동 검색**).
> 아래 표의 "메모" 를 삭제 판정으로 읽지 말 것 — §3차 의 삭제 안전 목록이 정본이다.

| 파일 | 줄 | 메모 |
|---|---|---|
| `Dev/DevTelegraphProbe.cs` | 119 | 개발용 프로브 |
| `Effects/EffectTestMover.cs` | 20 | 테스트용 이동기 |
| `Enemy/Boss/BossDeathEffectBinder.cs` | 74 | 구 보스 계통 |
| `Enemy/Boss/TriggerKnockbackAttack.cs` | 22 | 구 보스 계통 |
| `Enemy/Boss/Wells&No.23/TwentyThreeWells_Initializer.cs` | 165 | 구 보스 계통 (타입 3종) |
| `Enemy/DistanceState.cs` | 9 | enum 후보 |
| `Enemy/TrashMobState.cs` | 12 | enum 후보 |
| `Player/PlayerColorAssigner.cs` | 32 | *"OwnerClientId로 바디 머티리얼을 고른다"* — 미배선 신규? |
| `Rendering/BossArenaDecalReceiverInstaller.cs` | 92 | 데칼 이관 잔여물? |
| `Scene/KMKScene.cs` | 21 | 개인 테스트 씬 스크립트 |
| `Unit/Weapon/AttackElement.cs` | 10 | 🔴 **은희 영역** — 임의 삭제 불가 |
| `Unit/Weapon/AttackTriggerRelay.cs` | 24 | 🔴 **은희 영역** |
| `Unit/Weapon/OverlapAttack.cs` | 78 | 🔴 **은희 영역** |
| `BT/NetworkSetAnimState.cs` | 16 | BT 계통 |

## Tools/ 메뉴 67개 — 자기 신고 기준 정리 후보

파일이 스스로 폐기·1회용·한시라고 적은 것만 뽑았다(이름 추측이 아니라 주석 근거).

| 도구 | 자기 신고 |
|---|---|
| `Effects/Editor/EffectSystemSetup.cs` | *"이 폴더는 더 이상 없다 … 되살릴지 폐기할지는 VFX 담당(민경) 판단이 필요하다"* → **민경 확인 필요** |
| `Map/Editor/QuestLaserBlockerAuthoring.cs` | *"걷어내고 나면 이 파일도 지워도 된다"* |
| `Monster/Editor/BossGrabSocketAuthoring.cs` | *"한시적 조치"* → **은희 인터페이스 대기** |
| `Monster/Editor/No23ClipEventAuthoring.cs` | *"클립 이벤트는 레거시 BT 시대 이름"* → 적용 완료 여부 확인 필요 |
| `Rendering/Editor/WallOcclusionAuthoring.cs` | *"[씬이] 더 이상 존재하지 않아 이 도구가 씬을 못 찾고 있었다"* |
| `Map/Editor/ZoneWiring.cs` | *"2026-07 리팩토링: 통로는 손배치로 고정 — 절차생성/연결그래프/벽컷 [폐기]"* |

나머지 61개는 **읽기전용 검증기**와 **반복 사용 저작기**가 섞여 있어 자기 신고 근거가 없다.
개수를 줄이려면 판단 축이 필요하다(§다음 단계).

## 부수 발견 — 이번 조사에서 함께 드러난 것

1. 🔴 **`MonsterSpawner`(NetworkBehaviour)가 존 프리팹 루트 7종에 붙어 있다.**
   NGO 의 `NetworkBehaviourEditor.cs:321→413` 이 인스펙터를 그릴 때마다 `NetworkObject` 를 자동 부착하고,
   `NetworkObjectEditor.cs:188` 이 **제거 직후 다시 넣는다**("Remove Component 가 안 먹는" 증상의 정체).
   방아쇠는 `AutoAdd-NetworkObject-When-None-Exist` **`EditorPrefs`**(머신 단위) 로, 과거에 옵트아웃
   다이얼로그에서 "Yes" 를 누른 것이 영구 저장된 것이다.
   > ⚠️ **정정 (2026-09-09 2차 조사)** — 이 방아쇠 진단은 **틀렸다.** `Auto-Add` 는 원래 꺼져 있었다.
   > 실제 게이트는 **`Check for NetworkObject Component`**(`NetworkBehaviourEditor.cs:350`)이고,
   > 증상은 "조용한 자동 부착"이 아니라 **기본 버튼이 "Yes" 인 다이얼로그**다. §2차 조사 참조.
   그런데 존에 붙은 `MonsterSpawner` 는 **죽은 코드**다 — `IsServer` 가 영원히 false 라 `SpawnWave()` 가
   첫 줄에서 return 하고(`MapContentSpawner` 헤더의 2026-08-18 실측), `MapContentSpawner` 가 거기서
   읽는 것은 `ResolveSpawnPoints()` · `DefaultMonsterPrefab` **둘뿐**이다.
   → **근본 해결 = 존 프리팹에서 `MonsterSpawner` 를 떼고 순수 `MonoBehaviour` 저작 컨테이너로 교체.**
   그러면 NGO 가 손댈 이유가 영구히 사라진다(EditorPrefs 를 끄는 것은 머신 단위라 팀에 안 먹는다).
   ⚠️ `MonsterSpawner` 자체는 `MonsterScene`·`TrashMobScene` 두 씬에서 **진짜 네트워크 스포너로 쓰인다**
   (두 씬 모두 `NetworkManager` 보유) → 기반 클래스를 내리면 그 두 씬이 깨진다. **떼는 쪽이 안전하다.**

2. **`Stage1.prefab` dangling guid 3건** — `09f1c8cc…`(슬롯 4 QuestPrefab) · `0c9618bf…`(슬롯 8
   QuestPrefab) · `8c16f46f…`. 원인은 `4dc81649 chore(vcs): stop tracking SVN-owned art tree` 로 존
   프리팹이 SVN 트리 → `2.Prefabs` 사본으로 옮겨지며 guid 가 갈렸고 `ZoneLayoutCatalog.asset` 만 갱신된 것.

## 다음 단계 — 승인이 필요한 것

1. **삭제 정책** — 즉시 삭제 / 격리(`Deprecated/` 이동) / 목록만 유지 중 무엇으로 갈지
2. **BT + 구 보스 계통(그래프 3 + 프리팹 + BT 60파일 + `Enemy/` 31파일 ≈ 6800줄)** 을 한 덩어리로 폐기할지
3. **`Unit/Weapon/` 3건**은 은희, **`EffectSystemSetup`** 은 민경 확인이 필요하다(AGENTS.md §4)
4. Tools/ 67개를 줄이려면 **판단 축**이 필요하다 — 읽기전용 검증기를 남길지, 적용 완료된 배선기를 지울지
5. 조사 재현 스크립트는 세션 스크래치패드에 있다(`audit-deadcode.sh`) — 정식 보관이 필요하면
   `DevTools/` 로 옮긴다

---

## 실행 이력

### 2026-09-09 — 1차 적용 (팀장 승인: "증거 확정분만 지금 삭제")

| 대상 | 처리 | 검증 |
|---|---|---|
| `Assets/8.BehaviorTreeGraph/**/.omc/` 5파일 | 삭제 (미추적 → 커밋 없음) | 잔존 경로 0 |
| 루트 `VeyTrace.Map.{Core,Rotation,EditModeTests}.csproj` | 삭제 (미추적 · asmdef 없음) | 잔존 0 |
| BT 무참조 노드 21개 (+`.meta` 21) | 삭제 → 커밋 **`328f11d6`** | 컴파일 0에러 / 경고 4(기존과 동일, 신규 0). BT 60파일 2829줄 → 39파일 1886줄 |

교차검증: Unity 자신의 역참조 인덱스(`unity_find_references`)로 표본 2건 0건 확인.
스크립트 대상이라 **타입명 축까지 함께** 검사됐고 그쪽도 비었다
(스캔 규모 `yamlFiles 1298 · metaFiles 3466 · otherTextFiles 3626`).

### 보류 — 선행 조건이 남은 것

1. 🔴 **구 Wells 보스 계통 삭제** — 대상 파일은 확정됐으나 **`DefaultNetworkPrefabs.asset` 을 함께
   고쳐야** 한다(구 `Wells&No.23/Bomb.prefab` 이 그 목록에 등록돼 있어, 그냥 지우면 null 항목이 남아
   `NetworkManager` 시작 검증에서 걸린다). 그런데 그 파일에는 지금 **NGO 자동 부착 쓰레기 변경이
   얹혀 있어** 먼저 정리해야 한다 → NGO 토글 차단이 선행 조건.

   확정된 삭제 대상 (전부 참조 0 또는 서로만 참조):
   ```
   Assets/2.Prefabs/Wells&No.23/Wells.prefab          참조 0
   Assets/2.Prefabs/Wells&No.23/Bomb.prefab           ← 구 Wells.prefab + DefaultNetworkPrefabs
   Assets/8.BehaviorTreeGraph/Boss/Wells&No.23/No.23.asset                    참조 0
   Assets/8.BehaviorTreeGraph/Boss/Wells&No.23/Wells.asset                    ← 구 Wells.prefab
   Assets/8.BehaviorTreeGraph/Boss/Wells&No.23/No.23 BasicAttack Timer.asset  ← No.23.asset
   Assets/8.BehaviorTreeGraph/Boss/Wells&No.23/Boss State Changed.asset       ← 구 Wells.prefab
   Assets/8.BehaviorTreeGraph/Boss/Wells&No.23/ReStart.asset                  참조 0
   Assets/8.BehaviorTreeGraph/Boss/BossArea Blackboard.asset                  참조 0
   Assets/8.BehaviorTreeGraph/Boss/Open Blackboard.asset                      참조 0
   ```
   신·구 계통 분리 확인: 신 보스는 자기 `Monster/Boss/Bomb.prefab`(`BossBomb.cs` 보유)과
   자기 `Monster/Boss/Wells.prefab` 을 쓴다. 살아 있는 보스 = `Monster/Boss/TwentyThree.prefab`
   (`4.MapScene.unity` 의 `BossEncounterDirector.bossPrefab`).

   ⚠️ 이 그래프 3개를 지우면 그들이 참조하던 **`Assembly-CSharp` 클래스 34종이 새로 죽는다.**
   그 34종 삭제는 별 승인 사항이다(팀장이 고른 안에는 BT 60파일 일괄 폐기가 포함되지 않았다).

2. `Map/Editor/QuestLaserBlockerAuthoring.cs` 삭제 — 선행 조건은 `layprefab.prefab` 의
   `LaserBlockWall` 제거(`Tools/Map/Authoring/Remove Quest Laser Blockers`, 멱등).

3. **빈 폴더 1개** `Assets/1.Scripts/BT/Actions/Condition` — 이번 삭제 이전부터 비어 있었다
   (내가 지운 파일은 다른 폴더). `.meta` 만 남은 폴더라 정리 대상이지만 승인 범위 밖이라 남겼다.

4. 판단 필요 13건 · `Unit/Weapon/` 3건(은희) · `EffectSystemSetup`(민경) · Tools/ 67개 축소 —
   전부 미착수.

---

## 2차 조사 (2026-09-09) — 팀장 지시로 재검

### ⚠️ 정정 1 — NetworkObject 되쓰기 원인을 잘못 짚었다

초안은 *"과거에 옵트아웃 다이얼로그에서 Yes 를 눌러 `AutoAdd` 가 영구 저장된 것"* 이라고 적었다. **틀렸다.**
팀장 확인 결과 `Project Settings → Multiplayer → Netcode for GameObjects` 에서
**`Auto-Add NetworkObject Component` 는 원래 꺼져 있었고, `Check for NetworkObject Component` 가 켜져 있다.**

실제 게이트는 후자다 — `NetworkBehaviourEditor.cs:350`:
```csharp
if (!NetcodeForGameObjectsEditorSettings.GetCheckForNetworkObjectSetting()) return;
```
`Auto-Add` 가 꺼진 것은 "묻지 않고 넣기"만 꺼진 상태이고, 검사 자체는 계속 돌아
인스펙터를 열 때마다 *"NetworkBehaviours require a NetworkObject — 추가할까요?"* 다이얼로그가 뜬다.
기본 버튼이 **"Yes"** 이고, 제거 직후에도 `NetworkObjectEditor.cs:188` 이 같은 검사를 다시 불러 또 뜬다.

**→ 끌 토글은 `Check for NetworkObject Component` 다.**

타임스탬프 증거 (프리팹을 하나씩 클릭하며 다이얼로그를 넘긴 간격과 일치):
```
Zone_typeQuest01 14:17:28 · Zone_typeQuest02 14:20:14 · ZoneL_typeA 14:20:21 · ZoneL_typeB 14:20:26
(깨끗: ZoneL_typeC 09-08 / ZoneM_typeA·B·ZoneS_typeA 09-02)
```
배제된 후보: `ZoneMonsterSpawnerAuthoring.cs:20` 은 *"존 프리팹에 NetworkObject 는 붙이지 않는다"* 고
명시하고 실제로 안 붙인다. `BossRoomAuthoring.cs:271` 의 `AddComponent<NetworkObject>()` 는 대상이
`bossroom.prefab` 의 충전 기둥이다. 우리 코드에 존 프리팹을 대상으로 하는 부착 경로는 없다.

### ⚠️ 정정 2 — 내 「읽기전용 도구」 분류가 오탐을 냈다

1차 분류의 쓰기 탐지 정규식이 `AssetImporter` · `ModelImporter` · `SaveAndReimport` ·
`AnimationUtility.Set*` 계열을 빼먹었다. `No23ClipEventAuthoring` 의 "(적용)" 메뉴는
`importer.clipAnimations = clips; importer.SaveAndReimport();`(`:156-157`)로 **실제로 쓴다.**

정정 후: **읽기전용 9개 / 쓰기 26개** (오탐 분류는 11 / 24 였다).
교훈 #52 와 같은 뿌리 — *안 맞는 패턴은 "없음"과 구별되지 않는다.*

읽기전용 9개: `ProfilerWindow` · `BuildWindowsPlayer` · `SlotAuthoringValidator` · `ZoneWiring` ·
`BonePathDump` · `BossDataWiring` · `BossVariantAuthoring` · `BossWellsAuthoring` ·
`MonsterAnimatorParamAudit`

### 🔴 Tools/ 67개 재검 결론 — 측정으로는 삭제 근거가 0건이다

객관 축으로 **하드코딩된 대상 애셋의 존재 여부**를 전수 검사했다(도구 35개 · 경로 리터럴 전부).

```
대상 애셋이 사라진 도구 : 0건
```

즉 `WallOcclusionAuthoring` 의 *"[씬이] 더 이상 존재하지 않아 이 도구가 씬을 못 찾고 있었다"* 는
**과거 시점의 기록이고 지금은 경로가 유효하다**(이미 고쳐진 것). 자기 신고 6건 중에서도
"대상 부재"로 죽은 것은 없다.

**결론: 도구 개수를 줄이는 것은 측정 문제가 아니라 정책 문제다.** 증거 기반으로 지울 수 있는 것은
`QuestLaserBlockerAuthoring` 하나뿐이고 그것도 선행 조건(`layprefab` 의 `LaserBlockWall` 제거)이 남았다.

### 삭제가 아니라 「정정」 대상 1건 — `Map/Editor/ZoneWiring.cs`

44줄 중 **살아 있는 것은 `Tools/MapGen/Test Generate` 2개**(`MapGenerator.Generate(seed, 0)` 호출)뿐이다.
나머지는 **삭제된 시스템을 설명하는 낡은 주석**이다:

- 클래스 주석이 설명하는 "ZoneVolume → ZoneSlot 스켈레톤 생성 + 카탈로그 등록 + 참조 연결" 코드가
  **파일에 없다**(같은 주석이 스스로 *"절차생성/연결그래프/벽컷/개방변 매칭은 폐기"* 라고 적고 있다)
- `const PrefabDir = "Assets/50.Art/MapGen/MapObj/Zoneprefab"` — **존재하지 않는 폴더**다
  (그 상위엔 `LevelDeliveryV3`·`MapZoonSettingObj`·`ZoneLayout`·`ZoneMarkers`·`material`·`mesh`·`texture` 만 있다).
  둘 다 참조되지 않는 죽은 상수다
- 주석의 *"중형: 퀘스트 슬롯(4후보 중 랜덤 1곳)"* 은 실측 **2후보**(슬롯 4·8)와 어긋난다

CLAUDE.md §「문서와 코드가 충돌하면 실제 코드를 확인한 뒤 문서를 바로잡는다」 대상이다.
파일명(`ZoneWiring`)도 이제 내용과 안 맞는다 — 실질은 `MapGenTestMenu` 다.

---

## 3차 — Codex CLI 교차검증 (2026-09-09)

실행: `codex exec -s read-only` (읽기 전용). 프롬프트는 내 결론을 **확인**하지 말고 **반박**하도록 작성했다.
Codex 는 python 이 없어 Node.js 로 우회해 직렬화 파일을 뒤졌다(이 머신 Codex 셸의 함정 — `rg` 부재와 같은 계열).

### 🔴 정정 3 — 「판단 필요 13건」 중 4건은 **살아 있다.** 내 판정이 틀렸다

내가 반사 진입점 목록에서 **`[RuntimeInitializeOnLoadMethod]` 와 Behavior 의 `[BlackboardEnum]` 을 빠뜨렸다.**
더 나쁜 것은 — **1차 조사에서 이미 `InitializeOnLoad` grep 에 두 파일이 걸려 있었는데** 그것을
"데칼 이관 잔여물?" · "개발용 프로브" 로만 적고 **자기 인스턴스를 만든다는 사실과 연결하지 않았다.**
증거를 손에 들고 결론에 반영하지 못한 것이다.

| 파일 | 실제 상태 | 근거 |
|---|---|---|
| `Dev/DevTelegraphProbe.cs` | 🔴 **살아 있음** | `:35` `[RuntimeInitializeOnLoadMethod]` → `:39` 자기 컴포넌트 생성. 지우면 **에디터 F6 도구가 사라진다** |
| `Rendering/BossArenaDecalReceiverInstaller.cs` | 🔴 **살아 있음** | `:35` 자동 초기화 → `:39` 자기 생성 → `:64` `DecalReceivers.Tag(root)`. 지우면 **아레나 데칼 수신자 자동 설정이 사라진다** |
| `Enemy/DistanceState.cs` | 🔴 **에디터 등록됨** | `:3` `[BlackboardEnum]`. Behavior 패키지가 `TypeCache.GetTypesWithAttribute<BlackboardEnumAttribute>()` 로 찾는다(`BlackboardRegistry.cs:81`) |
| `Enemy/TrashMobState.cs` | 🔴 **에디터 등록됨** | 위와 동일 |

`Unit/Weapon/OverlapAttack.cs` 는 `MonsterMeleeAttack.cs:5` 에 **주석 참조**가 있다(컴파일 참조는 아님).

### 🔴 정정 4 — 「`Enemy/` 전체가 무참조로 보인다」는 것도 틀렸다

Codex 가 `Enemy/` 31개를 전수 조사한 결과 **대다수가 참조된다.** 특히:

- `Boss/GrabController.cs` 는 **신 보스 프리팹에 실제로 부착돼 있다** —
  `Monster/Boss/TwentyThree.prefab:706` · `TwentyThree_Solo.prefab:659`(`m_Enabled: 0` 이지만
  `grabSocket` 데이터 조회용이라 미사용이 아니다). `PlayerStateController.cs:760` 외에도 살아 있다.
- `Boss/FloorAreaEffect.cs` → `FX_Drop_Charge_Indicator.prefab:136` + `FloorAreaEffectSystem.cs:66`,
  그리고 VFX Entry → **`EffectCatalog.asset:22`** 까지 연결된다.
- `Boss/Wells&No.23/TwentyThreeArenaContext.cs` → `BossScene`·`MonsterScene`·`PlayerBossTest` 3개 씬.
- `Boss/ChargingObject.cs` → `BossScene` 에 실제 컴포넌트 4개.
- `MonsterTimeController.cs` · `Boss/AttackEffectRelay.cs` 는 **부착 0이지만 남은 코드의 필드 타입**이라
  단독 삭제하면 컴파일이 깨진다.
- 그래프 타입 문자열로만 살아 있는 것들: `JumpState` · `GroggyState` · `WellsState` ·
  `TwentyThreeBasicAttackChoice` · `TwentyThreeState`.

→ **`Enemy/` 는 "은희 인터페이스만 기다리는 죽은 폴더"가 아니다.** 구 보스 그래프를 폐기하면
그때 비로소 다수가 죽는다. 순서가 거꾸로였다.

### ✅ 확인된 것

| 주장 | 판정 | 비고 |
|---|---|---|
| A. 구 Wells 계통 스폰 경로 없음 | **참** | 살아 있는 보스 배선(`4.MapScene.unity:1085/1089` → `BossEncounterDirector.cs:328,339`)도 확인. `BossWellsAuthoring.cs:24` 도 **신** 경로를 쓴다 |
| C. `ZoneWiring.cs` 상수·주석이 죽었다 | **참** | 살아 있는 것은 메뉴 2개 + **`RunGen`(`:37`)**. `PrefabDir` 폴더 존재 검사 = **false** 확인. `ZoneVolume`·`ZoneDefinitionSO` 타입은 프로젝트에 **없다** |
| E. BT 21개 삭제가 아무것도 안 깼다 | **참** | 남은 BT 39개 → 삭제 타입 참조 **0** / 그래프 3개 → **0** / 직렬화 GUID → **0**. 그래프 직접 타입 합집합 **34종**도 일치 |

**`Bomb.prefab` 삭제 시 null 항목 위험은 내가 과장했다.** NGO 는 `NetworkPrefab.cs:150` 에서
경고 + `Validate()=false` → `NetworkPrefabs.cs:290` 에서 등록 거부 → `:125` 유효 항목만 담고
**계속 진행**한다. 즉 **경고 후 제외**이고 시작 실패가 아니다. 다만 목록을 자동 정리하지는 않으므로
등록 항목도 함께 지우는 것이 맞다는 결론은 유지된다.

### Codex 가 지적한 방법론 결함 6건 (수용)

1. **GUID 도 맥락 없이 세면 안 된다** — `.meta` 자기 선언 · 자기 참조 · **`stripped` 레코드** · 현재
   원본 프리팹의 타입을 구분해야 한다. 실제 반례: `4.MapScene.unity:1634` 의 `ChargingObject`
   stripped 4건이 가리키는 원본은 이미 `bossroom.prefab:3837` 에서 **`BossChargingPylon`** GUID 를 쓴다.
   내 방식으로 세면 구 타입이 살아 있는 것처럼 보인다.
2. 반사 진입점 목록이 불완전했다 — `RuntimeInitializeOnLoadMethod` · `BlackboardEnum` · Behavior 의
   노드/조건 등록(`NodeRegistry.cs:201`, `ConditionUtility.cs:28`).
3. **참조 존재 ≠ 런타임 도달 가능성.** 구 그래프끼리 참조해도 스폰 루트가 없을 수 있다.
4. **컴파일 의존성 ≠ 실행 의존성.** 부착 0이어도 필드 타입이면 단독 삭제가 깨진다.
5. 미사용 컴포넌트의 `OnNetworkSpawn` 과 **자동 실행 특성**을 혼동하면 안 된다.
6. **정적 검색 통과는 실행 검증 통과가 아니다.** 재임포트·컴파일·MPPM 은 별도.

### 부수 발견 (Codex)

- `4.MapScene.unity:1119` 의 `chargingObjects` 는 **현재 `BossEncounterDirector.cs` 에 없는 직렬화 필드**다 — 잔여 배선.
- `ZoneWiring.cs:2` 의 `using System.Collections.Generic` · `using System.Linq` 미사용.

### 삭제 안전 목록 — 3차 반영 후 (현재 정적 경로 기준)

```
Effects/EffectTestMover.cs
Enemy/Boss/BossDeathEffectBinder.cs
Enemy/Boss/TriggerKnockbackAttack.cs
Enemy/Boss/Wells&No.23/TwentyThreeWells_Initializer.cs
Player/PlayerColorAssigner.cs          (재질 변경 본문이 :26 부터 주석 처리돼 있음)
Scene/KMKScene.cs
```
제외: `Unit/Weapon/` 3건(은희 — 팀장 지시로 미착수) · 위 「살아 있음」 4건 ·
`Enemy/` 나머지(구 보스 그래프 폐기 이후로 순서 이동)

---

## 실행 이력 — 2차 적용 (2026-09-09, 팀장 승인 "①②랑 wells 계통까지 다 진행")

| # | 커밋 | 내용 | 검증 |
|---|---|---|---|
| ① | **`7b257f19`** | 무참조 스크립트 **5개** + BT 빈 폴더 4개(+`.meta`) | 선언 타입 7종 전부 코드·직렬화·guid 참조 0 재확인. 컴파일 **0에러/경고 4**(기존과 동일) |
| ② | **`df5249af`** | `ZoneWiring.cs` — 죽은 상수 2개 · 미사용 `using` 2개 · 폐기된 구현 설명 주석 제거 (`+18/−24`) | 메뉴 2개 + `RunGen` 보존 확인. Editor 어셈블리 **0에러**. 줄바꿈 오염 없음(양쪽 LF 저장 확인) |
| ③ | **`91493afc`** | 구 Wells 계통 **9파일** + `8.BehaviorTreeGraph` 트리 전체 + `DefaultNetworkPrefabs` 등록 항목 1개 + Assets 내 `.omc` 8곳(39파일) | 삭제 9 guid 를 Assets 전수 재검색 → **전부 0건**. Unity 콘솔 에러 **0건** |

팀장 지시로 **`Effects/EffectTestMover.cs` 는 보존**했다(삭제 목록에서 제외).

### 이 세션에서 내가 낸 실수 3건 (기록)

1. **`Player/PlayerColorAssigner.cs` 를 「다른 팀원 것」으로 분류하지 않았다.** `Player/` 는 AGENTS.md §5
   기준 **은희 영역**인데 삭제 목록에 그냥 넣었다. 팀장 승인 후 삭제했고 커밋 메시지에 명시했다 — **은희 공유 필요.**
2. **③ 커밋에서 `DefaultNetworkPrefabs.asset` 을 `git add` 하지 않았다.** 커밋 메시지는 그것을 포함한다고
   적고 있어 거짓 기록이 됐다. push 전이라 그 커밋에 넣어 바로잡았다(`f6a964a5` → `91493afc`).
   → **교훈: 「메시지에 적은 파일이 실제로 staged 인가」를 커밋 전에 대조할 것.**
3. **컴파일 결과를 두 번 잘못 읽을 수 있었다.** ②에서 `warningCount: 0` 은 Editor 어셈블리만 본 값이고
   기존 경고 4건은 `Assembly-CSharp`(미컴파일)에 있었다. ③에서는 `observedAssemblyCount: 0` 으로
   **아무 어셈블리도 컴파일되지 않았다** — 도구가 스스로 "nothing was verified by this cycle" 라고 적었다.
   → **교훈: `errorCount: 0` 을 보기 전에 `observedAssemblyCount` 를 먼저 볼 것.** 0이면 검증이 아니다.

### `ProjectSettings/NetcodeForGameObjects.asset` — 새로 생긴 파일 (미추적)

팀장이 설정 페이지를 열면서 생겼다. 담고 있는 것은 `NetworkPrefabsPath` ·
`TempNetworkPrefabsPath` · `GenerateDefaultNetworkPrefabs: 1` **뿐이고, 문제의 두 토글은 없다.**
→ 「`Auto-Add`/`Check for NetworkObject` 는 `EditorPrefs`(머신 단위)」 판정은 그대로 유효하다.
커밋 여부는 승인 범위 밖이라 손대지 않았다.

### 🔴 파급 재측정 — 그래프를 지우자 33개가 새로 죽었다

```
대상 .cs        424 → 419   (①의 5개)
무참조 후보      55 →  88   (+33)
직렬화 타입명   225 → 170   (-55 — 그래프가 이름으로 참조하던 만큼)
```

폴더별 신규 무참조: **`BT/` 약 34개** · **`Enemy/` 6개**(`Enemy` 4 + `Enemy/Boss` 1 +
`Enemy/Boss/Wells&No.23` 1) · 나머지는 기존 거짓 후보(테스트·CustomEditor·MenuItem 29).

**중요 — 3차에서 「살아 있다」고 판정했던 근거 두 개가 이제 사라졌다:**

- `DistanceState` · `TrashMobState` 의 `[BlackboardEnum]` 등록은 **선택지를 제공할 그래프가 0개**다.
- 남은 BT 노드의 `NodeDescriptionAttribute`/`ConditionAttribute` 에디터 등록도 같다.

즉 **프로젝트에 BehaviorGraph 가 0개인 지금, BT 계통 전체가 소비자 없는 코드**다.
다음 단계는 `BT/` 39개 + `Enemy/` 잔여를 한 덩어리로 볼지 결정하는 것이다 —
단 `Enemy/` 는 `GrabController`(신 보스 프리팹 부착) · `FloorAreaEffect`(EffectCatalog 연결) ·
`TwentyThreeArenaContext`(3개 씬) 처럼 **살아 있는 것이 섞여 있으니 폴더 단위로 밀 수 없다.**

---

## 실행 이력 — 3차 적용 (2026-09-09, 팀장 승인 "결정 3개 다 처리")

| # | 커밋 | 내용 |
|---|---|---|
| ① | **`f6175811`** | BT 노드 **37개** 삭제 + 고아 폴더 `.meta` 12개. **2개는 보존** |
| ② | **`51d6eb51`** | `Enemy/` **13개** 삭제 (도달 가능성 폐쇄로 ALIVE 15 / DEAD 13) |
| ③ | **`df3caf44`** | 존 프리팹에서 `NetworkBehaviour` 제거 — `ZoneMonsterSpawnSet` 신설 + 8종 이관 |

### ① 에서 「39개 일괄」이 아니라 37개였던 이유

- `BT/ServerSetAnimState.cs` — `NodeDescription` 없는 `NetworkBehaviour` 이고
  `50.Art/TestAssets/.../SKM_Golem (2).prefab` 에 **실제 부착**돼 있다.
- `BT/Actions/Event/ReStart.cs` — `Enemy/EnemyBTActivator.cs:27` 이
  `[SerializeField] private ReStart restartChannel` 로 들고 있다(컴파일 의존 + 직렬화 필드).
  `EnemyBTActivator` 는 `ModularRobots_R1.prefab` 에 부착되고 그 프리팹은
  `DefaultNetworkPrefabs` 와 아트 테스트 씬에서 참조된다.

BT 밖의 참조 4건은 **전부 주석**이었다(`TwentyThreeBoss.cs:119` · `AnimClipUtility.cs:10` ·
`MonsterTimeController.cs:81`). 개수만 봤으면 살아 있는 것으로 오판했을 자리다.

### ② 는 파일 단위 카운트로 안 됐다 — 폐쇄가 필요했다

살아 있는 Enemy 파일이 죽은 후보를 **필드 타입**으로 물고 있다:
`Enemy.cs → MonsterTimeController` · `TwentyThreeAnimEvents → JumpController` ·
`GrabController → TwentyThreeState`. 시드 9개(부착·외부 실코드 참조)에서 전이 폐쇄를 돌려
6개가 추가로 살아났다.

⚠️ **1차 계산은 틀렸다** — decl 맵을 `sed` 치환문으로 만들면서 `&` 가 "매치 전체"로 해석돼
`Wells&No.23` 경로가 깨졌고 `TwentyThreeState.cs` 가 ALIVE·DEAD 양쪽에 나왔다.
`printf` 로 바꿔 재계산했다. **교훈 #79 계열 — 편집·집계 도구가 종료코드 0으로 조용히 망가진다.**

### ③ — 「Remove Component 가 안 먹는」 문제의 종결

이관 도구가 구 `MonsterSpawner` + `NetworkObject` 를 함께 걷고 새 컴포넌트로 기본 몬스터를
승계한다. **스포너를 먼저 지운다** — `NetworkObject` 를 먼저 지우면 NGO 가 남은
`NetworkBehaviour` 를 보고 되붙일 수 있다.

검증: dry-run → 적용 → **재검증 "변경 0 / 이미맞음 8"(멱등)**. 디스크 전수로
존 프리팹 11종 중 `NetworkObject` 0 / 구 `MonsterSpawner` 0 / `ZoneMonsterSpawnSet` 8.
`MonsterSpawner` 는 이제 `MonsterScene`·`TrashMobScene` **두 씬에서만** 쓰인다(설계대로).

`bossroom.prefab` 의 `NetworkObject` 4개는 그대로 둔다 — 그것은 `4.MapScene`·`MonsterScene` 에
**씬 배치**된 보스 아레나이고 씬 배치 `NetworkObject` 는 NGO 가 정상 스폰한다.
카탈로그의 BossRoom 역할 디자인은 `ZoneS_typeBossEnter.prefab`(`NetworkObject` 0)이다.

### 최종 측정

```
무참조 후보  88 → 48   (그중 MenuItem 도구 29 → 비도구 19)
직렬화 타입명 170 → 169
코드 참조쌍           → 2027
```

남은 비도구 19개는 대부분 **알려진 거짓 후보**다 — NUnit 테스트 6 · `[CustomEditor]` 5 ·
`[RuntimeInitializeOnLoadMethod]` 2(`DevTelegraphProbe`·`BossArenaDecalReceiverInstaller`) ·
`Unit/Weapon/` 3(은희) · `Effects/EffectTestMover`(팀장 보존 지시) 등.

### 다음 — Play 테스트 후 퀘스트 착수

팀장이 Play 로 이상 없음을 확인한 뒤 퀘스트 영역에 착수한다.
`PLAN.md` 최상단의 퀘스트 계획은 **실측 반영 갱신이 필요하다** —
트렌치 `y=-3.49` / 상단 `y=-0.50`(단차 ≈3.0m) / 계단 2개(`x=-8`, `z=+2.2`·`-5.8`) /
기존 마커 4개가 트렌치 **밖**이라는 결과가 아직 반영되지 않았다.
