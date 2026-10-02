# PLAN-boss-counter-vulnerable.md — 간파 · 취약 · 제압 (23호 전용) + 간파 표시 타이밍 정리

> 상태: **승인 (팀장 09-28) · 구현 중** · 2026-09-28 · 작업자: 경석(Claude) · 브랜치 `feature/Boss23`
> 원본 기획: `Re_C_간파_시스템.md` · `Re_C_취약_및_제압_시스템.md` (팀 기획, 09-28 수령)
> 🔴 [PLAN-boss-vulnerable.md](Docs/history/PLANS/PLAN-boss-vulnerable.md)(09-21, 취약 5초·슈퍼아머 해제)는 **폐기** — 이 문서가 대체한다.

## 0. 용어 (혼동 금지)

| 기획 용어 | 코드 | 비고 |
|---|---|---|
| **간파** | 인터럽트 / 카운터 (`isInterruptAttack`, `CounterWindow`, `EnterCounterGroggy`) | 플레이어 우클릭 스킬(`FirstMeleeInterruptSkill`) |
| 간파 유효구간 | 카운터 창 (`_counterWindow`) | 돌진 0~1.5초 · 잡기 ≈1.88~4.40초 |
| 간파 게이지 | 신규 (`_counterGauge` 100→0) — 기존 `_counterGroggyCount`(0→5) 대체 | HUD `BossHealthHUD/Detection_Fill`(회색, 미배선) |
| **제압** | 기존 **Break** 상태·클립 재사용 | 5초 |
| **취약** | 신규 | 4초, **간파 성공 그로기 1.5초부터 시작** |
| 필수 기믹 · 페이즈 전환 | **차징(송전기 시퀀스 — 차징하러 가는 점프 포함)** · **레이지 돌진** · **등장 연출(점프)** | 전투 중 도약 공격(Leap)은 **일반 공격** |

중간보스(Gauntlet·Spinner)는 **취약·제압 없음** — §4 의 표시 타이밍 정리만 해당.

## 1. 팀장 확정 (09-28)

| # | 결정 |
|---|---|
| D1 | 간파 성공 → **그로기 1.5초 유지** + 취약 4초가 **그로기 시작 시점부터** 흐른다(움직이는 취약 = 2.5초). 그로기 중에도 간파 스킬 넉백 가능 |
| D2 | **송전기 전멸 그로기는 간파 게이지와 무관** — 지금 코드는 카운트를 +1 한다(`allowBreak:false`) → **분리** |
| D3 | 필수 기믹 = 차징(점프 포함) · 레이지 · 등장. 간파 판정 없음 · 취약 넉백으로 안 끊김 · 제압은 끝난 뒤로 **예약** |
| D4 | 제압 = 기존 Break 클립 재사용 |
| D5 | 솔로 빈틈(간파 쿨 8초 > 취약 4초 → 혼자선 벽 넉백 불가)은 **의도 — 그대로** |
| D6 | 넉백 경로 장애물 없음. **외곽 벽 = 보스방 경계 사각형**(투명벽 충돌도 벽 판정) |
| D7 | 증기 벤트: **코드만 바로 쓸 수 있게**. 플레이어·보스 **둘 다 피해**. 활성 주기 미정(인스펙터 노출). 위치 = 보스방에 배치될 벤트 전부. VFX 는 민경 |
| D8 | 간파 게이지 UI = HP 바 밑 회색 바(`Detection_Fill`) |
| D9 | 취약 중 잡기를 간파 스킬로 끊으면: **보스 3m 넉백 · 잡힌 플레이어는 그 자리 해제** |
| D10 | 취약·제압 비주얼은 **임시**(코드 틴트). 간파 가능 연출(노란빛)·VFX 에셋은 **민경 작업 — 건드리지 않는다** |
| D11 | 표시 타이밍 수정 1·2·5·6 채택(§4). 3(노란빛 교체)·4(정면 강조)는 하지 않음 |
| D12 | 잡기 예고 채움 방향 **반대로**(바깥 → 보스, "끌려온다") |

## 2. 상태 흐름 (서버 권한)

```
일반 ── 간파 성공(창 열림 + 정면 ±60° + 간파 스킬) ──▶ 게이지 −20
   ├ 게이지 > 0 ─▶ 그로기 1.5초 ┐ 취약 4초 (그로기 시작부터)
   │                           └─ 이동·일반 공격 가능 · 간파 창·연출 없음
   └ 게이지 = 0 ─▶ 제압 5초 (Break 클립 · 받는 피해 ×1.2 · 간파/넉백/환경 무시)
                     └ 종료 ─▶ 게이지 100 · 예약된 기믹 있으면 그것부터
취약 중 간파 스킬 적중(방향 무관) ─▶ 일반 공격 중단(잡기면 해제) + 공격자 반대로 3m/0.35초 넉백
   ├ 넉백이 방 경계에 닿음 ─▶ 게이지 −20 · 취약 즉시 종료 · 안쪽 0.5m 반동(0.3초)
   └ 분사 중 벤트 범위에 들어감(넉백 여부 무관) ─▶ 게이지 −20 · 취약 즉시 종료
   └ 게이지 = 0 이 되면 ─▶ 제압
우선순위: 사망 > 필수 기믹(예약) > 제압 > 취약 > 일반
```

## 3. 구현 (23호)

### 3-1. 게이지·제압 (기존 카운트/Break 교체)
- `NetworkVariable<float> _counterGauge`(서버 쓰기) 100 시작. 간파 성공·벽·벤트 각 `−20`(SO), 0 미만 금지, 자동 회복 없음.
- `BossCounterProgress` 를 게이지 기반으로(EditMode 테스트 갱신). `EnterCounterGroggy(allowBreak)` 는 **간파 성공 전용**으로.
- 송전기 전멸(`TwentyThreeBoss.cs:3245`)은 **게이지를 건드리지 않는** 별도 그로기 경로로 분리(D2).
- 제압: Break 경로 재사용, 지속 5초(SO `suppressDuration`), 받는 피해 ×1.2(`TakeDamage` 에서 플레이어 출처 피해에만), 종료 시 게이지 100.
- 필수 기믹 중 0% → `_pendingSuppress` 예약 → 기믹 종료 지점에서 실행. 제압 중 페이즈 전환 조건 → 예약 → 제압 종료 뒤 실행(일반 패턴 선택 없이).
- 사망은 모든 예약·게이지 처리를 버린다.

### 3-2. 취약
- `_vulnerableUntil`(서버 시각) — 간파 성공 순간 = 그로기 시작부터 4초(D1). 연장·갱신 없음.
- 취약 중 간파 창은 열리지 않는다(창 여는 곳에서 게이트) — 간파 가능 패턴을 골라도 판정·연출 없음.
- 필수 기믹 진입 시 취약 즉시 종료(기믹 우선).
- 임시 비주얼: 보스 몸 틴트(코드, `HitFlash.SetBaseTint` — 새 에셋 없음).

### 3-3. 취약 넉백
- 취약 중 `isInterruptAttack` 적중: 방향 = 공격자→보스 수평(겹치면 스킬 공격 방향). 3m / 0.35초(SO).
- 진행 중 일반 공격 중단 = `AbortAttackChain()`(잡기면 `ReleaseGrabbedPlayer` — 플레이어는 그 자리, D9). 필수 기믹 중이면 피해만.
- 재적중 → 남은 거리 폐기, 새 방향 3m 처음부터. 같은 틱 여러 명 → 서버 처리 순서의 첫 적중만 넉백.
- 이동: NavMeshAgent 끄고 운동학적으로 밀기(상태를 바꾸지 않는 변위 — 옛 계획 §1-3 의 `LinearKnockback` 함정 회피), 끝나면 `SamplePosition` 후 Warp.
- **벽 판정**: 보스방 경계 사각형(아레나 루트 bounds, 보스 반경만큼 안쪽)에 닿는 순간 성공 → 반동 0.5m/0.3초. 취약이 이미 끝났으면 이동만 끝내고 무효(§5.4).

### 3-4. 증기 벤트 (코드만)
- `SteamVent : NetworkBehaviour`(또는 서버 판정 MonoBehaviour + 전 피어 연출 훅) — **붙이면 자동 등록**(정적 목록, OnEnable/OnDisable).
- 서버: `interval` / `activeDuration` / `damage` / 범위(박스 또는 원) 인스펙터 노출. 분사 중 범위 안 **플레이어·보스 피해**(D7).
  취약 보스가 범위에 있으면 보스에게 `OnVentHit()` → 게이지 −20 · 취약 종료(취약당 최초 1회, 벽과 같은 틱이면 먼저 판정된 쪽).
- 연출은 이벤트/훅만(`onVentStart/Stop`) — VFX 는 민경이 꽂는다.

### 3-5. UI
- `BossHealthHUD` 에 `Detection_Fill` 참조 추가 → `fillAmount = gauge/100`. 복제는 게이지 NetworkVariable.

## 4. 간파 표시 타이밍 정리 (전수조사 09-28 · Codex+탐색 교차 일치)

| # | 문제 | 수정 |
|---|---|---|
| 1 | 몸 오버레이(`DissolveOverlay`)가 창 닫힌 뒤 **0.4초 잔류** · 켜질 때 0.8초 페이드 | 몬스터 프리팹 4종(23호·Gauntlet·Spinner·WallBot)의 **fadeIn/fadeOut 을 0** — 스크립트(민경 SVN) 무수정, 프리팹 값만 |
| 2 | 간파 스킬은 서버 시작 **0.15초 뒤** 판정 → 창 끝 무렵 누르면 실패 | ⚠️ 1안(닫힌 뒤 0.15초 유예)은 **폐기**(팀장 09-28 — 유예 없음, 시간이 다 되면 닫힌다). **서버 판정 창은 그대로, 표시만 0.15초(`HitDelay`) 먼저 끈다** → "보일 때 누르면 성공"이 보장된다. 이벤트로 닫히는 창(잡기 — 마지막 내려치기 직전)도 내려치기 타이머로 닫힘 시각이 정해지므로 같은 방식. 클라→서버 지연은 보정 안 됨(`AttackInfo` 무수정) |
| 5 | Gauntlet 창 1.5초 < 팔 든 자세 2.0초(명중 2.2초) — 0.7초가 "준비 중인데 창 닫힘" | Gauntlet `MonsterCounterWindow.windowDuration` **1.5 → 2.0** |
| 6 | Gauntlet·Spinner 예외 경로 정리 누락 · 늦은 합류 시 창 표시 누락 | 공격 이탈 시 `Counter.Close` + 표시 OFF 보장(`WallBot.cs:323` 패턴) · 창 상태를 `NetworkVariable<bool>` 로(23호 방식) |
| — | 23호 정면 ±60° 조건 — 로그로 실패 1건 확인(`정면=False`) | **변경 없음**(D11 — 방향 표식이 이미 정면을 그린다) |

## 5. 잡기 예고 채움 방향 (D12)
- `BossAttackConeTelegraph` 에 채움 방향 옵션(`fillInward`) — 잡기 슬롯만 바깥 끝 → 보스 쪽으로 자란다. 다른 공격 무변경.

## 6. 🔴 리스크
- **슈퍼아머와의 관계**: 문서상 취약 넉백은 간파 스킬 전용 — 슈퍼아머·`AutoHitReactions` 는 그대로 둔다(기본 공격 넉백 없음). 옛 계획의 "슈퍼아머 해제" 경로는 쓰지 않는다.
- **운동학 넉백 중 NavMesh 이탈**: 밀기 끝 `SamplePosition` 실패 시 마지막 유효 지점으로(옛 계획 §1-3 함정).
- **예약 처리**: 필수 기믹 종료 지점이 여러 곳(차징 성공/실패·레이지 종료·등장 끝) — 한 곳이라도 빠지면 제압이 영원히 안 온다 → 단일 `OnMandatorySequenceEnded()` 로 모은다.
- **게이지 복제**: 늦은 합류는 NetworkVariable 이라 안전. HUD 는 OnValueChanged + 스폰 시 현재 값.
- **팀 코드 경계**: 플레이어 스킬·`AttackInfo`·`DissolveOverlay`(SVN)는 무수정. 보스·중간보스·HUD 바인딩만.

## 7. 완료 기준 (Play · MPPM 2인)
1. 간파 성공 5회(환경 없이) → 제압 5초 → 게이지 100 복귀. HUD 회색 바가 20%씩 준다.
2. 간파 성공 → 1.5초 그로기 → 2.5초 움직이며 공격 → 취약 종료. 취약 중 간파 창·연출 없음.
3. 취약 중 간파 스킬: 방향 무관 3m 넉백 · 공격 중단 · 잡기 해제. 벽에 닿으면 −20 + 반동 + 취약 종료.
4. 송전기 전멸 그로기 → 게이지 변화 없음.
5. 차징·레이지·등장 중: 간파 없음, 취약 넉백 안 됨, 0% 되면 끝난 뒤 제압.
6. 제압 중 피해 ×1.2, 간파·넉백 무시. 제압 중 사망 → 즉시 사망.
7. 몸 오버레이가 창과 동시에 켜지고, 서버 창보다 **0.15초 먼저** 꺼진다(잔류 0). 표시가 보일 때 누른 간파는 성공, 꺼진 뒤 누르면 실패(유예 없음).
8. 잡기 예고가 바깥 → 보스로 차오른다.
9. 벤트: 컴포넌트를 붙인 오브젝트가 분사 중 플레이어·보스를 때리고, 취약 보스면 −20.

## 7-1. Codex 교차검증 반영 (09-28 — "보완 후 구현")

| # | 지적 | 반영 |
|---|---|---|
| C1 | Break 는 별도 상태가 아니라 **긴 `ForceGroggy`** — 그로기 중 벽→제압은 Groggy→Groggy 라 상태 콜백이 안 돈다 | 제압을 **전용 플래그·타이머**(`_suppressUntil`)로. 표현·종료를 상태 콜백에 기대지 않는다 |
| C2 | `AbortAttackChain` 은 창·예고·잡기는 정리하지만 **Attack 상태·돌진 이동(`StopDash`)·히트 윈도우는 남긴다** | 취약 넉백 = Abort + 돌진 정지 + 히트 윈도우 종료 + Attack 이탈을 명시. 잡기 해제는 Abort 안의 `EndGrabbedByInstigator`(`ReleaseGrabbedPlayer` 는 던지기 연출까지 나가므로 쓰지 않는다) |
| C3 | ×1.2 범위가 §2·§3-1 에서 달랐고, `TakeDamage(AttackInfo)` 엔 공격자 정보가 없다 | 기획대로 **플레이어 피해만** — `ReceiveAttack` 의 `AttackHitContext` 로 출처 판별. 벤트 피해 제외 |
| C4 | 필수 기믹 "끝"이 여러 갈래 | 예약 제압 실행 지점 = **송전기 전멸 성공** · **레이지 최종 종료(`FinishChain`)** · **등장 종료(`BeginCombatServer`)**. 차징 실패→레이지는 연결이라 **실행 안 함**. 슬롯 누락·타임아웃 예외 종료도 소비 |
| C5 | 같은 타격에 HP 임계(페이즈)와 게이지 0 동시 | 기획 "이미 진행 중인 기믹만 우선" → **제압 먼저, 페이즈 전환 예약** |
| C6 | 사망 우선: 넉백·벽·벤트도 피해 뒤 사망 확인. Abort 가 죽은 보스 콜라이더를 다시 켤 수 있다(`SetHurtable(true)`) | 모든 경로에 사망 가드 · 죽은 보스는 정리 코드가 Agent·콜라이더를 복구하지 않게 분리 |
| C7 | "아레나 루트 bounds" 는 믿을 계약이 아니다 | 경계 = **저작된 `InvisibleBoundaries` 네 벽의 안쪽 면**(`BossRoomAuthoring.cs:466`) — 투명벽 판정과 일치. 보스 반경 = 몸 캡슐 1.53m. 서버 이동만(NetworkTransform 보간), 클라 중복 변위 금지 |
| C8 | 존 오브젝트는 네트워크 스폰이 아니라 NetworkBehaviour 면 `IsServer=false` | 벤트 = **MonoBehaviour**, 서버에서만 판정, 분사 주기는 **서버 시각 기준**(클라도 같은 시각으로 연출). 유닛당 중복 제거 · 벤트로 사망 시 게이지 처리 없음 |
| C9 | `No23` 그로기 0.5초 · Break 2초 | 그로기 **1.5초** · 제압 **5초** |
| C10 | 송전기 분리 시 게이지뿐 아니라 **취약도 주지 않는** 기믹 보상 경로로 | 명시 |
| C11 | 테스트 영향 | `BossCounterProgressTests` · `BossCounterDataTests` · `CounterWindowTests`(공용 창 계약) 갱신 |

## 7-2. 진행 (09-28 세션 끝 기준) — 🔴 **다음 세션 = 취약**

| 단계 | 상태 | 비고 |
|---|---|---|
| 1 간파 표시 타이밍 | ✅ 팀장 확인 | 오버레이 페이드 0 · 표시 0.15초 먼저 소등(`CounterVisualLeadSeconds`) · Gauntlet 창 2.0 · 중간보스 창 NetworkVariable · 이탈 정리 |
| 2 잡기 예고 방향 | ✅ 팀장 확인 | `BossAttackEntry.telegraphFillInward` |
| 3 게이지 · 제압 | ✅ 구현 · **Play 대기** | `BossCounterProgress`(게이지 100→0, step 20) · `_counterGauge` NetworkVariable · `EnterCounterSuccess`/`EnterPylonGroggy`/`EnterSuppress`/`TryConsumePendingSuppress`/`TickSuppressExit` · 플레이어 피해 ×1.2 · No23/No23_Solo 그로기 1.5 · 제압 5 · 테스트 갱신 |
| 4 **취약 넉백 · 벽** | ✅ 구현(09-28 같은 세션으로 당김 — 팀장) · **Play 대기** | `StartVulnerable/EndVulnerable` · `StartVulnerableKnockback`(agent.Move, NavMesh 밖 금지) · `InterruptForKnockback` · 벽 = `InvisibleBoundaries` 안쪽 면 − 몸 반경 · 반동 0.5/0.3 · 벤트 `OnSteamVentHit` · 틴트 `_vulnerableVisual` · `MonsterBase.OnServerPreTick` 훅 |
| 5 증기 벤트 | ✅ 코드 · **Play 대기** | `Assets/1.Scripts/Map/SteamVent.cs` — 붙이면 자동 등록, 서버 시각 주기, 플레이어·보스 피해, 연출 훅 `onVentStart/Stop`. 취약 연동은 `TwentyThreeBoss.OnSteamVentHit`(현재 빈 자리) |
| 6 HUD | ✅ 구현 · **Play 대기** | `BossHealthHUD.counterGaugeFill` → `Detection_Fill`(23호가 아니면 숨김) |

### 🔴 09-28 팀장 Play 결과 — 다음 세션은 여기서 시작
| 항목 | 결과 |
|---|---|
| 간파 성공 → 게이지 −20 · HUD 회색 바 | ✅ |
| 취약 파란 틴트 | ✅ |
| 취약 중 간파 스킬 → 공격 끊김 | ✅ |
| 🔴 **취약 넉백 — 반대쪽으로 안 밀린다** | ❌ 공격만 끊긴다. 팀장 추정: "간파 스킬에 넉백 정보가 없어서". **확인 필요** — 코드는 `AttackInfo` 넉백 필드를 안 읽고 방향만 계산해 `agent.Move` 로 민다(`TickVulnerableKnockback`). 의심 순서: ① 그로기(첫 1.5초) 중 `HandleGroggy`/`ForceGroggy` 가 에이전트를 끄거나 `updatePosition` 을 막아 `agent.Move` 가 무효 ② `OnServerPreTick` 이 안 불리는 경로(Update 조기 return — `_serverLogicSuspended`·`_initialized`) ③ `_kbActive` 가 켜지자마자 꺼짐 ④ NetworkTransform 권한. **첫 한 줄: 넉백 틱마다 위치·agent.enabled·isOnNavMesh 진단 로그** |
| 🔴 **제압 중 그로기 애니가 루프로 계속 재생** | ❌ 5초 동안 반복된다 → 한 번 재생 후 유지(Break 클립처럼)로 바꿔야 한다. `ForceGroggy` 의 `groggyBool` 경로 / 컨트롤러 Groggy 상태 loopTime 확인 |
| 증기 벤트 | ⏳ 미확인 |
| 제압 5초 → 게이지 100 | 명시 확인 없음(다른 문제 없다고 함) |
- 🔎 **09-29 넉백 원인(코드 수정 · Play 재확인 대기)**: Editor.log 에서 두 번 모두 `취약 넉백` 직후 **첫 틱에** `외곽 벽 충돌`. `ResolveArena` 가 `Boundary_XMin` 등 이름(방 **로컬** 축)을 **월드 AABB** 로 읽는데, 존은 90° 단위 회전 배치(`MapContentSpawner.cs:61`) → 90°/270° 방에서 경계가 뒤집혀 즉시 벽 판정 → 공격자 쪽 0.5m 반동만 남음. 수정: 경계·판정을 `InvisibleBoundaries` 로컬 공간에서 계산 + 경계 1회 로그(방 회전·보스 로컬 위치). 위 ①~④ 의심은 해당 없음(`agent.Move` 는 돌았다). 🔴 방 회전값은 로그에 없어 **가설 확정은 다음 Play 의 `취약 벽 경계` 로그로**.
- 🔎 **09-29 그로기 루프 원인(수정 · Play 재확인 대기)**: `No23Controller` 의 AnyState→GroggyStart 가 `Groggy` bool 조건 — bool 이 켜진 동안 GroggyStart→Groggy 로 넘어가면 AnyState 가 다시 GroggyStart 로 보낸다(CanTransitionToSelf 0 이어도 **다른 상태**라 막히지 않음). 1.5초 그로기는 첫 재생 도중 bool 이 꺼져 안 보였고 제압 5초에서 드러남. 수정: 그 전이 **Mute** + `TwentyThreeBoss.PlayStateAnimation` 이 Groggy 진입 시 `GroggyStart` 로 1회 CrossFade(상태 복제 경로라 늦은 합류도 동일). 종료는 기존 Groggy→GroggyEnd. ⚠️ 그로기 1.5초인데 진입 클립(0~90f)이 더 길어 **애니가 로직보다 늦게 끝나는 것**은 기존 그대로(범위 밖 — 확인 필요 시 별도).
- ✅ **09-29 팀장 Play — 취약 넉백 동작 확인.** 로그 `방 회전 270°` → 원인 확정. 거리는 `BossDataSO` 취약 헤더 `vulnerableKnockbackDistance`(이미 노출).
- 09-29 넉백이 플레이어를 파고듦 → `TickVulnerableKnockback` 이 진행 방향 앞 플레이어에서 멈춤(`PlayerBlocksLunge` 재사용). 벽 판정이 먼저라 벽 성공은 안 잃는다.
- 🔎 09-29 점프어택 "둘 중 한 명만 맞음": 로그상 착지 4회 중 3회가 같은 한 명만. 버퍼(16)는 원인 아님(플레이어당 활성 콜라이더 1~2). 예고 중심 `_jumpArrivePoint` ↔ 판정 중심 `transform.position`(Warp 후) 어긋남 · 레이어/콜라이더 · 서버측 클라 위치 지연이 후보 → `LogJumpLandingDiagnostics` 임시 로그(원인 확정 후 삭제).
- 제압 애니: 09-29 Play 에서 끼임 때문에 **못 봄** — 재확인 대기.
- 🔴→✅ **09-29 취약 중 돌진이 안 나감**(준비 자세·경로 표시만, 로그 `Dash 체인이 Windup 에서 타임아웃`): 선딜 게이트를 `opensNow`(창 표시 여부)로 켜서 취약 중엔 꺼진 채 시작 → Windup 이 `ShouldRelease` 를 영영 못 받음. 수정: 게이트 사용 = `opensCounterWindow && !Grab`(공격 종류), 창 표시와 분리. Play 재확인 대기.
- 09-29 끼임(모터 겹침 해소) Codex 교차검증 = **"보완 후 가능"**. 채택: 서버는 원격 플레이어 모터를 안 돈다(오너만) · 벽 사이 분리 불가 · 잡힘 포즈 경로 우회. 기각: "23호 에이전트 반경 0.3 덮어씀"(09-28 `KeepPrefabAgentRadius` 로 이미 막음) · "CapsuleCast 는 시작 겹침 미감지"(모터는 `CapsuleCastNonAlloc` — 시작 겹침을 distance 0·normal −dir 로 반환). 모터 수정은 팀장 판단 대기.
- 🔎 09-29 2차 Play: 점프 **둘 다 맞음**(진단 전문 — 콘솔 목록은 2줄만 보여 한 명처럼 보였다). 문제는 비주얼: 두 명이 보스 중심 1.2/1.42m = 몸 반경 1.53 **안쪽**(착지가 위에 내려앉음) → 넉백 이동이 모터 시작 겹침에 막히고 플래시가 모델에 가린 것으로 추정. 끼임 진단 확정(거리 0 · 시작겹침 True · 분리 방향 = 이동 방향) → 모터 `IsEscapingEnemyOverlap` 적용(CONTEXT 은희 절). 점프 진단에 몸안·슈퍼아머 추가 — 다음 Play 로 확인.
- 🔎 **09-29 3차 Play(1인) — "경계 밖" 연쇄**: ① 보스 중심이 벽 0.5m 까지 감(`[23호/돌진] clearance 0.00`) — NavMesh 베이크 반경 0.5 가 벽 여유를 정하고 에이전트 반경(0.85)은 벽 여유와 무관(Unity 매뉴얼) → 몸 1.53 이 투명벽 밖으로 ~1m 돌출 → 벽-보스 끼임 · ② 잡기 해제가 손 소켓 = 경계 밖(은희 인계) · ③ 방 밖 낙하(낙하 복귀는 정상) · ④ 방 밖 플레이어 노린 점프 → Warp 실패 → 에이전트 이탈 → `IsStopped` 에러 폭주 · AI 영구 정지.
  - ✅ B 구현: 점프 착지점 = 경계 − 몸 반경 사각형 클램프 + `NavMesh.SamplePosition`(3m) · `WarpTo` 가 Warp 반환값 확인 → 재투영 → 실패 시 제자리 재부착.
  - ✅ **A 결정(팀장 09-29): 보스방만 NavMesh 여유 띠** — 보스방엔 보스 한 마리뿐, 보스 이동은 전부 NavMesh 경유(돌진 SetDestination · 넉백 agent.Move · 점프 투영+Warp). `BossRoomAuthoring` 메뉴 **Build Boss Room NavMesh Margin** → `bossroom.prefab/NavMeshMargin/Margin_*` 4개(`NavMeshModifierVolume` Not Walkable, 폭 1.0 · 경계 = 저작된 InvisibleBoundaries 안쪽 면). Rebuild Boss Room Bounds 끝에서도 호출. 프리팹 diff 추가 232줄 · 삭제 0 · guid 불변. 🔴 폭 상한 = 가장자리 ≤ 몸 반경 1.53(넘으면 취약 벽 판정 불가). ⏳ 침식이 띠에도 붙는지 미확인 → Play 로그 `[23호] NavMesh 여유` 로 폭 조정.
  - (기각) 보스 전용 에이전트 타입 + 추가 Surface — 한 마리뿐인 방에 과함. (기각) 매 틱 클램프 — 증상 되돌리기.
  - ~~⏳ A 보류~~  정석 후보 = 보스 전용 에이전트 타입(반경 ≈1.5) + 보스방 한정 NavMeshSurface 추가 베이크 → NavMesh 자체가 벽 여유를 준다. 대가: `NavMesh.SamplePosition`/`CalculatePath` 의 areaMask 오버로드가 어느 에이전트 타입 메시를 조회하는지 **미확인** — 보스 경로는 `NavMeshQueryFilter.agentTypeID` 오버로드로 바꾸는 게 안전(MonsterBase 공용 코드 포함, 착수 전 확인) · 런타임 베이크 1회 추가(30×30).
- ✅ **09-29 팀장 Play 후 요청 3건 (구현 · Play 확인 대기)**
  - 어퍼컷 예고 0.7 → **0.5**(No23·No23_Solo). FBX 60fps(TimeMode 3) → `uppercut_prep` 30f = 0.5초 — 0.7 이면 끝 프레임에서 0.2초 굳었다가 때렸다. 훅 2종도 같은 0.2초(요청 외라 유지).
  - 차징 진입 점프 = **착지 범위 공격**(예고 원 + 일반 착지 클립 + `ApplyJumpLandingDamage`, 피해는 점프 행). 09-21 "차징은 무음 착지·예고 없음" 뒤집기.
  - 점프어택은 체인 전체 **안 끊김**(`IsInJumpChain`): 슈퍼아머는 원래 걸려 있었고, 취약 넉백만 슈퍼아머와 무관하게 끊던 구멍(이륙 중 피격 가능)을 막음 — 피해만. "이륙 전" 구간은 이륙이 공격 시작과 동시라 없음. 플레이어에게 보스 스턴 CC 는 없음.
  - ✅ 취약 중 근접 Q 견인 무시 = **의도(팀장 09-29 A안)**. 규칙 통일: 취약 중 넉백은 **플레이어 간파로만**, 다른 CC 는 보스에게 피해만(CC 미적용). 23호 `AutoHitReactions => false` 가 이미 그렇게 동작.
  - 🔄 **⚠️ 뒤집음 — §3-3 "취약 4초 유지 · 재적중 = 새 방향 3m"**: 넉백이 **끝나면 그로기·취약을 함께 종료**(`FinishVulnerableKnockback` — 끝까지 밀림·플레이어에 막힘·벽 반동 종료 3경로 단일화). 그로기 중 다른 플레이어의 간파 → 다시 밀림(새 방향 3m), CC 는 합산·연장 없이 흐르다 넉백 종료 시 전부 끝. 제압으로 넘어간 경우는 제외(제압 5초 유지). 근거: 팀장 — "예외처리가 아니라 통일".
  - 정정: 09-29 앞서 "그로기 진입 클립 90f 가 1.5초 로직보다 길다" 우려 → 60fps 라 정확히 1.5초. 문제 아님.
- ✅ **09-29 잡기 낚아채기 타이밍**: 끌어오기 끝(AcquireGrab)에 곧바로 Carry 로 손 소켓에 붙던 것 → 대상은 **Push 로 끌려온 자리에 두고**, `Boss_23_grab` 의 `OnAttackHit`(정규화 0.354 = 1.10초, 손이 플레이어 쪽으로 오는 프레임)에서 Carry(`AttachGrabbed`). 이벤트 누락 시 Acquire 구간 끝(grabCatchDuration 1.1 — 같은 시점) 안전망 + 경고. 간파 창도 이 순간에 연다(C10 "실제로 붙잡은 뒤"). ⚠️ 뒤집음: 같은 날 앞서 "표시 지연(LateUpdate 부착)"으로 오진해 `Player.cs` 를 고쳤다가 팀장 지적으로 **되돌림** — 문제는 붙이는 시점이었다.
- Codex 교차검증은 크레딧 복구 후(09-28 20시 이후) — 이번 구현 전체(§3·§4 NavMesh 수정 포함).

### 다음 세션 인수인계 — 취약 (§3-2 · §3-3 · §7-1 C1~C8 참고)
1. **진입**: `EnterCounterSuccess()` 의 TODO — 게이지가 남으면 `_vulnerableUntil = now + 4`(그로기 1.5초 시작 시점부터, D1). 취약 중 간파 창 안 열기(창 여는 두 곳 — `StartAttack` 의 opensNow · `AcquireGrab`).
2. **넉백**: `ReceiveAttack` — 취약 중 `isInterruptAttack`(방향 무관) → 3m/0.35초, 공격자→보스 수평(겹치면 스킬 방향).
   `AbortAttackChain` + 돌진 정지·히트 윈도우·Attack 이탈(C2). 그로기 중에도 변위만(그로기·취약 타이머 불변). 재적중 = 새 방향 3m 처음부터. 필수 기믹 중이면 피해만.
3. **벽**: `InvisibleBoundaries` 네 벽 안쪽 면(`BossRoomAuthoring.cs:466`) − 몸 반경 1.53. 닿으면 게이지 −`environmentGaugeStep` · 취약 즉시 종료 · 0.5m/0.3초 반동. 0 이면 `EnterSuppress`(그로기→그로기라 **상태 콜백이 안 돈다** — `_suppressed` 플래그로 이미 대비, C1).
4. **벤트**: `OnSteamVentHit` 채우기 — 취약이면 −20 · 취약 종료, 취약당 1회, 벽과 같은 틱이면 먼저 온 쪽.
5. **임시 비주얼**: 취약 = 몸 틴트(`HitFlash.SetBaseTint`, 코드). VFX 에셋은 민경(D10).
6. 🔴 끼임: 넉백이 플레이어를 파고들 수 있다 — 겹침 해소는 은희(CONTEXT). 넉백 경로도 `PlayerBlocksLunge` 식으로 앞 플레이어에서 멈출지 결정 필요.

## 8. 범위 밖
- 간파 가능 노란빛·취약/제압 최종 VFX·효과음(민경) · 증기 벤트 배치·주기 값(추후) · `AttackInfo` 누른 시각(은희 합의)
- 23호 정면 강조 표시 · 보스 조기 스폰(로딩 중 하늘 대기) 아이디어
