# PLAN — 플레이어 프리팹을 base + 캐릭터 Variant 구조로 정리 (2026-09-16, 승인 대기)

작성: Claude / 대상: `Assets/2.Prefabs/Player/**`, `Assets/DefaultNetworkPrefabs.asset`, 일부 씬
관련: [Docs/tech/player-prefabs.md](Docs/tech/player-prefabs.md) (사실 원본) · [AGENTS.md](AGENTS.md) §3·§4 ·
[CONTEXT.md](CONTEXT.md) · [AIRULE.md](AIRULE.md)

> ⚠️ **플레이어 계통은 은희 담당 영역이다.** 착수 전 담당자 합의 필요.
>
> 🔴 **브랜치: `feature/player-variants`** — `feature/player-motor-owner-auth`(`6c25ca60`)에서 분기한다.
> `development` 에서 따지 **않는다**: 오너 권위 전환·시각 보간 컴포넌트 등 **프리팹 변경이
> owner-auth 브랜치에만 있어서**, development 기반으로 프리팹을 재구성하면 머지 때 GUID 단위로 충돌한다.
> 모터 작업과 **같은 프리팹을 건드리므로**, 이 브랜치에서 분리해 진행하고 머지 순서는 모터 → Variant 로 한다.

---

## 1. Goal

캐릭터를 늘릴 때 **프리팹을 통째로 복제하지 않는 구조**로 바꾼다.

- `Player.prefab` = 캐릭터에 무관한 **역할 base**
- `Player_<캐릭터>.prefab` = 그 base 의 **Prefab Variant**. 캐릭터 고유(Armature·스킬·스탯·평타)만 오버라이드
- 유저가 고른 값으로 **스폰할 Variant 를 고른다** (스폰 전 결정)

**이번 범위는 `Player_Paladin` Variant 까지**다. 로비 선택 UI 와 징크스는 범위 밖(§5).
즉 이번 작업이 끝난 시점에도 **게임 동작은 지금과 똑같아야 한다** — 구조만 바뀐다.

### 확정 사항 (2026-09-16 사용자 결정)

| 항목 | 결정 |
|------|------|
| 교체 방식 | **Prefab Variant**. 런타임 Armature 교체는 미채택 |
| 결정 시점 | **스폰 전.** 인게임 캐릭터 교체는 설계 범위 밖 |
| base/Variant 경계 | **스킬은 Variant 로 내린다.** base 는 `PlayerSkillController`(슬롯 컨테이너)까지 |
| 이번 범위 | **Paladin Variant 까지.** 선택 UI·징크스 제외 |

---

## 2. Current understanding

근거는 전부 [player-prefabs.md](Docs/tech/player-prefabs.md) 에 있다. 여기서는 이 작업에 걸리는 것만 요약한다.

### 2.1 출발점

- 정식 흐름이 스폰하는 것은 `Paladin.prefab` — **역할+캐릭터가 한 덩어리로 평탄화된 통짜 복제본**이다.
- `Player.prefab` 은 의도한 형태(중첩 `Armature`)지만 **현재 아무도 스폰하지 않고 값이 낡았다.**
- 둘은 variant 관계가 아니다. 루트 컴포넌트 구성은 **완전 동일**(각 37개)하고 차이는 값과 자식 조립 방식이다.

### 2.2 base 에서 걷어낼 것 = 캐릭터 고유 5종

| 컴포넌트 | 근거 |
|----------|------|
| `FirstMeleeMainSkill` | "Q 진격의 방패" — 가붕이 전용 |
| `FirstMeleeSubSkill` | "E 수호자의 의지" |
| `FirstMeleeUltimateSkill` | "R 최후의 심판" |
| `FirstMeleeInterruptSkill` | "우클릭 단죄의 방패" |
| `FirstMeleePassive` | "불굴의 의지" |

**✅ 걷어내도 코드가 안 깨진다 — 전부 null 안전이다.**

| 소비처 | 처리 |
|--------|------|
| `PlayerSkillController.InitializeSkill` ([:67](Assets/1.Scripts/Player/Skill/PlayerSkillController.cs:67)) | `if (skill == null) return;` |
| `Player.passive` ([:175](Assets/1.Scripts/Player/Player.cs:175), [:1285](Assets/1.Scripts/Player/Player.cs:1285)) | `GetComponent` → null 허용, 호출부는 `passive?.` |
| `PassiveHUD.Bind` ([:31](Assets/1.Scripts/UI/Combat/PassiveHUD.cs:31)) | `player != null ? GetComponent... : null` |

⇒ **base 는 스킬 슬롯이 비어 있어도 정상 기동한다.** 이게 이 계획의 전제다.

### 2.3 함께 Variant 로 내려갈 것

| 항목 | 현재 위치 | 비고 |
|------|----------|------|
| `Armature` (모델·리그·Animator·히트박스·무기 소켓) | Paladin 에 평탄화 | §3.1 에서 분리 |
| `Player.maxHp` / `attackDamage` | 루트 `Player` | 캐릭터별 스탯 |
| `DefaultAttackController.attackData` | `9.ScriptableObject/Player/Garen/PlayerDefaultAttackData.asset` | 경로부터 이미 캐릭터별이다 |
| `DefaultAttackController.defaultHitbox` | Armature 안 | Armature 를 따라간다 |

### 2.4 base 에 남을 것

네트워크(`NetworkObject`·루트 `NetworkTransform`) · 입력(`PlayerInput`·`PlayerInputReader`) ·
이동(`PlayerMotor`·`PlayerMovement`·`PlayerGroundingSensor`·`PlayerDashController`) ·
생명주기(`PlayerLifeCycleController`·`PlayerSoulController`·`PlayerReviveController`·`Corpse`) ·
낙하(`PlayerFallController`·`PlayerSafePointTracker`·`PlayerFallRecovery`·`PlayerLandingProtection`) ·
`StatusEffectController` · `PlayerStateController` · `PlayerEncounterLock` · `HurtBox` ·
`PlayerSkillController`(슬롯 컨테이너, 필드는 빈 채로) · `PlayerSkillTargeting` · `SkillCursorView` ·
`AimIndicator` · `SkillRangeIndicator` · HUD(`CombatHUD`·`OverheadHealthBar`) · `CameraFollowTarget` ·
`AudioListener` + `PlayerAudioListenerActivator` · `PlayerWaeponSlot`(컨테이너)

### 2.5 🔴 이 작업에서 걸리는 지뢰

1. **`transform.Find("Armature")` 이름 계약** — 폴백이 3곳이다
   ([`PlayerMovement.cs:38`](Assets/1.Scripts/Player/PlayerMovement.cs:38),
   [`PlayerSoulController.cs:175`](Assets/1.Scripts/Player/Soul/PlayerSoulController.cs:175),
   [`PlayableCharacterVisual.cs:30`](Assets/1.Scripts/Player/PlayableCharacterVisual.cs:30)).
   Variant 의 자식 이름은 **반드시 `Armature`** 로 한다. `Paladin_Armature` 로 두면 폴백이 전부 불발된다.
2. **HUD 사본이 갈라져 있다** — Paladin 안의 `CombatHUD` 사본에만 `CombatPanel`·`ShieldBar`·`ProfilPanel` 이
   있다. base 가 `CombatHUD.prefab` 을 중첩으로 쓰려면 **갈라진 내용을 원본 프리팹으로 올려야** 한다.
   이걸 빠뜨리면 HUD 가 퇴행한다.
3. **저작 툴이 폴더를 통째 순회한다** — `PlayerEncounterLockAuthoring`(폴더 스캔) ·
   `PlayerInterruptSkillAuthoring`(경로 하드코딩 `Player.prefab`/`Paladin.prefab`).
   Variant 생성 후 **대상 목록을 갱신해야** 한다. 특히 인터럽트 스킬은 이제 base 가 아니라 Variant 대상이다.
4. **`GlobalObjectIdHash` 는 프리팹마다 다르다** — Variant 는 **각각 NetworkPrefab 등록**이 필요하다.
   등록 누락 시 스폰이 조용히 실패한다.
5. **프리팹은 GitHub 관리 대상**([AGENTS.md](AGENTS.md) §3) — 머지 충돌 시 GUID 파손 위험. 작은 PR 로 나눈다.

---

## 3. Approach

**원칙: 한 단계가 끝날 때마다 게임이 돌아야 한다.** 중간에 "스폰되는 프리팹이 없는" 상태를 만들지 않는다.

### 3.1 P1 — `Paladin_Armature.prefab` 추출 (게임 동작 변화 없음)

`Paladin.prefab` 에서 캐릭터 부분만 뜯어 독립 프리팹으로 만든다.

- 대상: `Paladin_Armature` 서브트리 전체 — 리그·`tripo_part_0`·`DefaultAttack/AAC1~4`·
  `InterruptAttack`·`MainSkill`·`SubSkill`·`UltimateSkill`·VFX 트레일 2종·`Animator`·`NetworkAnimator`·
  Armature `NetworkTransform`
- 저장 위치: `Assets/2.Prefabs/Player/Paladin/Paladin_Armature.prefab`
- `Paladin.prefab` 은 이 단계에서 **그대로 둔다**(추출만 하고 아직 교체하지 않는다).
  Prefab Mode 에서 자식을 Project 로 끌면 원본의 자식이 중첩 인스턴스로 **바뀐다** —
  그걸 피하려면 씬에 인스턴스를 놓고 거기서 뽑은 뒤 씬을 버린다
- **에디터에서 손으로 한다. 저작 툴을 만들지 않는다** — 한 번 하고 끝나는 구조 변경이라
  멱등 재실행이 필요 없고, `SaveAsPrefabAsset` 이 중첩 VFX 프리팹·stripped 참조를 GUI 와
  동일하게 처리한다는 보장이 없다. (기존 `*Authoring` 툴들은 **머지에서 반복 유실되는 배선**을
  복구하려고 만든 것이라 성격이 다르다)
- ✅ 검증: `Paladin.prefab` **diff 가 0**. 추출물은 **YAML 파싱으로 대조**한다 —
  Animator·컨트롤러 / SkinnedMeshRenderer / `ColliderInfo` 6개(AAC1~4·MainSkill·InterruptAttack) /
  `NetworkTransform` 1 / `NetworkAnimator` 1 / `L_WeaponSocket`·`R_WeaponSocket` 존재.
  ⚠️ 무기 소켓은 컴포넌트가 없어 **이름으로만** 찾을 수 있다

### 3.2 P2 — base `Player.prefab` 정리

`Player.prefab` 의 GUID(`55ee4e06…`)를 **유지**한다. 이미 씬·네트워크 목록이 참조하고 있어서 새로 만들면
참조가 끊긴다.

1. `Armature` 슬롯을 **비운다**(`TempPlayer_Armature` 중첩 인스턴스 제거)
   — base 는 캐릭터가 없는 상태가 정상이다
2. 스킬 5종(§2.2) 제거 + `PlayerSkillController` 의 슬롯 4필드를 **비운다**
3. `DefaultAttackController.attackData` / `defaultHitbox` 를 **비운다**
4. HUD 갈라짐 해소 — Paladin 사본의 `CombatPanel`·`ShieldBar`·`ProfilPanel` 을
   `CombatHUD.prefab` 원본으로 올린다 (§2.5-2)
5. 스탯을 중립값으로 — 시연용 `9999`/`33` 은 Variant 로 간다
6. **`PlayerVisualReconciliationSmoother` 를 base 에 추가** — `9e3afee9` 가 Paladin 루트에만 붙였다.
   역할 쪽 컴포넌트이므로 base 의 것이다
7. **권한 값(`AuthorityMode`)은 브랜치 정책을 그대로 따른다** — 현재 owner-auth 기준으로
   루트·Armature = Owner(`1`), Corpse = Server(`0`). 🔴 **상수로 취급하지 말 것.**
   지스타 이후 서버 권위로 되돌릴 때 **Variant 개수만큼 고칠 곳이 늘어난다** — Variant 가
   이 값을 오버라이드하지 않고 **base 에서 상속받게** 두는 것이 이 작업의 이득 중 하나다
- ✅ 검증: base 단독 인스턴스를 씬에 올려 **에러 없이 Awake 가 통과**하는지 (조작·전투는 불가한 게 정상)

### 3.3 P3 — `Player_Paladin.prefab` Variant 생성

`Player.prefab` 을 base 로 Variant 를 만들고 캐릭터 고유를 얹는다.

1. `Armature` 자식으로 `Paladin_Armature.prefab` 중첩 — 🔴 **오브젝트 이름은 `Armature`**(§2.5-1)
2. 스킬 5종 추가 + `PlayerSkillController` 4슬롯 배선
3. `attackData` = `Garen/PlayerDefaultAttackData.asset`, `defaultHitbox` = Armature 안 히트박스
4. `FirstMelee*.hitboxAnchor` 2종 · 무기 트레일 배선 (§1.4 계약)
   💡 **여기는 저작 툴이 값어치를 할 수 있는 유일한 자리다** — 이 배선은 과거에 머지에서 반복
   유실됐고(`PlayerEncounterLockAuthoring` 주석의 3건), 캐릭터가 늘면 Variant 마다 반복된다.
   다만 **캐릭터가 2종 이상이 될 때** 만든다. 지금 1종에 도구를 세우는 건 이르다
5. 스탯 오버라이드
6. 저장: `Assets/2.Prefabs/Player/Paladin/Player_Paladin.prefab`
- ✅ 검증: `Paladin.prefab` 과 **루트 컴포넌트 구성·주요 값이 일치**하는지 기계적으로 대조 (§4)

### 3.4 P4 — 스폰 경로 전환

1. `DefaultNetworkPrefabs.asset` 에 `Player_Paladin` **등록**
2. `NetworkManager.prefab` 의 `defaultPlayerPrefab` → `Player_Paladin`
3. 테스트 씬 전환 — `BossScene`·`MonsterScene`·`PlayerDashTest`·`Dev_Boot` 의 참조를 `Player_Paladin` 으로
4. 구 `Player.prefab` 을 쓰던 `TrashMobScene`·`Debug/PlayerScene` 도 함께 전환
- ✅ 검증: Play 시 `Player_Paladin(Clone)` 이 **정확히 1개** 스폰

### 3.5 P5 — 구 프리팹 정리 + 툴·문서 갱신

1. `Paladin.prefab` 을 `DefaultNetworkPrefabs.asset` 에서 제거 후 삭제
2. `TempPlayer_Armature.prefab` 처리 결정 — 쓰이지 않으면 삭제
3. 저작 툴 2종의 대상 목록 갱신 (§2.5-3)
4. base 를 네트워크 목록에서 뺄지 결정 — base 는 스폰 대상이 아니다
5. [player-prefabs.md](Docs/tech/player-prefabs.md) §0·§2·§3·§4·§8 갱신, [CONTEXT.md](CONTEXT.md) 인계 갱신

---

## 4. 검증 방법

**기계적 대조가 핵심이다.** "눈으로 봤을 때 똑같다"로는 이 작업의 성공을 판정할 수 없다.

1. **P3 직후 — 구/신 프리팹 대조**
   `Paladin.prefab` vs `Player_Paladin.prefab` 의 **루트 컴포넌트 목록과 직렬화 값**을 뽑아 diff 한다.
   의도한 차이(기본값 상속으로 사라진 중복)만 남아야 한다.
2. **Play 1사이클** — `Dev_Boot` → 보스 입장 연출 → 평타 4타 → Q/E/R/우클릭 → 대시 → 피격 → 사망·부활.
   HUD 6슬롯·HP·실드·상태이상·보스 체력바 표시 확인.
3. **MPPM 2인** — 호스트/클라 양쪽에서 위를 반복. 특히 **Armature 회전 동기화**와 애니메이션 재생.
   🔴 Play 는 **사용자가 직접** 실행한다(MCP 로 Play 를 켜면 MPPM 이 깨진다).
4. **회귀 확인** — [PLAN-player-motor.md](PLAN-player-motor.md) 의 이동 검증 항목이 그대로 통과하는지.

---

## 5. 범위 밖 (이번에 하지 않는다)

| 항목 | 이유 |
|------|------|
| 로비 캐릭터 선택 UI | 현재 `LobbyPlayerSlotView` 는 연결·준비 상태만 다룬다. 선택 경로는 별도 작업 |
| `ResolvePlayerPrefabForClient` 매핑 구현 | 선택값이 없으면 매핑할 게 없다. 이번엔 `defaultPlayerPrefab` 유지 |
| 징크스 Variant | Armature·스킬·애니메이션이 없어 껍데기만 나온다 |
| `PlayableCharacterVisual` / `CharacterDefinition` 처리 | Variant 방식에서는 쓰이지 않는다. 존치/삭제는 별도 결정 |
| 스탯 소유자 재설계 | 현재대로 프리팹 `Player` 컴포넌트에 둔다 |

---

## 6. 리스크

| # | 리스크 | 완화 |
|---|--------|------|
| R-1 | **모터 작업과 같은 프리팹을 건드린다** | ✅ 브랜치 분리 완료 — `feature/player-variants` (`6c25ca60` 분기). 모터 쪽 프리팹 변경이 들어오면 **Variant 작업 중이라도 즉시 리베이스**해서 격차를 작게 유지한다 |
| R-2 | **프리팹 머지 충돌 → GUID 파손** ([AGENTS.md](AGENTS.md) §3) | P1~P5 를 각각 작은 PR 로. 작업 중 플레이어 프리팹 동시 수정 금지를 CONTEXT 에 명시 |
| R-3 | HUD 갈라짐을 못 올리고 base 로 내려가 **HUD 퇴행** | P2-4 를 독립 커밋으로 하고, 올리기 전후 HUD 스크린샷 대조 |
| R-4 | Variant 배선 누락(`hitboxAnchor`·`defaultHitbox`)으로 **공격이 조용히 안 나감** | §4-1 기계적 대조 + 저작 툴 재실행 |
| R-5 | `Armature` 이름 계약 위반 | P3-1 체크리스트 + 폴백 3곳을 아는 상태로 작업 |
| R-6 | NetworkPrefab 등록 누락 → 스폰 조용히 실패 | P4-1 후 즉시 Play 1회 |
| R-7 | 은희 담당 영역 침범 | 착수 전 합의. CONTEXT 작업 세션에 기재 |

---

## 7. 완료 조건

- [ ] `Player.prefab` 이 캐릭터 요소 0 인 base 이고, 단독으로 에러 없이 기동한다
- [ ] `Player_Paladin.prefab` 이 `Player.prefab` 의 Variant 이고, 자식 이름이 `Armature` 다
- [ ] 정식 흐름·테스트 씬 전부 `Player_Paladin` 을 스폰한다
- [ ] `Paladin.prefab` · (결정 시)`TempPlayer_Armature.prefab` 이 저장소에서 사라졌다
- [ ] §4 의 Play 1사이클과 MPPM 2인 검증을 **은희가** 통과시켰다
- [ ] 저작 툴 2종이 새 구조를 대상으로 돈다
- [ ] [player-prefabs.md](Docs/tech/player-prefabs.md) 가 새 구조로 갱신됐다

---

## 8. 승인받을 것

1. ~~착수 시점~~ → ✅ **브랜치 분리로 해결**(2026-09-16). `feature/player-variants` 에서 병행한다.
2. **작업 위치** — 이 워킹트리를 새 브랜치로 옮길 것인가, 별도 worktree 를 팔 것인가?
   · 여기서 전환하면 **진행 중인 모터 Play 검증이 끊기고** Unity 재임포트가 돈다.
   · 새 worktree 는 **SVN 아트가 없어** 모델·머티리얼이 비어 보인다([environment-setup.md](Docs/tech/environment-setup.md)).
     프리팹 조립 작업이라 아트가 보여야 하므로 **SVN 체크아웃이 선행**돼야 한다.
3. **담당** — 플레이어 계통은 은희 영역이다. 누가 하는가?
4. **`Paladin.prefab` 삭제** — P5 에서 지우는 게 맞는가, 한동안 남겨두는가?
