# PLAN-boss-fsm.md — 보스 FSM 재설계 (웰즈 & 23호)

> 작성 2026-08-06 · 경석(Claude) · 상태: **승인 대기**
> `PLAN.md` 는 은희 님의 승인 대기 계획서(2026-08-03 개발 진입점 단일화)가 점유 중이라 별도 파일로 둔다.
> 확정 설계 본문은 [Docs/tech/boss-fsm-design.md](Docs/tech/boss-fsm-design.md) — 이 파일은 **왜/무엇을/어디까지**만 담는다.

---

## 1. 목표

BT(`BehaviorGraphAgent`)로 돌고 있는 웰즈&23호를 **순수 코드 FSM**(`MonsterBase` 와 동형)으로 재작성한다.
동시에 기획에 새로 들어온 **카운터(인터럽트) 시스템**을 보스 FSM의 1급 개념으로 넣는다.

**이번 세션 산출물 = 설계 문서까지.** 코드·프리팹·BT 에셋은 건드리지 않는다.

## 2. 현재 이해 (그릴 + 감사로 확정된 전제)

🔴 **재작성의 이유는 "설계가 낡아서"가 아니라 "현행 구현이 계속 회귀를 만들어서"다.**
Claude·Codex 독립 2개 레인으로 전수 감사했다 → [boss-current-problems-audit.md](Docs/tech/boss-current-problems-audit.md).
공통 뿌리는 **권위 상태의 부재** — BT 가 상태 머신이고 Animator·이벤트 채널·애니 이벤트·
블랙보드가 상태를 나눠 갖는다. 그래서 부분 보수가 아니라 전면 재작성이다.


- **살아 있는 보스는 BT다.** `TwentyThree.prefab` 에 `BehaviorGraphAgent` 가 붙어 있고,
  기믹 실행체(`GrabController`·`JumpController`·`ChargeController`·`BombLauncher`)는 전부
  `Assets/1.Scripts/Enemy/Boss/` 에 있다.
- **`Monster/Boss/BossBase.cs` 는 23호에 붙어 있지 않다.** 더미 공격 2종(Slam/Sweep)만 있는 스켈레톤이다.
- **민경 님은 보스 작업에서 빠졌다** (2026-08-06 팀장 확정). 보스 전체가 경석 단독 소유가 되었으므로
  `AGENTS.md` §5 의 담당 분배는 갱신 대상이다. → BT·기믹 컨트롤러를 **자유롭게 재구성할 수 있다.**
- **7/30 보스 피격 조사 3건은 해결됐다** (팀장 확인). `Docs/tech/next-boss-hit-investigation.md` 는 폐기 대상.
- 볼트 `보스_(프로토타입).xlsx` 는 **웰즈&23호 문서가 아니다** — `Demon` 모델을 쓰는 별개의 프로토용 보스다.
  현재 코드의 `Groggy`/`Break`/확률 선택 구조가 여기서 왔다. **인터럽트→Groggy→Break 모델만 계승**하고
  `Dodge_Back`/`Dodge_Front`/`Return` 은 채택하지 않는다.

## 3. 접근 — 슬라이스 분할

문서 승인 후의 구현 순서다. 각 슬라이스는 **단독으로 MPPM 검증이 가능**한 단위로 끊었다.

| # | 슬라이스 | 내용 | 검증 |
|---|---|---|---|
| S0 | **설계 문서** (이번 세션) | 계획서 + `boss-fsm-design.md` 전면 재작성 | 팀장 승인 |
| S1 | **FSM 골격 + 공격 선택기** | 23호 상태 전이, 조건 게이트→가중치→연속방지 선택기, 쿨다운 재등록 배선, 폴백 행동 | 보스가 쿨 상황에서도 멈추지 않는가 |
| S2 | **근접 3종** | 좌/우 훅 · 어퍼(Airborne) · 애니 이벤트 히트/종료 | 히트 타이밍이 클립과 일치하는가 |
| S3 | **카운터 시스템** | 카운터 창 상태 + 정면 판정 + Groggy/Break + `IBossTelegraph` | 창 안/밖·정면/후방 4조합 |
| S4 | **Grab 체인** | Grab(창) → Hold → Throw, 카운터 시 취소 | 카운터 성공/실패 분기 |
| S5 | **Dash 캐리-푸시** | 돌진 + 캐리 소켓 + 벽 충돌 → 스턴 | 벽·맵끝·다중 피격 |
| **S6-0** | **`AreaZone` 인프라** ⬅ **S9 에서 당김**(2026-08-07 팀장 확정) · ✅ **완료** | 타입 있는 장판(화염/늪/독/번개), 같은 타입끼리만 중첩 성장 | 장판 단독 스폰·수명·중첩 |
| S6 | **JumpAttack** | 최원거리 타겟 + 장판 2단 + 메시 토글 | 장판과 착지 데미지 일치 |
| S7 | **페이즈/송전기/차징/레이지** | 임계 통과 → 고정 시퀀스 | 1·2·3인 송전탑 수 |
| S8 | **Wells FSM** | 4상태 + 폭탄 주기 + 그로기 동기화(23호가 단방향으로 밀어줌) | 23호 그로기 시 Wells 동반 정지 |
| S9 | **폭탄** (장판은 S6-0 으로 이동) | 폭탄을 별도 오브젝트·수명으로. 수평 당구 2단계, 되쳐내기 비례계수 노출. 폭발 시 S6-0 의 장판을 스폰 | 장판이 남은 채로 다음 폭탄이 날아가는가 |

**S1 이 다른 모든 슬라이스의 전제**다. S3 은 플레이어 쪽 의존이 있어(§6) 순서가 밀릴 수 있다.

## 4. 핵심 결정과 트레이드오프

그릴에서 팀장이 확정한 것 (2026-08-06). 근거가 갈렸던 것만 트레이드오프를 적는다.

| 항목 | 확정 | 트레이드오프 / 근거 |
|---|---|---|
| **카운터 창을 여는 패턴** | **Grab · DashAttack 2개만** | 훅·어퍼까지 열면 카운터가 상시 자원이 되어 그로기가 흔해진다 |
| Grab 창 범위 | **잡기 판정 직전 애니까지** | 못 끊으면 잡힌다 = 창에 실패 대가가 붙는다 |
| Dash 창 범위 | **돌진 전 구간** | 돌진은 회피/카운터 둘 다 열어 선택지를 준다 |
| **카운터 수단** | 우클릭 = `PlayerSkillSlot.Interrupt`(단죄의 방패). **전 캐릭터 공통** | 한 직업만 가능하면 파티 구성이 강제된다 |
| **성공 판정** | 보스 **정면 각도** + 창 안 | 헤드어택(은희 님) 구현 후 **그쪽으로 교체** → 판정을 교체 가능한 지점으로 분리 |
| **성공 결과** | 즉시 `Groggy` + `GroggyCount +1`. Count == max 면 `Groggy` 대신 **`Break`** | |
| **실패 결과** | 스킬은 정상 시전(쿨 소모), 패널티 없음. Grab 은 `Hold`→`Throw` 진행 | |
| **그로기 유발원** | **인터럽트 스킬만.** 일반 공격은 데미지만 | 현 `AttackInfo.isGroggyAttack`(공격에 붙은 플래그)로는 표현 불가 → §5 |
| **송전기 그로기** | `GroggyCount +1` (같은 카운터) — 단 **Break 승격은 안 함** | 페이즈 전환 직후 5초 무력화가 겹치면 페이즈 연출이 죽는다 |
| 그로기 수치 | `maxGroggyCount 5` / `Groggy 2s` / `Break 5s` (인스펙터) | |
| **페이즈 임계** | **66% / 33% 확정** | 더 이상 임시값 아님 |
| **송전탑 수** | 1인 1 / 2인 2 / **3인 4** | 현 `ChargeController` 의 `Clamp(playerCount, 1, 3)` 은 **버그**로 확정 |
| **차징** | 근접 시 데미지 + 뒤로 밀치기 **有**. 차징 중 다른 애니가 안 나오므로 **카운터 창 없음** | |
| **레이지 돌진 3회** | **카운터 창 없음** | 송전기 실패 벌칙이 카운터로 쉽게 풀리면 실패에 의미가 없다 |
| **Return(리쉬)** | **없음.** 보스룸 이탈 불가 | 전원 Soul → 타겟 없음 → `Idle`. **체력 회복 금지** — 전멸 직전 리셋은 재도전을 불가능하게 만든다. 그로기 카운트·송전기·폭탄·페이즈 플래그는 **전부 유지** |
| **Wells** | 별도 FSM. `Idle`/`Throw`/`Groggy`/`Dead` **4상태**(기존 `Jump` 삭제). 피격 대상 아님. 23호 그로기와 **동기화** | 23호 상태에 얹으면 폭탄 주기가 23호 공격에 끌려다닌다 |
| **쿨다운** | Jump 10s / Dash 5s / Grab 10s / 훅·어퍼 2~3s | |
| **거리 조건** | Dash = 원거리만. **Jump = 거리 무관** + 타겟은 **최원거리 플레이어** | 쿨만이 게이트면 10초마다 기계적으로 나와 읽힌다 — 타겟 규칙이 의도를 만든다 |
| **Dash 피격 결과** | **캐리-푸시** → 벽/맵 끝까지 → 플레이어 **스턴**, 보스는 패턴 복귀 | |
| **카운터 창 표현** | `IBossTelegraph` 인터페이스로 분리. 지금은 노란색 틴트, 나중에 VFX 컴포넌트 스왑 | 함수 하나보다 인터페이스가 나은 이유: 창 **진행도**를 표현하고 싶어질 때 인터페이스만 넓히면 된다 |
| **기존 기믹 컨트롤러** | 🔴 **재사용 안 함 — 전면 재작성** (2026-08-06 뒤집힘) | 초판은 "재사용"이었다. [감사](Docs/tech/boss-current-problems-audit.md) 결과 문제가 한 곳 고쳐서 닫히는 종류가 아니다 — **권위 상태 부재가 공통 뿌리**라 부분 보수는 회귀를 계속 만든다. `GroundProbe` 같은 순수 유틸만 선별 유지 |
| **폭탄 / 화염 장판** | 🔴 **분리** (신규 범위) | 지금은 `BombController` 하나가 투사체와 장판을 겸해 수명·크기·판정이 얽혀 있다. 폭발 시 장판을 **별도 스폰**하고 폭탄은 즉시 despawn |
| **Wells 상태 복제** | **23호 NetworkObject 에 싣는다** | Wells 는 **스폰되지 않는 중첩 NetworkObject** 라 자기 `NetworkVariable` 을 가질 수 없다 (감사 §3.1) |

## 5. 기존 코드에서 바꿔야 하는 것

설계가 요구하는 **기존 구조 변경**. 구현 슬라이스에서 실제로 부딪힐 지점이다.

1. **`AttackInfo.isGroggyAttack` 의 의미가 바뀐다.**
   지금은 *공격 자체*에 붙은 플래그라 "언제 맞았는가(카운터 창)"를 볼 수 없다.
   → 그로기 판정을 **보스 쪽 카운터 창 상태**로 옮기고, 이 플래그는 "이 스킬이 인터럽트 스킬인가"
   (= 슬롯이 `Interrupt` 인가)로 의미를 좁힌다. 몹(`MonsterBase`)도 같은 필드를 쓰므로 **몹 그로기 정책과
   함께 결정**해야 한다. 몹은 현행 유지가 안전하다.

2. **`HitFlash` 가 카운터 색을 덮어쓴다.**
   [HitFlash.cs](Assets/1.Scripts/Unit/HitFlash.cs) 는 `_originalColors` 를 **초기화 시점의 sharedMaterial
   색으로 캐시**하고 MaterialPropertyBlock 으로 복원한다. 카운터 노란색을 같은 경로로 칠하면
   **피격 한 번에 날아간다.** → `HitFlash` 에 베이스 틴트 오버라이드 진입점을 추가한다(카운터가 베이스를
   밀고, 피격 플래시는 그 위에서 Lerp). VFX 로 전환하면 이 진입점은 자동으로 안 쓰인다.

3. **`PlayerGrabbedState` 를 돌진 캐리에 재사용하려면 일반화가 필요하다.**
   [PlayerStateController.cs:700](Assets/1.Scripts/Player/PlayerStateController.cs:700) 이
   `instigator.GetComponentInChildren<GrabController>()` → `GrabSocket` 으로 **하드코딩**돼 있다.
   이동 권한 회수·물리 위임·복원은 이미 다 돼 있다. 단 **인터페이스로 바꾸는 것만으로는 부족하다** —
   보스에 잡기·돌진 소켓이 **둘 다** 붙으므로 타입 조회는 먼저 걸리는 쪽을 집는다.
   → **소켓 종류(`CarrySocketKind`)를 RPC 로 전파**해야 한다. → **이 파일은 은희 님 소유** (§6).

4. **`ChargeController.StartCharge` 의 `Clamp(playerCount, 1, 3)`** 를 고쳐야 3인 4기둥이 나온다.

5. **`WellsState` 에서 `Jump` 를 뺀다** (5 → 4 상태).

6. ~~**쿨다운 재등록 배선** — 선택기에 `AddType`/`RemoveType` 은 있으나 `BossBase` 가 호출하지 않는다.~~
   ✅ **해소(2026-08-07)** — `BossBase` 와 선택기를 통째로 폐기하고 `MonsterBase` 의 슬롯 쿨다운
   (`ConfigureAttackSlots`/`SetAttackCooldown`/`CooldownReady(slot)`)으로 대체했다.

## 5.1 🔴 구현 착수 후 발견한 공백 3건 (2026-08-07, 코드 실측)

정본이 예상하지 못한 것들이다. **S2·S5·S6·S7 이 이 공백을 공유**하므로 순서에 영향을 준다.

| # | 공백 | 실측 근거 | 영향 |
|---|---|---|---|
| G1 | 🔴 **플레이어가 CC 를 맞는 경로가 아예 없다.** `AttackInfo` 의 `knockbackStrength`/`staggerDuration` 을 **읽는** 코드가 플레이어 쪽에 0건이다 — 채우는 쪽(`FirstMeleeMainSkill:115`)만 있다. 몹은 `MonsterBase.ReceiveAttack` 이 해석하지만 플레이어엔 대응물이 없다 | 전수 grep (`Assets/1.Scripts/Player`·`Unit`) | 어퍼 에어본(S2) · 돌진 스턴(S5) · 차징 밀치기(S7) |

> ### ⚠️ G1 정정 (2026-08-07, S6-0 작업 중 실측) — **절반이 틀렸다**
>
> "CC 를 맞는 경로가 아예 없다"는 **과했다.** 정확히는 **두 가지가 갈린다**:
>
> | | 상태 | 근거 |
> |---|---|---|
> | **상태이상(CC) 적용** | ✅ **오늘 가능** | `StatusEffectController` 가 **Paladin 프리팹에 실제로 부착돼 있고**, `Apply(type, magnitude, duration, sourceId, maxStacks)` 로 **서버 적용 + 복제 + 스택**까지 완비돼 있다. `Unit.StatusEffects` 로 공개된다 |
> | `AttackInfo` 의 CC 필드 **자동 해석** | ❌ 없음 | 이 부분은 원래 판단이 맞다. 플레이어 피격 경로가 그 필드를 읽지 않는다 |
> | **위치 변위**(넉백 밀기·던지기) | ❌ 없음 | 플레이어 이동 권한이 **오너**라 서버가 위치를 써도 복제되지 않는다. `PlayerGrabbedState` 가 "물리를 시전자에게 위임"하는 이유가 이것 |
>
> **그래서 영향이 이렇게 줄었다**: 어퍼 에어본(S2)·돌진 스턴(S5)·차징 감속(S7)의 **상태이상 부분은
> 대기할 필요가 없다** — `unit.StatusEffects.Apply(...)` 로 지금 걸 수 있다.
> 막힌 것은 **밀기(변위)뿐**이다.
>
> 🔴 **어퍼 에어본을 "아직 넣지 말라"는 팀장 결정(§5.2)은 이 정정과 함께 재검토 대상이다** —
> 근거가 "적용할 경로가 없다"였는데, 상태이상 경로는 있다.
| G2 | **`MonsterMeleeAttack` 이 누굴 때렸는지 안 알려준다.** `_windowHits` 가 private. GauntletBot 이 `OnUppercutHit()` 을 빈 훅으로 남기고 끝낸 이유가 이것 | `MonsterMeleeAttack.cs:24` | CC 대상 지정 전반 |
| G3 | **`AreaZone` 구현이 0줄이다.** 장판은 폐기 대상 `BombController` 안에만 있다 | `find *AreaZone* *Hazard*` → 0건 | 폭탄(S9)·송전기(S7) 가 요구 → S6-0 으로 분리 |

> ⚠️ **G3 근거 정정 (2026-08-07)**: 처음에 "Jump(S6) 와 폭탄(S9) 이 같은 물건을 요구한다"고 적었는데
> **Jump 쪽은 틀렸다.** 정본 §10.5.2 가 명시한다 — **JumpAttack 의 빨간 장판(장판1·장판2)은
> 예고 표시(telegraph)이지 지속 영역이 아니다**(`AreaZone` 과 섞지 말 것. 그쪽은 `AoeTelegraph` 담당).
> 실수요자는 **폭탄(S9)** 과 **송전기(S7, `zoneDamage`/`zonePushForce`)** 다.
> S6-0 을 앞으로 당긴 결정 자체는 유효하다(의존 0 + S9 가 필요) — 근거만 정정한다.

## 5.2 팀장 확정 (2026-08-07 오후)

| 항목 | 확정 | 비고 |
|---|---|---|
| **어퍼 에어본** | 🔴 **아직 넣지 않는다** | G1 때문. `OnUpperHit()` 훅은 비워 둔 채 유지 |
| ~~**돌진 캐리-푸시**~~ | ~~매 틱 스턴+넉백 재적용~~ | ⚠️ **폐기(2026-08-07 오후)** — 아래 §5.2.1 참조 |
| **CC 적용 주체** | ✅ **결정됨** — 상태이상은 보스가 직접 `StatusEffects.Apply`, 변위는 플레이어 쪽 `Restrained.Push` | §5.2.1 |
| **`AreaZone` 순서** | **S9 → S6-0 으로 당김** | Jump·폭탄 공용 인프라 |
| **앵커 전환** | `MonsterMeleeAttack.SetColliderInfo()` 스왑 (컴포넌트 N개 아님) | ✅ 구현 완료 |
| **`CurrentPhase` 복제** | **하지 않는다** | `BossHealthHUD` 는 페이즈를 안 쓴다(체력만 폴링). 페이즈 연출이 필요해지는 S7 에서 재검토 |
| **연속 금지 임계** | **SO 로 노출** (`repeatBlockAfter`) | ✅ 구현 완료 |

## 5.2.1 🔴 은희 회신으로 뒤집힌 것 + R1·R2 종결 (2026-08-07 오후)

수령 문서: `player-interrupt-restrained-handoff.md` / 회신: [handoff-boss-reply-interrupt-restrained.md](Docs/tech/handoff-boss-reply-interrupt-restrained.md)

**R1 인터럽트 식별자 — ✅ 종결.** 단 형태가 바뀌었다: `AttackType` enum 값이 아니라
**`AttackInfo.isInterruptAttack` 플래그**(기존 `isGroggyAttack` 개명)다.
근거: `AttackType` 은 "어느 출처가 쐈나"이고 인터럽트는 그와 **직교한 능력**이다 —
enum 에 넣으면 "Q 슬롯인데 인터럽트인 스킬"을 표현할 수 없다.
→ 우리 쪽은 `IsInterruptAttack` **한 줄**만 갈아 끼운다(virtual 로 분리해 둔 값어치가 여기서 나왔다).
⚠️ `AttackType` 이 `{None, Default, Skill}` 로 축소됐다 — 우리는 `Default` 만 쓰므로 영향 0.

**R2 캐리 소켓 — ❌ 폐기.** 대체안 `Restrained` 상태(`RestraintMode{Carry, Push}`)로 간다.

**🔴 우리 결정이 뒤집혔다 — "매 틱 스턴+넉백 재적용" 폐기**

오전에 그렇게 확정했는데 실측이 반박했다(§C-1):
`Unit.Knockback(dir, strength)` **시그니처에 duration 이 없고** `AddForce(Impulse)` 를 1회 준다.
매 틱 재적용하면 **속도가 누적돼 플레이어가 튀어나간다.**
→ **`Restrained.Push` 채택.** 매 틱 "보스 앞 offset"을 계산하는 방식이라 보스가 가속·감속해도 자연히
따라붙고, 소켓 GameObject 도 방향/속도 동기화도 필요 없다(`float offset` 하나).

**팀장 답변 5건**

| # | 답 |
|---|---|
| 밀림 후 기절 | **보스가 건다**(지속시간이 보스 튜닝값) |
| 돌진 궤적 | **직선**(시작 시 방향 고정 + NavMesh 경계 클램프) |
| Q 슈퍼아머로 돌진 버티기 | ✅ **의도다**(기획 회의 확정) — 슈퍼아머면 **돌진을 무시하고 데미지만** 받는다 |
| `Push` 에 슈퍼아머 검사 | ✅ **넣는다.** 위 답의 결론이 곧 이것 — 검사가 없으면 "슈퍼아머면 안 밀린다"가 성립하지 않는다 |
| 적 공격 인터럽트 | **필요 없다.** 보스 그로기는 인터럽트 스킬 + 송전기만 |

> ⚠️ **위 3·4 는 초판과 반대다.** 초판은 "의도 아님 / 검사 안 넣음"이었고 기획 회의로 뒤집혔다.
> 낡은 판단을 남겨 두지 않기 위해 기록한다.

**🔴 그래서 `Push` 진입이 `bool` 을 반환해야 한다(회신 §2 로 요청함)**

3번이 "슈퍼아머면 무시 + 데미지만"이 되면서 C-2 의 비대칭이 다시 살아나는데,
은희 님 설계 안의 규칙("기절은 **밀림이 끝난 뒤에만**")을 그대로 따르면 자동으로 해소된다:

| 대상 | 밀림 | 기절 | 데미지 |
|---|---|---|---|
| 일반 | ○ | ○ (벽 도달 후) | ○ |
| **슈퍼아머** | ✕ (거부) | ✕ — **밀림이 시작되지 않았으니 "끝난 뒤"도 없다** | ○ |

→ 보스는 `BeginRestrainedByInstigator` 의 **반환값으로만** 갈라 처리한다. 밀린 대상만 목록에 넣고
벽 도달 시 그 목록에만 기절을 건다. **데미지는 밀림 여부와 무관하게 항상** 들어간다.

⚠️ **슈퍼아머 검사는 `Push` 한정이다.** `Carry`(잡기)에 넣으면 S4 가 회귀한다 —
잡기는 원래 슈퍼아머와 무관하게 걸린다.
⚠️ **밀림 도중 슈퍼아머 획득은 불가**하다(`Restrained` 진입만으로 `CanUseSkill` 이 false) →
진입 시점 1회 검사로 충분하다.

**🔴 머지 충돌 1곳**: `MonsterBase.TakeDamage` 의 `isGroggyAttack` 줄을 양쪽이 수정한다.
해소 = **둘 다 살리기**(플래그 이름은 은희 쪽, `if (!AutoHitReactions) return;` 는 보스 쪽,
**순서는 조기 반환이 먼저**). 머지 순서는 은희 → `development` → 보스가 받는 방향이 충돌 면적이 작다.

⚠️ **검증 함정(§C-3)**: 오프라인/미스폰이면 `CanWrite = IsSpawned && IsServer` 라 **상태이상이 안 걸린다.**
단독 Play 로 "기절이 안 된다"를 버그로 오진하지 말 것 — 보스 검증은 **MPPM 2인 이상**에서 판정한다.

## 5.11 송전탑 시스템 (2026-08-07)

신규 2파일: `Boss/BossChargingPylon.cs`(`Unit` 파생 기둥) · `Boss/BossChargeSequence.cs`(`IBossChargeSequence` 구현).

**실측**: `bossroom.prefab` 에 `Env_Mv_bosscharger_upper` **4개 + 레거시 `ChargingObject` 4개가 이미
붙어 있었다.** 그 코드가 요구사항(로컬좌표 상승/하강 · 활성 중에만 피격 · HP 0 이면 하강 · 멱등 종료)을
**정확히** 구현하고 있었으므로 로직을 그대로 승격했다(`Enemy/Boss` 는 폐기 대상 디렉터리라 이동).

**바꾼 것 2가지**

1. **정적 레지스트리**(`BossChargingPylon.Active`) — 기둥은 아레나에, 매니저는 보스에 붙으므로
   부모-자식 탐색으로 서로를 못 찾는다. `AreaZone.Active` 와 같은 패턴이다.
2. **완료 판정을 참여 집합 기준 `>=` 로** — 레거시의 `_destroyCount == _max` /
   `_reachedCount == _max` 는 **파괴와 도달이 섞이면 두 플래그 모두 영원히 false** 가 되어 교착했다.
   집합을 들고 세면 카운터가 어긋날 여지가 없다.

**🔴 스펙 전제 하나를 정정했다 — "도달"은 실패 조건이 아니다**

레거시 `ReachEvent` 는 "기둥이 **상승을 완료해 때릴 수 있게 됨**"에 발생하고, `TakeDamage` 가
활성 상태에서만 통하므로 **모든 기둥이 반드시 도달한다.**
즉 정본 §9.1 의 "하나라도 도달 → Rage" 는 **활성 직후 항상 실패**가 된다(성립 불가).
→ **실패는 제한시간 초과 단독**이다. 같은 절의 "제한시간 초과 → Rage" 와 일치하므로 스펙 안에서 모순이 없다.
`IBossChargeSequence` 주석의 합산 규칙 설명도 이 정정에 맞춰 읽어야 한다.

⚠️ **기둥 상승 시간은 제한시간에서 깎지 않는다**(`riseGrace`) — 깎으면 플레이어가 부술 시간이 줄어든다.

**프리팹 준비물**: ① `bossroom` 의 4개를 레거시 `ChargingObject` → **`BossChargingPylon`** 으로 교체
② 보스(또는 자식)에 **`BossChargeSequence`** 1개 부착.

**base 추가 2건** (전부 additive · 기존 11종 기본값 유지):

| 추가 | 무엇 | 왜 필요했나 |
|---|---|---|
| `MonsterBase.AutoHitReactions` (virtual, 기본 `true`) | 보스만 `false` — base 의 **자동 피격 반응 3종**을 끈다: ① `Hit` 경직 ② `isGroggyAttack` 그로기 누적 ③ `Knockback` | 정본 §1.1·§4 가 셋 다 부정한다(Hit=카운터 전용 / 그로기=인터럽트·송전기만 / 보스는 안 밀린다). **데미지·사망·HitFlash 는 그대로 돈다** |
| `MonsterBase.ForceHitReaction(duration, groggyAfter)` | `Hit` 강제 진입 + 타이머 종료 후 `Groggy` 로 이어붙이기 | `SetState`·`EnterHit` 이 private 이라 **보스가 `Hit` 에 들어갈 방법이 아예 없었다.** `groggyAfter` 가 없으면 `Hit` → `Idle` 이 되어 확정 스펙(Hit → Groggy/Break)이 성립하지 않는다 |

**`HitFlash` 에 베이스 틴트 진입점** (`SetBaseTint`/`ClearBaseTint`) — §5-2 예고대로 필요했다.
카운터 색을 같은 MPB 경로로 칠하면 플래시 종료 시 머티리얼 원색으로 되돌려 **피격 한 번에 날아간다.**
베이스로 넣으면 플래시가 그 색 **위에서** Lerp 하고 끝나도 그 색으로 복귀한다.

**설계 확정 4건**

1. **창 구간 = "공격 시작 → 히트 순간"** — Grab(잡기 판정 직전까지)·Dash(돌진 전 구간) 두 확정 스펙이
   **같은 구간으로 수렴**하므로 `AttackPhase` 기계 없이 성립한다. 히트에서 닫히므로 창에 실패 대가가 붙는다.
   `Attack` 을 벗어나는 모든 경로(취소·사망·그로기)에서도 닫는다 — base 에 Exit 훅이 없어
   `PlayStateAnimation` 이 파생이 상태 전이를 관측할 수 있는 유일한 지점이다.
2. **`Hit` 애니는 RPC 가 아니라 상태 복제로 돈다** — `PlayStateAnimation(Hit)` 에서 `hitReactionState`
   (getowned)로 CrossFade. 늦게 접속한 클라도 같은 애니를 본다(공격 애니와 달리 다지선다가 아니므로 가능).
3. **인터럽트 식별 = `isGroggyAttack`** (§5-1 확정을 그대로 구현). 은희 산출물 수령 후
   `|| attackType == <인터럽트>` **한 줄만** 추가하면 된다 — `IsInterruptAttack` 이 virtual 로 분리돼 있다.
4. 🔴 **정면 판정은 `sourcePosition` 이 아니라 공격자 루트를 쓴다.**
   `BaseAttack.CreateHitContext` 가 담는 `transform.position` 은 플레이어 루트가 아니라 **무기/히트박스
   자식**이다(이미 보스 쪽으로 뻗어 있음). 그 점으로 각도를 재면 전후 판정이 무기 길이만큼 편향된다.
   `sourceTransform.root.position` 을 우선 쓰고 없을 때만 폴백한다.

**S4 가 이어받을 것**: Grab 체인이 카운터로 취소될 때 `AttackPhase` 를 리셋해야 한다
(`ForceHitReaction` 이 상태를 `Hit` 로 바꾸므로 `HandleAttack` 은 안 돌지만, 다음 Grab 이
중간 phase 에서 시작하지 않게 진입 시 초기화할 것). → ✅ §5.5 에서 처리됨.

## 5.4 보스 방향 표시기 (신규 범위, 2026-08-07 팀장 지시)

로스트아크의 백어택/헤드어택 표시를 가져온다. 바닥에 **환형 섹터 2개**(전방/후방)를 그려
카운터·헤드어택·백어택 구역을 한눈에 보이게 한다. 구현 = `BossDirectionIndicator`
(순수 로컬 MonoBehaviour + `IBossTelegraph`).

| 결정 | 내용 | 근거 |
|---|---|---|
| 그리는 방법 | 코드 생성 환형 섹터 메시 2개 + 자식 렌더러 2개 + **공유 머티리얼 1개**(색은 MPB) | `AoeTelegraph.BuildDiscMesh` 선례. 아트·셰이더·렌더러피처 추가 0 |
| **플레이어 위에 안 그리기** | **정상 ZTest** (투명 큐). 플레이어는 불투명 큐라 깊이를 먼저 쓴다 → 자동으로 가려진다 | 흔한 `ZTest Always`(항상 위)를 **쓰지 않는 것**이 해법이다. 런타임에 머티리얼 큐를 검증해 경고 |
| **점프 예외** | 플래그가 아니라 **실측** — `GroundProbe` 로 발밑 바닥을 찾아 `보스Y − 바닥Y > airborneHideHeight` 면 숨김 | S6 구현에 의존하지 않고, 앞으로 어떤 공중 기믹이 와도 자동으로 맞다 |
| 높이 | `찾은 바닥 + 0.04` — 표준 0.05 보다 **의도적으로 낮게** | 절대 Y 금지 규칙(보스룸 0.50 / BossScene 0) 준수 + AoE 장판(0.05)이 항상 위에 와서 같은 평면 투명 정렬 깜빡임이 없다 |
| 회전 | **yaw 만** 월드로 직접 세팅(`LateUpdate`) | 보스 애니가 루트를 기울이면 링이 같이 기울어진다 |
| 전방 각도 | 🔴 **`counterFrontAngle` 을 그대로 읽는다**(새 필드 아님) | 표시와 판정이 **구조적으로 같은 값** — 링이 카운터에 대해 거짓말할 수 없다 |
| 후방 각도 | `backAttackAngle` SO 신규(60) | 백어택 판정이 구현되면 같은 값을 읽어야 어긋나지 않는다 |
| 카운터 강조 | 이 컴포넌트가 `IBossTelegraph` 를 구현 → 창이 열리면 전방 호가 강조색 | 링이 곧 텔레그래프 |
| 그림자 | 렌더러의 캐스팅·수신 OFF, 프로브 OFF | 바닥 장식이 그림자를 만들면 지면에 얼룩이 생긴다 |
| 사망 | `State == Dead` 면 숨김 | 디스폰 대기 중 방향 정보는 무의미 |

🔴 **`GetComponentInChildren<IBossTelegraph>()` 는 하나만 집는다** — 링과 전신 틴트를 같이 쓰면
한쪽이 조용히 안 돈다. `GetComponentsInChildren` 배열로 바꿔 **전부 구동**하도록 고쳤다.

⚠️ **머티리얼 애셋 1개가 필요하다** — 투명(Transparent) URP Unlit. `Shader.Find` 가 아니라
인스펙터 참조로 물려야 빌드에서 스트립되지 않는다(미니맵 전례). 비어 있으면 `LogError` + 컴포넌트 비활성.

**아직 안 한 것**: 전방 호가 "헤드어택 가능"까지 표현하려면 헤드어택 판정(은희)이 와야 각도가 확정된다.
지금은 전방 = 카운터·헤드 공용 구역으로 표시한다.

## 5.5 S4 Grab 체인 (2026-08-07)

`BossAttackPhase`(3층 3번째) 신규 — `None/Windup/Acquire/Hold/Throw/Recovery`.
**전부 `MonsterState.Attack` 안에서** 돈다(상태 추가 0, SpinnerBot 선례).

| 단계 | 무엇 | 종료 조건 |
|---|---|---|
| `Windup` | 잡기 모션. **카운터 창 열림** | 애니 이벤트 `OnAttackHit` → `Acquire` |
| `Acquire` | `grabRadius` 안 최근접 플레이어를 잡는다(유령 제외) | 성공 → `Hold` / 헛잡기 → `Recovery` |
| `Hold` | `grabTickInterval` 마다 `grabTickDamage`(전기) | `grabHoldDuration` 만료 → `Throw` |
| `Throw` | 던지기 모션 → 해제 + `grabThrowDamage` | `grabThrowDuration` 만료 → `Recovery` |
| `Recovery` | 복귀 경직(헛잡기 대가도 여기) | `grabRecoveryDuration` 만료 → `DecideNextAfterAction` |

**확정한 것**

1. **캐리는 오늘 동작한다.** `Player.BeginGrabbedByInstigator`/`EndGrabbedByInstigator` 가 이미
   **서버 가드 + 오너 ClientRpc** 까지 완비돼 있다. 소켓 부착(시각)만 `PlayerGrabbedState:700` 의
   `GetComponentInChildren<GrabController>()` 하드코딩 때문에 안 되는데, `followTarget == null` 이면
   위치 추종만 건너뛰고 **물리 위임·입력 차단은 그대로** 하므로 "제자리에 붙잡힘"으로 성립한다.
   → 은희 님 `CarrySocketKind` 수령 시 소켓만 붙는다(R2).
2. 🔴 **모든 이탈 경로에서 반드시 놓는다**(`AbortGrabChain`) — 카운터 성공 · 사망 · 디스폰.
   안 놓으면 **잡힌 플레이어가 이동 권한을 잃은 채 영구히 갇힌다**(보스가 죽으면 풀어 줄 주체가 없다).
   `PlayStateAnimation`(Attack 이탈 관측 지점) + `OnNetworkDespawn` 두 곳에 걸었다.
3. **`NotifyAttackEnd` 를 체인 중에는 무시한다** — 잡기 클립의 `OnAttackEnd` 가 base 로 가면
   `Hold` 에 들어가기도 전에 `Attack` 을 벗어난다. 체인이 자기 종료를 소유한다.
4. **`_stateTimer` 를 체인 전체 길이로 덮어쓴다**(관용구 3). 안 늘리면 `attackDuration` 만료가
   체인을 중간에 끊는다. 슈퍼아머도 같은 길이로 준다(중간 경직 방지).
5. **이벤트 유실 안전망** — 체인이 어느 단계에서든 `_stateTimer` 를 넘기면 `LogWarning` + 강제 종료.
   조용히 고착되지 않는다.
6. **Throw 변위는 미적용** — `OnGrabThrowRelease` 훅이 비어 있고 1회 경고를 남긴다.
   G1(플레이어 CC 수신 경로 없음) 결정 후 이 한 곳만 채우면 된다. **데미지는 지금도 나간다.**
   (G1 정정 후에도 **변위는 여전히 막혀 있다** — 상태이상만 열렸다.)

## 5.6 S6-0 `AreaZone` — 타입 있는 지속 영역 (2026-08-07 완료)

신규 2파일: `Monster/AreaZoneType.cs`(원소 타입 enum) · `Monster/AreaZone.cs`(`NetworkBehaviour`).
스키마는 정본 §11 `[장판]` — `zoneType · lifetime · growPerOverlap · maxRadius · tickDamage · tickInterval`.

**핵심 결정 5건**

1. 🔴 **효과를 `zoneType` 으로 분기하지 않는다.** 타입의 역할은 정본 §10.5.2 대로
   **"중첩 성장을 가르는 기준"뿐**이고, 데미지·둔화 수치는 **프리팹 필드로 저작**한다.
   타입별 `switch` 를 두면 밸런스가 코드에 박히고 죽은 분기의 원천이 된다
   → Fire/Swamp/Poison 프리팹이 각자 다른 숫자를 갖되 코드는 하나다.
2. **판정은 트리거가 아니라 한 순간의 `OverlapSphere`**(몬스터 규약 §3.4), 유닛당 1회, 유령 제외.
   그래서 **콜라이더를 붙이지 않는다** — 붙이면 `GroundProbe`·NavMesh·투사체 스윕에 끼어든다.
3. **상태이상은 `AttackInfo` 가 아니라 `unit.StatusEffects.Apply(...)` 로 직접 건다**(G1 정정 활용).
   지속시간을 틱 간격보다 살짝 길게 잡으면 **영역을 벗어난 직후 자연히 풀린다** —
   별도 이탈 판정이 필요 없다. `maxStacks: 1` 이라 매 틱 재적용이 스택을 쌓지 않고 갱신만 한다.
4. **바닥 정렬은 `GroundProbe` 하나로.** 절대 Y 금지(보스룸 0.50 / BossScene 0).
   🔴 경사면에서는 `ground.normal` 에 맞춰 눕힌다 — 폐기된 `MakeFloor()` 는 경사 회전을 계산해 놓고
   `Quaternion.identity` 로 덮어쓰는 버그가 있었다(§10.5.3). **그 버그를 옮기지 않았다.**
5. **반경만 복제**(`NetworkVariable<float>`) → 각 피어가 비주얼을 스케일. 위치는 스폰 메시지로 가고
   장판은 움직이지 않으므로 `NetworkTransform` 이 필요 없다.

**진입점**: `AreaZone.SpawnOrGrow(prefab, position, extraGroundMask)` — 서버 전용.
같은 타입이 그 자리에 이미 있으면 **스폰하지 않고 성장**시킨다(정본 §10.5.2 규칙 그대로).

⚠️ **성장 축은 중첩 성장(OnOverlap)만 넣었다.** 정본이 언급한 "시간 성장"은 JumpAttack 예고 표시가
담당하고(= `AoeTelegraph`, 지속 영역 아님) AreaZone 쪽 소비자가 없어 필드를 만들지 않았다.

⚠️ **아직 아무도 스폰하지 않는다** — 호출부는 S9(폭탄 폭발)·S7(송전기)다.
단독 검증은 **장판 프리팹을 씬에 배치**하면 된다(NGO 씬 오브젝트로 스폰돼 그대로 틱을 돈다).

**프리팹 준비물**: `NetworkObject` + `AreaZone` + 비주얼 자식(로컬 XY 지름 1 Quad/디스크 — `AoeTelegraph`
와 같은 규약) + 레이어 **`HazardArea(9)`** + `NetworkManager` NetworkPrefabs 등록.
레거시 23호 `Floor` 계열의 `HazardArea` 이관은 `Enemy/Boss` 폐기와 함께 사라지므로 **불필요**하다
(프리팹 재작성 시 잔존 여부만 확인).

## 5.7 S6 JumpAttack (2026-08-07)

시퀀스: `Leap`(도약+체공 · 착지점 확정 · 예고 장판 · 메시 off) → 착지점 이동 + 메시 on
→ `Land`(착지 클립, `OnAttackHit` 에 AoE) → `Recovery` → 재판단. 전부 `Attack` 안. 상태 추가 0.

**확정한 것 4건**

1. ⚠️ **애니메이터 `Jump` Int 를 쓰지 않는다.** 정본 §7 의 🔴 함정("`Jump` 를 0 으로 되돌리지 않으면
   다음 JumpAttack 이 영원히 안 나온다 — Leap 진입 조건이 `State==8 && Jump==0`")은 **그 Int 로 클립을
   넘기는 구조 때문에** 생긴다. 단계별 상태명 CrossFade(관용구 2)로 가면 그 함정이 **아예 성립하지
   않는다.** SO 에 `jumpHoverState`·`jumpLandingState` 를 두고 조회한다.
2. 🔴 **예고 장판은 보스 자식이 아니다.** 보스가 체공 중 착지점으로 순간이동하므로 자식이면 장판이
   따라가 버린다. 각 피어가 `jumpTelegraphPrefab` 을 **씬 루트에 로컬로** 띄우고 재사용한다
   (복제할 상태가 없는 순수 연출). 프리팹에 `NetworkObject` 를 붙이면 안 된다.
   보스 파괴 시 `OnDestroy` 에서 직접 지운다(부모가 없어 자동 소멸하지 않는다).
3. 🔴 **메시 토글은 `animator.transform` 하위만.** 보스 루트 하위에는 방향 표시기도 있어서
   전체를 끄면 그것까지 사라진다. `AoeTelegraph` 하위는 제외(HitFlash 와 같은 규칙).
4. **착지 AoE 반경 = 예고 반경(`jumpAoeRadius`) 동일 값.** 예고가 판정에 대해 거짓말하지 않게 —
   방향 표시기의 `counterFrontAngle` 재사용과 같은 원칙이다.
   착지 데미지는 **`OnAttackHit` 전용**(타이머 폴백 없음 — 정본 §3.3 비대칭 규칙).

**타겟**: 🔴 최원거리 플레이어. `base` 의 `_target` 은 최근접 락온이라 못 쓰므로 `jumpSearchRadius`
안에서 직접 훑는다(유령 제외). 쿨만이 게이트면 10초마다 기계적으로 나와 읽히므로 타겟 규칙이 의도를 만든다.

**이동**: `agent.Warp` 로 순간이동한다. `transform.position` 만 옮기면 에이전트 내부 위치가 갱신되지
않아 다음 이동에서 원래 자리로 튄다.

🔴 **체공 중 끊기면(카운터·사망) 메시가 꺼진 채 남아 보스가 투명해진다** — `AbortAttackChain` 에서
메시 복구 + 예고 제거를 함께 한다.

## 5.8 S7 페이즈 / 송전기 / 레이지 (2026-08-07)

**핵심 설계**: 송전기와 레이지를 **`weight = 0` 인 공격 행**으로 넣었다
(`BossAttackId.ChargeSequence` · `RageDash`, 끝에 추가).
가중치 0 이라 **룰렛이 절대 뽑지 않고** 페이즈 시스템만 직접 트리거하는데,
행으로 두면 **슈퍼아머·데드락 타이머·애니 상태명·쿨다운 기계를 전부 재사용**한다 →
새 상태 0, 새 base 훅 0.

🔴 **`No23.asset` 에 이 2행을 추가해야 한다.** 코드 기본값은 새 애셋에만 적용된다(교훈 #22/#55).
없으면 스폰이 아니라 **페이즈 통과 시점에** `LogError` 가 뜨고 시퀀스를 건너뛴다.

**정본 대비 고친 것**

1. 🔴 **페이즈 시퀀스 소비 타이밍** — 정본 §9 는 "`_pendingCharging` 은 **현재 행동이 끝난 뒤** 소비한다
   — 행동 도중 강제 중단하지 않는다"고 못박는다. `TakeDamage` 는 공격 한복판에도 들어오므로
   `EvaluatePhase` 에서 바로 시작하면 진행 중인 잡기·점프를 끊는다.
   → **`SelectAttackSlot` 에서 소비**한다. 그 함수는 `Idle`/`Walk` 에서만 불리므로 곧 "행동 종료 직후"다.
2. 🔴 **송전탑 수 1인 1 / 2인 2 / 3인 이상 4** — 레거시의 `Clamp(playerCount,1,3)` + `player3=3` 이
   3인에 3개만 켰다. `PylonCountFor` 가 인원을 **구간으로** 매핑해 이 버그를 원천 차단한다.
3. 🔴 **완료 판정 합산 규칙**을 `IBossChargeSequence` 문서에 못박았다 — 레거시의
   `_destroyCount == _max` / `_reachedCount == _max` 는 **파괴와 도달이 섞이면 둘 다 영원히 false** 가 되어
   차징에서 못 나왔다. `destroyed + reached >= max` 로 판정하고 결과는 이분법
   (전부 파괴 → `Groggy` / 하나라도 도달 → `Rage`).
4. **송전기 그로기는 Break 로 승격하지 않는다** — `EnterCounterGroggy(allowBreak: false)`.
   S3 에서 이미 그 플래그를 만들어 뒀다.

**송전탑 구현은 `IBossChargeSequence` 로 분리**했다. 구현이 없어도 시퀀스는 **일관되게 돈다** —
제한시간이 끝나면 스펙대로 실패 취급되어 레이지로 넘어간다(1회 경고를 남긴다).

**레이지**: 돌진 `rageDashCount`(3)회. 한 phase 안에서 **돌진 중 / 간격 대기**를 `_rageDashing` 으로 가른다
(타이머만으로는 두 구간을 구별할 수 없다). 돌진은 SpinnerBot 선례 —
`agent.speed` 배수 + `NavMesh.Raycast` 경계 클램프(낭떠러지 진입 불가) + 히트 윈도우로 유닛당 1회.
**카운터 창 없음**(차징 중엔 다른 애니가 안 나오고, 레이지는 실패 벌칙이라 쉽게 풀려선 안 된다).

🔴 **중단 시 뒷정리가 3가지다** — `AbortAttackChain` 에서 전부 한다:
① Rage 돌진 중이면 **에이전트 속도가 8배로 고정되고 히트 윈도우가 열린 채 남는다**
② 전기 장판·송전탑이 남아 **보스가 죽어도 아레나에 계속 피해를 준다**
③ Jump 체공 중이면 메시가 꺼진 채 남는다

⚠️ **미적용 2건** — 둘 다 플레이어 **변위** 경로가 없기 때문이다(G1 정정 참조).
`chargeShieldGainPerSec`(실드 개념이 PlayerSkill 머지에서 `Unit` 에서 제거됨) ·
정본의 `zonePushForce`(전기 장판 밀치기 — 데미지만 나간다).

## 5.9 S9 폭탄 — 수평 당구 (2026-08-07)

신규 2파일: `Boss/BossBombState.cs`(상태 4개) · `Boss/BossBomb.cs`(`NetworkBehaviour, IAttackReceiver`).
정본 §10.5.1 / §10.5.1.1 을 그대로 구현했다.

⚠️ enum 이름이 `BossBombState` 인 이유: 레거시 `Enemy/Boss/BombController.cs:6` 에 이미 `enum BombState`
가 있어 **컴파일 충돌**했다. 레거시는 전환 검증 후 삭제 예정이지만 지금은 공존하므로 이름을 갈랐다
(`BossBomb` 과 접두어가 맞아 오히려 일관된다).

**되쳐내기 경로를 실측으로 확정했다**

레거시 `BombController` 에는 **되쳐내기 진입점이 아예 없었다**(`Hold`/`Launch` 둘뿐).
실제 경로는 프리팹에 있었다 — `Bomb.prefab` 에 **`Hurtbox` + `Bomb`(`IAttackReceiver`)** 가 붙어 있고,
`Hurtbox.ResolveReferences` 가 `Unit` 이 없으면 **`GetComponentInParent<IAttackReceiver>()` 로 폴백**한다.
즉 `플레이어 기본공격 → BaseAttack.TryResolveHit → Hurtbox → IAttackReceiver` 가 살아 있는 경로다.
→ 새 `BossBomb` 이 `IAttackReceiver` 를 **직접 구현**해 중계 컴포넌트(`Bomb.cs`)를 없앴다.

**2단계 모델**

| | 단계 1 `Thrown` | 단계 2 `Resting`/`Sliding` |
|---|---|---|
| 중력 | **on** | **off** |
| Y | 자유(포물선) | **고정**(`FreezePositionY`) |
| 궤적 | 포물선 | 방향벡터 일직선 |
| 폭발 타이머 | 정지 | `Resting` 에서만 진행 |
| 되쳐내기 | 불가 | **`Resting` 에서만** |

🔴 **전이 시 세 가지를 함께 한다** — 중력 off · `FreezePositionY` · **y 속도 0** · 접촉점 스냅.
같이 하지 않으면 남은 y 속도가 constraint 와 싸워 떨린다(정본 명시).

**같은 콜라이더 쌍인데 상태에 따라 결과가 다르다** — 이게 이 기믹의 핵심 함정이다.

| 상황 | 결과 |
|---|---|
| `Thrown` 폭탄이 기존 폭탄과 만남 | 🔴 **둘 다 폭발** |
| `Sliding` 폭탄이 기존 폭탄과 만남 | **서로 밀림**(당구). 상대도 `Sliding` 이 되어 타이머가 멈춘다 |

**확정 규칙 반영**

1. **장판 겹침** — 폭발 시 `AreaZone.SpawnOrGrow` 를 쓰므로 같은 자리·같은 타입이면 **성장으로 갈음**된다.
   정본 규칙1("장판이 두 개 겹쳐 스폰되지 않게")이 **구조적으로 충족**된다(별도 처리 불필요).
2. **벽 1쿠션** — `wallBounceLimit`(기본 1) 만큼 튕기고 그 다음 벽 충돌에서 폭발. 되쳐내면 횟수가 리셋된다.
   반사는 **접촉 법선으로 직접 계산**한다 — 바운시 머티리얼에 의존하지 않아 결정적이다.
3. **밀려난 폭탄도 비행 취급** — `Sliding` 은 되쳐낸 쪽·맞은 쪽을 구분하지 않는다.
4. **`Sliding` 중에는 못 친다** — 🔴 **상태로 판단한다.** 속도로 판단하면 밀리다 순간적으로 느려지는
   프레임에 맞아 예외가 생긴다(정본 구현 메모).

**정지 판정**: `OnCollisionEnter` 만으로는 안 된다(부딪히고도 계속 밀려간다).
`velocity < restVelocityEpsilon` 이 `restHoldTime` 동안 유지되거나 `IsSleeping()` 이면 정지.

**유닛 접촉은 트리거로 받는다**(`OnTriggerEnter`, 허트박스가 트리거다) — 물리 응답으로 받으면
"**보스는 안 밀린다**"가 깨진다. 투척 중(`Thrown`)에는 스쳐도 안 터진다(공중 통과).

🔴 **남은 프로젝트 설정 작업**: 물리 충돌 매트릭스에서 **폭탄 레이어 ↔ 유닛 레이어의 물리 응답을 끊어야
한다**(정본 §10.5.4 — 문서에 절 자체는 없고 참조만 있다). 유닛 감지는 트리거로 하므로 끊어도 폭발은 산다.

**프리팹 준비물**: `NetworkObject` + `NetworkRigidbody` + `Rigidbody`(**useGravity on** / `FreezeRotation` /
`collisionDetectionMode = ContinuousDynamic`) + solid Collider + `Hurtbox`(ownerUnit **비움** → `IAttackReceiver`
폴백) + `BossBomb` + NetworkPrefabs 등록. ⚠️ **정지해도 논키네마틱 Dynamic 유지** — `isKinematic` 으로
재우면 당구가 안 된다. `Sleep()` 에 맡긴다.

✅ **S8 에서 던지는 쪽이 붙었다** — `BossWells` → `TwentyThreeBoss.SpawnAndThrowBomb`.

## 5.10 S8 Wells (2026-08-07)

신규 2파일: `Boss/BossWellsState.cs`(4상태) · `Boss/BossWells.cs`(**`MonoBehaviour`**).

**🔴 절대 `NetworkBehaviour` 로 만들면 안 된다**

Wells 는 프리팹의 **중첩 NetworkObject** 인데 NGO 는 그것을 스폰하지 않는다(씬 오브젝트만 지원).
`NetworkBehaviour.IsServer` 는 계산 프로퍼티가 아니라 **스폰 시 대입되는 필드**라 미스폰이면 영원히
false 다 — 과거에 이 클래스가 `NetworkBehaviour` 였을 때 **모든 애니메이션 이벤트를 조용히 삼켰다**
(2026-07-31 사고, `58278e9`). 서버 판정은 `NetworkManager.Singleton.IsServer` 로 한다.

**상태 복제를 23호에 실었다**

Wells 는 자기 `NetworkVariable` 을 가질 수 없으므로 `TwentyThreeBoss` 에
`NetworkVariable<BossWellsState> _wellsState` 를 두고 각 피어가 로컬 애니메이터만 구동한다.

🔴 **단 투척은 NetworkVariable 로 못 싣는다** — 같은 값이면 `OnValueChanged` 가 뜨지 않아
**두 번째 투척이 애니를 트리거하지 못한다.** 그래서 갈랐다:

| | 수단 | 이유 |
|---|---|---|
| 지속 상태(`Idle`/`Groggy`/`Dead`) | `NetworkVariable` | 늦게 접속한 클라도 현재 상태를 받는다 |
| 투척(일회성) | `ClientRpc` | 같은 값 재대입 문제가 없다 |

이 프로젝트의 "지속 = 상태 복제 / 다지선다·일회성 = ClientRpc" 분리와 같은 규칙이다.

**⚠️ 애니메이터 규약이 23호와 다르다** — 23호는 `State` Int 로 구동하지만 Wells 는 **전부 트리거**
(`IsThrow`/`IsGroggy`/`IsDead`/`IsInit`). Wells 의 `State` Int 는 **죽은 파라미터**다.
`Throw` 클립은 `hasExitTime: true` 라 **스스로 Idle 로 돌아온다**(23호와 반대) — FSM 이 되돌릴 필요가 없다.

🔴 **클립 이벤트 이름은 fbx(SVN)에 박혀 있다** — `ThrowBombEvent` / `BombDestroyEvent`.
`BossWells` 가 **그 이름 그대로** 공개 메서드를 노출한다. 바꾸면 이벤트가 조용히 무시된다.
(`BombHold` 라는 이벤트는 실재하지 않는다 — 레거시의 hold 는 BT 액션이 호출했다.
그래서 "손에 든 폭탄"은 **로컬 비주얼 토글**로 처리하고, 실물은 투척 프레임에 서버가 스폰한다.)

**주기와 동기화의 분리**(정본 §10 그대로)

- 폭탄 주기는 **23호 상태와 무관하게 Wells 가 자기 `Update` 로** 돈다.
- 23호가 밀어주는 것은 **그로기·사망 억제뿐**이며 **단방향**이다 —
  🔴 Wells 가 23호를 폴링하면 순서 의존이 생긴다.
- 억제가 풀리면 주기를 처음부터 다시 센다(그로기 직후 즉시 투척 방지).

⚠️ **`Update` 를 보스 파생에 선언하면 안 된다** — `MonsterBase.Update()` 를 가려 **FSM 이 통째로
멈춘다.** 그래서 주기를 Wells 자기 `MonoBehaviour.Update` 에 뒀다(base 를 가리지 않는다).
이 제약이 "Wells 가 자기 주기를 돈다"는 스펙과 마침 맞아떨어졌다.

**투척 방향**: 보스 전방 기준으로 `bombThrowPitch`(상향) + `spreadAngle`(좌우) 을 조합한다.
🔴 **소켓 회전에 의존하지 않는다** — 아트 임포트 회전 때문에 고정 방향이 뒤집혀 있던 전례가 있다.

**프리팹 준비물**: Wells 오브젝트에 `BossWells` + Wells 전용 `Animator` + `bombSocket`(손) +
(선택) `heldBombVisual`. `Hurtbox` 는 **붙이지 않는다** — Wells 는 피격 대상이 아니고 HP 는 23호 것뿐이다.

## 6. 네트워크 / 권한 가정

- 보스 FSM·판정·이동·페이즈·사망·**카운터 창 판정** = **전부 서버(호스트)**.
- 카운터 성공 판정은 **서버에서만** 한다: 플레이어 인터럽트 스킬의 히트가 서버 경로
  (`BaseAttack → ReceiveAttack`)로 들어온 시점에 보스의 창 상태 + 정면 각도를 서버가 본다.
  클라 예측 없음 — 오판정 시 그로기가 클라마다 갈린다.
- 카운터 창은 `NetworkVariable<bool>`(Server write / Everyone read) → `OnValueChanged` 로
  클라가 `IBossTelegraph` 를 구동. **표현만 클라, 판정은 서버.**
- 돌진 캐리 중 플레이어는 `PlayerGrabbedState` 로 **이동 권한이 서버에 위임**된다 (기존 잡기와 동일).
- 데미지 유입은 `BaseAttack → ReceiveAttack` 서버 경로만. 오너→서버 직접 데미지 RPC 금지.

## 7. 리스크와 오픈 이슈

| # | 리스크 | 대응 |
|---|---|---|
| R1 | **보스가 "이 히트 = 인터럽트 스킬"을 판별할 수 없다.** `AttackType`(`None/Default/Q/E/R`)에 우클릭 슬롯 값이 없다. 우리 브랜치의 Paladin 프리팹도 `interruptSkill: {fileID: 0}` 로 미배선 — 다만 **스킬 자체는 이미 있다**(팀장 확인), 식별 정보만 없는 상태다 | **요청 완료(8/7 17:00).** 임시 테스트 스킬은 만들지 않고 **기다린다.** 수령 전까지 S3 의 나머지(창 개폐·정면 판정·그로기 전이·텔레그래프)는 먼저 확정한다 |
| R2 | **`PlayerStateController` 일반화가 은희 님 소유 파일** | **요청 완료(8/7 17:00).** 인터페이스 한 줄이 아니라 **종류 인자를 RPC 까지 전파**하는 변경이다(소켓이 2개라 타입 조회로는 모호). 수령 전까지 S5 는 뒤로 미룬다 |
| R3 | **징크스(원거리) 스킬 SO 가 없다** — `9.ScriptableObject/Player/` 에 Garen 뿐 | "전 캐릭터 카운터 가능"은 징크스 구현 시 자동 충족되도록 **슬롯 기반**으로 설계(캐릭터별 분기 금지) |
| R4 | 몹(`MonsterBase`)이 `isGroggyAttack` 을 공유한다 | 보스만 의미를 바꾸고 몹은 현행 유지. 필드를 나누는 쪽이 안전하면 구현 시 분리 |
| R5 | BT → FSM 전환 중 **두 구현이 동시에 살아 있는 기간** | 프리팹에서 `BehaviorGraphAgent` 를 끄는 시점을 S1 완료 직후로 못박는다. 양쪽이 같이 도는 상태를 만들지 않는다 |
| R6 | `Assets/8.BehaviorTreeGraph` 는 **의도 추출용 참고 자료** | 전환 완료까지 **수정 금지**. 삭제는 전환 검증 후 별도 커밋 |

## 8. 범위 밖 (이번에 안 함)

- 코드·프리팹·BT 에셋 수정 (이번 산출물은 문서까지)
- 인터럽트 식별자 추가 · 캐리 소켓 종류 전파 (플레이어 = 은희 님, 요청 완료 · 8/7 17:00)
- 헤드어택 판정 (은희 님 · 구현되면 정면 판정을 교체)
- 밸런스 수치 확정 — §9 의 TBD 는 **인스펙터/SO 노출까지만** 하고 값은 비워 둔다
- 보스 등장 연출 (`BossEncounterDirector` 계열) — 이미 동작 중, 이번 재설계 대상 아님
- 사운드 · VFX

## 9. 값이 비어 있는 파라미터 (노출만, 값은 TBD)

전부 **인스펙터 또는 SO 노출**로 잡고 값은 팀장이 플레이하며 조절한다.

- 근접↔원거리 **전환 거리 임계값**, 각 공격의 거리창
- JumpAttack: 체공 시간(JumpTime), 장판 확장 시간, 착지 데미지, AoE 반경
- DashAttack: 돌진 속도, 캐리 밀기 속도, **벽 충돌 시 플레이어 스턴 시간**, 데미지
- Grab: `Hold` 지속, 전기 데미지, `Throw` 거리/데미지
- 훅/어퍼: 데미지, Airborne 높이·지속
- 송전기: **제한시간**, 기둥 HP/방어력, 실드 점증 속도
- 레이지: 돌진 3회 각각의 데미지·간격
- 페이즈별 차등 배수

## 10. 수용 기준

1. 보스가 **모든 공격이 쿨일 때도 멈춰 서 있지 않는다** (폴백 행동).
2. 같은 공격이 **연속 3회 이상 나오지 않는다** (연속 방지).
3. 카운터: 창 안 정면 = 그로기 / 창 안 후방 = 실패 / 창 밖 = 실패 — **4조합이 서버 기준으로 일관**.
4. `GroggyCount` 5회 도달 시 `Break`(5초). 송전기 그로기는 카운트를 올리되 Break 로 승격되지 않는다.
5. Grab 카운터 실패 시 `Hold` → `Throw` 가 끊기지 않고 진행된다.
6. Dash 피격 플레이어가 **벽까지 밀린 뒤 스턴**, 보스는 패턴으로 복귀한다.
7. 3인 접속 시 송전탑이 **4개** 활성된다.
8. 23호 그로기 시 Wells 도 그로기 애니로 동기화된다.
9. 전원 Soul → 보스 `Idle`, **체력 회복 없음**, 부활 시 이어서 교전.
10. MPPM 2~3인에서 호스트/클라 상태 애니가 갈리지 않는다.

## 11. 검증 계획

- **컴파일 0에러/0경고** + 콘솔 에러 0건.
- **MPPM 2인**: 카운터 4조합 · Grab 체인 · Dash 캐리 · 페이즈 2회 통과.
- **MPPM 3인**: 송전탑 4개 · 전원 Soul → Idle → 부활 후 교전 재개.
- 서버/클라 **상태 로그 대조** — `_state` 전이 순서가 동일한지.
- 카운터는 R1 때문에 **임시 인터럽트 스킬로만 검증 가능** — 실물 검증은 은희 님 구현 후로 미룬다.
  이 한계는 검증 보고에 명시한다.

---

## 결정 완료 (2026-08-06 팀장)

R1·R2 **둘 다 은희 님께 요청 완료. 기한 = 2026-08-07(금) 17:00.**
인계 문서: [handoff-player-carry-socket.md](Docs/tech/handoff-player-carry-socket.md)

- **R1 (인터럽트)** — 단죄의 방패 자체는 **이미 있고, 인터럽트 식별 정보만 없다.**
  `AttackType` 에 우클릭 슬롯에 해당하는 값이 없어서 보스가 판별할 수 없는 상태다. → 값 추가 요청.
  임시 테스트 스킬은 **만들지 않는다** (기다린다).
- **R2 (캐리 소켓)** — 요청 완료. 보스 쪽에 별도 캐리를 만들지 않는다.

### 그래서 일정이 이렇게 갈린다

| 시점 | 내용 |
|---|---|
| ~8/7(금) 17:00 | **FSM 상세 구성 확정** — 위 두 건에 의존하지 않는 전부 (S1·S2·S4·S6·S7·S8 설계 확정) |
| 8/7 17:00 | 은희 님 산출물 수령 → S3(카운터)·S5(Dash 캐리)의 마지막 배선 확정 |
| 주말 | **전 슬라이스 구현 + 테스트** |

**의존 없이 지금 확정 가능한 것 / 수령 후에만 가능한 것**

- 의존 없음: 상태 전이표, 선택기 4단(게이트·가중치·연속방지·폴백), 쿨다운 재등록, 애니 이벤트 배치,
  Grab 체인, JumpAttack, 페이즈·송전기·차징·레이지, Wells FSM, `IBossTelegraph`
- **A 수령 후**: 카운터 성공 판정의 "인터럽트 스킬인가" 조건 한 줄
- **B 수령 후**: Dash 캐리의 `BeginGrabbedByInstigator(boss, Dash)` 호출 + 돌진 소켓 배치
