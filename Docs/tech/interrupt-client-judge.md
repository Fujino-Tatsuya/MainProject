# 간파 클라 1차 판정 · 서버 2차 검증 (할 일 191)

> 상태: **구현 완료(1~6단계, 브랜치 `feature/InterruptClientJudge`)** — 사람 Play·MPPM 확인 전(§13).
> 커밋: 1 `27a5c26f` · 2 `c2bb6d88` · 3 `62e4032c` · 4 `baeb53a3` · 5 `a857cd31` · 6 이 문서.
> 계획(승인본): `_lane-artifacts/common/191-interrupt-client-judge/PLAN.md`(레포 밖). 문서와 코드가 다르면 코드가 기준이다.
> 관련: 대쉬 간파 [dash-interrupt.md](dash-interrupt.md) · 연출 [interrupt-presentation.md](interrupt-presentation.md) · 권한 [networking.md](networking.md).

**한 줄 정의**: 간파 성공은 **오너 클라가 자기 화면 기준으로 Hit 타이밍에 판정**한다. 서버는 같은 계산을 자기 이력으로 다시 돌려 **2차 검증**만 하고, 선착순/동시와 보스 효과 1회를 정한다.
누를 때의 예측과 Hit 타이밍 판정이 **같은 함수**라 예측과 판정이 어긋나는 일은 누른 뒤 세계가 바뀐 경우(보스 이동·창 변화·CC)뿐이다.

## 1. 확정 사항 (grill D1~D13)

| # | 항목 | 결정 |
|---|---|---|
| D1 | 권한 | 간파 성공 = 오너 클라 1차 판정, 서버 = 2차 검증(보험). 플레이어 권한 원칙의 **예외**(AGENTS.md §4) |
| D2 | 판정 시점 | Hit 타이밍. 누를 때는 같은 계산으로 **예측만** 한다 |
| D3 | 창 시간표 | 패턴 시작 때 창 열림·닫힘 예정을 서버가 배포. 다음 패턴은 예측하지 않는다 |
| D4 | 예측 ≠ 판정 | 강 예측 → 판정 실패 = 그 순간 연출 종료. 약 예측 → 판정 성공 = 성공 섬광은 그대로 |
| D5 | 끊김 | 넉백·끌기·잡기(상태 전환 CC)로 스킬이 끊기면 즉시 연출 종료. 일반 피해는 경직이 없어 무관 |
| D6 | 관제 매니저 | `InterruptManager` 하나(`NetworkManager.prefab`)가 시간표·판정·검증·선착순을 맡는다 |
| D7 | 조기 닫힘 지연 | 클라 판정 시각 ≤ 서버 닫힘 시각 + 허용 오차면 인정 |
| D8 | 동시 성공 | 첫 승인 후 **0.05초** 안 승인 = 동시(모두 연출·성공 기록). 그 뒤 = 선착순 패배(거부) |
| D9 | 동시 효과 | 보스 경직·제압 칸 감소는 **한 번만**. 연출·성공 기록(통계·특성)만 각자 |
| D10 | 암전 제외 | 디렉터 스위치 `keepCoSucceededBright`(기본 켬) — 함께 성공한 캐릭터도 내 암전에서 밝게 |
| D11 | 검증 수치 | 시각 ± (RTT/2 + 0.033초) · 거리 + 0.2m · 정면 각도 + 5°. 데이터 테이블 칸 |
| D12 | QA 기록 | 간파마다 예측 / 판정 / 서버 결과 / 어긋난 이유 한 줄. 보스 이동 보정은 이 비율을 보고 결정 |
| D13 | 보스 | "웰즈&23호"는 보스 하나. 다중 대상 처리 없음 |

## 2. 구조

```
보스(23호·중간보스·스파링 봇·허수아비)
  └ 창 열림(닫힘 예정·공격 시작·지목자) / 닫힘 ──▶ InterruptManager.ServerReportWindow (서버)
                                                   │ 창 시간표 named message 배포(열림·조기 닫힘·늦은 닫힘)
                                                   ▼
오너 클라  InterruptManager (모든 피어 하나)
  누름: BeginOwnerJudge → 누른 시각 표본으로 Predict → 시도 cue(대상 없음/약/강)
  판정 시각 표본이 쌓임: Judge → 판정 cue(성공/실패) → 성공이면 Claim 1회 ──▶ 서버
                                                   ▼
InterruptManager (서버)  InterruptClaimServer.Process
  소유 확인 → 같은 Judge 를 서버 이력으로(D11 여유) → 대상 규칙 → 장부(D8)
  → 첫 승인만 대상 효과(D9) → Approval(승인 목록) 전 피어 / Reject 그 오너
```

### 2.1 `InterruptManager` — 간파의 단일 진입점

- 위치: `Assets/1.Scripts/Player/InterruptJudge/InterruptManager.cs`, `NetworkManager.prefab` 루트의 MonoBehaviour(모든 피어 하나).
- **세계 스냅샷**: LateUpdate 마다 샘플 → 대상별 약 1초 링 버퍼(`InterruptSampleHistory<T>`, `HistorySeconds` 1.0), 시각 보간 조회(범위 밖 = false). 클라는 보는 값, 서버는 실제 값을 쌓는다.
  - 플레이어(`Player.OnNetworkSpawn` 에서 `RegisterPlayer`): 위치·바라보는 방향·Alive·CC(Knockback/Restrained)·진행 중 대시(시작·방향·속도·남은 시간).
  - 대상(`InterruptTargetRegistry` 그대로): 위치·정면·간파 불가 상태(`IInterruptJudgeTarget`)·지금 창의 지목자.
  - 간파 상자 형상은 샘플하지 않는다 — 판정 순간 `HitboxAnchorShape` 로 읽는다.
- **창 시간표**(`InterruptWindowSchedule`): 창 id·공격 시작·열림·닫힘 예정·실제 닫힘·닫힘 종류(`Open`/`OnTime`/`Early`/`Extended`/`Unscheduled` = 길이 모름)·지목자. 늦은 합류 클라에는 열린 창만 1회 보낸다.
- **시각**: 전부 서버 시각(`NetworkClock.GameNow` — 슬로우·일시정지 제외). 로컬 게임초 ↔ 서버 시각은 `InterruptServerClock`(오프셋을 부드럽게 따라가고, 0.25초 넘게 튀면 바로 맞춤).
- **전송 = named message 4개**: `InterruptManager.Window` / `.Claim` / `.Approval` / `.Reject`.
  - 이유 ①: `NetworkManager.prefab` 에 NetworkObject 가 없어 NetworkBehaviour·RPC 를 못 둔다(`GlobalTimeScale` 과 같은 방식).
  - 이유 ②: 간파 스킬 RPC(`RequestUseSkillRpc`)는 누를 때 한 번이라 Hit 타이밍 결과를 실을 수 없다.
  - 호스트 오너는 서버 처리를 직접 부른다(메시지 없음).
- 확인용 로그: 프리팹 `logWindows`(기본 끔, 에디터 전용)를 켜면 창 열림·닫힘·수신이 콘솔에 남는다.

## 3. 예측·판정 — 같은 순수 함수 (`InterruptJudgeCalculator`)

스킬은 `Predict`·`Judge` 만 부른다. 계산은 매니저 안 순수 함수 하나(`IInterruptJudgeWorld` 로 스냅샷을 읽음).

| 항목 | 규칙 |
|---|---|
| 판정 시각 · 근접 | 누른 시각 + **Hit 지연** |
| 판정 시각 · 대쉬 | **max(실제 대시 끝, 누른 시각 + Hit 지연)** |
| Hit 지연 | `InterruptHitDelay` = 클립 Hit 이벤트 시각 ÷ 지금 애니 속도(`PlayerAttackSpeedRules.AnimatorSpeed`). 공격속도 변경 직후도 같은 식(`PlayerInterruptSkillBase.TryGetJudgeHitDelay`) |
| 대시 끝 | **공용 스윕 `PlayerDashPath`** — `PlayerMotor` 의 실제 대시 이동과 같은 충돌 검사. 벽(`DashBlockedByWall`)·유닛(`DashEndedByUnit`)에 막히면 그 자리 |
| 대상 | 판정 앵커를 공격 방향(근접 = 조준, 대쉬 = 대시 방향)으로 돌려 판정 위치에 놓은 형상(`PlaceHitbox`)에 닿는 등록 대상 + 대상 정면 허용각(`InterruptTargetGeometry.FacesAttacker`) |
| 창 | 판정 시각에 창 시간표가 열려 있는가(조기 닫힘·늦은 닫힘 반영). 지목자가 있으면 그 사람만 |
| 상태 | 시전자 생존·CC 아님, 대상 간파 불가(취약·제압) 아님 |
| 출력 | `InterruptJudgeResult` — 대상 없음 / 실패(이유 `OutOfRange`·`Angle`·`CasterDown`·`CrowdControlled`·`TargetBlocked`·`NoWindow`·`WindowClosed`·`NotDesignated`) / 성공(대상·창 id). 예측은 `Forecast`(대상 없음/약/강)를 함께 낸다 |

- **예측**(누를 때) = 누른 시각 표본으로 위 계산을 판정 시각까지 내다본다. 보스 이동은 보지 않는다(D12 비율로 보정 여부 결정).
- **판정**(Hit 타이밍) = 판정 시각 표본이 이력에 쌓인 LateUpdate 에서 같은 계산.
- 서버 시도 슬로우 강·약 = 같은 `Predict` 를 서버 이력 최신 표본·서버 창 시간표로(`TryPredictServerAttempt`). 이미 소비된 창은 조기 닫힘이라 약.

## 4. 오너 판정 흐름

- 누를 때 `PlayerSkillController` → `InterruptManager.BeginOwnerJudge(시전자, 근접/대쉬, 공격 방향, 누른 로컬 시각)`.
- 다음 샘플 뒤 누른 시각 표본으로 `Predict` → 시도 cue + `OwnerPredicted` 이벤트 → 판정 시각 확정.
- 판정 시각 표본이 쌓이면 `Judge` → 판정 cue + `OwnerJudged` 이벤트.
  - 성공 = 신고 `InterruptClaim`(판정 시각·대상·창 id·공격 방향·내 위치) **1회**.
  - 실패·대상 없음 = 신고 안 함.
- 판정 전 끝(`OwnerJudgeAbandoned`, 이유 `InterruptOwnerJudgeAbandon`):
  - `SkillCut` — 스킬이 정상 완료 아닌 이유(넉백·끌기·잡기 = `Cancelled`·사망·대시·안전망)로 끝남(D5). 판정 전 정상 완료(End)는 판정을 기다린다.
  - `CastRejected` — 서버가 시전을 거부(`RejectSkillClientRpc`).
  - `OutOfHistory` — 판정 시각이 이력(1초) 밖.

## 5. 서버 검증 (`InterruptClaimServer` · `InterruptClaimValidator`)

1. **소유 확인** — 신고한 클라가 그 시전자의 오너인가.
2. **같은 `Judge` 를 서버 이력으로** — 별도 규칙 없음. 여유(D11):
   - 시각: 그 클라 RTT/2(`InterruptLagMonitor` 측정) + `verifyTimeSlack`. 창은 판정 시각 ± 시각 여유 — **조기 닫힘도 닫힘 + 여유까지 인정**(D7).
   - 시전자·대상 표본: 판정 시각 ± 시각 여유 안 **한 칸이라도 성공이면 통과**(계산기 `SampleOffset`).
   - 거리: 간파 상자 수평 반폭·반길이(구·캡슐 반지름) + `verifyDistanceSlack`.
   - 각도: 대상 정면 허용 반각 + `verifyAngleSlack`.
3. **대상 규칙**(첫 승인 후보만) — `IInterruptClaimTarget.ServerAcceptsInterruptClaim`(사망·23호 기믹 등).
4. **장부** `InterruptClaimLedger`(창마다, 서버 도착 시각 기준) — 첫 승인 → `simultaneousSeconds` 안(경계 포함) = 동시 → 그 뒤 = 선착순 패배(`FirstComeLost`). 같은 사람 중복 = `Duplicate`.
5. **결과 배포** — 승인 = 같은 창 승인 목록 전체를 모든 피어에(`ApprovalReceived`), 거부 = 그 오너에게 이유와 함께(`RejectionReceived` — `OutOfRange`·`Angle`·`WindowClosed`·`FirstComeLost`·`ServerRejected`). 서버는 `ServerClaimProcessed`.

### 5.1 허용 오차 칸 — 데이터 테이블

`InterruptManager` 에 `[DataTableSheet("SlowMotion", Order = 3)]`, 시트 `SlowMotion` · 대상 `NetworkManager` 열.

| 칸 | 기본값 | 뜻 |
|---|---|---|
| `verifyTimeSlack` | 0.033 | 시각 여유(초). RTT/2 에 더한다 |
| `verifyDistanceSlack` | 0.2 | 거리 여유(m) |
| `verifyAngleSlack` | 5 | 정면 각도 여유(도) |
| `simultaneousSeconds` | 0.05 | 동시 성공 인정 시간(초) |
| `logWindows` · `qaRecordFile` | — | `[DataTableIgnore]` — 디버그 칸 |

- 🟨 xlsx Export 전까지 테이블 모드·빌드에서도 **코드 기본값**(= 프리팹 값)이 쓰인다. Export 는 최신 development 워크트리에서 병합 Export 로([data-table.md](data-table.md)).

### 5.2 효과 나눔 (D9) — `IInterruptClaimTarget`

| 호출 | 언제 | 무엇 |
|---|---|---|
| `ServerApplyInterruptSuccessEffect` | **첫 승인만** | 창 소비·그로기·제압 칸·섬광. 서버 창이 이미 닫혔어도 준다(D7) |
| `ServerRecordInterruptSuccess` | 승인마다 | 결과 화면 통계(`CombatStatsEvents.ServerCounterSucceeded`) |
| 시전자 스킬 `ServerInterruptSucceeded` | 승인마다 | 특성·튜토리얼 훅 |

- 구현 대상: 23호 · SpinnerBot · GauntletBot · WallBot · 스파링 봇 · 허수아비(허수아비는 통계 없음).
- **세션 중 대상은 피격으로 성공을 정하지 않는다**(`InterruptManager.ServerVerifiesClaims`). 오프라인(세션·매니저 없음)은 기존 피격 판정 그대로.

### 5.3 서버 간파 스킬 (`PlayerInterruptSkillBase.ResolveHit`)

- 앵커 오버랩은 **피해만**. 신고 결과를 기다리지 않는다(3단계의 RTT+0.1초 대기는 4단계에서 삭제).
- 그때까지 승인이 없으면 시도 슬로우를 빗나감으로 정리한다.
- 승인이 오는 순간(`ServerOnClaimVerdict`) 성공 기록, **첫 승인이면 성공 슬로우 사건**(전역 슬로우는 한 번).
- 이번 시전이 이미 승인된 대상에는 그 피해에 간파 표식을 싣지 않는다(방금 든 취약에 간파 넉백이 또 들어가지 않게).

### 5.4 서버 시도 슬로우 경로

- 승인 순간(`OnServerStart`) → `InterruptManager.TryPredictServerAttempt` → 대상 없음 = 슬로우 없음 / 약 = `AttemptWeak` / 강 = `Attempt`. 디렉터 사건 흐름은 그대로([interrupt-presentation.md §6.2](interrupt-presentation.md#62-사건-흐름)).
- 원격 오너면 서버 승인은 오너 누름보다 RTT/2 늦다 — 시도 슬로우도 그만큼 늦게 들어온다.
- 대시 중 재예측(옛 139-2 ②)은 없다. 한 번 정한 강·약은 그대로다.

## 6. 연출 cue (4단계)

오너 연출은 매니저 cue 로만 움직인다: `InterruptOwnerCueRouter`(매니저 안 순수 판단) → 디렉터 `IInterruptOwnerCueSink`(`InterruptProductionDirector.OwnerJudgeCues.cs`, 재생 `InterruptOwnerJudgeCuePlayer`).

| cue | 보내는 때 | 디렉터가 하는 일 |
|---|---|---|
| 시도(대상 없음/약/강) | 누른 프레임 다음 LateUpdate — 누른 시각 표본으로 `Predict` | 기존 `LocalOwnerAttempt`(강 = 오너 암전·빛 모임). 이 시도는 내 서버 시도 슬로우 목적으로 걷지 않는다(D4) |
| 판정 성공 | `Judge` 성공 — **신고보다 먼저** | 서버를 기다리지 않고 성공 단계(강이었으면 섬광 ②~⑥, 약·대상 없음이면 섬광 순간만). 뒤이어 오는 내 서버 성공 슬로우 Started 는 한 번 삼킨다 |
| 판정 실패 → 종료 | `Judge` 실패 | 시도 걷기(0.2초). 약이면 걷을 것이 없다 |
| 서버 거부 → 종료 | 거부 수신(그 시전자·대상·창 id 가 판정 성공 상태일 때만) | 이미 튼 성공 섬광까지 즉시 끔(상태 기계·섬광 진행기 리셋·월드 섬광 정지) |
| CC 끊김 → 종료(D5) | 판정 전 `SkillCut` | 대기 판정 버림 + 걷기 |
| 판정 버림 → 종료 | 판정 시각이 이력 밖 | 걷기 |
| 승인 목록 | 승인 배포 수신(내가 목록에 있고 같은 창) | D10 켬이면 나 말고 함께 성공한 캐릭터를 이번 암전에 남김(오너·보스는 시도 때부터) |

- **D10 스위치** `keepCoSucceededBright` — `NetworkManager.prefab › InterruptProductionDirector`, 기본 켬, `[DataTableIgnore]`. 각 피어 자기 값.
- 서버 성공 슬로우는 첫 승인 1회. 내가 첫 승인자가 아니면 내 서버 성공 슬로우는 안 온다(섬광은 내 판정 cue 로 이미 나왔다).
- 구경하는 팀원 화면은 기존 규칙(첫 승인의 서버 성공 슬로우 → 월드 섬광). 동시 성공자의 두 번째 섬광은 그 본인 화면에만.

## 7. QA 기록 (D12, 5단계)

- 코드: `InterruptQaRecord`·`InterruptQaTracker`·`InterruptQaCsv`·`InterruptQaSummary`·`InterruptQaRules`(`InterruptQaRecord.cs`) + `InterruptQaRecorder`(세션마다 하나, 매니저가 시작·끝).
- **파일**: `C:/UnityProject/QA/191interrupt-<yyyyMMdd_HHmmss>-<host|client><clientId>/`
  - `interrupts.csv` — 줄마다 즉시 덧붙임, UTF-8 BOM, 헤더 1줄. 줄 0 개 세션은 폴더를 안 만든다.
  - `summary.txt` — 세션 끝. 간파 수·예측(강) ↔ 판정(성공) 일치·어긋난 이유별·서버 결과별·거부 이유별 개수.
- 켜기: 매니저 칸 `qaRecordFile`(기본 켬, 에디터·개발 빌드만 — 릴리스는 꺼짐). 꺼도 기록 이벤트(`RecordCompleted`, 보정 랩용)는 난다.
- 각 피어가 자기 오너 간파를 적는다(MPPM 이면 피어마다 폴더 하나). 신고 뒤 서버 응답 제한 2초(`DefaultServerTimeout`), 판정 대기 제한 3초.

| 열 | 뜻 |
|---|---|
| `seq`·`wall_clock`·`role`·`local_client` | 번호·벽시계·host/client·이 피어 clientId |
| `caster_client`·`caster_id`·`character`·`mode` | 시전자 clientId·NetworkObjectId·캐릭터·melee/dash |
| `press_time`·`judge_time`·`judge_delay` | 누른 시각·판정 시각(서버 시각)·차이 |
| `target_id`·`window_id` | 대상·창 id |
| `prediction`·`prediction_reason`·`prediction_window`·`dash_blocked` | 예측(NoTarget/Weak/Strong)·실패 이유·예측한 창·대시 막힘(wall/unit/none) |
| `judge`·`judge_reason`·`judge_detail` | 오너 판정(NoTarget/Failure/Success/Cut/Dropped/CastRejected)·이유·끊김 사유 |
| `server`·`server_reason`·`server_judge_reason`·`approved_count` | 서버 결과(NotClaimed/ApprovedFirst/ApprovedSimultaneous/Rejected/NoResponse)·거부 이유·서버 Judge 이유·승인 수 |
| `server_attempt` | 서버 시도 슬로우 강·약 |
| `predict_judge_match`·`final_success` | 예측 강 ↔ 판정 성공 일치 · 최종 성공(판정 성공 ∧ 승인) |
| `mismatch_reason` | 아래 어긋난 이유 |
| `resolve_x`·`resolve_z` | 예측 판정 위치 |

**어긋난 이유**(`InterruptQaMismatch` — 예측 → 판정 → 서버 중 처음 어긋난 단계):
`None` 일치 · `Range` 범위 · `Angle` 각도 · `WindowClosed` 창 닫힘 · `CrowdControl` CC 끊김 · `FirstCome` 선착순 · `ServerRejected` 서버 거부 ·
`BossMoved` 보스 이동(약·대상 없음 예측 → 판정 성공) · `WindowChanged` 창 시간표 변경 · `TargetState` 대상 상태 변화 · `CasterCut` 시전자 쓰러짐·사망·대시 · `NotDesignated` 지목자 아님 · `Dropped` 판정 버림 · `CastRejected` 시전 거부 · `NoResponse` 응답 없음 · `Other`.

- 완료 조건 판단: 솔로에서 어긋남이 `BossMoved`·`CrowdControl`·`WindowChanged` 말고 거의 없어야 한다. MPPM 에서 `ServerRejected` 비율이 높으면 §5.1 칸을 넓힌다.
- **QA 봇 간파(164)는 이 브랜치에 코드가 없다 → 할 일 193 으로 분리.** 봇이 새 경로(오너 판정 → 신고)를 타게 하는 것이 193 범위다.

## 8. 보정 랩 용도 변경 (`InterruptCalibrationLab`, 5단계)

옛 139 랩(오너·서버 예측·재예측·서버 판정 비교)을 **"예측 vs 판정 vs 서버"** 기록으로 바꿨다. 씬·질문 창·Y/N/Esc 흐름은 그대로다. 상세는 [dash-interrupt.md §10.2](dash-interrupt.md#102-예측-보정-랩139--interruptcalibrationlab).

- 표본 = QA 기록 한 줄(`InterruptQaRecorder.RecordCompleted`) + 오너 판정 순간(`InterruptManager.OwnerJudged`)의 허수아비 창 설정·실제 창 상태·대상 거리·각도.
- CSV 열 = 랩 앞 칸(`seq`·`real_time`·`window_mode`·`window_open_len`·`window_closed_len`) + **QA 열 전부**(QA 의 `seq` 는 `qa_seq`) + 랩 뒤 칸(`target_distance`·`target_angle`·`actual_window_open`·`actual_open_elapsed`·`actual_remaining`·`user_answer`).
- 일치율 4줄: 예측 ↔ 답 · 판정 ↔ 답 · 서버 ↔ 답 · 예측 ↔ 판정. summary 에 어긋난 이유별 개수.
- 기록 폴더는 그대로 `C:/UnityProject/QA/139calib-<시각>/samples.csv`.
- 판정 범위 기즈모(156) 시도 잔상은 `OwnerPredicted`·`ServerAttemptPredicted` 를 구독한다. '예측기 관측 기록 초기화' 버튼은 관측 중앙값과 함께 없어졌다.

## 9. 창 여닫는 곳 목록 (1단계)

### 9.1 23호 (`TwentyThreeBoss`) — 전부 `SetCounterWindow` 한 곳 → `ReportCounterWindowToManager`

`SetCounterWindow` 는 복제값이 바뀔 때만 알린다. 이미 열린 채 길이를 넘겨 다시 열면(착지 창 도중 다음 콤보 타 등) 새 창으로 알린다.

| # | 위치 | 열림/닫힘 | 닫힘 예정 |
|---|---|---|---|
| 1 | `StartAttack` — 창 여는 공격(돌진 등, 잡기 제외) | 열림 | `CounterWindowDuration`(= 선딜 게이트 타이머) · 공격 시작 = StartAttack 시각 |
| 1' | 같은 곳 — 창 안 여는 공격·취약/제압 중 | 닫힘 | — |
| 2 | `TryReleaseCounterAttack` — 게이트(타이머 끝 ∧ 애니 Hit) 발사 | 닫힘 | 애니가 늦으면 **늦은 닫힘** 갱신 배포 |
| 3 | `ResetCounterWindup`(= `AbortAttackChain` — 상태 이탈·사망·체인 타임아웃·디스폰·간파 성공) | 닫힘 | 조기 |
| 4 | `PerformAttackHit` — 잡기 외 공격의 히트 | 닫힘 | — |
| 5 | `PlayStateAnimation` — 서버에서 Attack 을 벗어남 | 닫힘 | — |
| 6 | 잡기 `AttachGrabbed`(붙잡은 뒤) | 열림 | **길이 모름** |
| 7 | 잡기 `TickGrabHold` — 잡힌 대상 소멸 | 닫힘 | — |
| 8 | 잡기 `AdvanceGrabSlam` — 내려치기 사이 쓰러짐 / 마지막 타 직전 | 닫힘 | — |
| 9 | 잡기 `ReleaseGrabbedPlayer` | 닫힘 | — |
| 10 | `BeginCharge` — 차징 시작 | 닫힘 | — |
| 11 | `BeginRage` — 레이지 시작 | 닫힘 | — |
| 12 | `OpenLandingCounterWindow` — 점프 착지 창 | 열림 | `landingCounterWindow` |
| 13 | `TickLandingCounterWindow` — 착지 창 만료 | 닫힘 | 제때 |
| 14 | 레이드 `OnRaidGimmickCounter` — 기믹 간파 성공 | 닫힘 | 조기 |
| 15 | 레이드 `EndRaidGimmick` | 닫힘 | — |
| 16 | 릴레이 `Prepare` 마지막 `relayCounterWindow` 초 | 열림 | 남은 준비 시간 · 지목자 없음(명단은 서버만) |
| 17 | 릴레이 `Prepare` 끝 | 닫힘 | 제때 |
| 18 | 연쇄 간파 `ChainMark` 끝 → `ChainWindow` | 열림 | `chainCounterWindow` · **지목자 = 지목 대상 clientId** |
| 19 | 연쇄 간파 창 도중 지목 대상 쓰러짐 / 창 끝 | 닫힘 | 조기 / 제때 |
| 20 | 밀어내기 `PnCharge` 송전탑 전부 파괴 → 무력화 | 열림 | `pushNorthStunTime` |
| 21 | 밀어내기 `PnStun` 끝 | 닫힘 | 제때 |

- 공격 시작 시각: 공격 상태(레이드 기믹 밖)면 `StartAttack` 시각, 기믹 창은 열림 시각과 같다.
- 간파 불가 상태: 서버 = `CounterBlockedByState`(취약·제압), 클라 = 취약 복제값만(§12).

### 9.2 중간보스 (`MonsterCounterWindow` — SpinnerBot·GauntletBot·WallBot)

`MonsterCounterWindow.RecordTransition` 한 곳. 열림 = 저작 길이 ÷ 공격속도, 만료 = 제때, `TryConsumeInterrupt`·`Close` = 조기. 공격 시작 시각은 모른다(= 열림 시각).

### 9.3 로비 스파링 봇 (`TrainingSparringBot`)

`MonsterCounterWindow` 경로 그대로. 훈련 창 연장(T31)은 `Counter.Open(windup)` 으로 닫힘 예정 = 예고 길이를 알린다(판정 타이머 무변경). 간파 불가 = 서버 취약·제압, 클라 복제값.

### 9.4 허수아비 (`TrainingDummyInterrupt`)

스폰 시작 상태 + `OnStateChanged`(서버)에서 알린다. 무기한 창이라 **길이 모름**. 닫힘 = 간파 성공·디버그 랩 게이트.

## 10. 삭제한 옛 경로 (5단계)

| 옛것 | 대신 |
|---|---|
| `DashInterruptPredictor`(근접·대쉬 공용 예측기) · `InterruptAttemptPrediction`(`AtPress`·`ForReprediction`) | `InterruptManager.Predict`(누른 시각 표본) |
| 창 길이 추정 — 관측 중앙값·**0.4초 규칙**(`StaleWindowMaxTimeToResolve`)·`InterruptWindowLengthResolver`·`TwentyThreeInterruptWindowLength`·로컬 창 길이 해석 | 서버 창 시간표(닫힘 예정·조기·늦은 닫힘) |
| 오너·서버 **대시 중 재예측**(약 → 강, `TickDashInterruptReprediction`·`TickServerDashReprediction`) | 없음 — 대시 끝은 공용 스윕으로 처음부터 정확히 |
| 오너 연출 약 → 강 올리기(`LocalOwnerUpgrade` → `InterruptOwnerPresentation.UpgradeToStrong`) | 없음 — 약 예측 → 판정 성공이면 성공 섬광만(D4) |
| 오너 즉시 예측(`PlayerSkillController.PredictOwnerInterrupt`) | 매니저 시도 cue |
| 139 계측 훅(`CalibrationOwnerPredicted`·`CalibrationServerPredicted`·`…Repredicted`·`CalibrationServerJudged`) | `OwnerPredicted`·`OwnerJudged`·`ServerAttemptPredicted`·QA `RecordCompleted` |
| 서버 `ResolveHit` 앵커 오버랩으로 성공 결정 | 신고 검증(§5). 오버랩은 피해만 |
| 3단계 `ResolveHit` RTT+0.1초 판정 대기(4단계) | `ServerOnClaimVerdict` 시점 기록 |

- 남은 것: `InterruptWindowReplicaTracker` 는 복제 창 열림 → 로컬 선착순 장부(143-2) 초기화만 한다. `DashInterruptPrediction.cs` 에는 `InterruptAttemptForecast`(3상태 enum)·`InterruptTargetGeometry` 만 남았다.
- 옛 테스트(`DashInterruptPredictionTests`·`InterruptWindowLengthResolverTests`·`TwentyThreeInterruptWindowLengthTests`·`InterruptCalibrationSample22Tests` 등)는 삭제.

## 11. 테스트

메뉴 `Tools/Tests/간파 판정·검증 EditMode 테스트 실행`(`InterruptJudgeEditModeTestRunner`):
`InterruptManagerSnapshotTests`(이력 보간·시간표·조기/늦은 닫힘·시각 변환) · `InterruptJudgeCalculatorTests`(범위·각도·창 경계·대시 막힘·공격속도) ·
`InterruptClaimServerTests`(허용 오차 경계·동시 0.05초·효과 1회) · `InterruptOwnerJudgeCueTests`(cue·D10) · `InterruptQaRecordTests`(어긋난 이유·CSV·요약).
보정 랩은 `Tools/Tests/간파 랩 EditMode 테스트 실행`.

## 12. 알려진 한계

- **호스트 유리**: 선착순은 서버 도착 기준이라 지연 0 인 호스트가 클라보다 유리하다.
- **원격 오너 슬로우 늦음**: 전역 슬로우(시도·성공)는 서버 승인 시점이라 원격 오너 화면에선 섬광보다 RTT/2 늦게 들어온다.
- **D10 은 원격에서 잘 안 보인다**: 암전이 섬광 순간(6프레임)에 꺼진 뒤 승인이 도착하는 일이 많다. 눈에 띄는 건 주로 호스트·저지연.
- **클라 23호 제압 복제값 없음**: 클라는 간파 불가 상태 중 취약만 안다. 제압 중엔 창이 안 열려 판정에는 영향 없다.
- **릴레이 지목자 없음**: 릴레이 창은 명단이 서버에만 있어 지목자 없이 배포한다(누구나 성공으로 예측·판정 → 서버 대상 규칙이 거른다).
- **시전자 위치 지연**: 오너 권한 이동이라 서버 이력의 시전자 위치가 보간 지연만큼 늦다. 시각 여유 안에서만 따라잡는다 — 대쉬 간파 끝에서 거부가 늘 수 있다(QA 비율로 판단).
- **예측은 보스 이동을 안 본다**: 누른 뒤 보스가 움직이면 `BossMoved` 로 남는다. 보정은 D12 비율을 보고 정한다.
- **창 닫힘 예정은 게임초 길이**: 슬로우가 끼면 실제 닫힘이 조금 어긋날 수 있어 닫힘은 항상 배포한다.
- **원격 플레이어의 대시·CC 칸**은 그 피어에서 보이는 값이다 — 판정은 오너 시전자만 쓰므로 문제 없음.
- **QA 봇 간파 미연결**: 할 일 193.

## 13. 사용자 Play 확인 (1~5단계 묶음)

솔로(`InterruptCalibrationLab`·보스 씬):
- 🟧 근접 간파: 강 연출이 나오면 성공, 약 연출이면 실패하는지(약인데 성공이면 섬광만 나오는지).
- 🟧 대쉬 간파: 벽·유닛에 막히는 대시에서 강 연출이 잘못 나오지 않는지. 막힌 자리 기준으로 판정되는지.
- 🟧 공격속도 버프 직후 간파: 판정 시점이 클립 Hit 과 맞는지.
- 🟧 강 예측 → 판정 실패(누른 뒤 보스가 비킴 등)면 암전이 바로 걷히는지.
- 🟧 맞아서 밀리거나 잡히면(넉백·끌기·잡기) 간파 연출이 바로 끝나는지. 일반 피해로는 안 끝나는지.
- 🟧 23호 각 패턴(돌진·후속 돌진·점프 착지·잡기·릴레이·연쇄·밀어내기 무력화)에서 창이 열린 동안 간파가 성공하는지 — `logWindows` 를 켜면 열림·닫힘이 콘솔에 남는다.
- 🟧 중간보스·스파링 봇·허수아비 간파가 그대로 되는지. 성공 시 그로기·제압 칸·결과 화면 간파 수가 1번씩 오르는지.
- 🟧 QA 기록: `C:/UnityProject/QA/191interrupt-…/interrupts.csv`·`summary.txt` 가 생기고, 어긋난 이유가 `BossMoved`·`CrowdControl`·`WindowChanged` 말고 거의 없는지.
- 🟧 보정 랩: 질문 창·CSV(`139calib-…/samples.csv`)에 QA 열이 붙고 일치율 4줄이 나오는지. 기즈모 시도 잔상이 남는지.

MPPM 2~3인:
- 🟧 클라에서 성공한 간파가 서버에서 거부되지 않는지(`server` = Approved, `ServerRejected` 비율).
- 🟧 둘이 거의 동시에 성공하면 두 화면 모두 섬광, 제압 칸·그로기는 1번만, 결과 화면 간파 수는 각자 +1.
- 🟧 0.05초보다 늦게 성공한 쪽은 선착순 거부 → 그 화면 섬광이 즉시 꺼지는지.
- 🟧 D10: 동시 성공 때 함께 성공한 캐릭터가 내 암전에서 밝게 남는지, 스위치를 끄면 어두워지는지(호스트 화면에서 보기 쉽다).
- 🟧 구경하는 팀원 화면: 첫 승인 섬광·슬로우만 나오는지.
- 🟧 늦게 들어온 클라: 열린 창을 받아 간파가 되는지.
