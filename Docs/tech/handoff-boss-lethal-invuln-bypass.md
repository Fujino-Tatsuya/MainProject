# handoff — 보스 즉사 패턴 무적 우회 (할 일 105)

> 2026-10-27 · 작업 claude-alt · 브랜치 `feature/BossLethalInvulnBypass`.
> 경석 영역(TwentyThreeBoss·BossElectricFloor·BossDataSO)과 은희 영역(Player)을 함께 건드려 공유한다.

## 1. 무엇·왜

보스 즉사 패턴 2종을 대시 등 **무적으로 피하지 못하게** 했다(사용자 grill 10-27).

| 패턴 | 호출 위치 | 데이터 칸(`BossDataSO`, int 0/1 — 1=무적 무시·0=끔, 기본 1) |
|---|---|---|
| ④ 폐기 처분 저지 실패 대폭발 | `TwentyThreeBoss.cs` 자폭 실패 분기 `ServerQueueDischarge(AllMask, …, lethal: raidSelfDestructBlastLethal, ignoreInvulnerability: …)` | `raidSelfDestructBlastIgnoreInvuln` |
| 북진 압박 차징 실패 방전 | `TwentyThreeBoss.RaidRelay.cs` `BeginPushNorthCharge` | `pushNorthDischargeIgnoreInvuln` |

- 뚫는 무적 = `InvulnerabilityCause` **전부**(Dash·LandingProtection·FallRecovery·Revive·Cinematic·SkillAction·TraitSelection·CinematicHidden). 모두 `PlayerInvulnerability.IsServerInvulnerable` 한 값으로 모이므로 그 게이트 하나만 넘긴다.
- 기존 즉사(`PlayerHitDamageCap.LethalOverride` — 한 방 상한·보호막 넘김, 가붕이 E 방어 불가)는 그대로다. 무적 무시는 **별도 플래그**라 패턴마다 따로 끌 수 있다.
- 다른 `ServerQueueDischarge` 호출(② 실패 방전·집결 방전·측면 장판·연쇄 실패)은 인자를 안 넘겨 예전 그대로 무적으로 피할 수 있다.
- 두 칸은 처음엔 bool 이었으나 xlsx 로 내보내려고 **int 0/1**(`[Range(0,1)]`)로 바꿨다(사용자 결정 10-27 (c)). 읽는 곳은 `!= 0`.
  기존 `raidSelfDestructBlastLethal`(bool)은 범위 밖이라 그대로다.
- 기존 보스 데이터 에셋(No23·No23_A·No23_B·No23_Raid·No23_Solo)에는 두 칸이 직렬화된 적이 없어(bool 시절에도 다시 저장 안 함) 로드 시 필드 초기값 `1` 이 들어간다
  (타입 변경 뒤 에디터에서 5개 모두 1·dirty 아님 확인). 에셋 파일은 다시 쓰지 않았다.

## 2. 메커니즘 (하나로)

```
BossElectricFloor.ServerQueueDischarge(…, ignoreInvulnerability)
  → TickRaid: _fireIgnoreInvuln          (Warn() 은 매번 false 로 되돌린다 — 일반·충전 장판은 영향 없음)
  → Fire: 대상마다 PlayerHitDamageCap.LethalOverride / PlayerInvulnerabilityBypass.Active 를 켜고
          BossPatternTargets.Damage → finally 로 둘 다 끈다
  → Player.ReceiveAttack → Unit.ApplyHealthDamage → Player.CanApplyHealthDamage
       bypass = PlayerInvulnerabilityBypass.Active   (추락 · 보스 즉사 공통 — 게이트 표시는 이것 하나)
       PlayerInvulnerabilityBypass.AllowsHealthDamage(invulnerable, bypass)

Player.ApplyFallDamage
  → before = PlayerInvulnerabilityBypass.Begin()  (Active = true, 이전 값 보관)
  → _fallDamageBypass = true                      (체력 재생 리셋 구분용만)
  → BreakShield + ApplyDirectHealthDamage
  → finally: _fallDamageBypass = false; PlayerInvulnerabilityBypass.End(before)
```

- `PlayerInvulnerabilityBypass`(`Assets/1.Scripts/Player/`) = 서버 전용 "무적 무시" 컨텍스트 + 순수 규칙. 추락과 보스 즉사 방전이 **같은 값·같은 규칙**으로 무적 게이트를 넘는다(새 차단 계층 없음).
  - 추락은 `Begin`/`End` 로 이전 값을 되돌린다 — 겹쳐 불려도 바깥 컨텍스트를 끄지 않는다. 보스 방전은 `LethalOverride` 처럼 넣고 finally 로 끈다(기존 그대로).
  - `_fallDamageBypass` 는 이제 **게이트에 쓰지 않고** 체력 재생 리셋 구분(`healthRegen.NotifyDamage(…, isFallDamage)`)에만 남았다 — 보스 방전은 일반 피해로 재생을 리셋한다.
  - 추락 동작은 그대로: 방어력(직접 피해 경로)·쉴드(`BreakShield`)·무적(위 컨텍스트) 무시, 무시 중엔 `base.CanApplyHealthDamage` 를 건너뛰는 것도 전과 같다.
- 피해 파이프라인 확인 결과(끝까지 들어간다):
  - `BossPatternTargets.IsValid` = 살아 있음(Alive·활성) + 23호가 쥐고 있지 않음. **무적은 거르지 않는다** → 무적 대상도 판정에 들어온다.
  - 판정은 발 위치(`FootPosition`) — Hurtbox 비활성(무적 동안 꺼짐)과 무관. `Damage` 는 `Unit.ReceiveAttack` 을 직접 부른다.
  - `Player.ReceiveAttack` 의 가드 훅(가붕이 E 방어·반격)은 무적이면 건너뛰고, 아니어도 `LethalOverride` 면 방어 불가 — 무적 무시 방전에서 막는 경로가 아니다.
  - 한 방 상한 = `LethalOverride` 로 0. 999999 피해 → 방어력 경감 뒤에도 보호막·HP 를 모두 넘긴다.
  - `Unit.ApplyHealthDamage` 의 차단은 사망(HP 0)과 `CanApplyHealthDamage` 뿐. `HealthFloor` 는 로비 훈련 보정에서만 0 이 아니다.
- 그대로 안 맞는 사람: 23호에게 잡혀 있음, 쓰러짐(Soul 등 Alive 아님)·완전 사망. 사양 범위 밖(무적이 아니다).

## 3. 데이터 테이블 (xlsx) — 병합 때 메인이 Export → SVN

처음(bool)엔 `Export Template` 이 bool 을 내보내지 않아(data-table.md §8 — 정수·실수·`[DataTableText]` 문자열만) xlsx 에 못 넣었다.
→ 사용자 결정 (c): 두 칸을 **int 0/1** 로 바꿨다. 이제 정수라 템플릿 대상이다.

- 확인(파일 안 씀): `Tools/Data/Export Template 필드 목록 보기 (파일 안 씀)` → `BossDataSO — 대상 5개(No23, No23_A, No23_B, No23_Raid, No23_Solo), 필드 288개`
  안에 `raidSelfDestructBlastIgnoreInvuln`·`pushNorthDischargeIgnoreInvuln` 둘 다 있다. (`raidSelfDestructBlastLethal` 은 bool 이라 여전히 없다 — 범위 밖.)
- **레인에서는 xlsx Export·SVN 을 하지 않았다.** 병합 때 메인이 development 에서 `Export Template — 병합` → SVN 커밋한다. 그러면 생길 것:

| 시트 | 새 행(필드) | 열(대상) | 값(기본) |
|---|---|---|---|
| `Boss` | `raidSelfDestructBlastIgnoreInvuln` | No23 · No23_A · No23_B · No23_Raid · No23_Solo | 5칸 모두 `1` |
| `Boss` | `pushNorthDischargeIgnoreInvuln` | No23 · No23_A · No23_B · No23_Raid · No23_Solo | 5칸 모두 `1` |

  - 1 = 무적 무시, 0 = 끔(무적으로 피할 수 있음). 병합 Export 라 기존 기획 값은 (대상, 필드) 로 유지되고 이 두 행만 늘어난다. Strings 시트 변화는 없다(툴팁은 Strings 대상 아님 — 확인은 Export 때).
  - Export 전까지는 행이 없으므로 테이블 모드 Play·빌드 모두 **SO 값(1)** 으로 돈다(data-table.md §3 "테이블에 없으면 = SO 값").

## 4. 게이지 인원(`ServerFloorHit`) 판단

- 기존: 무적이면 게이지 인원에서 뺐다(`!IsInvulnerable`). 근거 = 기획 [boss-electric-floor.md §7](../design/boss/boss-electric-floor.md) "무적… 으로 **피해가 무효가 된** 플레이어는 제외".
- 변경: `PlayerInvulnerabilityBypass.CountsAsFloorHit(invulnerable, bypass)` — **무적 무시 방전이면 무적이어도 센다**. 피해가 무효가 되지 않고 실제로 들어가므로 §7 의 정의 그대로다.
  일반 방전(무시 없음)은 예전처럼 뺀다.
- 영향: `ServerFloorHit` 는 지금 구독자가 없다(보스 고유 게이지 B6 미구현) — 런타임 동작 변화 없음, 로그 `게이지 인원` 숫자만 달라진다.

## 5. 확인

- EditMode: `PlayerInvulnerabilityBypassTests`(규칙 · 게이지 · 컨텍스트 기본 꺼짐 · 추락 컨텍스트 Begin/End·중첩 · BossDataSO 기본값 1),
  `PlayerHealthRegenModelTests`(추락 피해 재생 리셋 구분 포함), `BossRaidRulesTests` — 10-27 48/48 통과.
- **Play(사람 확인 필요)** — 레이드 23호(`No23_Raid`, 데이터 모드 아무거나):
  1. ④ 폐기 처분에서 실드를 안 부수고 시간 초과 → 예고 끝 순간 **대시 중**인 플레이어도 사망. Console `[Unit/진단] … 피해 적용 — 요청 999999`.
  2. 북진 압박에서 탑을 남긴 채 차징 끝 → 예고 칸 위에서 대시 중이어도 사망(1회차 안전 구역은 그대로 안전).
  3. 두 칸을 0 으로 하면(인스펙터 모드) 대시로 피할 수 있다 — `[Unit/진단] … CanApplyHealthDamage=false` 경고.
  4. 회귀: ② 실패 방전·측면 장판·일반 전기 장판은 여전히 대시로 피한다. 추락 피해는 예전처럼 무적 중에도 들어간다.
