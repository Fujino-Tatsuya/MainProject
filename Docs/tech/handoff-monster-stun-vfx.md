# handoff — 몬스터 Q 경직 표현 (자세 고정 + 머리 위 기절 이펙트)

> 2026-10-26 · 경석(Claude) · 브랜치 `feature/SettingsDdol` · 규칙 정본 [monster-aggro-hitreact.md](monster-aggro-hitreact.md) §2
> **민경 님 영역(상태이상 VFX)에 추가만 한 변경이 있다** — 플레이어 쪽 SO·Presenter·에셋은 손대지 않았다. §2 만 보면 된다.

## 0. 왜

팔라딘 Q 를 맞은 일반 몹이 "밀리기만 하고 경직이 없다 · 공격이 안 끊긴다"로 보였다(경석 10-26, HumanoidBot·ChompBot).
**판정은 이미 끊기고 있었다** — Q 는 밀림 0.25초 → 경직 0.2초를 싣고(`FirstMeleeMainSkill`), 몬스터는 Attack → Knockback → Hit 로 넘어가며
공격 타격 이벤트(`NotifyAttackHit`)는 Attack 상태에서만 유효하다. 문제는 **화면**이었다.

| 몹 | 컨트롤러 | 보이던 것 |
|---|---|---|
| HumanoidBot | 클립 = 공격·대기·걷기뿐. `Hit` 파라미터만 있고 상태·전이 없음 | 밀리는 동안 공격 클립이 계속 돎 → 공격을 끝까지 휘두르는 것처럼 보임 |
| ChompBot | `A_Chompbot_Hit`(15프레임)이 AnyState→Hit 로 연결됨 | 1.5배속 + 절반에서 0.25초 블렌드로 나가 반응이 거의 안 보임 |

## 1. 바꾼 것

| 파일 | 내용 |
|---|---|
| `Monster/MonsterHitReactionPolicy.cs` | `ShouldFreezeForHitReaction(state, rank, archetype, hasHitState)` — Hit/Knockback · 일반 몹 · 터렛 아님 · 피격 상태 없음이면 true |
| `Monster/MonsterBase.cs` `OnStateChanged` | 위 판정이 true 면 `ApplyLocomotionFreeze()`(대기 첫 프레임에서 정지 — Gauntlet 그로기와 같은 방식). 해제는 기존 상태 이탈 해제(`ApplyReleaseActionPose`)가 한다. **전 피어 로컬, RPC 없음**(복제된 `_state` 콜백) |
| `Monster/MonsterBase.cs` `HasHitAnimatorState` | 애니메이터에 `hitTrigger` 와 **같은 이름의 상태**가 있는가. 🔴 이 이름 규약에 기댄다 — 피격 상태를 다른 이름으로 저작하면 "없음"으로 보고 멈춰 세운다 |
| `Monster/MonsterBase.cs` `TryEnterKnockback` | 경직 값(`staggerDuration`)이 있는 밀림이면 **밀림 시작부터** `Stunned` 를 밀림+경직 길이로 건다(전엔 밀림이 끝난 뒤 경직 0.2초만). 이펙트가 0.45초 내내 보이게 하려는 것. 밀림 이동은 `HandleKnockback` 이 직접 해서 이동 봉쇄와 안 싸운다 |
| `Monster/MonsterStatusEffect.cs` | `event Action EffectsChanged` + `OnNetworkSpawn/Despawn`(복제 `_active.OnValueChanged` 구독·해제) **신설**. 기존 Apply/Remove/만료 무변경 |
| `Effects/MonsterStatusEffectVfxPresenter.cs` **신규** | 플레이어 `StatusEffectVfxPresenter` 의 몬스터판. 같은 `StatusEffectVfxSet` 형식을 읽는다 |
| `Resources/MonsterStatusEffectVfxSet.asset` **신규** | 몬스터용 표. 기절 → `FX_Faint_Entry`, 머리 위, 오프셋 (0, 0.3, 0), 우선순위 50 |
| `Monster/Editor/*Tests.cs`, `Editor/CombatEditModeTestRunner.cs` | 자세 고정 판정 표 · 몬스터 표 로드/기절 줄 테스트(전투 묶음에 등록) |

경직 길이(0.2초)·밀림 값은 **그대로**다(경석 10-26).

**2차(같은 날) — 중간보스 취약 8초**: 간파 그로기 1초와 별개로 카운터 성공부터 8초 취약(복제 플래그 `_vulnerable`)을 연다.
`MonsterCounterWindow.vulnerableDuration`(xlsx 행 없음 → 기본 8) · `MonsterBase.ServerBeginCounterVulnerable` · `UsesAttackSuperArmor`(취약 중 공격 슈퍼아머 안 걸기 —
Gauntlet·Spinner·WallBot 의 공격 슈퍼아머 부여 4곳이 이 값을 본다) · `CanEnterKnockback` 의 넷째 인자가 "취약 밀림 연쇄 중" → "취약 중" 으로 바뀌었다.
규칙 정본: [monster-aggro-hitreact.md](monster-aggro-hitreact.md) §2.0

## 2. 민경 님 확인용 — 몬스터 기절 이펙트

- 프리팹에 붙이지 않는다. `MonsterBase.OnNetworkSpawn` 이 `MonsterStatusEffectVfxPresenter.TryInstall` 로 런타임에 붙인다(체력바와 같은 방식). 23호(`rank: Boss`)는 제외.
- 표는 플레이어 표와 **따로**다(`Resources/MonsterStatusEffectVfxSet`) — 몬스터는 머리 위 기준점이 달라서 오프셋·배율을 따로 맞춘다. 엔트리는 같은 `FX_Faint_Entry` 를 가리킨다.
- 머리 위 자리 = 런타임 자식 `StatusVfx_Overhead`. **머리 뼈(`Head`)가 있으면 매 프레임 그 위 0.4m 로 따라간다**(최소 높이 = 몸 콜라이더 꼭대기).
  뼈가 없으면 몸 콜라이더 꼭대기 고정, 콜라이더가 꺼져 있으면 렌더러 bounds, 둘 다 없으면 루트 +2m. 몸 자리 = 그 중간, 발밑 = 루트.
  - 🔴 3차(경석 10-26 Play): 처음엔 콜라이더 꼭대기 고정이었는데, 몸 콜라이더는 몹마다 2m 캡슐 그대로라 **GauntletBot(골반만 1.7m)에서 별이 가슴 속에 묻혀** 중간보스만 안 보였다. 중간보스 3종 모두 `Head` 뼈가 있다.
- 켜고 끄기: `MonsterStatusEffect.EffectsChanged` + 상태 전이(사망이면 끔). **추가 네트워크 없음**.
- 🔴 몬스터에 기절을 거는 곳은 지금 **팔라딘 Q 밀림·경직**과 **고정 터렛 경직**뿐이다. 평타 경직(Hit 0.4초)은 상태이상을 안 건다 → 평타마다 별이 뜨지 않는다.
- **중간보스 간파 성공 그로기**는 상태이상이 아니라 FSM 상태(Groggy)라서, Presenter 가 `rank: MidBoss` ∧ `Groggy` 이면 기절 줄을 켠 것으로 본다.
  그 뒤 이어지는 **취약 8초**(`IsMidBossVulnerable`)는 하늘색으로만 보이고 별은 안 띄운다(팀장 10-26 — 별은 실제로 멈춰 있을 때만). 규칙: [monster-aggro-hitreact.md](monster-aggro-hitreact.md) §2.0
- 크기·높이가 몬스터에 안 맞으면 `Resources/MonsterStatusEffectVfxSet.asset` 의 `offset`·`scale` 만 고치면 된다.

## 3. 영향 범위

| 대상 | 자세 고정 | 기절 이펙트 |
|---|---|---|
| HumanoidBot | ✅ Q 밀림·경직 + 평타 경직(0.4초) 모두 대기 자세 정지 | ✅ Q 때 |
| ChompBot · MortarBot | ❌ 자기 피격 클립 그대로 | ✅ Q 때 |
| PeekABot · TeslaBot(고정 터렛) | ❌ 대기 상태가 숨기·충전 자세라 제외 | ✅ Q 경직 때(밀림 없음) |
| GauntletBot · SpinnerBot(중간보스) | ✅ Q 밀림·경직 동안(피격 상태 없음). Gauntlet 은 그로기 자세와 이어지고, Spinner 는 그로기 복귀 때 Dizzy 로 돌아간다 | ✅ **간파 성공 그로기(1초)** + Q 경직 때. 취약 8초 나머지는 하늘색만(팀장 10-26) |
| WallBot(중간보스) | ❌ 자기 피격 클립 그대로 | 같음 |
| 23호 | ❌ | ❌ 설치 안 함 |

## 4. 확인 (Play — 아직 안 함)

- [ ] HumanoidBot 이 공격하는 중에 Q → 공격 모션이 바로 멈추고 대기 자세로 밀린다, 머리 위 별이 뜬다, 플레이어가 안 맞는다
- [ ] ChompBot 공격 중 Q → 피격 클립 + 별, 공격이 안 들어온다
- [ ] Q 홀드(최대 3초) 동안 별이 계속 떠 있고, 놓으면 약 0.45초 뒤 사라진다(outro 0.3초)
- [ ] HumanoidBot 평타 경직 — 대기 자세로 잠깐 멈추고 별은 안 뜬다
- [ ] 별 높이·크기가 몬스터 머리와 맞는다(안 맞으면 §2 끝 줄)
- [ ] MPPM 클라이언트 창에서도 같다(전부 복제 상태 기준이라 같아야 한다)
- [ ] Q 로 죽인 몹 머리 위에 별이 남지 않는다
- [ ] 중간보스 간파 성공 → 1초 별 + 하늘색 → 별 꺼지고 하늘색은 간파 성공부터 8초까지 남는다. 그동안 움직이고 공격한다
- [ ] 취약 중 공격하는 Gauntlet·Spinner·WallBot 에 Q → 공격이 끊기고 밀림 + 경직 별. 8초가 지나면 Q 에 다시 안 밀린다
- [ ] 그로기 중 Gauntlet 을 Q 로 밀면 대기 자세가 이어진다(걷기·대기 모션이 돌지 않는다). Spinner 는 남은 그로기 동안 Dizzy 로 돌아간다
- [ ] 튜토리얼 스피너 연습 중 Q 는 여전히 막힌다(무한 슈퍼아머)
