# 보스 FSM 설계 문서 (BossBase — 웰즈 & 23호)

> 목적: `BossBase` 코드 FSM 스켈레톤의 설계를 문서화한다. 팀장 검토 + Codex 작업 지시용.
> 대상 파일: `Assets/1.Scripts/Monster/Boss/BossBase.cs` (+ `BossState`, `BossBasicAttackType`, `BossBasicAttackChoice`).
> 작성 2026-07-20. **"현재 구현" / "합의된 목표 방향" / "연기된 훅" / "원본 BT 의도" / "GAP·오픈이슈"** 로 구분.

---

## 0. 한눈에

- **모델**: `MonsterBase`와 동일한 *순수 코드 FSM + 서버 상태소유 + 클라 애니재생*. BT(`BehaviorGraphAgent`) 미사용.
- **상속**: `BossBase : Unit` (Enemy/MonsterBase 상속 금지). 데미지 유입 = `ReceiveAttack → TakeDamage(AttackInfo)` 서버 경로.
- **권한**: 스폰·FSM·이동목표·데미지·페이즈·사망 = **전부 서버(호스트)**. 클라 = `_state` NetworkVariable 복제받아 애니만 재생.
- **이 스켈레톤 범위**: FSM + 거리창·가중치 공격선택 + 페이즈 골격 + 근접공격 2종(Slam/Sweep). **잡기/폭탄/송전탑/차징전체/Dash/Jump는 virtual 훅만** 남기고 연기.

---

## 1. 상태 (`BossState`)

| 상태 | 의미 | 진입 | 탈출 |
|---|---|---|---|
| `Idle` | 비교전 대기 | 초기화, 행동 종료 후 | 타겟 감지 → Chase |
| `Chase` | 추격/교전 접근 | 사거리 밖 | 사거리 내 & 쿨 준비 → Attack |
| `Attack` | 기본 공격 수행(windup→hit→duration) | 공격 선택됨 | duration 종료 → DecideNext |
| `Charging` | 페이즈 전환 강제 진입(쉴드/버프+브로드캐스트) 골격 | HP 임계 하향 통과 | chargingDuration 종료 → Idle |
| `Groggy` | 그로기(다운) 골격 | 그로기 누적 임계 | groggyDuration 종료 → DecideNext |
| `Break` | 파츠 파괴/무력화 골격(연기 메커닉 진입점) | `EnterBreak(duration)` 호출 시 | duration 종료 → DecideNext |
| `Dead` | 사망 → 디스폰 | HP 0 | (없음) |

- 상태 복제: `NetworkVariable<BossState>`(Server write / Everyone read). `OnValueChanged` → `PlayStateAnimation`(클라 애니).
- 이동 블렌드용 `_animSpeed`(NetworkVariable<float>)도 서버가 agent 속도로 갱신 → 클라 Animator `animSpeedParam` 구동.

### 서버 틱 루프 (`TickServer`)
1. `status.BlocksMovement`면 `StopAgent()`.
2. 상태 스위치: Idle/Chase→`HandleSeekAndCombat`, Attack→`HandleAttack`, Charging/Groggy/Break→각 핸들러, Dead→no-op.

### 교전 루프 (`HandleSeekAndCombat`)
1. `_pendingCharging`(페이즈 통과 대기)면 즉시 `EnterCharging`.
2. 타겟 락온(유효하면 유지, 없을 때만 재탐색) → 없으면 Idle.
3. `FaceTarget`.
4. `!BlocksAttack && CooldownReady()`면 `attackChoice.GetRandomAttack(dist)` → None 아니면 `StartAttack(type)`.
5. 공격 미선택: 사거리 밖 → Chase+`MoveAgentTo`, 안 → 정지 대기.

---

## 2. 공격 선택 (거리창 + 가중치 + 쿨다운 재등록)

`BossBasicAttackChoice : BaseAttackChoice` — 기존 `TwentyThreeBasicAttackChoice` BT 패턴을 코드로 미러링.

- **거리창 필터**: 각 공격이 `[minDistance, maxDistance]`를 가지며, 현재 거리가 창 안일 때만 후보.
  - Slam: `[0, 3]`, 가중치 60. / Sweep: `[0, 4]`, 가중치 40. (인스펙터 조절)
- **가중치 룰렛**: 유효 후보들의 percentage 합 기준 랜덤 선택. 후보 없음/합 0 → `None(0)` → 공격 스킵.
- **쿨다운 재등록**: `RemoveType(type)`로 방금 쓴 공격을 후보에서 빼고, 잠시 뒤 `AddType(type)`로 복구 = "이 공격 지금 쿨다운" 표현. (현재 BossBase는 `AddType/RemoveType`를 아직 호출하지 않음 — **배선 필요**, GAP 참조.)
- 반환은 `(int)BossBasicAttackType`.

---

## 3. 공격 실행 (`StartAttack` → `HandleAttack`)

**현재 구현 (코드 타이머 구동):**
- `StartAttack(type)`: 타입별 `GetAttackTiming`으로 windup/duration 확정 → `_lastAttackTime` 기록, `_stateTimer=duration`, `StopAgent`, `FaceTarget`, (옵션)슈퍼아머, `ExecuteSpecialAttack(type)`(연기 no-op), `SetState(Attack)`.
- `HandleAttack(dt)`: `_stateTimer` 감소, `FaceTarget`, `elapsed >= windup`이면 `meleeAttack.Hit()` 1회, `_stateTimer<=0`이면 `DecideNextAfterAction`.
- 타이밍: Slam `windup 0.45 / dur 1.1`, Sweep `windup 0.35 / dur 1.0` (인스펙터).

> **⚠️ 합의된 목표 방향 (2026-07-20): 공격 타이밍을 애니메이션 이벤트로 전환.**
> 플레이어 `DefaultAttackController`와 동형으로, 보스/몹 공격도 **애니 이벤트(Hit/End)** 로 히트·종료를 구동한다.
> `attackWindup`/`attackDuration` 코드 타이머는 **이벤트 누락 대비 폴백**으로만 남긴다.
> → BossBase에 애니이벤트 수신 함수(예: `OnAttackHitEvent`, `OnAttackEndEvent`) + 릴레이 추가 필요.

---

## 4. 페이즈 시스템

- `EvaluatePhase()`(TakeDamage 내부): `hpPercent = CurrentHealth / maxHp`.
  - `<= phase3HpPercent(0.33)` → 목표 페이즈 2, `<= phase2HpPercent(0.66)` → 1, 그 외 0.
  - **하향으로만** 증가(`target > _phaseIndex`). 처음 통과 시: `_phaseIndex` 갱신 + `_pendingCharging=true` + `OnPhaseChanged(idx)`.
- `_pendingCharging`은 **현재 행동 종료 후**(DecideNext) 또는 다음 seek 틱에 `EnterCharging`으로 소비 → 행동 도중 강제 중단 안 함(연출 완결 보장).
- `EnterCharging`: `chargingDuration`(1.5s) 동안 SuperArmor 부여(슬라이스1은 쉴드/버프 대체) + `BroadcastBossState(Charging)` → Idle 복귀.

---

## 5. 피격 / 그로기 / 사망

- `TakeDamage(AttackInfo)`: base(방어/쉴드/체력/복제) → 서버 가드 → HP 0이면 `EnterDead` → `EvaluatePhase` → `isGroggyAttack`면 그로기 누적, `maxGroggyCount` 도달 시 `EnterGroggy`.
  - **주의(현재)**: MonsterBase와 달리 BossBase는 일반 피격 시 `Hit`(경직) 상태가 **없다**(보스는 아무 공격에나 경직되면 안 되므로 의도적). 그로기만 별도.
- `EnterDead`: agent/콜라이더 off → `Dead` → `OnDeath()` 훅 → `IDeathEffect` 있으면 재생 후 디스폰, 없으면 `despawnDelay` 후 디스폰. 임시 사망연출(디졸브/스케일+틴트) 포함.

---

## 6. 연기된 메커닉용 virtual 훅 (이후 슬라이스/Codex에서 override)

| 훅 | 호출 시점 | 기본 | 채울 내용 |
|---|---|---|---|
| `OnPhaseChanged(int page)` | 페이즈 하향 통과 | no-op | 페이즈별 패턴 강화/신규 공격 개방 |
| `ExecuteSpecialAttack(BossBasicAttackType)` | StartAttack 내 | no-op | 잡기/폭탄/송전탑/차징전체/Dash/Jump 실행 |
| `BroadcastBossState(BossState)` | EnterCharging 등 | no-op | BT/Events/`BossStateChanged` 채널로 상태 방송(연출 트리거) |
| `OnDeath()` | EnterDead 단일 지점 | no-op | 드롭/보상/처치연출/다음 페이즈 전환 |
| `EnterBreak(duration)` | (미호출) | 상태 진입 | 파츠 파괴/무력화 진입점 배선 |

---

## 7. 원본 BT 의도 (리버스 엔지니어링 — 재작성 시 보존)

> 출처: 기존 웰즈&23호 BT 5그래프 + 컨트롤러. FSM 재작성 전 보존해야 할 핵심 의도.

1. **거리창 + 가중치 공격 선택** — §2에 반영 완료.
2. **쿨다운 재등록**(Remove→나중에 Add) — 선택기엔 구현, **BossBase 배선 미완**(GAP).
3. **잡기 체인** — 잡기 성공 → 끌기 → 내려찍기/던지기 상태 기계. (`ExecuteSpecialAttack`+전용 상태로 이식 예정.)
4. **폭탄 상태 기계** — 폭탄 생성/부착/카운트다운/폭발 단계. (별도 컴포넌트 + 보스 상태 연동.)
5. **`BossStateChanged` 채널** — 보스 상태 변화를 연출/UI/BT에 방송. → `BroadcastBossState` 훅으로 대체 배선.
6. **송전탑(Pylon) 기믹** — 페이즈 중 송전탑 소환/파괴 루프.

---

## 8. GAP · 오픈 이슈 (팀장 결정 / Codex 작업 대상)

- [ ] **공격 타이밍 애니이벤트화** (§3 목표 방향) — Hit/End 이벤트 함수 + 릴레이. 클립에 이벤트 삽입(에셋).
- [ ] **넉백/CC/슈퍼아머 = Unit 통합** — 현재 보스는 `MonsterStatusEffect`(몹용) 참조. 합의된 방향은 슈퍼아머·CC를 **Unit 레벨로 통합**(플레이어와 정합). 넉백은 `Unit.Knockback` 공통 진입점(LinearKnockback 컴포넌트 or IKnockbackable override).
- [ ] **쿨다운 재등록 배선** — StartAttack 후 `RemoveType`, 타이머 뒤 `AddType` 호출 루프.
- [ ] **송전탑 개수** — 원본 3 vs 4 미확정.
- [ ] **페이즈 임계값** — `0.66 / 0.33`은 임시. 원본 임계 미확인.
- [ ] **차징 근접 데미지** — 원본에서 차징 중 근접 판정 존재 여부 미발견.
- [ ] **일반 피격 경직 정책** — 보스는 Hit 경직 없음(그로기만). 특정 약점/타이밍에만 경직 줄지 결정.
- [ ] **Wells(2호기) vs 23호 역할 분리** — 현재 스켈레톤은 23호 기준. Wells 별도 브레인/파츠 관계 정의 필요.

---

## 9. 네트워크 계약 요약

- 서버: `_state`/`_animSpeed`/HP/상태이상 = NetworkVariable(Server write). FSM·판정·이동·페이즈·사망 전부 서버.
- 클라: NetworkTransform로 위치, `OnValueChanged`로 상태→애니. 판정 관여 없음.
- 데미지 유입: 오너→서버 직접 데미지 RPC 금지. `BaseAttack → ReceiveAttack` 서버 경로만.
