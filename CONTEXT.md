# CONTEXT.md - Shared Project Language

This file defines the shared vocabulary for the project. Keep it concise. It is not a full spec and should not contain implementation plans.

Update this file when a term becomes important enough that future agents or teammates must use it consistently.

## ▶▶ 다음 세션 시작점 — 보스 재작성 (2026-08-07 종료)

🔴 **브랜치 `feature/Boss23`** · **컴파일 0에러 0경고** · **전부 미커밋**

**한 줄 상태: 코드·애니메이터·데이터가 끝났고, 남은 것은 프리팹 조립뿐이다.**
보스가 아직 한 번도 스폰된 적이 없다 — 그래서 `ValidateContract` 조차 돌지 않았다.

> **2026-08-07 밤 갱신** — 아래 **1·2단계는 완료**됐다. 컨트롤러를 고치는 게 아니라 **전면 재작성**으로
> 갔다(팀장 확정). 저작 도구 = `Assets/1.Scripts/Monster/Editor/TwentyThreeBossAuthoring.cs`
> (`Tools > Boss > 23호 — 컨트롤러 전면 재작성 + 데이터 저작`, 멱등).
>
> · `No23Controller` **신규** — 18상태 / 파라미터 `Speed`(Float)·`Groggy`(Bool)·`Death`(Trigger) /
>   전이 **5개뿐**(AnyState⇒Dead, AnyState⇒GroggyStart⇒Groggy⇒GroggyEnd⇒Locomotion). 나머지는 CrossFade.
> · `WellsBossController` **신규** — 4상태 / 트리거 4. 레거시 컨트롤러 2개는 **삭제**했다.
> · 로코모션은 `Speed` BlendTree(idle@0 / walk@2.5) — `_animSpeed` 는 `agent.velocity.magnitude` **원값(m/s)**.
> · 그로기·사망을 **파라미터 + AnyState** 로 받아 base 경로가 살아난다 → **코드·SO 스키마 수정 0줄.**
> · `No23.asset` 저작 완료(`archetype: Boss`, 공격 8행 상태명 전량, 애니 계약 필드 전량).
>
> 🔴 **`attackDuration` 은 올리지 않았다** — 아래 2단계 표의 "상향"은 오독이다. 보스가
> `_stateTimer = 체인길이 + attackDuration` 으로 이미 더한다(`TwentyThreeBoss.cs:456`).
> 🔴 **착지 상태는 원래 있었다** — `Arrive` 가 `landingattack` 을 쓰고 있었다. 다만 입장 연출
> (`BossEncounterDirector`)은 애니메이터를 **한 줄도 안 건드리므로** 고아였다. 새 컨트롤러엔
> `JumpLanding` 으로 다시 뒀다.
> 🔴 **fbx 후속(SVN)**: `Boss_23_idle`·`Boss_23_charging` 의 **Loop Time 이 꺼져 있다** — 오래
> 유지되는 상태인데 한 바퀴 뒤 마지막 프레임에서 굳는다. 애니 이벤트 저작과 같이 처리.
> 상세·이탈 근거 = [IMPLEMENTATION_NOTES.md](IMPLEMENTATION_NOTES.md) 최하단.

### 정본 문서 (읽는 순서)

| # | 문서 | 무엇 |
|---|---|---|
| 1 | [PLAN-boss-fsm.md](PLAN-boss-fsm.md) | 🔴 **구현 정본.** §5.1~5.11 에 슬라이스별 결정·근거·함정이 전부 있다 |
| 2 | [Docs/tech/boss-rebuild-standard.md](Docs/tech/boss-rebuild-standard.md) | 3층 구조·훅·규약·SO 설계(§10). §10.3.1 에 스키마 변경 이력 |
| 3 | [Docs/tech/boss-fsm-detailed-spec.md](Docs/tech/boss-fsm-detailed-spec.md) | 애니 계약(§1) · 점프(§7) · 폭탄/장판(§10.5) · 송전기(§9). **§1.1·§4 는 폐기** |
| 4 | [Docs/tech/handoff-boss-reply-interrupt-restrained.md](Docs/tech/handoff-boss-reply-interrupt-restrained.md) | 은희 회신(개정 1판) — 인터럽트·`Restrained` |
| 5 | [Docs/tech/layer-standard.md](Docs/tech/layer-standard.md) | 레이어 표준 + 이관 완료 기록 |

### ✅ 완료 — 슬라이스 9개 중 8개 (코드)

| S1 | S2 | S3 | S4 | S5 | S6-0 | S6 | S7 | S8 | S9 |
|---|---|---|---|---|---|---|---|---|---|
| ✅ | ✅* | ✅ | ✅* | ⏸ | ✅ | ✅ | ✅ | ✅ | ✅ |

`*` 부분 보류 — S2 어퍼 에어본(팀장 판단 보류. **단 G1 정정으로 지금 구현 가능**) / S4 Throw 변위(변위 경로 없음)
`⏸` S5 Dash 캐리 — 은희 `Restrained.Push` 머지 대기

**신규 파일 15개** (`Assets/1.Scripts/`)

| 위치 | 파일 |
|---|---|
| `Monster/` | `AreaZone.cs` · `AreaZoneType.cs` |
| `Monster/Boss/` | `TwentyThreeBoss.cs` · `BossDataSO.cs` · `BossAttackId.cs` · `BossAttackPhase.cs` · `IBossTelegraph.cs` · `BossCounterTelegraph.cs` · `BossDirectionIndicator.cs` · `BossBomb.cs` · `BossBombState.cs` · `BossWells.cs` · `BossWellsState.cs` · `IBossChargeSequence.cs` · `BossChargeSequence.cs` · `BossChargingPylon.cs` |

**폐기 4파일 삭제 완료** (`BossBase`·`BossState`·`BossBasicAttackType`·`BossBasicAttackChoice`) — 어셈블리에서 사라진 것까지 확인.

### 🔴 base(공유 자산) 변경 4건 — 전부 additive, 기존 11종 기본값 유지

| 파일 | 추가 | 왜 |
|---|---|---|
| `MonsterBase` | `ChaseSpeedMultiplier` | 페이즈 이동속도. **`SeekBoss` 분기에서만** 곱해진다 |
| `MonsterBase` | `AutoHitReactions`(보스만 false) | base 의 자동 피격 반응 **3종**(Hit·그로기누적·Knockback)을 끈다 |
| `MonsterBase` | `ForceHitReaction(duration, groggyAfter)` | `SetState`·`EnterHit` 이 private 이라 **보스가 `Hit` 에 들어갈 방법이 없었다** |
| `MonsterMeleeAttack` | `SetColliderInfo`/`ColliderInfo` | 공격별 히트박스 앵커 스왑 |
| `HitFlash` | `SetBaseTint`/`ClearBaseTint` | 카운터 색이 피격 플래시에 안 지워지게 |
| `AoeTelegraph` | `ShowGrowing` | 점프 착지 예고(시간 성장) |

### ▶ 다음 세션 착수 순서

**1단계 — 애니메이터 상태 3건 (팀장 확정, 이번 세션 미착수)**

| # | 할 일 | 근거 |
|---|---|---|
| ① | `No23.asset` 의 `locomotionState` **`Movement` → `Idle`** | 🔴 그 상태가 컨트롤러에 **없다.** 공격 상태가 `hasExitTime: false` 라 복귀 CrossFade 가 실패하면 **보스가 첫 공격 후 굳는다** |
| ② | 컨트롤러에 **`getowned` 상태 추가** | 클립(`Boss_23_getowned01/02`)은 있는데 상태가 없다. 카운터 리액션이 재생될 곳이 없다 |
| ③ | 컨트롤러에 **점프 체공·착지 상태 추가** | `Leap` 하나뿐이고 BlendTree 0개다. 클립은 `Boss_23_jumping`·`Boss_23_landingattack` |

실측한 실제 상태 이름 — **문서에서 베끼지 말고 이 목록을 쓸 것**(교훈 #63):

```
23호 (18):  Arrive Break Charging DashAttack Dead Grab Groggy GroggyEnd GroggyStart
            Holding Idle Leap LeftHook Rage RightHook Throw Uppercut Walking
Wells (5):  Die Groggy Idle Jump Throw
```

**2단계 — `No23.asset` 저작.** 아래 표대로 채우면 `ValidateContract` 가 통과한다.

| 필드 | 현재 | 고칠 값 | 안 고치면 |
|---|---|---|---|
| `archetype` | 0 (Melee) | **Boss** | 🔴 선택기가 **아예 안 돈다**(`LogError`) |
| `locomotionState` | `Movement` | **`Idle`** | 🔴 첫 공격 후 **굳는다** |
| `attackTrigger` | `Attack` | **비움** | `LogWarning`(보스는 CrossFade 경로) |
| `maxGroggyCount` | 3 | **5** | 카운터 3회에 Break |
| `groggyDuration` | 3 | **2** | 확정 스펙과 불일치 |
| `attackDuration` | 0.9 | **상향** | 데드락 타임아웃이라 Jump 체인에 부족 |
| `animatorStateName` ×6 | 전부 빈값 | `LeftHook`/`RightHook`/**`Uppercut`**/`Grab`/**`Leap`**/`DashAttack` | 🔴 애니 0 (`LogError` ×6). ⚠️ enum 이름 ≠ 상태 이름 2개 |
| **행 2개 추가** | 없음 | `ChargeSequence`·`RageDash` (**weight 0**) | 🔴 페이즈 통과 시점에 `LogError` |
| `grabHoldState`/`grabThrowState` | 빈값 | `Holding`/`Throw` | Hold·Throw 애니 없음 |
| `jumpHoverState`/`jumpLandingState` | 빈값 | 1단계 ③에서 만든 상태명 | 체공·착지 애니 없음 |
| `hitReactionState` | `getowned` | 1단계 ②에서 만든 상태명 | 카운터 리액션 없음 |

⚠️ **코드 기본값은 새 애셋에만 적용된다**(교훈 #22/#55) — 기존 `No23.asset` 은 인스펙터에서 직접 고쳐야 한다.
⚠️ **행 순서 = 쿨다운 슬롯 번호다.** 중간 삽입 금지, 끝에만 추가.

**3단계 — 프리팹 조립** (정본 §8 체크리스트)

| 대상 | 준비물 |
|---|---|
| **보스** | 루트 `Enemy(8)` / **`Hurtbox` 자식 `EnemyHurtBox(14)`**(없으면 안 맞는다) / 공격 앵커 `Weapon(12)` / **`NetworkAnimator` 제거** / `BossDirectionIndicator` + **투명 URP Unlit 머티리얼 1개** / `BossCounterTelegraph`(선택) / `BossChargeSequence` / `BossWells`(+손 소켓) / ⚠️ Relay·HitFlash 는 **붙이지 말 것**(런타임 자동 부착) / **여기서 `BehaviorGraphAgent` OFF**(R5) |
| **폭탄** | `NetworkObject`+`NetworkRigidbody`+`Rigidbody`(**useGravity on**/`FreezeRotation`/`ContinuousDynamic`) + solid Collider + `Hurtbox`(**ownerUnit 비움** → `IAttackReceiver` 폴백) + `BossBomb` + NetworkPrefabs 등록. ⚠️ **정지해도 논키네마틱 유지**(재우면 당구가 안 된다) |
| **장판** | `NetworkObject` + `AreaZone` + 비주얼 자식(로컬 XY 지름 1) + 레이어 **`HazardArea(9)`** + NetworkPrefabs 등록 |
| **송전탑** | `bossroom.prefab` 의 `Env_Mv_bosscharger_upper` **4개**를 레거시 `ChargingObject` → **`BossChargingPylon`** 으로 교체 |
| **프로젝트 설정** | 🔴 충돌 매트릭스에서 **폭탄 레이어 ↔ 유닛 레이어 물리 응답을 끊을 것**(유닛 감지는 트리거로 하므로 폭발은 산다) |
| **애니 이벤트** | `OnAttackHit`/`OnAttackEnd`(exitTime 0.05~0.1 앞) — 🔴 **SVN 커밋**(`SK_23.fbx.meta`) |

⚠️ Wells 클립 이벤트 이름은 **fbx 에 이미 박혀 있다** — `ThrowBombEvent`/`BombDestroyEvent`. 바꾸면 조용히 무시된다.

**4단계 이후** — S5 Dash(은희 머지 후 호출 3줄) → S2 어퍼 에어본(가능해짐) → **어색한 것들 수정**(팀장 목록 대기) → MPPM 검증 → 레거시 `Enemy/Boss`·`8.BehaviorTreeGraph` 삭제

### 은희 의존 — ✅ **R1·R2 둘 다 종결. `development` 에 머지 완료**(`a75398c`, 2026-08-07)

브랜치 `feature/InterruptSkill-CarrySocket` — `feature/maprendering` 이 아니라 **공통 조상 `cbb51b1`**
(PR #10 머지 지점)에서 갈라졌다. 회신 문서 = `Docs/tech/player-interrupt-restrained-handoff.md`.
**합의한 계약대로 왔다 — 수정 요청할 것 없음.** 코드에서 대조한 결과:

| 계약 | 실물 |
|---|---|
| R1 인터럽트 식별자 | `AttackInfo.isInterruptAttack` (bool 플래그, enum 아님) |
| R2 `Restrained` | `RestraintMode { Carry = 0, Push = 1 }` |
| Push 만 슈퍼아머 거부 + `bool` 반환 | `PlayerStateController.cs:149` |
| **S4 Grab 무수정 보장** | `BeginGrabbedByInstigator`/`EndGrabbedByInstigator` **래퍼 유지** |

**내가 할 일 2건**
- `TwentyThreeBoss.IsInterruptAttack` 의 `isGroggyAttack` → **`isInterruptAttack`** (한 단어).
- **S5 Dash 착수 가능** — `BeginRestrainedByInstigator(gameObject, RestraintMode.Push, frontOffset)`.

🔴 **머지 충돌은 `MonsterBase.cs` 딱 1파일**(양쪽이 동시에 건드린 유일한 파일). 해소 = 둘 다 살리기
(이름은 은희 쪽, `if (!AutoHitReactions) return;` 는 보스 쪽, **조기 반환이 먼저**).
추가로 내가 삭제한 `BossBase.cs` 를 은희가 리네임 때문에 건드려서 **delete/modify 충돌**이 뜬다 →
**삭제를 채택.** `AttackType` 축소(`None/Default/Skill`)는 내 코드에 무영향(`Default` 만 쓴다).
- 🔴 **머지 충돌 1곳**: `MonsterBase.TakeDamage` 의 `isGroggyAttack` 줄. 해소 = **둘 다 살리기**
  (이름은 은희 쪽, `if (!AutoHitReactions) return;` 는 보스 쪽, **조기 반환이 먼저**).
  순서는 은희 → `development` → 보스가 받는 방향(충돌 면적이 작다).

### ⚠️ 이 세션에 확정·정정된 것 (상세는 PLAN 링크)

- **뒤집힌 것 3건**: ① "돌진 = 매 틱 넉백 재적용" → **`Restrained.Push`**(`Unit.Knockback` 은 duration 없는 임펄스 1회라 누적되면 플레이어가 튀어나간다) ② **슈퍼아머로 돌진 버티기 = 의도**(기획 회의) → `Push` 에 슈퍼아머 검사 + `bool` 반환 요청. 슈퍼아머면 **밀림✕/기절✕/데미지○** ③ **"플레이어 CC 경로 없음"은 절반 틀렸다** — 상태이상은 `Unit.StatusEffects.Apply` 로 **오늘 가능**, 막힌 건 **변위뿐** (→ PLAN §5.1 G1 정정)
- **"도달"은 송전기 실패 조건이 아니다** — `ReachEvent` 는 "상승 완료"라 모든 기둥이 반드시 도달한다. 실패는 **제한시간 초과 단독** (→ PLAN §5.11)
- **JumpAttack 의 빨간 장판은 예고 표시(`AoeTelegraph`)이지 `AreaZone` 이 아니다** — 섞지 말 것

### 🔴 알아 둘 것 (함정)

- **`git status` 가 `Docs/` 변경을 숨긴다** — `core.fsmonitor` + `Docs` 정션. **`git -c core.fsmonitor=false` 로 커밋할 것**
- **컴파일 판정은 MCP 응답이 아니라 산출물로** — `Assembly-CSharp.dll` mtime > 최종 소스 + **dll 안에 새 타입 실존**(`grep -a`). MCP 의 `success:true` 는 접수 확인일 뿐이고, `Editor.log` 엔 직전 실패의 에러가 남는다(교훈 #61)
- **보스 검증은 MPPM 2인 이상에서만** — 오프라인/미스폰이면 `CanWrite = IsSpawned && IsServer` 라 **상태이상이 안 걸린다.** 단독 Play 로 "기절이 안 된다"를 버그로 오진하지 말 것
- **파생에 `Update()` 를 선언하면 `MonsterBase.Update` 를 가려 FSM 이 통째로 멈춘다**
- **enum 값 추가는 끝에만** — `MonsterArchetype`·`BossAttackId`·`AreaZoneType` 전부 SO 에 정수 직렬화
- **애니·앵커 접근이 graceful 이라 이름 오타가 무증상** — 죽은 설정값 9건 실재(교훈 #59). `ValidateContract` 가 스폰 시 잡는다
- **Codex 는 숫자·경로를 지어낸다** — 결론만 채택하고 재확인

---

## ▶▶ 현재 인수인계 (2026-08-07 · 보스 FSM 지원 2건 — 인터럽트 식별자 + 캐리 소켓)

작업 세션: **은희(Claude)**. 워크트리 `C:\UnityProject\MainProject-WorkTree`,
브랜치 `feature/InterruptSkill-CarrySocket` (base `MainProject/development` `6dbc1c34a`).
요청 출처: 경석 인계문 `Desktop\handoff-player-carry-socket.md` (기한 8/7 17:00).

**수정 중 (동시 편집 금지)**: `Assets/1.Scripts/Unit/Weapon/BaseAttack.cs`(enum 1줄) ·
`Assets/1.Scripts/Unit/ICarrySocketProvider.cs`(신규) · `Assets/1.Scripts/Enemy/Boss/GrabController.cs`(인터페이스 구현) ·
`Assets/1.Scripts/Player/{Player.cs, PlayerStateController.cs}` ·
`Assets/1.Scripts/Player/Skill/{PlayerSkillBase.cs, FirstMeleeMainSkill.cs, FirstMeleeInterruptSkill*.cs(신규)}` ·
`Assets/2.Prefabs/Player/Paladin/Paladin.prefab`

설계·완료조건은 [PLAN.md](PLAN.md) 최상단 참조. 이번에 확립된 계약만 적는다:

- 🔴 **인터럽트는 `AttackInfo.isInterruptAttack`이 싣는다**(기존 `isGroggyAttack` 개명).
  `AttackType`은 "어느 출처가 쐈나"라 인터럽트와 **직교**한다 — enum 값으로 넣으면 안 된다.
  **플래그는 하나뿐이고, 소비 방식은 수신측이 정한다**: 몬스터/중간보스 = `maxGroggyCount` 누적→그로기,
  보스 No.23 = 카운터 창·정면 각도(경석). `Docs/design/level-system.md:70`의 분담과 같다.
- 🔴 **`AttackType` = `{None=0, Default=1, Skill=2}`로 축소됐다**(Q/E/R 제거 — 구분해 읽는 코드가 없었다).
  `BaseAttack.attackType`·`Bomb.attackType`이 `[SerializeField]`라 **정수값 0·1은 고정**이다.
  특히 **`Bomb.attackType=1`(Default) = 폭탄은 평타에만 반응한다** — 값이 밀리면 이 기믹이 조용히 뒤집힌다.
  **값은 끝에만 추가할 것.**
- **`BaseAttack`엔 인터럽트 저작 토글이 없다.** 켜는 주체는 스킬뿐이고 스킬은 `BaseAttack`을 안 탄다.
  적 공격도 인터럽트를 걸어야 하면 그때 `[SerializeField] bool` + 생성자 인자를 되살린다(3줄).
- 🔴 **플레이어 스킬은 `BaseAttack`을 타지 않는다.** `AttackInfo`를 직접 만들어 `Hurtbox/Unit.ReceiveAttack`을
  부른다(서버 전용 경로). 인계문의 "BaseAttack.attackType을 지정" 전제는 이 코드베이스에 없는 경로였다.
- 🔴 **`PlayerActionState.Grabbed` → `Restrained`.** 서버가 플레이어 위치·입력을 잠시 통제하는 상태를
  **한 곳으로 묶었다**: `RestraintMode{Carry=잡기(소켓 종속), Push=돌진 밀기(시전자 정면 추종)}`.
  `PlayerGrabbedState`→`PlayerRestrainedState`, `GrabInteractionContext`→`RestraintContext`,
  `IGrabInteractionReceiver`→`IRestraintReceiver`. (원 요청이던 캐리 소켓 일반화 `ICarrySocketProvider`는 **폐기**.)
  - **`Push`는 소켓이 필요 없다** — 시전자 루트가 `NetworkTransform`으로 복제되므로 `position + forward × offset`이
    오너 클라에서도 성립한다. Y는 진입 시점 값으로 고정(피벗 높이 차이로 뜨거나 잠기는 것 방지).
  - **`Push`만 슈퍼아머를 거부**하고 `Carry`는 안 한다(넣으면 보스 Grab 체인 회귀). 판정은 **서버 진입에서만** —
    오너가 다시 판정하면 복제 지연 시 상태가 갈린다.
  - **`BeginRestrainedByInstigator`의 `bool` 반환이 계약이다** — 시전자는 이 값으로 후처리를 가른다
    (실제로 밀린 대상만 기절). `BeginGrabbedByInstigator`는 `Carry` 래퍼로 유지 → `GrabController` 무수정.
  - 🔴 **`followTarget == null` 널 허용을 깨지 말 것** — 위치 추종만 건너뛰고 물리 위임·입력 차단은 유지된다.
    보스에 잡기 소켓이 아직 없어서 "제자리에 붙잡힘"이 이 성질로 성립 중이다.
- 🔴 **`Unit.Knockback`은 임펄스 1회다** — duration 개념이 없다. `AttackInfo`의 `knockbackDuration`·
  `staggerDuration`은 `MonsterBase`만 소비하고 **플레이어 수신 경로는 무시**한다. 보스 돌진이 넉백 대신
  `Restrained.Push`로 간 이유다.
- 🟡 **잡기와 돌진 캐리는 `PlayerActionState.Grabbed` 하나를 공유한다.** 캐리 중 재진입은 조용히 거부되고
  (`CanReceiveGrab`), `EndGrabbedByInstigator()`는 시작 주체를 구분하지 않는다. 보스 1기 기준 무해.
- 🟡 **단죄의 방패는 이번에 처음 구현됐다** — 그 전까지 우클릭은 `PlayerInterruptState`(전방 돌진, **데미지 0**)였다.
  거동 스펙이 `Docs/design/`에 없어 "짧은 전방 강타 + Interrupt 태그"로 가정했다(PLAN 「명시적 가정」).
  판정 타이밍은 **애니 클립 수정 없이** SO 타이머(`hitDelay`)로 잡고, Hit 애니 이벤트가 나중에 심어지면
  그쪽이 우선하되 **1회만** 발동한다.

---

## 이전 인수인계 (2026-08-05 · 지연 체력바 — Claude → Codex 위임)

작업 세션: **은희(Claude → Codex 위임)**. 레인 `dash` = `C:\UnityProject\MainProject`,
브랜치 `feature/DelayedHealthBar` (base `Convayor-V2`).

**Codex가 수정 예정 (Claude·타 작업자 동시 편집 금지)**:
`Assets/1.Scripts/UI/Combat/DelayedHealthBar.cs`(신규) ·
`Assets/1.Scripts/Unit/Unit.cs`(이벤트 2줄) ·
`Assets/1.Scripts/UI/Combat/PlayerHealthHUD.cs` · `Assets/1.Scripts/UI/Combat/BossHealthHUD.cs`

설계·완료조건은 [PLAN.md](PLAN.md) 최상단 참조(중복 기재하지 않음). 요지만:
피격 시 잔상 바가 옛 HP에 0.4초 머문 뒤 고정 속도로 따라 내려온다. 피해 조각을 `Queue`로
붙잡고 `maxHeldHits=5` 초과 시 오래된 것부터 놓아준다(보스는 홀드 리셋 off) — 지속 피해에
잔상이 영구 고착하는 것을 막는 장치다.

프리팹 배선(`CombatHUD.prefab` · `BossHealthHUD.prefab`에 잔상 Image 추가·연결)은 **은희가
Unity에서 직접** 한다. Codex는 `.cs` 4개만 건드리고 프리팹·씬·`.meta`는 손대지 않는다.

⚠️ `CLAUDE.local.md`의 레인 표는 낡았다 — `soul`/`MainProject-BeaverLobby` 워크트리는 없고,
현재 살아있는 워처는 `dash`(MainProject)와 `fd`(MainProject-WorkTree, `feature/FloatingDamage`) 둘이다.

---

## 이전 인수인계 (2026-08-06 · 렌더링 룩 A/B + 픽셀레이트 + 어비스 복구 — `37a338f` **push 완료**)

브랜치 `feature/maprendering` 원격 동기화 완료(ahead 0). 커밋 5개:
`88f43cb` 픽셀레이트 · `3f7c43d` 룩 A/B 토글(F9) · `062308a` DoF 비활성 ·
`a239923` 아트 가이드+계획서 · `37a338f` 어비스 복구.

**닫은 것**

- **픽셀레이트** — 합성 패스 UV 양자화로 구현(패스 추가 0, 각 0.01ms). 블러와 **독립 반경**
  (`pixelateRegionScale`)이라 픽셀 범위만 좁힐 수 있다. 룩 A·B **공통**.
- **룩 A/B 토글(F9)** — A = 채도 살림 / B = 디밍 + 저채도 + **시야 차폐**.
  토글은 `dimEnabled`·`losEnabled` **필드만** 오간다(컴포넌트를 끄면 어비스가 함께 죽는다).
- **DoF 비활성** — 배경 흐림은 마스크 블러 단독. 삭제 아니라 `active: 0`(튜닝값 보존).
- **어비스 물안개 복구** — 포그 게이트에 묶여 **렌더되지 않고 있었다**(원인 3곳).

**문서**: [Docs/design/look-ab-tuning.md](Docs/design/look-ab-tuning.md)(아트용) ·
`PLAN-vision.md` §8.11~8.13 · 볼트 `Programming/Setting/렌더링 설정 종합 정리` ·
볼트 `Art-Planning/Setting 사용법/룩 A-B 비교와 설정 조절`.

**남은 육안 검증 2건** — ① 픽셀 영역 배율(`pixelateRegionScale` 1.242) 적정성
② **어비스 tint 는 8~19% 로 은은해 구멍 근처에서만 보인다** — 확인 필요.

**미커밋으로 남은 것** = `0.BootStrapScene` · `Paladin.prefab` · `TwentyThree.prefab` ·
`MultiplayerManager.asset` 4개인데 **전부 내용 변경 0(줄바꿈 노이즈)** 이다.
(이전 인계에 있던 "Paladin PlayerInput 되돌릴 것" 항목은 현재 diff 0 이라 **해소됨**.)

**🔴 이 세션의 반복 패턴 3건 — 다음에 의심할 것**

1. **C# 기본값을 바꿔도 이미 직렬화된 애셋은 안 바뀐다.** `pixelBlockSize` 4→16 을 코드에서만
   올려 화면이 그대로였다. 필드 기본값을 바꿀 때는 **애셋 파일의 실제 값을 열어 확인**할 것.
2. **한 계통을 다른 계통의 게이트 안에 두지 말 것.** 어비스가 포그 안에 있어서 "포그를 껐다"가
   무관한 기능을 조용히 껐다. 매니저 컴포넌트의 `enabled` 로 한 기능만 토글하는 것도 같은 실수.
3. **로그의 실패 기록이 현재 상태가 아닐 수 있다.** 셰이더 에러가 중간 편집 상태의 기록이었다 —
   로그를 믿지 말고 **현재 파일과 강제 재임포트로 재판정**할 것.

**⚠️ 신 Input System 은 에디터에서 Game View 포커스를 따른다.** 포커스 없으면 F9 가 안 먹는다.
그래서 `LookToggle` 은 콘솔에도 로그를 남긴다(입력 도달 여부와 적용 결과를 갈라내기 위함).

---

## 이전 인수인계 (2026-08-05 · 렌더링 조명·포스트프로세싱 — 브랜치 `feature/maprendering`)

작업 세션: **경석(Claude)**. 브랜치 `feature/maprendering`
(= `development` `cbb51b1` 기준으로 분기, 원격 push 완료).

**수정 예정 영역 — 렌더링/조명/포스트프로세싱.** Codex·팀원 동시 수정 주의 대상:
`Assets/99.Settings/*`(URP 애셋·볼륨 프로파일) · 툰 셰이더/머티리얼 ·
`4.MapScene`·`bossroom` 의 조명·볼륨 오브젝트. 세부 파일은 범위 확정 후 여기 추가한다.

### 목표 — "레퍼런스 이미지 수준의 플레이 화면"

팀장이 AI 생성 레퍼런스 이미지를 **목표 룩**으로 제시했다(2026-08-05, 채팅 첨부).
탑다운 쿼터뷰 고정 / 셀셰이딩 캐릭터 + 회화풍 배경 / 부드러운 접지 그림자 +
따뜻한 국소 조명(가로등·창) / 물·금속 반사 / 원경 디포커스 / 채도 높은 소품.
이미지에는 HUD 배치안(특성·강화·스탯 육각 슬롯, 하단 스킬 슬롯, HP/MP 바, 남은 시간,
우상단 미니맵)도 손으로 얹혀 있으나 **HUD 는 이 세션 범위와 별개 트랙**이다.

🔴 **레퍼런스 이미지는 아직 채팅에만 있다 — `Docs/design/` 에 저장할 것.**

- **카메라 조절은 완료** — FOV 52→34 / Priority 100 (`0522731`, development 에 포함).
  이번 세션은 카메라가 아니라 **셰이딩·조명·포스트프로세싱**이다.
- 승인된 계획서는 `PLAN.md` 가 아니라 **`PLAN-vision.md` §7** 이다(§4 단계 2~3 재개).
- 선행 참고: 툰셰이더 현황(`e5cc012`) · 벽 차폐/투명화 · `Docs/tech/fog-system.md`.

### 2026-08-05 마감 시점 기록 (`317a2a1`) — ✅ 이후 전부 push 됨

**코드 작업은 일단락됐고 남은 것은 육안 검증이다.** 컴파일 0에러/0경고, 콘솔 에러 0건,
`svn status Assets/50.Art` missing 0건, 배선 14개 항목 전수 확인 완료.

**이번 세션에 닫은 것**

| 닫은 것 | 커밋 |
|---|---|
| 그레이딩 LDR→HDR + 톤매핑·화이트밸런스·컬러조정·비네트 도입 | `77e6063` |
| 벽 투명화 복원 + **포그 매니저 OFF** | `3b2d07f` |
| **카메라 포스트프로세싱 활성** + TAA | `0dfec30` |
| 벽 투명화 대상을 Stage1 로 한정 + AA·디더 토글 분리 | `82b079b` |
| ProfilerHUD 배치 + DoF Bokeh | `421b844` |
| **화면공간 마스크 블러** 신설(셰이더·피처·컨트롤러·설정) | `e07d2ac` |
| 마스크 블러 `size.x` 무시 수정 + 성능 실측 기록 | `959b8c2` |
| HUD 마커를 마스크 블러 4패스로 교체 | `317a2a1` |

**AA 결정 = SMAA** (2026-08-05 팀장 판단). TAA 와 큰 차이를 못 느꼈고,
**고스팅이 없는 쪽이 맞다**는 판단이다. 대시 잔상은 TAA 부산물이 아니라 **대시 전용
VFX 로 따로 넣는다** — 그래야 통제가 된다. `m_Antialiasing: 2` 적용됨.
🔴 SMAA 에서는 `WallOcclusionSettings.animateDither` 를 켜면 안 된다(기본값 OFF, 유지).

**두 룩을 키 하나로 비교 (팀장 지시, 2026-08-05) — ✅ 2026-08-06 완료(위 인수인계 참조)**

지금은 채도를 살리는 방향으로 왔지만, 원래는 **어둡고 디밍이 들어간 상태**였다.
그쪽에 **픽셀레이트 + 블러**까지 얹은 것과 지금 화면을 **키 입력 한 번으로 전환**해
직접 비교할 수 있게 만든다.

| | A (현재) | B (비교 대상) |
|---|---|---|
| 채도·톤 | 살림 | 저채도 |
| 디밍 | 없음 | 있음 |
| 블러 | 마스크 블러 | 마스크 블러 |
| 픽셀레이트 | 없음 | **있음(신규)** |

착수 전 알아 둘 것:

- 🔴 **런타임에 `volume.sharedProfile` 값을 바꾸면 에디터에서 애셋이 영구 수정된다.**
  Play 를 끝내도 남는다. 씬에 Volume 을 2개 두고 weight 를 토글하거나
  `volume.profile`(런타임 클론)을 쓸 것.
- 🔴 **디밍을 되살리려고 `FogManager` 를 통째로 켜면 시야 제한(LoS)도 같이 돌아온다.**
  `dimEnabled` 와 `losEnabled` 는 독립 토글이므로 디밍만 원하면 `losEnabled: 0` 으로 둔다.
  (현재 값: `fogEnabled 0` / `dimEnabled 1` / `losEnabled 1`, 컴포넌트 자체가 OFF)
- 두 룩이 갈리는 축이 4개다(볼륨 값 · 디밍 · 마스크블러 설정 · 픽셀레이트).
  **"룩 프리셋" ScriptableObject 하나로 묶어 통째로 스왑**하는 편이 토글이 단순해진다.
- 픽셀레이트는 마스크 블러와 같은 계통의 풀스크린 패스다 — `MaskBlurFeature` 구조를
  그대로 재사용할 수 있다. ⚠️ **순서를 정해야 한다**: 블러→픽셀레이트면 블록 경계가
  또렷하고, 픽셀레이트→블러면 블록이 뭉개진다. 원하는 그림이 어느 쪽인지 먼저 결정.
- 토글 키는 **F8 을 피할 것**(ProfilerHUD 가 쓴다). 이 프로젝트는 신 Input System 이다.

**그 외 남은 것**

1. **Play 육안 검증** — 마스크 블러의 `feather`(0.35)·`blurStrength`(1)·`roundness`(4)
   는 전부 추정값이고 화면으로 확인한 적 없다.
2. **저사양 성능 재측정** — 팀장 PC 는 GPU 2.89ms 로 여유롭지만 고사양 기준이다.
   비싸면 `downsampleShift` 1→2 → `blurStrength` 하향 → 패스 축소 순.
3. 남은 격차 = **국소 조명**(가로등·창). 라이트맵 방향이 정해져야 착수 가능(아래 미해결).

**🔴 미해결 / 결정 대기**

- **라이트맵 베이크가 현재 맵 구조와 충돌한다.** `MapContentSpawner.cs:62` 가 존 레이아웃
  프리팹(바닥·벽 포함)을 런타임 `Instantiate` 하는데, 라이트맵 데이터는 씬 렌더러에
  직렬화되므로 런타임 생성물은 받지 못한다. 씬의 static 플래그도 0개다. 게다가 슬롯
  위치가 시드마다 셔플된다. 선택지 = 프리팹 라이트맵 베이크 / 라이트 프로브·APV /
  Stage1 만 굽고 생성물은 실시간. **조명 단계 전에 방향 결정 필요.**
- **퓨즈박스가 어둡다 — 미해결.** `Level_wall_hallway.prefab`(Stage1 에 13개)이 쓰는
  `MA_prop03` 은 `prop03_basecolor` + `pipe_basecolor` 두 장인데 오클루전 변종은 한 장만
  가져간다. 변종 14개 중 4개가 3~5장 → 1장으로 붕괴하고, **13개는 노멀맵을 잃었다**
  (노멀 텍스처 5개가 `textureType: 0`=Default 로 임포트돼 `IsNormalMap()` 이 false 를
  반환하기 때문 — 팀장이 재임포트 예정). 근본 해결은 변종 머티리얼을 없애고 원본
  ShaderGraph 에 디더를 심는 것. `50.Art` = SVN 이라 단일 담당 필요.
- ⚠️ **`ProfilerHUD` 는 제출 빌드 전에 MapScene 에서 제거할 것.** 클래스가
  `#if UNITY_EDITOR || DEVELOPMENT_BUILD` 로 감싸져 있어 릴리스 빌드에서 missing script 가 된다.
- ✅ **해소됨 (2026-08-06 확인) — 아래 되돌리기 명령은 실행하지 말 것.**
  `Paladin.prefab` 의 `git diff` 는 현재 **내용 변경 0**(줄바꿈 노이즈만)이다. 아래는
  당시 기록으로만 남긴다.
  <br>~~`PlayerInput` 이 `m_Enabled: 0 → 1` 로 켜져 있다(미커밋).~~
  단, **규칙 자체는 유효하다**: `Player.cs:146` 주석대로 프리팹 기본값은 비활성이 맞고,
  `EnableLocalInput()` 이 로컬(오너/오프라인)만 켠다 — 원격 클론이 디바이스 페어링을 시도해
  "Cannot find matching control scheme" 경고를 내는 것을 막기 위한 설계다.
  **`PlayerInput` 을 프리팹에서 켜 두면 안 된다.**

**미커밋으로 남긴 것** — 전부 팀장이 인스펙터에서 튜닝 중인 값이라 손대지 않았다:
`Global Volume Profile`(비네트 색 warm→진회색, 강도 0.606 / 대비 -3.7 / 채도 -11.6 —
내가 넣었던 +12·+18 이 과해서 되돌린 것) · `MaskBlurSettings`(`size.x` 0.65) ·
`Paladin.prefab`(위 항목, 되돌릴 것) · 줄바꿈 노이즈 2건.

**이 세션에 확립된 것**

- 🔴 **"적용했는데 화면이 그대로"가 세 번 나왔고 원인이 매번 달랐다.**
  ① `VolumeProfile.Add<T>()` 는 서브에셋 등록을 안 해 저장 시 `{fileID: 0}` 널이 된다
  (게다가 널이 되기 전 메모리 인스턴스를 `TryGet` 이 찾아 "이미 있음"으로 오판한다 —
  판단 기준은 존재 여부가 아니라 `AssetDatabase.Contains`).
  ② **카메라 포스트프로세싱이 꺼져 있었다**(`m_RenderPostProcessing: 0`).
  ③ `MaskBlurSettings.ResolveSize` 가 `size.x` 를 y 에서 유도해 인스펙터 값을 버렸다.
  **공통 교훈: 도구가 "성공" 로그를 찍어도 산출물 파일을 직접 열어 확인할 것.**
- 🔴 **게임플레이 카메라는 씬에 없다.** `4.MapScene` 의 Camera 는 0개이고
  `CameraTargetSwitcher.cs:132` 가 `CameraSwitcher.prefab` → `MainCamera.prefab` 을 런타임
  `Instantiate` 한다. 그 프리팹은 `CamaraScene`·`PlayerScene`(테스트 씬)에서만 직접 쓰이므로
  **씬만 훑으면 실제 카메라 설정에 도달하지 못한다.**
- ⚠️ **`FogManager` 는 이름과 달리 포그가 범인이 아니다.** 씬 값이 `fogEnabled: 0` 이라
  포그는 안 그려지고 있었고, 화면을 어둡게 만든 것은 `dimEnabled: 1` + `losEnabled: 1`
  (`losMaxDist: 4`)이다. 지금 OFF 라 **먼 거리 적이 전부 보인다** — 게임플레이 영향 있음.
- ⚠️ **탑다운에서 DoF 로 화면 외곽을 흐리게 할 수 없다.** DoF 는 깊이로 판단하는데
  좌우 외곽은 중앙과 깊이가 같다. 그래서 화면공간 마스크 블러를 새로 만들었다.
- ⚠️ **벽 투명화의 디더 무늬는 품질 결함이 아니라 의도된 비용 절충**이다(반투명 블렌딩
  대신 `clip()` 으로 불투명 큐 한 패스). 보기 싫다고 끄지 말 것 — 끄면 벽 뒤 플레이어가
  그냥 안 보인다. 디더+TAA 로 녹이는 시도는 **실패했다**(TAA 분산 클램프가 디더 변동을
  기각해 뿌연 얼룩이 된다). 2안 = MSAA + `AlphaToMask`.
- ✅ **렌더러 피처를 코드로 추가할 때는 `AddObjectToAsset` + `m_RendererFeatureMap` 정합
  확인이 필수다.** 맵은 features 개수와 길이가 맞아야 한다(long 1개 = hex 16자).
  URP 의 `ValidateRendererFeatures()` 가 재계산하지만 `internal` 이라 리플렉션으로 깨웠다.
- ✅ 셰이더는 반드시 **직렬화 참조**로 물릴 것(`MaskBlurFeature._shader`). `Shader.Find`
  만으로는 빌드에서 스트립된다(미니맵 전례).

### 브랜치 정리 (세션 시작 시)

- `feature/map-player-merge` 삭제(`8e5a2ae`) — `origin/development` 에 전부 포함된 것을
  확인한 뒤. `ahead 2` 는 **자기 원격 ref 가 낡아서 생긴 착시**였다.
- 로컬 `development` 를 `origin/development`(`cbb51b1`) 로 갱신 후 거기서 분기.
  `fix/AlphaAlert` 에는 development 에만 있는 커밋 12개가 빠져 있었다(ConveyorBelt 정리,
  진입점 단일화, 미니맵 머티리얼 배선, 루프백 바인딩 수정, 50.Art gitignore 통일 등).
- 🔴 **git↔SVN 하이브리드 함정 — "git 추적 해제" 커밋을 가로지르는 체크아웃은 SVN 소유
  파일을 디스크에서 지운다.** development 의 `5cd384f`(SVN 소유 아트 `.meta` 140건 추적 해제)를
  넘어오면서 git 이 `TestAssets/Temp_Images` 아래 `.meta` 5개를 **삭제**했다. 그대로 Unity 를
  켰으면 GUID 재발급으로 참조가 깨졌을 것이다. SVN 이 `!`(missing) 로 잡고 있어 `svn revert`
  로 복구, GUID 가 git 원본과 일치하는 것까지 확인했다.
  **브랜치를 갈아탄 뒤 Unity 를 켜기 전에 아래를 볼 것:**
  `svn status Assets/50.Art | Select-String "^!"`

---

## 이전 인수인계 (2026-08-05 · MCP 무한대기 해소 + 네트워크 권한 결함 2건, **PR #10 머지 완료** `cbb51b1`)

작업 세션: **경석(Claude)**. `fix/AlphaAlert` → `development` 머지 완료, 원격 동기화됨.
워킹트리에 남은 것은 **줄바꿈 노이즈 1개**(`MultiplayerManager`)뿐이다 — 아래 07-31 항목의
"내용 변경 0" 부류와 같다. 커밋하지 말 것.

### 이번에 닫은 것

| 닫은 것 | 커밋 |
|---|---|
| **결과 씬 전환 메시지에 송신자 검증** — 클라가 named message 로 호스트를 결과 화면으로 끌고 갈 수 있었다 | `6d34ea1` |
| **대시 서버 검증에 사망/영혼 배선** — 판정 입력이 `false` 하드코딩이라 죽은 플레이어 대시를 못 막았다 | `66d99af` |
| **Unity MCP 무한대기 해소 + 브릿지 런처** (핀 `cf61cc5`→`2ea969e`) | `58f3c19` · `effdf12` |
| 툰 룩 동결값 — 카메라 FOV 52→34 / Priority 100 / Paladin 툰 4값 | `0522731` |

두 결함은 Codex·Claude 교차 코드리뷰에서 나왔다. 리뷰 원문·39건 목록은 개인 보관.

### 다음 시작점 — 렌더링 수정 (**새 브랜치에서 작업**)

팀장 지시: 다음 작업은 렌더링 수정이며 **브랜치를 새로 파서** 진행한다. 구체 범위는 다음 세션에
확정. 관련 기존 문서를 먼저 확인할 것 — 툰셰이더 현황(위 07-31 §이번에 닫은 것 `e5cc012`),
벽 차폐/투명화, `Docs/tech/fog-system.md`.

### 이 세션에 확립된 것

- 🔴 **Claude Code 는 프로젝트 `.mcp.json` 이 아니라 `~/.claude.json` 의 user 스코프
  `mcpServers` 를 우선한다.** 둘 다 `unity` 가 있으면 프로젝트 쪽은 무시된다.
  MCP 가 "고쳤는데 그대로"면 **실행 중인 프로세스의 커맨드라인부터 본다**:
  `Get-CimInstance Win32_Process -Filter "name='node.exe'" | Select CommandLine`.
- 🔴 **브릿지 사본을 홈(`~/.unity-mcp/`)에 두지 말 것.** 사본은 패키지가 갱신돼도 조용히 낡고,
  낡았다는 신호가 어디에도 안 뜬다. 경로가 흔들리는 문제는 사본이 아니라 **런처로** 푼다 —
  `.mcp-bridge-launcher.js`(레포 루트, 커밋됨)가 `PackageCache` 를 실행 시점에 탐색하므로
  **앞으로 패키지 핀을 갱신해도 개인 설정을 고칠 필요가 없다.**
- ⚠️ **MCP 무한대기의 원인은 타임아웃 부재가 아니라 응답 id 유실이었다.** 서버가 합성 에러에
  id 를 안 실으면(`id:null`) HTTP 200 으로 나가고, 브릿지는 "정상 응답"으로 처리해
  **자체 타임아웃·재시도가 아예 발동하지 않는다.** 요청-응답 프로토콜에서 무한대기를 만나면
  "응답이 오는가"보다 **"응답의 상관키가 맞는가"** 를 먼저 본다.
- ✅ **MCP 패키지 수정 절차**: `C:\Users\user\unity-mcp-fork` 의 **`optimized` 브랜치**에서
  수정 → push → `Packages/manifest.json` 핀 갱신 → Unity 재시작. `main` 은 이 프로젝트가
  쓰는 계보가 아니다. lock(`packages-lock.json`)도 같이 커밋해야 팀원이 새 버전을 받는다.
- ⚠️ **`MapSceneManager` 만 송신자 검증이 빠져 있었다** — `LobbyUIController:259`,
  `NetworkLoadingFlowController:766`, `NetworkClock:264` 는 이미 하고 있었다. NGO custom
  named message 핸들러를 새로 추가할 때는 이 가드를 기본으로 넣을 것.
- ⚠️ **`PlayerDashController` 는 `PlayerLifeCycleController` 가 없으면 "살아있음"으로 폴백한다**
  (대시 전용 테스트 씬 대응). 프로덕션 프리팹에서 이 컴포넌트가 누락되면 사망 검증이 조용히
  빠지므로, 서버 스폰 경고(`[DashAlert] PlayerLifeCycleController가 없어…`)를 무시하지 말 것.
- 팀원 조치 안내는 볼트 `Core/MCP패키지_포크전환_안내.md`(2026-08-05 최신화)에 정리했다.

---

## 이전 인수인계 (2026-07-31 · 보스 이슈 정리 + 툰셰이딩 + Windows 빌드, `93716e0` — PR #10 로 머지됨)

작업 세션: **경석(Claude)**. 브랜치 `feature/map-player-merge`, origin 대비 **ahead 1**.
워킹트리에 남은 것은 **내용 변경 0 인 줄바꿈 노이즈 4개**뿐이다
(`0.BootStrapScene` · `GraphicsSettings` · `MultiplayerManager` ·
`UniversalRenderPipelineGlobalSettings` — 커밋하지 말 것. `git diff` 가 0줄이면 이 부류다).

### 이번에 닫은 것

| 닫은 것 | 커밋 |
|---|---|
| 툰셰이더 — 캐릭터 조명 독립 / 아웃라인 화면공간 px 고정 / 고정광 월드공간화, Wells·검·방패 툰 적용 | `e5cc012` |
| 보스 HUD 복구 — Paladin 의 CombatHUD 에 `BossHealthHUD` 재부착 + RectTransform 스케일 0 수정 | `ebbbf71` · `7e9c78c` |
| **Wells 폭탄 투척 복구 — 중첩 NetworkObject 제거** (아래 §정정 참고) | `58278e9` |
| `BombLauncher` 무증상 실패 제거 + `_bombController` 수명 대칭 | `88c4772` |
| Q 스킬 도중 사망 시 애니메이터 초기화(`Rebind`+`Update(0)`) | `bae2e98` |
| 부활 시 BT 플레이어 명부(`TargetGroup`/`TotalPlayerNumber`) 갱신 | `7c6ec58` |
| 은희 컨베이어벨트(`6150ee5`) rebase 병합 · SVN r258→r259 | — |
| **Windows 빌드 파이프라인** — `BuildWindowsPlayer` + 빌드 씬 목록 정리 + `productName` 복구 | `93716e0` |

### 다음 시작점 — 몬스터 배치

스포너 기계는 이미 있다: `MapContentSpawner`(마커별 그룹 스폰 구현됨) · `SpawnPoint`
(`AllowedTier` + `MonsterSpawnPoints`) · `MonsterGroupData`(티어/난이도/프리팹/가중치).
남은 것은 **저작·구성** 쪽이다.

### 이 세션에 확립된 것

- 🔴 **Wells 는 자체 `NetworkObject` 를 가지면 안 된다** — NGO 는 프리팹의 중첩
  NetworkObject 를 스폰하지 않는다. 서버 판정이 필요하면 `NetworkManager.Singleton.IsServer`
  를 쓴다(`BombLauncher`·`WellsAnimEvents` 패턴).
- ⚠️ **`Assets/8.BehaviorTreeGraph` 는 열기만 해도 런타임 그래프 RID 가 통째로 재직렬화된다**
  (No.23 = 4,675줄). 의도한 편집이 아니면 `git checkout` 으로 버릴 것. 그래프에 노드를 넣는
  대신 C# 에서 블랙보드를 쓰는 쪽이 diff·머지 비용이 훨씬 싸다.
- ⚠️ **`MonsterArea.asset` 은 고아라 삭제했다**(`58278e9`). Unity 가 BT 그래프를 열 때 다시
  지우려 들 수 있다 — `git status` 에 뜨면 정상이다.
- ✅ **Play 로그는 MCP 콘솔이 아니라 `%LOCALAPPDATA%\Unity\Editor\Editor.log` 로 읽힌다.**
  (2026-07-29 의 "스크린샷으로만 받는다"는 전제는 과했다. 한글은 깨지지만 ASCII 마커로
  검색하면 충분하다 — 이번 폭탄·BT 진단을 전부 이 방법으로 끝냈다.)
- ✅ **Windows 빌드는 `BuildWindowsPlayer` 로 뽑는다.** 에디터를 닫은 뒤 CLI 배치모드:
  `Unity.exe -batchmode -quit -projectPath <proj> -buildTarget Win64
  -executeMethod BuildWindowsPlayer.BuildWindows64 -buildOutput <exe> -logFile <log>`
  (또는 에디터 메뉴 `Build > Windows64 Player (MainFlow)`). 출력은 레포 밖에 둔다 —
  `.gitignore` 에 `/Build/` 규칙이 없다. 첫 빌드 기준 4m41s / 369MB.
- ⚠️ **`Unity.exe` 는 자기를 자식 프로세스로 재실행해서 셸에 exit 0 을 즉시 돌려준다.**
  종료코드로 빌드 성공을 판단하면 안 된다. 판정은 로그의 `[Build] result=` 줄과
  exe 존재로 한다(`MainProject_Data/level*` 개수 = 포함 씬 수).
- ⚠️ **`Assets/Editor/` 는 `.gitignore:79` 로 무시된다.** 팀 공유용 에디터 스크립트는
  `Assets/1.Scripts/Editor/` 에 둘 것(같은 `Assembly-CSharp-Editor` 로 컴파일된다).
- 빌드가 부수적으로 만드는 것: `Assets/99.Settings/PC_RPAsset.asset` 의 `m_Prefilter*` 값과
  `Assets/AddressableAssetsData/link.xml`. 둘 다 재생성물이라 커밋하지 않는다.
- 🔴 **런타임 `Shader.Find` 는 빌드에서 null 이 된다** — 어떤 머티리얼/씬/프리팹도 참조하지 않는
  셰이더는 빌드에서 스트립되기 때문. "에디터는 되는데 빌드만 안 됨"의 대표 원인이다.
  미니맵(`UI/MinimapComposite`)이 이걸로 빌드에서 안 보였다. 프로젝트 자체 셰이더 5개 중
  참조 0 이던 건 미니맵 하나뿐이고 나머지(ToonLit·ToonGlass·WaterDark·FullScreenFog)는 안전하다.
  **커스텀 셰이더는 반드시 머티리얼 에셋 → 인스펙터 참조 체인으로 물릴 것.**
  검증법: 빌드 로그에 `Compiling shader "<이름>"` 이 찍히는지 본다.
- `#if` 전수조사 완료(자체 코드 36건) — M키·미니맵 미표시와 무관했다. 실제로 빌드에서 빠지는 건
  `ProfilerHUD`(`UNITY_EDITOR || DEVELOPMENT_BUILD`) 하나뿐이고 어느 씬에도 안 붙어 있어 무해하다.
  플레이어 어셈블리 define 확인법: `Library/Bee/artifacts/*P.dag/Assembly-CSharp.rsp`.
- ⚠️ **TeslaBot 은 메쉬가 분리된 채 전투한다 — 미해결.** 조사만 했고 담당자 배정 대기다:
  [Docs/tech/teslabot-mesh-separation-handoff.md](Docs/tech/teslabot-mesh-separation-handoff.md).
  임시 조치로 `MapGenConfig` GroupID 3 을 TeslaBot → PeekABot 으로 대체해 뒀다(GroupName 에 "임시대체" 표기).
  🔴 `MapGenConfig.asset` 은 `50.Art` = **SVN** 이라 git 커밋으로는 안 넘어간다 — TortoiseSVN 으로 커밋할 것.
- 남은 이슈: Wells `BehaviorGraphAgent` 가 모든 피어에서 돎(멀티 검증 전 서버 게이트 필요) /
  `WeaponTrailEffect` NRE 다수(로그 오염) / SVN `MapGenConfig.asset` 미커밋 /
  빌드 exe 실플레이(타이틀→로비→맵→결과) 미검증.

---

## 이전 인수인계 (2026-07-29 · 폭탄 투척 경로 복구, 커밋 `93b4e02`)

작업 세션: **경석(Claude)**. 브랜치 `feature/map-player-merge`, 마지막 커밋 `93b4e02`.
워킹트리: 씬 2개(`0.BootStrapScene`·`4.MapScene`)와 `Bomb.prefab`·머티리얼·BT 에셋 등은
**의도적으로 미커밋 보류** — 다른 담당자 작업(대시 등)과 겹치므로 이번 커밋에 넣지 않았다.

### 다음 시작점 — 폭탄이 손 높이에서 정지하는 문제

**아트 fbx는 무죄이고 `GroundProbe`도 정상이다.** Play 로그가
`바닥=Env_floor_basic_typeA (7)(layer 0) y=0.50 → 착지 y=0.55`로 매번 맞게 나온다.
그런데 폭탄 오브젝트는 `y≈2.79~2.86`(= 보스 손 높이)에 남는다. 목표에 도달했다면
`MovePosition(_targetPos)`로 Y가 정확히 0.55여야 하므로, **비행 중 `CheckHitBetween`에
막혀 위치가 갱신되지 않는 것**이다(`29cc999`에서 잡았던 증상이 다른 원인으로 재발).

`93b4e02`에 진단 로그를 넣어 뒀다 — 다음 Play에서 콘솔을 `정지`로 필터하면
`[No.23] 폭탄이 바닥 판정에 걸려 정지 — <콜라이더>(layer N), 현재 위치 …, 목표 …`가
범인을 그대로 지목한다. 유력 후보는 **`Env_Mv_bosscharger_upper`**(송전기, Default 레이어,
보스 계층 밖이라 `Unit` 제외에 안 걸린다). ground 마스크가 Default를 포함하는 한
`Unit` 제외만으로는 부족하다는 뜻이 된다.

⚠️ **MCP 브릿지로 Play 로그를 읽을 수 없다.** `totalBuffered`가 에디터 기동 시점 값에서
멈추고 Play 로그가 들어오지 않는다(도메인 리로드 후 로그 콜백 미재등록 의심). 이전 규칙
"Play 로그는 Play 중에만 읽힌다"는 무효 — **콘솔 스크린샷으로 받아야 한다.**

### 이번에 닫은 것

| 닫은 것 | 커밋 |
|---|---|
| Wells 루트에 `NetworkObject` 부착 — `WellsAnimEvents`가 `NetworkBehaviour`라 서버 판정을 못 받아 투척 애니 이벤트가 무시됐다(`BombHold`는 전역 `IsServer`를 봐서 영향 없음 → "손에 들고만 있음") | `4a84fe9` |
| `BombThrow`의 조용한 실패 복원(주석 처리돼 있던 로그) + `_bombController` 수명 일치 + `BombAction.Mode` 활성화 | `4a84fe9` |
| 폭탄 스케일 손 `0.5` → 착지 `1.0` 시간 보간 + `HandleHit` ground 분기 진단 로그 | `93b4e02` |

### 이전 세션에 닫은 것

| 닫은 것 | 커밋 |
|---|---|
| 폭탄이 보스 히트박스를 바닥으로 오인 → `GroundProbe`로 바닥 판정 통일(레이어 폴백·원점 띄우기·**Unit 계층 제외**) | `2b4226c` |
| 바닥 위 표준 간격 `0.05` 도입(절대 Y 고정은 불가 — 보스룸 보행면은 0.50, BossScene은 0) + 바닥 상단 측정을 Max→**최빈값** | `88da3c6` |
| 보스룸 안전망·기준점을 실제 보행면 **Y 0.50**에 맞춰 재생성(도구 재실행) | `95f3d61` |
| 폭탄이 보스 손 높이에서 영구 정지 — **무시할 히트를 스윕 마스크에서 제외** | `29cc999` |
| 아트 오프셋 실측 도구 + Bomb 비주얼 정합 확인(center (0,0,0) → 프리팹은 정상) | `cc22c3b` |

⚠️ **아트를 고치면 `Cube.422/423` 로컬 값이 바뀌고, 그러면 `BombVisual`의 상쇄값
`(28.0, -1.23, -6.94)`도 함께 무효가 된다.** 눈대중으로 맞추지 말고
`Tools/Map/Authoring/Measure Bomb Visual Offset`으로 재측정할 것(center가 0에 오면 정합).

### 그 다음 예정 (팀장 지시 순서)

1. **git 최신화 → SVN 업데이트**
2. **은희 `feature/PlayerSkillAnimation` 머지** — 사운드(`feature/Sound`)는 그 브랜치에 붙인 상태로 받는다.
   준비물은 §2-b/§2-c에 정리돼 있다:
   - **프리팹 컴포넌트 유실 검사법**: `grep -o "Assembly-CSharp::[A-Za-z_0-9]*" | sort -u`를 머지 전/후
     `comm` 비교(이번에 `BossHudTarget`을 놓쳤던 방식의 재발 방지)
   - **Player.prefab 레이어 마스크 함정**: `EnemyHurtBox`(14) 유지 — `--ours`로 통째 되돌리면 보스를 못 때린다
   - FMOD/AudioListener는 그 브랜치에 붙은 상태로 받기로 결정
3. ~~**미결 1건**: `Wells.prefab`이 `NetworkObject` 없이 `DefaultNetworkPrefabs`에 등록된 무효 상태~~
   ~~→ **`4a84fe9`에서 부착으로 해소.**~~ ~~현재 정상 동작하지만 NGO 중첩 지원에 의존하는 구조~~

   ⚠️ **정정 (2026-07-31, `58278e9`): 부착은 해결책이 아니었고 그 뒤에도 폭탄은 안 나갔다.**
   진단("서버 판정을 못 받는다")은 맞았지만 처방이 반대였다. **NGO 는 프리팹의 중첩
   NetworkObject 를 스폰하지 않는다**(씬 오브젝트만 지원) — 붙여도 스폰되지 않으므로
   `IsServer` 는 계속 false 다. 런타임 로그로 실증:
   `ThrowBombEvent 진입 — IsServer=False, IsSpawned=False`.
   해결은 **NetworkObject 제거 + `WellsAnimEvents` 를 MonoBehaviour 로 전환**이었다.
   🔴 **다시 붙이지 말 것.** 상세는 `58278e9` 커밋 메시지와 `WellsAnimEvents` 클래스 주석.

### 이 세션에 확립된 재사용 규칙

- **바닥 판정은 `GroundProbe.TryFindGround` 하나만 쓴다.** 직접 `Physics.Raycast(..., "Ground")`를 쓰지 말 것.
  네 군데에서 같은 원인으로 터졌다(점프 착지·폭탄 투척·MakeFloor·비행 스윕).
- **바닥에 눕는 것은 `GroundProbe.SurfaceY(hit)`** (= 찾은 바닥 + 0.05). 절대 Y 상수 금지.
- **보스룸 보행면 = Y 0.50** (`BossFloorCollider` 상단·`BossLandingPoint`·`BossArea` 전부 0.50).
  이전 문서·주석의 "0.61"은 솟은 발판을 잘못 측정한 값이었다.
- ~~**Play 로그는 Play 중에만 MCP로 읽힌다**~~ → **⚠️ 정정(2026-07-29): Play 중에도 못 읽는다.**
  `unity_get_console_logs`의 `totalBuffered`가 에디터 기동 시점 값에서 멈춘다. Play 로그는
  **콘솔 스크린샷으로 받는다**고 전제하고 진단 계획을 세울 것.
- 로컬 교훈 로그(`Docs/_local/lessons.md`) #32~#37에 이번 세션 6건을 적었다.

---

## 이전 인수인계 (2026-07-28 · SVN r235 커밋 완료)

작업 세션: **경석(Claude)**. 브랜치 `feature/map-player-merge` (`30cf1df`, origin보다 1 앞).

### 이번 세션 완료 — SVN 최신화

**커밋 r235** (225 → 235). 신규 242 / 수정 104.

- 신규: `MapObj/mesh/level`(131) · `material`(48) · `texture`(32) · `mesh/object`(28)
- **⚠️ r234 GUID 사고 처리** — 팀원이 "누락된 meta 커밋"으로 MapObj meta 87개의 GUID를 새로
  발급해 올렸다. 그대로 update 했으면 git 쪽 41개 파일(존 프리팹 12·벽 10·머티리얼 17·씬 1)이
  전부 미싱 레퍼런스가 된다. `svn merge -c -234`로 GUID 재발급분만 역머지해 원본 유지.
  경위·재발 방지 = `Docs/_local/lessons.md` #14
- **콜라이더 플래그 복구** — floor/wall/urethane fbx meta 39개가 7/28 아트 교체로 `addColliders`·
  `isReadable` 0으로 리셋돼 있던 것을 r225 값(1/1)으로 되돌림.
- 검증: 컴파일 0에러/0경고, GUID 80/80 원본 일치, 이번 작업발 미싱 레퍼런스 0.
  (기존 미싱 5건 — MapScene FoW `maskTexture`(미사용) · `MA_Wall_basic.mat` 1 · `Stage1.prefab` 3 — 은 별개)
- git 정리 커밋 `30cf1df`: Tree/TutorialInfo 템플릿 잔재 + `all_mesh.unity` 제거
  (빌드 세팅 미등록·코드 참조 0 확인).

### 이어서 완료 — 경사로 콜라이더 + dash-soul 머지

- **`caaef90`** 경사로·계단 17개 MeshCollider 부착. Play에서 slope를 밟으면 낙하하던 문제.
  프리팹 안 언팩 사본이라 fbx `addColliders`가 전파되지 않는데 `MapColliderAuthoring`
  이름 필터에 slope/stair가 없었다. 키워드 추가로 해결(소품 88개는 통행 방해 우려로 제외).
- **`7a5db51`** `feature/dash-soul` 머지(59커밋). 씬이 `0.Scenes/MainFlow/`로 재편돼
  rename+양쪽수정 충돌 9건 발생, 전부 해소. 상세 = 머지 커밋 메시지.
  - MapScene은 **UnityYAMLMerge 3-way로 충돌 0** — 우리(맵 시스템 7)와 그쪽(옵션 UI 11)이
    겹치지 않았다. 병합 후 18개 오브젝트·참조·스크립트 전수 확인.
  - 자동 해소에 맡겼으면 조용히 깨졌을 3건을 수동 처리: **BossHudTarget 유실**(보스 체력바
    미표시), **레이어 슬롯 14 이중 점유**(EnemyHurtBox↔Corpse), **HasAimGroundPoint 게이트**
    (생성맵에서 지면 스킬 전면 차단).
- 검증: Unity 배치모드 컴파일 **error CS 0**, MapScene 댕글링 참조 0 / 미싱 스크립트 0.

### 이어서 완료 — Play 검증 피드백 반영 (`dec1eb3`·`f990c73`·`35aa703`)

**★ 이번 세션 최대 교훈: dash-soul 시스템들은 "씬마다 하나씩 배치"를 전제하고, 없으면
예외 없이 조용히 비활성화된다.** 증상만 보면 기능 버그로 보인다. MapScene에 배치한 것:
`FallBoundarySettings`(추락 감지) · `PlayerDashValidationManager`(대시 서버 검증) ·
`Temp_MultiGameRule`(부활 규칙) · `PartyWipeWatcher` · `SessionStatsTracker`.

- **콜라이더**: 바닥 82개(머티리얼 이름 판정 추가 — 아트가 `Cube.209` 식으로 내보내 이름
  필터로 안 잡혔다). **계단은 경사면 BoxCollider로 대체** — 계단 형상 콜라이더는 Rigidbody
  캡슐이 턱에 걸린다(`stepOffset` 보정 없음). 보이는 건 계단 그대로.
- **경사 등판**: `PlayerMovement`가 수평 벡터를 그대로 `MovePosition`에 넣어 경사면에 파고들었다.
  접지 중이면 지면 노멀 평면에 투영하도록 수정.
- **Soul**: `Soul` 레이어 신설(16) + `Soul`/`Corpse` 충돌을 `Default`/`Ground`/`Wall`/`Env`로
  제한. 어비스 위에서는 `useGravity`를 꺼 부유(속도만 0으로는 중력이 재가속시킨다).
- **낙사**: `ServerFallDeath` 구독자가 0이어서 사망 시 몸이 추락 지점에 남았다 → 안전지점 복귀.
- **사이클**: 전멸(=전원 `PermanentDead`) → Result → 로비. 결과 = `SessionResult` +
  `SessionStatsTracker`(생존 시간·처치 수) + `ResultStatsView`. 처치 수는
  `MonsterBase`/`BossBase` 사망 지점의 `MonsterDeathEvents`로 집계.
- **시체 자홍색**: 빌트인 `Default-Material`은 URP 미지원 → URP Lit로 교체.
- `GameManager.prefab`의 씬 이름이 리네임 전 값이었다(BootStrap 인스턴스는 이미 정상이라
  정식 플로우는 무영향, MapScene 직접 Play만 실패). 프리팹 최신화.

### 이어서 완료 — 보스룸 경계 + 도착 ACK (`3b626a1`·`93f65b8`·`a3d284e`)

승인 계획의 **A단계(투명벽) + Task 1**까지. 계획 사본 = `C:\Users\user\.claude\plans\synchronous-pondering-coral.md`

- `BossRoomAuthoring` 저작 도구로 `bossroom.prefab`에 생성(재실행 가능, 렌더러 바운즈 실측):
  `BossFloorCollider`(21×1×21, Default, 상단 Y 0.61) · `InvisibleBoundaries` 4면(Wall, 높이 8,
  트리거·렌더러 없음) · `PlayerArrivalPoints/Player1..3`(2m 삼각, 착지점 응시) · `BossLandingPoint`
- `BossTeleportManager`: 황금각 산개 → 도착 지점 배열 + ACK 계약. `encounterSequence` +
  대기 집합, sender·sequence 일치만 수락(중복 무시), 전원 ACK → `AlivePlayersArrived` 1회,
  5초 타임아웃 → `ArrivalAborted`, disconnect 처리, `IsEncounterBusy`로 재진입 차단
- ⚠️ 함정: `renderer.bounds`는 월드 좌표이고 `LoadPrefabContents`는 프리팹을 원점이 아닌 프리뷰
  씬에 올린다. 로컬인 `BoxCollider.center`에 그대로 넣으면 전체가 밀린다(1차 실행에서 Z≈109).
  (로컬 로그 `Docs/_local/lessons.md` #18 — 팀 공유 대상 아님)

### 이어서 완료 — Task 2 연출 잠금 (`c35d7bd`)

`PlayerEncounterLock` 신설. **잠금 = 새 계통이 아니라 기존 게이트 재사용**:
`PlayerActionState.Cinematic`(기존 `PlayerLockedState`) 하나로 이동·공격·스킬의 서버 승인
게이트가 함께 닫히고, 피해 무시는 `PlayerInvulnerability`의 `Cinematic` 토큰이 담당한다.
dash-soul 계통(대시 예측+서버 스냅샷 / 추락 감지 / 생명주기 전이)도 잠금 중 차단.

- ⚠️ **함정**: `Unit.Died`는 `_deathNotified`로 래치돼 재발행되지 않는다. 잠금 중 사망 신호를
  버리면 HP 0인데 Alive로 남는다 → `PlayerLifeCycleController`가 보류 후 해제 시 처리.
- 오너 입력은 `PlayerLifeInputPolicy` 한 곳에서만 적용(두 계통이 `SetInputEnabled`를 각자
  부르면 나중 호출이 앞선 차단을 지운다).
- `Player.prefab` 배선은 YAML 직접 편집(+21줄). **Unity가 포커스를 받아야 재임포트된다** —
  인스펙터에 컴포넌트가 보이는지 확인 필요.
- 미검증: Play/MPPM에서 잠금 중 공격·스킬·대시·추락·부활 무효 + 해제 후 복귀.
- 이탈 기록·검증 경계 = `IMPLEMENTATION_NOTES.md`(로컬, gitignore).
- ⚠️ MCP 주의: `unity_recompile_scripts` 직후 상태 조회를 부르면 도메인 리로드 중이라 응답이
  없다(무한대기). 컴파일 확인은 `Library/ScriptAssemblies/*.dll` 타임스탬프 + 콘솔 Error로.

### 이어서 완료 — Play 피드백 5건 (2026-07-29, `703988f`~`4482d0b`)

- **인게임에서만 바닥에 구멍** — 시드 차이(TestGenerate=TickCount / 인게임=Random.Range).
  `AssignSlotRoles`는 BossRoom·PlayerSpawn 후보를 **크기 무관**하게 뽑는데 역할 디자인은 Small만
  저작돼 있다 → `GetRoleLayout`이 null → `MapContentSpawner`가 **로그 없이 continue** → 구멍.
  → 역할 디자인 없으면 같은 크기 전투 풀 폴백 + 경고, 스포너는 에러 로그. **조용한 구멍 금지.**
- **보스만 피격 빨간 틴트 없음** — `Enemy.OnNetworkSpawn`이 `base` 호출 없이 `if (!IsServer) return;`
  으로 시작했다. `Unit.OnNetworkSpawn`의 HP 복제 구독 + `HitFlash` 자동 부착이 통째로 건너뛰어졌다.
  Unit 파생 5종 중 Enemy만 누락. **★파생 클래스에서 base 누락은 "그 타입만" 조용히 기능이 빠진다.**
- **충전 중 보스가 맵 밖으로** — BT가 복귀 위치로 읽는 `SpawnPointer.SpawnPoint`가 프리팹 기본값
  `(0,0,0)`이고 코드에서 아무도 채우지 않는다. BossScene은 아레나가 원점이라 우연히 맞았다.
  → Director가 스폰 직후 착지점(방 중앙)으로 채운다.
- **보스 진입로가 막혔다(직전 세션 회귀)** — 레이저 프리팹에 심은 차단벽이 Stage1 통로 26곳을
  전부 막았다. 어느 슬롯이 Quest가 되는지는 시드마다 달라 정적 배치로는 불가.
  → 레이저 벽 제거, `MapContentSpawner`가 역할 확정 시점에 **Quest 존만** 네 변으로 감싼다.
- **중간보스 감축** — 마커 수 = 스폰 수. 엘리트 그룹(5)은 마커 1개 제한 + 초과 마커 정리
  → `ZoneL_typeC` 4 → 1. 위치 수동 조정은 재실행해도 보존(앞쪽 마커 유지).

### 이어서 완료 — Play 피드백 4건 (`60f3862`·`66ac555`)

**★ 교훈: 민경 님 보스 코드는 아레나 바닥이 `Ground` 레이어에 Y=0이라고 가정한다(BossScene 기준).
생성맵 보스룸 바닥은 `Default` 레이어에 Y≈0.61이라 바닥을 찾는 모든 레이캐스트가 조용히 빗나간다.**
증상은 제각각으로 보이지만 뿌리가 하나였다.

- **보스를 때릴 수 없었다** — 보스 몸 콜라이더는 `HurtBox(EnemyHurtBox=14)` 하나뿐이고 루트에는
  콜라이더가 없다. 플레이어 공격 마스크는 `1280`(Enemy|Projectile)이라 14를 못 봤다.
  → 기본공격·Q `17664`, 궁극기 타겟팅 `16640`. **프로젝트 관례 = 적 대상 마스크는 Enemy|EnemyHurtBox**
  (폭탄의 `enemy=16640`이 그 근거).
- **폭탄이 공중에 떴다** — `BombLauncher.groundMask`/`BombController.ground`가 `Ground(8)`뿐 →
  착지 Y 탐색·낙하 판정·장판 스냅 전부 실패. → `9`(Ground|Default).
- **장판과 몸체가 어긋났다** — `JumpController`가 바닥 레이어를 `"Ground"` 하드코딩 + 착지 Y를
  `0f` 고정. → `groundMask` 인스펙터화(비면 Default+Ground 폴백) + `hit.point.y` 사용.
- **Quest 통로 차단** — `layprefab`(레이저)에 `LaserBlockWall`(3.17×8×0.6, Wall) 생성.
  `Level_wall_hallway`에 26개 들어 있어 **26곳이 모두 막힌다**. 열어야 할 곳은 인스턴스에서 끄면 된다.
  도구 = `Tools/Map/Authoring/Setup Quest Laser Blockers`.

### 이어서 완료 — Task 3 Director + 충전 기둥 + 존 스폰 마커 (`3c3833d`·`77a6458`)

**MapScene에 보스가 등장하는 경로가 처음으로 연결됐다.** `BossEncounterDirector`(씬 상주
NetworkObject)가 등장·전투 전환의 단일 소유자다: 도착 ACK → 참가자 잠금 → 보스 1회 스폰(+18m)
→ 곡선 하강 → 착지 → `BeginCombatServer()`(NavMesh Warp + 잠금 해제 + `OpenBT` 한 번에)
→ 보스 `Unit.Died` → `Capture(cleared: true)` → 결과 화면.

- ⚠️ **순서 함정**: `RunningOnlyOnServer`가 `OnNetworkSpawn`에서 `navMeshAgent.enabled = IsServer`로
  되돌린다. **에이전트 차단은 반드시 `Spawn()` 이후** — 아니면 상공의 보스를 NavMesh로 끌어내린다.
- **충전 기둥 4개**: `bossroom`의 `Env_Mv_bosscharger_upper`에 NetworkObject·NetworkTransform·
  BoxCollider·`ChargingObject`를 저작 도구로 부착(`Tools/Map/Authoring/Setup Boss Charge Pillars`).
  Director가 스폰 직후 `ChargeController.SetList`로 주입 → BT의 `SetChargingStateAction`이 그대로 동작.
- ⚠️ **기둥 레이어는 `Enemy(8)`** — BossScene은 `EnemyHurtBox(14)`지만 플레이어 공격 마스크가
  `m_Bits=256`(Enemy)뿐이라 14에 두면 기둥을 때릴 수 없다. **민경 님과 정리 필요**(마스크를
  넓힐지, 기둥을 8에 둘지).
- `ChargingObject` 절대 Y → 로컬 숨김/활성 위치 + Hidden/Rising/Active/Lowering 상태로 재작성.
  같은 세션 반복 사용 가능.
- **존 스폰 마커 복구**: 존 프리팹 12개 중 `ZoneLayout`이 3개만 남아 있었고 마커는 0개였다
  (v11 재작업·아트 교체 때 유실). `Author ZoneLayouts (from Catalog)` 재실행 → 11존 복구,
  마커 27개. `ZoneL_typeC` = **GauntletBot 중간보스**. 마커 위치는 러프값 → Play 후 조정.
- 배선 도구: `Tools/Map/Authoring/Wire Boss Encounter (MapScene)`(MapScene 열고 실행, 재실행 안전).
- 미검증: Play/MPPM 전체 흐름. 미구현: 대사 HUD·ESC 만장일치 스킵(Task 5), 착지 카메라
  흔들림·먼지 VFX(Task 4 나머지).

### 진행 중 (2026-07-28 이어서) — 승인 계획서 전제 갱신 완료

`Docs/superpowers/plans/2026-07-24-boss-encounter-intro.md`에 **Revised Premises 9항** 추가.
Task 1·Task 7(경계 부분) 완료 표시, 씬 경로(`MainFlow/4.MapScene`) 전면 수정, Task 2를
"기존 `GameplayAccess` 게이트 확장 + dash/fall/life 차단"으로 재작성, Task 3에 클리어 판정
(`Unit.Died` → `Capture(cleared: true)`)과 `PartyWipeWatcher` 경합 항목 추가.

~~다음 = Task 2~~ **완료**. 다음 = **Task 3**(`BossEncounterDirector`). Task 2가 수정한 파일 = `1.Scripts/Player/{PlayerEncounterLock.cs(신규),
PlayerInputReader, PlayerStateController, PlayerMovement, DefaultAttackController, Player,
PlayerDashController, Skill/PlayerSkillController, Fall/PlayerFallController,
Life/PlayerLifeCycleController, Life/PlayerLifeInputPolicy}`, `1.Scripts/Unit/StatusEffectController.cs`,
`2.Prefabs/Player/Player.prefab`. ⚠️ 플레이어 계통은 은희 담당 영역 — 동시 수정 주의.

### ▶▶ 다음 세션 시작점 (2026-07-29 마감 · 팀장 지시 = **분석 먼저, 수정 나중**)

팀장 지시 원문 취지: "지금 너무 수정만 하는데 달라지는 게 아니니 **분석부터 제대로** 해야 한다.
다음 세션에서 작업하자." → 다음 세션은 **코드를 고치기 전에 아래 4건의 분석을 먼저 끝내고
결과를 보고한 뒤** 지시를 받는다. 상세 경위·교훈은 `Docs/_local/lessons.md` #20~#26 +
「⏳ 미해결」 절.

**분석 1 — `Env_Wall_doorframe` 콜라이더 부재 (확인됨, 원인 미확정)**
- 사실: 문틀은 `MeshFilter`+`MeshRenderer`만 있고 콜라이더가 없다(인스펙터 확인). 그래서 통과된다.
- 벽 투명화와 무관함은 확정: `WallOcclusionDriver`에 `Collider`/`Physics`/`SetActive`/`enabled` 참조 **0건**
  (셰이더 전용). 투명해 보이는 것과 콜라이더 부재는 별개 사건이다.
- 분석할 것: `MapColliderAuthoring`의 이름 필터에 doorframe 계열이 포함되는지, 문틀 26개(+`Env_laser`)의
  콜라이더 유무 전수, 문틀이 통행을 막아야 하는지(문이므로 열려 있어야 할 수도) — **디자인 의도 확인 필요**.

**분석 2 — M키 지도 ↔ 우측하단 미니맵 탐사 동기화 (미구현)**
- 사실: 미니맵은 `_MaskTex`(R=explored, G=visible)로 점점 밝아진다. M키 지도(`MapOverviewUI`)는
  슬롯 사각형만 그리고 탐사 개념이 없다.
- 요구: 미니맵에서 밝아진 영역이 M키 지도에도 같이 그려져야 한다.
- 분석할 것: `MinimapController.GetExploredBits()`(이미 존재) + `_worldRect` 좌표계를 `MapOverviewUI`가
  어떻게 소비할지. 존 사각형 위에 탐사 마스크를 덮는 방식 vs 베이크 텍스처를 그대로 확대 표시하는 방식 —
  둘 중 어느 쪽이 지금 UI 구조에 맞는지 먼저 결정.

**분석 3 — `BossArea` ✅ 해결(2026-07-29): BT가 태그로 스스로 찾는다, 주입 불필요**
- No.23 BT는 `FindObjectWithTagAction`(Tag=문자열 `"BossArea"`)으로 씬에서 직접 찾아 `BossArea`
  GameObject 블랙보드 변수를 채우고, 같은 부모 아래 `SetEnableBoxColliderAction(Enable=true)`로 켠다
  (`No.23.asset` rid 1567581773390152037·038 — 읽기만 함). **Director 주입 불필요.**
- MapScene 현황은 이미 완비: `bossroom.prefab`에 tag `BossArea` 트리거(20.98×2×20.98, center y=1),
  충전 기둥 4개 = `Env_Mv_bosscharger_upper` + BoxCollider + ChargingObject + NetworkObject +
  NetworkTransform(maxHp 5·defense 0·riseHeight 1·moveSpeed 1 = KMK와 동일),
  `BossEncounterDirector.chargingObjects`에 4개 전부 연결.
- ⚠️ **BossScene의 BossArea를 MapScene으로 복사해 오면 안 된다** — 태그 2개가 되어 `FindObjectWithTag`가
  어느 것을 잡을지 보장되지 않고, 기둥 8개 중 4개만 동작하며, `TwentyThreeArenaContext`가 함께 오면
  보스 이중 스폰이 된다. `BossEncounterDirector.ValidateBossAreaTag()`가 태그 0개/2개 이상을 에러로 잡는다.
- 부수 확인: `HomePoint`는 코드·No.23 BT 어디서도 참조 0건(미사용 마커). `8.BehaviorTreeGraph/Boss/
  BossArea.asset` 그래프는 어느 씬·프리팹도 참조하지 않는 고아(예제 빌더 산출물).

**분석 4 — ✅ 해결(2026-07-29): 존 저작은 이미 맞았고, 검증 도구가 잘못된 대상을 읽었다**
- **거짓 경보였다.** `Save Placements`는 **씬의 Stage1 인스턴스**에 쓰고 씬을 dirty 처리한다(프리팹에
  Apply하지 않음). 그런데 `Validate Slot Authoring`은 `Stage1.prefab` **에셋**을 읽어 씬 오버라이드를
  못 봤다 → 재저작을 마친 뒤에도 "미저작 9건"을 계속 보고했다.
- 프리팹+씬 오버라이드(슬롯 대상 61건)를 병합한 실제 상태 = **실질 미저작 0건**. 도구가 세던 3건은
  전부 도달 불가 조합이었다: Slot 5·7 × `ZoneS_typeA`(둘 다 Boss/Spawn 후보 2곳뿐 → 절대 Combat 안 됨),
  Slot 8 × `Quest01`(`QuestPrefab=Quest02` 지정 시 카탈로그 Quest 풀 미조회).
- Quest는 의도대로 고정됨: Slot 4 `IsQuestCandidate` 0으로 내리고, Slot 8만 후보 + `QuestPrefab=Quest02`
  → 시드 무관 항상 Slot 8 / Quest02. 부작용으로 카탈로그 Quest 풀 2종은 사실상 미사용.
- 셔플은 카탈로그대로 돈다: Large 0·1·2 ↔ A/B/C 1:1 순열 / Medium은 Slot 3 `FixedPrefab=ZoneM_typeC`가
  pinned 제외되어 남은 풀 {A,B} 2개 ↔ Slot 4·9 2곳 1:1 / Small 전투는 Slot 6 하나 ↔ `ZoneS_typeA`.
  총 조합 = 6 × 2 × (Boss/Spawn 스왑 2) = 24가지. **Medium은 여유 0** — Medium 슬롯을 늘리거나 Slot 4를
  Quest 후보로 되살리면 즉시 풀 부족(재사용)으로 넘어간다.
- 남은 잔재: 옛 GUID 참조 9건(Slot 3:3, 4:3, 8:2, 9:1) + 끊긴 `QuestPrefab` 2건. 런타임 무해(null은
  프리팹 비교에 안 걸림), 리포트만 오염. 청소 도구 = `Tools/Map/Authoring/Cleanup Slot Authoring (dead refs)`.
- ⚠️ **구조적 취약점**: 저작 정본이 씬 오버라이드에만 있다 → Stage1 인스턴스에서 Revert 한 번이면
  61건이 날아간다(복구선 = 커밋된 MapScene). Stage1을 다른 씬에 인스턴스화하면 저작 0 상태로 떨어진다.
  근본 해소는 씬 오버라이드를 Stage1.prefab에 Apply해 정본을 프리팹으로 옮기는 것(씬 수술 리스크, 미착수).

**참고 — 이번 세션에 실제로 검증된 것**
- 보스 등장 흐름 정상: 로그 `SpawnPoint를 방 중앙 (500.49, 0.61, 0.49)으로 설정` → 하강 → `전투 시작 — BT 개방`.
  스크린샷의 보스 좌표 `(13.33, 0.08, -4.83)`은 그 수정 **이전** Play다.
- 미니맵 베이크 자체는 정상(`중앙 샘플 평균 밝기 0.308`). 남았던 원인은 실루엣 마스크였다.
- 레이저 통로 차단(26곳)은 **의도된 것**이며 유지한다. 내 판단으로 제거했다가 원복했다(`5dee39d`).
- ⚠️ 보스룸 저작 도구(`Rebuild Boss Room Bounds`)는 `PlayerArrivalPoints`·`BossLandingPoint`를 **재생성**한다
  → 실행 후 `Wire Boss Encounter (MapScene)`를 반드시 재실행(참조 끊김). 손으로 옮긴 지점도 초기화된다.

### 이번 세션 완료 (2026-07-29 · 아레나 자립화 + 유령 타겟팅)

- **`BossArenaContext` 신설 + bossroom.prefab에 부착·저장** — 아레나가 자기 부품(착지점·BossArea·
  충전 기둥 4개·도착 지점 3개)을 프리팹 내부 참조로 들고 있다. 참조가 전부 프리팹 내부 fileID라
  **절대좌표가 개입하지 않는다**. `BossEncounterDirector`는 `arena` 하나만 물어보고, 씬 배선이
  비어 있으면 여기서 채운다 → 다른 씬에 인스턴스화해도 동작. 저작 도구가 기준점을 재생성해
  참조가 끊기는 사고도 사라진다. 부착 도구 = `Tools/Map/Authoring/Wire Boss Arena Context (bossroom)`.
  - **위치 검증 결과: 착지점 localPos (0.49, 0.61, 0.49) == BossArea localPos, 간격 0.000m.**
    즉 "아레나 중앙으로 안 간다"의 원인은 절대좌표 하드코딩이 아니다(코드 전수 검색에서도 0건).
    `Spawn Point`·`ArrivePoint` 블랙보드 Vector3는 둘 다 런타임에 채워진다
    (Director→`SpawnPointer`→`GetSpawnPointAction` / `JumpController.SetTarget`). 남은 유일한 위험은
    `GameObject.Find("BossLandingPoint")` 이름 폴백 → 이제 arena를 먼저 보므로 최후 폴백으로만 남았다.
- **유령(Soul) 상태에서 몬스터가 더 이상 반응하지 않는다** — `MonsterTargeting.IsAttackable`(신설)이
  단일 기준. 기존 `IsTargetValid`가 `null`+`activeInHierarchy`만 봐서 Soul이 통과했고, 사망 직전에
  잡힌 타겟이 유지되어 몬스터가 유령을 쫓고 공격 모션까지 냈다(데미지는 Soul에서 hurtbox가 꺼져
  안 들어갔으므로 **행동만 남은 상태**였다). 적용: `MonsterBase.IsTargetValid`·`FindNearestTarget`,
  `BossBase` 동일 2곳, `GauntletBot.CountNearbyPlayers`(유령이 스매시 단계 인원수에 잡히던 것).
  - 판정 기준은 `ShouldEnableHurtbox`가 아니라 `PlayerLifeState.Alive`다 — 무적 프레임(대시 회피)이
    hurtbox를 끌 때 "맞지 않는다"를 "노리지 않는다"로 해석하면 대시 한 번에 타겟이 풀린다.
  - ⚠️ **No.23 보스는 미적용**: 타겟 선정이 BT 노드(`FindClosestWithTagAction` 등, 민경 님 영역)라
    코드에서 못 막는다. 같은 증상이 보스전에서 재현되면 BT 쪽 조건 추가가 필요하다.

### ▶ 진행 중 — "23호가 landing 직후 (0,0,0) 근처로 이동" (2026-07-29, 원인 2개 후보 모두 차단·검증 대기)

증상: 하강·착지까지 정상, **착지 직후** `TwentyThree(Clone)` position이 x≈1.9(맵 중앙)로 이동.
아레나는 x≈500이므로 500m 순간이동이다. 절대좌표 하드코딩은 코드 전수 검색 **0건**이었고,
착지점·BossArea localPos는 간격 **0.000m**로 정합했다. 그래서 원인은 다음 둘 중 하나다.

**후보 A — NavMeshAgent를 메시 밖에서 켰다 (착지 시점에 정확히 실행됨 = 가장 유력)**
- `SnapBossToNavMesh`가 `_bossAgent.enabled = true`를 **샘플링보다 먼저** 했다. NavMesh 밖에서
  에이전트를 켜면 Unity가 내부 위치를 가장 가까운 메시에 맞추는데, 아레나에 메시가 없으면
  그 "가장 가까운 곳"이 맵 본체(원점 근처)다 → 보스가 끌려간다.
- 수정: **샘플 먼저 → 실패하거나 착지점에서 2.5m 이상 떨어진 메시를 잡으면 에이전트를 켜지 않고
  에러 로그**. 아레나 안에서 못 움직이는 게 원점으로 날아가는 것보다 낫다(lessons #26 원칙).
- ⚠️ 이 경우 보스가 아레나에서 이동하지 못한다 = **NavMesh 베이크를 고쳐야 하는 진짜 문제**가 드러난다.
  `MapNavMeshBaker`는 `useGeometry=PhysicsColliders` + `layerMask=Default만` + `collectObjects=All`이고,
  bossroom의 `BossFloorCollider`(BoxCollider)는 layer 0(Default)이라 **수집 대상은 맞다**. 경계 5개는
  layer 7(Wall)로 올바르게 벽 취급. 즉 배선상 bossroom이 특별 취급되는 곳은 없다 — 남은 의심은
  아트 바닥 콜라이더 유실/unreadable mesh다(베이커에 `UnreadableMeshColliderBakeScope` 폴백이 있는 이유).

**후보 B — BT 절대 위치 블랙보드가 (0,0,0)으로 시작한다 (구조는 확인, 발동 여부 미확인)**
- No.23 루트가 `ParallelAllComposite` + `Start` 8브랜치 **병렬**이다. 브랜치[1]에
  `NavigateToLocationAction(Location="Spawn Point")`가, 브랜치[4]에 그 값을 **쓰는**
  `GetSpawnPointAction`이 있다 → 쓰기가 먼저라는 보장이 없다.
- `ArrivePoint`도 같다. `JumpController.SetTarget`이 채우는데 연출 착지 중엔 `_isCinematicLanding`으로
  **조기 반환해 한 번도 안 채워진다**. 그 값을 `SetPositionThroughRaycastAction`·`MoveForDurationAction`이
  **위치에 직접 쓴다**(파인딩 아님) → (0,0,0) 순간이동. 단 이들은 `SwitchComposite`(상태 스위치) 아래라
  첫 틱 무조건 실행은 아니다 — 그래서 "구조는 확실, 발동은 미확인"이다.
- 수정: `BossEncounterDirector.SeedArenaPositionBlackboard`가 스폰 직후 `Spawn Point`·`ArrivePoint`를
  **방 중앙으로 미리 덮는다**. BT 그래프는 보스 담당 영역이라 손대지 않고 최악값만 없앴다.

**✅ 판별 완료 (Play 1회, Editor.log 확인)** — 원점 이동 **해결**됨:
- `[BossEncounter] 보스 NavMesh 부착 완료 — (500.49, 0.67, 0.49) (착지점 오차 0.06m)`
  → **아레나에 NavMesh 정상 존재. 후보 A 기각.** bossroom 베이크는 문제없다.
- `[BossEncounter] BT 위치 블랙보드 초기화 — Spawn Point, ArrivePoint = (500.49, 0.61, 0.49)` → 시딩 동작.
- 즉 실제로 들었던 원인은 **후보 B(블랙보드 (0,0,0))** 쪽이다.

### ▶ 후속 — "보스가 공격만 하고 걷지 않는다" = 이동 배선이 아니라 **거리창** 문제 (2026-07-29 분석 완료)

- **로그 증거: 전체 런에 `Walk`/추격 상태 전환 0건.** Idle→Upper/LeftHook/RightHook/Grab/Jump→Idle 반복,
  마지막에 Charging→Groggy→Dead. 걷는 상태에 들어간 적이 없다.
- 원인 = `TwentyThreeBasicAttackChoice` 거리창이 거의 전 구간을 덮는다(프리팹 값):
  hook 0~3 / upper 0~3 / grab 0~1 / jump 5~10 / dash 10~20, 가중치 50/50/100/100/100.
  **빈 구간은 3~5m와 20m 초과뿐**이고 `GetRandomAttack`은 그때만 `None`을 반환한다 → BT가 Walk로 가는 건
  그 두 구간에서만이다. 플레이어가 붙어 있으면 항상 공격이 뽑히므로 **걷지 않는 게 설계상 정상**이다.
  → **팀장 확인(2026-07-29): 3~5m Walk 밴드 하나는 의도된 설계다.** 그 안쪽은 hook/upper/grab이 처리한다.
  남은 쟁점은 수치를 bossroom 크기에 맞추는 것뿐이다(아래).

**아레나 스케일 실측 — 튜닝 전제가 바뀌었다 (2026-07-29)**

| | 벽 안쪽 전투 구역 | 최대 거리(대각선) | BossArea 트리거 |
|---|---|---|---|
| BossScene(튜닝 환경) | Ground scale 5 → 약 48×48m, 벽 ±24m | ~68m | 10×10, 중심에서 **x+2.79 어긋남** |
| bossroom(실전) | **21.0×21.0m** | **29.67m** | 20.98×20.98, 방 전체·정중앙 |

- bossroom이 KMK 테스트장보다 **약 2.3배 작다.** 현재 거리창은 48m 방 기준으로 잡힌 값이다.
- ⚠️ **사각지대**: dash가 20에서 끊기는데 bossroom 최대 거리는 29.67m → **20~29.67m 구간(코너 대각)**에서
  `GetRandomAttack`이 `None`을 반환해 Walk로 간다. WalkSpeed 2로는 10m를 5초 걸어온다.
- 원칙: **공격 리치는 아레나 크기와 무관, 이동 공격과 사각지대만 아레나에 의존한다.**
  hook/upper/grab(0~3, 0~3, 0~1)은 애니메이션 리치이므로 스케일하면 안 된다(팔이 안 닿는 거리에서 때린다).
  그리고 **가장 먼 공격의 max는 항상 아레나 대각선 이상**이어야 한다.
- 권고 2안 (프리팹 값이라 팀장/민경 승인 후 변경, 아직 미적용):
  - **안 1(최소 변경, 권장)**: dash max **20 → 30**. 나머지 그대로 → 사각지대 0.
  - **안 2(방 크기에 맞춤)**: jump 5~9 / dash 9~30. 대시를 더 일찍 쓰게 하고 사각지대 0.
  - 별건: **WalkSpeed 2 → 3.5 이상** 검토(NavMeshAgent 기본값보다 낮아 걸어도 티가 안 난다).
- 참고: KMK의 BossArea는 10×10에 x+2.79 어긋난 반면 bossroom은 방 전체·정중앙이다. BT가 켜고 끄는
  그 콜라이더의 범위가 두 환경에서 크게 달라, KMK에서 "구역 밖"이던 거리가 bossroom에선 전부 "구역 안"이다.

**⚠️⚠️ 위 권고는 무효 — `origin/feature/Boss` 3커밋(민경, 미머지)이 거리창을 SO로 옮기고 값을 바꿨다 (2026-07-29 확인)**

`89bc9e1`(페이즈별 Page SO) → `e61999d`(SO 리스트 통합) → `9a2cad0`(넉백 SO + 씬 리네임).
거리창은 이제 프리팹 필드가 아니라 `9.ScriptableObject/Enemy/Boss/Wells&No.23/TwentyThreePage {0,1,2}.asset`이고,
`PageEventAction` BT 노드가 페이즈 전환 시 `PageEvent(page)`로 교체한다. 실측값:

| Page | hook | upper | grab | jump | dash |
|---|---|---|---|---|---|
| 0 | 0~4 (50) | 0~4 (35) | 0~4 (15) | 5~10 (60) | **5~10** (40) |
| 1 | 0~4 (40) | 0~4 (40) | 0~4 (20) | 5~10 (45) | **5~10** (55) |
| 2 | 0~4 (35) | 0~4 (40) | 0~4 (25) | 5~10 (25) | **5~10** (75) |

- **세 페이지 모두 0~4와 5~10만 덮는다. 10m 초과를 덮는 밴드가 하나도 없다.** bossroom 최대 거리는
  대각선 **29.67m** → **10~29.67m 전체가 Walk 구간**(가능 거리의 약 2/3). 이전(dash 10~20)과 성격이 반대다:
  "사각지대가 좁아 안 걷는다" → **"멀면 거의 항상 걷는다"**.
- 따라서 **`WalkSpeed`(=`Enemy.moveSpeed`)가 2인 것이 이제 치명적이다.** 20m를 10초 걸어온다.
  이전엔 코너 케이스였지만 지금은 상시 경로다. → **최우선 조정 대상은 dash max가 아니라 WalkSpeed.**
  권고: `moveSpeed` 2 → **5~7**(NavMeshAgent 기본 3.5보다도 낮은 현재값은 21m 방에 안 맞는다).
  대안(더 침습적): dash max를 대각선까지(예: 5~30). 단 페이즈별 jump/dash 비중 설계(40→55→75)를 깨뜨린다.
- ⚠️ **팀장이 확정한 "3~5m Walk 밴드"가 이미 4~5m로 절반이 됐다** — hook/upper/grab max가 3→4로 올랐다.
  의도 재확인 필요(민경과).
- 새 구조 자체는 개선이다: `GetRandomAttack`의 `None`→Walk 로직은 그대로이고, 수치만 SO로 빠졌다.

**⚠️ 머지 리스크 (가장 큰 이슈) — 분기점 `0aba7b3`(7/20), 미머지**

- 🔴 **`No.23.asset`이 양쪽에서 변경.** 민경 쪽 11,191줄(+6041/−5546). 그런데 **내 워킹트리도 이미 dirty
  (+3373/−3951)** — 이 세션 전부터 그랬고 내가 만든 변경이 아니다. 내용은 rid 재번호 + `Name: Self` 같은
  블랙보드 변수 삭제 = **Unity 재직렬화 churn**이다. `CommonMeleeRobot.asset`도 같은 성격(+516/−516).
  → **이 churn을 커밋하면 민경의 11k줄 작업을 덮어쓴다.** 머지 전에 반드시 `git checkout --`으로 폐기해야 한다
  (메모리 규칙 "8.BehaviorTreeGraph 수정금지"와도 일치). 거대 생성 YAML은 3-way 머지가 사실상 불가능하다.
- 🟡 `ProjectSettings/EditorBuildSettings.asset` 양쪽 변경(내 쪽 dirty + 민경 씬 리네임). union 성격이라 수동 병합 가능.
- 🟢 **소스 파일 충돌 0.** 민경이 건드린 것 = `BaseAttackChoice`, `TwentyThreeBasicAttackChoice`, `Unit.cs`,
  `KnockbackAttack`, `TwentyThreeWells_*`, `TwentyThree.prefab`, `PageEventAction`(신규), `IKnockbackSettable`(신규).
  내가 건드린 것과 **겹치는 파일이 없다.** 특히 `Enemy.cs`는 민경이 안 건드려 내 `ApplyOptionalSpeed`가 그대로 산다.

**씬 리네임 파급 — `KMKScene.unity` → `BossScene.unity`**

- `.meta`를 유지해 **GUID 보존 → 에셋 참조는 안 깨진다** ✅. `TwentyThreeArenaContext`·BossArea·Cylinder1~4·
  HomePoint 모두 BossScene에 잔존 → MapScene과 분리 유지, 이중 스폰 위험 없음(내 `ValidateBossAreaTag` 가드도 유효).
- 하지만 **이름 문자열 참조는 깨진다.** 갱신 대상: `Map/BossArenaContext.cs`, `Map/BossEncounterDirector.cs`,
  `Map/Editor/BossEncounterWiring.cs`, `Map/Editor/BossRoomAuthoring.cs`(주석), `CONTEXT.md`,
  `Docs/tech/game-structure-uml.md`, `Docs/tech/script-inventory.md`, `Docs/_local/lessons.md`.
  그리고 **`Assets/1.Scripts/Scene/BossScene.cs`는 스크립트 이름이 그대로다** — 씬만 리네임됐다.
- 그 커밋에서 FMOD `Sound`/`BGM` 오브젝트가 **제거**됐다(164줄 삭제). 보스 테스트 씬에서 BGM이 사라진 것 —
  사운드 담당(민경) 의도인지 확인 필요.

**부수 — `Unit.cs`의 `////`**

`// 방어력 경감률 적용` → `//// 방어력 경감률 적용`으로 바뀌었는데 **바로 아래 계산 코드는 그대로 살아 있다.**
경감률을 끄려던 시도였다면 실패했다(여전히 적용 중). 커밋 메시지는 "주석 정리"라 의도일 수도 있으나
데미지 밸런스에 직결되므로 확인 필요.

**부수 — 넉백**

`KnockbackAttack`에 `SetKnockbackStrength`(+`IKnockbackSettable`)가 생겨 세기를 SO로 주입하게 됐지만,
내가 보고한 **"Floor 넉백 매 프레임 재적중 + 피해 0"은 해결되지 않았다** — 중복 방지 로직은 없다.
이제 "세기 0"과 "피해 0"을 구분해서 봐야 한다.

**✅ `ChaseSpeed` 경고 정리 완료** — `Enemy.ApplyOptionalSpeed`로 교체했다. 부재는 **조용히 통과**(그래프마다
선택적으로 쓰는 변수이므로 정상), 반대로 **그래프가 쓰는데 넣을 값이 0**이면 그때 경고한다. 이유:
값 0인 프리팹에 이름만 맞추는 잘못된 수정을 경고가 유도하고 있었다.
- **이동 배선 자체는 정상**: NavMesh 부착 OK / 추격 노드 `NavigateToTargetAction(Speed→WalkSpeed)` /
  `Enemy`가 `WalkSpeed = moveSpeed = 2` 기록 성공(루트 `BehaviorGraphAgent`는 1개뿐 — 프리팹에 보이는
  두 번째 항목은 `m_GameObject: 0`인 고아 직렬화라 `GetComponent`가 올바른 쪽을 잡는다).
  단 **WalkSpeed 2는 보스치고 매우 느리다**(NavMeshAgent 기본 3.5보다 낮음) — 걸어도 티가 안 난다.
- ⚠️ **함정: `ChaseSpeed` 죽은 배선.** `[Enemy] ChaseSpeed 변수를 얻어오는 것에 실패` 경고가 뜨는데,
  No.23 그래프에 `ChaseSpeed`가 **없다**(가진 그래프는 `Enemy/CommonMeleeRobot.asset`뿐). 게다가
  `TwentyThree.prefab`의 `chaseSpeed = 0`이다. 지금은 무해하지만 다음 사람이 경고를 보고 이름을 맞추면
  **추격 속도 0이 되어 보스가 진짜로 안 움직인다.** 이름을 손대기 전에 값을 먼저 넣어야 한다.

### ▶ 후속 — 대시 2회차부터 이동 없음 = **서버 거부 후 즉시 EndDash** (원인 사슬 확정, 사유는 로그 대기)

- `PlayerDashController.RespondDashClientRpc`가 거부/중단 시 곧바로 `stateController.EndDash()`를 호출한다.
  호스트에서는 ServerRpc→ClientRpc 왕복이 사실상 같은 프레임이라 **`PlayerDashState.Tick`이 변위를 한 번도
  적용하기 전에 상태가 끝난다.** `PlayerDashState.Enter`가 `SetAnimatorMoving(false)`를 호출하므로 증상이
  "가만히 있는 애니메이션만 순간 출력 + 이동 0"으로 정확히 나타난다.
- 왜 1회차만 통과하는지는 서버 `PlayerDashValidationManager.ValidateRequest`의 거부 사유에 달렸다.
  유력 후보 = `NoFreshSnapshot`(clientLocalTime↔serverNow 시계 도메인 + `SnapshotFreshnessTolerance`) 또는
  충전 장부 epoch/revision 불일치.
- **거부가 로그 0줄이었다**(조용한 실패). 경고를 추가했다 → 다음 Play에서
  `[Dash] 서버가 대시를 취소했습니다 — approved=… / reason=… / 남은시간=… / 권한충전=…` 한 줄로 확정된다.
- ⏩ **후속 결론은 아래 「3. 대시 — 쿨타임 결함 3건 수정 완료」 참조.** 여기 적힌 후보 중
  `NoFreshSnapshot`은 확정되지 않았고, 실제로 잡힌 것은 "거부 시 충전 미환불 + 스냅샷 기준 충전 판정 +
  오프라인 멈춘 시계" 3건이다.

### ▶ 부수 발견 (미처리, 보고만)

- `[No.23] Floor 넉백 공격 적중: Player(Clone) (피해 0)`이 **19회 이상 연속**으로 찍힌다.
  장판 넉백이 매 프레임 재적중하면서 데미지는 0 — 중복 방지 누락과 데미지 값 둘 다 의심된다.

### ▶▶ 다음 세션 시작점 (2026-07-29 마감 · 팀장 지시)

**1. 몬스터가 NavMesh 없는 공중을 걸어서 건너온다 (확정 — 원인 미규명)**
- 팀장 확인: 몹이 순간이동한 게 아니라 **걸어서** 고립 플랫폼으로 건너왔다. 즉 NavMesh가 **틈 위 공중에
  깔려 있다.** `ReattachAgents` Warp는 원인이 아니다(그 건은 별개로 1.5m 제한으로 수정 완료 — lessons #31).
- 조사할 것: `NavMeshSurface` 설정(`agentClimb`/`agentRadius`/voxel size). 낮은 단차를 이어붙이는
  `agentClimb`가 크면 플랫폼 사이 틈이 walkable로 연결된다. `MapNavMeshBaker.Awake`가 강제하는 값은
  `useGeometry`·`collectObjects`·`layerMask`뿐이고 **에이전트 파라미터는 씬 세팅 그대로**다 — 거기부터 본다.
- ⚠️ 이번 세션에 내가 바꾼 것도 용의자다: NavMesh를 **다리가 열린 상태로** 굽게 했다(`BakeOpenScope`).
  카브(`ZoneBridgeGate` 의 `BridgeGapCarve`)가 안 먹으면 다리가 물러난 구간이 walkable로 남는다.
  Play에서 그 오브젝트가 생성되는지, 크기가 다리 구간을 덮는지 먼저 확인할 것.

**2. 민경 팀원 커밋 받기 (`origin/feature/Boss`) — 커밋 추가 도착, 총 13개 ahead**
- 기존 3개(`89bc9e1` 페이즈별 Page SO · `e61999d` SO 리스트 통합 · `9a2cad0` 넉백 SO + BossScene 리네임)에
  더해 `465a934`(몬스터 시간 제어 HitStop/SlowMotion + `WaitForAnimState` BT 노드) ·
  `01fd648`(`SetNumberWithTag` onlyCountRoot) · `677ffc4`(No.23 BT 그래프) · `63242b2`(maxRageCount 2→5).
- 팀장 방침(2026-07-29): **feature/Boss 쪽을 권위로 받고 내 로컬 수정본은 폐기**한다.
- 상세 분석·리스크는 이 문서 위쪽 「⚠️⚠️ 위 권고는 무효」 절 참조.

**2-b. ✅ 머지 완료 (2026-07-29 · `3ef3cab`, 컴파일 0에러 0경고 / Play 검증 대기)**

- 충돌 10건 해결: 보스 소유 에셋(BossScene·TwentyThree·Wells·No.23) = theirs / TagManager·Unit.cs =
  ours / BombLauncher = theirs / ChargeController·JumpController = 수동 병합 / Player.prefab = 충돌
  블록만 ours.
- ⚠️ **Player.prefab에서 살린 저쪽 clean hunk**: `PlayerDefaultAttack.targetLayer` ·
  `DefaultAttack.hittableLayers` 가 **Enemy(8) → EnemyHurtBox(14)** 로 전환됐다. 보스와 일반몹
  (`ModularRobots_R1`) 모두 레이어 14 노드가 있어 정합. `--ours`로 파일 전체를 되돌리면 256으로
  회귀해 **보스를 못 때리게 된다** — 다음에 이 파일 충돌 시 주의.
- 머지 후 컴파일 수정 1건: `BossBasicAttackChoice.PageEvent(int)` 구현 추가(`BaseAttackChoice`에
  추상 멤버 신설). 코드 FSM 보스는 Page SO 체계가 없어 의도적 no-op.
- **의도적으로 안 받은 것**: Player.prefab의 `AudioListener` + `FMODUnity.StudioListener`.
  이 머지는 FMOD 파일·참조 코드를 하나도 안 가져오고(`51adcf1`에서 사운드 분리) 이 워킹카피에 FMOD가
  미설치라 받으면 Missing Script가 된다. AudioListener를 Player 프리팹에 두면 멀티에서 리스너가
  여러 개가 되는 문제도 있다 → `feature/Sound` 머지 때 함께 검토.
- 머지 직후 Unity가 BT 에셋 3개(`No.23` +4598/-5033 등)를 재직렬화했다 → 규칙대로 폐기했다.
  커밋하면 민경 저작 바이트를 덮어쓴다.
- 후속 커밋 `c273ad8`: `EditorBuildSettings` 경로를 BossScene으로 갱신.

**2-c. 머지 후 Play 검증에서 나온 회귀 3건 — 수정 완료 (2026-07-29)**

- **보스가 멈추고 애니메이션이 아무것도 안 보인다(사망 연출 포함)** = `MonsterTimeController.HitStop` 재진입 결함.
  코루틴이 복원값으로 `currentScale`을 기억했는데, HitStop 진행 중(배율 0)에 또 맞으면 두 번째
  코루틴이 **0을 복원값으로 기억** → 0.25초 뒤 배율을 0으로 "복원" → `animator.speed`·`agent.speed`
  **영구 0**. `Enemy.TakeDamage`가 피격마다 부르므로 0.25초 내 2연타면 재현된다.
  게다가 BT의 `WaitForAnimStateAction`은 `normalizedTime`을 보므로 애니메이터가 멈추면 **BT도 로그
  없이 영원히 대기**한다(장판·데미지는 시간 기반이라 계속 돌아 원인이 가려진다).
  → 복원값을 최초 진입에서만 기록 + `OnDisable`에서 배율 1 복구.
  ⚠️ 부수 발견: `Enemy.OnNetworkSpawn`이 `IsServer` 게이트 **뒤에서** `_monsterTimeController`를
  잡으므로 HitStop은 서버에서만 돈다 — 클라는 타격감 연출을 못 받고, 이 정지도 호스트 화면 한정이었다.
- **보스 HP bar가 다시 안 보인다** = `BossHudTarget`이 TwentyThree.prefab에서 **머지로 유실**됐다
  (theirs 채택). 원본 블록(fileID `9114957203948571100`, 루트 `TwentyThree`에 부착, 직렬화 필드 없음)을
  그대로 복원했다.
  ★**다음 머지 때 쓸 검사법**: 커밋 로그로 유실을 추정하면 틀린다. 프리팹별로
  `grep -o "Assembly-CSharp::[A-Za-z_0-9]*" | sort -u`를 머지 전/후로 `comm`하면 사라진 컴포넌트가
  바로 나온다(이번엔 TwentyThree에서 `BossHudTarget` 1건, Wells는 0건으로 확정됐다).
- **폭탄이 바닥으로 떨어지지 않는다** = `BombLauncher.groundMask`가 Wells.prefab에서 **Ground(3) 단독**
  이었다. 생성맵 바닥은 **Default(0)** 이라 레이캐스트가 빗나가고, 빗나가면 `target.y`를 그대로 둬서
  폭탄이 공중 지점에 착지한다. → `m_Bits: 8` → `9`(Default+Ground). 같은 계열인
  `BombController.ground`는 이미 9였다(런처만 저작 누락). 빗나갈 때 경고 로그도 추가했다.
  TwentyThree는 중첩 Wells 인스턴스의 컴포넌트를 참조(stripped)하고 오버라이드가 없어 이 값이 그대로 적용된다.

**2-a. 🔴 머지로 유실된 내 작업 — 다시 해야 함 (팀장 지시로 기록)**

프리팹 YAML은 수동 머지하지 않는다(GUID/fileID 깨짐 위험). Boss 쪽을 통째로 받고 아래를 재작업한다.

⚠️ **머지 후 실측하니 이 표의 예상이 대부분 틀렸다.** 아래가 검증된 결과다(2026-07-29).

| 대상 | 실제 결과 | 근거 |
|---|---|---|
| `Bomb.prefab` 아트 모델 교체 | ✅ **온전함 — 재작업 불필요.** 충돌 없이 auto-merge되어 양쪽이 합쳐졌다. 플레이스홀더 `Sphere`의 MeshRenderer는 비활성(`m_Enabled: 0`) 유지, 아트 인스턴스 `BombVisual`(scale 0.684) 존재. 부모 오프셋(28.0, −1.23, −6.94)이 fbx 내부 오프셋(−40.96, 1.80, 10.16)×0.684와 상쇄되어 폭탄 원점(≈0.15, 0, −0.03)에 놓인다 | `1b13d6e` |
| "TwentyThree 피격 가능+지면 인식" | ❌ **애초에 프리팹 작업이 아니었다.** `60f3862`의 TwentyThree.prefab diff는 **비어 있다** — 코드 작업(JumpController `groundMask`/`GroundProbe`)이고 그 코드는 병합에서 우리 것으로 살렸다. 현재 theirs 프리팹도 레이어14(EnemyHurtBox) 노드 + Hurtbox를 갖고 있다 | `60f3862` |
| `maxShield` 직렬화 제거 | 무해. 코드에 이미 없는 필드의 잔여 직렬화값이라 다음 재저장 때 사라진다 | `1271b85` |
| `BossScene.unity` | 재작업 아님 — 보스 테스트 씬은 민경 소유로 인계(팀장 확인). 우리 `8e0215b`(102줄 추가)는 버린다 | `8e0215b` |
| `Player.prefab` 오디오 2개 | 유실 아님(의도적 미채택) — `feature/Sound` 머지 때. 팀장 방침: 사운드 브랜치를 **은희 `feature/PlayerSkillAnimation`에 붙인 상태로** 받는다 | 위 2-b |

**🔴 남은 실제 후속 1건 — `Wells.prefab`**
- 머지 후에도 `NetworkObject`가 **없는데** `DefaultNetworkPrefabs.asset`에는 **등록돼 있다**(`6e2c783`, 민경 작업). 즉 "네트워크 프리팹으로 등록됐지만 NetworkObject가 없는" 무효 상태가 그대로다.
- 선택지 두 개이고 **민경 확인이 필요하다**: ① Wells를 네트워크 스폰할 것이면 루트에 `NetworkObject` 부착, ② 스폰 주체가 없으면(현재 `BossEncounterWiring`의 보스 프리팹은 `TwentyThree.prefab`이다) 등록을 제거. 둘 중 뭐든 하기 전에는 무효 항목 경고가 남는다.

- Boss 쪽도 같은 파일을 만졌다: `18befc0`·`89bc9e1`·`e61999d`·`9dbdf8c`·`9a2cad0`·`465a934`.
  → `Bomb.prefab`/`TwentyThree.prefab`은 **양쪽 커밋 충돌**이므로 "theirs" 채택 시 위 3건이 사라진다.
- `Wells.prefab`의 `NetworkObject`는 **HEAD에도 Boss에도 없다.** 그런데 Wells는 오래전부터
  `DefaultNetworkPrefabs.asset`에 등록돼 있다(`6e2c783`) → 지금 레포는 "네트워크 프리팹으로 등록됐지만
  `NetworkObject`가 없는" 무효 상태다(`TwentyThree.prefab`은 갖고 있다). 머지 후 인스펙터로 재부착하고
  별도 커밋할 것. 민경과 담당 경계 확인 필요.
- `CommonMeleeRobot.asset` dirty(내 로컬 1032줄)는 BT 리세이브 churn → `git checkout --`으로 폐기하고
  Boss 쪽 1828줄을 받는다. `0.BootStrapScene.unity` dirty는 **내용 차이 0**(개행 변환뿐), 팀장 지시로 보존.
- 🔴 **머지 전 필수**: `git status`로 `Assets/8.BehaviorTreeGraph/*` dirty 확인 → dirty면 `git checkout --`으로
  폐기. Unity가 리컴파일마다 재직렬화하므로 계속 되살아난다. 커밋하면 민경의 11k줄을 덮어쓴다.
- 🔴 **`git add -A` 금지** — 위 BT 에셋이 섞여 들어간다(이번 세션에 실제로 한 번 섞여 amend로 제거).

**3. 대시 — 쿨타임 결함 3건 수정 완료 (2026-07-29 후속, Play 검증 대기)**

팀장 판단("1회 제한이 아니라 쿨타임이 안 돈다")이 맞았다. `DashChargeLedger`의 회복 계산 자체는
정상이고(별도 콘솔 하네스로 실행 검증), 문제는 **쿨타임이 리셋/동결되는 경로**였다.

- **(1) 거부 시 예측 충전이 환불되지 않았다** — 오너는 입력 순간 소비로 `Revision`을 올리고, 거부한
  서버는 소비를 안 해 `Revision`이 더 낮다. 그래서 응답의 권한 충전값이 `SyncToAuthoritative`의
  과거-리비전 가드에 걸려 **조용히 버려졌다**. 결과: 거부 1회 = 대시 안 나가고 재충전 2초는 통째 손실.
  → `DashChargeLedger.ForceAdoptAuthoritative`(리비전 무시 채택) 추가, 거부 경로에서만 사용.
  잔여시간은 응답의 `nextChargeReadyServerTime`을 오너 도메인으로 환산해 이식한다.
- **(2) 충전 유무를 과거 스냅샷으로 판정했다** — `snapshot.ChargeCount`는 마지막 물리 tick 값이라
  회복 경계 직후에는 아직 0이다. 충전은 서버만 바꾸는 자원이라 지연보정할 이유가 없다(오탐만 생긴다).
  → `DashValidationPolicy.Validate`에 `authoritativeChargeCount` 파라미터 추가, 현재 서버 장부로 판정.
- **(3) 오프라인 Play에서 시계가 멈춰 충전이 영구히 회복되지 않았다** — `NetworkClock`은 세션이 안 돌면
  `LocalNow`/`ServerNow`를 **상수 0**으로 돌려준다. 그런데 `OwnerNow()`는 `Instance != null`만 보고
  폴백을 결정했다 → 프리팹은 씬에 있고 세션은 안 켠 상태(예: `PlayerDashTest` 단독 Play)에서
  `Advance(0.0)`만 반복 → **대시 딱 1회**. → `NetworkClock.IsRunning` 추가하고 그걸로 폴백 판단.
- 부수: 서버 충전 소비 시각을 RPC 도착시각 → **추정 입력시각**으로 옮겼다(오너/서버 회복 시점 정렬).

⚠️ **반증된 가설 (기록용)**: "원격 클라는 오너가 서버보다 먼저 회복해서 경계 입력이 구조적으로
NoCharge 거부된다"— 12초 시뮬레이션으로 반증됐다. 승인 응답의 `SyncToAuthoritative`가 오너 타이머를
**응답 도착 시점**으로 재시작하기 때문에 오너는 항상 서버보다 RTT만큼 **늦다**. 부작용으로 오너
체감 쿨타임 = `rechargeDuration + RTT`(100ms RTT면 2.1초)다. 지금은 안전한 방향이라 그대로 뒀다.

- 남은 검증: Play 1회. 성공 경로에도 로그를 넣었으므로
  `[Dash] 시작 — 남은충전 n/1, 재충전 2.00s, now=…, 시계=NetworkClock|Time.timeAsDouble` 한 줄이 뜬다.
  **`시계=Time.timeAsDouble`로 찍히면 (3)의 상황**이고, 간격이 2초보다 훨씬 길면 아직 다른 원인이 있다.
- EditMode 테스트: 정책 2건·장부 3건 추가(`DashValidationPolicyTests`·`DashChargeLedgerTests`).
  에디터가 열려 있어 Test Runner 배치 실행은 못 했고, 대신 순수 로직 파일을 `dotnet`으로 떼어
  같은 단정을 실제 실행해 전부 통과 확인했다. **Test Runner 실행은 아직 안 했다.**

**3-a. 보스룸 진입 경사(`Env_object_bossroomenter`) 콜라이더 — 완료 (2026-07-29)**

- 증상: 보스룸 진입 4방향 경사를 못 올라간다. 콜라이더가 없었다(lessons #29 재발).
- 원인 = **두 저작 경로가 모두 놓치는 사각지대**. 이 오브젝트는 존 프리팹 안의 **fbx 모델 프리팹
  인스턴스**다. ① fbx 임포터 `addColliders: 0`이라 모델 쪽에서 안 붙고, ② `MapColliderAuthoring`의
  `AddFloorWallColliders`는 `IsPartOfPrefabInstance`면 건너뛴다(원본 프리팹에서 1회 붙이는 전제인데,
  여기서 원본은 fbx라 컴포넌트를 붙일 수 없다). 이름 필터(`floor/wall/hallway/slope/stair`)에도
  `Env_object_*`는 소품으로 분류돼 안 걸린다.
- 조치: 새 메뉴 `Tools/Map/Authoring/Add MeshColliders to Walkable Model Instances`.
  허용목록(`bossroomenter`) − 제외목록(`_mv_`)으로만 동작한다. **엘리베이터
  `Env_object_MV_bossroomenter`는 이동 플랫폼이라 의도적으로 제외**(팀장 확인).
  fbx `.meta`는 SVN 관리라 임포터 설정을 못 건드리므로 git 쪽(존 프리팹)에 인스턴스 오버라이드로 붙였다.
- 형상 실측(면적 가중, 삼각형 법선): 위쪽 면이 **0~10도 7% / 20~30도 84% / 30~40도 8% → 전부 60도 이하.**
  완경사라 MeshCollider(비볼록)로 충분하고 계단식 램프 박스 대체는 불필요하다.
  ⚠️ bounds(rise/run)로 각도를 추정하면 헛값이 나온다(첫 시도 65.8도 — 중앙 구조물 높이를 경사로 착각).
  도구가 이제 각도 분포를 로그로 남긴다.
- ⚠️ **부수효과 주의**: 이 계열 도구는 `Assets/2.Prefabs/Map` 전체를 `LoadPrefabContents`로 순회하는데,
  그 과정에서 **중첩 프리팹 에셋의 루트 위치가 0으로 정규화**되는 일이 있다(이번에 `bossroom.prefab`의
  루트가 `(6.199, 0, 108.774)` → `(0,0,0)`으로 바뀌어 되돌렸다). MapScene 인스턴스는 위치 오버라이드를
  3축 다 갖고 있어 영향은 없었지만, **도구 실행 후 `git status`로 의도 외 프리팹 변경을 확인할 것.**

**4. 미착수 (오후 목표 잔여)**
- 벤트에서 증기 나옴
- 이동 플랫폼 바닥 `Env_MV_floor_typeA` 컨베이어 — 은희와 협의 후 추가

**완료된 것 (이번 세션 후반)**
- 다리 개통 F 상호작용 **동작 확인**(패널 4개 → 링 4개 → 다리 lerp 이동). 링 각도 버그 수정
  (원을 로컬 XZ에 그리고 또 90° 돌려 벽면이 됐던 것 → 로컬 XY로 그림).
- 다리 조각에 MeshCollider 자동 부착(없으면 NavMesh에 안 올라간다 — lessons #29).
- 열림 위치 저작 완료(팀장 확인: 의도 맞음). `Record Bridge CLOSED/OPEN Positions` 도구 2개.

### ▶ 이전 세션 시작점(참고용)

**증상: 보스룸으로 이동은 되는데 보스가 안 나온다 — 정상이다.** MapScene에는 보스를 스폰하는
주체가 없다. 보스를 스폰하는 `TwentyThreeArenaContext`(`OnNetworkSpawn`에서 `boss.Spawn()`)는
`BossScene`·`PlayerBossTest`에만 배치돼 있고, **MapScene의 `TwentyThree.prefab` 참조는 0건**이다.
그게 **Task 3 `BossEncounterDirector`**의 일이고 이번 범위는 "도착까지"였다.

1. **승인 계획서에 "달라진 전제" 반영** — 착수 첫 단계. 씬 경로(`MainFlow/4.MapScene`),
   플레이어 프리팹 1개, 연출 잠금 대상 확대(dash·fall·revive·soul), `PartyWipeWatcher` 오발 억제,
   카메라 우선순위(Float 뷰), 클리어 판정 연결, 담당 경계, DynamicsManager 재수정 불필요.
2. **Task 2~8** 순서대로. Task 3에서 보스 스폰 소유자를 Director 하나로 정리한다
   (`TwentyThreeArenaContext`는 민경 님 영역 — 중복 스폰이 실제로 생기면 그때 수정, 팀장 승인 받음).
3. 보스 격파 시 `SessionStatsTracker.Active.Capture(cleared: true)` 연결 → 결과 클리어 판정 완성.
4. **미구현 확인분**: dash HUD 위젯(충전 개수·재충전 게이지), 로딩바 보간,
   RMB 스킬(`FirstMeleeInterruptSkill` 구현체 자체가 없음), Result UI 서체·배치.
5. push 안 된 상태. 롤백 지점 = `backup/pre-dash-soul-merge`(`caaef90`).

### 확인만 하고 넘긴 것

- **로비 Ready**: 호스트는 자동 Ready + GameStart 버튼, 클라이언트만 Ready — **의도된 설계**.
- `ProjectSettings/DynamicsManager.asset`이 serializedVersion 13 → 23으로 포맷 마이그레이션됨
  (Unity가 저장 시 재작성). 신규 필드는 전부 기본값이지만 팀원 pull 시 통째로 바뀐 diff를 본다.
- 몬스터 루트 레이어가 제각각(ChompBot=19 이름없는 레이어, SpinnerBot·WallBot=0). 콜라이더는
  전부 Enemy(8)라 현재 증상은 없으나 레이어 마스크 로직에서 물릴 수 있다.

### 주의 — SVN meta

`50.Art/Char/Boss/bomb.fbx.meta`가 로컬 미버전 상태다. r233에서 fbx만 meta 없이 올라와
우리 Unity가 GUID를 새로 발급한 것 — **커밋하지 말 것**(r234와 같은 사고가 된다).
boss 담당(민경)이 자기 프로젝트에서 커밋해야 한다.

### 이전 세션 완료

1. **벽 투명화 per-pixel 재설계** — 오브젝트당 스칼라 불투명도(MPB) → 프래그먼트 월드좌표 기반
   셰이더 계산. 물리 쿼리 0, MPB 0, 약 1,600줄 → 473줄. Play 검증 통과.
   설계·검증·한계 = [Docs/tech/wall-occlusion-implementation.md](Docs/tech/wall-occlusion-implementation.md)
   - 삭제된 타입: `WallOcclusionUnit/Proxy/Manager/VisibilityContributor/Core/RuntimeBinder/ProjectBridge`
   - 현행 타입: `WallOcclusionDriver`(Assembly-CSharp) + `Globals`/`MaterialBinder`/`Settings`(Occlusion asmdef)
2. **맵 프리팹 콜라이더 복구** — 아트 교체로 12개 프리팹 콜라이더 0개였던 것 복구.
3. **`Assets/level` 아트팩 145개 재배치** — GUID 참조 그래프로 사용/미사용 판정 후 이전.
   `AssetDatabase.MoveAsset`만 사용, **GUID 145/145 보존, 미싱 레퍼런스 0**.
   - `50.Art/MapGen/MapObj/{mesh/level, material, texture}` ← FBX·머티리얼·셰이더그래프·텍스처 (**SVN**)
   - `2.Prefabs/Map/Props` ← 프롭 프리팹 38개 (git)
   - `99.Settings` ← `PP.asset`, `PP_Renderer.asset` (미참조 URP 파이프라인, 위치만 잡아둠)
   - 이름 충돌 3건은 `_level` 접미사로 개명 (`MA_prop03_level` 등 — 기존 50.Art와 이름만 같고 별개 에셋)
4. **오클루전 머티리얼 매핑 5쌍 → 14쌍** — level 폴더 머티리얼 9종이 매핑에서 빠져 프롭들이
   디더 셰이더를 못 달고 있던 문제 수정.

### 다음 작업 (이어서)

1. **SVN 최신화** — `50.Art/MapGen/MapObj` 아래 신규 파일들을 SVN에 add/commit. git에는 안 보인다
   (`.gitignore:83`이 `50.Art/` 제외, `Assets/50.Art.meta` 1개만 추적).
2. **머지** — SVN 상태 맞춘 뒤 진행.

### 주의

- **`8.BehaviorTreeGraph/**` 는 다른 담당자 작업물이다. 수정·커밋하지 말 것.** (현재 워킹트리에
  수정 상태로 있으나 의도적으로 커밋에서 제외했다.)
- 미커밋으로 남긴 것: `TitleScene`·`ProjectSettings`(줄바꿈만 변경, 내용 0), `all_mesh.unity`,
  `FogProfile.asset`, BT 에셋 2종.
- 아트(FBX·텍스처·머티리얼)는 SVN, 코드·씬·프리팹은 git. 이 경계를 넘기지 말 것.

## 이전 인수인계 (2026-07-21 → Codex)

작업 세션: **경석(Claude)** — MapScene 몬스터/보스입장 통합 완료(컴파일 0, 1차 플레이 검증). 다음 작업자 = Codex.

- **상세 현황·남은작업·조사항목 = [Docs/tech/map-monster-boss-handoff.md](Docs/tech/map-monster-boss-handoff.md)** (이 세션 산출물 전체 + 우선순위 목록).
- 계획 잠금: `PLAN.md` §"MapScene 몬스터 통합" + §6(보스 입장).
- 최근 수정 파일(동시수정 주의): `1.Scripts/{Map/*, Monster/MonsterBase.cs, Player/PlayerMovement.cs·PlayerAimIndicator.cs, Unit/Weapon/BaseAttack.cs, Player/Skill/FirstMeleeMainSkill.cs}`, `MapScene.unity`, 존 프리팹 12개.
- **아직 push/커밋 안 됨.** git + SV( 50.Art meta·MapGenConfig) 분리 커밋 예정 — 핸드오프 문서 §4.
- 즉시 다음 후보: 패드 y 가림 조치 / 멀티(MPPM) 텔레포트 검증 / 터렛 스폰 재확인 / **MortarBot 복귀 후 간헐 Idle 회귀 조사**(핸드오프 §3).

## Project Summary

A top-down cooperative action game inspired by Ravenswatch-style structure.

Current near-term target:
- Start game
- Boss intro sequence
- Boss combat
- Listen-server network vertical slice

Later scope:
- Map expansion
- Growth systems
- General mobs
- Additional content

## Core Terms

- Player: A human-controlled networked unit.
- Host: The player running the listen server.
- Client: A connected player that is not the host.
- Server authority: Logic owned and decided by the server/host, then replicated.
- Owner authority: Logic controlled by the owning client, usually player input and movement.
- Unit: A gameplay actor with common state and snapshot behavior.
- UnitBase: The common base for shared unit state and snapshot only. Movement, abilities, status effects, and networking behavior should be composed with components where possible.
- Boss: A server-authoritative enemy with encounter flow, patterns, state, and network-visible presentation.
- Boss intro: The sequence before combat begins, including presentation and state transition into battle.
- State abnormality: Status effect or condition applied to a unit.
- Build: A player growth or ability configuration concept.
- Skill: A player or boss action/pattern defined by data and executed by runtime logic.
- ScriptableObject data: Authoring-time gameplay data for skills, builds, bosses, patterns, and tuning values.
- Vertical slice: A thin but complete path through gameplay, networking, UI/presentation, and verification.

## Networking Language

- Player input: Usually owner-authoritative.
- Player movement: Usually owner-authoritative unless a specific anti-cheat or server correction rule is chosen.
- Boss state: Server-authoritative.
- Enemy state: Server-authoritative.
- Damage: Server-authoritative.
- Drops/rewards: Server-authoritative.
- Scene progression: Server-authoritative.
- Snapshot: A compact representation of state needed for synchronization, save, debug, or replay-like inspection.

## Design Preferences

- Prefer composition over deep inheritance.
- Prefer data-driven tuning for gameplay content.
- Prefer small vertical slices over broad unfinished systems.
- Prefer clear module interfaces that hide meaningful implementation.
- Prefer names from this file and `Docs/` over ad hoc synonyms.

## Open Vocabulary To Resolve

Add definitions when these become concrete:
- Exact boss encounter phase names
- Player class names
- Ability categories
- Build/growth terminology
- State abnormality taxonomy
- Scene/session flow terms
- Network room/lobby terms

## Resolved Terms (2026-07-21)

- Boss enter pad: BossRoom 역할 존 중앙의 진입 패드(트리거+테두리 표시). 생존 플레이어 점유 시 카운트다운(3·2·1), 전원 이탈 시 취소. 완주 시 생존자 전원 보스룸으로 텔레포트. 튜닝은 BossTeleportManager 인스펙터.
- RangedTurret: 고정 포탑 몬스터 아키타입(PeekABot·TeslaBot). 넉백 면역, 경직만 적용.
- Knockback direction: 공격이 AttackInfo.knockbackDirection으로 명시(방향성 공격). zero면 수신측이 방사형(대상-공격자)으로 폴백(장판/폭발형).
