# PLAN-boss-entrance-charge.md — 입장 연출 애니 + 차징 점프 이동 (2026-09-21)

> 그릴 7문항으로 확정. **승인 후 구현.**
> 상위 문서 — [PLAN.md](PLAN.md)(1~8차 확정) · [PLAN-boss-backlog.md](PLAN-boss-backlog.md) · [CONTEXT.md](CONTEXT.md).

---

## 0. 먼저 — 백로그가 준 거짓 신호 2건 (실물 확인으로 정정)

| 백로그 주장 | 실물 (2026-09-21 확인) | 처리 |
|---|---|---|
| **B1 점프 이륙 = 다음 1순위, 미착수** | ✅ **이미 구현·저작 완료.** `BossAttackPhase.JumpTakeoff` · `CrossFadeJumpTakeoffClientRpc` · `No23.asset`: `jumpTakeoffState: Leap` / `jumpTakeoffDuration: 0.633` | 백로그에서 **완료로 이동** |
| **B2 = "8개 공격 전부 `damage: 0`" → 데미지 미착수** | ⚠️ `attacks[].damage` 는 실제로 전부 0 이 **맞다**. 그런데 그건 **"데미지가 없다"가 아니다** — `attackDamage: 10` 폴백이 들어가고, 전용 필드는 따로 채워져 있다(`grabTickDamage 5` · `grabThrowDamage 20` · `chargeAuraDamage 20`). **팀장이 Play 로 확인함.** | 값은 **건드리지 않는다.** 백로그 B2 문구만 정정 |

🔴 **교훈** — `damage: 0` 은 "미설정"이 아니라 **"폴백을 쓴다"** 는 뜻이다. 이 레포에는 폴백이
`e.damage > 0 ? e.damage : AttackDamage` 형태로 5군데 있다. 0 을 보고 "안 들어간다"로 읽지 말 것.

---

## 0-B. Codex 교차검증 (2026-09-21) — **계획 2건이 뒤집혔다**

`codex exec -s read-only` 로 돌렸다. 조사는 실제로 수행됐으나(40여 명령 · 173,768 토큰 ·
`TwentyThreeBoss.cs`·`MonsterBase.cs`·`BossEncounterDirector.cs`·두 `.asset`·애니메이터 컨트롤러·
레포 전체 참조 스윕) **최종 보고서 직전에 워크스페이스 크레딧이 소진돼 exit 1** 로 죽었다.
중간 진행 메시지 4개에 남은 판정을 건져 **전부 이 레포에서 직접 재확인**했다.

🔴 **운용 — `CLAUDE.local.md` 의 Codex 실패 판정 규칙이 이걸 못 거른다.**
문서는 "결과 파일이 2~3KB 이거나 `sandbox_lock_failed` 가 보이면 실패"라고 하는데,
이번엔 결과 파일이 **0바이트**였고 에러(`out of credits`)는 **stderr 에만** 있었다.
→ 판정 기준에 **"0바이트"** 와 **"out of credits"** 를 추가할 것.
또 **v0.155.0-alpha.9.2**(문서는 0.153.4)는 `CODEX_HOME` 이 `%TEMP%` 밑이면
`Refusing to create helper binaries under temporary dir` 경고를 낸다(진행은 된다).

| | 판정 | 근거 |
|---|---|---|
| **E1 전체 무효** (개정함, 아래 §2) | 🔴 틀림 | 돌진은 `ignoreDistanceWindow: 1` 이라 거리창을 통째로 건너뛴다([TwentyThreeBoss.cs:690](Assets/1.Scripts/Monster/Boss/TwentyThreeBoss.cs:690)). `dashMaxDistance` 도 `min(값, 지속시간×속도)` 에서 **지속시간이 먼저 물린다**([:1121](Assets/1.Scripts/Monster/Boss/TwentyThreeBoss.cs:1121)) |
| **E2 전제 틀림** (개정함, 아래 §3) | 🔴 틀림 | `jumpSearchRadius` 참조 2곳은 `FindFarthestPlayer` 2개가 아니다 — [:2284](Assets/1.Scripts/Monster/Boss/TwentyThreeBoss.cs:2284) 타겟 탐색 + [:3785](Assets/1.Scripts/Monster/Boss/TwentyThreeBoss.cs:3785) **`CountAlivePlayers()` = 차징 송전탑 개수 결정** |
| C3 입장 데미지 차단 | ✅ 확인 (근거가 더 강함) | `SetServerLogicSuspended(true)` 는 Update 만 멈추는 게 아니라 **State 를 `Idle` 로 바꾼다** |
| C4 `Land` 에서 `FinishChain()` 건너뛰기 | ✅ 확인 | 자원 정리 없이 다음 행동만 결정하는 함수 → 차징으로 이어갈 땐 안 부르는 게 맞다. **플래그 설계 유지** |
| 잡기 내려치기 데미지원 | ✅ 정정 | `AttackDamage` 아님 — `grabSlamDamage > 0 ? grabSlamDamage : grabThrowDamage`(=20) ([:1763](Assets/1.Scripts/Monster/Boss/TwentyThreeBoss.cs:1763)) |
| **입장 착지 애니가 자동으로 안 끊긴다** | 🔴 새 리스크 | 애니 교체 경로는 `OnStateChanged → PlayStateAnimation`([MonsterBase.cs:1571](Assets/1.Scripts/Monster/MonsterBase.cs:1571)) **뿐이다.** 전투 시작 후 보스가 Idle 에 머물면 상태 변화가 없어 착지 클립이 계속 돈다 → **E3 에 로코모션 복귀 진입점 추가**(§4) |
| `chargeZonePrefab` 비어 있음 | ⚠️ 확인 | `{fileID: 0}` → E4 완료기준의 "전기 장판"은 **지금 데이터로 검증 불가**. 없는 걸 찾지 말 것 |

**못 받은 것** (크레딧 소진으로 답이 안 나옴): C5(`EndDashMove` 무해성) · C7(`ChargeMove` 제거 시 남는 참조) ·
C8(놓친 위험 전반) · C9(플래그 vs 전용 단계). → **내가 직접 확인하고 구현 중에 기록한다.**

---

## 1. 범위

**한다**

| # | 무엇 | 크기 |
|---|---|---|
| **E1** | 돌진 사거리 — **지속시간·속도**를 올린다 (거리값이 아니었다) | 데이터만 |
| **E2** | `jumpSearchRadius` → **중립적 이름**으로 개명 (두 용도를 다 덮게) | 아주 작음 |
| **E3** | **보스방 입장 연출 애니** — 하강 중 `JumpHover`, 착지 순간 `JumpLanding`. **데미지 없음** | 작음 |
| **E4** | **Charging 이동을 점프로** — 이륙→체공→착지 풀세트. 걸어가는 `ChargeMove` 구간 제거 | 중간 |

**안 한다 (팀장 확정)**

- 데미지 밸런스 — 정상 동작 중. 위 §0.
- 착지 클립 길이 맞추기 — `Boss_23_landingattack` 1.92초가 점프 1초 / 입장 0.9초에서 잘리는 건
  **지금대로 둔다.** B3-① 은 "의도"로 문서에만 남긴다.
- `leashRadius` — PLAN §5 는 "15 유지"라 했으나 실물은 **22**. 이미 누군가 올렸고 방 크기를
  생각하면 22 가 합리적이라 **건드리지 않는다.** (기록만)

---

## 2. E1 — 돌진 사거리 (개정 · 팀장 확정 2026-09-21)

### 왜 원래 계획이 무효였나

PLAN §5 는 거리값 2개를 올리자고 했는데 **둘 다 효과가 0 이다.**

| 값 | 무효 사유 |
|---|---|
| 돌진 `maxDistance` 20→45 | DashAttack 은 `ignoreDistanceWindow: 1`. [TwentyThreeBoss.cs:690](Assets/1.Scripts/Monster/Boss/TwentyThreeBoss.cs:690) 의 `if (!e.ignoreDistanceWindow)` 가 거리창 자체를 건너뛴다 — **`minDistance`/`maxDistance` 를 아예 안 읽는다** |
| `dashMaxDistance` 16→30 | 실사거리 = `min(dashMaxDistance, dashDuration × moveSpeed × dashSpeedMultiplier)`. 현재 `min(16, 0.91×2.5×6 = **13.65**)` → **13.65m 가 상한**이다. 30 으로 올려도 그대로 |

🔴 **코드 주석이 이미 말하고 있었다** ([:1117](Assets/1.Scripts/Monster/Boss/TwentyThreeBoss.cs:1117)):
*"`dashMaxDistance` 는 구속 조건이 아닌 경우가 많다 — 실측상 지속시간이 먼저 끝난다."*
PLAN §5 가 이 주석보다 나중에 쓰였는데 반영되지 않았다. **다음에 SO 값을 올리기 전에
그 값을 읽는 코드에 클램프가 있는지 먼저 본다.**

### 바꿀 것

진짜 병목은 **지속시간과 속도**다. 둘 다 올린다.

| 값 | 현재 | → | 근거 |
|---|---:|---:|---|
| `dashDuration` | 0.91 | **1.5** | 실사거리의 실제 상한 |
| `dashSpeedMultiplier` | 6 | **7.8** | 팀장 지시 "속도 1.3배" (6 × 1.3) |
| `dashMaxDistance` | 16 | **30** | 위 둘을 올리면 **이제 이 값이 의미를 갖는다.** 방 한 변(30m) |

**결과 실사거리** = `min(30, 1.5 × 2.5 × 7.8 = 29.25)` = **29.25m** (기존 13.65m → **2.1배**).
속도는 15 m/s → **19.5 m/s**.

🔴 **29m 는 방(30×30) 을 거의 가로지른다.** 의도한 것이 "방 어디서든 후열로 돌진"이므로
방향은 맞지만 **체감은 Play 에서 봐야 한다.** 과하면 **`dashDuration` 부터** 내린다
(속도는 팀장이 지정한 값이므로 그대로 두고).

### 이 변경이 자동으로 따라오는 것 (확인함 — 손댈 필요 없음)

- **직선 예고** — `DashTelegraphReach()` 가 같은 식을 쓴다 → 예고도 29m 로 늘어난다.
  "예고가 판정에 대해 거짓말하지 않는다" 규약 유지.
- **체인 예산** — [:831](Assets/1.Scripts/Monster/Boss/TwentyThreeBoss.cs:831) 이 `DashDuration` 프로퍼티를 읽으므로 자동 반영.
- **벽 클램프** — `StartDashMove` 의 `NavMesh.Raycast` 가 방 밖으로 안 나가게 이미 자른다.

### 🔴 Play 에서 볼 것 (이 변경의 부작용 후보)

1. **캐리 대상이 29m 끌려간다.** `dashCarryFrontOffset 2.2` + 벽 여유로 끼는 건 막혀 있다.
   ⚠️ **정정(2026-09-21)** — 여기 원래 "`dashStunDuration: 1` 보다 돌진(1.5초)이 길어져
   끌려가는 도중 스턴이 풀린다"고 적었는데 **틀렸다.** `dashStunDuration` 은 캐리 지속이 아니라
   **벽 충돌 후** 거는 기절이다(`ReleaseDashCarry` 에서 구속을 푼 다음 줄, `applyImpact` 일 때만).
   캐리는 `Restrained` 상태이고 타이머가 없어 돌진 내내 유지된다 — 둘은 겹치지 않는다.
   🔴 **값 이름으로 의미를 단정한 것**이 원인이다. 이 계획서에서 같은 실수를 세 번 했다
   (`damage: 0` → "데미지 없음" · `dashMaxDistance` → "사거리 상한" · 이번). §2 의 교훈과 같은 뿌리.
2. 돌진 예고선이 화면을 가로지를 만큼 길어져 **읽기 어려울 수 있다.**

### 안 건드리는 것

- `rageDashDuration 0.7` · `rageDashSpeedMultiplier 6` · `rageDashMaxDistance 16`
  → 레이지 돌진 실사거리 `min(16, 10.5)` = **10.5m** 로 유지. 3연속이라 의미가 다르다.
- 돌진 `maxDistance` (효과 0 이므로 건드릴 이유가 없다) · `minDistance 0` · `leashRadius 22`
- 🔴 `No23_Solo.asset` — 솔로 테스트용 별도 저작본. **범위 밖.**

## 3. E2 — `jumpSearchRadius` → `playerScanRadius` (개정)

### 왜 `attackSearchRadius` 가 아닌가

참조가 2곳인데 **용도가 다르다**:

| 위치 | 용도 |
|---|---|
| [:2284](Assets/1.Scripts/Monster/Boss/TwentyThreeBoss.cs:2284) `FindFarthestPlayer()` | 점프·돌진 타겟 선정 |
| [:3785](Assets/1.Scripts/Monster/Boss/TwentyThreeBoss.cs:3785) `CountAlivePlayers()` | **차징 송전탑 개수 결정** (1인 1 / 2인 2 / 3인+ 4) |

두 번째는 공격 탐색이 아니다 → `attackSearchRadius` 로 바꾸면 **더 틀린 이름**이 된다.
**`playerScanRadius`** 로 간다 (팀장 확정).

⚠️ 기록해 둘 것 — 이 값이 30 → 45 로 올라갔을 때 **송전탑 개수 판정 반경도 같이 올라갔다.**
의도된 것이었는지는 확인된 바 없다. 지금 분리하지는 않되(팀장 확정) **Tooltip 에 명시**한다.

### 할 일

- `BossDataSO.jumpSearchRadius` → `playerScanRadius`
- 🔴 **`[FormerlySerializedAs("jumpSearchRadius")]` 를 반드시 붙인다.** 안 붙이면 `No23.asset` ·
  `No23_Solo.asset` 의 저작값 **45 가 조용히 기본값 30 으로 되돌아간다.** 컴파일도 테스트도 안 깨진다.
- Tooltip 에 **두 용도를 다 적는다** — "점프·돌진 타겟 탐색 + 차징 송전탑 인원 계산 **공용**".
- 호출부 2곳에도 한 줄 주석으로 다른 쪽 용도를 가리킨다.
- 바꾼 뒤 `.asset` 키가 실제로 `playerScanRadius: 45` 로 재직렬화됐는지 **확인한다.**

## 4. E3 — 보스방 입장 연출 애니

### 지금

`BossEncounterDirector` 가 보스를 착지점 +18m 에 스폰하고 `descendDuration 1.2초` 동안
`transform.position` 을 직접 내린다. **애니메이터는 아무도 안 건드린다** → 로코모션/Idle 포즈로
뻣뻣하게 내려온다. 착지 후 `impactHoldSeconds 0.9초` 정지 뒤 전투 시작.

### 바꿀 것

| 시점 | 재생 |
|---|---|
| `BeginDescent()` | `jumpHoverState`(= `JumpHover`) — 점프어택 체공과 같은 포즈 |
| `TickDescending` 이 `SetPhase(Impact)` 하는 줄 | `jumpLandingState`(= `JumpLanding`) |
| `BeginCombatServer()` — **FSM 깨우기 직전** | 로코모션 복귀 (아래 🔴 Codex 지적) |

🔴 **세 번째 줄이 Codex 교차검증에서 나온 것이다.** 원래 계획은 "착지 클립(1.92초)이
`impactHoldSeconds`(0.9초) 에서 잘린다"고 전제했는데 **그 보장이 없다.** 애니를 바꾸는 경로는
`OnStateChanged → PlayStateAnimation`([MonsterBase.cs:1571](Assets/1.Scripts/Monster/MonsterBase.cs:1571)) **뿐이고**, 전투가 시작돼도
보스가 `Idle` 에 머물면 **상태 변화가 없어 착지 클립이 계속 돈다.** 명시적으로 되돌린다.
⚠️ 순서 — **`SetServerLogicSuspended(false)` 보다 앞**이다. 뒤에 두면 FSM 이 고른 첫 애니를 덮는다.

### 배선

보스의 애니메이터는 `TwentyThreeBoss` 가 소유하고, Director 는 `MonsterBase` 만 들고 있다.
→ **작은 인터페이스 하나**를 판다 (`IBossChargeSequence` 와 같은 관용구).

```csharp
public interface IBossEntranceAnimation
{
    void PlayEntranceDescentServer();   // 체공 포즈
    void PlayEntranceLandingServer();   // 착지 클립
    void EndEntranceAnimationServer();  // 로코모션 복귀 — FSM 을 깨우기 직전
}
```

- `TwentyThreeBoss` 가 구현 — 내부에서 **기존 `CrossFadeJumpStateClientRpc(landing:)` 를 그대로 재사용**한다.
  새 RPC 를 만들지 않는다.
- `CacheBossComponents` 에서 `GetComponent<IBossEntranceAnimation>()` 캐시. **없으면 경고만 하고
  연출은 그대로 진행**한다(웰즈·다른 보스가 들어와도 안 깨지게).

### 🔴 함정 · 이미 확인한 것

1. **착지 데미지는 안 나간다 — 이미 막혀 있다.** `NotifyAttackHit` 이 `State != MonsterState.Attack`
   이면 즉시 반환하는데, 연출 중에는 `SetServerLogicSuspended(true)` 라 FSM 이 안 돌아 State 가
   `Attack` 이 아니다. → `Boss_23_landingattack` 의 `OnAttackHit` 이벤트가 와도 무해하다.
   **주석으로 근거를 남긴다** — 나중에 누가 연출 중 FSM 을 깨우면 여기가 터진다.
2. **`CrossFadeJumpStateClientRpc` 는 앞뒤 표식도 같이 만진다** (`DirectionIndicator.SetSuppressed(!landing)`).
   입장에도 그게 맞다 — 하강 중 숨고 착지 후 나온다. 그대로 재사용한다.
3. **RPC 를 `Spawn()` 과 같은 프레임에 쏜다.** NGO 는 같은 틱의 스폰 메시지와 RPC 순서를
   보장하지만 이 레포에서 실측한 적이 없다 → **MPPM 2인에서 클라 화면을 반드시 확인**한다.
   (호스트에서만 보이는 종류의 버그는 이 레포가 이미 여러 번 겪었다.)
4. 착지 클립 1.92초 > `impactHoldSeconds` 0.9초 → **잘린다. 의도(팀장 확정).**

### 완료 기준

1. 하강 중 보스가 **체공 포즈**다 (Idle 로 서서 내려오지 않는다).
2. 착지 순간 **착지 클립이 튄다.**
3. **데미지가 안 들어간다** — 연출 중 플레이어 HP 불변.
4. **MPPM 클라(호스트 아님) 화면에서도 똑같이 보인다.**
5. 🔴 **전투가 시작되면 착지 포즈가 남지 않는다** — 보스를 가만히 두고(공격 안 하고 멀리 서서)
   Idle 에 머물게 만들어 확인한다. 이게 Codex 가 짚은 지점이다.

## 5. E4 — Charging 이동을 점프로

### 지금

`BeginCharge()` 가 NavMesh 로 `BossLandingPoint` 까지 **걸어간다**(`ChargeMove` 단계 ·
`chargeMoveSpeedMultiplier 3` · `chargeMoveTimeout 4초` · 못 가면 워프). 도착하면 `StartChargingInPlace()`.

### 바꿀 것 — 점프어택과 **같은 경로를 재사용**한다

```
BeginCharge()                 착지점 = BossLandingPoint 확정 (기존 로직 그대로)
  → 이륙 (JumpTakeoff)        보이고 맞는다. Leap 클립 0.633초
  → 체공 (Leap)               숨김 + 무적. 1.2초
  → 착지 (Land)               BossLandingPoint 로 워프 + JumpLanding 클립
  → StartChargingInPlace()    ← 여기서 갈린다 (점프어택은 Recovery 로 간다)
```

**단계를 새로 만들지 않는다.** 기존 `JumpTakeoff`/`Leap`/`Land` 를 그대로 쓰고
**`bool _chargeJump` 플래그**로 종료 분기만 가른다. 이유: 전용 단계 3개를 추가하면 이륙 배속
역산·모델 숨김·무적 복구·`AbortAttackChain` 회수가 **전부 복제**된다 — 그 복제본이 한쪽만
고쳐지는 게 이 레포에서 반복된 사고다.

### 손댈 지점

| 파일 | 내용 |
|---|---|
| `TwentyThreeBoss.BeginJump()` | 이륙 개시 부분을 **`BeginJumpTakeoff()` 로 추출**(동작 불변) |
| `TwentyThreeBoss.BeginJumpHover()` | 🔴 `_chargeJump` 면 **착지 예고 장판을 안 띄운다**(팀장: 예고는 안 붙인다) |
| `TwentyThreeBoss.BeginCharge()` | NavMesh 이동 블록 제거 → `_jumpArrivePoint = _chargeMoveTarget` + `BeginJumpTakeoff()` |
| `TwentyThreeBoss` `case Land:` | `if (_chargeJump) { _chargeJump = false; StartChargingInPlace(); }` else 기존 Recovery |
| `TwentyThreeBoss.AbortAttackChain()` | `_chargeJump = false` 리셋 (**조기 반환 앞**) |
| `TwentyThreeBoss` `_stateTimer` 예산 (811행) | `ChargeMoveTimeout` → `JumpTakeoffDuration + JumpHover + JumpLanding` 로 교체 |
| **제거** | `BossAttackPhase.ChargeMove` · `TickChargeMove()` · `ChargeArriveDistance`/`ChargeMoveTimeout`/`ChargeMoveSpeedMul` 프로퍼티 |
| **제거** | `BossDataSO.chargeMoveArriveDistance` · `chargeMoveSpeedMultiplier` · `chargeMoveTimeout` |

🔴 **`BossAttackPhase` 에서 값을 지우는 것**은 "끝에만 추가" 규약과 반대 방향이다. 런타임 전용
enum 이라 직렬화는 없지만 **로그·디버그 표시가 정수로 읽히므로** 지운 자리 뒤 값들이 한 칸씩
당겨진다. → **자리를 비우지 말고 `ChargeMove` 를 `[Obsolete]` 없이 그냥 지우되, 지웠다는 주석을
그 자리에 남긴다**(뒤 값이 당겨지는 것은 감수 — 저장되는 값이 아니다).

### 🔴 함정

1. **`StartChargingInPlace()` 의 `EndDashMove()`** — 이제 이동을 안 하므로 저장값이 없다.
   `_dashPrev*` 가 `< 0` 이면 복원을 건너뛰는지 **확인하고**, 아니면 조건을 건다.
   (안 보면 `agent.speed` 가 엉뚱한 값으로 덮인다.)
2. **`FaceScreenSouthForCharge()` 는 유지**한다 — 워프로 도착해도 방향은 화면 남쪽이어야 한다.
   순서도 그대로(에이전트가 선 다음).
3. **`_wells.SetSuppressed(true)`** — 점프 경로가 이미 한다. 차징 점프도 같이 걸리고,
   `StartChargingInPlace` 뒤에 그로기·사망이 다시 억제를 밀어 주므로 안전하다.
   단 **해제 경로(`ReleaseWellsSuppression`)가 `ArriveJump` 에 있으므로 차징 점프도 지난다** — 확인할 것.
4. **차징 착지에 데미지가 안 붙는다** — `PerformAttackHit` 의 `case ChargeSequence: break;` 가
   이미 no-op 이다. 즉 `JumpLanding` 클립의 `OnAttackHit` 이 와도 아무 일도 안 난다. **의도대로다.**
5. **워프가 NavMesh 밖이면** 차징 위치가 어긋난다. 기존 `NavMesh.SamplePosition` 스냅은
   **그대로 유지**한다(이동을 없애도 착지점 보정은 필요하다).

### 완료 기준

1. 차징 진입 시 보스가 **올라갔다가 사라지고** `BossLandingPoint` 에 **떨어진다.**
2. 착지 직후 **차징이 정상 시작**된다 — 송전탑·오라·구슬.
   ⚠️ **전기 장판은 이 목록에서 뺀다** — `chargeZonePrefab` 이 `{fileID: 0}`(비어 있음)이라
   지금 데이터로는 나올 수가 없다(Codex 지적, 확인함). **없는 걸 찾느라 시간 쓰지 말 것.**
3. 체공 중 **안 맞는다**. 착지 후 **다시 맞는다.**
4. 차징 착지에 **데미지가 없다.**
5. 차징을 카운터·그로기로 끊어도 **보스가 투명하거나 무적으로 남지 않는다.**
6. 연속으로 차징해도 **애니 배속이 남지 않는다**(이륙 3배속 복원).
7. 컴파일 에러 0 · 콘솔 경고 0.

---

## 6. Unity 에디터

전부 `.cs` + `No23.asset`(git 추적 텍스트 에셋)이다 → **에디터를 켜둔 채로 해도 된다**(CLAUDE.md).
Play 모드만 아니면 된다 — 확인함(`isPlaying: false`).

## 7. 검증

이 작업이 끝나면 **MPPM 2~3인 한 판**으로 아래를 한 번에 본다.

- [PLAN-boss-backlog.md](PLAN-boss-backlog.md) **B0 잡기 3건** (기존 대기분)
- **E3** 입장 연출 4건 · **E4** 차징 점프 7건
- 거리값(E1): 방 끝에서도 돌진이 나가는가
