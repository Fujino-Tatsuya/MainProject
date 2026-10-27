# handoff — 튜토리얼 보스방 구간 수정 (2026-10-22 · 경석(Claude))

> 튜토리얼·23호 전수조사(감사 목록 = 경석 로컬 세션 노트 `audit-tutorial-boss-1022.md`)에서 나온 **보스방·몬스터 쪽** 결함을 경석 지시로 바로 고쳤다.
> 튜토리얼 파일(은희 영역 혼재)을 건드린 것은 아래 3곳(+테스트)뿐이다. 안전 지점(`MapSceneSafePoints`·`MapSceneDeathRules`)은 경석 지시로 10-23 전부 원복 — 손대지 않았다. **Play 미확인**(컴파일만).

## 튜토리얼 파일 변경

| 파일 | 무엇을 | 왜 |
|---|---|---|
| `Map/MapScene/Flow/MapSceneBossApproachView.cs` `Apply` | Finished 가 아니면 `_finishedCleared = false` | 전멸 재도전(Finished → BossEnter 되감기) 뒤 다시 도착해도 안내를 안 비워서 "보스방으로" 목표·미니맵 펄스가 보스전 내내 남았다 |
| `Map/MapScene/Flow/MapSceneBossApproachRules.cs` | `MapSceneBossApproachModel.NotifyBossRetry()` 추가(도착 래치·팁 예약 해제) | 재도전 때 패드 카운트다운 자막(`TUT_B_PAD_COUNT`)·도착 처리·23호 정면 팁이 다시 돈다 |
| `Map/MapScene/Flow/MapSceneBossApproachGuide.cs` `Update` | Finished 가 아니면 `NotifyBossRetry()` 호출(서버) | 위 래치 해제 지점. 첫 판에서는 도착 전이라 no-op |
| `Editor/MapSceneBossApproachRulesTests.cs` | `BossRetry_ReleasesArrival_PadAndTipAgain` | 모델 회귀 |

## 보스방 매니저 변경(경석 영역 — 참고)

- `BossTeleportManager.TeleportAfter` — `WaitForSeconds` → `ServerTime` 폴링(표시와 같은 시계). 간파 슬로우 중 만료하면 숫자 0 뒤에도 안 떠나던 것.
- `ReviveSoulsForForcedStart` — 튜토리얼 G11 전이면 `consumeLife: false`(S11 "도착 전 사망 차감 없음").

## Play 확인 항목

1. 보스전 → 공용 목숨 0 전멸 → 복도4 리스폰 → 패드 → 재도착: 목표·미니맵 펄스가 지워지는가, 패드 카운트 자막이 다시 나오는가.
2. 타이머 만료 강제 이동 중 간파 슬로우: 숫자 0 과 이동이 맞는가.
