# 핸드오프 — 적 공격 범위표시 C3 패키지 (경석 담당)

> 2026-10-19 · 브랜치 `feature/SkillIndicatorC3` · 작성: Claude(claude-alt).
> 디자인 쪽에서 만든 **적 공격 범위표시 C3 시험 구현**을 몬스터 담당(경석)에게 넘기는 요약이다.
> **이번 레인에서는 몬스터 스크립트를 고치지 않았다** — 스킬(플레이어) 범위표시만 적용했고, 적 쪽은 이 문서로만 전달한다.
> 패키지 원본은 사용자가 별도로 전달한다. 상세(경로별 주의·되돌리기·웹 편집기 사용법)는 패키지 README 가 원본이다.

## 1. 한 줄 요약

피해 범위·시간·서버 권한·RPC·쿨다운·애니 이벤트는 **그대로**, 바닥 예고의 **외형(색·이중 경계·쐐기 텍스처)만** 바꾸는 시험 구현.
정본 = 팔레트 3개(일반/간파/발동) + 개별 override 8개(01·02·04·05·08·09·10·11) + 연결된 표시 15개.

## 2. 대상 15종

| ID | 표시 | 쐐기(최종) | 적용 경로 |
|---|---|---|---|
| 01 | 23호 좌·우 훅 사각 예고 | 끔 | BossAttackConeTelegraph |
| 02 | 23호 어퍼 경로 + 끝 원 | 끔 (켜면 직선 구간만) | BossAttackConeTelegraph 합집합 |
| 03 | 23호 잡기 · 안쪽으로 채우는 부채꼴 | 없음 | BossAttackConeTelegraph 부채꼴 |
| 04 | 23호 일반 돌진 · 경로 띠 | 켬 · 크기100/간격75/시작69 | BossAttackConeTelegraph |
| 05 | 23호 레이지 돌진 · 즉시 채운 띠 | 켬 · 크기100/간격75/시작74 | BossAttackConeTelegraph |
| 06 | 23호 점프 · 고정 착지 경계 원 | 4개 · 앵커 81.4% | TwentyThreeBoss RPC → EnemyC3JumpView |
| 07 | 23호 점프 · 차오르는 착지 원 | 성장층(경계층 06 쐐기 유지) | 같은 점프 뷰 성장층 |
| 08 | 건틀렛 펀치 · 좌우 사각 예고 | 끔 | BossAttackConeTelegraph |
| 09 | 건틀렛 스매시 · VFX 원 | 4개 · 앵커 81% · 외곽선 불투명도 1 | GauntletBot → 재생 SpriteRenderer 의 sprite 교체 |
| 10 | 스피너 채찍 · 사각 예고 | 끔 | BossAttackConeTelegraph |
| 11 | 스피너 돌진 · 간파 경로 사각 | 켬 · 크기100/간격75/시작69 | BossAttackConeTelegraph |
| 12 | 박격포 · 고정 착탄 원 | 4개 · 앵커 81.4% | MonsterSkyStrike 바닥 판 |
| 14 | 웰즈 드론 · 잠긴 착탄 원 | 4개 · 앵커 81.4% | WellsDroneAttack |
| 15 | 23호 전기 장판 · 경고 칸 | 없음 | BossElectricFloor 경고/성장 판 |
| 16 | 23호 전기 장판 · 발동 칸 | 없음 | BossElectricFloor 발동 판 (기존 점멸 alpha 배율만 적용) |

ID 13 은 없다(패키지 원본 번호 그대로).

## 3. 팔레트 (`HEX / 불투명도`)

| 요소 | 일반 공격 | 간파 가능 공격 | 발동 영역 |
|---|---|---|---|
| 외곽선 | #ffffff / 0.95 | #ffffff / 0.95 | #ffc3b5 / 0.95 |
| 내부선 | #bc8178 / 1 | #b982c4 / 1 | #bb726a / 1 |
| 외곽 채움 | #ed8f82 / 0.25 | #d29ef5 / 0.25 | #e96b62 / 0.25 |
| 진행 채움 | #ed8f82 / 0.232 | #d29ef5 / 0.232 | #e96b62 / 0.232 |
| 쐐기 | #ed8f82 / 0.42 | #d29ef5 / 0.42 | #e96b62 / 0.42 |

- 개별 override 가 그룹보다 우선. 스매시 09 는 외곽선 불투명도만 1.
- **색과 간파 판정은 별개** — 붉게 바꿔도 간파 여부는 안 바뀐다.
- 공통 기하: 외곽선 3.5 · 안쪽선 2.5 · 선 간격 2 · 외곽 채움 시작 57% · 직선 쐐기 크기 100%.

## 4. 패키지가 건드리는 파일

| 구분 | 파일 | 비고 |
|---|---|---|
| 기존 수정 (patch) | `Monster/Boss/BossAttackConeTelegraph.cs` | 01~05·08·10·11 데칼 |
| | `Monster/Boss/BossElectricFloor.cs` | 15·16 |
| | `Monster/Boss/GauntletBot.cs` | 09 — **패키지 기준 이후 development 에서 변경됨** |
| | `Monster/Boss/TwentyThreeBoss.cs` | 06·07 점프 — **패키지 기준 이후 크게 변경됨(+305/−74)** |
| | `Monster/Boss/WellsDroneAttack.cs` | 14 |
| | `Monster/MonsterSkyStrike.cs` | 12 |
| 신규 | `Monster/EnemyC3Visuals.cs` | JSON 파싱·텍스처 생성·캐시·점프 뷰 |
| 신규 (Editor) | `Monster/Editor/EnemyC3Validation.cs` | `Tools > Enemy C3 > …` 검증·기존 테스트 실행 메뉴 |
| 신규 | `Assets/Resources/Telegraph/EnemyC3Selected.json` | 런타임용 15개 펼친 설정 |

- 경로는 `Assets/1.Scripts/` 기준. 신규 3개는 patch 에 없다 — meta 와 함께 따로 복사.
- 패키지 patch 기준 HEAD 는 `6ed043d8`. 그 뒤로 `GauntletBot.cs`·`TwentyThreeBoss.cs` 가 바뀌었다(2026-10-19 확인) → **`git apply` 대신 diff 를 읽고 수동 통합** 권장.
- 신규 3개 meta guid(`acdcdb11…`·`727428fd…`·`b711abdb…`)는 현재 프로젝트와 충돌 없음(2026-10-19 확인).
- prefab/scene 배선 변경 없음. 기존 Telegraph 재질·셰이더(`MA_TelegraphDecal`·`MA_TelegraphQuad`·`SG_TelegraphDecal`·`TelegraphQuad.shader`)에 그대로 의존.

## 5. 주의·미검증 (README 에서 추림)

| 항목 | 내용 |
|---|---|
| 수치 단위 | 간격75·시작69 등은 **웹 시안 좌표**. 화면 px·월드 m 아님. 기준(사각 폭200, 돌진 폭114, 어퍼 폭130, 원 반경165, 칸 한 변300)으로 실제 도형 크기에 맞춰 환산 |
| 플레이어 수치 복사 아님 | 플레이어 직선의 .96m/2m/2.6m 를 적에 그대로 쓴 것이 아니다 |
| preset 선택 방식 | 몬스터 종류·팔레트·도형으로 preset ID 를 **추론**하는 시험 방식. 공격 ID 를 명시 전달할지는 담당자 결정 |
| 스타일 소스 | C3 경로는 `TelegraphStyleSO` 의 색·마스크 대신 JSON 색을 쓴다. 기존 마스크 미적용 — 편집 소스 혼동 주의 |
| 점프 | 카탈로그 VFX(Drop_Charge_Boundary/Indicator) 대신 EnemyC3JumpView 생성. 경사면 투영 안 됨. 정식 VFX 체계와 통합할지 결정 필요 |
| 스매시 | SVN 프리팹은 안 고치고 재생된 SpriteRenderer 의 sprite 만 교체. 프리팹 구조가 바뀌면 대상 재확인 |
| 그랩 | 반경 8m/90°·바깥→안쪽 채움. 각도 밖 원형 잔선 수정본 — 거리 계산을 전체 원 반경 차이로 되돌리면 재발 |
| 프리셋 반영 | JSON 갱신 후 **Play 재시작** 필요(캐시). 실행 중 즉시 반영 없음 |
| 텍스처 캐시 | 첫 생성 프레임 지연·장시간 전투 메모리. 캐시 상한/풀링은 정식 반영 전 검토 |
| 검증 범위 | 컴파일 0 에러, 선택값 일치 검사, 기존 테스트 25개 통과(TelegraphStyle/MonsterSkyStrike/BossElectricFloorPattern). **최종 설정 15종 Play 캡처 없음, MPPM·빌드 미검증**. 동봉 PNG 는 생성 텍스처이지 Play 화면이 아니다 |
| 범위 밖 | 조준 레이저·몸 크로스헤어·부위 발광·방향 링·플레이어 스킬 표시·SVN VFX 원본 |

## 6. 이번 스킬 범위표시 교체와의 관계

| 질문 | 답 |
|---|---|
| 같은 셰이더·재질을 쓰나? | **아니오.** 스킬 쪽은 `SkillLineIndicator.shader`·`SkillLineRange.mat`·`SkillRangeIndicator.mat`(+신규 `SelectedTargetCircleC.mat`) 만 교체했다. 적 패키지는 Telegraph 재질·셰이더 + 런타임 생성 텍스처를 쓰고, 스킬 표시 파일을 참조하지 않는다 |
| 겹치는 파일이 있나? | 없음. 스킬 쪽은 `Player/Skill/Targeting/` 2개 + `Player.prefab`, 적 쪽은 `Monster/` 6개 + 신규 3개 |
| 디자인 언어 | 같은 C3 계열(흰 외곽선 + 내부선 + 쐐기). 스킬 = 민트(#c0f5de), 적 = 붉은/보라 계열 |
| 순서 의존 | 없음. 어느 쪽을 먼저 병합해도 된다 |

## 7. 통합 뒤 확인 (README 프로그래머 확인 항목)

- [ ] 호스트/클라 양쪽에서 위치·방향·최대 범위, 예고 종료/취소/사망 시 해제
- [ ] 돌진(일반/레이지/스피너) 핀 고정·최소/최대 경로, 어퍼 합집합 경계
- [ ] 그랩 90° 부채꼴만 표시·안쪽 채움
- [ ] 점프/스매시 바닥 높이·반경·페이드·그로기 종료, 박격포/드론 착탄 시간
- [ ] 전기 칸 경고→발동→점멸→해제, 타일 중첩
- [ ] 첫 텍스처 생성 지연, 장시간 캐시 메모리, 재실행/씬 전환 정리
- [ ] 철판 홈·지형 가림·다른 맵·다수 VFX 중첩·색각 차이에서 간파/일반 구분
