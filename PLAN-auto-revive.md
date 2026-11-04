# PLAN — 보스방 자동부활 (2026-11-02, 경석)

> 상태: **승인됨(2026-11-02, 경석) · 구현됨(같은 날) — EditMode 통과, Play 확인 전.**
> 테스트는 계획한 8건 중 회귀 3건을 기존 테스트로 대신했다(기존 31건 그대로 통과) — 신규 5건(`MapSceneDeathRulesTests` "보스방" 묶음).
> 관련: [Docs/tech/party-lives-respawn.md](Docs/tech/party-lives-respawn.md)(공용 목숨·전멸 리스폰) ·
> [Docs/tech/tutorial-director.md §11](Docs/tech/tutorial-director.md)(사망 규칙 S11) · [Docs/tech/debug-hotkeys.md](Docs/tech/debug-hotkeys.md)

## 0. 출발점 — 처음 요청과 grill 뒤 범위

처음 요청: ① 목숨 소진 시 보스방 입구로 한 번에 넘어가기(Alt+9 처럼) ② 자동부활 모드(목숨 무한·키 입력 없음·보스방 사망만 차감).

코드 확인 결과(2026-11-02):

| 상황 | 현재 동작 |
|---|---|
| 보스방 도착(G11) **전** 사망 | **이미** 3초 뒤 키 없이 자동 부활 · 무적 2초 · 구간 안전 지점 · 목숨 차감 없음 (`MapSceneDeathRules`) |
| 보스방 도착 **뒤** 사망 | Soul(유령) — **자동 부활 없음**. F10 디버그 키(목숨 1 차감)로만 부활. 목숨 0 이면 PermanentDead(관전) |
| 목숨 0 + 전멸 | **이미** 복도4 리스폰 + 보스 리셋 + 목숨 5 재충전 + 특성 재선택 (`PartyWipeRespawner`) |
| Alt+9 | 호스트·튜토리얼 중 · 남은 게이트를 정식 경로로 열고 패드로 보스방까지. `DEV_HOTKEYS` 로 지금 Standalone 빌드에 들어 있다 |

🔴 발견: 보스방에서 **목숨이 남은 채 전원이 Soul** 이 되면 `PartyLivesRules.IsStillInPlay`(Soul + 목숨 > 0 = 아직 플레이 중)라 전멸로 판정되지 않는다 →
F10 을 누르지 않으면 **아무도 안 살아나고 결과·리스폰도 없는 정지 상태**(코드 해석, Play 미확인). 이번 작업이 이 구멍을 같이 막는다.

**사용자 결정(grill)**

| # | 질문 | 결정 |
|---|---|---|
| Q1 | 기능 ① | **안 만든다.** 목숨 소진은 보스방에서만 일어나는 구조라 의미 없음. Alt+9 는 지금 빌드에 있으니 그대로 둔다(이번 범위에서 코드 변경 없음) |
| Q2 | 보스방 목숨 0 이후 | **기존 규칙** — 0 이후 사망은 관전 → 전원 쓰러지면 복도4 리스폰·보스 리셋·목숨 재충전 |
| Q3 | 켜는 곳 | **스위치 없음 — 자동부활은 상시.** Alt+9 는 호스트가 누를 때만 동작(이미 그렇다) |
| Q4 | 보스방 부활 위치 | **그 자리** |
| Q5 | 대기·무적 | **존과 같게 3초 · 2초**(`MapSceneDeathRules.reviveDelaySeconds`·`reviveInvulnerabilitySeconds` 그대로) |
| Q6 | F10 | **그대로 둠**(목숨 0 이면 어차피 거부) |

## 1. 목표 · 완료 조건

- 맵씬 튜토리얼 판에서 **보스방 도착(G11) 뒤 사망 → 3초 뒤 그 자리에서 자동 부활 + 무적 2초 + 공용 목숨 1 차감.** 키 입력 없음.
- 목숨이 0 이면 지금 규칙 그대로(사망 목적지 PermanentDead → 전원이면 전멸 리스폰). 차감 부활 시점에 목숨이 이미 0 이면 거부 → Soul 로 남고 전멸 판정이 '탈락'으로 센다(기존).
- G11 전 규칙·전멸 리스폰·보스 강제 개시 부활·F10·Alt+9 는 동작 변화 없음.
- 완료 = 코드 + EditMode 테스트 통과 + 문서 갱신 + Play 확인(아래 §6).

## 2. 범위 밖

- 기능 ①(목숨 소진 → 입구 이동), 켜고 끄는 스위치·로비 옵션·데모 설정 칸.
- **절차 맵(비튜토리얼)** — `MapSceneDeathRules` 가 튜토리얼 판에만 붙는다. 절차 맵 보스방은 지금처럼 F10 만. 지금 실제 흐름이 튜토리얼 맵씬이라 제외(필요하면 후속).
- Alt+9 를 `DEV_HOTKEYS` 밖으로 빼는 것(정식 릴리스 때 다시 정한다).
- 목숨 표시 UI.

## 3. 접근

변경 파일 2개 + 테스트 1개 + 문서. 네트워크·RPC·복제값 추가 없음.

### 3.1 `MapSceneDeathRulesModel`(순수) — "그 자리" 와 "즉시" 를 분리

지금 `Pending.InPlace` 는 두 뜻을 함께 갖는다: **그 자리에서**(위치) + **대기 없이 즉시**(`Evaluate` 의 `if (!p.InPlace && now < p.DueAt)`).
보스방 부활은 "그 자리 + 3초 대기" 라 둘을 쪼갠다.

- `Pending.Immediate` 추가. `Evaluate` 의 대기 검사를 `!p.Immediate` 로 바꾼다.
- 기존 경로는 둘을 같이 세운다 — `RecordDeath`(`InPlace = Immediate = IsMoveInProgress`) · `ConvertAllToInPlace`(둘 다 true). → 기존 동작·테스트 무변화.
- 새 `RecordBossRoomDeath(clientId, now)` — `Free = false`, `InPlace = true`, `Immediate = false`, `DueAt = now + delay`.
- `MapSceneReviveOrder` 는 그대로(`InPlace` 만 실행부가 쓴다). `RevivesAtBossEnterPoint(order, finished)` 는 finished 면 false — 보스방 주문은 고정 지점으로 안 간다.

### 3.2 `MapSceneDeathRules`(서버) — 도착 뒤에도 사망을 받는다

- `HandleServerLifeStateChanged`: 지금은 `RulesActive`(= `!_finished` …)가 아니면 무시한다.
  → `_finished` 이고 튜토리얼 활성(`_director.IsTutorialActive`)이면 `RecordBossRoomDeath` 로 기록(로그: "보스방 — 3초 뒤 그 자리, 목숨 1 차감").
- `ServerDeathKeepsSoul`(`KeepsSoul` → `IsPenaltyFree`)은 보스방 기록이 `Free = false` 라 false — 사망 목적지는 공용 목숨으로 정해진다(0 이면 PermanentDead → `Evaluate` 가 Dropped). **변경 없음.**
- `Revive` 실행부: `order.InPlace` → 그 자리, `TryCompleteReviveOnServer(consumeLife: true)` → 거부면 기존 경고 로그 후 Soul. **변경 없음.**
- `Finish()` 의 `ConvertAllToInPlace`(도착 순간 남아 있던 G11 전 대기 = 차감 없음·즉시)는 그대로.
- 전멸 리스폰 `ResumeForBossRetry`(대기 비움·`_finished = false`)도 그대로 — 재도착 전엔 G11 전 규칙, 재도착 뒤 다시 보스방 규칙.
- 클래스 상단 요약 주석의 "보스방 도착 뒤 사망은 일반 규칙(자동 부활 없음)" 을 고친다.

### 3.3 네트워크 권한

전부 서버. 기존 경로 그대로 — `PlayerLifeCycleController.ServerLifeStateChanged` 구독 → 서버 `ServerTime` 으로 대기 → `PlayerReviveController.TryCompleteReviveOnServer`(공용 목숨 `NetworkVariable` 차감) → `PlayerLandingProtection.BeginProtection`.
클라는 기존 생명주기·목숨 복제를 받기만 한다. 새 RPC·NetworkVariable 없음.

## 4. 엣지

| 경우 | 처리 |
|---|---|
| 목숨 1 에서 둘이 동시에 사망 | 둘 다 Soul(사망 시점 목숨 > 0). 먼저 부활한 쪽이 0 으로 만들고 다른 쪽은 거부 → Soul 유지 → 전멸 판정 '탈락'(기존 갇힌 Soul 규칙, R1) |
| 목숨 남은 채 전원 사망 | 3초 뒤 전원 그 자리 부활(지금의 정지 상태 해소) |
| 보스 연출 잠금 중 사망(등장·잡기 등 `PlayerEncounterLock.IsCinematicLocked`) | 기존 `BlockedByCinematic` — 경고 1회 후 잠금이 풀리면 부활 |
| 대기 중 F10 / 강제 개시 부활로 이미 Alive | `Evaluate` 가 Dropped(기존) — 이중 차감 없음 |
| 대기 중 이탈 | `HandleClientDisconnected` → Forget(기존) |
| 재합류 생명 복원(Alive → Soul, 할 일 48) | 보스방이면 같은 규칙으로 3초 뒤 차감 부활 — 지금(G11 전)도 같은 훅으로 부활시킨다 |
| 대기 중 보스 격파(결과 전환 대기) | 그대로 부활(무해 — 결과 화면으로 넘어간다). 막지 않는다 |
| 대기 중 전멸 리스폰 | `ResumeForBossRetry` 가 대기를 비운다(기존) |

## 5. 리스크

| # | 리스크 | 대응 |
|---|---|---|
| R1 | `InPlace` 의미 분리로 G11 전 즉시 부활(이동 카운트다운)이 깨짐 | 기존 경로는 `Immediate = InPlace` 로 같이 세운다 + 기존 31건 테스트 그대로 통과 확인 |
| R2 | 그 자리 부활 → 장판 위 연속 사망 | 사용자 결정(그 자리). 무적 2초가 유일한 보호 — Play 에서 체감 확인, 문제면 후속으로 보스방 안전 지점 |
| R3 | 보스방 사망이 이제 목숨을 실제로 소모 → 전멸 리스폰이 전보다 자주 남 | 의도된 변화(지금은 F10 을 안 누르면 소모 자체가 없었다) |
| R4 | `MapSceneDeathRules` 는 다른 세션도 만지는 파일 | 구현 시작 때 `git status`·`git diff` 로 미커밋 변경 확인, CONTEXT.md 작업 세션에 수정 파일 기재 |

## 6. 검증

**EditMode(`MapSceneDeathRulesTests`, 튜토리얼 러너 — 기존 31건 + 신규)**

- `BossRoomDeath_WaitsDelay_ThenRevivesInPlace_ConsumingLife` — 3초 전 Waiting, 뒤 Revive · `InPlace` true · `ConsumeLife` true
- `BossRoomDeath_IsNotPenaltyFree` — `IsPenaltyFree` false(사망 목적지가 목숨을 본다)
- `BossRoomDeath_PermanentDead_IsDropped` — 목숨 0 → 관전이면 기록 버림
- `BossRoomDeath_WaitsForSoul` / `_BlockedByCinematic_ThenRevives`
- `MoveInProgressDeath_StillImmediate` — 기존 즉시 부활 무변화(회귀)
- `ConvertAllToInPlace_MakesImmediate` — 도착 순간 남은 G11 전 대기는 즉시(회귀)
- `BossRoomOrder_DoesNotUseBossEnterPoint` — `RevivesAtBossEnterPoint(order, finished: true)` false

**Play(사람 — 솔로 + MPPM 2인)**

1. Alt+9 로 보스방 → 사망 → 3초 뒤 **그 자리** 부활·무적 2초·목숨 5 → 4(로그 `[Tutorial] … 자동 부활 — 그 자리, 목숨 차감 1개`).
2. 목숨 0 까지 소모 → 다음 사망 관전 → 전원 쓰러지면 복도4 리스폰·목숨 5 재충전.
3. 리스폰 뒤 보스방 재도착 전 사망 = 차감 없이 구간 부활, 재도착 뒤 = 1번과 같음.
4. MPPM: 둘이 동시에 사망(목숨 남음) → 둘 다 부활. 목숨 1 에서 동시 사망 → 한 명만 부활, 남은 한 명 Soul 이 전멸 판정에 '탈락'.
5. 보스 등장 연출 중 사망(가능하면) → 연출 끝난 뒤 부활.
6. G11 전 존·패드 카운트다운 중 사망 동작이 전과 같은지(회귀).

## 7. 문서 갱신(구현과 함께)

- `Docs/tech/tutorial-director.md §11` — 보스방 도착 뒤 사망 = 3초 뒤 그 자리 자동부활 + 1 차감.
- `Docs/tech/party-lives-respawn.md §2` 상태 전이 · §7 테스트 범위 — 보스방 자동 부활 추가, 정지 상태(목숨 남은 전원 Soul) 해소.
- `Docs/tech/tutorial-qa.md` — Play 항목 1~4.
- `CONTEXT.md` — 작업 세션·인수인계(구현 세션에서).
