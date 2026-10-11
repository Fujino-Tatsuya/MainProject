# handoff — 튜토리얼 존3 스피너봇 연습 모드 (할 일 15 · S9 몬스터 쪽)

> 받는 사람: **경석**(몬스터 영역 소유). 작성: 은희 레인(claude-alt), 2026-10-11, 브랜치 `feature/TutorialSpinnerPractice`.
> 요지: **SpinnerBot.cs 에 주입 인터페이스 하나(`ISpinnerBotOverride`)와 호출 지점 4곳만** 넣었다.
> MonsterBase · MonsterCounterWindow · CounterWindow · 데이터(SO/xlsx) · 프리팹은 **손대지 않았다.**
> 구현체가 붙어 있지 않은 스피너봇(절차 맵 `ZoneM_typeC` 포함)·다른 중간보스는 동작이 그대로다.

## 1. SpinnerBot 에 넣은 훅 (`Assets/1.Scripts/Monster/Boss/SpinnerBot.cs`, +62 / −7줄)

| 위치 | 내용 | 구현체 없을 때 |
|---|---|---|
| 필드 + `Override` 프로퍼티 | 같은 오브젝트의 `ISpinnerBotOverride` 를 처음 한 번 `GetComponent` 로 찾아 캐시 | `null` |
| `StartAttack` 첫머리 | 주입이 `OverridesAttack` 면 ① 창 컴포넌트가 없거나 `TryBeginSpin()` 이 false → **공격을 시작하지 않고 return**(상태는 Seek 가 둔 Chase/Idle 그대로) ② true → 채찍 없이 스핀 확정. 아니면 원래 `SpinReady && Random.value < spinChance` | `else` 분기 = 원래 줄 그대로(Random 소비 순서도 동일) |
| `StartAttack` 돌진 끝점 | `ResolveDashTarget` 뒤에 `ClampDashTarget` 로 한 번 더 자름(예고 띠 길이도 이 값을 따라감) | 호출 안 함 |
| `HandleAttack` Dash 단계 | 주입 아래서 시작한 스핀이면 `meleeAttack.Hit()` 대신 `TickHarmlessDash()`(**피해 0**, 밀쳐내기는 구현체) | 원래 반복 히트 블록(들여쓰기만 한 단계) |
| `TakeDamage` 첫 줄 | 주입이 `BlocksHealthDamage` 면 `attackInfo.damage = 0`(값 복사 지역 사본) 후 base | 조건 false |
| 파일 끝 | `public interface ISpinnerBotOverride` 정의(멤버 5개) | — |

**왜 `TakeDamage` 에서 0 으로 만드나** — `Unit.CanApplyHealthDamage` 를 override 하면 막힌 피해마다
`[Unit/진단] … 피해를 거부했다` 경고가 찍힌다(튜토리얼 내내 콘솔 도배). 피해 0 은 `ApplyHealthDamage` 가 조용히
버리고, 그 뒤 `MonsterBase.TakeDamage`(피격 반응) · SpinnerBot 간파 판정(`TryConsumeInterrupt` →
`CombatStatsEvents.RaiseServerCounterSucceeded` → `CounterSucceeded` → `ForceGroggy`)은 그대로 돈다.
⚠️ `ApplyDirectHealthDamage`·비율 피해(AttackInfo 를 안 타는 경로)는 막지 않는다 — 튜토리얼 존3 에 그런 피해원이 없다는 전제.

**왜 MonsterBase 를 안 고쳤나** — 휴면/전환 대기의 "인지·추격·공격 정지"는 이미 공개된
`MonsterBase.SetServerLogicSuspended(bool)` 로, 휴면의 넉백·CC 무시는 `MonsterStatusEffect` 무한 슈퍼아머로 해결된다.

## 2. 연습 모드 본체 (`Assets/1.Scripts/Tutorial/SpinnerPractice/`)

| 파일 | 역할 |
|---|---|
| `TutorialSpinnerPractice.cs` | NetworkBehaviour, `ISpinnerBotOverride` 구현. 튜토리얼 스피너봇 **루트**에 붙인다 |
| `SpinnerPracticeModeModel.cs` | 순수 C# 모드 상태 기계(휴면 깃발 × 연습/전환 대기/본 모드) |
| `SpinnerPracticeWindowSchedule.cs` | 순수 C# 창 간격(기본 9초) |
| `SpinnerPracticeWindowTracker.cs` | 순수 C# 창 열림/닫힘 → 성공/놓침 · 연속 놓침 |
| `SpinnerPracticeArea.cs` | 순수 함수 스폰 반경 클램프(위치 · 돌진 직선 끝점) |
| `Editor/SpinnerPracticeModelTests.cs` | EditMode 24건. 지금은 `PlayerEditModeTestRunner` 픽스처 목록에 등록(튜토리얼 러너로 옮길 예정) |

모드별 동작:

| 모드 | FSM | 체력 | 넉백·CC | 공격 | 이동 범위 |
|---|---|---|---|---|---|
| 휴면 `Dormant` | 정지(`SetServerLogicSuspended(true)`) | 잠금 | 무시(무한 SuperArmor) | 없음 | 클램프 |
| 연습 `Practice` | 동작 | 잠금(피격 반응은 있음) | 받음 | 스핀만 · 창 매번 · 간격 9초 · 돌진 피해 0 + 밀쳐내기 | 클램프 |
| 전환 대기 `Transition` | 정지(기본 2초) | 잠금 | 받음 | 없음 | 클램프 |
| 본 모드 `Real` | 동작 | 감소 | 받음 | **원래 스피너봇**(채찍+스핀, 40%·쿨 6초, 피해 있음) | 없음(원래 리쉬) |

- 창 길이는 원래 값(`MonsterCounterWindow` 저작 1.5초 ÷ 공격속도) 그대로다. 성공 시 원래 동작(공격 취소 + 그로기 `groggyDuration` = 프리팹 1초).
- 간파 슬로우모션(`IInterruptSlowMotionTarget`)은 구현하지 않는다 → 스피너봇에 연결되지 않는다.
- 집계: 새 카운터 없음. `CombatStatsEvents.ServerCounterSucceeded` 는 SpinnerBot 원래 경로가 창 하나당 1회 낸다(연습 중에도).

## 3. 튜토리얼 본류가 연결하는 법

1. 튜토리얼 씬(또는 튜토리얼 전용 SpinnerBot 프리팹 Variant)의 스피너봇 루트에 `TutorialSpinnerPractice` 를 **미리** 붙인다.
   NetworkBehaviour 라 **런타임 AddComponent 금지**. `MonsterCounterWindow` 가 같은 루트에 있어야 한다(원래 프리팹에 있음).
2. 인스펙터: `startDormant`(기본 true) · `windowInterval` 9 · `practiceRadius` 6m · `pushRadius` 2.5m · `pushStrength` 5 · `realModeDelay` 2.
   반경 중심은 서버 스폰 위치. 바꾸려면 `SetPracticeAnchor(Vector3)`.
3. 서버(호스트)에서 호출:
   - `SetDormant(bool)` — 휴면 걸기/풀기(풀면 휴면 전 모드, 처음이면 연습).
   - `EnterPractice()` — 연습 시작(휴면 해제 · 첫 창 즉시 가능 · 연속 놓침 0).
   - `EnterRealMode()` — 2초 정지 후 본 모드. 중복 호출 무시.
4. 서버 이벤트(인스턴스):
   - `ServerWindowOpened(practice)`
   - `ServerWindowClosed(practice, bool succeeded)`
   - `ServerWindowMissed(practice, int consecutiveMisses)` — "연속 4번 놓치면 힌트" 는 여기 인자 ≥ 4 로.
   - `ServerModeChanged(practice, SpinnerPracticeMode)`
   - 서버 조회: `ConsecutiveMisses`.
5. 전 피어 조회: `Mode`(NetworkVariable 복제), `IsHealthLocked`(= 본 모드 전) — 체력 바 회색·잠금 표시는 안내 UI 몫.

## 4. 판단(위임에서 확정되지 않아 보수적으로 고른 것)

- **창 간격 9초 = 창 열림 → 다음 창 열림 가능 시각**(열림 기준). 첫 창은 연습 시작 직후 사거리에 들어오면 바로.
- **놓침 = 성공 없이 닫힌 모든 경우** — 만료뿐 아니라 공격 취소·`EnterRealMode` 정지로 닫힌 창도 놓침으로 센다.
  이벤트는 모드와 무관하게 낸다(본 모드 창도 알린다) — 걸러 쓰는 건 본류 몫.
- **밀쳐내기**: 원래 스피너봇 근접 판정은 `applyKnockback: 0` 이라 피해 0 만으로는 밀리지 않는다. 그래서 구현체가
  돌진 동안 몸 중심 `pushRadius` 안 플레이어를 **스핀 한 번에 한 사람당 1회** `Unit.Knockback` 으로 민다(판정 형상은 단순 원).
- **휴면/전환 대기 체력 잠금**: 전환 대기 2초 동안도 체력은 잠근다(본 모드 시작부터 감소).
- **클램프**: 휴면·연습·전환 대기에서 매 LateUpdate 수평 원 밖이면 원 위로 되돌린다(에이전트 켜짐 = `Warp`, 넉백 중 = transform).
  본 모드에서는 걸지 않는다(원래 스피너봇 = 원래 리쉬 18m). 출구 배리어가 반경 6m 밖이 아니면 인스펙터로 줄일 것.
- 휴면 해제 시 슈퍼아머를 통째로 `RemoveStatus` 한다 — 스피너봇 데이터는 `startsWithSuperArmor: 0` 이라 원래 상태와 같다.

## 5. 검증

- 컴파일 에러 0, `Tools/Tests/플레이어 EditMode 테스트 실행` 202 통과 / 0 실패(이 중 `SpinnerPracticeModelTests` 24/24).
- 🔴 **Play 검증 안 함**(사람이 Play/MPPM 으로 확인 필요): 휴면 무적 · 9초 주기 창 · 놓침 시 피해 0 밀쳐내기 ·
  간파 성공 그로기 · 반경 클램프 · 본 모드 전환 후 원래 패턴 · 클라 창에서 `Mode` 복제.
