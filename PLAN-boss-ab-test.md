# PLAN — 23호 A·B 비교 보스 + 북진 압박 + 0줄 자폭 (2026-10-20 · 경석 · 내일 빌드 플레이테스트)

> 상태: ✅ 승인(경석 10-20 — "Codex 교차검증 후 전부 한 번에") · ✅ Codex 교차검증(10-20, 아래 §7 반영) · ✅ 구현(미커밋) — 컴파일 0 · 전투 279/0 · 데이터 테이블 239/0 · 보스 패턴 10/0 · ✅ 경석 Play(A·B · 자폭 · 1차 지적 반영 후 재확인). 작업자: 경석(Claude) · 브랜치 `feature/Boss23`
> 근거: 경석 지시(10-20) · 제안서 `61_wells-no23-gimmick-push-north.md`(은희, 열린 질문 답 포함) · [PLAN-boss-raid-relay.md](PLAN-boss-raid-relay.md)

## 0. grill 확정 (경석 10-20)
| 항목 | 결정 |
|---|---|
| 두 보스 | **차이는 둘뿐** — 70% 송전기의 드론 유도 유무 · 30% 기믹. 나머지(HP·상한·배수·개전·콤보·공격 수치)는 같다 |
| 공통 기준 | **레이드 규칙** — 인원 HP(1인 2880 × 1/2.5/4.55) · 한 방 90% 상한 · 페이즈 배수 · ① 개전 시연(둘 다) |
| 보스 A | 70% 송전기 **기본형**(드론 유도 없음, 실패 = 레이지) · 30% **간파 릴레이** · 0줄 자폭 |
| 보스 B | 70% 송전기 **레이드형**(드론 유도, 실패 = 방전) · 30% **북진 압박**(신규) · 0줄 자폭 |
| 0줄 자폭 | **HP 0 → 죽지 않고 자폭 단계**(카운트다운 + 실드). 실드 파괴 = 처치 / 실패 = 즉사 대폭발(전멸 → 재도전) |
| 북진 '북' | 릴레이와 같은 **화면 기준** — 안전 구역 = 릴레이와 같은 화면 아래(3줄 6칸), 북 = 마름모 위 꼭짓점 |
| 밀기 | 무력화 중 **간파 1번 = 1칸**, 방향 북 고정(취약 단계 없음). 무력화 3초에 1회만 인정 |
| HP 임계 한 방에 여러 개 | 기획 논의 대기(경석) — 지금 그대로(예약 큐 = 순서대로 전부 나옴) |
| 기본 보스 프리팹 | `TwentyThree.prefab` data → A 로 바꿔도 됨(팀원 Play 도 A) |
| 자폭 실패 뒤 생존자 | 보스 HP 1 로 남고 **다음 피해에 사망** |
| 버그① 보라색 ≠ 판정 | 표시가 판정보다 0.15초 먼저 꺼진다(09-28 설계 `CounterVisualLeadSeconds`). → **같은 순간에 꺼지게**(선행 0). 09-28 결정 뒤집음 |
| 버그② 뒤에서 간파 성공 | 원인 = 오늘 앞뒤 구분 끔(`useFacingRules`). → **간파만 정면 필요**: 새 칸 `counterRequiresFront`(기본 켬)로 분리, 백어택·바닥 앞뒤 표시는 계속 꺼짐 |
| ✅ 확정(경석 10-23) 정면 표시기 | 간파는 정면(반각 `counterFrontAngle` 90 = 앞쪽 반원)만 되지만 **바닥 정면 표시기(`BossDirectionIndicator`)는 꺼 둔 채로 간다**(`useFacingRules` = false). 판정 범위는 보스 모델 방향으로 읽는다 — 버그 아님 |
| ✅ 확정(경석 10-23) 팔라딘 Q 경직 | 취약 중 Q 경직은 **간파 그로기가 아니다** — 정지 조건 없음(`BossPauseCondition.None`), 장판·드론은 멈추지 않는다 |

## 1. 보스 A·B 에셋과 빌드
- **B** = 기존 `TwentyThree_Raid.prefab` + `No23_Raid.asset` — phases 만 바꾼다: 0.70 `RaidDroneCharge` · 0.30 `RaidPushNorth`(신규). 45% 구출·20% 릴레이 행 삭제.
- **A** = 신규 `No23_A.asset`(`No23_Raid` 복제 — 레이드 규칙 그대로) phases: 0.70 `ChargeSequence` · 0.30 `RaidInterruptRelay`.
  `TwentyThree.prefab`(기본 보스)의 data 를 `No23_A` 로 바꾼다 → 기본 빌드·에디터 기본 = A, 레이드 빌드·툴바 '레이드' = B.
  - 새 에셋은 xlsx 에 열이 없어 **SO 값을 그대로 쓴다**(경고만, data-table.md §61) → xlsx 수정 없음. 기존 `No23.asset` 은 지우지 않는다(되돌리기용).
  - ⚠️ 기본 보스가 바뀌므로 팀원 Play 에도 A 가 나온다 — 확인 필요(§6-1).
- 둘 다 0줄 자폭: 데이터 칸 `selfDestructAtZero`(신규).
- 빌드: 기존 메뉴 그대로 — 기본 exe(A) · `WindowsRaid` exe(B, `BOSS_RAID_BUILD`).

## 2. 0줄 자폭 (`selfDestructAtZero`)
- 피해가 HP 를 0 으로 만들려 하면 **HP 1 에서 멈추고** 자폭 단계를 예약(현재 행동 끝난 뒤 = 다른 기믹과 같은 소비 지점).
- 자폭 단계 = 기존 ④ 흐름 재사용(중앙 점프 → 차징 자세 → 체력바 실드 · 카운트다운 `raidSelfDestructTime`).
  - 실드 파괴 → **보스 사망**(기존 사망 연출·결과 화면). 실드 몫 넘친 피해는 HP 로 가지 않는다.
  - 시간 초과 → 즉사 대폭발(기존). 살아남은 사람이 있으면 보스는 HP 1 로 남아 **다음 피해에 사망**.
- 자폭 단계 중·예약 중 HP 는 1 아래로 안 내려간다.

## 3. 북진 압박 (`RaidPushNorth`, 보스 B 30%)
| 단계 | 내용 |
|---|---|
| 개시 | 송전기처럼 **중앙 점프 → 남쪽(화면 아래) 바라봄**. 드론·상시 장판 정지. 자막 1줄 |
| 차징 N회차 | 송전탑 인원 비례(기존 규칙 1/2/4) 상승 · 차징 구슬. 바닥 예고 = 1회차 **안전 구역(릴레이와 같은 6칸) 뺀 전부**, 2회차부터 **전체**. 예고 길이 = 차징 시간(1회차 12초 · 이후 10초) |
| 탑 전부 파괴 | 차징 끊김 · 바닥 예고 취소 → **무력화 3초**(그로기 클립 · 간파 창 표시) |
| 간파 1회 | 북으로 **1칸**(대각선 한 칸 ≈ 타일 대각) 밀림 → 그 자리에서 다음 차징. 3초 동안 간파 없으면 그 자리에서 다음 차징 |
| 북 끝 도달 | 기믹 종료 → **그로기 8초 + 받는 피해 +30%**(제압 경로, 길이·배율만 이 값) |
| 실패 | 차징 끝까지 탑이 남음 → 예고 칸 **즉사 방전**(기존 즉사 경로). 전멸이면 기존 리스폰(기믹 처음부터) · 살아남으면 다음 차징 |
- 기믹 중 보스는 **피해를 받지 않는다**(릴레이와 같음 — HP 가 0줄로 새지 않게). 무력화 중 간파만.
- 수치는 전부 데이터 칸(`pushNorth*`, xlsx 미노출). 탑 체력 = 기존 `raidPylonHpMultiplier`.
- 목표 = 마름모 위 꼭짓점 칸 중심. 중앙에서 대각선 3칸 → 최소 3회차(제안서 그대로).

## 4. 보스 A 쪽 연결
- 릴레이·자폭은 지금도 `raidScript` 안에서만 돈다 — A 데이터가 `raidScript` 켜짐(레이드 규칙)이라 그대로 동작. 코드 변경 없음.
- 70% `ChargeSequence` 는 기존 기본형 송전기(실패 = 레이지 돌진 3회).

## 5. 검증
- 컴파일 0 · 전투 EditMode(0줄 판정·북진 칸 계산 순수 규칙 테스트 추가) · 보스 패턴.
- Play 솔로: A(70 송전기 → 30 릴레이 → 0 자폭 → 사망) · B(70 드론 송전 → 30 북진 3회 → 0 자폭). F3·디버그 HP 감소가 없으면 검증용 단축키 1개(체력 −10%) 추가 여부 질문.
- 빌드 두 개 뽑기(기본·레이드) — 경석이 내일.

## 6. 버그 수정 (같이 들어간다)
- `CounterVisualLeadSeconds` 0 — 표시 꺼짐 = 판정 닫힘. 창 길이·데이터는 그대로.
- `IsCounterFromFront`: `useFacingRules` 대신 `counterRequiresFront` 를 본다. `BossDirectionIndicator`·`IsBackAttackHit` 는 `useFacingRules` 그대로(꺼짐).

## 7. Codex 교차검증 반영 (10-20)
| 지적 | 반영 |
|---|---|
| P0 xlsx `Boss` 시트에 `No23_Raid` 페이즈 3개 셀 — 배열 축소 시 범위 밖 오류로 **A·B 빌드 전부 중단** | **B 도 새 에셋 `No23_B`** — `TwentyThree_Raid.prefab` data override 를 교체. `No23`·`No23_Raid` 는 무수정(xlsx 수정 없음) |
| P0 `TakeDamage` 만 막으면 직접·비율 피해(`Unit.ApplyHealthDamage`)로 샌다 | 보스 `TakeDamage` 에서 치명 피해를 HP−1 로 깎고 + `CanApplyHealthDamage` 재정의로 보호 중 HP 피해 차단(Unit 무수정) |
| P0 자폭 실드 넘침·실드 0 뒤 추가타로 조기 사망, 성공 = 제압 | 0줄 자폭 중 HP 피해 전부 차단, 실드 파괴 = 처치(보호 해제 후 정상 `TakeDamage` 사망 경로), 실패 보호는 **방전 발동까지** 유지 |
| P0 북진: 장판 예고 12초 vs 탑 제한 12초+상승 여유 → 탑을 부숴도 방전 | 장판 **취소 API**(pending·Warn 해제 + FloorClear RPC) · 회차 종료 시각 하나로 묶음(탑 판정이 방전보다 먼저) · 회차마다 공격 예산 갱신 · `BeginCharge` 재호출 금지(제자리 재차징) |
| P1 보라색: 상수 0 만으로는 돌진 애니 준비 지연 시 어긋남 | 23호는 표시를 **판정 창 개폐에 직접 묶음**(독립 조기 소등 예약 제거). 공용 상수는 그대로(중간보스 무변경) |
| P1 기본/레이드 송전기 차이(30/25초 · 3인 탑 4/3 · 탑 HP ×1/×2) | 각 송전기 판의 일부로 유지(드론 유도 판 = 레이드 송전기 그대로) |
| 30%·0줄 한 방 동시 | 페이즈 예약 뒤에 자폭 1회 — 그동안 HP 1 보호 |

## 8. 구현 메모 (10-20)
- 에셋: `No23_A`(guid e6c69007…) = 0.7 ChargeSequence · 0.3 RaidInterruptRelay · `selfDestructAtZero` / `No23_B`(guid fefa4ae4…) = 0.7 RaidDroneCharge · 0.3 RaidPushNorth · `selfDestructAtZero`. `TwentyThree.prefab` → A, `TwentyThree_Raid.prefab` override → B. `No23`·`No23_Raid` 무수정.
- 0줄: `TwentyThreeBoss.TakeDamage`(치명 → HP 1 + 예약, 페이즈 판정 뒤) · `CanApplyHealthDamage` 재정의(직접 피해) · 상태 `ZeroGuard`(Reserved→Active→Killing/Exploding→Done). 실드 파괴 = 정상 `TakeDamage` 사망.
- 북진: `TwentyThree.RaidRelay.cs` `RaidGimmick.PushNorth` — 회차마다 `_charge.Begin`(제자리) · 예고 = 차징 + 상승 여유(`RiseGrace`) 같은 종료 시각 · 탑 파괴 → `BossElectricFloor.ServerCancelDischarge`(신규) · 무력화 = 피격 리액션 클립 + 간파 창. 보상 = `EnterSuppressWith`(이번 제압만 8초·×1.3).
- 보라색: 23호 표시는 판정 창 개폐만 따른다(조기 소등 예약 제거). 간파 정면 = `counterRequiresFront`(기본 켬).
- 자막 2줄(`BOSS_RAID_PushNorthGuide`·`Success`).
- 시험 요령: 보스에게 **F9(가장 가까운 몬스터 즉사)** 한 번 = HP 1 로 멈추며 70% → 30% → 0줄 자폭이 **순서대로** 예약된다.
