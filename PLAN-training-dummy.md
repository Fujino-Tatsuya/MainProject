# PLAN — 허수아비 (Training Dummy) · 2026-09-16

> 브랜치 `feature/training-dummy` (development 에서 분기).
> grill 워크플로우로 5라운드 합의 후 사용자 승인(2026-09-16). 이 문서가 잠긴 계획이다.

## 목표와 범위

연습장에 놓을 **표적 오브젝트 1종**을 만든다. 플레이어가 스킬을 때려보고
데미지 수치·판정·타이밍을 확인하는 용도다.

**이번 브랜치에 포함:**
- `TrainingDummy` 스크립트 일체 + 프리팹 + 네트워크 프리팹 등록

**의도적으로 제외 (별도 작업):**
- 연습장 씬 자체와 진입/퇴장 흐름 — `NetworkLoadingFlowController` 가
  `targetSceneName = "MapScene"` 를 하드코딩하고 있어 네트워크/SceneManagement 작업이다 (은희 영역)
- 검증 씬 — **사용자가 직접 만든다**
- DPS 미터
- 상태이상 아이콘 UI
- `Unit` 의존을 인터페이스로 걷어내는 코어 리팩터링 (아래 "남은 설계 부채" 참조)

## 분류 — 몬스터가 아니라 맵 오브젝트다

`AGENTS.md` 상 **몬스터 전체는 경석(팀장) 단독 담당**이다. 허수아비는 그 경계를 침범하지 않는다.

- `MonsterBase` / `MonsterDataSO` / `MonsterStatusEffect` / FSM / `NavMeshAgent` / 리쉬 — **하나도 안 쓴다**
- 특히 `MonsterBase.HandleSeekAndCombat` 의 리쉬 복귀는 `Revive()` 를 불러 **체력을 최대로 되돌린다.**
  허수아비의 자체 회복 규칙과 정면 충돌하므로 상속하면 안 된다
- 스크립트는 `Assets/1.Scripts/Map/TrainingDummy/` — `BreakableCrate` 가 `Map/Breakable/` 에 있는 것과 같은 결

## 접근 — `Unit` 만 상속한다

`TrainingDummy : Unit`. `Unit` 은 "재활용하는 남의 코드"가 아니라 **이 프로젝트의 데미지 파이프라인 그 자체**다
(서버 권한 피해 적용 · HP `NetworkVariable` 복제 · 클라 연출 이벤트).

`IAttackReceiver` 만 구현하는 `BreakableCrate` 방식을 검토했으나 **요구사항 3개가 깨진다**:

| 필요한 것 | 요구하는 타입 | 근거 |
|---|---|---|
| 데미지 숫자 | `Unit` 구체 타입 | `FloatingPopupRequest(Unit target, …)` — `FloatingDamageSettings.cs:29` |
| 히트플래시 | `Unit` 구체 타입 | `HitFlash._unit = GetComponent<Unit>()` — `HitFlash.cs:37` |
| 궁극기 조준 | `Unit` 구체 타입 | `PlayerSkillTargeting.ResolveUnit` → `GetComponentInParent<Unit>()` |
| 넉백 | `IKnockbackable` 만 | `LinearKnockback` 은 `Unit` 무관 — 그대로 재사용 |

## 확정 사양

### 스탯 · 체력
- 최대 HP **500** / 방어력 **0** / 1종, 인스펙터 고정값
- **죽지 않는다. 체력 하한 1.**
  0 을 허용하면 `PlayerSkillTargeting` 이 `CurrentHealth <= 0` 을 **InvalidTarget** 으로 처리해
  (`:201`, `:293`, `:387`) 회복 전까지 궁극기·타겟팅 스킬 조준이 끊긴다.
  하한 1 이면 `Unit.Died` 도 발생하지 않는다
- **회복**: 마지막 피격 후 **3초** 대기 → **1초에 걸쳐** 최대치까지.
  회복 중 피격 시 **즉시 중단 + 대기 타이머 리셋**
- 타이머 산술은 `TrainingDummyRegen` (순수 C#, MonoBehaviour 아님) 으로 분리 — 나중에 EditMode 테스트를 싸게 붙이려고

### 데미지 표시 — 명목 피해
- HP 100 남았는데 500 맞으면 **500** 이 뜬다. HP 1 에 붙어 있어도 계속 뜬다
- `Unit` 기본 경로는 **실제 HP 델타**를 띄우므로(`Unit.cs:533`) 쓸 수 없다
- **`Unit.cs` 는 건드리지 않는다.** 대신 허수아비 전용 RPC → `TrainingDummyDamagePresenter` →
  `FloatingDamageSpawner.Submit(this, …)` 직접 호출
- `Unit.OnNetworkSpawn` 이 자동 부착하는 `FloatingDamagePresenter` 와 `UnitCameraFeedbackReporter` 는
  스폰 직후 **제거**한다. 안 그러면 같은 타격에 숫자가 두 번 뜬다
- 타격 카메라 쉐이크도 전용 presenter 가 같이 맡는다 (공격자 clientId 를 RPC 로 함께 보냄)

### HP 바
- **신규 전용 컴포넌트** `TrainingDummyHealthBar`.
  기존 `UnitOverheadHealthBar` 는 `GetComponentInParent<Player>()` + `!IsOwner` 하드코딩이라
  (`UnitOverheadHealthBar.cs:18`) 재사용 불가. 고치면 원격 플레이어 체력바에 회귀 위험 + `UI/Combat` 은 은희 영역
- 머리 위 월드스페이스 빌보드 / **항상 표시** / **게이지 + 숫자(`342 / 500`)** / 거리 컬링 없음

### 피격 반응
- **넉백 받는다** — `LinearKnockback` + **Rigidbody(회전 3축 전부 고정)**. 밀리되 안 넘어진다
- **상태이상 받는다** — **`StatusEffectController`**(플레이어형) 부착.
  몬스터형 `MonsterStatusEffect` 는 `AreaZone` 장판이 안 걸리고 `GetStatMultiplier` 가 항상 `1f` 라 못 쓴다
- **상태이상 시각화는 범위 밖.** 허수아비는 안 움직여서 슬로우/루트/침묵이 눈에 안 보이고,
  Airborne 을 실제로 띄우려면 허수아비 전용 이동 처리를 새로 짜야 한다
- **자리 복귀**: 스폰 지점에서 **5m** 벗어나면 **즉시 순간이동 복귀 + 회전 복구 + 넉백 속도·상태이상 전부 해제**.
  해제를 빼면 `복귀 → 남은 속도로 재이탈 → 복귀` 루프가 된다

### 네트워크 · 레이어
- **NetworkObject** / 서버 권한 / `NetworkTransform AuthorityMode = 0 (Server)` — `ChompBot` 과 동일
- `Assets/DefaultNetworkPrefabs.asset` 등록
- 루트 **`Enemy`(8)** + 자식 Hurtbox **`EnemyHurtBox`(14)**.
  `CombatTarget`(18) 은 레이어만 정의돼 있고 **C# 어디에서도 참조되지 않는다** — 쓰면 아무 스킬도 안 맞는다.
  플레이어 스킬 SO 의 `hittableLayers` 는 전부 Enemy + EnemyHurtBox 조합이다

### 비주얼
- 프리미티브 + 머티리얼. SVN 에 허수아비 아트가 아직 없다 → 나오면 자식만 교체

### 배치
- 스크립트 `Assets/1.Scripts/Map/TrainingDummy/`
- 프리팹 `Assets/2.Prefabs/` 직하 (사용자가 추후 정리)

## 리스크와 알려진 한계

1. **체력 1 에 붙어 있는 동안 히트플래시와 피격 쉐이크가 멈춘다.**
   `HitFlash` 는 `Unit.ClientDamaged`(실제 HP 델타) 를 구독하는데 하한 1 에서는 델타가 0 이다.
   데미지 숫자는 전용 경로라 계속 뜬다. 플래시까지 살리려면 `HitFlash` 를 건드려야 해서 범위 밖으로 둔다
2. **방어력 경감 공식을 `TrainingDummy` 가 자체 계산한다.** 명목 피해를 알아야 하는데
   `Unit` 이 경감 후 값을 돌려주는 API 가 없다. `Unit.ApplyMitigatedHealthDamage` 와 **중복**이므로
   코어 공식이 바뀌면 같이 고쳐야 한다. 코드에 주석으로 표시한다
3. **`LinearKnockback` 은 `EndKnockback` 에서 `NavMeshAgent` 가 있을 때만 `isKinematic` 을 되돌린다.**
   허수아비엔 에이전트가 없으므로 첫 넉백 이후 비-kinematic 으로 남는다.
   속도는 0 으로 정리되고 중력도 끄므로 제자리에 멈추지만, 자리 복귀 때 명시적으로 원복한다
4. 검증 씬은 사용자가 만든다 — **`FloatingDamageSpawner` 가 없으면 데미지 숫자가 안 뜬다**
   (현재 `4.MapScene` 에만 존재). `MainCamera.prefab` 이 없으면 쉐이크도 없다

## 남은 설계 부채

사용자가 선호한 안은 "`Unit` 도 안 쓰고 인터페이스만 활용"이었다. 그게 더 깔끔한 설계인 것은 맞지만,
`UI/Combat` · `Player/Skill/Targeting` 의 `Unit` 구체 타입 의존을 `IDamageTarget` 류로 걷어내는
**코어 리팩터링**이고 은희 담당 영역이다. 허수아비 브랜치에서 하지 않는다. 하려면 선행 별도 작업으로 뺀다.

## 완료 기준과 검증

- [ ] 컴파일 오류 0
- [ ] **호스트 단독 Play** — 때려서 데미지 숫자 · HP바(게이지+숫자) · 3초 후 회복 · 넉백 · 5m 이탈 시 복귀 확인
- [ ] **MPPM 2인** — 클라가 때렸을 때 HP · 데미지 숫자 · 회복이 호스트/클라 양쪽에서 일치
- [ ] 체력이 0 이 되지 않고 1 에서 멈추는지, 그 상태에서도 궁극기 조준이 되는지

⚠️ **Play 는 사용자가 직접 누른다.** MCP 로 Play 모드에 진입하면 MPPM 이 깨진다.
