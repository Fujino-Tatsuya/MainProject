# 몬스터 어그로 · 피격 경직 규칙 (일반 몹 · 중간보스)

> 2026-10-26 경석 확정. 23호(`rank: Boss`)는 별도 규칙(위협표·점프/돌진 승계 — PLAN-boss-aggro)이라 여기서 뺀다.
> 계획·배경: [PLAN-midboss-hitreact-aggro.md](../../PLAN-midboss-hitreact-aggro.md)

## 1. 어그로 (서버, `MonsterBase.HandleSeekAndCombat`)

| 단계 | 규칙 |
|---|---|
| 획득 | 대상이 없을 때 `detectionRadius` 안 **최근접** 플레이어(Δy ≤ `detectionHeightTolerance`, 유령 제외). 실패하면 0.2~0.25초 뒤 재시도 |
| 유지 | 한 번 물면 유지(인지 반경 밖이어도) |
| **사거리 안 전환** | 대상이 `attackRange + 0.5` 밖이고 다른 플레이어가 `attackRange` 안이면 그중 최근접으로 바꾼다. Idle/Chase 에서만, 0.2초 간격. **전 몹 공통**(23호 제외) — `TrySwitchToInRangeTarget` |
| 주기 재선정 | `retargetInterval` > 0 이면 그 주기로 Idle/Chase 에서 최근접을 다시 고른다(현재 대상도 후보). 중간보스 3종 = 8초, 일반 몹 = 0(끔) |
| 해제 | 사망·유령 / Δy 초과 1초 / `loseTargetRadius` 밖 1초(일반 몹 = 인지×1.2, 중간보스 = 0 끔) / 몬스터가 스폰에서 `leashRadius` 이탈(Return·풀피) |
| 피해 기반 | 없음 — 때린 사람으로 돌아서는 규칙은 없다(사거리 안 전환이 그 역할을 대신한다) |

## 2. 피격 경직·밀림 (`MonsterHitReactionPolicy`)

| | 일반 몹 | 중간보스(`rank: MidBoss`) |
|---|---|---|
| 평상시 피격 | 0.4초 경직(갱신형). 공격 중 평타는 면제 | **경직 없음 · 밀림 없음**(피해만) |
| 팔라딘 Q(밀림·경직 값을 싣는 유일한 플레이어 공격) | 밀림 → 경직 | 평상시 **면역** / **취약 중 밀림 → 경직**(공격 중이어도) |
| 취약 중 평타·간파 스킬 | — | **피해만** |
| 슈퍼아머 | 경직·밀림 전부 막음 | 같음. 단 **취약 중에는 공격 중 슈퍼아머를 걸지 않는다**(`UsesAttackSuperArmor`). 튜토리얼 스피너 연습의 무한 슈퍼아머는 그대로 막는다 |

### 2.0 중간보스 간파 성공 → 그로기 1초 · 취약 8초 (팀장 10-26)

| | 길이 | 동작 | 보이는 것 |
|---|---|---|---|
| **그로기** | `MonsterCounterWindow.groggyDuration` = 1초(xlsx) | 멈춰 있다(공격·이동 없음) | 하늘색 + **머리 위 기절 별** |
| **취약** | `MonsterCounterWindow.vulnerableDuration` = 8초, **카운터 성공부터**(그로기 포함) | 그로기가 끝나면 평소처럼 움직이고 **공격한다** — 공격을 쉬는 상태가 아니라 **CC 에 약한** 상태다 | **하늘색만**(별 없음) |
| 취약 중 팔라딘 Q | 밀림 0.25 + 경직 0.2초 | 공격 중이면 공격이 끊긴다. 그로기 중에 밀렸으면 끝난 뒤 **남은 그로기로 복귀**(그로기 시간은 밀림 동안에도 흐른다) | 기절 별(Q 경직의 `Stunned`) |

- 취약은 FSM 상태가 아니라 **복제 플래그**(`MonsterBase._vulnerable` → `IsMidBossVulnerable`)다. 서버가 `ServerBeginCounterVulnerable`(카운터 성공 3곳 —
  Gauntlet·Spinner·WallBot `CounterSucceeded`)로 열고, 시간 만료·사망·리쉬 복귀·스폰에서 닫는다. 취약 중 다시 간파하면 8초를 처음부터 센다.
- ⚠️ `vulnerableDuration` 은 아직 `GameData.xlsx` 에 행이 없다 → SO·프리팹 값(기본 8)으로 돈다(Play·빌드 공통). 테이블에서 튜닝하려면
  Monster 시트 `MonsterCounterWindow` 블록에 행을 추가한다(SVN).
- **취약 색**: 취약 동안 몸을 23호와 같은 하늘색(0.45, 0.75, 1)으로 칠한다 — `MonsterBase.RefreshVulnerableTint`, 복제 플래그만 보고 전 피어가 칠한다.
  간파 창 색과 같은 베이스 틴트 칸이라 창이 켜져 있으면 비킨다(취약 중 다음 공격이 창을 열 수 있다 — 닫히면 다시 칠한다).
- 피격 클립: WallBot 만 있다(`A_WallBot_Hit`). Gauntlet·Spinner 는 없고, 에셋 구매품이라 추가하지 않는다.
- **경직 자세 고정(10-26)**: 피격 상태가 없는 몹(HumanoidBot·GauntletBot·SpinnerBot)은 Hit·Knockback 동안 **대기 자세로 멈춰 선다**
  (`MonsterHitReactionPolicy.ShouldFreezeForHitReaction` — 터렛·23호 제외). Gauntlet 은 취약 중 Q 로 밀려도 그로기 자세(대기 정지)와 이어져
  취약이 끊겨 보이지 않는다. Spinner 는 그로기로 돌아올 때 정지를 풀어 Dizzy 클립을 보인다. 팔라딘 Q 밀림·경직 동안은 `Stunned` 가 걸려
  **머리 위 기절 이펙트**가 뜬다(평타 경직은 안 뜬다). 상세: [handoff-monster-stun-vfx.md](handoff-monster-stun-vfx.md)
- Gauntlet 은 그로기를 "대기 자세 정지"로 보여 주므로 밀림 뒤 그로기로 복귀할 때 다시 건다(`OnVulnerableGroggyResumed`).

## 2.1 바닥 예고(데칼) — 간파 가능한 공격만 (팀장 10-26)

| 공격 | 예고 |
|---|---|
| Gauntlet 스매시 · Spinner 스핀 돌진 | 그대로(파랑 = 간파 가능) |
| Gauntlet 펀치 · Spinner 채찍 | **바닥 예고 없음.** 휘두르기 전 대기 0.125초만(`punchTelegraphDuration`·`whipTelegraphDuration` — 대기를 아예 빼면 너무 빠르다) |
| 23호 송전기 차징 오라 | **원 표시**(`chargeAuraTelegraphPrefab` = `AoeDecalTelegraph`, 반경 = 판정 반경). 09-29 에 비웠던 것을 되돌렸다 — 원이 없으면 위험한지 모른다 |

⚠️ 두 대기 시간은 `GameData.xlsx` MidBoss 시트에도 있다 — Play(데이터: 테이블)·빌드는 xlsx 값을 쓰므로 xlsx 도 0.125 여야 한다.

## 3. 왜 (10-26 문제)

10-14 P1(공격이 끝나면 슈퍼아머 해제) 이후 중간보스가 추격 중 피격마다 경직됐다. 분산 교전에서 멀리 있는 대상을
쫓는 동안 옆 사람이 때리면 경직이 끊기지 않고, 락온이라 옆 사람으로 돌아서지도 않아 GauntletBot 이 제자리에 섰다
(피격 클립이 없어 그냥 서 있는 것으로 보였다).

## 4. 테스트

`Tools/Tests/전투 EditMode 테스트 실행` — `MonsterHitReactionPolicyTests`(등급별 경직·밀림 표 · 경직 자세 고정) · `MonsterEngagePolicyTests`(사거리 전환 히스테리시스) · `MonsterStatusEffectVfxSetTests`(몬스터 기절 이펙트 표).
