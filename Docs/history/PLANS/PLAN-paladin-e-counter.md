# PLAN — 팔라딘 E 방어·자동 반격 (Re:C 변경안)

> 2026-10-16 · 브랜치 `feature/PaladinCounterE` · 상태: **승인됨**(2026-10-16 사용자 "추천대로 진행" — 결정 1~25 모두 권장안 확정)
> 기획 원본: `_lane-artifacts/common/Re_C_팔라딘_E_스킬_변경안.md`(레포 밖) · 조사 원본: `_lane-artifacts/common/PLAN-paladin-counter-e.md`(Worktree2 기준 줄 번호)
> 이 문서의 `파일:줄` 은 이 브랜치 실제 코드로 다시 확인한 값이다. 코드가 바뀌면 코드를 따른다.

## 1. 개요

새 팔라딘 E(수호자의 의지)는 **방어 대기 → 첫 방어 성공 → 자동 반격 1회 → 종료**로 바뀐다(기존 = 즉시 보호막 부여).
기존 클래스 `FirstMeleeSubSkill`(PlayerInstantSkill, Sub 슬롯)과 데이터 `FirstMeleeSubSkillData.asset` 을 그대로 두고
동작만 바꾸므로 `Player_Paladin.prefab` 배선은 건드리지 않는다.

### 1.1 기획 필수 규칙(§2~4) → 확정값

| 항목 | 확정 |
| --- | --- |
| 방어 방향 | Cast 시점 조준 방향으로 고정(`OnServerStart` direction), `snapRotationOnStart = 1` |
| 방어 각도 | 전방 180도(좌우 90도), 바닥 평면(y 제거), 경계 포함(`<=`) |
| 방어 대기 | 최대 1초(`guardDuration`) |
| 방어 성공 | `now < guardEnd` 에 피해가 들어오는 순간, **공격 주체**가 방어 각도 안 → 그 피해 무시 + 반격 단계 진입 |
| 반격 | 첫 성공 시 자동 1회. 고정 방어 방향 기준 전방 120도·3m, 대상당 1회, 공격력 250%(`attackDamageMultiplier 2.5`) |
| 반격 중 | `now < counterEndAt` 동안 방향 무관 피해 무시·모든 넉백 무시. 추가 성공이 와도 반격 재발동·연장 없음 |
| 실패 | 대기 시간 안에 성공 없으면 반격 없이 종료. 범위 밖·방어 불가 공격은 정상 적용(범위 밖 넉백이면 E 끊김) |
| 쿨타임 | 7초, E 승인 순간 시작, 환불 없음(이미 그렇게 동작 — `PlayerSkillController.cs:593-595`) |
| 행동 제한 | 대기·반격 중 이동·회전·평타·대시·다른 스킬 불가(`CanMoveWhileActive = false`), 종료 후 가능 |
| 입력 예약 | 없음 — E 종료 시 오너에서 `PlayerInputReader.ClearInputBuffer()` |

방어 성공과 반격 적중은 **별개 판정**이다. 멀리 있는 보스의 공격을 막아 반격이 나가도, 보스가 3m·120도 밖이면 반격 피해는 없다.

## 2. 설계 핵심

### 2.1 단계 정의와 시간 경계

| 단계 | 진입 | 피격 판정 | 넉백 | 종료 |
| --- | --- | --- | --- | --- |
| Idle | — | Pass | 통과 | — |
| Guarding | `Begin(now, …)` | `now < guardEnd` 이고 주체가 방어 각도 안 → **BlockAndCounter**, 그 밖 Pass | 통과(→ E 끊김) | `now >= guardEnd` → Ended(GuardExpired), 반격 없음 |
| Countering | 첫 BlockAndCounter. `counterHitAt = now + counterHitDelay`, `counterEndAt = now + counterDuration` | `now < counterEndAt` → **Ignore**(방향 무관). `now >= counterEndAt` → Pass(종료 처리 전이라도) | `now < counterEndAt` 동안 차단 | `now >= counterHitAt` → CounterHitDue(1회), `now >= counterEndAt` → Ended(CounterFinished) |
| Finished | Ended 처리 | Pass | 통과 | — |

- 시간 경계는 모두 **시각 비교**다. Countering 이 '종료 처리가 됐는가'에 기대지 않으므로 스크립트 실행 순서와 무관하다.
- 같은 틱에 CounterHitDue 와 Ended 가 함께 오면 **CounterHitDue 를 먼저** 처리한다(반격 → 종료 순서 고정).
- `counterHitDelay` 는 `[0, counterDuration]` 으로 Clamp(모델 Begin + 데이터 OnValidate 경고).
- 방어 불가(§2.3)는 단계와 무관하게 항상 Pass.

### 2.2 피격 가로채기 지점

- 공격 피해(AttackInfo 경로)는 서버 전용 `Player.ReceiveAttack`(`Player.cs:1701`)을 지난다. 훅은 그 맨 위,
  `BossRaidRules.CapHitDamage`(`:1709`)·`base.ReceiveAttack`(`:1711`) 앞에 둔다.
  플레이어 루트 `PlayerSkillController.ActiveSkill`(`PlayerSkillController.cs:79`)이 `IPlayerIncomingAttackGuard` 일 때만 동작한다.
- **무적 토큰은 쓰지 않는다**: `PlayerInvulnerability` 는 Hurtbox 를 꺼서 '맞는 순간'을 감지할 수 없다.
- **SuperArmor 도 쓰지 않는다**: `PlayerRestraintPolicy.IsBlockedBySuperArmor`(`PlayerStateController.cs:527-533`)가 Carry·Push 를 모두 거부해서 '잡기 방어 불가'와 충돌한다.
- 막은 피격은 `ReceiveAttack` 이 **true** 를 반환한다(투사체 소멸, SkyStrike 적중 등록). `ServerAttackReceived`(`Player.cs:1715`)를 내지 않으므로
  패시브 쿨 감소·쉴드 파문·피해 숫자·체력 재생 리셋이 없다.

### 2.3 방어 불가 규칙 (모두 Pass = 정상 적용, 방어 성공 트리거 안 함)

| 경우 | 근거 |
| --- | --- |
| 공격 주체 없음·파괴됨(Unity null)·사망(HP ≤ 0)·자기 자신 | §2.5 해석 실패. SteamVent, 사망한 사수의 SkyStrike, 미사용 AreaZone·BossBomb |
| 피해 0 이하 | 기획 '피해가 들어오는 순간'. 예: 23호 오라 `dmg = Max(0, …)`(`TwentyThreeBoss.cs:4984`) |
| 무적 중 피격 | 훅 조건에서 `invulnerability.IsServerInvulnerable` 이면 건너뛰고, 판정은 기존 `CanApplyHealthDamage`(`Player.cs:1727-1736`)가 한다. 전기 장판·드론은 `BossPatternTargets` 로 `Player.ReceiveAttack` 을 직접 불러 Hurtbox 가 꺼져도 들어오기 때문 |
| 레이드 자폭 즉사(`PlayerHitDamageCap.LethalOverride`) | forcedUnblockable |
| 잡기·구속 | 구조상 불가 — 구속 경로(`TryReceiveRestraint` `PlayerStateController.cs:251` → `ApplyRestrained` `:273`)로 들어와 Skill 상태가 빠지면 `HandleSkillStateExit`(`PlayerSkillController.cs:395-418`)가 서버에서도 `OnEnd(Cancelled)` 를 부른다. 잡기 끌어당김 자체는 피해가 없어 방어 성공을 만들지 않는다 |

### 2.4 ReceiveAttack 우회 피해 — 원래 방어 대상 아님

| 경로 | 위치 |
| --- | --- |
| 낙사 | `Player.ApplyFallDamage`(`Player.cs:1742`) → `ApplyDirectHealthDamage`(`:1751`) |
| Unit 직접·퍼센트 피해 | `Unit.ApplyDirectHealthDamage`·`ApplyMaxHealthPercentDamage`·`ApplyCurrentHealthPercentDamage`(`Unit.cs:285-306`) |
| 레거시 `GrabController.ApplyDamage` | 컴포넌트 비활성 |

이 경로는 훅을 거치지 않으므로 Play 체크는 회귀 확인용이다.

### 2.5 공격 주체 해석 규칙

```csharp
if (ctx.sourceUnit != null) return ctx.sourceUnit;
Transform t = ctx.sourceTransform;
Unit u = t != null ? t.GetComponentInParent<Unit>() : null;
return u != null ? u : null;
```

- **Unity null 명시 비교만** 쓴다. C# `?.`·`??` 는 파괴된 객체(가짜 null)를 거르지 못해 `MissingReferenceException` 을 던지고,
  훅이 `ReceiveAttack` 맨 위라 그 피격 전체(피해·사망 판정)가 사라진다. 선례: `MonsterSkyStrike.cs:83-84`, `Unit.ResolveAttackerClientId`(`Unit.cs:271`).
- **`sourcePosition` 금지** — 전기 장판은 발밑, 드론은 착탄점이 들어온다. 방향 기준은 해석된 주체의 위치다.
- 바닥 평면 겹침(XZ 거리 0.01m 미만)은 **정면 = 방어 성공**(`TwentyThreeBoss.IsCounterFromFront` `:5514` 선례).

### 2.6 공격별 귀속 현황

| 공격 | 귀속 | 방어 |
| --- | --- | --- |
| 근접(`MonsterMeleeAttack`) | sourceUnit 있음 | 가능 |
| 23호 직접 타격·오라·벽 충돌 | sourceTransform = 보스 | 가능 |
| 전기 장판·웰즈 드론 | sourceTransform = 보스(장판·착탄점 위치 아님) | 가능 |
| GauntletBot·WallBot | sourceTransform = 봇 | 가능 |
| SkyStrike | sourceUnit 있음(`MonsterSkyStrike.cs:84`). **MortarBot(일반 몹, rangedStyle 1)** 이 사용, 보스 미사용. 런타임 싱글턴(`Instance` getter `:22-32`, `MonsterBase.ServerBeginSkyStrike` `MonsterBase.cs:1336-1358`) | 가능(사수 생존 시) |
| `MonsterProjectile` | `_owner` 미전달(`MonsterProjectile.cs:96`, `:177`). PeekABot·TeslaBot 일반 몹 전용, 보스 미사용 | **불가 — 주체 없음**(C5 제외 확정) |
| AreaZone·BossBomb | 현재 스폰 안 됨 | — |

### 2.7 반격 보장 규칙

- **규칙**: 방어에 성공했는데 반격 판정 전에 E 가 **CasterDied 외**의 이유(구속·낙하·기타 Cancelled)로 끝나면,
  남은 반격 1회를 **다음 프레임에 서버에서** 실행한다. 원점 = 그때의 플레이어 위치, 방향 = 고정된 방어 방향.
- **필요한 이유 — 23호 돌진 스침 + 캐리**: `TickDash`(`TwentyThreeBoss.cs:4571-4576`)가 `meleeAttack?.Hit()`(스침, 방어 가능)
  바로 뒤 같은 호출 안에서 `TryCarryDashTarget()` → `BeginRestrainedByInstigator(Push)`(`:4602`)를 부른다.
  규칙이 없으면 '피해는 흡수됐는데 반격이 없는' 기획 위반이 매번 정해진 대로 생긴다.
  - 결과: 스침 피해 0·넉백 없음 → 캐리로 E 종료 → 다음 프레임 반격(보스가 3m·120도 안이면 피해) → 벽 충돌 피해·넉백·기절은 정상 적용.
- **다음 프레임으로 미루는 이유**: OnEnd 는 보스의 `TryCarryDashTarget` 루프 안에서 불린다. 거기서 보스 `ReceiveAttack` 을 다시 부르면(재진입)
  보스가 죽은 뒤에도 `_dashCarried`·`StartDashMove` 가 계속 실행될 수 있다.
- **기각한 대안**
  - (a) Countering 중 Push 구속 거부 — 잡기 끌어당김(`BeginGrabPull`)도 `RestraintMode.Push`(`TwentyThreeBoss.cs:2362`)라 모드만 보고 거부하면 잡기가 방어 가능해진다.
  - (c) 돌진 스침 방어 불가 — AttackInfo 플래그가 필요해 범위 밖.

### 2.8 넉백 게이트 — 단계 전체 적용

- 막은 근접 히트 직후 `MonsterMeleeAttack` 이 Knockback 을 부르고, 23호 오라(`TickChargeAura` `TwentyThreeBoss.cs:4976-5018`)도
  ReceiveAttack 뒤에 따로 Knockback(`:5015-5016`)을 부른다. 넉백은 스킬을 끊으므로 `Player.OnKnockback`(`Player.cs:484`) 맨 위에서 막는다.
- `Unit.Knockback`(`Unit.cs:774`)에는 공격자 정보가 없어 '이 넉백이 Pass 된 히트의 것인지' 구분할 수 없다.
  그래서 게이트는 **반격 단계(`now < counterEndAt`) 전체에 적용**된다 — 방어 불가 히트의 넉백도 무시된다(현재 반격 중 넉백을 동반하는 방어 불가 공격은 없다).
- `counterEndAt` 이후에는 실행 순서와 무관하게 통과. 방어 대기(Guarding) 중 범위 밖 공격의 넉백은 정상 적용되고 E 가 끊긴다.

### 2.9 반격 범위 — '3D 구 → 평면 각도'

- 브로드페이즈 = `Physics.OverlapSphereNonAlloc(origin, counterRange, …, hittableLayers, Collide)`(3D 구, 반경 3m).
- 거리는 대상 콜라이더 표면(`ClosestPoint`) 기준, 각도는 바닥 평면 기준(ClosestPoint 가 원점이면 bounds 중심 방향). Q·어쌔신 콘(`AssassinConeAttack.IsInsideCone` `:118`)과 같은 식.
- 따라서 '높이 무관'은 **각도만**이다. 아주 높이 있는 콜라이더는 구 밖이라 빠질 수 있다(캡슐 브로드페이즈는 범위 밖).
- 순회 형태는 `FirstMeleeMainSkill.OnHoldTick`(`FirstMeleeMainSkill.cs:119-180`)을 따른다 — `ResolveHitUnit` → `Object target = unit != null ? unit : hurtbox` 중복 방지, 살아 있는 첫 Unit 에 `ServerTakeOnHitBonus`.

### 2.10 선입력 방향 편차 (알려진 편차)

- 방향은 Cast 시점 `GetCurrentAimDirection()`(`PlayerSkillController.cs:355-357`, 본체 `:995-1001`)이다.
- 다른 행동 중에 누른 E 는 선입력 버퍼(`PlayerInputReader.inputBufferSeconds = 0.2` `PlayerInputReader.cs:14`)로 늦게 나가고, **재생 시점의 조준**을 쓴다.
  기획 §2 '입력 순간 고정'과 다르다 — 유지(스킬별 opt-out 은 범위 밖).
- 입력 예약 없음: E 종료 시 오너에서 `ClearInputBuffer()`(`PlayerInputReader.cs:162-170`) — 선입력 버퍼와 함께 스킬 예약(`skillReservation`, 할 일 44)도 비운다.
  다른 행동 중 누른 E 자체의 0.2초 선입력은 유지한다.

### 2.11 이미 맞는 것

- 쿨타임 승인 즉시 시작·환불 없음(`PlayerSkillController.cs:593-595`).
- 피해 스냅샷 = `FinalAttackDamage × AttackDamageMultiplier + FlatDamageBonus`(`:599-601`) → 배율 2.5 = 공격력 250%. 시점은 E 승인 시점(다른 스킬과 동일).
- 평타·다른 스킬·간파는 Skill 상태에서 이미 막히고, 대시는 `CanBeCanceledByDash` 기본 false 로 막힌다.

## 3. 결정 (확정 2026-10-16 사용자: 권장안)

| # | 항목 | 확정 |
| --- | --- | --- |
| 1 | PLAN 승인 | 승인. C1 승인 대기 없이 C2 부터 진행 |
| 2 | 방어 성공 후 반격 보장 | 채택 — §2.7. '캐리로 끊기면 반격 없음' 대안은 기각 |
| 3 | 행동 제한 | 채택 — `CanMoveWhileActive = false`(현재 true, `FirstMeleeSubSkill.cs:21`). 평타·스킬·대시는 기존 Skill 상태 규칙 |
| 4 | 방어 방향 고정 | Cast 시점 조준 + `snapRotationOnStart = 1`. 선입력 편차 유지(§2.10) |
| 5 | 반격 방향 | 고정된 방어 방향 기준 전방 120도·3m(공격자 쪽으로 돌지 않음) |
| 6 | 반격 타이밍 자리값 | `counterHitDelay 0.1`·`counterDuration 0.4`, `[DataTableIgnore]`. Clamp + OnValidate 경고. 클립이 생기면 Hit/End 이벤트로 교체 |
| 7 | 피해 스냅샷 시점 | E 승인 시점 |
| 8 | 반격 중 피해 무시 방향 | 방향 무관(방어 불가 제외), `counterEndAt` 까지만 |
| 9 | 반격 중 넉백 | 모든 넉백 무시, 단계 전체 적용(§2.8) |
| 10 | 반격 중 기절·에어본·경직 무시(C10) | **보류 확정** — 현재 막을 대상 없음(아래 C10) |
| 11 | SuperArmor | 미사용 |
| 12 | 잡기 시 반격 중단 | 기존 구속 → `HandleSkillStateExit(Cancelled)`. 이미 성공 상태면 반격 보장으로 1회 |
| 13 | 주체 없음·파괴·사망 | 방어 불가(sourcePosition 폴백 없음) |
| 14 | 피해 0 이하 | Pass |
| 15 | 무적 중 피격 | Pass |
| 16 | 바닥 평면 겹침(XZ < 0.01m) | 정면 = 방어 성공 |
| 17 | LethalOverride | 방어 불가 |
| 18 | 막은 피격 처리 | `ReceiveAttack` true 반환, `ServerAttackReceived` 없음 |
| 19 | 반격 `isInterruptAttack` | false(보스 카운터 창과 섞지 않음) |
| 20 | MonsterProjectile 소유자 전달(C5) | **제외 확정** — 일반 몹 투사체는 '주체 없음 → 방어 불가' |
| 21 | 반격 범위 높이 | 3D 구 → 평면 각도(§2.9) |
| 22 | 입력 예약 없음 범위 | E 종료 시 오너 `ClearInputBuffer()`, E 자체 선입력은 유지 |
| 23 | 구 `shieldAmount`·`shieldDuration` | SO 에 미사용으로 남김(지우면 xlsx 열 때문에 테이블 Play·빌드 오류). 병합 Export 때 제거 |
| 24 | xlsx 갱신 | 사람이 SVN 병합 Export — 아래 §5 |
| 25 | 연출 자리값 | 반격 순간 `PlayerShieldVfx.ServerInterruptWave()` 재사용. 정식 VFX·SFX·애니는 민경 추후 |

## 4. 커밋 단위

| 커밋 | 내용 | 파일 |
| --- | --- | --- |
| C1 | docs — 이 문서 + `character_garen.md` subSkill 칸 | 문서만 |
| C2 | 순수 규칙 `FirstMeleeGuardRules`(`IsInsideGuardArc`·`IsInsideCounterSector`) + EditMode | `Player/Skill/FirstMeleeGuardRules.cs`, `Player/Editor/FirstMeleeGuardRulesTests.cs`, `PlayerEditModeTestRunner.cs` |
| C3 | 단계 상태기계 `FirstMeleeGuardCounterModel` + EditMode | `Player/Skill/FirstMeleeGuardCounterModel.cs`, `Player/Editor/FirstMeleeGuardCounterModelTests.cs`, 러너 |
| C4 | 훅 `IPlayerIncomingAttackGuard` + `AttackSourceResolver`, `Player.ReceiveAttack`(`:1701`)·`OnKnockback`(`:484`) 맨 위 | `Player/IPlayerIncomingAttackGuard.cs`, `Unit/Weapon/AttackSourceResolver.cs`, `Player/Player.cs` |
| ~~C5~~ | **제외 확정** — MonsterProjectile `_owner` 전달(경석 영역) | — |
| C6 | 데이터 필드·SO 값 | `FirstMeleeSubSkillData.cs`, `FirstMeleeSubSkillData.asset` |
| C7 | E 를 방어 대기 → 흡수·넉백 무시 → 종료로 교체(반격 피해 제외) | `FirstMeleeSubSkill.cs` |
| C8 | 반격 판정 + 반격 보장 + 자리값 연출 | `FirstMeleeSubSkill.cs` |
| C9 | 툴팁 저작 문구·문서 정리 | `SkillTooltipAuthoring.cs`, `localization-en-draft.md`, `player-prefabs.md` |
| ~~C10~~ | **보류 확정** — 반격 중 CC 무시. 플레이어 대상 Stunned 는 23호 돌진 벽 충돌(`TwentyThreeBoss.cs:4668`)뿐이고 그 대상은 구속 순간 E 가 이미 끝나 있다. AreaZone 상태이상은 스폰 안 됨 | — |

C2~C9 의 세부(테스트 이름·의사코드)는 조사 원본 PLAN 의 같은 커밋 항목을 따르되, 줄 번호는 이 문서 값을 쓴다.

## 5. 데이터 열

| 필드 | 형 / 기본값 | 비고 |
| --- | --- | --- |
| `guardDuration` | float 1.0, Min 0.05 | 방어 대기 최대(초). Paladin 시트 새 열 |
| `guardAngle` | float 180, Range 0-360 | 전방 방어 전체 각도(바닥 평면). 새 열 |
| `counterAngle` | float 120, Range 0-360 | 반격 부채꼴 전체 각도(바닥 평면). 새 열 |
| `counterRange` | float 3, Min 0 | 3D 구 반경, 대상 표면(ClosestPoint) 기준. 새 열 |
| `counterHitDelay` | float 0.1, `[DataTableIgnore]` | ≤ counterDuration Clamp. 애니 Hit 이벤트로 대체 예정 |
| `counterDuration` | float 0.4, `[DataTableIgnore]` | 피해·넉백 무시 종료. 애니 End 이벤트로 대체 예정 |
| 기존 값 변경 | cooldownTime 14→7, attackDamageMultiplier 0→2.5, maxActiveDuration 0.5→1.5, snapRotationOnStart 0→1, hittableLayers 0→17664(Enemy·Projectile·EnemyHurtBox, Q 와 동일) | 현재 에셋 값 확인함(14 / 0 / 0.5 / 0) |
| `shieldAmount`·`shieldDuration` | 유지(미사용) | 병합 Export 때 제거 |

> ⚠️ **xlsx 갱신 전에는 툴바 '데이터: 인스펙터' 로만 검증한다.** xlsx Paladin 시트에는 아직 maxActiveDuration 0.5,
> attackDamageMultiplier 0, cooldownTime 14 가 남아 있어 '데이터: 테이블' 모드와 **빌드**에서는 E 가 깨진다(maxActiveDuration 0.5 + 여유로 강제 종료 폴백, 반격 피해 0).
> xlsx 반영(값 3개 + 새 열 4개)은 병합 후 SVN 병합 Export 로 하며 **빌드 전 필수**다.

## 6. Play 확인 (사람이 '데이터: 인스펙터' 모드로)

- 사전: 콘솔 `[Skill] Sub 시작 — 피해 스냅샷 N, 쿨타임 7s`, MaxActiveDuration 경고 없음.
- 솔로 23호 근접 정면: 피해 숫자·HP 감소·넉백 없음 → 0.1초 뒤 반격 충격파 + 보스 피해 = 공격력×2.5(툴팁 {dmg} 와 같음).
- **23호 돌진 정면**: 스침 피해 0·넉백 없음 → 캐리로 E 종료 → 다음 프레임 반격 1회(로그 'deferred counter') → 벽 충돌 피해·넉백·기절 정상.
- 등진 채 피격: 피해·넉백 정상, E 끊김, 반격 없음. / 1초 무피격: 반격 없이 종료, 쿨 7초 누른 순간부터, 환불 없음.
- 경계: 정확히 옆(90도 근처) 성공, 약간 뒤 실패.
- 전기 장판·웰즈 드론: 발밑·착탄점이 아니라 보스 방향 기준. 3m 밖 보스는 충격파만 나오고 피해 없음.
- 반격 중 연속 피격(근접 연타·뒤쪽·오라 틱) 모두 무시, 반격 1회, 0.4초 뒤 피해·넉백 정상. 범위 안 다수 몬스터 각자 1회.
- 잡기: 대기 중 끌어당김 → E 즉시 끊김, 반격 없음, 잡기·틱·슬램 피해 정상.
- 대기·반격 중 WASD·회전·좌클릭·대시·Q·R·우클릭 불가, 그 사이 입력이 종료 뒤 재생되지 않음(진단 로그 dropped).
- 평타 중 누른 E 는 0.2초 선입력대로 나가고 방향은 재생 시점 조준(알려진 편차).
- 회귀(훅 무관 경로): 낙사, SteamVent(주체 없음), 레이드 자폭(LethalOverride) 정상.
- MortarBot SkyStrike: 정면 방어 → 반격, 측후방 피해. PeekABot·TeslaBot 투사체: 정면이라도 피해(방어 불가).
- 무적 중 피격은 방어 성공 아님. 막은 피격으로 팔라딘 패시브 쿨 감소 없음.
- MPPM 2인: 클라 팔라딘으로 성공·실패·잡기·돌진 정면 — 판정 서버, 양쪽 화면 충격파.
- 튜토리얼 Zone1 SkillE 체크 통과(ServerSkillUsed). Q·R·우클릭 간파·거너·어쌔신 피격·넉백 회귀.

## 7. 범위 밖

- E 방어·반격 애니메이션 클립과 Hit/End 이벤트 · 정식 VFX·SFX(민경) · GameData.xlsx 직접 수정
- HolyShield 정리(ShieldType.HolyShield, HolyShieldEffect·System, PlayerShieldVfx 보호막 색, HealthShieldTests, SO 구 필드)
- AreaZone·BossBomb instigator · AttackInfo unblockable/grab/dash 플래그 · 반격 중 Push 구속 거부
- Unit.Knockback 공격자 정보로 히트별 넉백 게이트 세분화 · 보스 코드(TwentyThreeBoss 돌진·캐리 순서) 변경
- PlayerRestraintPolicy SuperArmor·Carry 충돌(경석 확인 대기 별도 이슈) · 반격과 보스 카운터 창·간파 연동
- 스킬별 선입력 opt-out · 캡슐 브로드페이즈 · 밸런스 수치 확정(기획 §4 는 테스트용)

## 8. 비판 17건 요약

| 지적 | 처리 |
| --- | --- |
| [must-fix ×2] 23호 돌진 스침 흡수 후 같은 틱 캐리로 E 종료 → 반격 없음 | **반영** — 반격 보장 규칙(§2.7) |
| Countering 시간 상한 없음(실행 순서 의존) | 반영 — `now < counterEndAt` 시간 경계 |
| CounterHitDue·Ended 같은 틱 순서 미정 | 반영 — 반격 먼저, Clamp |
| sourceTransform `?.` 가짜 null 예외 | 반영 — Unity null 명시 비교(§2.5) |
| 넉백 게이트가 방어 불가 히트 넉백도 무시 | 수용·문서화 — 공격자 정보 없음, 단계 전체 적용(§2.8) |
| 무적 중 피격이 방어 성공 | 반영 — 무적이면 Pass |
| 반격 범위가 완전한 평면이 아님 | 반영 — '3D 구 → 평면 각도'로 문서·테스트 이름 정정 |
| 선입력으로 '입력 순간' 방향이 아님 | 수용·알려진 편차(§2.10) |
| §5 제안(행동 제한·입력 예약 없음) 결정 항목 없음 | 반영 — 결정 3·22 로 분리 후 확정 |
| 주체 사망·피해 0 처리 누락 | 반영 — 방어 불가(§2.3) |
| C8 의사코드 컴파일 불가·Unity null 누락 | 반영 — C8 에서 `OnHoldTick` 형태 그대로 |
| C5 가 경석 영역 수정 | 반영 — C5 제외 확정 |
| C10 검증 불가 | 반영 — 보류 확정 |
| 'SkyStrike 는 스폰 안 됨' | **기각** — `MonsterBase.ServerBeginSkyStrike` 가 `MonsterSkyStrike.Instance` 싱글턴 사용, MortarBot rangedStyle 1 |
| '모든 피해가 ReceiveAttack 경유' 서술 오류 | 반영 — 우회 경로 명시(§2.4) |
| 줄 번호 1 차이 | 반영 — 이 브랜치 기준 재확인. '피해 스냅샷 598-600' 지적은 기각(599-601 맞음, 598 은 주석) |

## 9. 조사 원본 대비 줄 번호 변경 (Worktree2 → 이 브랜치)

| 참조 | 원본 | 이 브랜치 |
| --- | --- | --- |
| `Player.ReceiveAttack` / CapHitDamage / base 호출 | :1683 / :1691 / :1693 | :1701 / :1709 / :1711 |
| `Player.CanApplyHealthDamage` | :1709-1717 | :1727-1736 |
| `Player.ApplyFallDamage` → `ApplyDirectHealthDamage` | :1724 → :1733 | :1742 → :1751 |
| `Player.OnKnockback` | :469 | :484 |
| `Unit` 직접·퍼센트 피해 | :285-305 | :285-306 |
| `TwentyThreeBoss.TickChargeAura` / dmg | :4977-5016 / :4983 | :4976-5018 / :4984 |
| `MonsterBase.ServerBeginSkyStrike` | :1333-1352 | :1336-1358 |
| `MonsterSkyStrike.Instance` | :22-31 | :22-32 |
| `FirstMeleeMainSkill.OnHoldTick` 순회 | :140-170 | :119-180 |
| `PlayerInputReader.ClearInputBuffer` | — | :162-170(스킬 예약도 비움) |

그 밖(`PlayerSkillController` :79·:355-357·:395-418·:593-601·:995, `PlayerInputReader` :14, `FirstMeleeSubSkill` L18·L21·L27-47,
`PlayerStateController` :251·:273·:527-533, `Unit` :774, `TwentyThreeBoss` :2362·:4571-4576·:4602·:4668, `AssassinConeAttack` :118,
`MonsterProjectile` :96·:177, `StatusEffectController.Apply` :143·:152)은 원본과 같다.
