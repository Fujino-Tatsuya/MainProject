# 행동 입력 거부 피드백 — 쿨타임·차단 효과 이벤트와 사운드

> 할 일 44 2차(2026-10-15). 스킬을 쿨타임 중에 눌러 거부됐을 때 오너 화면에서만 효과음·UI 를 낼 수 있게 한 연결 지점.
> 3차(2026-10-15): 차단 효과(침묵·속박·약화) 거부 이벤트를 더하고, 선입력 버퍼가 거부 사유별로 입력을 보관/버리는 규칙을 확정했다(§0).

## 0. 거부 사유별 선입력 버퍼 처리

Idle/Move 에서 입력(이번 프레임 눌림 또는 선입력 버퍼)으로 평타·대시·스킬·인터럽트를 시작하지 못했을 때의 처리.
판정 순서는 위에서 아래(먼저 걸린 사유 하나만 적용 — 한 입력에 이벤트는 많아야 하나).

| 사유 | 판정 | 처리 | 이벤트 |
|---|---|---|---|
| 바쁨 — 행동 중(공격·스킬·대시·넉백 등 Idle/Move 가 아닌 상태)·요청 응답 대기 | 상태가 Idle/Move 아님 / `PlayerSkillController.IsBusy` / 평타 `TryStart` 거부(과열 아님) | **0.2초 보관** — 풀린 첫 틱에 다시 판정 | 없음 |
| CC — 행동 불능 상태이상 | 그 행동의 차단 판정이 참 + `StatusEffectController.IncapacitatingEffects`(기절·에어본) 보유 | **0.2초 보관** | 없음 |
| 차단 효과 — 그 행동만 막는 상태이상(봉인) | 그 행동의 차단 판정이 참 + 행동 불능 CC 없음(아래 표) | **즉시 버림** | `PlayerStateController.ActionBlockedByEffect` |
| 자원 부족 — 스킬 쿨타임 | `IsBlockedByCooldown` | **즉시 버림** | `PlayerSkillController.CooldownRejected` |
| 자원 부족 — 대시 충전 없음·거너 과열 | `IsOutOfCharge` / `IsOutOfResource` | **즉시 버림** | 없음 |
| 그 밖 — `CanUse` 불충족·조준 진입 실패·빈 슬롯·비접지·우클릭 인터럽트 조건(조준 방향 없음 등) | 위에 안 걸린 거부 | **즉시 버림** | 없음 |

행동별 차단 판정(`StatusEffectController` 의 차단 테이블)과 그 결과:

| 행동 | 차단 판정 | CC(보관) | 차단 효과(버림 + 이벤트) |
|---|---|---|---|
| 스킬(Q·E·R·우클릭 스킬) | `BlocksSkill` | 기절·에어본 | 침묵 |
| 평타 | `BlocksAttack` | 기절·에어본 | (현재 없음) |
| 대시 | `BlocksMovement` | 기절·에어본 | 속박 |
| 인터럽트 상태(우클릭에 스킬 미배정) | `BlocksInterrupt` | 기절·에어본 | 속박·약화 |

- 둘이 겹치면(기절 + 침묵) CC 가 이긴다 — 보관했다가 기절이 풀린 첫 틱에 침묵이 남아 있으면 그때 버리고 이벤트 1회.
- 넉백·잡기·구속(Restrained)·연출은 상태 전이라 '바쁨' 행으로 처리된다(사망·연출 잠금 진입은 버퍼를 비운다).
- 상태이상·쿨타임 판정은 시작 요청 **전에** 한다 — 걸리면 원격 클라도 요청을 보내지 않는다(복제된 상태이상 목록 기준, 서버 최종 검증은 그대로).
- 판정 순서: 스킬 = 빈 슬롯 → 바쁨 → 상태이상 → 쿨타임 → `TryUse`. 대시 = `TryBeginPredictedDash` 실패 후 상태이상 → 충전 없음 → 그 밖. 평타 = 상태이상 → `TryStart` → 과열.
- 코드: `PlayerActionAttempt`·`PlayerActionRejectPolicy`(`PlayerInputBuffer.cs`), `PlayerStateController.RejectByEffect`·`ClassifyDashReject`, `PlayerStateBase.TryConsumeActionInput`·`TryStartSkill`.
- 테스트: `PlayerInputBufferTests` — `Resolve_*`, `ClassifyEffectBlock_*`, `CooldownReject_*`, `EffectBlock_*`, `OtherReject_*`.

## 1. 이벤트

### 1.1 쿨타임 거부 — `PlayerSkillController.CooldownRejected`

- `event Action<PlayerSkillSlot>`, 인자 = 거부된 슬롯(`Main`·`Sub`·`Interrupt`·`Ultimate`).
- **오너 로컬 전용**: 그 플레이어를 조작하는 피어(오프라인·호스트 자기 캐릭터·원격 클라 자기 캐릭터)에서만 난다. 서버·다른 피어에는 없다. 사운드 전용 RPC 도 없다.
- **입력 1회 = 1회**: 쿨타임이면 그 입력을 버퍼에서 버리고 한 번 낸다. 버렸으므로 다음 틱에 같은 입력으로 다시 나지 않는다.
- **안 나는 경우**: 바쁨(입력은 0.2초 들고 있다가 풀린 첫 틱에 판정), 슬롯 미배정, CC·차단 효과(차단 효과가 먼저 판정돼 그쪽 이벤트가 난다)·`CanUse` 거부, 조준 중 억제 입력.
- 판정 = 이 피어의 쿨타임 장부(`IsBlockedByCooldown` — 원격 오너는 서버가 미러한 HUD 장부). HUD 가 쿨타임으로 보이면 시전 요청을 보내지 않는다(조준 진입 게이트와 같은 조기 차단, 최종 검증은 그대로 서버).
- 원격 클라에서 장부가 드물게 어긋나 요청이 서버에서 거부되면 이벤트는 나지 않는다(의도 — 그대로 둔다).

### 1.2 차단 효과 거부 — `PlayerStateController.ActionBlockedByEffect`

- `event Action<PlayerBufferedInput, StatusEffectType>` — 인자 = 거부된 입력 종류(`Attack`·`Dash`·`SkillMain`·`SkillSub`·`SkillInterrupt`·`SkillUltimate`), 그때 걸려 있던 차단·CC류 상태이상(`StatusEffectImmunityPolicy.BlockedCrowdControl` 범위, 예: `Silenced`). UI 에서 "침묵" 같은 문구를 고를 때 쓴다.
- 평타·대시·스킬·인터럽트 공용 하나다. 쿨타임 이벤트와 **따로** 있고 같은 입력에 둘이 겹쳐 나지 않는다(차단 효과를 먼저 판정). 기존 `CooldownRejected` 는 그대로다.
- 오너 로컬 전용(`Player.IsInputSource`), 입력 1회 = 1회. 기절·에어본(행동 불능 CC) 중에는 나지 않는다.
- 구독: 같은 플레이어의 `PlayerStateController` 에 `ActionBlockedByEffect += ...`.

> 공용 이벤트로 둔 이유: 차단 효과로 거부되는 행동이 스킬(침묵)만이 아니라 대시(속박)·인터럽트(속박·약화)에도 있다. 스킬 전용으로 두면 대시 거부 피드백을 따로 또 만들어야 한다.
> 쿨타임은 스킬에만 있는 자원이라 기존 스킬 전용 이벤트를 그대로 둔다.

## 2. 사운드 지정

| 거부 | 컴포넌트 › 칸 | 단위 |
|---|---|---|
| 쿨타임 | `PlayerSkillController` › **`Cooldown Reject Sound Key`** | 슬롯 공용 하나 |
| 차단 효과 | `PlayerStateController` › **`Effect Blocked Sound Key`** | 평타·대시·스킬·인터럽트 공용 하나 |

- `Player.prefab`(base) 의 위 칸에 `Sound` 시트 `key` 를 적는다. Variant 는 base 를 따른다.
- **비우면 무음**(기본값). 키가 있으면 `AudioManager.Instance.Play(key, 플레이어 위치)` — 2D 엔트리는 위치를 무시한다.
- 사운드 추가 순서는 [data-table.md §6 사운드 키](data-table.md#사운드-키--사운드-동기화): `Sound` 시트에 행 작성(예: key `SFX_SkillCooldownReject`·`SFX_ActionBlocked`, audioType `UI` 또는 `SFX`, is3D `0`) → `Tools/Data/사운드 동기화` → 위 인스펙터 칸에 같은 키.
- 슬롯·행동마다 다른 소리가 필요하면 이벤트 인자로 구독하는 쪽에서 나눈다.

## 3. 선입력 버퍼 진단 로그

0.2초 창이 실제 플레이에서 얼마나 자주 아슬아슬하게 놓치는지 숫자로 보는 관측 전용 로그. 버퍼 규칙은 바꾸지 않는다.

- 범위: 오너 클라만. `UNITY_EDITOR || DEVELOPMENT_BUILD || DEV_HOTKEYS` 일 때만 컴파일된다(훅은 `[Conditional]` — 심볼이 없으면 호출째 사라진다).
- 토글: `PlayerInputBufferDiagnostics.Enabled`(기본 켜짐). 에디터는 메뉴 `Tools/Debug/선입력 버퍼 진단 로그`(EditorPrefs 에 남음).
- 출력: 콘솔 `[InputBuf]` 한 줄씩 + CSV. 에디터 = 프로젝트 `Logs/InputBuffer-<시각>.csv`, 빌드 = `Application.persistentDataPath`. Play 종료 때 입력별 개수·분포 요약과 CSV 경로를 콘솔에 남긴다.
- 이벤트: `press`·`buffered`(누른 프레임에 실행 못 함 + 사유)·`consumed`(즉시/버퍼 실행 + press→실행 ms)·`expired`·`dropped`(사유)·`overwritten`·`actionable`(바쁨 → Idle/Move).
- 핵심 숫자: `actionable` 의 **놓친 간격** = 실행 가능해진 시각 − 만료 시각(press + 유지 시간), **press→actionable** = 그 입력이 살아남으려면 필요했던 유지 시간. 바쁜 동안 만료된 입력만 센다.
- 알려진 한계: 원격 클라 오너의 근접 평타 콤보 예약 입력은 버퍼에서 지우지 않아(창을 서버만 안다) 콤보 중 누른 LMB 가 `expired` 로 잡힐 수 있다. Idle/Move 안에서 요청 응답만 기다리는 경우(스킬 요청 대기)는 `actionable` 로 보지 않는다.
- 코드: `PlayerInputBufferDiagnostics.cs`(추적기·집계·세션), 훅은 `PlayerInputReader`(기록·소비·Resolve·비움)·`PlayerStateController.ChangeState`/`SetState`. 테스트: `PlayerInputBufferDiagnosticsTests`.
