# PLAN — 23호 전기 장판 · 웰즈 자폭 드론 (2026-10-01 · 경석 · ✅ 승인 10-02)

> 기획 원본: [boss-electric-floor.md](Docs/design/boss/boss-electric-floor.md) · [wells-suicide-drone.md](Docs/design/boss/wells-suicide-drone.md)
> (레포 밖 `Re_C_*.md` 사본, 2026-09-30 협의 반영본. 도식 이미지는 없어 텍스트가 기준 — 팀장 OK).
> 관련: [PLAN-boss-backlog.md](Docs/history/PLANS/PLAN-boss-backlog.md) B5(드론)·B6(고유 게이지)·B7, [PLAN-boss-fsm.md](Docs/history/PLANS/PLAN-boss-fsm.md).

## 1. 목표 / 완료 조건

- 23호 보스전에 **일반 전기 장판**(교차 2줄, 약 6.6초 주기)과 **송전기 기믹 전기 장판**(A/B 교대)을 넣는다.
- 웰즈가 **자폭 드론**(표식 3초 추적 → 위치 고정 1초 → 낙하 → 원형 범위 피해, 23호도 맞음)을 쓴다.
- 보스 상태(취약·과충전·잡기·송전기·제압·사망)별 처리가 기획서 표대로 동작한다.
- **완료 = MPPM 2인**에서 호스트·클라 양쪽에 예고·연출이 보이고, 피해는 서버에서 1회만 들어간다.
  EditMode 테스트(패턴 선택 규칙·타일 마스크) 통과.

## 2. grill 확정 (2026-10-01 팀장)

| 항목 | 결정 |
|---|---|
| 타일 범위 | 벽 안쪽 **28×28, 칸 4m**, **방 로컬 좌표**(방은 90° 단위로 회전 배치됨 — `ResolveArena` 와 같은 축). 드론 원 지름 0.5칸 = **2m**(반경 1m) |
| 제압 범위 | **체크형(Flags)으로 연다**: `BossPauseCondition { Suppress, PylonGroggy, … }` — 장판·드론이 각자 "어느 상태에서 멈출지" 를 인스펙터에서 고른다. 기본값 = 둘 다. 그로기 종류가 늘면 enum 끝에 비트 추가 |
| 송전기 중 근접 오라 | `TickChargeAura` **유지** + A/B 장판 **추가** |
| 연출 | **임시 연출 + 민경 훅** — 예고 = 기존 `AoeTelegraph`(Square/Circle 차오름), 전기·폭발 = 임시 파티클, 크로스헤어 = 단순 메시/스프라이트. 프리팹 슬롯을 비워 교체 가능하게 |

## 3. 기본값으로 정한 것 (이견 있으면 리뷰 때)

- **쓰러짐** = `PlayerLifeState != Alive`(코드에 별도 '쓰러짐' 없음 — DeadPresentation/Soul 이 그 자리). 완전 사망 = PermanentDead.
- **붙잡힘** = 23호가 **실제로 쥔** 대상(`_grabbed`). 끌려가는 중(`_pulledPlayers`)은 피해 대상 — 기획서 6.4 그대로.
- **무적** = `PlayerInvulnerability.IsServerInvulnerable` — `CanApplyHealthDamage` 가 이미 막으므로 별도 처리 없이 "게이지 인원 제외" 판정에만 쓴다.
- **보스 고유 게이지** 미구현(B6) → 장판 적중 인원만 이벤트로 내보내고(`ElectricFloorHit(int count)`) 구독자는 없음.
- **피해량(임시)**: 장판 20 · 드론→플레이어 25 · 드론→23호 = SO 값(초기 300, 23호 HP 대비는 리뷰 때 조정). 고정 수치로 시작, 최대 체력 비례는 옵션 필드만.
- **전투 시작 시점** = 웰즈 공격 주기가 시작되는 시점과 같게(입장 연출 착지 후). 장판 첫 예고 4초, 드론 첫 공격 5초.
- 웰즈의 기존 주기(`bombThrowInterval 6`, `OnWellsAttackCycle` 은 지금 경고만)는 **드론 전용 타이머로 대체**(첫 5초 / 종료 후 7초 / 재개 최소 2초).
- 화면 기준 `(1,1)` = 위 모서리 → 방 로컬의 한 모서리로 고정. 패턴(A 4모서리·B 양 축)이 대칭이라 방향 선택은 결과에 영향 없음. Gizmo 로 번호 확인.

## 4. 구조

**권한**: 전부 **서버 판정**. 선택·타이머·피해는 서버, 클라는 RPC 로 받은 것만 재생(기존 `ShowJumpTelegraphClientRpc` 패턴).

| 파일(신규) | 역할 |
|---|---|
| `Monster/Boss/BossTileGrid.cs` | 순수 C#. 방 로컬 ↔ 타일 `(r,c)` ↔ 49비트 마스크(`ulong`). 마스크 빌더: 줄·축·사각(A-1~4)·안쪽 5×5·홀수 줄 4개 |
| `Monster/Boss/BossElectricFloorPatterns.cs` | 순수 C#. 패턴 선택 규칙(직전 줄 제외 · A 직전 조합 제외 · B 방향 교대 · A/B 교대 · 리셋). **EditMode 테스트 대상** |
| `Monster/Boss/BossElectricFloor.cs` | 23호에 붙는 NetworkBehaviour. 일반/기믹 모드 스케줄러, 발동 순간 발 위치 판정 → `ReceiveAttack`. `ShowFloorClientRpc(mask, warnTime)` · `FireFloorClientRpc(mask)` · `ClearFloorClientRpc()` |
| `Monster/Boss/WellsDroneAttack.cs` | 23호(=웰즈 상태 소유자)에 붙는 NetworkBehaviour. 대상 선정·추적·고정·낙하·피해. `BeginMarkClientRpc(targetNetId)` · `LockClientRpc(pos)` · `CancelClientRpc()` |
| `Monster/Boss/BossPauseCondition.cs` | `[Flags]` enum + 23호 현재 상태 → 비트 판정 헬퍼 |
| `Effects/…/ElectricFloorView.cs`, `DroneView.cs` | 클라 로컬 연출(타일별 `AoeTelegraph` 풀 · 전기 파티클 · 크로스헤어 · 드론 모델 낙하). **드론은 NetworkObject 아님**(부술 수도 밀 수도 없는 연출 전용 — 기획서 8) |
| 데이터 | `BossElectricFloorDataSO` · `WellsDroneDataSO` 신규, `BossDataSO` 에서 참조(머지 충돌 줄이려 분리) |

**드론 프리팹**: `DRONE.fbx`(Generic, 내장 테이크 Hover·DashStart/Loop/End·RotorSpin 등)로 연출 프리팹 신규. 머티리얼은 Flat Kit 변환 대상 목록에 추가.

**23호에서 바꾸는 곳(최소)**: `OnWellsAttackCycle`(:3624) 경고 제거 · `BeginCharge`/충전 결과(:3832-3857) · `EnterSuppress`/`TickSuppressExit` · `EnterPylonGroggy` · 사망 — 각 지점에서 두 컴포넌트에 `OnBossStateChanged` 한 줄씩 알린다. 판정 로직을 23호 본체(5.5천 줄)에 넣지 않는다.

## 5. 상태별 처리 (기획서 표 → 구현)

| 보스 상태 | 장판 | 드론 |
|---|---|---|
| 취약 · 과충전(Rage) · 잡기 | 그대로(주기·직전 줄 기록 유지) | 그대로 |
| 송전기 충전 시작 | 일반 장판 제거 → A/B 시작(첫 그룹 50%) | 타이머 정지 + 진행 중 공격 제거 |
| 예고 중 송전기 전멸 | 현재 예고 제거, 공격 안 함 · B 1단계 뒤 전멸이면 2단계 생략 | — |
| 충전 제한시간 종료 | 예고·VFX 제거 → 실패 처리 | — |
| 충전 종료(성공/실패) | A/B 기록 리셋, **일반 장판 4초 뒤 재개** | 멈춘 타이머부터 재개(최소 2초) |
| 제압(Flags 해당 상태) | 예고·VFX 제거, 중지 → 종료 후 4초부터 | 정지 + 제거 → 종료 후 재개(최소 2초) |
| 사망 | 전부 제거 | 전부 제거, 직전 대상 기록 리셋 |

## 6. 단계 (각 단계 끝에 커밋)

> 진행(10-02): S0 `8b4ca4cf` ✅ · S1 `2a2105a7` ✅(EditMode 8/8) · S2~S5 `9cfa5acb` ✅ 구현(컴파일만 확인) · S6 교차검증 → 팀장 Play/MPPM.
> **구현 중 바뀐 점**: ① 두 컴포넌트는 NetworkBehaviour 가 아니라 23호가 스폰 때 `AddComponent` 하고 ClientRpc 를 중계(프리팹 NB 구성 불변).
> ② 데이터 SO 는 `BossDataSO.electricFloor/wellsDrone` 에서 참조(비면 기본값). ③ 23호 상태 알림 대신 **매 프레임 폴링**
> (`IsChargeGimmickActive` · `ActivePauseConditions` · `IsFightActive`) — 진입·이탈 지점을 23호에 흩뿌리지 않으려고.
> ④ Flags 에 `CounterGroggy`(간파 성공 그로기, 기본 꺼짐 — 기획상 취약은 정상 작동) 추가. ⑤ 드론은 웰즈 주기(`OnWellsAttackCycle`)가 아닌 자기 타이머.

- **S0** 기획서 2종 `Docs/design/` 복사 · 이 PLAN 승인 · CONTEXT 작업 세션 갱신.
- **S1** `BossTileGrid` + `BossElectricFloorPatterns` + EditMode 테스트(교차 13칸 · 5×5=25 · 홀수 4줄=28 · A 2개 겹침 1회 · 직전 줄/조합 제외 · B 방향 교대).
- **S2** 일반 장판: 스케줄러 · 발 위치 판정 · RPC · 임시 연출 · 방 Gizmo(타일 번호). Play 1인.
- **S3** 송전기 A/B: 충전 시작/전멸/시간초과/종료 연결 · 오라와 공존 확인.
- **S4** 자폭 드론: 대상 선정(유효 인원·직전 제외) · 크로스헤어 추적 · 고정 · 낙하 · 플레이어+23호 피해 · 추적 중/고정 후 무효 처리 차이.
- **S5** `BossPauseCondition` Flags 연결(제압·그로기) · 사망 정리.
- **S6** MPPM 2인 검증(아래) · CONTEXT/lessons/decisions 정리 · 푸시.

### 6-1. 팀장 Play 피드백 반영 (10-02)

- 드론 범위 0.5칸 → **1.25칸**(2.5배) · 드론 모델 **2배** · 크로스헤어 **점멸 없음**(줄다가 멈춤 → 고정 순간 사라짐 → 드론 등장·낙하) · 연출 값 SO 노출 (`9db05813`).
- 드론→23호 피해 300 → **120** · **송전기 기믹 시작 = 점프 출발**(장판 ChargePrep 로 정리, A/B 는 착지 후 / 드론 정지·취소) (`7fa7d382`).

### 6-2. 보류 — 장판 패턴 모양 SO 편집 (10-02 팀장: "지금은 확인만")

- **가능**: 마스크가 `ulong` 하나라 SO 에 저장 + 7×7 토글 격자 인스펙터로 편집 가능. 격자는 **7×7 고정**(팀장).
- 안 A(추천, 반나절~하루): 규칙은 코드(일반 교차 · A 프리셋 중 2개 · B 1→2단계 교대), **모양만** 데이터 — A 프리셋 N개 추가·수정, B 1단계 1개 · 2단계 한 쌍.
- 안 B(하루+): 그룹·단계·후보·뽑는 수·직전 제외까지 데이터로 조립. 기획 규칙과 어긋날 여지 큼.
- 착수 시: `BossElectricFloorPatterns` 의 정적 마스크를 SO 주입으로 바꾸고, EditMode 테스트는 SO 값(칸 수)으로 검증.

## 7. 검증

- EditMode: S1 테스트 전부.
- Play/MPPM 2인 체크리스트 = 기획서 §11(장판) · §15(드론) 항목 그대로. 추가로:
  - 클라 화면에서도 예고·전기·크로스헤어·드론이 보이는가(RPC 누락 = 호스트만 보이는 사고).
  - 방 회전 배치(90°)에서 타일이 바닥과 맞는가 — Gizmo 로 확인.
  - 드론이 23호를 맞힐 때 간파·취약 판정을 타지 않는가(`isInterruptAttack=false`, 비플레이어 출처 → 제압 ×1.2 미적용).

## 8. 리스크

- **23호 상태 진입·이탈 지점 누락** → 장판이 그로기 중에 계속 나오는 사고. 상태 알림을 한 함수로 모으고, Gizmo/로그로 현재 모드 표시.
- **보스 사망 뒤 장판 잔존**(PLAN-boss-fsm.md:442 에 적힌 위험) → 사망 시 서버 정리 + `ClearFloorClientRpc`.
- **발 위치 판정 vs 연출 경계 불일치** → 칸 경계를 타일 마스크 하나로 그리고 판정한다(같은 `BossTileGrid`).
- **드론↔23호 판정**: 23호 콜라이더가 커서 원(반경 1m)과의 겹침을 `Collider.ClosestPoint` 로 계산. 레이어 마스크에 보스를 넣지 않고 직접 판정.
- **송전기 개수 커플링**(`playerScanRadius`, CLAUDE.local.md) 은 이번 범위가 아니지만 S3 에서 송전탑 수가 바뀌지 않았는지 같이 본다.
