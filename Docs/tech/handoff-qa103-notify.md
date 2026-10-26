# handoff — 할 일 103 C5: qa 브랜치 전용 게임 코드 알림 (팔라딘 방어 · 23호 기믹 결과)

> 🔴 **qa/BossBalance103 전용이다. development 로 병합하지 않는다.** (qa/* 브랜치는 development 병합 금지 규칙)
> 23호 = 경석 영역, 팔라딘 E = 은희 영역. 이 문서는 "qa 브랜치의 게임 코드에 무엇이 들어가 있나" 를 알리는 공유용이다.

## 원칙

- 게임 로직·수치·타이밍 변경 **0**. 정적 이벤트 선언 + 결과가 이미 정해진 지점에 `?.Invoke` 호출 1줄씩만.
- 구독자는 QA 계측기 `QABoss103Recorder`(에디터 전용, 호스트) 하나뿐. 구독자가 없으면 아무 일도 안 한다.
- 추가한 줄마다 주석 `// QA103 (qa 브랜치 전용 알림)` — 이 문자열로 grep 하면 전부 나온다.
- 기존 `FightLog(...)` 문자열·`Debug.Log` 는 그대로다.

## 1. 팔라딘 방어 — `Assets/1.Scripts/Player/Skill/FirstMeleeSubSkill.cs`

| 위치 | 추가 |
|---|---|
| 클래스 끝 | `public static event Action<Player, GuardVerdict> QA103GuardAbsorbed` |
| `ServerTryAbsorbAttack` — `return verdict != GuardVerdict.Pass;` 바로 위 | `if (verdict != Pass) QA103GuardAbsorbed?.Invoke(owner, verdict);` |

- `BlockAndCounter` = 첫 방어 성공(반격 진입), `Ignore` = 반격 단계 중 흡수. 둘 다 피해를 흡수한 경우다.
- CSV(run_chars, 팔라딘 행): `paladin_guard_success` = 흡수 전부, `paladin_guard_counter` = BlockAndCounter, `paladin_guard_ignore` = Ignore.
  계측기가 구독하면 0 부터 센다(구독 전 C4 판은 빈칸).

## 2. 23호 기믹 결과 — `TwentyThreeBoss.cs` · `TwentyThreeBoss.RaidRelay.cs`

선언(`TwentyThreeBoss.cs`, `FightLog` 바로 위):
`public static event Action<TwentyThreeBoss, string gimmick, string result, int value> QA103GimmickResult`

| 파일 · 메서드 | 옆 FightLog | gimmick | result | value |
|---|---|---|---|---|
| `TwentyThreeBoss.cs` · `TickCharge` | ② 전력 공급 성공 | 전력 공급 | success | 0 |
| 〃 | 송전기 성공 | 송전기 | success | 0 |
| 〃 | ② 전력 공급 실패 → 방전 | 전력 공급 | fail | 부순 탑 칸 수(방전 안전 칸, `_safeTileScratch.Count`) |
| 〃 | 송전기 실패 → 레이지 | 송전기 | fail | 0 |
| `TwentyThreeBoss.cs` · `EndSelfDestruct` | ④ 폐기 처분 저지 | 자폭 저지 | success | 0 |
| 〃 | ④ 폐기 처분 실패 → 대폭발 | 자폭 저지 | fail | 0 |
| 〃 (`_zeroGuard = Exploding` 직후) | (없음 — 0줄 실패) | 0줄 자폭 | fail | 0 |
| `TwentyThreeBoss.cs` · `KillAfterZeroSelfDestruct` | 0줄 자폭 저지 — 처치 | 0줄 자폭 | success | 0 |
| `TwentyThreeBoss.cs` · `AcquireGrab`(헛잡기) | ③ 새 가족 성공(헛잡기) | 새 가족 | success | 0 |
| `TwentyThreeBoss.cs` · `FinishChain` | ③ 새 가족 실패 | 새 가족 | fail | 0 |
| `TwentyThreeBoss.cs` · `EnterCounterSuccess` | ③ 새 가족 성공(간파) | 새 가족 | success | 1(간파 구출) |
| `RaidRelay.cs` · `BeginRelayPrepare` | 간파 릴레이 — 제한 시간 초과 | 간파 릴레이 | timeout | 밀어낸 횟수 |
| `RaidRelay.cs` · `EndRelayPush` | 간파 릴레이 성공 | 간파 릴레이 | success | 밀어낸 횟수 |
| `RaidRelay.cs` · `BeginRelayFailDash` | 간파 릴레이 실패 — 돌진 | 간파 릴레이 | fail | 밀어낸 횟수 |
| `RaidRelay.cs` · `TickChain`(ChainDash) | 연쇄 간파 N회 실패 | 연쇄 간파 N회 | fail | N |
| `RaidRelay.cs` · `OnChainCounter` | 연쇄 간파 N회 성공 | 연쇄 간파 N회 | success | N |
| `RaidRelay.cs` · `ResolveChain` | 연쇄 간파 결과 k/n → o | 연쇄 간파 | Full/Partial/None | 성공 수 k |
| `RaidRelay.cs` · `TickPushNorth`(PnCharge) | 북진 N회차 — 탑 파괴 | 북진 N회차 | success | N |
| 〃 | 북진 N회차 실패 | 북진 N회차 | fail | N |
| `RaidRelay.cs` · `OnPushNorthCounter` | 북진 N회차 — 간파 | 북진 N회차 간파 | success | N |

- 드론 유도(레이드 ②)의 결과는 따로 정해지는 지점이 없다 — ② 전력 공급 성공/실패 자체가 드론 유도 결과다(실패 시 value = 부순 탑 칸 수).
- 기믹 이름은 `QABoss103FightLog`(로그 파싱)와 같은 말을 쓴다. 0줄 실패는 기존 로그에 별도 줄이 없어 알림에만 있다.

## 3. 계측기 쪽(QA 코드)

- `QABoss103Recorder` 가 두 이벤트를 구독. 기믹 알림은 `run_gimmicks.csv` 에 `source=event` 행, `fight_t` = Combat 진입부터의 서버 시각 차(근사).
- 중복 제거 `QABoss103GimmickNotify.Merge`: 알림 1건마다 같은 gimmick·같은 result 의 로그 행 1건을 지운다.
  남는 로그 행(결과 없이 끝난 `ended`, 판 종료 `timeout`, 알림이 못 잡은 것)은 `source=log` 보조로 남는다.
- 순수 부분 EditMode: `QABoss103MetricsTests`(C5 절).

## development 로 가져가려면

이 알림은 QA 측정 전용이라 development 에 넣지 않는다. 정식으로 필요해지면 게임 쪽 공개 이벤트(예: `CombatStatsEvents`)로
담당자(23호 = 경석, 팔라딘 = 은희)와 합의해 새로 설계한다.
