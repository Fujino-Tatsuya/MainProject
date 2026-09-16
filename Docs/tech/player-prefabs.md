# 플레이어 프리팹 — `Player.prefab` / `Armature` / `Paladin.prefab`

> **목적.** 이 프리팹들은 이름도 내용도 비슷해서 AI 에이전트와 사람이 반복해서 헷갈린다.
> "무엇이 무슨 역할이냐", "지금 실제로 스폰되는 건 뭐냐", "어느 쪽을 고쳐야 하냐"의 **단일 사실 원본**이다.
> 사실(as-is) 항목은 2026-09-16 기준 `feature/player-motor` 브랜치의 **YAML·코드를 직접 읽어** 확인했다.
> 코드 주석·옛 문서를 믿지 말고 §9 의 확인 명령으로 다시 검증할 것.

---

## 0. 30초 요약

**설계 의도(to-be).** `Player` 는 말 그대로 **플레이어라는 역할**이다 — 네트워크·입력·이동·스킬·생명주기를
소유한다. 그 밑의 **`Armature` 자식을 교체해서 플레이 캐릭터를 바꾼다.** 캐릭터마다 프리팹을 통째로
복제하지 않는다.
**교체 시점 = 스폰 전.** 유저가 고른 값에 따라 어떤 캐릭터로 스폰될지가 결정된다(팀 확정 2026-09-16).
스폰된 뒤에 캐릭터가 바뀌는 경우는 없다.

**현재 데이터(as-is).** 정식 흐름이 실제로 스폰하는 것은 **`Paladin.prefab`** 이다. 이것은 역할+캐릭터가
한 덩어리로 평탄화된 **통짜 복제본**이라 위 의도에서 벗어나 있다.

| 질문 | 답 |
|------|-----|
| `Player` 의 역할은? | **캐릭터에 무관한 플레이어 역할 셸.** 캐릭터는 `Armature` 자식 교체로 바꾼다 |
| 지금 정식 흐름이 스폰하는 건? | **`Paladin.prefab`** — 의도와 다른 통짜 복제본이다 |
| 지금 게임 동작을 바꾸려면? | **`Paladin.prefab`** 을 고친다. `Player.prefab` 만 고치면 게임에 반영되지 않는다 |
| 교체 구조는 구현돼 있나? | **코드는 있고, 데이터가 없다** — `PlayableCharacterVisual` + `CharacterDefinition` 이 존재하지만 **어느 프리팹에도 안 붙어 있고 `CharacterDefinition` 에셋은 0개**다 (§1) |
| 둘은 같은 프리팹의 변형(Variant)인가? | **아니다.** 완전히 독립된 프리팹이라 자동 동기화가 **전혀** 없다 |
| 런타임 로그에서 어느 쪽인지 아는 법 | 오브젝트 이름. `Paladin(Clone)` = 현재 정상 / `Player(Clone)` = 구 경로로 스폰된 것 |

---

## 1. 설계 의도 — `Player` = 역할, `Armature` = 캐릭터

### 1.0 확정된 것 (2026-09-16 팀 결정)

- `Player` 는 캐릭터에 무관한 **역할** 프리팹이다. 캐릭터는 그 밑 **`Armature` 자식 교체**로 바꾼다.
- **캐릭터는 스폰 전에 결정된다** — 유저가 선택한 값이 입력이고, 그 값에 따라 스폰되는 캐릭터가 달라진다.
- 따라서 **스폰 이후 캐릭터 교체(인게임 체인지)는 설계 범위가 아니다.**
- ✅ **구현 방식 = `Player.prefab` 을 base 로 하는 캐릭터별 Prefab Variant.**
  안정성을 우선한 선택이다. 런타임 Armature 교체(§1.5 (B))는 **채택하지 않는다.**
  Variant 를 각각 NetworkPrefab 으로 등록하고, 유저 선택값으로 **스폰할 Variant 를 고른다.**

### 1.1 계층에서의 역할 분담

```
Player                     ← 역할. 캐릭터가 뭐든 바뀌지 않는다
│                             네트워크(NetworkObject/NetworkTransform) · 입력(PlayerInput)
│                             이동(PlayerMotor/PlayerMovement) · 스킬 · 생명주기 · 상태이상 · HurtBox
└─ Armature                ← 캐릭터. 이걸 교체해서 플레이 캐릭터를 바꾼다
   ├─ (리그/본 계층)          모델 · Animator · SkinnedMeshRenderer
   ├─ DefaultAttack/AAC1~4    캐릭터별 평타 히트박스
   ├─ MainSkill/InterruptAttack  캐릭터별 스킬 히트박스
   └─ L/R_WeaponSocket        무기 소켓
```

**역할 쪽(루트)에 두는 것**: 네트워크·입력·이동·스킬 컨트롤러·생명주기·상태이상·HUD·HurtBox.
**캐릭터 쪽(Armature)에 두는 것**: 모델·리그·Animator·히트박스·무기 소켓.

### 1.2 코드에는 이미 있다 — `PlayableCharacterVisual` + `CharacterDefinition`

교체 메커니즘은 **이미 구현돼 있다.**

| 파일 | 역할 |
|------|------|
| `Assets/1.Scripts/Player/CharacterDefinition.cs` | 캐릭터 1종을 기술하는 SO — `visualPrefab` · `soulVisualPrefab` · `animatorController` · `defaultAttackData` + 기본 스탯 |
| `Assets/1.Scripts/Player/PlayableCharacterVisual.cs` | 루트에 붙어 교체를 수행한다. `[RequireComponent(PlayerMovement, DefaultAttackController)]` |

`PlayableCharacterVisual.ApplyCharacter(definition)` 가 하는 일:

1. `visualRoot`(= `transform.Find("Armature")`) **아래를 전부 지우고** `definition.VisualPrefab` 을 새로 `Instantiate`
2. 새 비주얼의 `Animator` 에 `definition.AnimatorController` 를 꽂는다
3. `PlayerAnimationEventRelay` · `PlayerRootMotionRelay` 를 **없으면 자동으로 붙인다**
4. `defaultAttack.SetAnimator(animator)` · `movement.SetArmature(animator.transform)` 로 역할 쪽 참조를 **재배선**
5. `defaultAttack.ApplyData(definition.DefaultAttackData)` 로 평타 데이터 교체
6. `CharacterApplied` 이벤트 발행 → `PlayerSoulController` 가 받아서 소울 비주얼을 맞춘다

### 1.3 🔴 그런데 데이터가 없다 (2026-09-16 확인)

| 확인 항목 | 결과 |
|-----------|------|
| `PlayableCharacterVisual` 이 붙은 프리팹/씬 | **0개** (GUID `32f7d2df…` 전수 검색) |
| `CharacterDefinition` 에셋(.asset) | **0개** |
| 스폰 경로의 캐릭터 선택 | 미구현 — `NetworkLoadingFlowController.ResolvePlayerPrefabForClient()` 는 `// TODO: Replace this with the client character selection lookup` 하나에 `defaultPlayerPrefab` 을 그대로 반환 |
| `CharacterDefinition` 의 기본 스탯(`maxHp`·`attackDamage`·`moveSpeed`·`defense`) | **아무도 읽지 않는다.** `ApplyCharacter` 는 비주얼·애니메이터·평타 데이터만 적용한다. 스탯은 여전히 프리팹의 `Player` 컴포넌트에 직렬화돼 있다 |

⇒ **의도(to-be)는 코드에 있고, 현재 데이터(as-is)는 "캐릭터마다 프리팹 통째 복제"다.**
이 문서의 §2~§6 은 as-is 기록이고, 그 격차를 어떻게 메울지는 §8 에 있다.

### 1.4 Armature 교체 계약 — 바꿀 때 무엇이 걸리는가

`Player.prefab` 의 루트 컴포넌트가 Armature 내부를 가리키는 참조는 **정확히 아래가 전부**다
(중첩 프리팹 stripped 참조 전수 추출).

| 참조 | 자동 보정 | 비고 |
|------|:--------:|------|
| `Player.animator` · `DefaultAttackController.animator` · `PlayerSkillController.animator` | ✅ | 세 곳 모두 `Awake` 에서 `GetComponentInChildren<Animator>()` (`Player.cs:168`, `DefaultAttackController.cs:128`, `PlayerSkillController.cs:56`) |
| `PlayerMovement.armature` | ✅ | `transform.Find("Armature")` (`PlayerMovement.cs:38`) — 🔴 **자식 이름이 정확히 `Armature` 여야 한다** |
| `PlayerLandingProtection.blinkVisualRoot` | ✅ | 비면 루트 전체에서 `Renderer` 를 긁는다 (`PlayerLandingProtection.cs:161`) |
| `PlayerSoulController.soulVisualRoot` | ✅ | `PlayableCharacterVisual.VisualRoot` → `transform.Find("Armature")` → `transform` 순 폴백 (`PlayerSoulController.cs:175`) |
| `DefaultAttackController.defaultHitbox` | ❌ | **수동 배선 필수.** 평타 판정 히트박스(`ColliderInfo`) |
| `FirstMeleeMainSkill.hitboxAnchor` · `FirstMeleeInterruptSkill.hitboxAnchor` | ❌ | **수동 배선 필수.** 비면 런타임 에러 (`FirstMeleeInterruptSkill.cs:87`) |
| `TrailTransform.weaponTrailEffect` (무기 FBX 안) | ❌ | 무기 궤적. Armature 의 `WeaponTrailEffect` 를 가리킨다 |
| `WeaponTransformRelay.weapon` (`PlayerWaeponSlot` 아래) | ❌ | 무기는 Armature 밖(`PlayerWaeponSlot`)에 있고 소켓만 Armature 안에 있다 |

🔴 **`transform.Find("Armature")` 에 의존하는 곳이 3군데 있다** — `PlayerMovement.cs:38`,
`PlayerSoulController.cs:175`, `PlayableCharacterVisual.cs:30`.
**Paladin 의 자식 이름은 `Paladin_Armature`** 라 이 폴백이 전부 불발된다(현재는 필드가 손으로 배선돼 있어
증상이 안 나올 뿐이다). 교체 구조로 정리할 때 **자식 이름을 `Armature` 로 통일**해야 한다.

### 1.5 🔴 NGO 제약 — "스폰 전 결정"이 강제하는 것

교체 방식을 고르기 전에 알아야 할 네트워크 사실 두 가지다. **둘 다 NGO 패키지 코드에서 확인했다.**

**(1) 클라이언트는 서버가 만든 오브젝트를 받는 게 아니라, 등록된 프리팹을 스스로 인스턴스화한다.**
`NetworkSpawnManager.cs:873-918` — 클라는 `globalObjectIdHash` 로 `NetworkPrefabOverrideLinks` 를 조회해
**자기 쪽 프리팹**을 `Instantiate` 한다.
⇒ **서버가 스폰 직전에 Armature 를 바꿔치기해도 클라에는 전달되지 않는다.** 서버만 다른 캐릭터를 보게 된다.

**(2) NetworkBehaviour 의 인덱스는 계층 순서로 매겨진다.**
`NetworkObject.cs:2751-2765` — `GetComponentsInChildren<NetworkBehaviour>(true)` 순회 순서가 곧
`NetworkBehaviourId` 이고, 이 id 로 RPC 와 NetworkVariable 이 라우팅된다.
⇒ **NetworkBehaviour 의 집합과 순서가 모든 피어에서 동일해야 한다.** 런타임에 자식을 갈아끼워
NetworkBehaviour 가 늘거나 줄거나 순서가 바뀌면 **RPC 가 엉뚱한 컴포넌트로 간다.**

**현재 Armature 안에 NetworkBehaviour 가 2개 있다** — `NetworkTransform`(회전 XYZ 전용, 위치·스케일 동기화
꺼짐, 서버 권위)과 `NetworkAnimator`(오너 권위). 이게 이 설계의 핵심 제약이다.

#### 그래서 길은 둘뿐이다

| | **(A) 캐릭터별 Player Variant 를 스폰 시 선택** | **(B) 단일 `Player.prefab` + 런타임 Armature 교체** |
|---|---|---|
| 방식 | 캐릭터마다 `Player.prefab` Variant 를 만들어 **각각 NetworkPrefab 으로 등록**하고, 유저 선택값으로 스폰할 프리팹을 고른다 | `Player.prefab` 하나만 스폰하고 `PlayableCharacterVisual.ApplyCharacter()` 로 Armature 를 갈아끼운다 |
| 붙일 자리 | `NetworkLoadingFlowController.ResolvePlayerPrefabForClient(clientId)` — **TODO 주석이 정확히 이 자리다** | 같은 자리에서 캐릭터 id 를 NetworkVariable 로 실어 보내고 각 피어가 `OnNetworkSpawn` 에서 적용 |
| Armature 안 NetworkBehaviour | **그대로 둬도 된다** | 🔴 **전부 들어내야 한다.** 회전 동기화는 루트로 옮기거나 NetworkVariable 로 대체해야 함 |
| NGO 지원 | 네이티브. `NetworkPrefab` 의 `SourcePrefabToOverride`/`OverridingTargetPrefab` 오버라이드 기능이 이 용도다 | 직접 구현 |
| 비용 | 캐릭터 수만큼 Variant 유지 (역할 부분은 base 에서 상속되므로 중복 아님) | 네트워크 재작업(회전 동기화·애니메이터 배선) |
| 평가 | ✅ **채택(2026-09-16).** "스폰 전에 결정된다"는 전제와 정확히 맞고, 네트워크 구조를 건드리지 않아 안정적이다 | ❌ 미채택. 인게임 교체가 필요할 때만 값어치가 있는데 그건 설계 범위 밖이고(§1.0), Armature 의 NetworkBehaviour 제거라는 네트워크 재작업이 따라온다 |

⚠️ (A) 로 가더라도 **`Player.prefab` 을 진짜 base 로 만드는 일**(§8)은 그대로 필요하다. 지금처럼 캐릭터마다
Variant 가 아니라 **통짜 복제본**을 만들면 §4 의 어긋남이 캐릭터 수만큼 늘어난다.

---

## 2. 프리팹들의 신원

| | `Player.prefab` | `Paladin.prefab` | `TempPlayer_Armature.prefab` |
|---|---|---|---|
| 경로 | `Assets/2.Prefabs/Player/Player.prefab` | `Assets/2.Prefabs/Player/Paladin/Paladin.prefab` | `Assets/2.Prefabs/Player/TempPlayer_Armature.prefab` |
| 성격 | **역할 셸** (의도한 형태) | **역할+캐릭터 통짜 복제본** | **캐릭터(Armature) 본체** |
| GUID | `55ee4e06e5b56ec48a45b3796040b9ae` | `af4a760f53d82b64f8369a09c962374c` | `8de5f51f34fe3cf4aab41eb2c59d402d` |
| 루트 fileID | `8559504096609571310` | `6077492126708577103` | — |
| 루트 오브젝트 이름 | `Player` | `Paladin` | `TempPlayer_Armature` |
| 루트 레이어 / 태그 | 6 (Player) / `Player` | 6 (Player) / `Player` | — |
| 모델 | `PlayerBaseModel.fbx` (Armature 경유) | `paladin 1.fbx` (평탄화) | `PlayerBaseModel.fbx` |
| Animator Controller | `4.Animations/Player/PlayerAnimatorController.controller` | **동일** | 동일 |
| 최초 커밋 | `d40f6bcb` (2026-06-09) | `36d7f9e6` (2026-07-06, "Add Garen player prefab setup") | — |
| 전환 커밋 | — | `5e773aea` (2026-07-30, "Paladin migration complete") | — |
| 네트워크 프리팹 목록 등록 | ✅ | ✅ | — |

둘 다 `DefaultNetworkPrefabs.asset` 에 등록돼 있다 — **"목록에 있다"는 "쓰인다"가 아니다.**
YAML 상 `Player.prefab` 은 `Paladin.prefab` 의 base 도 variant 도 아니다. 공유하는 것은 스크립트(.cs)와
AnimatorController, `PlayerDefaultAttackData.asset` 뿐이고 **프리팹 데이터는 100% 별개다.**

가붕이(근거리) 캐릭터의 현재 캐릭터 에셋이 `paladin 1.fbx` 다
([Docs/design/players.md](../design/players.md), [character_garen.md](../design/character/character_garen.md)).
원거리(징크스)용 Armature 는 아직 없다.

---

## 3. 누가 무엇을 스폰하는가 — 스폰 경로는 **두 개**다

헷갈림의 절반은 여기서 나온다. 플레이어를 띄우는 길이 둘이고, 둘은 서로 다른 필드를 본다.

**경로 A — 정식 흐름 (`NetworkLoadingFlowController`)**
`Assets/1.Scripts/Loading/NetworkLoadingFlowController.cs`
`defaultPlayerPrefab` 을 직접 `Instantiate` → `SpawnAsPlayerObject()`.
`NetworkManager.prefab` 의 `NetworkConfig.PlayerPrefab` 은 **비어 있다(`fileID: 0`)** — NGO 자동 스폰은 돌지 않는다.

**경로 B — 테스트 씬 (NGO 기본 동작)**
씬에 직접 박힌 `NetworkManager` 의 `NetworkConfig.PlayerPrefab` 을 NGO 가 접속 시 자동 스폰
(`AutoSpawnPlayerPrefabClientSide: 1`). 일부 씬은 `MonsterTestBootstrap.playerPrefab` 도 함께 들고 있다.

### 씬별 실제 값

| 씬 | 빌드 목록 | 스폰 경로 | 사용 프리팹 |
|----|:--------:|----------|------------|
| `MainFlow/0.BootStrapScene` (→ Title→Lobby→Loading→Map) | ✅ | A · `NetworkManager.prefab` 의 `defaultPlayerPrefab` (씬 오버라이드 없음) | **Paladin** |
| `Dev_Boot` | ✅ | A · 씬에서 `defaultPlayerPrefab` 오버라이드 | **Paladin** |
| `Debug/PlayerDashTest` | ✅ | B · `NetworkConfig.PlayerPrefab` 오버라이드 | **Paladin** |
| `BossScene` | ❌ | B · `NetworkConfig.PlayerPrefab` | **Paladin** |
| `MonsterScene` | ❌ | B · `NetworkConfig.PlayerPrefab` + `MonsterTestBootstrap.playerPrefab` | **Paladin** |
| `TrashMobScene` | ❌ | B · `NetworkConfig.PlayerPrefab` + `MonsterTestBootstrap.playerPrefab` | 🔴 **Player** (미갱신) |
| `Debug/PlayerScene` | ❌ | 씬에 인스턴스 직접 배치 | 🔴 **Player** (미갱신) |
| `MainFlow/4.MapScene` | ✅ | — (플레이어 인스턴스 없음. 런타임 스폰) | — |

`DevSceneBooter.playerPrefabOverride` 를 비워두면 `NetworkManager.prefab` 기본값(= Paladin)을 쓴다.
`flow.SetDefaultPlayerPrefab(null)` 은 무시되므로 **빈 오버라이드가 기본값을 지우지 않는다.**

---

## 4. 구조 차이 — 중첩 프리팹 vs 평탄화

**`Player.prefab` = 의도한 조립 (Armature 가 교체 가능한 중첩 프리팹)**

```
Player  (루트 컴포넌트 37개)
├─ Armature            <중첩: 2.Prefabs/Player/TempPlayer_Armature.prefab>   ← 교체 지점
├─ CombatHUD           <중첩: 2.Prefabs/UI/CombatHUD.prefab>
├─ OverheadHealthBar   <중첩: 2.Prefabs/UI/OverheadHealthBar.prefab>
├─ CameraFollowTarget  <중첩: 2.Prefabs/Camera/CameraFollowTarget.prefab>
├─ PlayerWaeponSlot ── SM_Wep_Shield_01 / SM_Wep_Sword_03  <중첩: FBX>
├─ AimIndicator · SkillRangeIndicator · HurtBox
└─ Corpse ── CorpseVisualPlaceholder
```
평타/스킬 히트박스(`AAC1~4`, `MainSkill`, `InterruptAttack`)와 무기 소켓은 **`TempPlayer_Armature.prefab` 안에** 있다
— 즉 캐릭터 쪽에 올바르게 들어가 있다.

**`Paladin.prefab` = 거의 전부 평탄화(unpack) — 교체 지점이 사라진 상태**

```
Paladin  (루트 컴포넌트 37개 — Player 와 동일 구성)
├─ Paladin_Armature   (Animator · NetworkAnimator · NetworkTransform · PlayerAnimationEventRelay · WeaponTrailEffect ×2)
│  ├─ Player_Shild_Trail_Dark / Player_Sword_Trail_Blood  <중첩: 5.VFX/Player/*.prefab>
│  ├─ root ── (본 계층 전체가 이 프리팹 안에 직접 들어 있음)
│  ├─ tripo_part_0 (SkinnedMeshRenderer)
│  ├─ DefaultAttack ── AAC1~AAC4   (BoxCollider + ColliderInfo)
│  ├─ InterruptAttack · MainSkill  (BoxCollider + ColliderInfo)
│  └─ SubSkill · UltimateSkill
├─ PlayerWaeponSlot ── SM_Wep_Shield_01 / SM_Wep_Sword_03  <중첩: FBX>
├─ CameraFollowTarget         (평탄화됨 — 프리팹 링크 없음)
├─ AimIndicator · SkillRangeIndicator · HurtBox
├─ CombatHUD                  (평탄화됨 — 프리팹 링크 없음)
│  ├─ BossHealthHUD           <중첩: 2.Prefabs/UI/BossHealthHUD.prefab>
│  ├─ CombatPanel ── SkillBar(Slot_P/Q/E/RMB/R/Dash) · HpBar · ShieldBar · StatusEffects
│  └─ ProfilPanel ── Profile
├─ OverheadHealthBar          (평탄화됨 — 프리팹 링크 없음)
└─ Corpse ── CorpseVisualPlaceholder
```

### 🔴 여기서 나오는 결론

1. **Paladin 은 Armature 를 교체할 수 없다.** 캐릭터가 프리팹 본체에 녹아 있고, 자식 이름도
   `Paladin_Armature` 라 §1.4 의 이름 계약도 깨져 있다.
2. **`CombatHUD.prefab` 을 고쳐도 Paladin 의 HUD 는 안 바뀐다.** 링크가 끊긴 사본이고 이미 갈라져 있다 —
   `CombatHUD.prefab` 에는 `CombatPanel`·`ShieldBar`·`ProfilPanel` 이 **없고** Paladin 안의 사본에만 있다.
   `OverheadHealthBar.prefab`·`CameraFollowTarget.prefab` 도 마찬가지다.
3. **반대로 `TempPlayer_Armature.prefab` 수정은 `Player.prefab` 에만 가고 Paladin 에는 안 간다.**
   실제 사고: `4e3b292f`(NetworkTransform 서버 권위 전환)가 `Paladin.prefab`(루트+Armature)과
   `Player.prefab`(루트)만 고쳤다. Player 쪽 Armature NT 는 `TempPlayer_Armature.prefab` 안에 있어서
   **지금도 Owner 권한(`AuthorityMode: 1`)으로 남아 있다.**

---

## 5. 값 차이 — 확인된 것 전부

**루트 컴포넌트 목록은 양쪽이 완전히 동일하다 — 각 37개(Transform 포함), 빠진 것도 더 붙은 것도 없다.**
`PlayerSkillController`·`FirstMeleePassive`·`FirstMeleeInterruptSkill`·`AudioListener` 전부 양쪽에 있다.
"어느 한쪽에 컴포넌트가 없다"는 식의 옛 서술은 전부 무효다(§6). **차이는 전부 직렬화된 '값'에 있다.**

| 항목 | `Player.prefab` | `Paladin.prefab` | 메모 |
|------|-----------------|------------------|------|
| `Player.maxHp` | `50` | `9999` | 🟡 `0516f1e8` "시연용 밸런스 수정" 의 **시연용 값이 그대로 남아 있음** |
| `Player.attackDamage` | `5` | `33` | 🟡 위와 같은 커밋 |
| `Player.moveSpeed` / `defense` | 10 / 25 | 10 / 25 | 동일 |
| `PlayerDefaultAttack.targetLayer` / `DefaultAttackController.hittableLayers` | `256` | `17408` | ⚪ **둘 다 죽은 데이터다.** 양쪽 프리팹이 같은 `PlayerDefaultAttackData.asset` 을 물고 있고, `Awake` 의 `ApplyData()` 가 SO 값 **`17664`(Enemy+Projectile+EnemyHurtBox)** 로 덮어쓴다 (`DefaultAttackController.cs:123`, `PlayerDefaultAttack.Configure`). 🔴 **인스펙터에 보이는 값과 런타임 값이 다르다** — 이것 자체가 함정이다 |
| `DefaultAttackController.attackSteps` | `[]` | `[]` | SO 에서 주입된다. 스텝별 `Hitbox` 는 SO 에 없어서 `defaultHitbox` 로 폴백 |
| `Player.animator` · `DefaultAttackController.animator` · `PlayerSkillController.animator` | 배선됨 | **비어 있음** | ⚪ 버그 아님 — §1.4 자동 보정 |
| `PlayerLandingProtection.blinkVisualRoot` | 배선됨 | **비어 있음** | ⚪ 경미 — 비면 루트 전체 `Renderer` 를 긁어서 Paladin 은 **무기 메시까지 함께** 깜빡인다 |
| 루트 `NetworkTransform` | Server (`AuthorityMode: 0`) | Server (`0`) | `4e3b292f` |
| Armature `NetworkTransform` | 🔴 **Owner (`1`)** — `TempPlayer_Armature.prefab` 안 | Server (`0`) | `4e3b292f` 가 Player 계열 Armature 를 놓쳤다 |
| `Corpse` `NetworkTransform` | Server (`0`) | Server (`0`) | |
| `NetworkAnimator` | Owner (`1`) | Owner (`1`) | 동일 |
| `AudioListener` + `PlayerAudioListenerActivator` | ✅ | ✅ | PLAN.md 의 "Paladin 에는 둘 다 없다"는 **해소된 과거 상태** |
| `PlayerInput` | `m_Enabled: 0` · 같은 `.inputactions` | `m_Enabled: 0` | 양쪽 동일. CONTEXT.md 의 "Paladin 만 `m_Enabled: 1` 이라 되돌릴 것" 항목은 **해소됨** |
| `HurtBox` 자식 | ✅ L13 (PlayerHurtbox) | ✅ L13 | 동일 |

---

## 6. 기존 문서·주석 중 **틀린** 서술

| 위치 | 잘못된 서술 | 실제 | 상태 |
|------|------------|------|------|
| `Docs/tech/game-structure-uml.md` §요약 3 | "플레이어의 현재 기준 프리팹은 `Player.prefab`" | 🔴 현재 스폰되는 것은 Paladin 이다 (다만 **의도상 역할 프리팹은 Player 가 맞다** — §1) | 2026-09-16 취소선 + 정정 표기 완료 |
| `Docs/tech/game-structure-uml.md` §6.2 앞 | "`Paladin.prefab` 은 Hurtbox 와 `PlayerSkillController` 가 없다" | 🔴 둘 다 있다. 루트 컴포넌트 구성은 양쪽 **완전 동일** | 2026-09-16 취소선 + 정정 표기 완료 |
| `Assets/1.Scripts/Monster/MonsterTestBootstrap.cs:7` (주석) | "Player=NetworkAnimator NRE / Paladin=히트박스 없음" | 🔴 옛 브랜치 상태. Paladin 은 `AAC1~4`·`MainSkill`·`InterruptAttack` 을 모두 갖고 있다 | **미수정** — 코드 주석이라 손대지 않음 |
| `PLAN.md:2326` | "`Paladin.prefab` 에는 `AudioListener` 도 `PlayerAudioListenerActivator` 도 없다" | 🟡 그 뒤 수정됨 | 과거 기록이라 그대로 둠 |

---

## 7. 수정할 때의 규칙

**결정 트리**

1. **지금 게임 동작을 바꾼다**(전투·이동·네트워크·UI) → **`Paladin.prefab`** 을 고친다. 실제로 스폰되는 건 이쪽이다.
2. **캐릭터 고유 데이터**(모델·리그·Animator·히트박스·무기 소켓)를 바꾼다 → **Armature 쪽**이다.
   Paladin 은 평탄화돼 있어 `Paladin.prefab` 안에서 직접 고쳐야 한다.
3. **스크립트(.cs)만 고친다** → 프리팹 건드릴 필요 없음. 둘 다 같은 스크립트를 쓴다.
4. **새 컴포넌트를 플레이어에 붙인다** → 손으로 YAML 을 고치지 말고 **저작 툴**을 쓴다.
   툴이 `Assets/2.Prefabs/Player` 폴더의 플레이어 프리팹을 **양쪽 다** 순회한다:
   - `Tools/Player/Authoring/Repair PlayerEncounterLock Wiring` (`PlayerEncounterLockAuthoring.cs`, 멱등)
   - `Tools/Player/Authoring/Wire Interrupt Skill (단죄의 방패)` (`PlayerInterruptSkillAuthoring.cs`)
5. **`Player.prefab` 만 고쳤다** → 지금은 게임에 반영되지 않는다. 의도한 게 맞는지 다시 생각할 것.
6. **역할/캐릭터 경계를 새로 긋는다** → §1.4 계약을 먼저 읽고, §8 의 미결 사항과 함께 팀에 올린다.

**주의사항**

- 프리팹은 **GitHub 관리 대상**이다(아트는 SVN — [AGENTS.md](../../AGENTS.md) §3). 머지 충돌 시 GUID 파손
  위험이 있으니 프리팹 변경은 작은 PR 로 나눈다.
- 플레이어 계통은 **은희 담당 영역**이다. 동시 수정 전에 `CONTEXT.md` 작업 세션을 확인할 것.
- 프리팹의 네트워크 설정을 **코드 주석으로 믿지 마라.** YAML 을 직접 읽어라 (CONTEXT.md 2026-09-15 교훈).
- **인스펙터 값이 런타임 값이라고 믿지 마라.** 평타 레이어 마스크처럼 `Awake` 에서 SO 가 덮어쓰는 필드가 있다(§5).

---

## 8. 의도와 현재 데이터를 수렴시키기 — 남은 일

**정해진 것**(§1.0): 역할/캐릭터 분리 · 캐릭터는 **스폰 전에** 유저 선택값으로 결정 ·
방식은 **`Player.prefab` base + 캐릭터별 Prefab Variant**.

### 8.0 🔴 Variant 를 만들기 전에 풀어야 하는 것 — 캐릭터 고유 스킬

현재 **루트에 붙은 스킬 컴포넌트 5종이 전부 가붕이(근거리) 전용**이다 —
`FirstMeleeMainSkill`("Q 진격의 방패") · `FirstMeleeSubSkill` · `FirstMeleeUltimateSkill` ·
`FirstMeleeInterruptSkill`("단죄의 방패") · `FirstMeleePassive`.
징크스(원거리)는 좌클릭 스택·우클릭 스택 발동 등 **다른 스킬 세트**가 필요하다([players.md](../design/players.md)).

Prefab Variant 는 base 의 컴포넌트를 **추가**하긴 쉬워도 **제거**는 취약하다(`m_RemovedComponents`).
그러므로 base 경계를 먼저 정해야 한다:

- **base(`Player.prefab`)** = 캐릭터 무관 역할 — 네트워크·입력·이동·생명주기·상태이상·HurtBox·HUD·
  `PlayerSkillController`(슬롯 컨테이너)까지.
- **Variant** = 캐릭터 고유 — `Armature` 오버라이드 + **스킬 컴포넌트 5종** + 스탯 + 평타 데이터.

⚠️ 즉 **base 에서 `FirstMelee*` 5종을 걷어내는 작업**이 Variant 작업의 선행 조건이다. 이걸 안 하면
징크스 Variant 가 base 의 근거리 스킬을 떠안게 된다.

### 8.1 그다음 — `Player.prefab` 을 진짜 base 로 되돌리기

지금은 `Paladin.prefab` 이 통짜 복제본이라 base 가 없다. 순서대로:

1. **캐릭터 분리** — `Paladin.prefab` 에서 캐릭터 부분(리그·메시·히트박스·무기 소켓)을 뜯어
   **`Paladin_Armature.prefab`** 으로 만든다. 🔴 **루트 자식 이름은 `Armature`** 로 붙인다(§1.4 이름 계약).
2. **역할 쪽 되돌리기** — HUD 사본(`CombatHUD`·`OverheadHealthBar`·`CameraFollowTarget`)을 원본 프리팹 링크로
   복구한다. 갈라진 내용(`CombatPanel`·`ShieldBar`·`ProfilPanel`)은 **원본 프리팹 쪽으로 올린다.**
3. **수동 배선 4종 재연결**(§1.4) — `defaultHitbox` · `FirstMeleeMainSkill.hitboxAnchor` ·
   `FirstMeleeInterruptSkill.hitboxAnchor` · 무기 트레일.
4. **네트워크 정리** — `TempPlayer_Armature.prefab` 의 Armature NT 를 **Server 권위로**(`4e3b292f` 가 놓친 것, §5).
5. **데이터 정리** — 시연용 값(`maxHp 9999` / `attackDamage 33`) 정리. 스탯의 소유자를 정한다 —
   프리팹의 `Player` 컴포넌트인가, `CharacterDefinition` 인가(현재 후자는 **아무도 안 읽는다**, §1.3).
6. **씬 정리** — `TrashMobScene` · `Debug/PlayerScene` 이 아직 구 `Player.prefab` 인스턴스를 쓴다.

### 8.2 Variant 만들기

- 캐릭터별 **`Player.prefab` Variant** 생성 — `Player_Paladin.prefab` 등. Variant 가 오버라이드하는 것은
  §8.0 의 경계대로 **Armature · 스킬 5종 · 스탯 · 평타 데이터**.
- 각 Variant 를 **NetworkPrefab 으로 등록**(`DefaultNetworkPrefabs.asset`).
  구 `Paladin.prefab` 은 목록에서 **뺀다**(§3 의 씬들을 먼저 옮긴 뒤).
- `PlayableCharacterVisual` / `CharacterDefinition` 은 **이 경로에서는 쓰지 않는다.**
  남겨둘지 지울지 결정할 것 — 남기면 "쓰이지 않는 교체 경로"가 또 하나의 혼동 원인이 된다.

### 8.3 선택값을 스폰까지 나르기

🔴 **현재 로비에 캐릭터 선택 UI 가 없다** — `LobbyPlayerSlotView` 는 연결·준비 상태만 다룬다.
이 경로는 **처음부터 만들어야 한다.**

- 로비에서 클라가 고른 캐릭터 id 를 **서버로 보내고**, 서버가 clientId → 캐릭터 id 로 들고 있는다.
- `NetworkLoadingFlowController.ResolvePlayerPrefabForClient(clientId)` 의 TODO 를 구현 —
  캐릭터 id → Variant 프리팹 매핑. **이 함수 하나가 (A) 방식의 유일한 분기점이다.**
- 미선택·잘못된 id 의 폴백을 정한다(현재 `defaultPlayerPrefab` 이 그 자리를 대신하고 있다).

### 8.4 계획서

위 작업의 단계·검증·리스크는 **[PLAN-player-variants.md](../../PLAN-player-variants.md)** 에 있다(승인 대기).
이번 1차 범위는 **`Player_Paladin` Variant 까지**이고, 로비 선택 UI 와 징크스는 범위 밖이다.

**진행되면 §0 · §1.0 · §1.3 · §3 · §8 을 갱신할 것.**

---

## 9. 재확인 명령 (이 문서를 의심할 때)

```bash
# 어떤 씬이 어느 프리팹을 가리키는가 (Player GUID / Paladin GUID)
grep -rl "55ee4e06e5b56ec48a45b3796040b9ae" Assets --include=*.unity --include=*.asset --include=*.prefab
grep -rl "af4a760f53d82b64f8369a09c962374c" Assets --include=*.unity --include=*.asset --include=*.prefab
```

```bash
# 정식 흐름의 스폰 프리팹 (NetworkManager.prefab)
grep -n "PlayerPrefab\|defaultPlayerPrefab\|AutoSpawnPlayerPrefabClientSide" Assets/2.Prefabs/Network/NetworkManager.prefab
```

```bash
# 교체 구조가 아직도 미배선인지 (둘 다 결과 없음 = 미배선)
grep -rl "32f7d2dfdf2e4c52aa11d74c05bbf9f1" Assets --include=*.prefab --include=*.unity
grep -rl "CharacterDefinition" Assets --include=*.asset
```

```bash
# "Armature" 이름에 의존하는 코드
grep -rn '"Armature"' Assets/1.Scripts --include=*.cs
```

```bash
# NetworkTransform / NetworkAnimator 권한 (0=Server, 1=Owner)
grep -n "AuthorityMode" Assets/2.Prefabs/Player/Player.prefab Assets/2.Prefabs/Player/Paladin/Paladin.prefab Assets/2.Prefabs/Player/TempPlayer_Armature.prefab
```

---

## 관련 문서

- [game-structure-uml.md](game-structure-uml.md) — 전체 구조 (§6 의 정정 사항 주의)
- [layer-standard.md](layer-standard.md) — 레이어 번호와 공격 마스크 표준
- [networking.md](networking.md) · [architecture.md](architecture.md) — 권한 모델
- [Docs/design/players.md](../design/players.md) — 가붕이/징크스 설계
- [PLAN-player-motor.md](../../PLAN-player-motor.md) — 이동/NetworkTransform 권한 작업
