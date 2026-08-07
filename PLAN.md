# CURRENT PLAN — 보스 FSM 지원 2건: 인터럽트 식별자 + 캐리 소켓 일반화 (2026-08-07)

> 상태: **구현 진행 중**(사용자 지시 = PLAN 작성 후 바로 착수).
> 브랜치 `feature/InterruptSkill-CarrySocket` (base `MainProject/development` `6dbc1c34a`).
> 요청 출처: 경석 → 은희 인계문 `C:\Users\user\Desktop\handoff-player-carry-socket.md` (기한 8/7 17:00).
> 담당: 은희(플레이어 계통). 보스 쪽 소비 코드는 경석이 작성한다.

## 목표

보스 FSM 재작성이 플레이어 쪽에 요구하는 두 계약을 **기존 동작 회귀 없이** 제공한다.

- **A.** 보스가 서버에서 "이 히트 = 인터럽트 스킬"을 판별할 수 있다. ✅ 구현·컴파일 검증 완료
- **B'.** 서버가 플레이어를 잠시 구속할 수 있다 — 잡기(소켓 종속)와 돌진 밀기(정면 추종)를
  `Restrained` 한 상태의 두 모드로. ✅ 구현·컴파일 검증 완료
  (원 요청 B "캐리 소켓 일반화"는 폐기. 경위는 §접근 B 참고)

## 현재 이해 (코드 확인 완료)

| 사실 | 근거 |
|---|---|
| `AttackType`에 우클릭 슬롯 값이 없다 | [BaseAttack.cs:4](Assets/1.Scripts/Unit/Weapon/BaseAttack.cs:4) |
| 🔴 **플레이어 스킬은 `BaseAttack`을 타지 않는다** — `AttackInfo`를 직접 만들어 `ReceiveAttack` 호출 | [FirstMeleeMainSkill.cs:114](Assets/1.Scripts/Player/Skill/FirstMeleeMainSkill.cs:114), [FirstMeleeUltimateSkill.cs:58](Assets/1.Scripts/Player/Skill/FirstMeleeUltimateSkill.cs:58) |
| 🔴 **단죄의 방패가 미구현** — `interruptSkill` 슬롯이 비어 있다 | [PlayerSkillController.cs:22](Assets/1.Scripts/Player/Skill/PlayerSkillController.cs:22) |
| `InterruptAttack` 앵커 노드는 컴포넌트 0개의 맨 Transform | [Paladin.prefab:5806](Assets/2.Prefabs/Player/Paladin/Paladin.prefab:5806) |
| 현재 우클릭 = `PlayerInterruptState`(전방 돌진 + Animator 트리거), **데미지 0** | [PlayerStateController.cs:604](Assets/1.Scripts/Player/PlayerStateController.cs:604) |
| 슬롯에 스킬이 배정되면 위 상태 경로는 자연 대체된다 | [PlayerStateController.cs:516-520](Assets/1.Scripts/Player/PlayerStateController.cs:516) |
| `AttackType`은 **2곳**에서 직렬화된다 → 값은 반드시 끝에 추가 | [BaseAttack.cs:73](Assets/1.Scripts/Unit/Weapon/BaseAttack.cs:73), [Bomb.cs:6](Assets/1.Scripts/Enemy/Boss/Bomb.cs:6) (`Bomb.cs:12`에서 값 비교) |
| `PlayerGrabbedState`가 `GrabController` 구체 타입으로 소켓을 찾는다 | [PlayerStateController.cs:700-703](Assets/1.Scripts/Player/PlayerStateController.cs:700) |
| `BeginGrabbedByInstigator` 호출부는 1곳뿐 | [GrabController.cs:208](Assets/1.Scripts/Enemy/Boss/GrabController.cs:208) |
| 🟡 잡기와 캐리가 **상태 하나(`Grabbed`)를 공유**한다 — `CanReceiveGrab`이 이미 Grabbed면 거부하고, `EndGrabbed()`는 시작 주체를 안 본다 | [PlayerStateController.cs:344-346](Assets/1.Scripts/Player/PlayerStateController.cs:344), [:166](Assets/1.Scripts/Player/PlayerStateController.cs:166) |
| 스킬 애니 이벤트 릴레이는 존재하나 **Hit 이벤트를 쓰는 스킬이 아직 없다**(Q=홀드틱, E=판정없음, R=채널) | [PlayerAnimationEventRelay.cs:43](Assets/1.Scripts/Player/PlayerAnimationEventRelay.cs:43) |
| Garen Animator에 `Interrupt` 상태가 존재 | `Assets/4.Animations/Player/Garen/PlayerAnimatorController.controller` |

## 명시적 가정 (설계 문서 부재 — 확정되면 SO 수치만 조정)

단죄의 방패의 거동 스펙은 `Docs/design/`에 없다(`character_garen.md`는 전부 TBD, 인계문이 참조한
`boss-fsm-design.md`는 존재하지 않음). 아래를 가정하고 진행한다.

1. **단죄의 방패 = 짧은 전방 방패 강타.** 데미지 1회 + `AttackType.Interrupt` 태그. 카운터 창·정면 각도·
   그로기 전이는 **전부 보스 쪽 책임**(인계문 §A 명시)이므로 플레이어는 태그만 싣는다.
   - `Docs/design/level-system.md:64-65`의 "가붕이 = 패링" 해석은 채택하지 않는다 — 방어 창 기반 패링은
     "히트를 보스에 보낸다"는 인계문 계약과 어긋나고 범위가 훨씬 크다. 기획 확정 시 재논의.
2. **애니메이션 클립을 수정하지 않는다.** 판정 타이밍은 SO의 `hitDelay` 타이머로 잡고,
   `Hit` 애니 이벤트가 나중에 심어지면 그쪽이 우선하도록 **둘 다 받되 1회만 발동**한다.
   (클립은 아트/SVN 관할이고, 현재 어떤 스킬도 Hit 이벤트를 안 쓴다 = 미검증 경로.)
3. 수치는 전부 임시값. `attackDamageMultiplier`·쿨타임은 기획 확정 전 placeholder.

## 접근

### A. 인터럽트 식별자 + 단죄의 방패

1. **인터럽트는 `AttackType`이 아니라 `AttackInfo.isInterruptAttack`(기존 `isGroggyAttack` 개명)이 싣는다.**
   - 이유: `AttackType`은 "어느 출처가 쐈나"이고 인터럽트는 그와 **직교한 능력**이다. enum 값으로 넣으면
     "Q 슬롯인데 인터럽트인 스킬"을 표현할 수 없고, 같은 사실을 두 곳에서 말하게 된다.
   - **플래그는 하나만 둔다.** `AttackInfo`는 공격자가 아는 사실("인터럽트다")만 싣고,
     **소비 방식은 수신측이 정한다** — 몬스터/중간보스는 `maxGroggyCount`까지 누적→그로기
     ([MonsterBase.cs:775](Assets/1.Scripts/Monster/MonsterBase.cs:775) · [BossBase.cs:375](Assets/1.Scripts/Monster/Boss/BossBase.cs:375)),
     보스 No.23은 카운터 창·정면 각도 판정(경석). `Docs/design/level-system.md:70`의 분담과 일치한다.
   - 곁들여 **`AttackType`을 `{None, Default, Skill}`로 축소** — Q/E/R을 구분해 읽는 코드가 없었다.
     정수값 `None=0`·`Default=1`은 **고정**(기존 에셋 25곳과 `Bomb.attackType=1`이 여기 걸려 있다).
   - **`BaseAttack`의 저작 토글은 삭제.** 인터럽트를 켜는 주체는 스킬뿐이고 스킬은 `BaseAttack`을 안 탄다 —
     켤 수 없는 체크박스는 거짓 약속이다.
2. `FirstMeleeInterruptSkillData : PlayerSkillData` 신설 — `hitDelay`, `skillDuration`, `maxHitResults`.
3. `FirstMeleeInterruptSkill : PlayerInstantSkill` 신설 — `Slot => Interrupt`.
   서버가 `hitDelay` 경과(또는 `Hit` 애니 이벤트) 시 `HitboxAnchor` 기준 Overlap →
   `new AttackInfo(damageSnapshot, AttackType.Interrupt)` → `ReceiveAttack`. `skillDuration`에 자체 종료.
   판정은 **1회만**(`hasResolvedHit` 래치).
4. SO 에셋 `Assets/9.ScriptableObject/Player/Garen/FirstMeleeInterruptSkillData.asset`.
5. `Paladin.prefab` 배선: `InterruptAttack` 노드에 `BoxCollider`+`ColliderInfo`,
   루트에 `FirstMeleeInterruptSkill`, `PlayerSkillController.interruptSkill` 연결.

### B. ~~캐리 소켓 일반화~~ → **폐기.** 돌진이 넉백+기절 방식으로 바뀌며 불필요해졌다 (2026-08-07)

`ICarrySocketProvider` 코드는 원복했다. 소켓 제공자가 `GrabController` 하나뿐이라 `Kind` 탐색 자체가 무의미했다.

### B'. `Restrained` — 서버 구속의 단일 상태 (경석 개정 1판 승인)

넉백+기절만으로는 "밀고 가기"가 안 된다(§리스크 C-1 — `Unit.Knockback`은 임펄스 1회, duration 개념이 없다).
그래서 잡기와 밀기를 **한 상태의 두 모드**로 묶는다.

6. `PlayerActionState.Grabbed` → **`Restrained`** 개명(정수값 유지). `PlayerGrabbedState` → `PlayerRestrainedState`.
   `GrabInteractionContext` → `RestraintContext`, `IGrabInteractionReceiver` → `IRestraintReceiver`.
7. `enum RestraintMode : byte { Carry = 0, Push = 1 }` — `Tick()`의 목표 자세만 갈린다:

   | 모드 | 추종 대상 |
   |---|---|
   | `Carry` | `GrabController.GrabSocket` (**기존과 동일**) |
   | `Push` | `instigator.position + instigator.forward × frontOffset` |

   **Push는 소켓도 방향/속도 동기화도 안 쓴다.** 시전자 루트가 `NetworkTransform`으로 복제되므로 오너
   클라에서 월드 위치가 저절로 맞는다 — "돌진 소켓은 애니메이션으로 안 움직이는 고정 자식이어야 한다"는
   제약이 사라진다. Y는 **진입 시점 값으로 고정**한다(캐리 중 `isKinematic`이라 중력이 없고,
   시전자 Y를 그대로 쓰면 피벗 높이 차이로 플레이어가 조용히 뜨거나 잠긴다).
8. **`Push`만 슈퍼아머를 거부한다** — `Unit.Knockback`과 같은 규칙(슈퍼아머면 안 밀린다)을
   플레이어 쪽 한 곳에 둔다. `Carry`는 원래 슈퍼아머와 무관하게 걸렸고 바꾸면 보스 Grab 체인이 회귀한다.
   판정은 **서버 진입(`TryReceiveRestraint`)에서만** 한다 — 오너가 다시 판정하면 복제 지연 시 상태가 갈린다.
9. **`BeginRestrainedByInstigator`가 `bool`을 반환한다.** 시전자는 이 값으로 후처리를 가른다
   (돌진이 벽에 닿았을 때 **실제로 밀린 대상만** 기절). 데미지는 이 값과 무관한 별도 경로.
10. `BeginGrabbedByInstigator`/`EndGrabbedByInstigator`는 **`Carry` 호환 래퍼로 유지** —
    [GrabController.cs:208](Assets/1.Scripts/Enemy/Boss/GrabController.cs:208)·[:228](Assets/1.Scripts/Enemy/Boss/GrabController.cs:228)은 무수정이다.
11. 🔴 **`followTarget == null` 널 허용을 유지한다** — 위치 추종만 건너뛰고 물리 위임·입력 차단은 그대로 간다.
    보스에 잡기 소켓이 아직 없는 동안 "제자리에 붙잡힘"이 이 성질로 성립 중이다(경석 요청).

## 네트워크 권한 가정

- 단죄의 방패: 오너 입력 → 서버 승인(`PlayerSkillController`) → 서버만 판정. 기존 스킬 계약과 동일.
- `Restrained`: **상태 전이·해제·슈퍼아머 판정 = 서버**, **위치 추종 실행 = 오너**(`IsMovementAuthority`).
  서버가 위치를 직접 쓰지 않으므로 "플레이어 이동은 오너 권한"(networking.md) 원칙이 유지된다.
  `Transform`은 복제 불가이므로 RPC엔 **모드(byte) + offset(float)만** 싣는다.
  입력 차단은 별도 계통이 아니라 **상태 진입만으로** 성립한다(`CanMove`/`CanUseSkill`이 `Idle|Move` 한정).

## 리스크 / 한계

- 🟡 **잡기와 돌진 밀기가 `PlayerActionState.Restrained`를 공유한다.** 구속 중 재진입은 조용히 거부되고
  (`CanReceiveRestraint`), `End`는 시작 주체를 구분하지 않는다. 보스 1기 기준 무해 — **계약으로 명시**한다.
- 🔴 **C-1: `Unit.Knockback`은 임펄스 1회다.** `AttackInfo`의 `knockbackDuration`·`staggerDuration`은
  `MonsterBase`만 소비하고 플레이어 수신 경로는 무시한다. 이 사실 때문에 보스 돌진이 넉백이 아니라
  `Restrained.Push`로 간다(경석 확인·설계 변경 완료).
- 🟡 구속 중 `detectCollisions = false`라 플레이어가 벽을 통과한다.
  **벽 판정은 보스 책임** — 캐리 중이면 벽에서 떨어진 지점에서 돌진을 정지한다(경석 수용).
- 🟡 단죄의 방패 판정 타이밍이 애니 클립과 어긋날 수 있다(타이머 기반). SO 수치로 맞추고,
  정밀 타이밍이 필요하면 클립에 `HandleSkillEvent(0)` 이벤트를 심는다.
- 🔴 프리팹 배선은 Unity 재임포트가 필요하다 — 인스펙터에서 컴포넌트가 보이는지 육안 확인 필수.

## 범위 밖

- 보스 쪽 카운터 창·정면 각도·그로기/Break 전이·시각 피드백 (경석).
- 돌진 소켓 자체의 생성/배치 (경석).
- 패링 해석의 방어 창/반사 메커니즘 (기획 확정 후 별건).
- `PlayerInterruptState` 제거 — 스킬 미배정 시의 폴백으로 남긴다.

## 완료 조건

1. C# 컴파일 0 에러. ✅ (경고는 전부 기존 파일)
2. 직렬화된 `attackType` 값이 변하지 않는다 — `0`×22 / `1`×3 유지, 특히 `Bomb.attackType=1`(평타 반응). ✅
3. 우클릭 → 단죄의 방패 발동 → 적중 시 수신측이 `attackInfo.isInterruptAttack == true`를 본다. ⏳ Play 필요
4. 중간보스(GauntletBot·SpinnerBot·WallBot)가 인터럽트 누적으로 그로기에 들어간다
   (`maxGroggyCount` 3/3/4 — **이전엔 켜는 주체가 없어 사실상 죽은 경로였다**). ⏳ Play 필요
5. 기존 잡기(Grab → Hold → Throw) 회귀 없음 — `GrabController` 무수정, `Carry` 래퍼 경유. ⏳ Play 필요
5-1. `BeginRestrainedByInstigator(boss, Push, offset)` → 플레이어가 보스 정면을 따라간다. ⏳ Play 필요
5-2. 슈퍼아머(Q 홀드) 중이면 `Push`가 **false를 반환**하고 밀리지 않는다. `Carry`는 그대로 걸린다. ⏳ Play 필요
6. MPPM 2인(호스트/클라) 검증. ⏳

> ⚠️ 프리팹·SO에 `isGroggyAttack` YAML 키 24개가 고아로 남는다(전부 값 0). Unity가 해당 에셋을
> 재직렬화할 때 자연 소멸한다 — 의미 손실은 없고 diff 노이즈로만 나타난다.

---

# CURRENT PLAN — 카메라 쉐이크 + HP 비네트 (2026-08-06)

> 상태: **승인 대기**. 브랜치 `feature/CameraFeedback` (base `development`), 레인 `MainProject`.
> 구현 위임: Codex(`.cs`만) / 프리팹 부착: 은희 또는 Claude(Codex 종료 후).
> grill 완료 — 아래는 확정된 결정만 담는다.

## 목표

전투 타격감을 **로컬 표현**으로 올린다.

1. **카메라 쉐이크** — 내가 맞을 때(강) / 내가 때렸을 때(약·짧게).
2. **HP 비네트** — `현재HP / 최대HP`가 낮아질수록 화면 테두리가 진해지고, 회복되면 연해진다.
   피격 순간의 번쩍임이 아니라 **HP 수위를 상시 반영하는 연속 표현**이다.

## 스코프

- **In**: 로컬 플레이어 기준 쉐이크 2종 + HP 비율 비네트.
- **Out**: 카메라 플레어(원 요청 3번) — 렌즈 플레어인지 화면 플래시인지 미확정이라 분리.
- **Out**: 보스 착지·광범위 기믹 연출 지진 — `BossEncounterDirector`를 건드려야 해서 별건.
- **Out**: 피격 순간 비네트 펄스 — HP 연속 표현으로 요구가 충족된다. 나중에 얹을 수 있게 진폭 소스를 하나로 둔다.

## 🔴 불변식 (이걸 어기면 3인 플레이가 망가진다)

**세 효과 전부 로컬 전용이다.** 서버 RPC로 브로드캐스트하면 한 명이 맞을 때 3명 화면이 다 흔들린다.
피해 이벤트(`ClientDamagedAmount` 등)는 **모든 피어에서 발동**하므로 "로컬 플레이어인가" 게이트가
반드시 있어야 한다. 판정 기준은 기존 코드와 동일하게 쓴다:

- 내가 맞았나 = `unit is Player p && (p == Player.LocalPlayer || p.IsOwner)`
- 내가 때렸나 = `attackerClientId == NetworkManager.Singleton.LocalClientId`

두 판정 모두 [FloatingDamagePresenter.cs:73-82](Assets/1.Scripts/UI/Combat/FloatingDamage/FloatingDamagePresenter.cs:73)에
이미 있다. **똑같이 쓸 것**(새로 만들지 말 것).

## 현재 이해 (조사 완료 — 전제로 삼아도 된다)

| 사실 | 근거 |
|---|---|
| 필요한 이벤트가 이미 다 있다. 새 이벤트·새 RPC **불필요** | `Unit.cs`: `ClientHpChanged(prev,next)` / `ClientDamagedAmount(amount,channel)` / `ClientDamagedAttributed(amount,channel,attackerClientId)` |
| **`Unit.OnNetworkSpawn`이 모든 Unit에 컴포넌트를 자동 부착하는 관례가 있다** | [Unit.cs:505-509](Assets/1.Scripts/Unit/Unit.cs:505) — `HitFlash`, `FloatingDamagePresenter`. 덕분에 적 프리팹 작업이 0이다 |
| 카메라 리그는 씬에 없다. **런타임 생성**이다 | `CameraTargetSwitcher`가 `MainCamera.prefab` + `PlayerFollowCamera.prefab`을 인스턴스화. MapScene에 Cinemachine 참조 0건 |
| Cinemachine **3.1.6** (`CinemachineCamera`/`CinemachineBrain`/`CinemachineFollow`) | `CameraTargetSwitcher.cs` |
| 추락·관전 카메라 전환이 있다 | `CameraTargetSwitcher.IsInFallView` / `IsSpectatorMode` — vcam 2개를 우선순위로 스왑 |
| MapScene에 `Global Volume` + 프로필 에셋 존재 | `4.MapScene.unity:4002`, 프로필 GUID `746a3f69856cf614d8e782652e51b262` |

## 접근

### A. 부착 지점 — `MainCamera.prefab`에 컴포넌트 1개

신규 `CameraFeedback`을 **`MainCamera.prefab`(= Brain을 든 렌더 카메라)에 부착**한다. 이유:

- 리그가 런타임 생성이라 씬 배선으로는 못 잡는다. 프리팹에 있으면 리그와 함께 생성된다.
- 튜닝값을 `SerializeField`로 프리팹에서 조절할 수 있다(런타임 `AddComponent`면 불가능).
- ⚠️ **Impulse 리스너는 vcam이 아니라 Brain 쪽에 둔다.** vcam에 붙이면 추락·관전 전환 때 끊긴다.
  타입은 **`CinemachineExternalImpulseListener`**(순수 `MonoBehaviour`)다.
  🔴 **정정**: 이 계획의 초안에 적었던 `CinemachineIndependentImpulseListener`는 3.1.6에 **존재하지 않는다**.
  `CinemachineImpulseListener`는 `CinemachineExtension`이라 vcam 전용이므로 Brain에 못 쓴다.
  (파일명 `Runtime/Impulse/CinemachinExternalImpulseListener.cs`에 오타가 있지만 클래스명은 정상이다.)

`Volume`과 `CinemachineIndependentImpulseListener`는 **이 컴포넌트가 런타임에 `AddComponent`로 만든다**
→ 프리팹 작업은 "컴포넌트 1개 부착"뿐이고 참조 배선이 없다.

### B. 비네트 — 런타임 생성 Volume (공유 에셋 절대 건드리지 않음)

🔴 **씬의 `Global Volume` 프로필을 런타임에 수정하면 안 된다.** `sharedProfile`은 에셋이라
값이 에디터에서 디스크에 남고, 모든 피어·모든 세션에 새어나간다.

대신 `CameraFeedback`이 자기 GameObject에:

1. `Volume` 추가 — `isGlobal = true`, `priority`를 Global Volume보다 높게(예: 100)
2. `VolumeProfile`을 **런타임 인스턴스로** 생성(`ScriptableObject.CreateInstance<VolumeProfile>()`)
3. 그 프로필에 `Vignette` 오버라이드만 추가하고 `intensity`를 매 프레임 갱신
4. `OnDestroy`에서 프로필 인스턴스 `Destroy` (누수 방지)

```
매 프레임:
  Player p = Player.LocalPlayer
  if (p == null || p.FinalMaxHp <= 0) → 목표 강도 0
  else ratio = clamp01(p.CurrentHealth / (float)p.FinalMaxHp)
       목표 강도 = Lerp(intensityAtFullHp, intensityAtZeroHp, 1 - ratio)
  현재 강도 = MoveTowards(현재, 목표, smoothingPerSecond * dt)   // 고정 속도
  vignette.intensity.value = 현재 강도
```

- **고정 속도로 따라가는 이유**: `Lerp` 감쇠는 끝이 안 닿아 미세하게 남고 프레임레이트에 의존한다
  (지연 체력바에서 같은 결론을 냈다 — `DelayedHealthBar` 참조).
- HP를 **이벤트가 아니라 폴링**으로 읽는다. 목표가 "수위 반영"이라 델타가 필요 없고,
  늦은 바인딩·부활·최대HP 변동이 전부 자동으로 맞는다.
- Soul/사망 표현은 건드리지 않는다 — `PlayerCombatUiLifecyclePolicy`가 HUD를 따로 처리한다.

### C. 쉐이크 — Cinemachine Impulse, 방향은 무작위

`CameraFeedback`에 공개 메서드 2개를 두고 per-Unit 리포터가 호출한다.

| 계기 | 세기 | 지속 |
|---|---|---|
| 내가 맞음 | 강 | 길게 |
| 내가 때림 | 약 | 짧게 |

- 두 값 전부 `SerializeField`(진폭·지속). 기본값은 Codex 재량, 튜닝은 플레이 후 프리팹에서.
- 피해량에 비례시키지 **않는다** — v1은 고정 2단계. (비례는 튜닝 축이 늘어나 나중에.)
- 연타 시 임펄스가 누적돼 화면이 멀미나면 안 된다 → **최소 재발동 간격**(`SerializeField`, 예 0.05s)을
  계기별로 따로 둔다.

### D. 피해 계기 수집 — `Unit` 자동 부착 리포터 1개

신규 `UnitCameraFeedbackReporter`를 `FloatingDamagePresenter`와 **똑같은 방식**으로 만든다:

- `Unit.OnNetworkSpawn`에 2줄 추가해 자동 부착([Unit.cs:508](Assets/1.Scripts/Unit/Unit.cs:508) 바로 아래).
  **적/보스/플레이어 프리팹 수정 0.**
- `OnEnable`에서 자기 `Unit`의 `ClientDamagedAmount` + `ClientDamagedAttributed` 구독, `OnDisable`에서 해제.
- 분기:
  - 이 Unit이 **로컬 플레이어**이고 피해가 들어왔다 → `CameraFeedback.Instance?.ReportLocalPlayerHit()`
  - 이 Unit이 로컬 플레이어가 **아니고** `attackerClientId == LocalClientId` → `ReportLocalPlayerDealtDamage()`
- `CameraFeedback.Instance`는 `FloatingDamageSpawner.Instance`와 같은 형태의 정적 접근자.
  **없으면 조용히 아무것도 안 한다**(카메라 리그 없는 테스트 씬에서 예외 금지).

⚠️ **중복 발동 주의**: `ClientDamagedAmount`와 `ClientDamagedAttributed`가 같은 피해에 둘 다 나온다.
맞음 쉐이크는 한쪽만 구독하거나 같은 프레임 중복을 눌러야 한다 — 아니면 진폭이 2배가 된다.

## 변경 파일 (정확히 4개)

| 파일 | 변경 |
|---|---|
| `Assets/1.Scripts/Camera/Feedback/CameraFeedback.cs` | **신규** — Impulse 소스/리스너 + 런타임 Volume·Vignette + 정적 `Instance` |
| `Assets/1.Scripts/Camera/Feedback/UnitCameraFeedbackReporter.cs` | **신규** — Unit 피해 이벤트 → 로컬 판정 → `CameraFeedback` 호출 |
| `Assets/1.Scripts/Unit/Unit.cs` | 자동 부착 2줄 (`FloatingDamagePresenter` 블록 바로 아래) |
| `Assets/1.Scripts/Camera/CameraTargetSwitcher.cs` | **필요할 때만** — Brain 카메라를 리포터가 못 찾으면 접근자 1개 추가. 불필요하면 손대지 말 것 |

## 구현 중 드러난 전제 (2026-08-06 Codex 보고 → 전부 실측 확인)

1. 🔴 **`MainCamera.prefab`의 `m_RenderPostProcessing: 0`** — post-processing이 꺼져 있어 비네트가
   아무것도 그리지 않는다. **코드로 켜지 않는다** — 켜면 그 카메라 범위의 Volume 오버라이드가 전부
   살아나서(씬 `Global Volume`·`FogProfile` 등) 게임 전체 룩이 바뀌고 툰셰이딩 작업과 충돌한다.
   컴포넌트는 경고 1회만 남기고, **켜는 것은 렌더링 전역 결정으로 팀장 확인 후 프리팹에서** 한다.
2. 🔴 **`FloatingDamageSettings.asset`의 `displayFilter: 0`(=AllDamage)** 이면
   `FloatingDamageSpawner.RequiresAttributedDamageRpc`가 false여서 `ClientDamagedAttributedClientRpc`가
   아예 나가지 않는다 → **타격 쉐이크가 영구히 안 뜬다.** 그 게이트는 "소비자가 없으면 RPC를 아끼는"
   장치이므로 두 번째 소비자를 OR로 더한다. `Unit`이 카메라 구현을 모르도록
   `CameraFeedback.RequiresAttributedDamageRpc` 정적 프로퍼티를 경유한다.
3. ✅ 카메라의 `m_VolumeLayerMask`가 Default(bit 1)이고 `MainCamera`가 layer 0이라 런타임 Volume이
   마스크에 잡힌다. (안 맞으면 비네트가 조용히 안 나온다.)

## 완료 조건

1. 변경 파일 최대 4개. **프리팹·씬·`.meta`·SO 무수정** (부착은 사용자 몫)
2. 신규 `.cs`는 **UTF-8(BOM)** — `Docs/tech/conventions.md` 규칙
3. 씬의 `Global Volume` 프로필 에셋이 런타임에 **수정되지 않음**(별도 Volume + 인스턴스 프로필)
4. 카메라 리그·`CameraFeedback`이 없는 씬에서 예외 0건 (전부 null 안전)
5. 3인 기준: 한 명이 맞을 때 **그 사람 화면만** 흔들린다
6. C# 컴파일 0 에러 / 0 경고
7. 단일 커밋 + `work_completed`에 커밋 해시 + 프리팹 부착 안내

## 검증 계획

- (사용자) `MainCamera.prefab`에 `CameraFeedback` 부착 → MapScene Play
- 솔로: 맞으면 강하게 흔들림 / 때리면 약하게 / HP 낮추면 비네트 진해지고 회복하면 연해짐
- **MPPM 2~3인: 한 명만 맞았을 때 다른 화면이 흔들리지 않는지** (불변식 검증 — 이게 핵심)
- 추락·관전 카메라 전환 중에도 쉐이크가 살아있는지

## 리스크

- Codex가 컴파일 검증을 못 한다(Unity 미실행) → 에러 시 Claude가 수정.
- Cinemachine 3.x Impulse API 이름이 2.x와 다르다(`CinemachineImpulseSource`,
  `CinemachineIndependentImpulseListener`). 3.1.6 기준으로 확인하며 쓸 것.
- `Unit.cs`는 코어(은희 담당)지만 자동 부착 2줄이라 기존 경로 무영향.
- 비네트 강도 기본값이 과하면 저HP에서 시야를 가린다 → 기본값은 보수적으로.
