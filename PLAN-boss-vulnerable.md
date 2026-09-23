# PLAN-boss-vulnerable.md — 인터럽트 성공 후 **취약 상태** (2026-09-21)

> 그릴 16문항으로 확정. **승인 후 구현.**
> 상위 문서 — [PLAN.md](PLAN.md) · [PLAN-boss-backlog.md](PLAN-boss-backlog.md) · [CONTEXT.md](CONTEXT.md).

## 0. 한 줄

인터럽트 성공 시 보스에게 **5초짜리 취약 상태**를 건다. 그 동안 슈퍼아머가 해제되어
**밀림·경직·기절이 들어간다.** 단 차징·레이지 시퀀스 중에는 취약이어도 슈퍼아머가 유지된다.

---

## 1. 🔴 조사에서 드러난 것 — 전제 3개가 틀렸다

### 1-1. 슈퍼아머만 꺼서는 **아무 일도 안 일어난다**

보스는 CC가 **세 겹**으로 막혀 있다.

| # | 차단 | 위치 | 이번에 |
|---|---|---|---|
| 1 | `AutoHitReactions => false` — base 의 Hit경직·그로기누적·**넉백 진입**을 전부 끔 | [TwentyThreeBoss.cs:62](Assets/1.Scripts/Monster/Boss/TwentyThreeBoss.cs:62) | **그대로 둔다**(§1-2) |
| 2 | 슈퍼아머 — `ApplyStatus` 가 SA 외 모든 CC를 무시 | `MonsterStatusEffect:74` | 취약 중 해제 |
| 3 | 프리팹에 **`LinearKnockback`(IKnockbackable) 없음** + Rigidbody `isKinematic:1` · `Constraints:126`(FreezeAll) | `TwentyThree.prefab` | **안 붙인다**(§1-3) |

### 1-2. 🔴 `AutoHitReactions` 를 켜는 것은 **오답**이다

이 플래그 하나가 세 가지를 동시에 연다:

1. `TryEnterKnockback` 진입 — 그런데 그건 `SetState(MonsterState.Knockback)` 을 하고,
   `PlayStateAnimation(s != Attack)` 이 **`AbortAttackChain()` 을 부른다** →
   **팀장 확정("넉백은 패턴을 안 끊는다")과 정면으로 어긋난다.**
2. **그로기 누적**([MonsterBase.cs:1213](Assets/1.Scripts/Monster/MonsterBase.cs:1213)) — 팀장 확정("CC는 누적 안 함")과 어긋난다.
3. 피격 경직 `EnterHit` — 스킬 한 대마다 보스가 경직된다.

→ **보스 전용 경로를 따로 판다.** `AutoHitReactions` 는 `false` 로 유지한다.

### 1-3. `LinearKnockback` 을 보스에 붙이면 **안 된다**

그건 물리 경로다 — NavMeshAgent 를 끄고 `AddForce` 로 민 뒤, 끝날 때
`NavMesh.SamplePosition(…, 2f)` 에 **실패하면 Warp 를 건너뛴다.** 그러면 에이전트가
오프메시에서 켜지고, Unity 가 "가장 가까운 메시"로 스냅하는데 아레나(x≈500)에 메시가 없으면
**맵 본체(원점)로 끌려간다** — `BossEncounterDirector` 주석에 기록된 사고다.

반면 `MonsterBase.HandleKnockback` 은 **매 틱 `NavMesh.SamplePosition(next, 0.5f)`** 로
클램프하고 실패하면 그 틱 이동을 생략한다. **맵 이탈이 구조적으로 불가능하다.**
우리는 이 클램프 방식만 가져오고 상태 전이는 안 쓴다(§3-2).

### 1-4. ⚠️ 예고는 이미 보스를 따라간다 — 내 초기 우려는 과장이었다

`BossAttackConeTelegraph` 는 런타임에 **보스 GameObject 본체에** `AddComponent` 되고
([:1074](Assets/1.Scripts/Monster/Boss/TwentyThreeBoss.cs:1074)) `LateUpdate` 에서 자기 `transform` 기준으로 그린다 →
**밀리면 예고도 같이 움직이고 같이 돈다. RPC 재전송이 필요 없다.**
게다가 돌진은 `PerformAttackHit` 이 `HideAttackConeClientRpc()` 를 부른 **다음 줄**에서
`BeginDash()` 를 하므로 **돌진 중에는 예고가 아예 없다.**
→ "예고가 판정에 대해 거짓말한다" 문제는 이 작업에서 발생하지 않는다.

### 1-5. 🔴 슈퍼아머가 이미 "무한"이 아니다 (의도와 코드가 어긋남)

`ApplyStatus` 가 만료시각을 **덮어쓴다**:

```
스폰        ApplyStatus(SuperArmor, 0f)            → 무한
공격마다     ApplyStatus(SuperArmor, _stateTimer)   → 유한으로 교체
_stateTimer 경과 → Update 만료 스캔이 SuperArmor 제거
```

→ **첫 공격 이후로는 공격 사이사이에 슈퍼아머가 이미 없다.** 지금은 §1-1 의 1·3번 때문에
증상이 안 보일 뿐이다. 이걸 먼저 고치지 않으면 "언제 취약한지"가 데이터가 아니라 **우연**으로 정해진다.

### 1-6. 지금 보스에게 CC를 거는 스킬은 **하나도 없다**

- 플레이어 스킬 중 몬스터에 `Stunned`/`Rooted` 를 거는 곳 **0건**(E·R에 CC 없음, 원거리 캐릭터 스킬 미구현)
- `Stunned` 를 거는 코드는 `MonsterBase` 의 포탑 분기와 `ExitKnockback` 뿐이고 **둘 다 Q의 `staggerDuration`** 에서 온다
- 그런데 팀장 확정으로 **Q의 `staggerDuration` 은 보스에 적용하지 않는다**

→ **이번 작업 후 실제로 들어오는 것은 "Q의 밀림" 하나뿐이다.** 경직·기절은 **인프라만** 깐다(팀장 확정).

---

## 2. 확정 사항 (그릴 16문항)

| # | 결정 |
|---|---|
| D1 | 취약 = **넉백 + 경직 + 기절** 전부 받는다 (단 §1-6 — 지금 들어오는 건 넉백뿐) |
| D2 | **인터럽트 성공 시에만** 부여. 차징 실패 그로기는 취약 없음 |
| D3 | 지속 **5초** |
| D4 | **차징·레이지 시퀀스 중에는 SA 유지** — 취약이 남아 있어도 CC 안 통함 |
| D5 | **차징 진입 이륙도 SA 유지**. 반면 **점프어택 이륙(JumpTakeoff)은 CC 가능** |
| D6 | 슈퍼아머 수명을 고친다 — **항상 SA, 취약 때만 해제** |
| D7 | **경직·기절 → 패턴 끊김**(`AbortAttackChain` + 리액션) |
| D8 | **넉백 → 패턴 유지, 위치만 밀림** |
| D9 | Q의 `staggerDuration` 은 **보스에 적용하지 않는다**(일단. 3인이 돌아가며 넣으면 5초 내내 CC가 되어 인터럽트 1회 대비 딜타임이 과함) |
| D10 | 취약 중 **재인터럽트 무시** — 카운터 창을 아예 안 연다 |
| D11 | CC로 끊은 것은 **그로기 카운트에 누적 안 함**(인터럽트·차징 그로기만 누적) |
| D12 | 돌진 중 밀리면 **목적지도 같이 평행이동**(직선 유지, 곡선 방지) |
| D13 | 밀림 배율 **0.4**, **`BossDataSO`**(보스 전용) |
| D14 | 넉백 애니 — 공격 중이면 **현 공격 애니 유지**, 비공격 중이면 `getowned` |
| D15 | 취약 타이머는 차징 중에도 **그대로 흐른다**(날아간다) |
| D16 | 표시 = **보스 HUD 버프/디버프 슬롯**(로아식). 이번 범위에 포함 |

---

## 3. 구현

### 3-1. 취약 상태 본체 (`TwentyThreeBoss`)

```csharp
readonly NetworkVariable<float> _vulnerableUntil = new(...);   // 서버 write, 전 피어 read
public bool IsVulnerable => _vulnerableUntil.Value > NetworkManager.ServerTime.TimeAsFloat;
```

- **부여**: `ReceiveAttack` 의 카운터 성공 지점(`EnterCounterGroggy` 호출 옆) 한 곳뿐.
- **만료**: 서버 틱에서 시간만 비교(별도 코루틴 없음 — 이 레포의 만료 규약과 동일).
- 🔴 **`ServerTime` 을 쓴다.** `Time.time` 은 피어마다 다르고, 이 값은 복제되어 HUD가 읽는다.

### 3-2. 슈퍼아머 재설계 (D6)

**현재**: 스폰 무한 → 공격마다 유한으로 덮어씀 → 만료 (§1-5)

**변경**: 슈퍼아머를 **단일 소유자**가 관리한다.

```
매 서버 틱:
  bool wantSA = !IsVulnerable || InSuperArmorSequence;
  if (wantSA  && !status.HasSuperArmor) status.ApplyStatus(SuperArmor, 0f);   // 무한
  if (!wantSA &&  status.HasSuperArmor) status.RemoveStatus(SuperArmor);
```

- `InSuperArmorSequence` = 차징(`ChargeMove` 제거됐으므로 `_chargeJump` · `ChargeWait`) ·
  레이지(`RageDash`) 구간 (D4·D5)
- **공격별 `ApplyStatus(SuperArmor, _stateTimer)` 2곳을 제거**한다([:843](Assets/1.Scripts/Monster/Boss/TwentyThreeBoss.cs:843) · [:1235](Assets/1.Scripts/Monster/Boss/TwentyThreeBoss.cs:1235)) —
  틱 소유자와 두 주인이 생기면 서로 덮어쓴다.
- 🔴 `BossAttackEntry.superArmor` 필드가 **의미를 잃는다.** 지워야 하나, 다른 보스(GauntletBot 등)가
  같은 SO 구조를 쓰므로 **23호에서만 안 읽는다**는 주석을 단다(유령 필드 교훈).

### 3-3. 밀림 — 상태를 바꾸지 않는 변위 (D8·D12·D13)

`TryEnterKnockback` 을 **쓰지 않는다**(§1-2). `TwentyThreeBoss.ReceiveAttack` 에서 직접:

```csharp
if (IsVulnerable && attackInfo.knockbackStrength > 0f && attackInfo.knockbackDuration > 0f)
    BeginPush(dir, attackInfo.knockbackStrength * KnockbackResistance, attackInfo.knockbackDuration);
```

서버 틱에서 `_pushRemaining > 0` 이면:

```csharp
Vector3 step = _pushDir * (_pushSpeed * dt);
Vector3 next = transform.position + step;
if (NavMesh.SamplePosition(next, out var hit, 0.5f, NavMesh.AllAreas))
{
    if (agent != null && agent.enabled && agent.isOnNavMesh) agent.Move(step);  // 경로 유지
    else transform.position = new Vector3(hit.position.x, transform.position.y, hit.position.z);
    _dashDestination += step;   // [D12] 돌진 목적지도 평행이동
}
// 샘플 실패 = 경계 → 이번 틱 생략 (맵 이탈 불가)
```

- 🔴 **`agent.Move()` 를 쓴다.** `agent.enabled = false` 로 만들면 진행 중인 돌진·전진이 죽는다
  (`StartDashMove` 가 "agent 꺼짐" 경고를 내고 변위 0이 된다). `Move` 는 경로를 유지한 채 변위만 준다.
- **상태를 안 바꾼다** → `PlayStateAnimation` 이 안 불리고 `AbortAttackChain` 도 안 돈다 → 패턴 유지(D8).
- D14 — 애니는 손대지 않는다. 공격 중이면 공격 애니가 그대로 돌고, 비공격 중이면
  `getowned` 를 한 번 재생한다(공격 중이 아닐 때만).

### 3-4. 경직·기절 → 패턴 끊김 (D7) — **인프라만** (§1-6)

`MonsterStatusEffect._active` 변화를 서버에서 관측해, `Stunned`/`Rooted`/`Airborne` 이
**새로 켜지면** 패턴을 끊는다:

```csharp
if (justGainedHardCC && _attackPhase != BossAttackPhase.None && !InSuperArmorSequence)
{
    AbortAttackChain();                 // 잡힌 사람 해제·모델 복구·예고 정리까지 전부
    ForceHitReaction(HitReactionDuration);   // getowned
}
```

- 🔴 **그로기 카운트를 올리지 않는다**(D11) — `EnterCounterGroggy` 를 부르지 않는다.
- 지금은 이 경로를 발동시킬 스킬이 없다(§1-6). **로그로만 검증**하고, 나중에 CC 스킬이
  생기면 배선 없이 바로 동작한다.

### 3-5. 취약 중 재인터럽트 무시 (D10)

`AcquireGrab` / `StartAttack` 의 `SetCounterWindow(true)` 를 `!IsVulnerable` 로 게이트한다.
→ 취약 중에는 카운터 창이 안 열리므로 `ReceiveAttack` 의 카운터 판정이 성립하지 않는다
(인터럽트 스킬은 데미지만 들어간다).

### 3-6. HUD 버프/디버프 슬롯 (D16)

레퍼런스(로스트아크) 기준 — **보스 HP 게이지 아래 중앙**, 작은 정사각 아이콘을 가로로 배열.
버프는 노란 테두리 / 디버프는 빨간 테두리로 묶고, **각 아이콘 하단에 남은 초**.

- `CombatHUD.prefab` 에 슬롯을 **저작 스크립트로 멱등 생성**(미니맵·타이머 때와 같은 방식 —
  `CombatHudSlotAuthoring` 확장). 🔴 프리팹 수작업 편집 금지(팀원과 충돌).
- **아이콘 에셋 자리는 비워 둔다**(팀장 확정 — 아직 없음). 비어 있으면 단색 사각 + 툴팁만.
- 마우스 오버 툴팁은 이름 + 남은 초.
- 표시할 항목은 지금 **취약 하나뿐**이지만, 데이터 주도로 만들어 나중에 늘릴 수 있게 한다.

---

## 4. 🔴 함정

1. **`agent.Move()` 는 agent 가 켜져 있고 on-mesh 일 때만 유효하다.** 체공(`Leap`)은 무적이라
   CC가 안 들어오므로 문제없지만, 워프 직후 한 프레임은 확인이 필요하다.
2. **취약 중 페이즈 전환**으로 차징이 시작되면 그 순간부터 SA 가 다시 켜진다(D4).
   타이머는 계속 흐르므로(D15) 차징 20초 안에 취약이 통째로 소모된다 — **의도다.**
3. **`_chargeJump` 와 점프어택 이륙이 같은 코드 경로**(`BeginJumpTakeoff`)다.
   D5 에 따라 **그 플래그로 SA 여부를 갈라야 한다.**
4. **밀림 중 `_dashDestination` 평행이동**(D12)은 돌진이 실제로 진행 중일 때만 의미가 있다.
   돌진 전(예고 중)에 밀리면 목적지는 아직 없다 — `BeginDash` 가 그때 위치에서 새로 잡는다.
5. **`BossAttackEntry.superArmor` 가 죽은 필드가 된다**(§3-2). 안 읽히는 노브를 남기면
   다음 사람이 그걸 조절하고 아무 일도 안 생긴다 — 주석으로 명시할 것.
6. 🔴 **취약은 복제되어야 한다** — HUD 가 클라에서 읽는다. `Time.time` 금지, `ServerTime` 사용.

## 5. 완료 기준

1. 인터럽트 성공 → **5초 취약**. HUD 슬롯에 남은 초가 뜬다(호스트·클라 둘 다).
2. 취약 중 Q → 보스가 **밀린다. 패턴은 안 끊긴다**(돌진 중이면 계속 돌진하되 경로가 평행이동).
3. 🔴 **방 밖으로 나가지 않는다** — 벽 구석으로 몰아붙여도 NavMesh 경계에서 멈춘다.
4. 취약이 아닐 때 Q → **안 밀린다**.
5. 차징·레이지 중에는 취약이어도 **안 밀린다**.
6. 점프어택 이륙 중에는 밀린다 / 체공 중에는 아무것도 안 들어간다.
7. 취약 중 인터럽트 재시도 → **데미지만**, 취약 갱신 없음, 그로기 카운트 불변.
8. 취약 5초가 끝나면 슈퍼아머가 **자동으로 돌아온다**.
9. 공격 사이사이에 슈퍼아머가 **꺼지지 않는다**(§1-5 회귀 확인).
10. 컴파일 에러 0 · 콘솔 경고 0.

## 6. 범위 밖

- 경직·기절을 실제로 거는 **플레이어 스킬**(원거리 캐릭터·Q 경직 복원) — 인프라만 깐다
- 취약 아이콘 **아트 에셋** — 자리만 만든다
- `LinearKnockback` 을 보스에 붙이는 것 — §1-3 이유로 하지 않는다
- 일반몹·중간보스의 밀림 저항 — `BossDataSO` 에만 둔다(D13)
