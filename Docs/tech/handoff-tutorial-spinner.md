# handoff — 튜토리얼 존3 스피너봇 연습 모드 (할 일 15 · S9 몬스터 쪽)

> **🟪 Legacy (할 일 107, 2026-10-27)** — 연습 모드 코드는 `Assets/1.Scripts/Tutorial/Legacy/SpinnerPractice/` 로 옮겨 보관하고 맵씬 연결을 끊었다.
> 경석 코드에 남은 훅 두 개는 **지우지 않았다**(동작 변화 0): `SpinnerBot` 의 `ISpinnerBotOverride`(구현체가 등록 해제된 `SpinnerBot_Tutorial` 뿐 → 맵씬 스피너봇은 늘 원래 경로) ·
> `MapContentSpawner.ServerMonsterPrefabOverride`(설치하던 `TutorialSpinnerSpawnOverride` 의 `RuntimeInitializeOnLoadMethod` 를 지움 → 늘 null). 지울지는 경석 판단.

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

**휴면은 여기 없다(할 일 15 I0 통합)** — 휴면은 모든 몬스터 공통 `MonsterBase.SetDormantServer` / `IsDormant` 하나다
(FSM 정지 + 무적 + 넉백/CC 무시, [handoff-tutorial.md](handoff-tutorial.md) §1·§2). 처음 레인에서 따로 두었던 스피너 휴면
(`SetDormant`·`startDormant`·`Dormant` 모드·휴면용 `SetServerLogicSuspended`·무한 SuperArmor)은 삭제했다.
전환 대기의 "인지·추격·공격 정지"만 공개 API `MonsterBase.SetServerLogicSuspended(bool)` 를 쓴다.

## 2. 연습 모드 본체 (`Assets/1.Scripts/Tutorial/Legacy/SpinnerPractice/`)

| 파일 | 역할 |
|---|---|
| `TutorialSpinnerPractice.cs` | NetworkBehaviour, `ISpinnerBotOverride` 구현. 튜토리얼 스피너봇 **루트**에 붙인다 |
| `SpinnerPracticeModeModel.cs` | 순수 C# 모드 상태 기계(연습/전환 대기/본 모드, 연습으로 시작) |
| `SpinnerPracticeWindowSchedule.cs` | 순수 C# 창 간격(기본 9초) |
| `SpinnerPracticeWindowTracker.cs` | 순수 C# 창 열림/닫힘 → 성공/놓침 · 연속 놓침 |
| `SpinnerPracticeArea.cs` | 순수 함수 스폰 반경 클램프(위치 · 돌진 직선 끝점) |
| `Editor/SpinnerPracticeModelTests.cs` | EditMode 22건. `Tools/Tests/맵씬 EditMode 테스트 실행`(`MapSceneEditModeTestRunner`)에 등록 |

모드별 동작:

| 모드 | FSM | 체력 | 넉백·CC | 공격 | 이동 범위 |
|---|---|---|---|---|---|
| (base 휴면 — 모드 아님) | 정지(`MonsterBase` 휴면) | 무적(base) | 무시(base) | 없음 | 클램프(현재 모드가 본 모드가 아니면) |
| 연습 `Practice` | 동작 | 잠금(피격 반응은 있음) | 받음 | 스핀만 · 창 매번 · 간격 9초 · 돌진 피해 0 + 밀쳐내기 | 클램프 |
| 전환 대기 `Transition` | 정지(기본 2초) | 잠금 | 받음 | 없음 | 클램프 |
| 본 모드 `Real` | 동작 | 감소 | 받음 | **원래 스피너봇**(채찍+스핀, 40%·쿨 6초, 피해 있음) | 없음(원래 리쉬) |

- 창 길이는 원래 값(`MonsterCounterWindow` 저작 1.5초 ÷ 공격속도) 그대로다. 성공 시 원래 동작(공격 취소 + 그로기 `groggyDuration` = 프리팹 1초).
- 간파 슬로우모션(`IInterruptSlowMotionTarget`)은 구현하지 않는다 → 스피너봇에 연결되지 않는다.
- 집계: 새 카운터 없음. `CombatStatsEvents.ServerCounterSucceeded` 는 SpinnerBot 원래 경로가 창 하나당 1회 낸다(연습 중에도).

## 3. 튜토리얼 본류가 연결하는 법

1. 튜토리얼 씬(또는 튜토리얼 전용 SpinnerBot 프리팹 Variant)의 스피너봇 루트에 `TutorialSpinnerPractice` 를 **미리** 붙인다.
   NetworkBehaviour 라 **런타임 AddComponent 금지**. `MonsterCounterWindow` 가 같은 루트에 있어야 한다(원래 프리팹에 있음).
2. 인스펙터: `windowInterval` 9 · `practiceRadius` 6m · `pushRadius` 2.5m · `pushStrength` 5 · `realModeDelay` 2.
   반경 중심은 서버 스폰 위치. 바꾸려면 `SetPracticeAnchor(Vector3)`.
3. 서버(호스트)에서 호출:
   - 휴면 걸기/풀기는 디렉터 `SetMonsterDormantServer(spinner, bool)`(= base `SetDormantServer`). 존 장부가 활성 때 건다.
   - `EnterPractice()` — 연습 시작(첫 창 즉시 가능 · 연속 놓침 0). **휴면은 풀지 않는다** — 먼저 휴면을 풀 것(휴면 중이면 경고).
   - `EnterRealMode()` — 2초 정지 후 본 모드. 중복 호출 무시. 휴면 중이면 전환 대기 타이머가 멈춘다.
4. 서버 이벤트(인스턴스):
   - `ServerWindowOpened(practice)`
   - `ServerWindowClosed(practice, bool succeeded)`
   - `ServerWindowMissed(practice, int consecutiveMisses)` — "연속 4번 놓치면 힌트" 는 여기 인자 ≥ 4 로.
   - `ServerModeChanged(practice, SpinnerPracticeMode)`
   - 서버 조회: `ConsecutiveMisses`.
5. 전 피어 조회: `Mode`(NetworkVariable 복제, 초기값 연습), `IsHealthLocked`(= 본 모드 전) — 체력 바 회색·잠금 표시는 안내 UI 몫.

## 4. 판단(위임에서 확정되지 않아 보수적으로 고른 것)

- **창 간격 9초 = 창 열림 → 다음 창 열림 가능 시각**(열림 기준). 첫 창은 연습 시작 직후 사거리에 들어오면 바로.
- **놓침 = 성공 없이 닫힌 모든 경우** — 만료뿐 아니라 공격 취소·`EnterRealMode` 정지로 닫힌 창도 놓침으로 센다.
  이벤트는 모드와 무관하게 낸다(본 모드 창도 알린다) — 걸러 쓰는 건 본류 몫.
- **밀쳐내기**: 원래 스피너봇 근접 판정은 `applyKnockback: 0` 이라 피해 0 만으로는 밀리지 않는다. 그래서 구현체가
  돌진 동안 몸 중심 `pushRadius` 안 플레이어를 **스핀 한 번에 한 사람당 1회** `Unit.Knockback` 으로 민다(판정 형상은 단순 원).
- **전환 대기 체력 잠금**: 전환 대기 2초 동안도 체력은 잠근다(본 모드 시작부터 감소).
- **클램프**: 연습·전환 대기에서 매 LateUpdate 수평 원 밖이면 원 위로 되돌린다(에이전트 켜짐 = `Warp`, 넉백 중 = transform).
  본 모드에서는 걸지 않는다(원래 스피너봇 = 원래 리쉬 18m). 출구 배리어가 반경 6m 밖이 아니면 인스펙터로 줄일 것.
- (I0) 스폰 후 디렉터가 휴면을 걸기 전 틈에는 연습 모드로 돈다 — 연습 공격은 피해 0·체력 잠금이라 안전하다고 보고 별도 대기 모드를 두지 않았다.

## 4-1. S9 연결 결과 (2026-10-11 · `feature/TutorialZone34`)

- §3-1 "미리 붙인다" = **튜토리얼 전용 Variant** `Assets/2.Prefabs/Monster/Tutorial/SpinnerBot_Tutorial.prefab` 로 했다. 원본 `SpinnerBot.prefab`·공용 `ZoneM_typeC` 는 무수정. 자세한 건 §6 · [tutorial-zone3.md](tutorial-zone3.md).
- `practiceRadius` 는 존3 흐름이 G6 배리어 위치로 다시 계산해 `SetPracticeRadius` 로 넣는다(StageTutorial 실측 4.4m).

## 4-2. v8 연계 연습(2026-10-12 · `feature/TutorialV8` · claude-alt) — 경석 공유

v8 시나리오 §3.5 가 존3 을 "간파 3회"에서 **[간파 → 취약 유지 → 우클릭 밀치기 → 유효 벽 충돌 → 원위치 복귀] 파티 합계 3회**로 바꿨다.

**SpinnerBot.cs 에 더한 것(+8 / −2줄)** — 주입 멤버 1개와 호출 1곳뿐. 구현체가 없으면 동작이 같다.

| 위치 | 내용 | 구현체 없을 때 |
|---|---|---|
| `ISpinnerBotOverride.NotifyInterruptHit(ulong attackerClientId)` (신규 멤버) | 간파(우클릭) 적중이 **창 밖**에서 들어왔다 — 튜토리얼이 취약 밀치기로 쓴다 | — |
| `TakeDamage` 간파 판정 | 창 밖 간파 적중(또는 창 컴포넌트 없음)이면 `Override?.NotifyInterruptHit(DamageAttackerClientId)` 후 return. 창 안 = 원래 성공 경로 그대로 | `Override` null → 이전과 같이 return |

**재사용 판단(결정)** — 23호(`TwentyThreeBoss`) 간파 게이지·제압·취약·벽 충돌은 **재사용하지 않았다**.
전부 23호 private 이고(벽 = 보스방 `InvisibleBoundaries` 사각형 수학, 밀치기 = 23호 `ReceiveAttack` 안 자체 이동, 이벤트 없음), 플레이어 우클릭 자체는 원래 몬스터를 밀지 않는다.
같은 **방식**(공격자 반대 방향 고정 거리·시간 밀치기 = 23호 `vulnerableKnockbackDistance` 3m 를 출발점으로, 벽 면 거리 수학 판정)만 튜토리얼 쪽 순수 모델로 새로 뒀다.
MonsterBase · TwentyThreeBoss · MonsterCounterWindow · 데이터는 무수정.

**튜토리얼 쪽(`Tutorial/SpinnerPractice/`)**
- `SpinnerComboModel.cs`(순수, EditMode `SpinnerComboModelTests`) — 단계 Ready → Stunned(원래 그로기) → Vulnerable(시간 종료 없음) → Recovering → (그 자리에서 Ready | Done) — 원위치 복귀(Returning) 단계는 사용자 변경(2026-10-13)으로 없앴다. 벽 충돌 = 몸 가장자리 ↔ 유효 벽 면 ≤ 0.4m,
  또는 NavMesh 가장자리에 막혔고 ≤ 1.6m(NavMesh 가 벽에서 에이전트 반경만큼 안쪽에서 끝나서). 벽 쪽으로 민 것만(법선 내적). 한 연계 1회.
- `TutorialSpinnerPractice` — 위 모델을 돌린다. 취약 유지·회복 = `MonsterBase.SetServerLogicSuspended(true)`(공개 API, 전환 대기와 같은 것) + `MonsterStatusEffect.ApplyStatus(SuperArmor, 0)`(외부 넉백 무시) ·
  밀치기 이동 = `NavMeshAgent.Move`(NavMesh·배리어 카브 밖으로 못 나감, 플레이어 콜라이더 무시). 본 전투(`EnterRealMode`) 때 모두 해제.
  새 복제 변수 3개(`_comboPhase`·`_wallPoint`·`_hasWall` — Variant NetworkBehaviour, hash 무관). 새 서버 이벤트 `ServerComboCounterAccepted`·`ServerComboEvent`. 새 서버 API `SetPracticeWall`·`RelocateAnchor`.
- 유효 벽·연습 위치는 존3 흐름이 존3 슬롯 사각형의 긴 변에서 정한다(`TutorialZone3Geometry.ChoosePracticeLayout` — 벽 면에서 몸 가장자리 4m, 존 끝에서 8m 안쪽). 튜닝은 인스펙터(D26).
- 간파 성공으로 쿨타임을 돌려주는 보정은 없다 — 게임 전체의 '간파 성공 시 우클릭 쿨 80% 감소'와 존3 연습의 '파티 우클릭 쿨 초기화' 둘 다 할 일 90 에서 제거. 연습 중에도 정상 우클릭 쿨.

## 5. 검증

- 컴파일 에러 0. `SpinnerPracticeModelTests` 는 I0 이후 `Tools/Tests/맵씬 EditMode 테스트 실행` 에서 돈다(22건).
- 🔴 **Play 검증 안 함**(사람이 Play/MPPM 으로 확인 필요): base 휴면 중 스피너봇 무적·정지 · 휴면 해제 후 `EnterPractice` · 9초 주기 창 · 놓침 시 피해 0 밀쳐내기 ·
  간파 성공 그로기 · 반경 클램프 · 본 모드 전환 후 원래 패턴 · 클라 창에서 `Mode` 복제.

## 6. 스폰 치환 훅 — `MapContentSpawner` (경석·맵 담당 공유, S9)

**왜**: `TutorialSpinnerPractice` 는 NetworkBehaviour 라 런타임에 붙일 수 없고, 존3 스피너봇은 공용 `ZoneM_typeC` 의 스폰 지점 Override(`SpinnerBot.prefab`)에서 나온다.
공용 존·원본 프리팹을 고치면 절차 맵 스피너봇에도 퍼지므로, 튜토리얼일 때만 스폰할 프리팹을 Variant 로 바꾼다.

| 파일 | 바뀐 것 |
|---|---|
| `Map/MapContentSpawner.cs` | `public static Func<GameObject, GameObject> ServerMonsterPrefabOverride`(기본 null) + `SpawnMonsterInstance` 첫 2줄(돌려준 값이 null 이 아니면 그 프리팹으로 Instantiate) |
| `Tutorial/SpinnerPractice/TutorialSpinnerSpawnOverride.cs`(신규) | 훅 설치(SubsystemRegistration). `MapSceneStageMarker.IsActive` ∧ SpinnerBot(연습 컴포넌트 없음)일 때만 → NetworkPrefabs 목록에서 `TutorialSpinnerPractice` 가 붙은 SpinnerBot(=Variant)을 찾아 돌려준다 |
| `Assets/DefaultNetworkPrefabs.asset` | Variant 1줄 등록(NGO 자동 등록) |

- 영향: 훅은 마커가 없으면 늘 null 을 돌려준다 → 절차 맵·Debug 씬·다른 몬스터는 그대로. 두 스폰 경로(마커·`ZoneMonsterSpawnSet`)가 모두 `SpawnMonsterInstance` 를 지나 같이 적용된다.
- Variant = SpinnerBot.prefab + `TutorialSpinnerPractice` + 머리 위 체력바(`UI/OverheadHealthBar.prefab`, 로컬 y 0.5) + `TutorialSpinnerHealthBarRule`. NetworkObject hash 1723163300(오버라이드 저장).
  원본 SpinnerBot 을 고치면(경석) Variant 도 따라간다 — 원본 루트에 같은 이름 컴포넌트를 추가하는 변경만 피하면 된다.
