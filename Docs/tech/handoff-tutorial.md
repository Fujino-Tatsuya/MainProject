# Handoff — 튜토리얼 스테이지가 몬스터·보스 타이머·보스 진입 코드에 넣은 훅 (경석 공유용)

> **🟪 할 일 107 (2026-10-27)** — 맵씬 튜토리얼 정리. 경석 코드에 넣은 훅은 **동작 그대로**이고 이름만 바뀌었다: `TutorialDirector` → `MapSceneDirector`, `TutorialBossPath` → `MapSceneBossPath`, `TutorialStageMarker` → `MapSceneStageMarker`,
> `TutorialMonsterZones`/`Ledger` → `MapSceneMonsterZones`/`Ledger`(`MonsterBase.SetDormantServer` 휴면은 계속 쓴다). 스피너봇 연습(I0 통합 대상)은 Legacy — [handoff-tutorial-spinner.md](handoff-tutorial-spinner.md) 맨 위.
> 존2·존3 기상은 이제 교육 흐름 대신 `MapSceneZoneFlow` 가 부른다. 전체: [tutorial-director.md](tutorial-director.md) 맨 위.

> 할 일 15 / `feature/TutorialStage`. 몬스터는 경석 영역이라 **최소 훅**만 넣고 여기에 무엇을·왜·어디를 바꿨는지 남긴다.
> 튜토리얼 쪽 설계: [tutorial-director.md](tutorial-director.md) §8 · 시나리오 [../design/tutorial-scenario.md](../design/tutorial-scenario.md) §2.2·§4.
> 스피너봇 연습/본 모드는 별도 문서 `handoff-tutorial-spinner.md` — 휴면 통합 결과는 §2.

## 1. S4 — 몬스터 휴면 (`MonsterBase`)

**왜**: 튜토리얼은 몬스터를 맵 생성 때 전부 스폰한다(기존 그대로). 그런데 존2~4 몬스터는 "자기 차례"(존 진입·타이머 시작·다리 개통)까지
가만히 있어야 하고, 배리어 너머 원거리(거너 평타 14m·Q 16m·대상 지정 R, 팔라딘 R)에 맞아도 피해·넉백이 없어야 한다(§2.2).

**무엇을** — `Assets/1.Scripts/Monster/MonsterBase.cs`

| 자리 | 바뀐 것 |
|---|---|
| 필드 `_dormant` | 서버 전용 플래그. **기본 false**, 복제 안 함 |
| `public bool IsDormant` | 읽기(서버에서만 의미 있음 — 클라는 항상 false) |
| `public void SetDormantServer(bool)` | 켜기/끄기. 켤 때 진행 중 이동·공격·넉백을 정리하고 Idle(넉백 중이면 꺼 둔 에이전트를 다시 켬)·타깃 해제·간파 표시 끔 — `SetServerLogicSuspended(true)` 의 정리와 같은 순서 |
| `Update` | `if (_serverLogicSuspended \|\| _dormant)` — FSM(인지·추격·공격·`HandleKnockback` 밀기/Warp) 정지, 이동 블렌드 0 |
| `ReceiveAttack` 첫 줄 | `if (_dormant) return false;` — 죽은 대상과 같은 "거절". 피해·`PlayHitVFXRpc`·넉백·경직·인터럽트 누적·파생 `TakeDamage`(스피너봇/중간보스 간파 소비)·피해 숫자(`ClientDamageDealtClientRpc`)가 모두 빠진다 |
| `CanApplyHealthDamage` override (신규) | `!_dormant && base` — `ReceiveAttack` 을 안 타는 직접·비율 피해, `DevMonsterKill`(F9)의 `TakeDamage` 직접 호출도 막는다 |
| `OnKnockback` override (신규) | 휴면이면 무시 — `Unit.Knockback` → `LinearKnockback` 임펄스 경로 |

**영향 범위**: 호출처는 `MapSceneMonsterZones`/`MapSceneDirector` 뿐이고(스피너봇 연습 컴포넌트는 `IsDormant` 를 읽기만 한다), 디렉터는 `MapSceneStageMarker` 가 있는 스테이지(튜토리얼)에서만 켜진다.
절차 맵·Debug 씬·다른 스테이지의 몬스터는 `_dormant` 가 영원히 false 라 동작이 같다(추가된 분기 3개가 모두 false 로 지나간다).
`SetServerLogicSuspended`(보스 연출 정지 — 무적 아님)와는 플래그가 따로라 서로를 풀지 않는다.

**한계**
- 휴면은 서버에만 있다. 클라 쪽 타깃 선택(대상 지정 R 등)은 휴면 몬스터도 고를 수 있고, 맞으면 아무 일도 없다(피해·숫자·피격 이펙트 없음).
  빔·투사체 자체의 착탄 이펙트(공격 쪽 연출)는 그대로 보일 수 있다 — Play 에서 확인할 것.
- `ReceiveAttack` 이 false 를 돌려주므로 "죽은 대상이면 관통"하는 공격이 있다면 휴면 몬스터도 관통한다(의도 — 무적 대상이 탄을 먹지 않음).
- 상태이상: 몬스터 CC 는 전부 `ReceiveAttack`(AttackInfo 의 경직·넉백·인터럽트)과 `Unit.Knockback` 으로만 들어온다(`MonsterStatusEffect.ApplyStatus` 는 몬스터 자기 코드만 부른다) → 둘 다 막힘. 휴면 진입 때 기존 상태(스폰 슈퍼아머 등)는 지우지 않는다.

## 2. 스피너봇 연습 모드와의 휴면 통합 (할 일 15 I0 — 완료)

- **휴면 = `MonsterBase.SetDormantServer(bool)` / `MonsterBase.IsDormant` 하나.** 모든 몬스터 공통(존 장부가 존3 스피너봇에도 건다).
- 스피너봇 연습 컴포넌트(`Tutorial/SpinnerPractice/TutorialSpinnerPractice`)의 `SetDormant`·`startDormant`·모델의 휴면 깃발·`SpinnerPracticeMode.Dormant` 는 **삭제**했다.
  남은 모드 = 연습 / 전환 대기 / 본 모드(연습으로 시작). 스피너가 휴면용으로 걸던 `SetServerLogicSuspended(true)`·무한 SuperArmor 도 삭제 —
  휴면 중 정지·무적·넉백/CC 무시는 base 휴면이 한다. `SetServerLogicSuspended` 는 이제 **전환 대기(기본 2초)에만** 쓴다(base 휴면과 플래그가 따로라 겹쳐도 서로를 풀지 않는다).
- base 휴면 중에는 컴포넌트가 전환 대기 타이머를 흘리지 않는다(기존 "휴면 중 타이머 정지" 유지). `EnterPractice`/`EnterRealMode` 는 휴면을 **풀지 않는다** — 휴면 중 부르면 경고 1줄.
- 존3 순서(S9): 활성 때 존 장부가 스피너봇을 휴면 → 존3 첫 진입 때 `SetMonsterDormantServer(spinner, false)` 후 `EnterPractice` → G5 → `EnterRealMode` → 처치 = G6(장부가 자동).
  연습 모드의 "죽지 않음"(체력 피해 0)은 스피너 컴포넌트 몫이다(휴면 무적과 별개 — 연습 모드는 간파가 맞아야 하므로 휴면을 풀고 들어간다).
- 스피너봇 쪽 상세: [handoff-tutorial-spinner.md](handoff-tutorial-spinner.md).

## 3. S5 — 보스 타이머 · 보스 진입 패드 · 타이머 HUD

**왜**: 튜토리얼은 존4 설정 자막 뒤(G8)에 타이머가 처음 차오르고(D1·D6 420초), 그 전엔 HUD 도 안 보이며(§4),
보스 진입 패드는 복도4 특성 전원 선택(G10) 전에는 밟아도 카운트다운이 없어야 한다(D3·D17). **Stage1 = 모든 판**이라 이 규칙들이
절차 맵에 퍼지지 않게, 경석 코드는 튜토리얼 판별을 **`MapSceneBossPath` 한 곳**(→ `MapSceneStageMarker.IsActive` + 씬에 `MapSceneDirector`)에만 묻는다.
비튜토리얼에서는 그 질문이 모두 "기존대로"를 돌려준다(자동 시작 true · HUD 숨김 false · 패드 활성 true).

| 파일 | 바뀐 것 |
|---|---|
| `Map/BossTimerManager.cs` | `HandleMapGenerated`: `!MapSceneBossPath.ShouldAutoStartTimer` 면 보류 없이 return(Idle 유지). `ApplyPendingStart` 에도 같은 이중 안전 1줄 |
| | **`public bool StartTimerServer(float limitSecondsOverride = 0f)`** (서버, Idle 일 때만) — 지금부터 시작. 덮어쓴 제한시간은 `_limitOverride`(NetworkVariable&lt;float&gt;, 0 = 인스펙터)로 복제 |
| | `LimitSeconds` = 덮어쓰기 &gt; 0 ? 그 값 : 인스펙터 값. `Fill01`·`StartTimer` 가 이걸 쓴다(클라 게이지도 같은 길이). 자동 시작은 덮어쓰지 않아 기존 값 그대로 |
| `UI/Combat/BossTimerHUD.cs` | `available` 에 `&& !MapSceneBossPath.ShouldHideTimerHud(timer.State)` — 튜토리얼 · Idle · 디렉터 '타이머 HUD 표시' 꺼짐일 때만 알파 0 |
| | (할 일 166, 튜토리얼 무관) 해골 아이콘(`BossIcon`) 점멸 — Running 동안만 매 프레임 `Time.deltaTime` 으로 위상 누적, 주기 = Lerp(시작 10초, 최소 0.2초, EaseInExpo(1 − 남은/제한)), 알파 1↔최저 0.25 를 0.5−0.5cos 로 오감(`Image.color.a` 만). Running 아님·위젯 숨김이면 알파 1·위상 0. 칸 비면 자식 `BossIcon` 이름으로 찾음. 테스트 `BossTimerIconBlinkTests` |
| `Map/BossEnterTrigger.cs` | `Push` 가 `PadOccupied(_inside.Count > 0, MapSceneBossPath.IsBossPadEnabled)` 를 보낸다(잠긴 패드 = 비어 있음). `Update` 가 잠금 변화(G10)를 보면 다시 `Push` — 이미 서 있던 사람으로 카운트다운 시작 |
| `Map/BossEnterZoneVisual.cs` | 잠긴 동안 범위 테두리(LineRenderer)를 끈다 — 디렉터 복제 게이트라 모든 피어·늦은 클라 같은 값 |

**안 바꾼 것(계약 유지)**: 시간 도메인(`TimerNow` = GameNow/ServerTime)·만료 판정·만료 사실 유지·0.5초 재시도·`ForceStartEncounter`(3초 경고 →
Soul 강제 부활 → 전원 이동)·도착 시 `Stopped` + 게이지 0·`BossTeleportManager` 전부(패드 크기·카운트다운·ACK·페이드).
- 🔴 **만료 강제 이동은 튜토리얼 규칙을 하나도 보지 않는다** — 패드 잠금은 패드 점유 보고만 끄므로, 잠긴 동안 패드는 '진행 중'(카운트다운)을 만들 수 없고
  `ForceStartEncounter` 를 미루게 할 수도 없다. 배리어(G9·G10)는 순간이동이라 무관. 테스트 `MapSceneBossPathRulesTests.Expiry_*`.
- 스폰 순서: 판별은 씬 로드 때(`MapSceneStageMarker.OnEnable`·`MapSceneDirector.Awake`) 이미 확정이라 `OnGenerated`·`OnNetworkSpawn` 어느 쪽이 먼저든
  타이머는 한 프레임도 자동 시작하지 않는다(디렉터의 NetworkVariable 을 기다리지 않는다). 디렉터 활성 때 타이머가 Idle 이 아니면 에러 로그.
- 엘리베이터 문: 경석 코드 아님 — 튜토리얼 쪽 `MapSceneElevatorDoor` 가 `Animator.Play("Open")`. 컨트롤러·존 프리팹 무수정.

**누가 부르나**: `MapSceneDirector` 가 G8 이 열릴 때(어떤 경로든) `StartTimerServer(420)`. 다른 호출처 없음.

## 4. 그 밖에 몬스터 쪽은 안 바꿨다
- `MonsterDeathEvents.ServerMonsterDied` 는 구독만 한다(세션 통계와 같은 채널).
- 존 프리팹·원본 몬스터 프리팹·데이터 무수정. 존 배정은 스폰 뒤 위치로 튜토리얼 쪽이 계산한다.
- (S9 정정) `MapContentSpawner` 에는 **스폰 치환 훅** `ServerMonsterPrefabOverride`(기본 null)를 넣었다 — 튜토리얼일 때만 존3 SpinnerBot → `SpinnerBot_Tutorial` Variant. [handoff-tutorial-spinner.md](handoff-tutorial-spinner.md) §6.
- (S10) `ZoneBridgeGateManager` 에 읽기 전용 정적 이벤트 2개(`ServerPanelActivated`·`ServerBridgeOpened`, 구독자 없으면 무동작) — [tutorial-zone4.md](tutorial-zone4.md) §4. 다리 재베이크 끊김·고정 조각 에러 로그 권고는 같은 문서 §6.
