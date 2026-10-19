# 플레이어 프리팹 — `Player.prefab`(base) / `Player_Paladin.prefab`(Variant) / `Armature`

> **목적.** 이 프리팹들은 이름도 내용도 비슷해서 AI 에이전트와 사람이 반복해서 헷갈린다.
> "무엇이 무슨 역할이냐", "지금 실제로 스폰되는 건 뭐냐", "어느 쪽을 고쳐야 하냐"의 **단일 사실 원본**이다.
> 🟢 **§0 은 2026-09-29 `fix/Player` 의 base + Variant 전환 후 사실**이다([PLAN-player-variants.md](../history/PLANS/PLAN-player-variants.md)).
> 🟡 **§2~§6 은 전환 이전(통짜 복제본 시절, 2026-09-16) 기록**이다 — 사고 원인·함정 설명으로만 읽을 것.
> 코드 주석·옛 문서를 믿지 말고 §9 의 확인 명령으로 다시 검증할 것.

---

## 0. 30초 요약 (2026-09-29 이후)

**구조.** `Player.prefab` = 캐릭터 무관한 **역할 base**. 캐릭터마다 그 **Prefab Variant** 를 두고,
Variant 가 `Armature`(캐릭터 몸체) · 스킬 · VFX · 무기 · 스탯 · 평타 데이터를 얹는다.
**캐릭터는 스폰 전에 고른다** — 스폰할 Variant 를 고르는 방식이다(런타임 Armature 교체는 미채택, §1.5).

```
Player.prefab (base)            ← 네트워크·입력·이동·생명주기·상태이상·HUD·HurtBox·실루엣 태그·시각 보간
│                                  🔴 기본 공격 없음(2026-09-30) — 캐릭터가 IPlayerBasicAttack 구현을 얹는다
├─ Player_Paladin.prefab (Variant)
│  ├─ Armature  = <중첩 Paladin/Paladin_Armature.prefab>  ← 모델·리그·Animator(4.Animations/Player/Garen/GarenAnimatorController — Idle/Walk = Garen_Idle·Garen_Walk.anim)·히트박스·VFX 소켓
│  ├─ PlayerWaeponSlot/SM_Wep_Shield_01 · SM_Wep_Sword_03
│  ├─ VFX  (EffectSocketPlayer 들)
│  └─ 루트 추가: PlayerDefaultAttack·DefaultAttackController(콤보) · FirstMelee* 스킬 5종 · PlayerSkillVfx · PlayerShieldVfx · EffectAnimEvents
└─ Player_Gunner.prefab (Variant, 2026-09-30~10-01 — character_gunner.md / PLAN-gunner.md)
   ├─ Armature  = <중첩 Gunner/Gunner_Armature.prefab>    ← gunner.fbx(Generic) · Animator(GunnerAnimatorController) · rig/artillery/LaserGun(오프셋 0 — Mount 회전만 상쇄) · InterruptAttack 앵커
   └─ 루트 추가: GunnerHeat · GunnerBeamAttack · GunnerBasicAttack(연사) · GunnerHeatHUD(임시) · GunnerBeamView ·
                 GunnerChargeLaserSkill(Q) · GunnerCoolBackstepSkill(E) · GunnerInterruptSkill(우클릭) · GunnerTrackingLaserSkill(R)
   (+ 별도 네트워크 프리팹 Gunner/GunnerTrackingLaser.prefab — R 이 서버에서 스폰)
```

**거너 조립은 손으로 하지 말고 메뉴로** — `Tools/Player/Gunner/` 아래 단계별 메뉴(`Player/Editor/GunnerShellAuthoring.cs`)가
프리팹·데이터 SO·슬롯 배선·애니메이터를 만든다. 재실행해도 이미 있는 것은 건너뛴다(덮어쓰지 않음).

| 질문 | 답 |
|------|-----|
| 지금 스폰되는 건? | **`Player_Paladin`** — 정식 흐름·테스트 씬 전부. 로그 이름 `Player_Paladin(Clone)`. 거너는 **Dev Boot 개인 선택**(메인 툴바 Dev Boot 캐릭터 드롭다운 `캐릭터: …` — `CharacterRoster` 목록, EditorPrefs)으로만 스폰된다 |
| 전투·이동·UI 등 **역할** 동작을 바꾸려면? | **`Player.prefab`**(base). Variant 가 상속한다 |
| 가붕이 **고유**(스킬·VFX·모델·히트박스·스탯)를 바꾸려면? | **`Player_Paladin.prefab`**, 몸체는 **`Paladin_Armature.prefab`** |
| 새 캐릭터를 추가하려면? | base 의 Variant 를 새로 만들고 `DefaultNetworkPrefabs` 에 등록. 🔴 **`GlobalObjectIdHash` 가 YAML 에 기록됐는지 확인**(아래) |
| 새 캐릭터 패시브는? | Variant 루트에 **`IPlayerPassive`** 구현 컴포넌트를 붙인다(HUD 가 이것만 본다). 적중·피격 반응은 `Player.ServerAttackLanded` / `ServerAttackReceived` 구독 — base 는 구체 패시브를 모른다([PLAN-passive-onhit.md](../history/PLANS/PLAN-passive-onhit.md)) |
| 자식 이름 규칙 | 몸체 인스턴스 이름은 반드시 **`Armature`** (`transform.Find("Armature")` 폴백 3곳, §1.4) |
| 구 프리팹은? | `Paladin.prefab` · `TempPlayer_Armature.prefab` → **`Player/Legacy/`** 보관. `Paladin_VFX.prefab` 은 **원래 위치(`Player/Paladin/`)에 유지**(2026-09-29 사용자 결정). 셋 다 **스폰 대상이 아니다** — 고쳐도 게임에 반영되지 않는다. (NGO 자동 생성으로 `DefaultNetworkPrefabs` 에는 **등록돼 있다** — 아래 행) |
| 네트워크 목록 | `DefaultNetworkPrefabs` 는 **NGO 가 자동 생성**한다(`ProjectSettings/NetcodeForGameObjects.asset` `GenerateDefaultNetworkPrefabs: 1`) — NetworkObject 가 붙은 프리팹은 base·Legacy 포함 **전부 다시 들어온다**. 손으로 빼지 말 것. 스폰되지 않는 프리팹이 목록에 있는 건 무해(해시가 전부 다르다) |
| 캐릭터 선택 UI / `ResolvePlayerPrefabForClient` | ✅ 2026-10-02 구현(§8.3). 로비 `Panel_CharacterSelect` → `CharacterRoster` id → Variant. 실패 시 `defaultPlayerPrefab` |

🔴 **Variant 해시 함정.** `SaveAsPrefabAsset` 만으로는 Variant 의 `NetworkObject.GlobalObjectIdHash` 오버라이드가
YAML 에 **안 써져 base 해시를 상속**한다(에디터 메모리 값만 고유 → 빌드에서 충돌). 에셋의 NetworkObject 를
`SetDirty` → `SaveAssetIfDirty` 해서 기록할 것. `Player_Paladin` = `913233600`, `Player_Gunner` = `816596077`,
`GunnerTrackingLaser` = `4090968237`, base = `1250559839`.

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
| 스폰 경로의 캐릭터 선택 | ✅ `ResolvePlayerPrefabForClient()` = `ServerCharacterSelectionStore` + `CharacterRoster`(§8.3) |
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
꺼짐)과 `NetworkAnimator`. 권한 값은 브랜치 정책에 따라 바뀌지만(§5), **개수와 위치가 제약의 본질**이다.

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

> 🟡 **여기서부터 §6 까지는 2026-09-16 전환 이전 기록이다.** 현재 구조는 §0, 수정 규칙은 §7.

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
| Animator Controller | `4.Animations/Player/PlayerAnimatorController.controller`(당시 경로. 지금 팔라딘은 `4.Animations/Player/Garen/GarenAnimatorController.controller` — guid 유지 이동, §0) | **동일** | 동일 |
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
   **한동안 혼자 다른 값이었다**(이후 오너 권위 전환으로 우연히 일치, §5).
   같은 이유로 **`9e3afee9`(시각 보간 컴포넌트)는 지금 Paladin 루트에만 붙어 있다.**

---

## 5. 값 차이 — 확인된 것 전부

**루트 컴포넌트는 `PlayerVisualReconciliationSmoother` 하나만 빼고 동일하다** — Player 37개 / Paladin 38개
(Transform 포함). 이 하나도 캐릭터 고유가 아니라 **역할 쪽**이라, 원래는 양쪽에 있어야 한다.
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
| 루트 `NetworkTransform` | **Owner (`AuthorityMode: 1`)** | Owner (`1`) | ✅ 양쪽 동일 |
| Armature `NetworkTransform` | Owner (`1`) — `TempPlayer_Armature.prefab` 안 | Owner (`1`) | ✅ 양쪽 동일 |
| `Corpse` `NetworkTransform` | Server (`0`) | Server (`0`) | 동일 |
| `NetworkAnimator` | Owner (`1`) | Owner (`1`) | 동일 |
| `PlayerVisualReconciliationSmoother` | ❌ **없음** | ✅ 있음 | 🔴 `9e3afee9` 가 Paladin 루트에만 붙였다. **역할 쪽 컴포넌트**라 base 로 가야 한다 |
| `AudioListener` + `PlayerAudioListenerActivator` | ✅ | ✅ | PLAN.md 의 "Paladin 에는 둘 다 없다"는 **해소된 과거 상태** |
| `PlayerInput` | `m_Enabled: 0` · 같은 `.inputactions` | `m_Enabled: 0` | 양쪽 동일. CONTEXT.md 의 "Paladin 만 `m_Enabled: 1` 이라 되돌릴 것" 항목은 **해소됨** |
| `HurtBox` 자식 | ✅ L13 (PlayerHurtbox) | ✅ L13 | 동일 |

> 🔴 **권한 값은 브랜치마다 다르다.** 위 표는 `feature/player-motor-owner-auth`(지스타까지의 개발 브랜치)
> 기준이다. `feature/player-motor-server-auth` 에서는 루트·Armature 가 Server(`0`) 다.
> 전환 스위치는 `Player.ServerAuthoritativeMovement` + 프리팹 `AuthorityMode` 두 개다
> ([PLAN-player-motor.md](../../PLAN-player-motor.md) "결정 — 지스타까지는 오너 권위").
> **프리팹을 복제·분리할 때 이 값을 하드코딩된 상수로 취급하지 말 것.**

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

**결정 트리 (2026-09-29 이후)**

1. **역할 동작**(이동·네트워크·생명주기·상태이상·HUD·실루엣 등 캐릭터 무관)을 바꾼다 → **`Player.prefab`**(base).
   모든 캐릭터 Variant 가 상속한다. 🔴 Variant 에서 같은 필드를 오버라이드해 두면 base 수정이 가려진다 — 인스펙터의 굵은 글씨 확인.
2. **캐릭터 고유**(스킬 5종·스킬 VFX·무기·스탯·평타 데이터)를 바꾼다 → **`Player_Paladin.prefab`**(Variant).
3. **캐릭터 몸체**(모델·리그·Animator·히트박스·VFX 소켓 위치)를 바꾼다 → **`Paladin/Paladin_Armature.prefab`**.
4. **스크립트(.cs)만 고친다** → 프리팹 건드릴 필요 없음.
5. **새 컴포넌트를 플레이어에 붙인다** → 역할이면 base, 캐릭터 고유면 Variant. 손으로 YAML 을 고치지 말고 **저작 툴**을 쓴다:
   - `Tools/Player/Authoring/Repair PlayerEncounterLock Wiring` (`PlayerEncounterLockAuthoring.cs`, 멱등 — `Player/` 폴더 순회, `Legacy/` 제외)
   - `Tools/Player/Authoring/Wire Interrupt Skill (단죄의 방패)` (`PlayerInterruptSkillAuthoring.cs` — `Paladin_Armature` + `Player_Paladin`)
   - `Tools/Rendering/Look/Wire Player Silhouette` (`PlayerSilhouetteAuthoring.cs` — base)
5-1. **캐릭터·무기 머티리얼**(2026-10-02 Flat Kit 전환 · 10-17 어쌔신 추가) → 셰이더·외곽선·셀 수치는 공통 부모 **`3.Materials/FlatKit/Player/FK_Paladin_Toon.mat`**(가붕이 몸)에서만 고친다.
   캐릭터·무기마다 그 **Material Variant** 를 두고 **Base Map(·틴트)만** 덮는다: `FK_Paladin_Sword/Shield_Toon` · `Gunner/FK_Gunner_Body` · `Gunner/FK_LaserGun` · `Assassin/FK_Assassin_Body`(머리·몸·단검 L/R 공용, §14).
   새 캐릭터 = Variant 하나 추가. 정리 툴 `Tools/Rendering/Flat Kit/플레이어 Variant 구성 (가붕이 부모)`(`FlatKitPlayerVariants.cs`, 재실행 안전 — 🔴 Variant 의 Base Map·틴트 외 오버라이드를 전부 되돌린다).
   ToonLit Variant(`3.Materials/Toon/`, 부모 `PlayableCharacter_Toon`)는 되돌리기용으로만 남아 있다(미사용) — 이전 계획 [PLAN-playable-toon-material.md](../history/PLANS/PLAN-playable-toon-material.md) · [PLAN-flatkit.md](../../PLAN-flatkit.md).
5-2. **캐릭터 고유 HUD**(2026-10-06, [PLAN-assassin.md](../../PLAN-assassin.md) A5) → 공용 `CombatHUD` 에 넣지 않는다. 캐릭터 전용 UI 프리팹을
   **`<Char>_Armature/HUD`** 아래에 중첩한다(자체 Canvas, 오너만 표시). 유령 상태에서 Armature 가 꺼지면 함께 숨는다.
   예: 거너 과열 게이지 = `Gunner_Armature/HUD/GunnerHeatGauge`(`Tools/Player/Gunner/과열 게이지 프리팹 (임시 UI)`, 재실행 안전).
   P 칸 툴팁 출처는 `IPlayerPassive`(툴팁 구현 시) → 없으면 하위의 `IPassiveTooltipProvider` — `PassiveHUD` 는 캐릭터 타입을 모른다.
6. **`Legacy/` 의 프리팹이나 `Paladin/Paladin_VFX.prefab` 을 고쳤다** → 게임에 반영되지 않는다. 어떤 씬·스폰 경로도 가리키지 않는다(`DefaultNetworkPrefabs` 자동 등록은 스폰과 무관).
7. **역할/캐릭터 경계를 새로 긋는다** → §1.4 계약을 먼저 읽고 팀에 올린다.

**주의사항**

- 프리팹은 **GitHub 관리 대상**이다(아트는 SVN — [AGENTS.md](../../AGENTS.md) §3). 머지 충돌 시 GUID 파손
  위험이 있으니 프리팹 변경은 작은 PR 로 나눈다.
- 플레이어 계통은 **은희 담당 영역**이다. 동시 수정 전에 `CONTEXT.md` 작업 세션을 확인할 것.
- 프리팹의 네트워크 설정을 **코드 주석으로 믿지 마라.** YAML 을 직접 읽어라 (CONTEXT.md 2026-09-15 교훈).
- **인스펙터 값이 런타임 값이라고 믿지 마라.** 평타 레이어 마스크처럼 `Awake` 에서 SO 가 덮어쓰는 필드가 있다(§5).

---

## 8. 의도와 현재 데이터를 수렴시키기 — 남은 일

> ✅ **8.0~8.2 완료(2026-09-29, `fix/Player`)** — base 정리·`Paladin_Armature` 추출·`Player_Paladin` Variant·스폰 전환·
> 구 프리팹 `Legacy/` 이동. 경과와 판단은 [PLAN-player-variants.md](../history/PLANS/PLAN-player-variants.md) §3.
> **남은 것 = 8.3(캐릭터 선택 경로)** 과 스탯 소유자·`PlayableCharacterVisual`/`CharacterDefinition` 존폐 결정.
> 아래 8.0~8.2 본문은 계획 당시 기록이다.

**정해진 것**(§1.0): 역할/캐릭터 분리 · 캐릭터는 **스폰 전에** 유저 선택값으로 결정 ·
방식은 **`Player.prefab` base + 캐릭터별 Prefab Variant**.

### 8.0 🔴 Variant 를 만들기 전에 풀어야 하는 것 — 캐릭터 고유 스킬

현재 **루트에 붙은 스킬 컴포넌트 5종이 전부 가붕이(근거리) 전용**이다 —
`FirstMeleeMainSkill`("Q 진격의 방패") · `FirstMeleeSubSkill`("E 수호자의 의지" — 전방 방어 → 자동 반격, [PLAN-paladin-e-counter.md](../history/PLANS/PLAN-paladin-e-counter.md).
타이밍은 초 값이 아니라 **클립 이벤트** — 애니메이터 상태 `SubSkillGuard`(방어 클립 `Custom0` 창 열림 · `Custom1` 닫힘)와
`SubSkillCounter`(반격 클립 `Hit` 판정 · `End` 종료, 방어 성공 시 서버가 전 피어 CrossFade). 초 값은 안전망 `maxActiveDuration` 만) · `FirstMeleeUltimateSkill` ·
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
4. **네트워크 값 확인** — `AuthorityMode` 를 **브랜치 정책에 맞춘다**(현재 owner-auth: 루트·Armature = Owner,
   Corpse = Server). Variant 가 이 값을 오버라이드하지 않고 **base 에서 상속**하게 두면, 지스타 이후
   서버 권위로 되돌릴 때 base 한 곳만 고치면 된다. 🔴 상수로 취급하지 말 것(§5).
   `PlayerVisualReconciliationSmoother`(`9e3afee9`)도 base 로 올린다 — 역할 쪽 컴포넌트다.
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

✅ **2026-10-02 구현** ([PLAN-character-select.md](../../PLAN-character-select.md)). 아래는 결과 요약.

- 목록 = `Assets/9.ScriptableObject/Player/CharacterRoster.asset` (배열 인덱스 = 캐릭터 id, 비활성 칸은 선택 불가). 로비 `LobbyUIController` 와 `NetworkManager.prefab` 의 `NetworkLoadingFlowController` 가 같은 에셋을 참조.
- 로비: 클라 → 서버 `Lobby.CharacterRequest`, 서버가 검증(사용 가능·Ready 아님) 후 `Lobby.State` 에 클라별 id 를 실어 방송. 기본 0번, 중복 선택 허용.
- Lobby 씬은 맵 로딩 때 언로드되므로 서버 맵은 static `ServerCharacterSelectionStore` 에 둔다(세션 시작·종료 시 비움, 퇴장 시 제거).
- `NetworkLoadingFlowController.ResolvePlayerPrefabForClient(clientId)` = 캐릭터 id → Variant. **이 함수 하나가 (A) 방식의 유일한 분기점이다.**
  미선택·잘못된 id·비활성 칸 → `defaultPlayerPrefab`(Paladin). Dev Boot 는 로비를 안 거치므로 이 폴백으로 지금처럼 동작.
- 범위 밖으로 남긴 것: 전투 HUD 초상화(`CombatHUD.prefab` 에 Paladin 고정 — Gunner 도 Paladin 얼굴).

### 8.4 계획서

위 작업의 단계·검증·리스크는 **[PLAN-player-variants.md](../history/PLANS/PLAN-player-variants.md)** 에 있다(2026-09-29 완료).
1차 범위는 **`Player_Paladin` Variant 까지**였고, 로비 선택 UI 와 징크스는 범위 밖이다.

---

## 9. 재확인 명령 (이 문서를 의심할 때)

```bash
# 어떤 씬이 어느 프리팹을 가리키는가
# Player_Paladin (현행) — 씬 7개 + NetworkManager.prefab + DefaultNetworkPrefabs 가 나와야 한다
# 🔴 아래 두 검색은 DefaultNetworkPrefabs.asset 을 뺀다 — NGO 자동 생성이 NetworkObject 프리팹을 전부 등록하므로 거기엔 늘 나온다
grep -rl "df27ec97b40aab24da03bb1fb7cb43ce" Assets --include=*.unity --include=*.asset --include=*.prefab
# 구 Paladin / Paladin_VFX / TempPlayer_Armature 를 참조하는 곳 — 결과가 나오면 안 된다 (프리팹 자신은 guid 를 .meta 에만 갖는다)
grep -rl "af4a760f53d82b64f8369a09c962374c\|8d5b48551745dae429fc070cfa0af71f\|8de5f51f34fe3cf4aab41eb2c59d402d" Assets --include=*.unity --include=*.asset --include=*.prefab | grep -v DefaultNetworkPrefabs
# base Player — Player_Paladin(Variant 의 부모)에서만 나와야 한다
grep -rl "55ee4e06e5b56ec48a45b3796040b9ae" Assets --include=*.unity --include=*.asset --include=*.prefab | grep -v DefaultNetworkPrefabs
```

```bash
# Variant 가 고유 해시를 YAML 에 갖고 있는가 (없으면 base 해시 상속 = 충돌)
grep -n -A1 "propertyPath: GlobalObjectIdHash" Assets/2.Prefabs/Player/Paladin/Player_Paladin.prefab
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
grep -n "AuthorityMode" Assets/2.Prefabs/Player/Player.prefab Assets/2.Prefabs/Player/Paladin/Paladin_Armature.prefab Assets/2.Prefabs/Player/Paladin/Player_Paladin.prefab
```

---

## 10. 머리 위 대시 아이콘 · 에임 대시 틴트 (2026-10-12, 할 일 29)

**대시 아이콘** — 로컬(오너) 플레이어 머리 위 체력바 **오른쪽**에 대시 슬롯 아이콘을 띄운다. 준비/미준비 두 상태만(채움·숫자 없음).

| 항목 | 내용 |
|------|------|
| 위치 | `2.Prefabs/UI/OverheadHealthBar.prefab` → `Bar/DashIcon`(`Slot` = `Common_DashSlotIcon` 테두리, `Icon` = `Common_DashIcon`, 같은 1254² 캔버스라 둘 다 꽉 채움). `Player.prefab` 은 이 프리팹을 중첩하므로 **모든 Variant 가 오버라이드 없이 상속**한다 |
| 배치값 | 앵커 = Bar 오른쪽 가운데, 피벗 (1, 0.5), 위치 (40, -2), 크기 40×40, 스케일 1.05(아이콘) — 2026-10-13 사용자 Play 검증에서 조정. 크기·간격은 `DashIcon` RectTransform 에서 조절 |
| 위치 | 처음엔 게이지 만충 표시(오른쪽 끝)를 피해 왼쪽(22×22)에 뒀으나, 사용자 Play 검증에서 오른쪽·40×40 으로 옮겼다(10-13). 옛 캐릭터 HUD(`GunnerHeatGauge`·`AssassinHUD`)는 비활성 |
| 표시 규칙 | `OverheadDashIconRules.ShouldShow` — **로컬 플레이어 + `PlayerLifeState.Alive`** 일 때만. 원격 플레이어 머리 위엔 없다(예측 충전은 오너만 진행). 사망 연출·Soul(대시 금지)·영구 사망(관전)은 숨긴다 — 체력바 자체는 이때도 보인다. 체력바가 숨으면 자식이라 같이 숨는다 |
| 준비 판정 | `PlayerDashController.IsReady`(예측 충전 > 0). 공급자 = `PlayerOverheadHealthBarRule`(`IOverheadDashIndicator`). 공급자가 없는 바(스피너봇 등)는 아이콘을 숨긴다 |
| 인스펙터 | `UnitOverheadHealthBar` 「대시 아이콘」: `dashIconRoot` · `dashIconGraphics`(색을 곱할 Slot·Icon) · `dashReadyColor`(흰색) · `dashNotReadyColor`(0.35 회색, 알파로 흐리게도 가능) |
| 테스트 | `UI/Combat/Editor/OverheadDashIconRulesTests` — `Tools/Tests/플레이어 EditMode 테스트 실행` 에 등록(UI 픽스처도 이 러너가 돌린다) |

**머리 위 라벨 = 이름 / 상태이상** (2026-10-16, 할 일 50) — `Bar/NameLabel` 은 평소 이름(P1·P2…), CC·붙잡힘 중에는 그 이름 하나로 바뀐다(규칙·우선순위: [status-effects.md](../design/status-effects.md) §머리 위 표시).
글자는 흰색 고정 + 검정 아웃라인 ≈3px(2026-10-16 후속) — 전역 폰트(TMP Settings·원본 `LiberationSans SDF`·`Resources/NotoSansKR … SDF`)는 그대로 두고 **이름표 전용 폰트 에셋**을 쓴다.

| 에셋 | 설정 |
|------|------|
| `Assets/Fonts/Overhead/LiberationSans SDF - Overhead.asset` | `NameLabel` 의 폰트. `TextMesh Pro/Fonts/LiberationSans.ttf` · 샘플 86 · 패딩 20 · 1024² · SDFAA_HINTED · **정적** · ASCII 32–126. fallback 표 = 아래 한글 에셋 하나 |
| `Assets/Fonts/Overhead/NotoSansKR SDF - Overhead.asset` | `Fonts/NotoSansKR-VariableFont_wght.ttf` · 샘플 28 · 패딩 6 · 2048² · SDFAA · **정적** · KS X 1001 완성형 2,350자(상태이상 문구 전부 포함) |
| `2.Prefabs/UI/LiberationSans SDF - Overhead Outline.mat` | `NameLabel` 의 머티리얼(guid 그대로). Mobile SDF·`OUTLINE_ON`, `_MainTex` = 위 라틴 아틀라스, Outline Width **0.35** · Face Dilate **0.5** |
| `OverheadHealthBar.prefab` → `Bar/NameLabel` | 18pt · **Character Spacing 20** (아래 「자간」) |

- **두께 맞춤**: 아웃라인은 아틀라스의 (패딩+1)/샘플 크기에 비례한다. 라틴 21/86 ≈ 한글 7/28 ≈ 0.25 로 맞춰 같은 머티리얼 값에서 같은 두께가 나온다.
  한글 글리프는 TMP 가 이 머티리얼을 복사한 fallback 머티리얼(`… Outline + NotoSansKR SDF - Overhead Atlas`)로 그려 아웃라인이 그대로 붙는다.
  예전 원본 LiberationSans SDF(패딩 9/샘플 86)로는 18pt 에서 바깥으로 ≈1.9px 까지만 닿아 3px 가 안 나왔다.
- **측정**(1080p, 렌더 커버리지 합): 18pt 기준 라틴 중앙값 2.9px · 한글 2.8px. 실제 게임은 바 스케일 0.9(`screenScale`)라 16.2px 글자에서 라틴 2.6 · 한글 2.9px.
  값을 더 올리면 라틴은 패딩 한계로 두께가 더 늘지 않고 한글 획만 깎인다.
- **자간 20**(2026-10-16 후속 2): TMP 는 글자마다 쿼드를 순서대로 그려서, **오른쪽 글자의 아웃라인이 왼쪽 글자의 흰 면을 덮는다**.
  라틴 글자 사이 원래 면 간격은 16.2px 에서 1–2px 뿐이라 ≈2.7px 아웃라인보다 좁다 — 자간 0 에서 `Stunned` 등이 글자당 10–16px² 씩 먹혀 겹쳐 보였다(한글은 간격이 넓어 0–2px²).
  폰트 메트릭 문제가 아니고(advance 가 원본 `LiberationSans SDF` 와 같다), Dilate 부풀림도 아니다(면 간격 변화 ≤1px).
  자간 20 에서 먹힘 ≈0 — 머티리얼·두께는 그대로다. 아웃라인을 두껍게 바꾸면 자간도 다시 맞춘다(10→84px², 15→8px², 18→1px², 20→0.1px², 영문 9개 합계).
- **정적 아틀라스**라 에디터·Play 에서 에셋이 다시 써지지 않는다(git 노이즈 없음). 2,350자 밖 글자(희귀 한글·한자 등 닉네임)는 TMP Settings 전역 fallback(`NotoSansKR … SDF`)로 그려진다 — 보이긴 하지만 아웃라인이 ≈1px 로 얇다.
- 2,350자 밖 글자는 전역 fallback 으로 외곽선 약 1px — 사용자 승인(2026-10-16: 정적 아틀라스 유지, 한글 전용 에셋 9.6MB 그대로).
  글자를 늘릴 땐 Font Asset Creator 에서 에셋에 저장된 생성 설정(문자 목록 포함)으로 다시 굽는다.

**에임 대시 틴트** — `PlayerAimIndicator.tintOnDashCooldown`(기본 **꺼짐**). 켜면 예전처럼 재충전 중 인디케이터를
`dashCooldownTint` 로 어둡게 한다(쿼드 머티리얼 **인스턴스**의 `_BaseColor` — §12). 꺼져 있으면 항상 원래 색이고, 어두운 상태에서 끄면 원래 색으로 되돌린다.
base·Variant 모두 이 값을 오버라이드하지 않는다(2026-10-12 확인) — 다시 켜려면 base `Player.prefab` 에서 켠다.
## 11. 회전 잠금 — `PlayerFacingLock` (할 일 27, 2026-10-12)

몸(`Armature`) 회전을 행동이 잠시 묶어 둘 때 쓰는 **원인별 토큰**이다. `PlayerMovement` 가 하나 들고 있다.

- `AcquireFacingLock(cause, direction)` — 그 방향을 보고 잠근다. `ReleaseFacingLock(cause)` — **그 원인만** 푼다.
  원인이 하나라도 남으면 잠겨 있다. 다른 기능(어쌔신 등)이 잠금을 써도 서로 풀지 않는다.
- 막는 것: 이동 입력 회전(시뮬레이션 입력 `CanRotate`)·`RotateToward`(추적 회전).
  **`RotateImmediately` 는 막지 않는다** — 대시 진입·낙하 복귀처럼 행동이 직접 내리는 회전이다.
  잠금을 건 행동이 끊기면 `PlayerAttackState.Exit` 가 다음 상태 `Enter` 보다 먼저 돌아 잠금이 먼저 풀린다.
- 현재 사용처: 거너 기본 공격(`GunnerBasicAttack`, 규칙은 `GunnerAttackFacingModel`). 2026-10-12 후속으로 **발 단위**로 좁혔다.
  - **토큰(공격 잠금)** = 공격 런타임 전체(`BeginRuntime` ~ `ResetRuntime`). 이동 입력 회전·`RotateToward` 를 공격 내내 막는다.
    해제 = 마무리 분기(마지막 발 + `ShotRecovery`) → `EndServer`/`EndRpc`, 또는 대시·넉백·구속·사망 등 상태 전이 → `CancelCurrentAttack`.
  - **발 잠금** = 각 발의 준비 시작 ~ 그 발 사격 끝. 이 동안은 커서로도 돌지 않는다.
    - 첫 발: 준비 시작 = 공격 시작 순간의 **조준**(커서로 즉시 돌아봄), 준비 동작(`WindupDuration`) 내내 잠김.
    - 이어지는 발(홀드·입력 창): 준비 동작이 없어 준비 시작 = 오너가 발사를 요청하는 순간. 그때의 **몸 방향**으로 잠근다 — 덜 돌았으면 덜 돈 방향.
    - 사격 끝 = 그 발이 이 피어에서 나간 때(`PlayShot`). 원격 오너는 왕복 지연만큼 잠긴다. `ShotRpc`(신뢰성 없음)가 빠지면 다음 발 시각에 풀린다.
  - **발 사이**(홀드 연사 간격·창으로 이은 다음 발 전·후딜) = 오너가 커서 쪽으로 `GunnerBasicAttackData.betweenShotTurnSpeed`(도/초)로 돈다
    (`TickOwner` → `RotateImmediately` — 토큰이 막지 않는 행동 회전). 0 = 사이에도 고정(이어지는 발이 전부 시작 방향).
  - 이동 입력은 공격 중 회전에 쓰이지 않는다 — Attack 상태는 `CanMovementRotate=false`(평타 중 이동 불가)이고 토큰도 막는다. 커서 회전만 있다.
  - ⚠️ 스턴(상태이상)은 상태 전이가 아니라 거너 평타를 끊지 않는다(기존 동작).
  - **네트워크**: 이어지는 발의 허락은 서버(눌림·창 예약·간격·과열), **요청 시점과 방향은 오너**다. 오너가 `FireShotRpc(direction)` 에
    잠근 몸 방향을 실어 보내고 서버는 그 방향으로 판정한다(몸 회전이 오너 권한이라 오너 몸이 기준). 서버는 눌림·예약이 없으면 요청을 버린다(창 우회 방지).
    창 예약을 받았는데 요청이 `FireInterval × 3` 안에 안 오면 예약을 버리고 마무리한다.
  - **원격 일치**: 오너 화면은 발 잠금이 `PlayShot` 까지라 몸=빔. 다른 피어는 몸(`Armature` NetworkTransform)과 빔(`ShotRpc`)이 같은 경로(오너→서버→피어)로
    오므로 같은 오너 시각을 보여 준다. 다만 이어지는 발은 발사 직전에 몸이 돌고 있었으므로, 보간 버퍼만큼(≈ 회전 속도 × 보간 지연) 몸이 늦게 따라와
    잠깐 어긋났다가 맞는다 — 빔은 서버 끝점을 매 프레임 다시 겨누므로(`GunnerBeamView.UpdateBeam`) 끝점은 정확하다.
- 테스트: `GunnerAttackFacingModelTests`(플레이어 EditMode 러너).

---

## 12. 에임 인디케이터 — 3D 쿼드 (2026-10-13, 할 일 33)

URP 데칼(`DecalProjector`)을 버리고 **바닥에서 띄운 얇은 쿼드**로 바꿨다. 데칼은 지형·벽에 투영돼 경사·단차에서 늘어지고
캐릭터 몸에도 찍혔다. 쿼드는 발밑 높이에 **수평으로 눕힌 채 조준 방향으로 Y축만** 돈다.

| 항목 | 내용 |
|------|------|
| 구조 | base `Player.prefab` → `AimIndicator`(게임오브젝트·Transform fileID 유지 — Variant 오버라이드 보존). 컴포넌트 = `MeshFilter`(내장 Quad) + `MeshRenderer`. **콜라이더 없음**. 같은 프리팹의 다른 데칼(`SkillRangeIndicator` 등)은 그대로 데칼 |
| 머티리얼 | `3.Materials/Player/AimIndicator/AimIndicator3D.mat` — URP `Lit`(2026-10-13 Unlit → Lit), Transparent·Alpha 블렌드, Receive Shadows·Specular Highlights·Environment Reflections 끔, Smoothness·Metallic 0(번들거림 방지), 텍스처 = 옛 데칼 머티리얼(`AimIndicator.mat`)의 화살표. 옛 `AimIndicator.mat`·`.shadergraph` 는 참조만 끊겼고 남아 있다 |
| 깊이·그림자 | ZTest LEqual(벽·지형에 **가려진다**), ZWrite Off, 렌더 큐 Transparent(3000), 그림자 캐스트·수신 끔, 라이트/반사 프로브 끔 |
| 높이 | 캐릭터 위치 0.5m 위에서 아래로 `groundMask` 레이(3.5m) → 맞으면 그 Y, 아니면 캐릭터 Y. 여기에 `heightOffset` 을 더한다. 조준점(마우스) 높이가 아니라 **발밑** 기준이라 계단·단차에서 파묻히지 않는다 |
| 회전·위치 | `AimIndicatorPlacement`(순수 계산) — yaw = `atan2(x, z)`, 회전 = `Euler(90, yaw + indicator_rot_offset(90), 0)`, 중심 = 발밑 + 조준 방향 × `arrowForwardOffset`. **월드 값을 매 프레임 `LateUpdate` 에서 다시 넣어** 부모(플레이어) 회전과 무관하다. 크기는 부모 스케일을 상쇄한다 |
| 인스펙터 (`PlayerAimIndicator`) | `heightOffset` 0.08m · `arrowScale` 1.2m(쿼드 한 변) · `arrowForwardOffset` 0.6m. 옛 데칼(2×2m, 중심 1m 앞 = 발밑~2m) 대비 **60%** — 발밑~1.2m |
| 오너 전용 | 기존 규칙 그대로 — 네트워크 중 비오너는 `OnNetworkSpawn` 에서 인디케이터를 끄고 `Update`·`LateUpdate` 를 건너뛴다 |
| 대시 틴트 | §10 — `MeshRenderer` 의 머티리얼을 `Awake` 에서 인스턴스로 복제해 `_BaseColor` 를 바꾼다(프리팹 애셋 오염 없음) |
| 저작 | `Tools/Player/Authoring/Aim Indicator 3D Quad`(`AimIndicatorAuthoring`, 멱등) — 머티리얼 생성/갱신(셰이더를 Lit 로 맞추고 인스펙터와 같은 `BaseShaderGUI.SetMaterialKeywords(…, LitGUI.SetMaterialKeywords)` 로 키워드 재설정) + 데칼 제거 + 쿼드 배선 |
| 테스트 | `Player/Editor/AimIndicatorPlacementTests` — `Tools/Tests/플레이어 EditMode 테스트 실행` 에 등록 |

**화살표 이미지 교체** — `3.Materials/Player/AimIndicator/AimIndicator3D.mat` 의 `_BaseMap`(인스펙터 **Base Map**) 슬롯 하나에 새 텍스처를 넣으면 끝이다. 런타임 코드는 텍스처·머티리얼 경로를 모르고 쿼드의 머티리얼만 본다. 저작 메뉴를 다시 돌려도 `_BaseMap` 이 차 있으면 덮어쓰지 않는다. 새 이미지도 화살표가 텍스처 **왼쪽(-U)** 을 가리키게 그릴 것(아니면 `indicator_rot_offset` 조정).

---

## 13. 어쌔신 머리카락 2차 모션 (2026-10-16, 할 일 47)

머리카락 본 28개(`Hair_Source00~06_01~04`, 7가닥 × 4마디, 전부 `Head` 아래)는 FBX 에 스킨돼 있지만 클립에 커브가 없어
굳어 있었다. 납품 스크립트를 그대로 들여와 `Assassin_Armature.prefab` 루트에 붙였다. **(10-16 머리카락 작업 기준) FBX·아바타·클립·Animator 컨트롤러·SVN 무수정.**
10-17 v18(§14)에서 FBX 임포트 배율 ×1.25(SVN)·아바타 재생성·`Run_Loop` 0.8 로 바뀌었고, 바인드 포즈를 ×1.25 길이 값으로 다시 캡처했다.

| 항목 | 내용 |
|------|------|
| 스크립트 | `1.Scripts/Player/Assassin/Hair/` — 납품 원본 그대로·`.meta` guid 유지: `AssassinHairMotion`(v13 물리 솔버) · `AssassinActionHairAnimation`+`AssassinActionHairProfiles`(v16 동작별 보정) · `AssassinDiveHairAnimation`(v15 회피). 우리 코드: `AssassinHairDriver`(구동기) · `AssassinHairClipRules`/`AssassinHairClipTracker`(순수 규칙) · `AssassinHairArmClearance`(팔 밀어내기 후처리) |
| Armature 구성 | 루트에 위 4 컴포넌트(`AssassinHairMotion`·`AssassinActionHairAnimation`·`AssassinDiveHairAnimation`·`AssassinHairDriver`). 솔버는 **에디터에서 바인드 포즈 캡처**(체인 7×4·몸 캡슐 8, `captured`) — 런타임 자동 캡처에 기대지 않는다 |
| 한 프레임 순서 | 구동기 `[DefaultExecutionOrder(21000)]`(SDArmTwist 20000·Animator 뒤). `Awake` 에서 v13 `externalDrive=true`, 보정 2종 `Capture`(첫 Animator 평가 전 원래 자세) → 매 프레임 `Update`: 보정 `RestoreBasePose` → (Animator·SDArmTwist) → `LateUpdate`: v13 `Advance` **1회** → 지금 클립에 맞는 `ApplyPose` → 팔 밀어내기 후처리. v13 을 한 프레임에 두 번 돌리지 않는다. 구동기만 꺼지면 v13 이 자기 LateUpdate 로 혼자 돈다 |
| 팔 밀어내기 후처리 | `LateUpdate` 마지막(v13 `Advance`·보정 `ApplyPose` 뒤) 1회. 팔 캡슐 4개(좌우 상완·전완)에 닿는 마디만 뿌리 쪽부터 **회전**시켜 캡슐 밖으로 민다(뿌리 고정·마디 길이 보존, 본 회전이 자식 마디를 데려가므로 다음 마디는 옮겨진 자리에서 판단). 캡슐·반경·`bindAllowance` 는 v13 이 가진 것을 이름으로 찾아 재사용. 가슴·허리 캡슐은 **가드**로만 쓴다 — 민 가닥이 그 프레임 원래 깊이보다 몸통으로 더 들어가지 않게 한 걸음을 줄인다(이미 겹친 몸통은 v13 몫, 건드리지 않음). v13 이 팔 접촉을 못 푸는 이유는 옆 가닥이 몸통만 보는 가상 중심 가닥에서 8°/12° 이상 못 벗어나기 때문이다(납품 원본은 무수정). 다음 프레임 v13 `Advance` 가 `CaptureTargets` 에서 바인드 회전으로 되돌린 뒤 자기 내부 점으로 다시 쓰므로 후처리 결과는 시뮬레이션 상태에 들어가지 않는다. 인스펙터: `armClearanceEnabled`(기본 켬) · `armClearanceMaxDegrees`(마디 하나를 더 꺾는 최대 각, 기본 45° — 진단상 필요 회전 22~56°, 45° 위로는 잔여 겹침이 거의 안 줄고 꺾임만 커진다). 판단은 `AssassinHairArmClearance`(순수 함수) |
| 클립 고르기 | **Base Layer** 현재 상태의 가장 무거운 클립(UpperBody 레이어는 안 본다). `Dodge_Combat_F_0` → v15, v16 프로필이 있는 클립 → v16, 그 외 → v13 만. 클립 안 초 = 정규화 시간 × 원본 길이(재생 속도 3.6배여도 위상 같음) |
| trailing 방향 | "출발 반대 방향"은 처음·상태/클립 전환·루프 한 바퀴·시간 되감기(Any State 재진입)·v13 리셋 때 `-Armature.forward` 로 다시 잡고, 그 사이엔 유지(납품 브리지 규칙 + 리셋) |
| 캡처 실패 | 경고 1줄 후 보정만 끄고 v13 은 계속 |
| 네트워크 | 순수 표현 — NetworkVariable·RPC·NetworkAnimator 변경 없음. 각 피어가 자기 Animator(NetworkAnimator 복제) 결과로 로컬 계산, 피어마다 미세하게 달라도 된다 |
| 튜닝 | 납품 기본값(v13 기본·보정 strength 1·회피 fan 22 — 납품 사용법 값). 🔸 길이 차원 값(`hairRadius`·캡슐 반경·끝 마디·`gravitySupport`·`maximumDynamicSpeed`·`neighbourOffsetLimit`)은 **납품값 × 리그 배율**(v18 = 1.25, §14). 그 밖의 값은 프리팹 인스펙터에서만 조정(길이 차원 값은 저작 메뉴가 매번 다시 쓰므로 `AssassinHairAuthoring` 의 `Delivered*` 상수에서 바꾼다), xlsx 노출 안 함. 구동기 인스펙터: `actionHairEnabled`·`diveHairEnabled`·`armClearanceEnabled`·`armClearanceMaxDegrees` |
| 저작 | `Tools/Player/Assassin/머리카락 본 연결`(`AssassinHairAuthoring`, 멱등 — 없으면 추가·있으면 재캡처, 기존 인스펙터 값 유지 — 단 위 길이 차원 값은 납품값 × 임포트 배율로 매번 다시 쓴다). 전체 저작(`AssassinShellAuthoring` Armature 단계)도 같은 `Ensure` 를 부른다 |
| 테스트 | `Player/Editor/AssassinHairClipRulesTests`(규칙) · `AssassinHairArmClearanceTests`(밀어내기·상한·길이 보존·몸통 가드) · `AssassinHairPrefabTests`(체인 7×4 본·캡처·캡슐·구동기·Variant 중첩·길이 값 = 납품값 × 임포트 배율·겹침 허용치 재계산 대조) — `Tools/Tests/플레이어 EditMode 테스트 실행` 에 등록 |

**리셋 경로** — 피어마다 로컬 시뮬레이션이라 서버/오너에서만 불리는 훅(`TeleportAuthoritative`·`PlaceOwnerAtSpawnClientRpc`·`PlayerFallRecovery`·`BossTeleportManager`)에 걸지 않는다. 별도 리셋 코드를 더하지 않았다(아래로 전부 덮임).

| 경로 | 덮는 것 |
|------|---------|
| 사망 → 유령 → 부활 | `PlayerSoulController` 가 alive visual(Armature)을 끄고 켠다 → v13 `OnEnable → ResetSimulation`(모든 피어 — 생명 상태 복제로 각자 켠다) |
| 스테이지 이동(보스룸·엘리베이터) | `NetworkTransform.Teleport` → 원격도 한 프레임에 스냅 → v13 Head 이동 거리 > `teleportDistance`(4m) 자동 리셋 |
| 낙하 복귀 · 스폰 배치 · 전멸 리스폰 | 오너: `TeleportAuthoritative` 스냅(1m 넘으면 시각 보간 안 함) → 4m 넘으면 자동 리셋, 그 아래는 빠른 이동으로 처리. 원격: `Teleport` 플래그 없이 복제돼 NT 보간(≤0.1초)으로 미끄러진다 → 리셋 없이 빠른 이동으로 처리. v13 은 마디 길이를 강제하고 루트 속도를 그대로 실어 날라(상대 속도만 2.5m/s 제한 — 납품 2 × 리그 배율 1.25) 늘어나거나 튀지 않는다(에디터 스모크: 15m/6프레임 슬라이드 길이 오차 0) |
| 프레임 끊김(> 0.5초) | v13 `deltaTime > 0.5` 리셋 |

## 14. 어쌔신 v18 비주얼 — Flat Kit 재질 · 키 ×1.25 (2026-10-17, PLAN-assassin §9)

계획 [PLAN-assassin.md](../../PLAN-assassin.md) §9. 가붕이·거너와 통일감을 맞추려고 형상 v18g + 텍스처 T3H 로 바꾸고 키를 1.252 → 약 1.565m 로 키웠다. **판정·스킬 수치·클립·네트워크는 그대로다.**

| 항목 | 내용 |
|------|------|
| 크기 | **FBX 임포터 Scale Factor(`Assassin.fbx.meta` 첫 `globalScale`) 1 → 1.25 만** 쓴다(SVN). 메시·본 **위치**에 구워지고 본 lossyScale 은 1 이다. humanScale 0.674 → 0.843(아바타 T-pose 도 ×1.25 로 다시 만들어짐) |
| 🔴 루트 배율 금지 | `Assassin_Armature` 루트에 배율을 걸지 않는다 — `DefaultAttack`·`InterruptAttack`(BoxCollider 판정)과 VFX 기준점이 루트의 자식이라 판정이 같이 커진다 |
| 단검 | 손 본에 붙은 단검 **루트** `m_LocalScale` 1 → 1.25(본 lossyScale 이 1 이라 직접 키운다). 쥐는 위치 오프셋은 루트 아래 자식에 있어 루트를 따라 커진다 — **곱하지 않는다**(곱하면 ×1.5625). 게임에서 쓰는 오프셋은 `Player_Assassin.prefab` 이 단검 자식에 덮어쓴 값이고 Armature 쪽 값은 가려져 있다. 저작 `AssassinShellAuthoring` 이 새로 붙이는 단검 = 임포트 배율(`AssassinHairAuthoring.ImportScale`) |
| 재질 | `3.Materials/FlatKit/Player/Assassin/FK_Assassin_Body.mat`(§7 5-1 Variant). 슬롯 5개 = `Assassin_HeadNeck` 0·1(Head·Face) · `Assassin_SourceBody` 0 · 단검 L/R 각 0 — Face 도 FK(셸 저작의 ToonLit Face 분기 제거). 단검도 어쌔신 아틀라스를 쓰고 단검 UV 가 얼굴·눈 섬과 겹친다(기존부터 — 전용 텍스처는 별도 아트 작업). 🔸 `Assassin.fbx.meta` 의 재질 remap 이 없는 guid 를 가리켜 FBX 원본 슬롯은 비어 있다 → 슬롯 오버라이드를 Revert 하면 분홍이 된다. ToonLit `3.Materials/Toon/Assassin_Toon.mat` 은 되돌리기용 |
| 조명 | Flat Kit 은 장면 조명을 그대로 받고 실시간 그림자를 받지 않는다(ToonLit 은 고정광 `_FIXEDLIGHT_ON`·그림자 0.35) — 가붕이·거너와 같다 |
| 머리카락 | §13 의 길이 차원 값 × 임포트 배율로 재캡처(`AssassinHairAuthoring.RigScale` = `ModelImporter.globalScale`, 리그 실측과 1% 넘게 다르면 에러): 캡슐 반경 · `hairRadius` 0.0225 · 끝 마디 0.1875 m · `gravitySupport` 0.8125 · `maximumDynamicSpeed` 2.5 · `neighbourOffsetLimit` 0.1125 · `bindAllowanceReference` 재계산. 각도·시간 상수(`armClearanceMaxDegrees` 47.7° 등)는 그대로 |
| 달리기 | `AssassinAnimatorController` `Run_Loop` 재생 속도 = **1 / 임포트 배율 = 0.8**(×1.25 로 클립 자연 속도 4.98 → 6.23 m/s, 재생 0.8 로 실제 4.98 m/s ≈ 이동 5 m/s). Start/Stop 은 1. 저작 `AssassinShellAuthoring.BuildAnimator` |
| SVN ↔ git | FBX·.meta·BaseColor 는 SVN, 나머지는 git — 둘은 짝이다. `Docs/tech/art-svn.json` 핀이 새 리비전을 가리킨다. 한쪽만 받으면 키·단검·머리카락·달리기 속도가 어긋난다 |
| 테스트 | `Player/Editor/AssassinVisualPrefabTests`(슬롯 5개·부모·BaseMap · 단검 루트 = 임포트 배율 · `Run_Loop` = 1 / 배율 · Armature 루트·판정 박스 배율 1) · `AssassinHairPrefabTests.SolverLengthsFollowRigScale`·`BindAllowancesMatchCapsuleRadii` — `Tools/Tests/플레이어 EditMode 테스트 실행` 에 등록 |
| 되돌리기 | `globalScale` 1 → `머리카락 본 연결` 재실행(배율 1 로 다시 캡처) · 단검 루트 1 · `Run_Loop` 1 · 재질 오버라이드 `Assassin_Toon` · `AssassinShellAuthoring.MaterialPath` · `FlatKitPlayerVariants` 의 어쌔신 블록(지우지 않으면 메뉴를 돌릴 때마다 슬롯이 FK 로 돌아간다) · 위 테스트 등록 해제 · 핀 |

---

## 관련 문서

- [game-structure-uml.md](game-structure-uml.md) — 전체 구조 (§6 의 정정 사항 주의)
- [layer-standard.md](layer-standard.md) — 레이어 번호와 공격 마스크 표준
- [networking.md](networking.md) · [architecture.md](architecture.md) — 권한 모델
- [Docs/design/players.md](../design/players.md) — 가붕이/징크스 설계
- [PLAN-player-motor.md](../../PLAN-player-motor.md) — 이동/NetworkTransform 권한 작업
