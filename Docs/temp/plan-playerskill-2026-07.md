# PLAN — 플레이어 스킬 추상화 레이어

> 기획서: 근거리 캐릭터 스킬 기획서 v1.2 (VeyTrace)
> 브랜치: feature/PlayerSkill
> 상태: 그릴 완료(전 항목 합의) — **구현은 사용자 요청 시 시작**

## 1. 목표

Q/E/우클릭/R 4개 액티브 스킬 + 향후 캐릭터 추가(스킬 수십 개)를 수용하는 공통 스킬 기반.

## 2. 비범위

- 패시브 '불굴의 의지' (온힛 버프 구조로 추후 별도 설계 — 버프 부여/공격 트리거 분리 원칙만 합의)
- 콤보/그로기 카운트 등 공용 전투 시스템
- DefaultAttackController 수정 (검증 완료 코드 유지, 기본 공격은 스킬로 흡수하지 않음)
- 쿨타임 UI, R의 타겟팅 UI/선택 방식
- PlayerData 클래스 (스킬은 당장 `player.AttackDamage`를 읽음, 이중 장부 정리는 스킬 구현 후 보강 — 합의됨)

## 3. 구조 (그릴 확정)

### 3.1 클래스 계층 — 3단 (확장 대비, 합의됨)

```
PlayerSkillBase (abstract MonoBehaviour, Player 루트 부착)
 ├─ PlayerInstantSkill      // 프레스 즉발 — E, 우클릭
 ├─ PlayerHoldSkill         // 홀드 지속, 릴리즈/최대시간 종료 — Q
 └─ PlayerChannelingSkill   // 고정 캐스트, 틱 지원 — R
      └─ (구체 스킬은 타입 클래스 상속)
```

- 스킬 내부 상태: `enum SkillState { Ready, Charging, Channeling, Active, Cooldown }`
- 단일 진입점: 입력 측은 스킬 타입을 몰라도 됨 — `OnPress / OnRelease`만 호출, 분기는 타입 클래스 내부
- 강제 중단: `Cancel(reason)` 단일 경로 (CC/사망/상태 전환 시 FSM이 1회 호출)
- 레퍼런스 코드의 즉시 실행(`Execute`)은 서버 권위로 분리: `OnServerStart`(판정) + `OnClientPlay`(연출)

### 3.2 파일 (Assets/1.Scripts/Player/Skill/)

| 파일 | 내용 |
|---|---|
| `PlayerSkillSlot.cs` | `enum { Main, Sub, Interrupt, Ultimate }` |
| `PlayerSkillData.cs` | SO 베이스: 쿨타임, 피해 계수, 입력 방식(Press/Hold), 최대 지속시간, 틱 주기, usableWhileDead(기본 false), 애니 상태/트리거 이름. 스킬별 파생 SO |
| `PlayerSkillBase.cs` | 추상 베이스 + `SkillState` enum |
| `PlayerInstantSkill.cs` / `PlayerHoldSkill.cs` / `PlayerChannelingSkill.cs` | 타입 클래스 |
| `PlayerSkillController.cs` | 단일 NetworkBehaviour — 입력 라우팅, 서버 승인, 쿨타임 장부, RPC 전부 (스킬별 RPC 금지) |
| `PlayerSkillState.cs` | FSM 상태 (아래 3.4) |

### 3.3 슬롯 매핑 (합의됨)

| 계층 노드 (Armature 하위 앵커) | 슬롯 | 키 | 스킬 | 타입 |
|---|---|---|---|---|
| MainSkill | Main | Q | 진격의 방패 | Hold |
| SubSkill | Sub | E | 수호자의 의지 | Instant |
| InterruptAttack | Interrupt | 우클릭 | 단죄의 방패 | Instant |
| UltimateSkill | Ultimate | R | 최후의 심판 | Channeling |

- **앵커/로직 분리**: 판정 앵커(ColliderInfo+Collider)는 Armature 하위 노드(회전 상속 필수 — PlayerMovement가 armature만 회전), 로직 컴포넌트는 Player 루트. armature 교체 시 앵커 참조 재연결만으로 복구
- 컨트롤러는 슬롯→스킬 컴포넌트 SerializeField 매핑

### 3.4 FSM — 단일 Skill 상태 (합의됨, 스킬별 상태에서 변경)

- `PlayerActionState.Skill` 하나만 추가. 이동 가능/취소 가능 여부는 실행 중인 스킬 인스턴스에 위임
- 스킬 시작은 **Idle/Move에서만** (기존 Interrupt와 동일). 공격 중 캔슬 허용은 추후 CanEnter 한 곳만 열면 되는 구조로
- 기존 Interrupt 상태/입력은 단죄의 방패 구현 시 흡수·제거 (그때 enum 감소)
- 진입은 `BeginKnockback` 선례처럼 컨트롤러 주도 `SetState(new PlayerSkillState(context, skill))` 경로

### 3.5 네트워크 RPC 표면 (v1에 시그니처 확정, 합의됨)

```
[오너→서버] RequestUseSkillRpc(slot, direction, targetRef)   // targetRef: R용 NetworkObjectReference, 없으면 default
[오너→서버] UpdateSkillAimRpc(direction)                     // 홀드 조향(Q), 전송 주기 제한(~10Hz), 서버는 최신값만 유지
[오너→서버] NotifySkillReleasedRpc()                         // 홀드 해제
[서버→클라] PlaySkillClientRpc(slot, direction) / EndSkillClientRpc(slot) / RejectSkillClientRpc(owner)
```

- 서버 검증: FSM CanEnter + 쿨타임 + `skill.CanUse()` (사거리/대상 생존 등)
- 오프라인(!IsNetworkActive)은 로컬 즉시 실행 (DefaultAttackController 선례)
- 서버 안전망: 최대 지속시간 초과 시 강제 종료 (`attackEndFallbackTime` 선례)

### 3.6 쿨타임 (합의됨)

- **서버 승인 즉시 시작, 전 스킬 통일. 환불 없음**
- 사망 시: 실행 중 스킬은 `Cancel(CasterDied)`로 취소하되 **쿨타임은 계속 카운팅**. 사망 상태에서는 `usableWhileDead` 스킬 외 시전 차단

### 3.7 애니메이션 이벤트 (합의됨)

- `enum SkillAnimationEventType { Hit, End, Custom0, Custom1 }` — Custom은 스킬 고유 타이밍(R 면역 구간, Q 견인 시작 등)
- PlayerAnimationEventRelay에 `HandleSkillEvent(int)` 1개 추가 → Player → 컨트롤러 → 활성 스킬 `OnAnimationEvent(e)`. 판정은 서버만
- R 슈퍼아머는 추상화 무관 — 기존 StatusEffectController를 스킬이 Custom 이벤트 구간에서 사용

### 3.8 데이터 흐름

- SO(정적 설계값, 기획서 TBD 수치) 주입 → 서버에서 스킬 시작 시 `SO 계수 × player.AttackDamage` 스냅샷 (`CalculateDamageSnapshot` 선례)

### 3.9 입력

- InputActions에 `SkillMain(Q)`, `SkillSub(E)`, `SkillUltimate(R)` 추가, 우클릭은 기존 `Interrupt` 액션 재사용
- PlayerInputReader에 슬롯별 pressed/held 프로퍼티 추가

## 4. 완료 조건 (1차 커밋)

1. 3.2 파일 전부 + PlayerInputReader/Relay 확장 컴파일 통과
2. 기존 동작(이동/기본 공격/인터럽트) 무변화 — 새 코드는 미연결
3. 전 파일 UTF-8(BOM)
4. 구체 스킬 1개 추가 시 수정 지점 = 파생 클래스 + 파생 SO + 슬롯 매핑 + (필요시 앵커 참조) 로 한정되는지 점검

## 5. 구현 순서 (합의됨 — 착수는 요청 시)

1. ~~**추상화 레이어**~~ ✅ f4bfbcf
2. ~~**E 수호자의 의지**~~ ✅ ca19367, 네이밍 통일 4544122 (FirstMeleeSubSkill) — MPPM 검증 대기
3. **상태이상 시스템 보강** (아래 6절 — 그릴 완료)
4. **우클릭 단죄의 방패** — 기존 Interrupt 대체 (FirstMeleeInterruptSkill)
5. **Q 진격의 방패** — 홀드+조향+견인 (FirstMeleeMainSkill)
6. **R 최후의 심판** — 대상 지정 (FirstMeleeUltimateSkill)
7. 패시브 불굴의 의지 (별도 설계)

## 6. 상태이상(버프/디버프) 시스템 — 그릴 확정 (2026-07-15)

> 배경: 기존 StatusEffectController는 플래그 보관+질의만 있는 골격. SetEffects/Unit.ChangeStatusEffectType은
> 호출처 0곳(미사용)이라 마이그레이션 부담 없음. R(슈퍼아머)·Q(무력화) 전에 선행 구축.

### 6.1 모델 — 인스턴스 기반

- `StatusEffectInstance` (struct, INetworkSerializable): `{ type, magnitude(배율), duration(0=수동해제), appliedServerTime, sourceId }`
- `StatusEffectType` enum은 **식별자로 유지** (중첩 키·직렬화·해제/면역·UI 조회용). 차단류 기존 7종 + 스탯 modifier 6종 추가 (MoveSpeed/AttackDamage/AttackSpeed/Defense/MaxHp/MaxShield)
- 차단 매핑(BlocksMovement 등)은 하드코딩 → 타입→차단셋 테이블로 이전

### 6.2 스탯 — modifier 집계 (전 스탯)

- base 스탯 불변, 최종값 = base × 활성 배율의 **곱** (스택 정책 = 곱연산, 합의됨)
- Unit에 `Final*` 게터 6종. 소비처 배선은 확실한 곳만: 데미지 스냅샷 2곳(PlayerSkillController·DefaultAttackController) → FinalAttackDamage, PlayerMovement → MoveSpeed 배율 곱. 나머지는 게터만 제공
- 재적용 키 = (type, sourceId) → 시간/수치 갱신. 다른 출처는 병존, 각자 만료

### 6.3 데이터 — SO 없음, 파라미터 방식

- `Apply(type, magnitude, duration, source)` — 수치는 거는 쪽(스킬 SO 등)이 보유. 상태이상 전용 SO 안 만듦

### 6.4 네트워크 — 인스턴스 리스트 전체 동기화

- `NetworkList<StatusEffectInstance>` — 서버만 쓰기(Apply/Remove/만료 스윕), 전 피어가 리스트에서 로컬 집계
- 시간 기준은 ServerTime (피어 간 시계 차이 대응). 추후 버프 타이머 UI 공짜

### 6.5 배치/마이그레이션

- 기존 StatusEffectController를 개조(NetworkBehaviour 승격) + `Assets/1.Scripts/Unit/`으로 이동 (meta guid 보존 → Player.prefab 참조 유지)
- `Blocks*`/`HasSuperArmor` 시그니처 유지 → PlayerStateController 무수정
- Unit의 `_statusEffectType`/`ChangeStatusEffectTypeRpc` 삭제 (미사용 확인)
- 몬스터 프리팹 부착은 이번에 안 함 — Q 무력화 구현 때

### 6.6 완료 조건

1. 컴파일 통과 + 기존 동작 무변화 (거는 코드가 아직 없으므로 집계 결과는 현행과 동일)
2. Apply/Remove/만료/곱연산 집계가 서버 권위로 동작 (검증은 E 스킬에 임시 이속 버프 붙여 MPPM 확인 가능)
3. UTF-8(BOM)
