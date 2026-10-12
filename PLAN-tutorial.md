# PLAN — 튜토리얼 스테이지 구현 (할 일 15)

> 작성 2026-10-11 메인. 원본 시나리오: `C:\Users\user\Downloads\tutorial-scenario.md`(초안 v5) → 브랜치에 `Docs/design/tutorial-scenario.md` 로 넣는다.
> **사용자 지시(10-11 출근 전)**: 새 브랜치로 위임, 단계별 플랜 → 마지막 단계까지 메인 추천대로 자율 진행. 🔴 **사용자가 직접 검증하기 전까지 development 병합 절대 금지.**

## 0. 운영
- 브랜치(둘 다 최신 origin/development 기준, push 는 자기 브랜치로만):
  - **Work1 `feature/TutorialStage`** — 본류(진행 상태·게이트·배리어·휴면·타이머·안내·사망 규칙·존별 단계).
  - **Work2 `feature/TutorialTraits`** — 특성 선택 시스템(독립 모듈). 끝나면 **`feature/TutorialStage` 로 병합**(development 아님).
- 레인 = 모드 레인(coop-agent 0-C), 워커 claude-alt. 단계 하나 = handoff 하나 = 커밋 하나 크기. 메인이 단계 보고를 읽고 다음 단계를 보낸다.
- 검증: 컴파일 0 · EditMode(플레이어 + 새 튜토리얼 테스트) 통과 · 커밋. **Play/MPPM 은 사용자만**(alt 는 Play 금지). 순수 로직은 Unity 비의존 클래스로 빼서 EditMode 테스트.
- xlsx·SVN 은 건드리지 않는다(규칙: Export 는 development 병합 후 Main 에서). 문구는 SO 기본값 + `[DataTableText]` 준비만.
- 몬스터·보스 타이머·보스 진입 코드(경석 영역)는 **최소 훅**만 넣고 `Docs/tech/handoff-tutorial.md` 에 무엇을 왜 바꿨는지 기록. 튜토리얼 스테이지에서만 동작하게(절차 맵·다른 스피너봇·Debug 연습장에 퍼지지 않게).
- 존 프리팹 5종은 공용 → 튜토리얼 전용 오브젝트(배리어·허수아비·트리거)는 **StageTutorial/씬 쪽**에 둔다.

## 1. 미결 논의(§8.3)·합의(§8.2) — 시나리오 "제안"대로 채택(사용자 지시: 메인 추천대로)
| # | 채택 | 메모 |
|---|---|---|
| D1 타이머 시작 시점 | 존4 설정 설명 뒤 시작 — **튜토리얼 스테이지 플래그로만** 켠다 | Stage1=모든 판이라 사실상 본게임 규칙. 🔴 팀장 합의 필요 — 병합 전 확인 항목 |
| D4 | 존4 클리어 = 중간보스 처치 | |
| D5 | 자동 부활 3초·2초 무적, 타이머 전 목숨 유지 / 후 목숨 1 차감, **단계 기준** | |
| D9 | 전원 완료 + 첫 완료자 후 60초 자동 진행 | 조정값 |
| D10 | 존1 허수아비 3개(간파 사이클 없는 변형) | |
| D11 | 대시 포함 | |
| D12 | 스킬 잠금 안 함, R 취소 팁 | |
| D13 | 존2 = 9마리 전멸 | |
| D14 | 특성 강제, 자동 진행·타이머 만료 시 무작위 1장 자동 획득, 창 닫기 | |
| D15 | 사망·관전 중에도 목표 패널 표시 | |
| D16 | "간파", 첫 등장만 "간파(인터럽트)" | |
| D17 | 패드 규칙 유지 + G10 개방 때 전원 경고 | |
| D2-2 | 연습 모드: 피해 없음·창 간격 9초·원래 창 길이·연속 4번 놓치면 힌트 | |
| D18 | 스피너봇에 간파 슬로우 연결 안 함 | |
| D19 | 보스방 도착 시 23호 정면 간파 팁 1줄 | |
| D20·D21 | 게이지 이름·원흉 = 문구 placeholder(ID 로 교체 가능) | 기획 결정 |
| D23 | 스킵·2회차 범위 밖 | |
| D8 특성 내용 | **기획서 미공유 → 프레임워크 + placeholder 특성**(현재 실효 배율 공격력·이동속도 + 이벤트형 쿨감 정도, SO 데이터) | 기획서 오면 데이터만 교체 |
| D22 | 코드 유지. 팔라딘 패시브 xlsx 툴팁("피격 시")·어쌔신 시트 정리는 **xlsx 작업이라 이번 범위 밖** — 병합 후 Main 에서 | 보고에 남김 |

## 2. 단계 (Work1 `feature/TutorialStage`)
| 단계 | 내용 | 완료 조건 |
|---|---|---|
| **S0** 문서·맵 정합 | 시나리오 → `Docs/design/tutorial-scenario.md`, 이 PLAN → `PLAN-tutorial.md`. §1.3 존3 `ZoneM_typeC` 를 `StageTutorial.prefab`·`TutorialStageAuthoring.Slots` 에 반영(씬 오버라이드와 일치). 코드 현황 확인(§7.1 목록이 실제와 맞는지) | 커밋, 불일치 목록 보고 |
| **S1** 진행 상태 코어 | 씬 상주 `TutorialDirector`(서버 소유·복제): 단계·게이트(G1~G10)·개인 체크리스트·파티 집계·교착 방지 60초(서버 실제 시간)·이탈 재판정·늦은 클라 재동기화. 순수 `TutorialFlowModel` + EditMode | 테스트 통과 |
| **S2** 구간 감지 | 존1~4·복도1~4 중간·보스 진입 구간을 서버에서 판정(플레이어 위치 기반, 180° 회전 고려). 첫 진입·개인 1회 이벤트 | 테스트 |
| **S3** 배리어 | G1~G4·G6·G7·G9·G10 배리어: 모든 피어에서 플레이어 차단(모터 스윕 마스크가 보는 레이어), 대시 불가, 몬스터 차단(NavMeshObstacle carve), 보이는 placeholder 머티리얼, 개방 복제, 추락 복구 후보 제외 | 테스트 + 저작 메뉴 |
| **S4** 몬스터 휴면·존 장부 | 존 단위 몬스터 묶음(서버), 휴면 = 인지·공격 정지 + 무적(피해·넉백·CC 무시), 기상 API, 존 클리어 판정(존2 전멸·존3 스피너봇·존4 중간보스). 몬스터 쪽 최소 훅 + handoff 문서 | 테스트 |
| **S5** 타이머·보스 길 | 튜토리얼 플래그: 타이머 자동 시작 끔·서버 시작 API·420초·Idle 때 HUD 숨김(만료 강제 이동·도착 정지 계약 유지). G9/G10 + 패드 비활성/활성 + 엘리베이터 문 열림 구동 | 테스트 |
| **S6** 안내 UI | 목표 패널(상단 중앙)·안내 자막·강조(펄스) — 사망·관전 중에도 표시, 늦은 클라 재구성. 문구 SO(ID → 문구, 캐릭터별 변형, `{Q}` 등 툴팁 이름 치환), 시나리오 §5 초안 그대로 | 테스트 |
| **S7** 존1 | 착지 후 환영 → 이동 5m(오너 보고)·대시(서버 승인 지점)·허수아비 3개 적중 5회·패시브 툴팁 1초 표시(클라 보고) → G1. 거너 과열·R 취소 팁 | 테스트 |
| **S8** 존2·복도 특성 연결 지점 | 존2 진입 안내 → 4초 뒤 기상, Q/E 안내·넛지, 전멸 → G3. 복도1~4 중간 지점 이벤트 → 특성 선택 호출 지점(인터페이스만, 구현은 Work2) + 대기 표시 + 게이트 G2/G4/G7/G10 | 테스트 |
| **S9** 존3 스피너봇 | 연습 모드(체력 고정·회전만·창 매번 9초 간격·돌진 피해 없음·자리 유지) → 간파 파티 합계 3회(`ServerCounterSucceeded`) → 2초 멈춤 → 본 모드 → 처치 G6. 빗나감/이른 간파/힌트 문구. 튜토리얼 스피너봇에만. handoff | 테스트 |
| **S10** 존4 | 진입 → 타이머 게이지 등장·설정 자막(전원 동시) → G8 타이머 시작 → 3초 뒤 기상(중간보스 제외) → 패널 n/4 → 다리 → 중간보스 기상 → 처치 G9. 타이머 만료 문구. NavMesh 재베이크 끊김·다리 고정 조각 에러 로그 확인 | 테스트 |
| **S11** 사망 규칙 | 자동 부활(단계 기준 목숨), 안전 지점·2초 무적, 패드/강제 이동 시 대기 부활 취소·즉시 부활, 연출 잠금 중 처리 | 테스트 |
| **S12** 통합 | `feature/TutorialTraits` → `feature/TutorialStage` 병합, 복도 4곳 특성 창 연결·창 열린 동안 무적·자동 획득(교착 방지·타이머 만료). 보스방 도착 → 안내 종료. 전체 EditMode·push(브랜치). 최종 보고 = §9 QA 체크리스트 + 사용자 Play 확인 순서 | 사용자 검증 대기 |

## 3. 단계 (Work2 `feature/TutorialTraits`)
| 단계 | 내용 |
|---|---|
| **T1** 데이터·서버 | 특성 SO(id·이름·설명·효과 종류·값·아이콘), 후보 풀·제시 N장(기본 3)·중복 규칙(후보 제외), placeholder 특성 4~6종(공격력·이동속도 배율 — 출처 id 를 특성마다 다르게 해 중첩, 이벤트형 쿨감). 서버 선택 RPC 검증(제시 후보인지·이미 골랐는지)·효과 적용(`StatusEffectController.Apply` 영구)·자동 획득 API. 순수 로직 EditMode |
| **T2** UI | 특성 선택 창(모달 — `UiModalBlocker`, 그 플레이어만 입력 차단), 상단 설명 옵션, 창 아래 한 줄 옵션, 선택/자동 닫힘, 획득 특성 다시 보기(간단 목록) |
| **T3** 마무리 | 외부 호출 인터페이스(`OpenSelection(clientId, corridorIndex, showIntro)`, `AutoPickAll()`, 선택 완료 이벤트) 확정·문서화 → S12 에서 연결 |

## 3-1. v8 시나리오 반영 (브랜치 `feature/TutorialV8`, 2026-10-12)

> 원본: `Docs/design/tutorial-scenario.md` v8(커밋 f58d7c8e). 사용자가 v8 문서로 사양을 확정 → 승인 대기 없이 진행(위임 지시). 레인 = claude-alt.
> **사용자 확정(문서보다 우선)**: 목숨 차감 기준 G11 유지(문서 §2.5·§4·D5·§9 를 G11 로 고침) · 타이머 420초 유지 · Spawn Point 예외 새 작업 없음 ·
> 존3 '간파 성공 → 파티 우클릭 쿨 초기화'는 튜토리얼 전용으로, 다른 레인의 '인터럽트 성공 시 우클릭 쿨 80% 감소' 위에 덮는다(쿨 장부 공개 API 사용).
> 🔴 GameData.xlsx·SVN 무수정. Play 검증은 사람.

| 단위 | 내용 | 완료 조건 |
|---|---|---|
| **V0** 문서 | 이 절 + 시나리오 G11 문구 정정 | 커밋 |
| **V1** 존1 | 개인 항목 = 이동·대시·패시브 + **기본 공격·Q·E·R·우클릭 각각 실제 사용 1회**(적중 불필요, 허수아비 적중 5회 제거). 존1 전에 쓴 것도 인정, 사망으로 안 되돌림, 조준 취소·거부 제외. **G1 은 60초 교착 방지 제외**. 목표 패널 체크리스트(8줄)·동료 진행도(카운트다운 없음)·캐릭터별 힌트(§5 ID). 판정 훅: 기본 공격 = 각 기본 공격 서버 시작(새 `Player.ServerBasicAttackStarted`), Q·E·R·우클릭 = `ServerSkillUsed`(승인 = 실제 발동; R 은 대상 확정 뒤 승인), **거너 Q = 발사**(새 `GunnerChargeLaserSkill.ServerFired`) | 컴파일 0 · EditMode |
| **V2** 복도1 쿨 초기화 | G1 뒤 각 플레이어의 **복도1 첫 진입**(구간 판정) 때 그 사람의 Q·E·R·우클릭(기본+대체 스킬 전부)·대시 충전 초기화 + 자막 `TUT_C1_COOLDOWN_RESET`(그 사람). 플레이어별 1회. 진행 중 행동(실행 중 스킬·어쌔신 변신·E 강화 대기)이 있으면 끝날 때까지 **초기화 예약 유지** 후 한 번 더 | 컴파일 0 · EditMode |
| **V3** 존2 | 전멸만. Q·E 체크·Q/E 자막·슬롯 강조·넛지 제거, 목표 = 처치 n/total | 컴파일 0 · EditMode |
| **V4** 존3 연계 | [간파 성공 → 취약(시간 종료 없음, 자율 이동·공격 정지) → 우클릭 밀치기 → 연습용 지정 벽 충돌 → 원위치 복귀] 파티 합계 3회, 마지막 복귀 완료 = G5 → 본 전투. 간파 성공마다 파티 전원 우클릭 쿨 1회 초기화. 복귀 중 무적·공격 없음·밀치기 무시·플레이어 몸 무시. 본 전투 전환 때 교육 제한 해제. 거너 전용 간파 자막 제거 | 컴파일 0 · EditMode |
| **V5** 문구·문서 | `TutorialGuideIds`·시드·v8 문구 갱신 메뉴(빈 변형 행 규칙 유지), tutorial-zone1·zone3·corridors-zone2·director·qa·handoff-tutorial-spinner 갱신 | 커밋 · push |

**결정(조사 결과 — 보고에도 적음)**
- **23호 간파 게이지·제압·취약·벽 충돌은 재사용하지 않는다.** 전부 `TwentyThreeBoss` private(벽 = 보스방 `InvisibleBoundaries` 사각형 수학, 밀치기 = 보스 `ReceiveAttack` 안 자체 이동, 이벤트 없음). 같은 **방식**(공격자 반대 방향 고정 거리·시간 밀치기 + 벽 면 거리 수학 판정)만 튜토리얼 스피너봇 쪽에 순수 모델로 새로 둔다.
- **플레이어 우클릭은 원래 몬스터를 밀지 않는다**(23호만 자기 코드에서 밀린다). 튜토리얼 스피너봇이 취약일 때 간파 적중(`AttackInfo.isInterruptAttack`)을 받으면 튜토리얼 컴포넌트가 민다 → `ISpinnerBotOverride` 에 멤버 1개(`NotifyInterruptHit`) + `SpinnerBot.TakeDamage` 호출 1줄. Q 넉백·일반 피해는 이 경로를 안 타서 연계로 안 센다.
- 쿨 초기화는 **기존 공개 API 만** 쓴다: 스킬 = `PlayerSkillController.ReduceCooldownServer(skill, GetCooldownRemaining(skill))`(등록된 기본·대체 스킬 전부 — `GetSkillIndex` ≥ 0), 대시 = 새 `PlayerDashController.ServerResetChargeToFull`/`OwnerResetChargeToFull`(기존 `ResetChargeToOne` 짝). `PlayerSkillController` 는 고치지 않는다(80% 레인과 충돌 없음).
- 유효 벽 = 존3 슬롯 사각형의 긴 변 중 스피너봇 스폰에 가까운 쪽(연습용 지정 벽). 연습 위치 = 스폰의 벽 방향 좌표만 벽에서 `몸 반경 + 4m`(밀치기 3m × 1~2회) 로 옮긴 점(존 끝에서 8m 안쪽). 밀치기 거리·시간·접촉 여유는 인스펙터(D26 튜닝).
- 캐릭터별 힌트(`TUT_Z1_Q_GUNNER`·`TUT_Z1_E_*`·`TUT_Z1_R_ASSASSIN`)는 목표 줄 변형 행으로 채운다(그 캐릭터의 Q/E/R 목표 줄이 힌트가 됨). `TUT_Z1_ATTACK_GUNNER`·`TUT_Z1_PASSIVE_*` 는 기존대로 빈 행.
- G1 대기 줄은 카운트다운 없는 새 행 `TUT_WAIT_PARTY_NOAUTO`(시나리오 §5 에 없음).

**진행(2026-10-12)**: V0 `0af4cac4` · V1 `c3f6f295` · V2 `348376fc` · V3 `3ab36055` · V4 `dda9ca7b` · V5 문서 `3ff996b6`. Play 확인 순서 = Docs/tech/tutorial-qa.md(v8 반영).
- 26(`feature/InterruptSuccessCooldown`) 병합 `df299c3a` 뒤 정리: 존3 간파 성공 판정 = `PlayerInterruptSkillBase.ServerInterruptSucceeded`(26 의 시전자 쿨 감소 **뒤** 발생 → 파티 우클릭 쿨 0 으로 덮음), `ServerCounterSucceeded`↔`ServerInterruptResolved` 같은 프레임 짝짓기 제거.
  `TutorialCooldownReset` = `PlayerSkillController.ResetCooldownServer(skill)`(위 '기존 공개 API' 결정의 `ReduceCooldownServer(…)` 를 대체). 🔴 **V8 은 26 에 의존 — 26 을 먼저 development 에 병합할 것.** 상세: Docs/tech/tutorial-zone3.md §5.

## 4. 리스크
- 몬스터·타이머·보스 진입 훅은 경석 영역 — 최소 침습 + handoff 문서, 병합 전 경석 공유.
- 4.MapScene·StageTutorial 프리팹 대량 변경 → 다른 사람 씬 변경과 충돌 가능. 브랜치에서만, 병합 전 development 재반영.
- D1(타이머 시작)·Stage1=모든 판 → 본게임 규칙 변경. 사용자·팀장 확인 전 병합 금지.
- alt 는 Play 를 못 하므로 멀티 동작(오너 권한 이동·배리어·늦은 클라)은 사용자 MPPM 에서만 확인된다.
