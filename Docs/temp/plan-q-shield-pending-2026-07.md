# PLAN — Q 진격의 방패 (FirstMeleeMainSkill)

> 브랜치: feature/PlayerSkill (b2e9a5a = feature/CombatUI 최신에서 FF — MaxShield 제거 반영됨)
> 상태: 그릴 완료(2026-07-20) — **승인 대기**
> 이전 플랜: CombatUI 1차 → Docs/temp/plan-combatui-2026-07.md (완료), 스킬 전체 설계 → Docs/temp/plan-playerskill-2026-07.md

## 1. 목표

Q 홀드 스킬: 방패를 들고 **자동 전진 + 마우스 조향**, 경로상 적에게 **틱당 데미지 + 진행방향 넉백**(견인), 홀드 동안 **시전자 슈퍼아머**.

## 2. 그릴 확정 사항 (2026-07-20)

| 항목 | 결정 |
|---|---|
| 홀드 중 이동 | 자동 전진. 조향은 마우스 에임과 현재 방향을 비교해 좌/우 판별 → 틱당 고정 각도(기본 5°, 변수) 회전 |
| 견인 방식 | `Unit.Knockback()`에 플레이어 **진행 방향**을 넘겨 전 대상 동일 방향 넉백 — 다음 틱에도 범위에 들어오도록 |
| 틱 효과 | 데미지 + 넉백 동시 |
| 등급 분기 | 없음 — 모든 Enemy 동일 처리. 보스는 v1에서 신경 안 씀 (SuperArmor 보유 예정 → Unit 공통 검사가 자연 거부) |
| SuperArmor | **Unit으로 이전** — Unit이 StatusEffectController를 조회, Knockback 공통 진입점에서 거부 |
| 시전자 CC | Q 홀드 동안 시전자에게 SuperArmor 상태이상 부여 (기존 시스템 Apply) |
| 수치 | 전부 임시값 (SO에서 조절) |

## 3. 비범위

- 보스(Wells/23) SuperArmor 부여·프리팹 부착 — 보스 작업 쪽 몫
- **실전 몹 프리팹 LinearKnockback 부착** — 몬스터 작업은 `feature/map-player-merge`에서 별도 작업자 진행 중(2026-07-20 확인, ModularRobots_R1은 미사용). 요건만 인수인계: 몹 프리팹 최상단(Rigidbody·NavMeshAgent와 같은 레벨)에 `LinearKnockback` 부착 — 없으면 Q 견인이 데미지만 적용됨
- 애니메이션 상태 추가·VFX (기존 잔여 수동 작업과 동일하게 사용자 몫, SO에 이름만 기입)
- 단죄의 방패(우클릭)·R — 다음 단계

## 4. 구조

### 4.1 SuperArmor Unit 이전 (선행 리팩터링)

- `Unit`: StatusEffectController 캐시 + `HasSuperArmor` 게터 추가. `Knockback()` 공통 진입점에서 슈퍼아머 시 거부
- `PlayerStateController.BeginKnockback`의 중복 슈퍼아머 검사 제거 (사망 등 나머지 거부 사유는 유지)
- 기존 넉백 회귀 없음 확인 (보스 넉백 어택 → 플레이어)

### 4.2 새 파일 (Assets/1.Scripts/Player/Skill/)

| 파일 | 내용 |
|---|---|
| `FirstMeleeMainSkillData.cs` | SO: advanceSpeed, steerAnglePerTick(기본 5°), tickInterval, damageCoefficient, knockbackStrength (+베이스: 쿨타임, maxDuration 등) |
| `FirstMeleeMainSkill.cs` | PlayerHoldSkill 파생 — 아래 4.3~4.5 |

### 4.3 이동/조향 — 오너 권위 (networking.md 준수)

- 전진·회전은 **오너 로컬 시뮬레이션** (NetworkTransform 복제). 스킬 활성 동안 기본 이동 입력 차단(PlayerSkillState), 스킬이 오너 Update에서 전진+조향 수행
- **UpdateSkillAimRpc 사용 안 함** — 서버는 복제된 `transform.forward`를 그대로 사용 (에임 이중 전송 불필요)

### 4.4 서버 틱 판정

- 서버 코루틴, tickInterval마다: MainSkill 앵커(Armature 하위) 콜라이더 범위 내 Enemy(Unit) 수집 → 데미지 스냅샷(`SO 계수 × FinalAttackDamage`) + `Unit.Knockback(시전자 forward, strength)`
- IKnockbackable 없는 Unit은 데미지만 적용 (현 LogError는 스팸 방지 위해 완화 검토)

### 4.5 시전자 슈퍼아머

- 서버 시작 시 `StatusEffectController.Apply(SuperArmor, duration=maxDuration)`, 종료/취소(Cancel) 시 Remove — R 슈퍼아머 선례와 동일 접근

### 4.6 프리팹/에셋

- `Player.prefab`: MainSkill 앵커 판정 콜라이더 확인/추가 + 슬롯 매핑(Main→FirstMeleeMainSkill) 연결
- FirstMeleeMainSkillData SO 에셋 생성 (임시값)
- InputActions의 SkillMain(Q) — 추상화 때 추가분 재사용, 없으면 추가

## 5. 완료 조건

1. 컴파일 통과 + 전 파일 UTF-8(BOM) + 기존 동작 무변화 (E/기본공격/기존 넉백)
2. MPPM 2인: ① 각자 Q 홀드 → 자동 전진+조향이 상대 화면에 복제 ② 대상이 틱마다 데미지 받고 진행방향으로 밀림 ③ 홀드 중 보스 넉백 피격에도 미중단(슈퍼아머) ④ 릴리즈/최대시간 종료 + 쿨타임 HUD 필 동작
   - ②의 견인 검증은 실전 몹 부재로 **검증용 대상에 LinearKnockback 임시 부착**으로 확인 (대상·커밋 여부는 검증 시점에 결정)
3. 오프라인(!IsNetworkActive) 로컬 즉시 실행 경로 동작

## 6. 구현 순서

1. SuperArmor Unit 이전 + 회귀 확인
2. SO + FirstMeleeMainSkill 골격 (시작/종료/취소·쿨타임)
3. 오너 전진/조향
4. 서버 틱 (데미지+넉백)
5. 시전자 슈퍼아머
6. Player.prefab 배선 + SO 에셋 → MPPM 검증 (견인은 임시 대상으로) + 몬스터 작업자에게 LinearKnockback 요건 인수인계
