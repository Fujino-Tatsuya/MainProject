# 몬스터 데이터 인스펙터 칸 전수조사 (2026-10-19, feature/Boss23, 읽기 전용 조사)

## 방법
- 칸 목록: 정의 파일을 파싱(public / [SerializeField], 중첩 클래스 포함, 주석 처리된 유령 필드는 제외).
- 코드 읽기: Assets 전체 .cs 1301개에서 주석·문자열을 지운 뒤 칸 이름 검색. 범용 이름(damage·cooldown·weight·color 등)은 대표 줄을 손으로 확인. "죽은 칸" = 참조 0, Tooltip/Obsolete 미사용, 또는 읽지만 효과 0(빈 콜백·경고 로그만).
- xlsx: `Assets/50.Art/DataTable~/GameData.xlsx` 현재본의 Boss·MidBoss·Monster 시트 A열. "행 n" = 그 칸 이름의 행 수(배열이면 원소 수).
- 에셋 값: `Assets/2.Prefabs/Monster/Data/*.asset` + 중간보스 프리팹 3종(GauntletBot/SpinnerBot/WallBot.prefab; SpinnerBot_Tutorial 변형은 이름만 오버라이드). MonsterDataSO 비교 대상은 일반·중간보스 Data 10개(보스 3개 제외).
- 에셋(git) 변경: `git log -p` 로 Data 폴더·중간보스 프리팹의 **값 수정**만 셈(파일 생성·칸 추가 제외). tune·팀장 = 커밋 메시지에 `tune` 또는 `팀장`.
- xlsx(SVN) 변경: GameData.xlsx 리비전 34개(r360 10-02 ~ r456 10-16)를 `svn cat` 으로 임시 폴더에 받아 칸 값 변경 리비전 수를 셈. "불일치" = 현재 xlsx 값 ≠ 에셋(인스펙터) 값인 칸 수(에셋에 칸이 없으면 제외) — 테이블 모드·빌드에선 xlsx 가 이긴다.
- 한계: BossAttackEntry 와 BossComboEntry 의 weight·cooldown·allowedFromPhase 는 YAML 키가 같아 git 집계가 섞임. 커밋 안 한 Play 튜닝은 안 보임. 작업 트리의 미커밋 변경(No23·No23_Raid 콤보 JumpDash 추가)은 집계 밖.

## 타입별 요약
| 타입 | 칸 | 죽은 칸 | xlsx 행 있음 | 에셋 간 전부 같음 | git 값변경 0 | xlsx 값변경 ≥1 | xlsx≠에셋 칸 |
|---|---|---|---|---|---|---|---|
| MonsterDataSO | 60 | 2 | 31 | 17 | 42 | 9 | 54 |
| BossDataSO(자체) | 115 | 9 | 87 | 65 | 87 | 9 | 4 |
| └ BossAttackEntry(×8행) | 29 | 0 | 20 | 0 | 13 | 2 | 12 |
| └ BossPhaseEntry | 4 | 0 | 3 | 0 | 4 | 0 | 0 |
| └ BossComboEntry+Step | 5+2 | 0 | 0(Ignore) | 0 | 5+2 | - | - |
| BossElectricFloorDataSO(2개) | 14 | 2 | 시트 없음 | 12 | 14 | - | - |
| WellsDroneDataSO(1개) | 32 | 2 | 시트 없음 | 비교불가 | 28 | - | - |
| TelegraphStyleSO(1개, 중첩 4+13) | 17 | 0 | 시트 없음 | 비교불가 | 17 | - | - |
| GauntletBot(프리팹) | 24 | 0 | 10 | 비교불가 | 23 | 0 | 0 |
| SpinnerBot(프리팹) | 16 | 0 | 8 | 비교불가 | 16 | 0 | 0 |
| WallBot(프리팹) | 15 | 0 | 10 | 비교불가 | 15 | 0 | 0 |

23호 인스펙터 = MonsterDataSO 60칸 + BossDataSO 115칸 + 공격 행 8×29칸.

## 죽은 칸 15개
| 칸 | 근거 | xlsx 행 |
|---|---|---|
| MonsterDataSO.maxShield | 참조 0 | **있음**(Monster·Boss) |
| MonsterDataSO.attackWindup | `MonsterBase.AttackWindupSeconds`(MonsterBase.cs:955)만 읽고 호출자 0. 🔴 기획이 xlsx r425 에서 4개 몹 값을 고쳤다 | **있음** |
| BossDataSO.raidSelfDestructCounterBonus / CounterInterval / CounterWindow / DamageReduction | 참조 0(10-19 실드로 대체) | **있음** |
| BossDataSO.grabThrowDistance | 읽지만 `OnGrabThrowRelease`(TwentyThreeBoss.cs:2847)가 경고 로그만, 오버라이드 없음 | **있음** |
| BossDataSO.bombThrowInterval | `ConfigureCycle` 로 넘기지만 콜백 `OnWellsAttackCycle`(TwentyThreeBoss.cs:4210)이 빈 함수 | **있음** |
| BossDataSO.spreadAngle | 참조 0(폭탄 투척 09-16 제거) | **있음** |
| BossDataSO.jumpTelegraphPrefab | 참조 0, Tooltip 미사용(에셋엔 프리팹 참조가 남아 있음) | 없음 |
| BossDataSO.bombPrefab | 참조 0 | 없음 |
| BossElectricFloorDataSO.warnOuterColor / warnFillColor | [Obsolete] | 없음 |
| WellsDroneDataSO.circleOuterColor / circleFillColor | [Obsolete] | 없음 |

## 전체 표

### MonsterDataSO  (`Assets/1.Scripts/Monster/MonsterDataSO.cs`)

| 칸 | Header | 타입 | 코드 읽기 | xlsx | 에셋 값 | 에셋(git) 값 변경: 커밋 수 / tune·팀장 / 마지막 | xlsx(SVN) 값 변경 리비전 수 · xlsx≠에셋 칸 수 |
|---|---|---|---|---|---|---|---|
| `archetype` | 아키타입 | MonsterArchetype | 읽음 MonsterBase.cs:584 | 대상 · **행 없음** | 다름 | 0회(안 건드림) | 0 |
| `rank` | 아키타입 | MonsterRank | 읽음 MonsterBase.cs:23 | 대상 · **행 없음** | 다름 | 0회(안 건드림) | 0 |
| `attackDamage` | 스탯 (Unit.Initialize로 주입) | int | 읽음 MonsterBase.cs:238 | 대상 · Monster 행 1 / Boss 행 1 | 다름 | 1회 / 1 / 2026-10-07 (몹 0·보스 1) | 1회 · 불일치 10 |
| `moveSpeed` | 스탯 (Unit.Initialize로 주입) | float | 읽음 MonsterBase.cs:238 | 대상 · Monster 행 1 / Boss 행 1 | 다름 | 2회 / 1 / 2026-10-06 (몹 2·보스 0) | 2회 · 불일치 3 |
| `chaseSpeed` | 스탯 (Unit.Initialize로 주입) | float | 읽음 MonsterBase.cs:647 | 대상 · Monster 행 1 / Boss 행 1 | 다름 | 2회 / 1 / 2026-10-06 (몹 2·보스 0) | 2회 · 불일치 3 |
| `attackSpeed` | 스탯 (Unit.Initialize로 주입) | float | 읽음 MonsterBase.cs:2467 | 대상 · Monster 행 1 / Boss 행 1 | 전부 같음 (1) | 1회 / 1 / 2026-10-03 (몹 1·보스 0) | 2회 · 불일치 10 |
| `attackCooldown` | 스탯 (Unit.Initialize로 주입) | float | 읽음 MonsterBase.cs:1868 — 23호는 행 cooldown 0 인 행의 폴백만 | 대상 · Monster 행 1 / Boss 행 1 | 다름 | 0회(안 건드림) | 2회 · 불일치 9 |
| `maxHp` | 스탯 (Unit.Initialize로 주입) | int | 읽음 MonsterBase.cs:238 | 대상 · Monster 행 1 / Boss 행 1 | 다름 | 3회 / 2 / 2026-10-14 (몹 0·보스 3) | 4회 · 불일치 10 |
| `defense` | 스탯 (Unit.Initialize로 주입) | int | 읽음 MonsterBase.cs:238 | 대상 · Monster 행 1 / Boss 행 1 | 다름 | 0회(안 건드림) | 1회 · 불일치 3 |
| `maxShield` | 스탯 (Unit.Initialize로 주입) | int | **죽은 칸** — 참조 0 | 대상 · Monster 행 1 / Boss 행 1 | 전부 같음 (0) | 1회 / 0 / 2026-08-13 (몹 0·보스 1) | 0 |
| `detectionRadius` | 인지 / 교전 범위 | float | 읽음 MonsterBase.cs:1950 | 대상 · Monster 행 1 / Boss 행 1 | 다름 | 0회(안 건드림) | 0 |
| `detectionHeightTolerance` | 인지 / 교전 범위 | float | 읽음 MonsterBase.cs:505 | 대상 · Monster 행 1 / Boss 행 1 | 전부 같음 (2) | 0회(안 건드림) | 0 |
| `attackRange` | 인지 / 교전 범위 | float | 읽음 MonsterBase.cs:264 | 대상 · Monster 행 1 / Boss 행 1 | 다름 | 3회 / 0 / 2026-09-15 (몹 2·보스 1) | 0 |
| `leashRadius` | 인지 / 교전 범위 | float | 읽음 MonsterBase.cs:486 | 대상 · Monster 행 1 / Boss 행 1 | 다름 | 1회 / 0 / 2026-09-18 (몹 0·보스 1) | 0 |
| `retargetInterval` | 인지 / 교전 범위 | float | 읽음 MonsterBase.cs:1016 | 대상 · Monster 행 1 / Boss 행 1 | 다름 | 0회(안 건드림) | 0 |
| `loseTargetRadius` | 인지 / 교전 범위 | float | 읽음 MonsterBase.cs:523 | 대상 · Monster 행 1 / Boss 행 1 | 다름 (에셋 3개엔 칸 없음) | 0회(안 건드림) | 0 |
| `returnSpeedMultiplier` | 인지 / 교전 범위 | float | 읽음 MonsterBase.cs:1415 | 대상 · Monster 행 1 / Boss 행 1 | 전부 같음 (5) | 0회(안 건드림) | 0 |
| `turnSpeed` | 회전 | float | 읽음 MonsterBase.cs:2128 | 대상 · Monster 행 1 / Boss 행 1 | 전부 같음 (10) | 1회 / 0 / 2026-09-08 (몹 1·보스 0) | 0 |
| `avoidanceRadius` | 회피 / 크라우드 (부분 겹침·성능) | float | 읽음 MonsterBase.cs:269 | 대상 · Monster 행 1 / Boss 행 1 | 전부 같음 (0.3) | 0회(안 건드림) | 0 |
| `obstacleAvoidance` | 회피 / 크라우드 (부분 겹침·성능) | ObstacleAvoidanceType | 읽음 MonsterBase.cs:270 | 대상 · **행 없음** | 전부 같음 (2) | 0회(안 건드림) | 0 |
| `projectilePrefab` | 원거리 (RangedTurret / RangedMo | GameObject | 읽음 MonsterBase.cs:252 (원거리 몹 전용) | 대상 · **행 없음** | 다름 | 0회(안 건드림) | 0 |
| `projectileSpeed` | 원거리 (RangedTurret / RangedMo | float | 읽음 MonsterBase.cs:252 (원거리 몹 전용) | 대상 · Monster 행 1 / Boss 행 1 | 다름 | 0회(안 건드림) | 0 |
| `projectileLifetime` | 원거리 (RangedTurret / RangedMo | float | 읽음 MonsterBase.cs:252 (원거리 몹 전용) | 대상 · Monster 행 1 / Boss 행 1 | 전부 같음 (4) | 0회(안 건드림) | 0 |
| `minStandoff` | 원거리 (RangedTurret / RangedMo | float | 읽음 MonsterBase.cs:760 (원거리 몹 전용) | 대상 · Monster 행 1 / Boss 행 1 | 다름 | 0회(안 건드림) | 0 |
| `projectileArcHeight` | 원거리 (RangedTurret / RangedMo | float | 읽음 MonsterBase.cs:252 (원거리 몹 전용) | 대상 · Monster 행 1 / Boss 행 1 | 다름 | 0회(안 건드림) | 0 |
| `projectileSplashRadius` | 원거리 (RangedTurret / RangedMo | float | 읽음 MonsterBase.cs:252 (원거리 몹 전용) | 대상 · Monster 행 1 / Boss 행 1 | 다름 | 0회(안 건드림) | 0 |
| `rangedStyle` | 원거리 — 공격 방식 (RangedMobile, P | MonsterRangedStyle | 읽음 MonsterBase.cs:1311 (원거리 몹 전용) | 제외(DataTableIgnore) | 다름 (에셋 9개엔 칸 없음) | 0회(안 건드림) | - |
| `skyStrikeTelegraphTime` | 원거리 — 공격 방식 (RangedMobile, P | float | 읽음 MonsterBase.cs:1350 (원거리 몹 전용) | 제외(DataTableIgnore) | 다름 (에셋 9개엔 칸 없음) | 0회(안 건드림) | - |
| `skyStrikeFallTime` | 원거리 — 공격 방식 (RangedMobile, P | float | 읽음 MonsterBase.cs:1368 (원거리 몹 전용) | 제외(DataTableIgnore) | 다름 (에셋 9개엔 칸 없음) | 0회(안 건드림) | - |
| `skyStrikeFloorTolerance` | 원거리 — 공격 방식 (RangedMobile, P | float | 읽음 MonsterBase.cs:1355 (원거리 몹 전용) | 제외(DataTableIgnore) | 다름 (에셋 9개엔 칸 없음) | 0회(안 건드림) | - |
| `skyStrikeLosBlockMask` | 원거리 — 공격 방식 (RangedMobile, P | LayerMask | 읽음 MonsterBase.cs:1325 (원거리 몹 전용) | 제외(DataTableIgnore) | 다름 (에셋 9개엔 칸 없음) | 0회(안 건드림) | - |
| `repositionBetweenAttacks` | 원거리 — 공격 방식 (RangedMobile, P | bool | 읽음 MonsterBase.cs:790 (원거리 몹 전용) | 대상 · **행 없음** | 다름 | 0회(안 건드림) | 0 |
| `retreatMinDuration` | 원거리 — 공격 방식 (RangedMobile, P | float | 읽음 MonsterBase.cs:768 (원거리 몹 전용) | 대상 · Monster 행 1 / Boss 행 1 | 전부 같음 (1.2) | 0회(안 건드림) | 0 |
| `repositionMinDuration` | 원거리 — 공격 방식 (RangedMobile, P | float | 읽음 MonsterBase.cs:795 (원거리 몹 전용) | 대상 · Monster 행 1 / Boss 행 1 | 전부 같음 (1.2) | 0회(안 건드림) | 0 |
| `attackDuration` | 공격 타이밍 | float | 읽음 MonsterBase.cs:1036 | 대상 · Monster 행 1 / Boss 행 1 | 다름 | 0회(안 건드림) | 0 |
| `attackWindup` | 공격 타이밍 | float | **죽은 칸** — MonsterBase.AttackWindupSeconds 만 읽고 그 호출자 0 | 대상 · Monster 행 1 / Boss 행 1 | 다름 | 0회(안 건드림) | 1회 · 불일치 4 |
| `cancelWindupIfTargetLeavesRange` | 공격 타이밍 | bool | 읽음 MonsterBase.cs:1196 (원거리 몹 전용) | 대상 · **행 없음** | 다름 | 0회(안 건드림) | 0 |
| `engageAttackDelay` | 공격 타이밍 | float | 읽음 MonsterBase.cs:1882 | 대상 · Monster 행 1 / Boss 행 1 | 다름 | 0회(안 건드림) | 0 |
| `maxGroggyCount` | 그로기 | int | 읽음 MonsterBase.cs:1680 | 대상 · Monster 행 1 / Boss 행 1 | 전부 같음 (0) | 2회 / 0 / 2026-09-08 (몹 2·보스 0) | 0 |
| `groggyDuration` | 그로기 | float | 읽음 MonsterBase.cs:1712 | 대상 · Monster 행 1 / Boss 행 1 | 전부 같음 (3) | 2회 / 0 / 2026-09-29 (몹 0·보스 2) | 1회 · 불일치 2 |
| `startsWithSuperArmor` | 슈퍼아머 | bool | 읽음 MonsterBase.cs:274 | 대상 · **행 없음** | 전부 같음 (0) | 1회 / 0 / 2026-08-13 (몹 0·보스 1) | 0 |
| `hasSuperArmorWhileAttacking` | 슈퍼아머 | bool | 읽음 MonsterBase.cs:1043 | 대상 · **행 없음** | 다름 | 0회(안 건드림) | 0 |
| `hitStunDuration` | 피격 / 사망 | float | 읽음 MonsterBase.cs:1703 | 대상 · Monster 행 1 / Boss 행 1 | 전부 같음 (0.4) | 0회(안 건드림) | 0 |
| `despawnDelay` | 피격 / 사망 | float | 읽음 MonsterBase.cs:1779 | 대상 · Monster 행 1 / Boss 행 1 | 전부 같음 (2) | 0회(안 건드림) | 0 |
| `animSpeedParam` | 애니메이터 파라미터명 | string | 읽음 MonsterBase.cs:2408 | 대상 · **행 없음** | 다름 | 1회 / 0 / 2026-09-10 (몹 1·보스 0) | 0 |
| `attackTrigger` | 애니메이터 파라미터명 | string | 읽음 MonsterBase.cs:2233 | 대상 · **행 없음** | 다름 | 2회 / 0 / 2026-09-10 (몹 2·보스 0) | 0 |
| `attackFinishTrigger` | 애니메이터 파라미터명 | string | 읽음 MonsterBase.cs:1228 | 대상 · **행 없음** | 다름 | 1회 / 0 / 2026-09-10 (몹 1·보스 0) | 0 |
| `hitTrigger` | 애니메이터 파라미터명 | string | 읽음 MonsterBase.cs:2234 | 대상 · **행 없음** | 다름 | 1회 / 0 / 2026-09-10 (몹 1·보스 0) | 0 |
| `groggyBool` | 애니메이터 파라미터명 | string | 읽음 MonsterBase.cs:2595 | 대상 · **행 없음** | 다름 | 2회 / 0 / 2026-09-08 (몹 2·보스 0) | 0 |
| `deathTrigger` | 애니메이터 파라미터명 | string | 읽음 MonsterBase.cs:1797 | 대상 · **행 없음** | 다름 | 1회 / 0 / 2026-09-08 (몹 1·보스 0) | 0 |
| `locomotionState` | 애니메이터 파라미터명 | string | 읽음 MonsterBase.cs:2236 | 대상 · **행 없음** | 다름 | 0회(안 건드림) | 0 |
| `locomotionClipSpeed` | 애니 재생 속도 — 이동 클립 맞춤 | float | 읽음 MonsterBase.cs:2532 | 제외(DataTableIgnore) | 다름 | 0회(안 건드림) | - |
| `locomotionFullBlendSpeed` | 애니 재생 속도 — 이동 클립 맞춤 | float | 읽음 MonsterBase.cs:2533 | 제외(DataTableIgnore) | 다름 | 0회(안 건드림) | - |
| `locomotionIdleCycleSeconds` | 애니 재생 속도 — 이동 클립 맞춤 | float | 읽음 MonsterBase.cs:2534 | 제외(DataTableIgnore) | 다름 | 0회(안 건드림) | - |
| `locomotionMoveCycleSeconds` | 애니 재생 속도 — 이동 클립 맞춤 | float | 읽음 MonsterBase.cs:2535 | 제외(DataTableIgnore) | 다름 | 0회(안 건드림) | - |
| `locomotionFullBlendWhileMoving` | 애니 재생 속도 — 이동 클립 맞춤 | bool | 읽음 MonsterBase.cs:2537 | 대상 · **행 없음** | 다름 | 0회(안 건드림) | 0 |
| `locomotionAnimSpeedRange` | 애니 재생 속도 — 이동 클립 맞춤 | Vector2 | 읽음 MonsterBase.cs:2536 | 대상 · **행 없음** | 전부 같음 ({x: 0.5, y: 2.5}) | 0회(안 건드림) | 0 |
| `locomotionPlaybackScale` | 애니 재생 속도 — 이동 클립 맞춤 | float | 읽음 MonsterBase.cs:2538 | 제외(DataTableIgnore) | 다름 | 0회(안 건드림) | - |
| `animatorControllerOverride` | 애니메이터 컨트롤러 교체 (선택) | RuntimeAnimatorController | 읽음 MonsterBase.cs:179 | 대상 · **행 없음** | 다름 | 0회(안 건드림) | 0 |
| `telegraphStyle` | 공격 예고 색·마스크 (아트 조절) | TelegraphStyleSO | 읽음 TelegraphStyleSO.cs:136 | 제외(DataTableIgnore) | 전부 같음 ({fileID: 11400000, guid: ) | 0회(안 건드림) | - |

### BossDataSO  (`Assets/1.Scripts/Monster/Boss/BossDataSO.cs`)

| 칸 | Header | 타입 | 코드 읽기 | xlsx | 에셋 값 | 에셋(git) 값 변경: 커밋 수 / tune·팀장 / 마지막 | xlsx(SVN) 값 변경 리비전 수 · xlsx≠에셋 칸 수 |
|---|---|---|---|---|---|---|---|
| `attacks` | 공격 테이블 — 배열 인덱스 = 쿨다운 슬롯 번호 | BossAttackEntry | 읽음 TwentyThreeBoss.cs:489 | 대상 · **행 없음** | 다름 | 0회(안 건드림) | 0 |
| `repeatPenalty` | 공격 선택기 | float | 읽음 TwentyThreeBoss.cs:1123 | 대상 · 행 1 | 전부 같음 (0.3) | 0회(안 건드림) | 0 |
| `repeatBlockAfter` | 공격 선택기 | int | 읽음 TwentyThreeBoss.cs:1116 | 대상 · 행 1 | 전부 같음 (2) | 0회(안 건드림) | 0 |
| `combos` | 콤보 — 단타 행 재사용(PLAN-boss-comb | BossComboEntry | 읽음 TwentyThreeBoss.cs:1284 | 제외(DataTableIgnore) | 다름 (에셋 1개엔 칸 없음) | 0회(안 건드림) | - |
| `openingCombo` | 콤보 — 단타 행 재사용(PLAN-boss-comb | BossAttackId | 읽음 TwentyThreeBoss.cs:1266 | 제외(DataTableIgnore) | 다름 (에셋 1개엔 칸 없음) | 0회(안 건드림) | - |
| `comboStepInterval` | 콤보 — 단타 행 재사용(PLAN-boss-comb | float | 읽음 TwentyThreeBoss.cs:1365 | 대상 · **행 없음** | 다름 (에셋 1개엔 칸 없음) | 0회(안 건드림) | 0 |
| `comboChaseGrace` | 콤보 — 단타 행 재사용(PLAN-boss-comb | float | 읽음 TwentyThreeBoss.cs:1378 | 대상 · **행 없음** | 다름 (에셋 1개엔 칸 없음) | 0회(안 건드림) | 0 |
| `raidOpeningStepInterval` | 콤보 — 단타 행 재사용(PLAN-boss-comb | float | 읽음 TwentyThreeBoss.cs:1365 | 대상 · **행 없음** | 다름 (에셋 1개엔 칸 없음) | 0회(안 건드림) | 0 |
| `phases` | 페이즈 — hpThreshold 내림차순으로 저작 | BossPhaseEntry | 읽음 TwentyThreeBoss.cs:431 | 대상 · **행 없음** | 다름 | 0회(안 건드림) | 0 |
| `partyHpMultipliers` | 레이드 변형 (PLAN-boss-raid.md) — | float | 읽음 TwentyThreeBoss.cs:4546 | 대상 · 행 3 | 다름 (에셋 2개엔 칸 없음) | 0회(안 건드림) | 0 |
| `playerHitDamageCapRatio` | 레이드 변형 (PLAN-boss-raid.md) — | float | 읽음 TwentyThreeBoss.cs:6423 | 대상 · 행 1 | 다름 (에셋 2개엔 칸 없음) | 0회(안 건드림) | 0 |
| `raidScript` | 레이드 변형 (PLAN-boss-raid.md) — | bool | 읽음 TwentyThreeBoss.cs:293 | 대상 · **행 없음** | 다름 (에셋 2개엔 칸 없음) | 0회(안 건드림) | 0 |
| `raidChargeTimeLimit` | 레이드 변형 (PLAN-boss-raid.md) — | float | 읽음 TwentyThreeBoss.cs:5429 | 대상 · 행 1 | 다름 (에셋 2개엔 칸 없음) | 0회(안 건드림) | 0 |
| `raidDroneGuideCooldown` | 레이드 변형 (PLAN-boss-raid.md) — | float | 읽음 TwentyThreeBoss.cs:4591 | 대상 · 행 1 | 다름 (에셋 2개엔 칸 없음) | 0회(안 건드림) | 0 |
| `raidDischargeWarnTime` | 레이드 변형 (PLAN-boss-raid.md) — | float | 읽음 TwentyThreeBoss.cs:4519 | 대상 · 행 1 | 다름 (에셋 2개엔 칸 없음) | 0회(안 건드림) | 0 |
| `raidDischargeDamage` | 레이드 변형 (PLAN-boss-raid.md) — | int | 읽음 TwentyThreeBoss.cs:4520 | 대상 · 행 1 | 다름 (에셋 2개엔 칸 없음) | 0회(안 건드림) | 0 |
| `raidPylonHpMultiplier` | 레이드 변형 (PLAN-boss-raid.md) — | float | 읽음 TwentyThreeBoss.cs:4330 | 대상 · 행 1 | 에셋에 없음(코드 기본값) | 0회(안 건드림) | 0 |
| `raidSelfDestructTime` | 레이드 ④ 폐기 처분 — 자폭 카운트다운 + 체력바 | float | 읽음 TwentyThreeBoss.cs:4552 | 대상 · 행 1 | 다름 (에셋 2개엔 칸 없음) | 0회(안 건드림) | 1회 |
| `raidSelfDestructGaugeDamage` | 레이드 ④ 폐기 처분 — 자폭 카운트다운 + 체력바 | int | 읽음 TwentyThreeBoss.cs:4546 | 대상 · 행 1 | 다름 (에셋 2개엔 칸 없음) | 0회(안 건드림) | 1회 |
| `raidSelfDestructCounterBonus` | 레이드 ④ 폐기 처분 — 자폭 카운트다운 + 체력바 | float | **죽은 칸** — 참조 0 · Tooltip 미사용 [Tooltip/Obsolete 표시] | 대상 · 행 1 | 에셋에 없음(코드 기본값) | 0회(안 건드림) | 0 |
| `raidSelfDestructCounterInterval` | 레이드 ④ 폐기 처분 — 자폭 카운트다운 + 체력바 | float | **죽은 칸** — 참조 0 · Tooltip 미사용 [Tooltip/Obsolete 표시] | 대상 · 행 1 | 에셋에 없음(코드 기본값) | 0회(안 건드림) | 0 |
| `raidSelfDestructCounterWindow` | 레이드 ④ 폐기 처분 — 자폭 카운트다운 + 체력바 | float | **죽은 칸** — 참조 0 · 주석상 미사용 | 대상 · 행 1 | 에셋에 없음(코드 기본값) | 0회(안 건드림) | 0 |
| `raidSelfDestructDamageReduction` | 레이드 ④ 폐기 처분 — 자폭 카운트다운 + 체력바 | float | **죽은 칸** — 참조 0 · Tooltip 미사용 [Tooltip/Obsolete 표시] | 대상 · 행 1 | 에셋에 없음(코드 기본값) | 0회(안 건드림) | 0 |
| `raidSelfDestructBlastWarn` | 레이드 ④ 폐기 처분 — 자폭 카운트다운 + 체력바 | float | 읽음 TwentyThreeBoss.cs:4578 | 대상 · 행 1 | 에셋에 없음(코드 기본값) | 0회(안 건드림) | 0 |
| `raidSelfDestructBlastDamage` | 레이드 ④ 폐기 처분 — 자폭 카운트다운 + 체력바 | int | 읽음 TwentyThreeBoss.cs:4580 | 대상 · 행 1 | 에셋에 없음(코드 기본값) | 0회(안 건드림) | 0 |
| `raidSelfDestructBlastLethal` | 레이드 ④ 폐기 처분 — 자폭 카운트다운 + 체력바 | bool | 읽음 TwentyThreeBoss.cs:4581 | 대상 · **행 없음** | 에셋에 없음(코드 기본값) | 0회(안 건드림) | 0 |
| `counterFrontAngle` | 카운터 / 그로기 — maxGroggyCount·g | float | 읽음 BossDirectionIndicator.cs:525 | 대상 · 행 1 | 전부 같음 (60) | 0회(안 건드림) | 0 |
| `backAttackAngle` | 카운터 / 그로기 — maxGroggyCount·g | float | 읽음 MonsterBase.cs:1510 | 대상 · 행 1 | 전부 같음 (60) | 0회(안 건드림) | 0 |
| `hitReactionState` | 카운터 / 그로기 — maxGroggyCount·g | string | 읽음 TwentyThreeBoss.cs:630 | 대상 · **행 없음** | 전부 같음 (getowned) | 0회(안 건드림) | 0 |
| `hitReactionStateRight` | 카운터 / 그로기 — maxGroggyCount·g | string | 읽음 TwentyThreeBoss.cs:631 | 대상 · **행 없음** | 전부 같음 (getowned_R) | 0회(안 건드림) | 0 |
| `breakDuration` | 카운터 / 그로기 — maxGroggyCount·g | float | 읽음 TwentyThreeBoss.cs:5788 | 대상 · 행 1 | 전부 같음 (5) | 2회 / 0 / 2026-09-29 | 0 |
| `counterGaugeStep` | 간파 게이지 · 제압 (팀 기획 09-28) | float | 읽음 TwentyThreeBoss.cs:5786 | 대상 · 행 1 | 전부 같음 (20) | 0회(안 건드림) | 0 |
| `environmentGaugeStep` | 간파 게이지 · 제압 (팀 기획 09-28) | float | 읽음 TwentyThreeBoss.cs:5955 | 대상 · 행 1 | 전부 같음 (20) | 0회(안 건드림) | 0 |
| `suppressDamageMultiplier` | 간파 게이지 · 제압 (팀 기획 09-28) | float | 읽음 TwentyThreeBoss.cs:6197 | 대상 · 행 1 | 전부 같음 (1.2) | 0회(안 건드림) | 0 |
| `vulnerableDuration` | 취약 (팀 기획 `Re_C_취약_및_제압_시스템.m | float | 읽음 TwentyThreeBoss.cs:5928 | 대상 · 행 1 | 다름 | 1회 / 1 / 2026-10-07 | 2회 · 불일치 2 |
| `vulnerableKnockbackDistance` | 취약 (팀 기획 `Re_C_취약_및_제압_시스템.m | float | 읽음 TwentyThreeBoss.cs:5929 | 대상 · 행 1 | 전부 같음 (3) | 0회(안 건드림) | 0 |
| `vulnerableKnockbackTime` | 취약 (팀 기획 `Re_C_취약_및_제압_시스템.m | float | 읽음 TwentyThreeBoss.cs:5930 | 대상 · 행 1 | 전부 같음 (0.35) | 0회(안 건드림) | 0 |
| `wallReboundDistance` | 취약 (팀 기획 `Re_C_취약_및_제압_시스템.m | float | 읽음 TwentyThreeBoss.cs:5931 | 대상 · 행 1 | 전부 같음 (0.5) | 0회(안 건드림) | 0 |
| `wallReboundTime` | 취약 (팀 기획 `Re_C_취약_및_제압_시스템.m | float | 읽음 TwentyThreeBoss.cs:5932 | 대상 · 행 1 | 전부 같음 (0.3) | 0회(안 건드림) | 0 |
| `vulnerableTint` | 취약 (팀 기획 `Re_C_취약_및_제압_시스템.m | Color | 읽음 TwentyThreeBoss.cs:6171 — 임시 표시(VFX 대기) | 대상 · **행 없음** | 전부 같음 ({r: 0.45, g: 0.75, b: 1, ) | 0회(안 건드림) | 0 |
| `aggroRetargetInterval` | 취약 (팀 기획 `Re_C_취약_및_제압_시스템.m | float | 읽음 TwentyThreeBoss.cs:820 | 대상 · 행 1 | 전부 같음 (8) | 0회(안 건드림) | 0 |
| `aggroAvoidsRepeatTarget` | 취약 (팀 기획 `Re_C_취약_및_제압_시스템.m | bool | 읽음 TwentyThreeBoss.cs:729 | 대상 · **행 없음** | 다름 | 1회 / 1 / 2026-09-03 | 0 |
| `threatAggro` | 위협 어그로 — 피해량 기반(PLAN-boss-ag | bool | 읽음 TwentyThreeBoss.cs:748 | 대상 · **행 없음** | 에셋에 없음(코드 기본값) | 0회(안 건드림) | 0 |
| `threatHalfLife` | 위협 어그로 — 피해량 기반(PLAN-boss-ag | float | 읽음 TwentyThreeBoss.cs:767 | 대상 · 행 1 | 에셋에 없음(코드 기본값) | 0회(안 건드림) | 0 |
| `threatSwitchRatio` | 위협 어그로 — 피해량 기반(PLAN-boss-ag | float | 읽음 TwentyThreeBoss.cs:795 | 대상 · 행 1 | 에셋에 없음(코드 기본값) | 0회(안 건드림) | 0 |
| `threatMinHold` | 위협 어그로 — 피해량 기반(PLAN-boss-ag | float | 읽음 TwentyThreeBoss.cs:795 | 대상 · 행 1 | 에셋에 없음(코드 기본값) | 0회(안 건드림) | 0 |
| `threatMinScore` | 위협 어그로 — 피해량 기반(PLAN-boss-ag | float | 읽음 TwentyThreeBoss.cs:784 | 대상 · 행 1 | 에셋에 없음(코드 기본값) | 0회(안 건드림) | 0 |
| `farPressureDistance` | 위협 어그로 — 피해량 기반(PLAN-boss-ag | float | 읽음 TwentyThreeBoss.cs:1073 | 대상 · 행 1 | 에셋에 없음(코드 기본값) | 0회(안 건드림) | 0 |
| `farPressureWeightMultiplier` | 위협 어그로 — 피해량 기반(PLAN-boss-ag | float | 읽음 TwentyThreeBoss.cs:1098 | 대상 · 행 1 | 에셋에 없음(코드 기본값) | 0회(안 건드림) | 0 |
| `farPressureCooldownScale` | 위협 어그로 — 피해량 기반(PLAN-boss-ag | float | 읽음 TwentyThreeBoss.cs:1096 | 대상 · 행 1 | 에셋에 없음(코드 기본값) | 0회(안 건드림) | 0 |
| `chargeClearGroggyDuration` | 위협 어그로 — 피해량 기반(PLAN-boss-ag | float | 읽음 TwentyThreeBoss.cs:4469 | 대상 · 행 1 | 전부 같음 (1.5) | 1회 / 1 / 2026-09-03 | 0 |
| `grabRadius` | Grab 체인 — 값은 플레이로 튜닝(PLAN §9 | float | 읽음 TwentyThreeBoss.cs:3105 | 대상 · 행 1 | 전부 같음 (3.74) | 1회 / 0 / 2026-09-15 | 0 |
| `grabHoldDuration` | Grab 체인 — 값은 플레이로 튜닝(PLAN §9 | float | 읽음 TwentyThreeBoss.cs:3097 | 대상 · 행 1 | 전부 같음 (1.13) | 1회 / 0 / 2026-09-17 | 0 |
| `grabTickInterval` | Grab 체인 — 값은 플레이로 튜닝(PLAN §9 | float | 읽음 TwentyThreeBoss.cs:2746 | 대상 · 행 1 | 전부 같음 (0.5) | 0회(안 건드림) | 0 |
| `grabTickDamage` | Grab 체인 — 값은 플레이로 튜닝(PLAN §9 | int | 읽음 TwentyThreeBoss.cs:2746 | 대상 · 행 1 | 다름 | 1회 / 1 / 2026-10-07 | 1회 |
| `grabThrowDuration` | Grab 체인 — 값은 플레이로 튜닝(PLAN §9 | float | 읽음 TwentyThreeBoss.cs:3098 | 대상 · 행 1 | 전부 같음 (0.65) | 1회 / 0 / 2026-09-17 | 0 |
| `grabThrowDistance` | Grab 체인 — 값은 플레이로 튜닝(PLAN §9 | float | **죽은 칸** — 읽지만 OnGrabThrowRelease 가 경고 로그만 → 효과 0 | 대상 · 행 1 | 전부 같음 (6) | 0회(안 건드림) | 0 |
| `grabThrowDamage` | Grab 체인 — 값은 플레이로 튜닝(PLAN §9 | int | 읽음 TwentyThreeBoss.cs:2803 | 대상 · 행 1 | 전부 같음 (20) | 0회(안 건드림) | 0 |
| `grabRecoveryDuration` | Grab 체인 — 값은 플레이로 튜닝(PLAN §9 | float | 읽음 TwentyThreeBoss.cs:3099 | 대상 · 행 1 | 전부 같음 (0.8) | 0회(안 건드림) | 0 |
| `grabHoldState` | Grab 체인 — 값은 플레이로 튜닝(PLAN §9 | string | 읽음 TwentyThreeBoss.cs:3123 | 대상 · **행 없음** | 전부 같음 (Holding) | 0회(안 건드림) | 0 |
| `grabThrowState` | Grab 체인 — 값은 플레이로 튜닝(PLAN §9 | string | 읽음 TwentyThreeBoss.cs:3124 | 대상 · **행 없음** | 전부 같음 (Throw) | 0회(안 건드림) | 0 |
| `grabCycleSpeed` | Grab 체인 — 값은 플레이로 튜닝(PLAN §9 | float | 읽음 TwentyThreeBoss.cs:3094 | 대상 · 행 1 | 전부 같음 (1.4) | 0회(안 건드림) | 0 |
| `grabPullDuration` | Grab 체인 — 값은 플레이로 튜닝(PLAN §9 | float | 읽음 TwentyThreeBoss.cs:3100 | 대상 · 행 1 | 전부 같음 (1.65) | 0회(안 건드림) | 0 |
| `grabCatchDuration` | Grab 체인 — 값은 플레이로 튜닝(PLAN §9 | float | 읽음 TwentyThreeBoss.cs:3101 | 대상 · 행 1 | 전부 같음 (1.1) | 0회(안 건드림) | 0 |
| `grabSlamCount` | Grab 체인 — 값은 플레이로 튜닝(PLAN §9 | int | 읽음 TwentyThreeBoss.cs:3103 | 대상 · 행 1 | 전부 같음 (3) | 0회(안 건드림) | 0 |
| `grabSlamDamage` | Grab 체인 — 값은 플레이로 튜닝(PLAN §9 | int | 읽음 TwentyThreeBoss.cs:2803 | 대상 · 행 1 | 다름 | 1회 / 1 / 2026-10-07 | 1회 |
| `grabEndDuration` | Grab 체인 — 값은 플레이로 튜닝(PLAN §9 | float | 읽음 TwentyThreeBoss.cs:3102 | 대상 · 행 1 | 전부 같음 (1.37) | 0회(안 건드림) | 0 |
| `grabCatchState` | Grab 체인 — 값은 플레이로 튜닝(PLAN §9 | string | 읽음 TwentyThreeBoss.cs:632 | 대상 · **행 없음** | 전부 같음 (Grab) | 0회(안 건드림) | 0 |
| `grabEndState` | Grab 체인 — 값은 플레이로 튜닝(PLAN §9 | string | 읽음 TwentyThreeBoss.cs:633 | 대상 · **행 없음** | 전부 같음 (GrabEnd) | 0회(안 건드림) | 0 |
| `grabPullKnockback` | Grab 체인 — 값은 플레이로 튜닝(PLAN §9 | float | 읽음 TwentyThreeBoss.cs:3104 | 대상 · 행 1 | 전부 같음 (4) | 0회(안 건드림) | 0 |
| `playerScanRadius` | 플레이어 탐색 반경 (공용) | float | 읽음 TwentyThreeBoss.cs:3473 | 대상 · 행 1 | 전부 같음 (45) | 0회(안 건드림) | 0 |
| `jumpHoverTime` | JumpAttack — 거리 무관 / 최원거리 플레 | float | 읽음 TwentyThreeBoss.cs:3532 | 대상 · 행 1 | 전부 같음 (1.2) | 0회(안 건드림) | 0 |
| `jumpLandingDuration` | JumpAttack — 거리 무관 / 최원거리 플레 | float | 읽음 TwentyThreeBoss.cs:3533 | 대상 · 행 1 | 전부 같음 (1) | 0회(안 건드림) | 0 |
| `jumpRecoveryDuration` | JumpAttack — 거리 무관 / 최원거리 플레 | float | 읽음 TwentyThreeBoss.cs:3534 | 대상 · 행 1 | 전부 같음 (0.4) | 0회(안 건드림) | 0 |
| `jumpAoeRadius` | JumpAttack — 거리 무관 / 최원거리 플레 | float | 읽음 TwentyThreeBoss.cs:3537 | 대상 · 행 1 | 전부 같음 (5.25) | 1회 / 0 / 2026-09-15 | 0 |
| `jumpLandingDamage` | JumpAttack — 거리 무관 / 최원거리 플레 | int | 읽음 TwentyThreeBoss.cs:3354 | 대상 · 행 1 | 다름 | 1회 / 1 / 2026-10-07 | 1회 |
| `jumpTakeoffState` | JumpAttack — 거리 무관 / 최원거리 플레 | string | 읽음 TwentyThreeBoss.cs:634 | 대상 · **행 없음** | 전부 같음 (Leap) | 0회(안 건드림) | 0 |
| `jumpTakeoffDuration` | JumpAttack — 거리 무관 / 최원거리 플레 | float | 읽음 TwentyThreeBoss.cs:3535 | 대상 · 행 1 | 전부 같음 (0.633) | 0회(안 건드림) | 0 |
| `jumpHoverState` | JumpAttack — 거리 무관 / 최원거리 플레 | string | 읽음 TwentyThreeBoss.cs:3995 | 대상 · **행 없음** | 전부 같음 (JumpHover) | 0회(안 건드림) | 0 |
| `jumpLandingState` | JumpAttack — 거리 무관 / 최원거리 플레 | string | 읽음 TwentyThreeBoss.cs:3993 | 대상 · **행 없음** | 전부 같음 (JumpLanding) | 0회(안 건드림) | 0 |
| `silentLandingState` | JumpAttack — 거리 무관 / 최원거리 플레 | string | 읽음 TwentyThreeBoss.cs:3991 | 대상 · **행 없음** | 전부 같음 (EntranceLanding) | 0회(안 건드림) | 0 |
| `jumpTelegraphPrefab` | JumpAttack — 거리 무관 / 최원거리 플레 | GameObject | **죽은 칸** — 참조 0 · Tooltip 미사용 [Tooltip/Obsolete 표시] | 대상 · **행 없음** | 전부 같음 ({fileID: 5997264177071607) | 2회 / 0 / 2026-09-04 | 0 |
| `jumpLandSeparation` | JumpAttack — 거리 무관 / 최원거리 플레 | float | 읽음 TwentyThreeBoss.cs:3538 | 대상 · 행 1 | 전부 같음 (1.2) | 0회(안 건드림) | 0 |
| `jumpKnockbackStrength` | JumpAttack — 거리 무관 / 최원거리 플레 | float | 읽음 TwentyThreeBoss.cs:3541 | 대상 · 행 1 | 다름 | 1회 / 1 / 2026-08-13 | 0 |
| `chargeTimeLimit` | 송전기(차징) — 페이즈 진입 시퀀스 | float | 읽음 TwentyThreeBoss.cs:5429 | 대상 · 행 1 | 전부 같음 (30) | 1회 / 1 / 2026-10-02 | 0 |
| `chargeLoopStartFrame` | 송전기(차징) — 페이즈 진입 시퀀스 | int | 읽음 TwentyThreeBoss.cs:483 | 대상 · 행 1 | 전부 같음 (62) | 1회 / 1 / 2026-10-02 | 0 |
| `chargeLoopEndFrame` | 송전기(차징) — 페이즈 진입 시퀀스 | int | 읽음 TwentyThreeBoss.cs:483 | 대상 · 행 1 | 전부 같음 (125) | 0회(안 건드림) | 0 |
| `chargeZonePrefab` | 송전기(차징) — 페이즈 진입 시퀀스 | GameObject | 읽음 TwentyThreeBoss.cs:5315 | 대상 · **행 없음** | 전부 같음 ({fileID: 0}) | 0회(안 건드림) | 0 |
| `chargePylonsSolo` | 송전기(차징) — 페이즈 진입 시퀀스 | int | 읽음 TwentyThreeBoss.cs:5374 | 대상 · 행 1 | 전부 같음 (1) | 0회(안 건드림) | 0 |
| `chargePylonsDuo` | 송전기(차징) — 페이즈 진입 시퀀스 | int | 읽음 TwentyThreeBoss.cs:5375 | 대상 · 행 1 | 전부 같음 (2) | 0회(안 건드림) | 0 |
| `chargePylonsTrioPlus` | 송전기(차징) — 페이즈 진입 시퀀스 | int | 읽음 TwentyThreeBoss.cs:5376 | 대상 · 행 1 | 전부 같음 (4) | 0회(안 건드림) | 0 |
| `rageDashCount` | 레이지 — 송전기 실패 벌칙 | int | 읽음 TwentyThreeBoss.cs:4614 | 대상 · 행 1 | 전부 같음 (3) | 0회(안 건드림) | 0 |
| `rageDashInterval` | 레이지 — 송전기 실패 벌칙 | float | 읽음 TwentyThreeBoss.cs:5431 | 대상 · 행 1 | 전부 같음 (0.5) | 0회(안 건드림) | 0 |
| `rageDashDuration` | 레이지 — 송전기 실패 벌칙 | float | 읽음 TwentyThreeBoss.cs:5430 | 대상 · 행 1 | 전부 같음 (0.7) | 0회(안 건드림) | 0 |
| `rageDashSpeedMultiplier` | 레이지 — 송전기 실패 벌칙 | float | 읽음 TwentyThreeBoss.cs:5432 | 대상 · 행 1 | 다름 | 1회 / 1 / 2026-08-13 | 0 |
| `rageDashMaxDistance` | 레이지 — 송전기 실패 벌칙 | float | 읽음 TwentyThreeBoss.cs:5439 | 대상 · 행 1 | 전부 같음 (16) | 0회(안 건드림) | 0 |
| `rageDashDamage` | 레이지 — 송전기 실패 벌칙 | int | 읽음 TwentyThreeBoss.cs:4719 | 대상 · 행 1 | 다름 | 1회 / 1 / 2026-10-07 | 1회 |
| `dashDuration` | 돌진(Dash) — [S5] | float | 읽음 TwentyThreeBoss.cs:5434 | 대상 · 행 1 | 다름 | 2회 / 0 / 2026-09-21 | 0 |
| `dashSpeedMultiplier` | 돌진(Dash) — [S5] | float | 읽음 TwentyThreeBoss.cs:5435 | 대상 · 행 1 | 다름 | 1회 / 0 / 2026-09-21 | 0 |
| `dashMaxDistance` | 돌진(Dash) — [S5] | float | 읽음 TwentyThreeBoss.cs:5436 | 대상 · 행 1 | 다름 | 1회 / 0 / 2026-09-21 | 0 |
| `dashDamage` | 돌진(Dash) — [S5] | int | 읽음 TwentyThreeBoss.cs:4849 | 대상 · 행 1 | 다름 | 1회 / 1 / 2026-10-07 | 1회 |
| `dashCarryFrontOffset` | 돌진(Dash) — [S5] | float | 읽음 TwentyThreeBoss.cs:5437 | 대상 · 행 1 | 다름 | 1회 / 1 / 2026-09-19 | 0 |
| `dashStunDuration` | 돌진(Dash) — [S5] | float | 읽음 TwentyThreeBoss.cs:5438 | 대상 · 행 1 | 전부 같음 (1) | 0회(안 건드림) | 0 |
| `dashKnockbackStrength` | 돌진(Dash) — [S5] | float | 읽음 TwentyThreeBoss.cs:3542 | 대상 · 행 1 | 다름 | 1회 / 1 / 2026-08-13 | 0 |
| `bombThrowInterval` | Wells (23호에 종속) — [S8] | float | **죽은 칸** — 읽지만 콜백 OnWellsAttackCycle 이 빈 함수 → 효과 0 | 대상 · 행 1 | 전부 같음 (6) | 0회(안 건드림) | 0 |
| `bombPrefab` | Wells (23호에 종속) — [S8] | GameObject | **죽은 칸** — 참조 0 (폭탄 투척 09-16 제거) | 대상 · **행 없음** | 다름 | 1회 / 0 / 2026-08-13 | 0 |
| `spreadAngle` | Wells (23호에 종속) — [S8] | float | **죽은 칸** — 참조 0 (폭탄 투척 제거) | 대상 · 행 1 | 전부 같음 (15) | 0회(안 건드림) | 0 |
| `electricFloor` | 전기 장판 · 자폭 드론 (PLAN-boss-ele | BossElectricFloorDataSO | 읽음 TwentyThreeBoss.cs:474 | 대상 · **행 없음** | 다름 | 1회 / 1 / 2026-10-14 | 0 |
| `wellsDrone` | 전기 장판 · 자폭 드론 (PLAN-boss-ele | WellsDroneDataSO | 읽음 TwentyThreeBoss.cs:476 | 대상 · **행 없음** | 전부 같음 ({fileID: 11400000, guid: ) | 0회(안 건드림) | 0 |
| `globalAttackInterval` | 공격 간격 — 전역 | float | 읽음 TwentyThreeBoss.cs:4392 | 대상 · 행 1 | 전부 같음 (1.5) | 0회(안 건드림) | 1회 · 불일치 2 |
| `chargeAuraRadius` | 차징 오라 — 접근 차단 | float | 읽음 TwentyThreeBoss.cs:5307 | 대상 · 행 1 | 다름 | 1회 / 0 / 2026-09-21 | 0 |
| `chargeAuraInterval` | 차징 오라 — 접근 차단 | float | 읽음 TwentyThreeBoss.cs:5308 | 대상 · 행 1 | 전부 같음 (1) | 0회(안 건드림) | 0 |
| `chargeAuraDamage` | 차징 오라 — 접근 차단 | int | 읽음 TwentyThreeBoss.cs:5309 | 대상 · 행 1 | 전부 같음 (20) | 0회(안 건드림) | 0 |
| `chargeAuraKnockbackStrength` | 차징 오라 — 접근 차단 | float | 읽음 TwentyThreeBoss.cs:5310 | 대상 · 행 1 | 다름 | 1회 / 1 / 2026-08-13 | 0 |
| `chargeAuraTelegraphPrefab` | 차징 오라 — 접근 차단 | GameObject | 읽음 TwentyThreeBoss.cs:5240 | 대상 · **행 없음** | 전부 같음 ({fileID: 0}) | 2회 / 0 / 2026-09-29 | 0 |

### BossAttackEntry  (`Assets/1.Scripts/Monster/Boss/BossDataSO.cs`)

| 칸 | Header | 타입 | 코드 읽기 | xlsx | 에셋 값 | 에셋(git) 값 변경: 커밋 수 / tune·팀장 / 마지막 | xlsx(SVN) 값 변경 리비전 수 · xlsx≠에셋 칸 수 |
|---|---|---|---|---|---|---|---|
| `attackId` |  | BossAttackId | 읽음 TwentyThreeBoss.cs (다수) | 대상 · **행 없음** | 다름 / 행마다 다름 | 0회(안 건드림) | 0 |
| `animatorStateName` |  | string | 읽음 TwentyThreeBoss.cs:647 | 대상 · **행 없음** | 다름 / 행마다 다름 | 1회 / 0 / 2026-09-17 | 0 |
| `cooldown` |  | float | 읽음 TwentyThreeBoss.cs:492 | 대상 · 행 8 | 다름 / 행마다 다름 | 1회 / 0 / 2026-09-17 | 1회 · 불일치 12 |
| `minDistance` |  | float | 읽음 TwentyThreeBoss.cs:1092 | 대상 · 행 8 | 다름 / 행마다 같음 | 3회 / 2 / 2026-09-17 | 0 |
| `maxDistance` |  | float | 읽음 TwentyThreeBoss.cs:868 | 대상 · 행 8 | 다름 / 행마다 다름 | 5회 / 0 / 2026-09-15 | 0 |
| `ignoreDistanceWindow` |  | bool | 읽음 BossContactReachPolicy.cs:32 | 대상 · **행 없음** | 다름 / 행마다 다름 | 1회 / 0 / 2026-09-17 | 0 |
| `attackTargeting` |  | BossAttackTargeting | 읽음 TwentyThreeBoss.cs:569 | 대상 · **행 없음** | 다름 / 행마다 다름 | 1회 / 0 / 2026-09-17 | 0 |
| `weight` |  | float | 읽음 TwentyThreeBoss.cs:1083 | 대상 · 행 8 | 다름 / 행마다 다름 | 2회 / 2 / 2026-09-03 | 0 |
| `allowedFromPhase` |  | int | 읽음 TwentyThreeBoss.cs:1084 | 대상 · 행 8 | 다름 / 행마다 같음 | 0회(안 건드림) | 0 |
| `damage` |  | int | 읽음 TwentyThreeBoss.cs:1730 | 대상 · 행 8 | 다름 / 행마다 다름 | 1회 / 1 / 2026-10-07 | 1회 |
| `opensCounterWindow` |  | bool | 읽음 TwentyThreeBoss.cs:106 | 대상 · **행 없음** | 다름 / 행마다 다름 | 0회(안 건드림) | 0 |
| `counterWindowDuration` |  | float | 읽음 TwentyThreeBoss.cs:107 | 대상 · 행 8 | 다름 / 행마다 다름 | 2회 / 1 / 2026-09-03 | 0 |
| `superArmor` |  | bool | 읽음 TwentyThreeBoss.cs:1542 | 대상 · **행 없음** | 다름 / 행마다 같음 | 0회(안 건드림) | 0 |
| `hitboxAnchorName` |  | string | 읽음 TwentyThreeBoss.cs:885 | 대상 · **행 없음** | 다름 / 행마다 다름 | 2회 / 0 / 2026-08-10 | 0 |
| `lungeDistance` |  | float | 읽음 TwentyThreeBoss.cs:892 | 대상 · 행 8 | 다름 / 행마다 다름 | 1회 / 0 / 2026-09-18 | 0 |
| `lungeSpeedMultiplier` |  | float | 읽음 TwentyThreeBoss.cs:902 | 대상 · 행 8 | 다름 / 행마다 같음 | 0회(안 건드림) | 0 |
| `lungePathRadius` |  | float | 읽음 TwentyThreeBoss.cs:892 | 대상 · 행 8 | 다름 / 행마다 다름 | 1회 / 0 / 2026-09-18 | 0 |
| `coneRadius` |  | float | 읽음 TwentyThreeBoss.cs:1771 | 대상 · 행 8 | 다름 / 행마다 다름 | 1회 / 0 / 2026-09-18 | 0 |
| `coneAngle` |  | float | 읽음 TwentyThreeBoss.cs:1776 | 대상 · 행 8 | 다름 / 행마다 다름 | 1회 / 0 / 2026-09-18 | 0 |
| `coneOffsetAngle` |  | float | 읽음 TwentyThreeBoss.cs:1790 | 대상 · 행 8 | 다름 / 행마다 다름 | 0회(안 건드림) | 0 |
| `boxWidth` |  | float | 읽음 TwentyThreeBoss.cs:892 | 대상 · 행 8 | 다름 / 행마다 다름 | 0회(안 건드림) | 0 |
| `boxLength` |  | float | 읽음 TwentyThreeBoss.cs:1757 | 대상 · 행 8 | 다름 / 행마다 다름 | 0회(안 건드림) | 0 |
| `boxLateralOffset` |  | float | 읽음 TwentyThreeBoss.cs:1766 | 대상 · 행 8 | 다름 / 행마다 다름 | 0회(안 건드림) | 0 |
| `boxBackOffset` |  | float | 읽음 TwentyThreeBoss.cs:1767 | 대상 · 행 8 | 다름 / 행마다 다름 | 0회(안 건드림) | 0 |
| `telegraphDuration` |  | float | 읽음 TwentyThreeBoss.cs:1495 | 대상 · 행 8 | 다름 / 행마다 다름 | 1회 / 0 / 2026-09-29 | 0 |
| `telegraphFillInward` |  | bool | 읽음 TwentyThreeBoss.cs:1847 | 대상 · **행 없음** | 다름 / 행마다 다름 | 0회(안 건드림) | 0 |
| `hitEventFallbackNormalized` |  | float | 읽음 TwentyThreeBoss.cs:4754 | 대상 · 행 8 | 다름 / 행마다 다름 | 0회(안 건드림) | 0 |
| `prepStateName` |  | string | 읽음 TwentyThreeBoss.cs:661 | 대상 · **행 없음** | 다름 / 행마다 다름 | 0회(안 건드림) | 0 |
| `telegraphPoseNormalized` |  | float | 읽음 TwentyThreeBoss.cs:2004 | 대상 · 행 8 | 다름 / 행마다 같음 | 1회 / 0 / 2026-10-02 | 0 |

### BossPhaseEntry  (`Assets/1.Scripts/Monster/Boss/BossDataSO.cs`)

| 칸 | Header | 타입 | 코드 읽기 | xlsx | 에셋 값 | 에셋(git) 값 변경: 커밋 수 / tune·팀장 / 마지막 | xlsx(SVN) 값 변경 리비전 수 · xlsx≠에셋 칸 수 |
|---|---|---|---|---|---|---|---|
| `hpThreshold` |  | float | 읽음 TwentyThreeBoss.cs:600 | 대상 · 행 3 | 다름 / 행마다 다름 | 0회(안 건드림) | 0 |
| `sequence` |  | BossPhaseSequence | 읽음 BossRaidRules.cs:22 | 대상 · **행 없음** | 다름 / 행마다 다름 | 0회(안 건드림) | 0 |
| `damageMultiplier` |  | float | 읽음 TwentyThreeBoss.cs:435 | 대상 · 행 3 | 다름 / 행마다 다름 | 0회(안 건드림) | 0 |
| `speedMultiplier` |  | float | 읽음 TwentyThreeBoss.cs:439 | 대상 · 행 3 | 다름 / 행마다 다름 | 0회(안 건드림) | 0 |

### BossComboEntry  (`Assets/1.Scripts/Monster/Boss/BossComboEntry.cs`)

| 칸 | Header | 타입 | 코드 읽기 | xlsx | 에셋 값 | 에셋(git) 값 변경: 커밋 수 / tune·팀장 / 마지막 | xlsx(SVN) 값 변경 리비전 수 · xlsx≠에셋 칸 수 |
|---|---|---|---|---|---|---|---|
| `name` |  | string | 읽음 TwentyThreeBoss.cs:1337 — 로그 출력 전용 | 비대상 | 다름 / 행마다 다름 (에셋 1개엔 칸 없음) | 0회(안 건드림) | - |
| `weight` |  | float | 읽음 TwentyThreeBoss.cs:1307 | 비대상 | 다름 / 행마다 다름 (에셋 1개엔 칸 없음) | 2회 / 2 / 2026-09-03 | - |
| `allowedFromPhase` |  | int | 읽음 TwentyThreeBoss.cs:1307 | 비대상 | 다름 / 행마다 같음 (에셋 1개엔 칸 없음) | 0회(안 건드림) | - |
| `cooldown` |  | float | 읽음 TwentyThreeBoss.cs:1308 | 비대상 | 다름 / 행마다 같음 (에셋 1개엔 칸 없음) | 1회 / 0 / 2026-09-17 | - |
| `steps` |  | BossComboStep | 읽음 TwentyThreeBoss.cs:1298 | 비대상 | 다름 / 행마다 다름 (에셋 1개엔 칸 없음) | 0회(안 건드림) | - |

### BossComboStep  (`Assets/1.Scripts/Monster/Boss/BossComboEntry.cs`)

| 칸 | Header | 타입 | 코드 읽기 | xlsx | 에셋 값 | 에셋(git) 값 변경: 커밋 수 / tune·팀장 / 마지막 | xlsx(SVN) 값 변경 리비전 수 · xlsx≠에셋 칸 수 |
|---|---|---|---|---|---|---|---|
| `attack` |  | BossAttackId | 읽음 TwentyThreeBoss.cs:1300 | 비대상 | 다름 / 행마다 다름 (에셋 1개엔 칸 없음) | 0회(안 건드림) | - |
| `forceFire` |  | bool | 읽음 TwentyThreeBoss.cs:1378 | 비대상 | 다름 / 행마다 다름 (에셋 1개엔 칸 없음) | 0회(안 건드림) | - |

### BossElectricFloorDataSO  (`Assets/1.Scripts/Monster/Boss/BossElectricFloorDataSO.cs`)

| 칸 | Header | 타입 | 코드 읽기 | xlsx | 에셋 값 | 에셋(git) 값 변경: 커밋 수 / tune·팀장 / 마지막 | xlsx(SVN) 값 변경 리비전 수 · xlsx≠에셋 칸 수 |
|---|---|---|---|---|---|---|---|
| `firstDelay` | 일반 전투 (§4.2) | float | 읽음 BossElectricFloor.cs:181 | 비대상 | 전부 같음 (4) | 0회(안 건드림) | - |
| `normalWarnTime` | 일반 전투 (§4.2) | float | 읽음 BossElectricFloor.cs:203 | 비대상 | 전부 같음 (1.2) | 0회(안 건드림) | - |
| `normalGapAfterVfx` | 일반 전투 (§4.2) | float | 읽음 BossElectricFloor.cs:211 | 비대상 | 다름 | 0회(안 건드림) | - |
| `chargeWarnTime` | 송전기 충전 기믹 (§5) | float | 읽음 BossElectricFloor.cs:237 | 비대상 | 전부 같음 (2) | 0회(안 건드림) | - |
| `chargeGroupGap` | 송전기 충전 기믹 (§5) | float | 읽음 BossElectricFloor.cs:242 | 비대상 | 전부 같음 (1) | 0회(안 건드림) | - |
| `vfxDuration` | 공통 | float | 읽음 BossElectricFloor.cs:278 | 비대상 | 전부 같음 (0.4) | 0회(안 건드림) | - |
| `damage` | 공통 | int | 읽음 BossElectricFloor.cs:266 | 비대상 | 전부 같음 (20) | 0회(안 건드림) | - |
| `pauseOn` | 공통 | BossPauseCondition | 읽음 BossElectricFloor.cs:119 | 비대상 | 전부 같음 (3) | 0회(안 건드림) | - |
| `warnOuterColor` | 연출 (임시 — 민경 교체 훅) | Color | **죽은 칸** — Obsolete [Tooltip/Obsolete 표시] | 비대상 | 전부 같음 ({r: 1, g: 0.15, b: 0.1, a) | 0회(안 건드림) | - |
| `warnFillColor` | 연출 (임시 — 민경 교체 훅) | Color | **죽은 칸** — Obsolete [Tooltip/Obsolete 표시] | 비대상 | 전부 같음 ({r: 1, g: 0.05, b: 0.02, ) | 0회(안 건드림) | - |
| `electricColor` | 연출 (임시 — 민경 교체 훅) | Color | 읽음 BossElectricFloor.cs:390 | 비대상 | 전부 같음 ({r: 0.45, g: 0.85, b: 1, ) | 0회(안 건드림) | - |
| `electricFlickerSpeed` | 연출 (임시 — 민경 교체 훅) | float | 읽음 BossElectricFloor.cs:418 | 비대상 | 에셋에 없음(코드 기본값) | 0회(안 건드림) | - |
| `electricVfxPrefab` | 연출 (임시 — 민경 교체 훅) | GameObject | 읽음 BossElectricFloor.cs:380 | 비대상 | 전부 같음 ({fileID: 0}) | 0회(안 건드림) | - |
| `surfaceOffset` | 연출 (임시 — 민경 교체 훅) | float | 읽음 BossElectricFloor.cs:458 | 비대상 | 전부 같음 (0.06) | 0회(안 건드림) | - |

### WellsDroneDataSO  (`Assets/1.Scripts/Monster/Boss/WellsDroneDataSO.cs`)

| 칸 | Header | 타입 | 코드 읽기 | xlsx | 에셋 값 | 에셋(git) 값 변경: 커밋 수 / tune·팀장 / 마지막 | xlsx(SVN) 값 변경 리비전 수 · xlsx≠에셋 칸 수 |
|---|---|---|---|---|---|---|---|
| `firstDelay` | 시간 (§4) | float | 읽음 WellsDroneAttack.cs:86 | 비대상 | 에셋 1개: 5 | 0회(안 건드림) | - |
| `trackTime` | 시간 (§4) | float | 읽음 WellsDroneAttack.cs:136 | 비대상 | 에셋 1개: 3 | 0회(안 건드림) | - |
| `lockTime` | 시간 (§4) | float | 읽음 WellsDroneAttack.cs:156 | 비대상 | 에셋 1개: 1 | 0회(안 건드림) | - |
| `cooldown` | 시간 (§4) | float | 읽음 WellsDroneAttack.cs:147 | 비대상 | 에셋 1개: 7 | 0회(안 건드림) | - |
| `resumeMinDelay` | 시간 (§4) | float | 읽음 WellsDroneAttack.cs:119 | 비대상 | 에셋 1개: 2 | 0회(안 건드림) | - |
| `diameterInTiles` | 범위 · 피해 (§7 · §9) | float | 읽음 WellsDroneAttack.cs:194 | 비대상 | 에셋 1개: 1.25 | 1회 / 1 / 2026-10-02 | - |
| `playerDamage` | 범위 · 피해 (§7 · §9) | int | 읽음 WellsDroneAttack.cs:240 | 비대상 | 에셋 1개: 25 | 0회(안 건드림) | - |
| `pauseOn` | 범위 · 피해 (§7 · §9) | BossPauseCondition | 읽음 WellsDroneAttack.cs:92 | 비대상 | 에셋 1개: 3 | 0회(안 건드림) | - |
| `droneModel` | 드론 모델 | GameObject | 읽음 WellsDroneAttack.cs:367 | 비대상 | 에셋 1개: {fileID: 3484687064186887524,  | 1회 / 0 / 2026-10-16 | - |
| `droneScale` | 드론 모델 | float | 읽음 WellsDroneAttack.cs:383 | 비대상 | 에셋 1개: 2 | 0회(안 건드림) | - |
| `approachDistance` | 드론 비행 — 화면 좌하단/우하단 대각선에서 날아온 | float | 읽음 WellsDroneAttack.cs:377 | 비대상 | 에셋 1개: 10 | 0회(안 건드림) | - |
| `droneRotationOffset` | 드론 비행 — 화면 좌하단/우하단 대각선에서 날아온 | Vector3 | 읽음 WellsDroneAttack.cs:382 | 비대상 | 에셋 1개: {x: 0, y: 0, z: 0} | 0회(안 건드림) | - |
| `dashStartClip` | 드론 비행 — 화면 좌하단/우하단 대각선에서 날아온 | AnimationClip | 읽음 WellsDroneAttack.cs:298 | 비대상 | 에셋 1개: {fileID: 7866669278416069025,  | 0회(안 건드림) | - |
| `dashLoopClip` | 드론 비행 — 화면 좌하단/우하단 대각선에서 날아온 | AnimationClip | 읽음 WellsDroneAttack.cs:298 | 비대상 | 에셋 1개: {fileID: -2281602620767728765, | 0회(안 건드림) | - |
| `dropHeight` | 드론 비행 — 화면 좌하단/우하단 대각선에서 날아온 | float | 읽음 WellsDroneAttack.cs:377 | 비대상 | 에셋 1개: 8 | 1회 / 1 / 2026-10-02 | - |
| `fallTime` | 드론 비행 — 화면 좌하단/우하단 대각선에서 날아온 | float | 읽음 WellsDroneAttack.cs:456 | 비대상 | 에셋 1개: 1 | 1회 / 1 / 2026-10-02 | - |
| `approachSlowTimePortion` | 드론 비행 — 화면 좌하단/우하단 대각선에서 날아온 | float | 읽음 WellsDroneAttack.cs:458 | 비대상 | 에셋 1개: 0.5 | 0회(안 건드림) | - |
| `approachSlowDistancePortion` | 드론 비행 — 화면 좌하단/우하단 대각선에서 날아온 | float | 읽음 WellsDroneAttack.cs:458 | 비대상 | 에셋 1개: 0.15 | 0회(안 건드림) | - |
| `explosionDuration` | 드론 비행 — 화면 좌하단/우하단 대각선에서 날아온 | float | 읽음 WellsDroneAttack.cs:166 | 비대상 | 에셋 1개: 0.5 | 0회(안 건드림) | - |
| `crosshairSize` | 크로스헤어 (추적 단계) | float | 읽음 WellsDroneAttack.cs:490 | 비대상 | 에셋 1개: 2.4 | 0회(안 건드림) | - |
| `crosshairStartScale` | 크로스헤어 (추적 단계) | float | 읽음 WellsDroneAttack.cs:490 | 비대상 | 에셋 1개: 1.5 | 0회(안 건드림) | - |
| `crosshairEndScale` | 크로스헤어 (추적 단계) | float | 읽음 WellsDroneAttack.cs:490 | 비대상 | 에셋 1개: 0.8 | 0회(안 건드림) | - |
| `crosshairShrinkPortion` | 크로스헤어 (추적 단계) | float | 읽음 WellsDroneAttack.cs:483 | 비대상 | 에셋 1개: 0.8 | 0회(안 건드림) | - |
| `crosshairReferenceDistance` | 크로스헤어 (추적 단계) | float | 읽음 WellsDroneAttack.cs:493 | 비대상 | 에셋 1개: 20 | 0회(안 건드림) | - |
| `crosshairTowardCamera` | 크로스헤어 (추적 단계) | float | 읽음 WellsDroneAttack.cs:480 | 비대상 | 에셋 1개: 1.5 | 0회(안 건드림) | - |
| `crosshairTexture` | 크로스헤어 (추적 단계) | Texture2D | 읽음 WellsDroneAttack.cs:345 | 비대상 | 에셋 1개: {fileID: 2800000, guid: 1dcefd | 0회(안 건드림) | - |
| `crosshairStartColor` | 크로스헤어 (추적 단계) | Color | 읽음 WellsDroneAttack.cs:343 | 비대상 | 에셋 1개: {r: 0.15, g: 1, b: 0.2, a: 0.9 | 0회(안 건드림) | - |
| `crosshairMidColor` | 크로스헤어 (추적 단계) | Color | 읽음 WellsDroneAttack.cs:487 | 비대상 | 에셋 1개: {r: 1, g: 0.55, b: 0.05, a: 0. | 0회(안 건드림) | - |
| `crosshairColor` | 크로스헤어 (추적 단계) | Color | 읽음 WellsDroneAttack.cs:486 | 비대상 | 에셋 1개: {r: 1, g: 0.2, b: 0.15, a: 0.9 | 0회(안 건드림) | - |
| `crosshairMidColorAt` | 크로스헤어 (추적 단계) | float | 읽음 WellsDroneAttack.cs:487 | 비대상 | 에셋 1개: 0.5 | 0회(안 건드림) | - |
| `circleOuterColor` | ⚠️ 미사용 — 색은 TelegraphStyle(c | Color | **죽은 칸** — Obsolete [Tooltip/Obsolete 표시] | 비대상 | 에셋 1개: {r: 1, g: 0.15, b: 0.1, a: 0.3 | 0회(안 건드림) | - |
| `circleFillColor` | ⚠️ 미사용 — 색은 TelegraphStyle(c | Color | **죽은 칸** — Obsolete [Tooltip/Obsolete 표시] | 비대상 | 에셋 1개: {r: 1, g: 0.05, b: 0.02, a: 0. | 0회(안 건드림) | - |

### TelegraphStyleSO  (`Assets/1.Scripts/Monster/TelegraphStyleSO.cs`)

| 칸 | Header | 타입 | 코드 읽기 | xlsx | 에셋 값 | 에셋(git) 값 변경: 커밋 수 / tune·팀장 / 마지막 | xlsx(SVN) 값 변경 리비전 수 · xlsx≠에셋 칸 수 |
|---|---|---|---|---|---|---|---|
| `attack` | 공격 예고 (부채꼴·경로 띠) — 23호·건틀렛 펀 | TelegraphDecalStyle | 읽음 TelegraphStyleSO.cs:140 | 비대상 | 에셋 1개: 13개 값, 원소마다 다름 | 0회(안 건드림) | - |
| `interrupt` | 인터럽트 예고 — 간파 가능한 공격(23호 돌진·스 | TelegraphDecalStyle | 읽음 TelegraphStyleSO.cs:140 | 비대상 | 에셋 1개: 13개 값, 원소마다 다름 | 0회(안 건드림) | - |
| `circle` | 원형 판 — 웰즈 드론 착탄 원 | TelegraphPlateStyle | 읽음 WellsDroneAttack.cs:430 | 비대상 | 에셋 1개: 13개 값, 원소마다 다름 | 0회(안 건드림) | - |
| `floorTile` | 사각 판 — 23호 전기 장판 칸 | TelegraphPlateStyle | 읽음 BossElectricFloor.cs:360 | 비대상 | 에셋 1개: 13개 값, 원소마다 다름 | 0회(안 건드림) | - |

### TelegraphDecalStyle  (`Assets/1.Scripts/Monster/TelegraphStyleSO.cs`)

| 칸 | Header | 타입 | 코드 읽기 | xlsx | 에셋 값 | 에셋(git) 값 변경: 커밋 수 / tune·팀장 / 마지막 | xlsx(SVN) 값 변경 리비전 수 · xlsx≠에셋 칸 수 |
|---|---|---|---|---|---|---|---|
| `outline` |  | TelegraphLayer | 읽음 BossAttackConeTelegraph.cs:388 | 비대상 | 에셋 1개: 24개 값, 원소마다 다름 | 0회(안 건드림) | - |
| `outlineWidth` |  | float | 읽음 BossAttackConeTelegraph.cs:161 | 비대상 | 에셋 1개: 2개 값, 원소 전부 같음 | 0회(안 건드림) | - |
| `fill` |  | TelegraphLayer | 읽음 BossAttackConeTelegraph.cs:388 | 비대상 | 에셋 1개: 24개 값, 원소마다 다름 | 0회(안 건드림) | - |

### TelegraphPlateStyle  (`Assets/1.Scripts/Monster/TelegraphStyleSO.cs`)

| 칸 | Header | 타입 | 코드 읽기 | xlsx | 에셋 값 | 에셋(git) 값 변경: 커밋 수 / tune·팀장 / 마지막 | xlsx(SVN) 값 변경 리비전 수 · xlsx≠에셋 칸 수 |
|---|---|---|---|---|---|---|---|
| `outline` |  | TelegraphLayer | 읽음 MonsterSkyStrike.cs:230 | 비대상 | 에셋 1개: 24개 값, 원소마다 다름 | 0회(안 건드림) | - |
| `fill` |  | TelegraphLayer | 읽음 MonsterSkyStrike.cs:230 | 비대상 | 에셋 1개: 24개 값, 원소마다 다름 | 0회(안 건드림) | - |
| `rimRatio` |  | float | 읽음 BossElectricFloor.cs:361 | 비대상 | 에셋 1개: 2개 값, 원소마다 다름 | 0회(안 건드림) | - |

### TelegraphLayer  (`Assets/1.Scripts/Monster/TelegraphStyleSO.cs`)

| 칸 | Header | 타입 | 코드 읽기 | xlsx | 에셋 값 | 에셋(git) 값 변경: 커밋 수 / tune·팀장 / 마지막 | xlsx(SVN) 값 변경 리비전 수 · xlsx≠에셋 칸 수 |
|---|---|---|---|---|---|---|---|
| `color` |  | Color | 읽음 BossPatternVisuals.cs(PaintStyled) | 비대상 | 에셋 1개: 8개 값, 원소마다 다름 | 0회(안 건드림) | - |
| `mask` |  | TelegraphMask | 읽음 BossPatternVisuals.cs:107 | 비대상 | 에셋 1개: 40개 값, 원소마다 다름 | 0회(안 건드림) | - |

### TelegraphMask  (`Assets/1.Scripts/Monster/TelegraphStyleSO.cs`)

| 칸 | Header | 타입 | 코드 읽기 | xlsx | 에셋 값 | 에셋(git) 값 변경: 커밋 수 / tune·팀장 / 마지막 | xlsx(SVN) 값 변경 리비전 수 · xlsx≠에셋 칸 수 |
|---|---|---|---|---|---|---|---|
| `texture` |  | Texture2D | 읽음 BossPatternVisuals.cs:110~113 | 비대상 | 에셋 1개: 8개 값, 원소 전부 같음 | 0회(안 건드림) | - |
| `mode` |  | TelegraphMaskMode | 읽음 BossPatternVisuals.cs:110~113 | 비대상 | 에셋 1개: 8개 값, 원소 전부 같음 | 0회(안 건드림) | - |
| `tileSize` |  | float | 읽음 BossPatternVisuals.cs:110~113 | 비대상 | 에셋 1개: 8개 값, 원소 전부 같음 | 0회(안 건드림) | - |
| `strength` |  | float | 읽음 BossPatternVisuals.cs:110~113 | 비대상 | 에셋 1개: 8개 값, 원소 전부 같음 | 0회(안 건드림) | - |
| `rotation` |  | float | 읽음 BossPatternVisuals.cs:110~113 | 비대상 | 에셋 1개: 8개 값, 원소 전부 같음 | 0회(안 건드림) | - |

### GauntletBot  (`Assets/1.Scripts/Monster/Boss/GauntletBot.cs`)

| 칸 | Header | 타입 | 코드 읽기 | xlsx | 에셋 값 | 에셋(git) 값 변경: 커밋 수 / tune·팀장 / 마지막 | xlsx(SVN) 값 변경 리비전 수 · xlsx≠에셋 칸 수 |
|---|---|---|---|---|---|---|---|
| `punch01Weight` | 공격 선택 — 가중치 룰렛 (러프값, Play로 튜 | float | 읽음 GauntletBot.cs:451 | 대상 · 행 1 | 에셋 1개: 35 | 0회(안 건드림) | 0 |
| `punch02Weight` | 공격 선택 — 가중치 룰렛 (러프값, Play로 튜 | float | 읽음 GauntletBot.cs:451 | 대상 · 행 1 | 에셋 1개: 35 | 0회(안 건드림) | 0 |
| `punch03Weight` | 공격 선택 — 가중치 룰렛 (러프값, Play로 튜 | float | 읽음 GauntletBot.cs:451 | 대상 · 행 1 | 에셋 1개: 10 | 0회(안 건드림) | 0 |
| `smashWeightSingle` | 스매시 — 근접 플레이어 수에 따른 가중치 | float | 읽음 GauntletBot.cs:449 | 대상 · 행 1 | 에셋 1개: 20 | 0회(안 건드림) | 0 |
| `smashWeightDouble` | 스매시 — 근접 플레이어 수에 따른 가중치 | float | 읽음 GauntletBot.cs:448 | 대상 · 행 1 | 에셋 1개: 40 | 0회(안 건드림) | 0 |
| `smashWeightTriplePlus` | 스매시 — 근접 플레이어 수에 따른 가중치 | float | 읽음 GauntletBot.cs:447 | 대상 · 행 1 | 에셋 1개: 60 | 0회(안 건드림) | 0 |
| `smashCountRadius` | 스매시 — 근접 플레이어 수에 따른 가중치 | float | 읽음 GauntletBot.cs:473 | 대상 · 행 1 | 에셋 1개: 4 | 0회(안 건드림) | 0 |
| `maxNearbyResults` | 스매시 — 근접 플레이어 수에 따른 가중치 | int | 읽음 GauntletBot.cs:470 | 제외(DataTableIgnore) | 에셋 1개: 8 | 0회(안 건드림) | - |
| `smashRadius` | 스매시 — AoE 데미지 / 텔레그래프 | float | 읽음 GauntletBot.cs:171 | 대상 · 행 1 | 에셋 1개: 3 | 0회(안 건드림) | 0 |
| `smashDamageMultiplier` | 스매시 — AoE 데미지 / 텔레그래프 | float | 읽음 GauntletBot.cs:498 | 대상 · 행 1 | 에셋 1개: 1.5 | 0회(안 건드림) | 0 |
| `smashMaxHitCount` | 스매시 — AoE 데미지 / 텔레그래프 | int | 읽음 GauntletBot.cs:493 | 제외(DataTableIgnore) | 에셋 1개: 8 | 0회(안 건드림) | - |
| `smashTelegraph` | 스매시 — AoE 데미지 / 텔레그래프 | EffectSocketPlayer | 읽음 GauntletBot.cs:83 | 대상 · **행 없음** | 에셋 1개: {fileID: 2123847881958745088} | 1회 / 0 / 2026-09-08 | 0 |
| `punchTelegraphDuration` | 펀치 예고 (2026-09-28 팀장: 범위를 보여 | float | 읽음 GauntletBot.cs:149 | 대상 · 행 1 | 에셋 1개: 0.5 | 0회(안 건드림) | 0 |
| `sideLateralOffset` | 펀치 예고 (2026-09-28 팀장: 범위를 보여 | float | 읽음 GauntletBot.cs:372 | 제외(DataTableIgnore) | 에셋 1개: 0.95 | 0회(안 건드림) | - |
| `smashAnticipationState` | 애니메이션 — CrossFade 대상 상태명(컨트롤 | string | 읽음 GauntletBot.cs:551 | 대상 · **행 없음** | 에셋 1개: Smash Anticipation | 0회(안 건드림) | 0 |
| `punch01LState` | 애니메이션 — CrossFade 대상 상태명(컨트롤 | string | 읽음 GauntletBot.cs:552 | 대상 · **행 없음** | 에셋 1개: Gauntlet_Punch01_L | 0회(안 건드림) | 0 |
| `punch01RState` | 애니메이션 — CrossFade 대상 상태명(컨트롤 | string | 읽음 GauntletBot.cs:553 | 대상 · **행 없음** | 에셋 1개: Gauntlet_Punch01_R | 0회(안 건드림) | 0 |
| `punch02LState` | 애니메이션 — CrossFade 대상 상태명(컨트롤 | string | 읽음 GauntletBot.cs:554 | 대상 · **행 없음** | 에셋 1개: Gauntlet_Punch02_L | 0회(안 건드림) | 0 |
| `punch02RState` | 애니메이션 — CrossFade 대상 상태명(컨트롤 | string | 읽음 GauntletBot.cs:555 | 대상 · **행 없음** | 에셋 1개: Gauntlet_Punch02_R | 0회(안 건드림) | 0 |
| `punch03LState` | 애니메이션 — CrossFade 대상 상태명(컨트롤 | string | 읽음 GauntletBot.cs:556 | 대상 · **행 없음** | 에셋 1개: Gauntlet_Punch03_L | 0회(안 건드림) | 0 |
| `punch03RState` | 애니메이션 — CrossFade 대상 상태명(컨트롤 | string | 읽음 GauntletBot.cs:557 | 대상 · **행 없음** | 에셋 1개: Gauntlet_Punch03_R | 0회(안 건드림) | 0 |
| `interruptFlash` | VFX | EffectSocketPlayer | 읽음 GauntletBot.cs:295 | 대상 · **행 없음** | 에셋 1개: {fileID: 4960452879373660667} | 0회(안 건드림) | 0 |
| `spark_L` | VFX | EffectSocketPlayer | 읽음 GauntletBot.cs:360 | 대상 · **행 없음** | 에셋 1개: {fileID: 3603264997813650131} | 0회(안 건드림) | 0 |
| `spark_R` | VFX | EffectSocketPlayer | 읽음 GauntletBot.cs:360 | 대상 · **행 없음** | 에셋 1개: {fileID: 7117946031536760415} | 0회(안 건드림) | 0 |

### SpinnerBot  (`Assets/1.Scripts/Monster/Boss/SpinnerBot.cs`)

| 칸 | Header | 타입 | 코드 읽기 | xlsx | 에셋 값 | 에셋(git) 값 변경: 커밋 수 / tune·팀장 / 마지막 | xlsx(SVN) 값 변경 리비전 수 · xlsx≠에셋 칸 수 |
|---|---|---|---|---|---|---|---|
| `spinChance` | 스핀 — 선택 | float | 읽음 SpinnerBot.cs:172 | 대상 · 행 1 | 에셋 1개: 0.4 | 0회(안 건드림) | 0 |
| `spinCooldown` | 스핀 — 선택 | float | 읽음 SpinnerBot.cs:127 | 대상 · 행 1 | 에셋 1개: 6 | 0회(안 건드림) | 0 |
| `spinWindup` | 스핀 — 타이밍/이동 (플레이테스트 튜닝) | float | 읽음 SpinnerBot.cs:158 | 대상 · 행 1 | 에셋 1개: 1 | 0회(안 건드림) | 0 |
| `dashDuration` | 스핀 — 타이밍/이동 (플레이테스트 튜닝) | float | 읽음 SpinnerBot.cs:159 | 대상 · 행 1 | 에셋 1개: 1.2 | 0회(안 건드림) | 0 |
| `dashRepeatInterval` | 스핀 — 타이밍/이동 (플레이테스트 튜닝) | float | 읽음 SpinnerBot.cs:160 | 대상 · 행 1 | 에셋 1개: 0.5 | 0회(안 건드림) | 0 |
| `dashSpeedMultiplier` | 스핀 — 타이밍/이동 (플레이테스트 튜닝) | float | 읽음 SpinnerBot.cs:225 | 대상 · 행 1 | 에셋 1개: 15 | 0회(안 건드림) | 0 |
| `dashMaxDistance` | 스핀 — 타이밍/이동 (플레이테스트 튜닝) | float | 읽음 SpinnerBot.cs:442 | 대상 · 행 1 | 에셋 1개: 40 | 0회(안 건드림) | 0 |
| `dashHalfWidth` | 스핀 — 타이밍/이동 (플레이테스트 튜닝) | float | 읽음 SpinnerBot.cs:231 | 제외(DataTableIgnore) | 에셋에 없음(코드 기본값) | 0회(안 건드림) | - |
| `whipTelegraphDuration` | 예고 (2026-09-28 팀장: 범위를 보여 준  | float | 읽음 SpinnerBot.cs:182 | 대상 · 행 1 | 에셋 1개: 0.5 | 0회(안 건드림) | 0 |
| `sideLateralOffset` | 예고 (2026-09-28 팀장: 범위를 보여 준  | float | 읽음 SpinnerBot.cs:98 | 제외(DataTableIgnore) | 에셋 1개: 0.65 | 0회(안 건드림) | - |
| `spinStartState` | 애니 상태 (컨트롤러 일치) | string | 읽음 SpinnerBot.cs:520 | 대상 · **행 없음** | 에셋 1개: Spin Attack Start | 0회(안 건드림) | 0 |
| `spinLoopState` | 애니 상태 (컨트롤러 일치) | string | 읽음 SpinnerBot.cs:527 | 대상 · **행 없음** | 에셋 1개: Spin Attack Loop | 0회(안 건드림) | 0 |
| `whipStateR` | 애니 상태 (컨트롤러 일치) | string | 읽음 SpinnerBot.cs:534 | 대상 · **행 없음** | 에셋 1개: Attack Whip R Start | 0회(안 건드림) | 0 |
| `whipStateL` | 애니 상태 (컨트롤러 일치) | string | 읽음 SpinnerBot.cs:534 | 대상 · **행 없음** | 에셋 1개: Attack Whip L Start | 0회(안 건드림) | 0 |
| `spinEffectId` | 애니 상태 (컨트롤러 일치) | string | 읽음 SpinnerBot.cs:476 | 대상 · **행 없음** | 에셋 1개: Spin | 0회(안 건드림) | 0 |
| `interruptFlash` | VFX | EffectSocketPlayer | 읽음 SpinnerBot.cs:397 | 대상 · **행 없음** | 에셋 1개: {fileID: 7148632310401938588} | 0회(안 건드림) | 0 |

### WallBot  (`Assets/1.Scripts/Monster/Boss/WallBot.cs`)

| 칸 | Header | 타입 | 코드 읽기 | xlsx | 에셋 값 | 에셋(git) 값 변경: 커밋 수 / tune·팀장 / 마지막 | xlsx(SVN) 값 변경 리비전 수 · xlsx≠에셋 칸 수 |
|---|---|---|---|---|---|---|---|
| `shieldChance` | 방패 3단 — 선택 | float | 읽음 WallBot.cs:131 | 대상 · 행 1 | 에셋 1개: 0.5 | 0회(안 건드림) | 0 |
| `shieldCooldown` | 방패 3단 — 선택 | float | 읽음 WallBot.cs:118 | 대상 · 행 1 | 에셋 1개: 8 | 0회(안 건드림) | 0 |
| `dashDuration` | 돌진 (실패 시) | float | 읽음 WallBot.cs:164 | 대상 · 행 1 | 에셋 1개: 0.8 | 0회(안 건드림) | 0 |
| `dashSpeedMultiplier` | 돌진 (실패 시) | float | 읽음 WallBot.cs:255 | 대상 · 행 1 | 에셋 1개: 6 | 0회(안 건드림) | 0 |
| `dashMaxDistance` | 돌진 (실패 시) | float | 읽음 WallBot.cs:250 | 대상 · 행 1 | 에셋 1개: 8 | 0회(안 건드림) | 0 |
| `dashRepeatInterval` | 돌진 (실패 시) | float | 읽음 WallBot.cs:205 | 대상 · 행 1 | 에셋 1개: 0.5 | 0회(안 건드림) | 0 |
| `dashColliderInfo` | 돌진 (실패 시) | ColliderInfo | 읽음 WallBot.cs:64 | 대상 · **행 없음** | 에셋 1개: {fileID: 5501001001001001004} | 0회(안 건드림) | 0 |
| `shockRadius` | 착지 충격파 (돌진 종료) | float | 읽음 WallBot.cs:467 | 대상 · 행 1 | 에셋 1개: 3 | 0회(안 건드림) | 0 |
| `shockDamageMultiplier` | 착지 충격파 (돌진 종료) | float | 읽음 WallBot.cs:469 | 대상 · 행 1 | 에셋 1개: 1.2 | 0회(안 건드림) | 0 |
| `shockKnockback` | 착지 충격파 (돌진 종료) | float | 읽음 WallBot.cs:489 | 대상 · 행 1 | 에셋 1개: 6 | 0회(안 건드림) | 0 |
| `shockMaxHitCount` | 착지 충격파 (돌진 종료) | int | 읽음 WallBot.cs:463 | 제외(DataTableIgnore) | 에셋 1개: 8 | 0회(안 건드림) | - |
| `shockDuration` | 착지 충격파 (돌진 종료) | float | 읽음 WallBot.cs:164 | 대상 · 행 1 | 에셋 1개: 0.7 | 0회(안 건드림) | 0 |
| `gatherStateName` | 착지 충격파 (돌진 종료) | string | 읽음 WallBot.cs:176 | 대상 · **행 없음** | 에셋 1개: AttackStart | 0회(안 건드림) | 0 |
| `interruptFlash` | VFX | EffectSocketPlayer | 읽음 WallBot.cs:383 | 대상 · **행 없음** | 에셋 1개: {fileID: 3099268962609029083} | 0회(안 건드림) | 0 |
| `debugLog` | 진단 | bool | 읽음 WallBot.cs:501 | 대상 · **행 없음** | 에셋 1개: 0 | 0회(안 건드림) | 0 |